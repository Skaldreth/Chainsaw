using System.Security.Cryptography;
using System.Text.Json;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Security;

/// <summary>
/// The vault: keeps the data key in pinned memory while unlocked and clears it when locked, and gives the stores in
/// this module guarded access to the decrypted content.
/// </summary>
/// <remarks>
/// Two locks keep the slow key derivation away from readers. <see cref="_operationGate"/> serialises the operations
/// that read or write the file (create, unlock, change master key, save), so that they never interleave.
/// <see cref="_stateGate"/> guards the key, the content and the state in memory and is only held briefly, so that
/// <see cref="State"/>, the stores' reads and <see cref="Lock"/> never wait for PBKDF2.
/// </remarks>
internal sealed class Vault : IVault, IDisposable
{
    /// <summary>Serialises the operations that read or write the vault file.</summary>
    private readonly Lock _operationGate = new();

    /// <summary>Guards the fields below; held only for short, in-memory work and the small writes of a save.</summary>
    private readonly Lock _stateGate = new();

    /// <summary>The vault file.</summary>
    private readonly VaultFile _file;

    /// <summary>The PBKDF2 iteration count for a new or changed master key.</summary>
    private readonly int _iterations;

    /// <summary>The data key while unlocked, in a pinned array; <see langword="null"/> while locked.</summary>
    private byte[]? _dataKey;

    /// <summary>The file as last read or written while unlocked, so that its header is kept on every save.</summary>
    private VaultDocument? _document;

    /// <summary>The decrypted content while unlocked.</summary>
    private VaultContent? _content;

    /// <summary>The state, kept in a field so that reading it never touches the disk.</summary>
    private VaultState _state;

    /// <summary>Counts the locks, so that an unlock that was overtaken by a lock does not unlock afterwards.</summary>
    private long _lockGeneration;

    /// <summary>Creates the vault for the folder in the options. Nothing secret is read until it is unlocked.</summary>
    /// <param name="options">Where the file lives and the iteration count for new master keys.</param>
    public Vault(VaultOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _file = new VaultFile(options.Directory);
        _iterations = options.Iterations;
        _state = _file.Exists ? VaultState.Locked : VaultState.NotCreated;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The event is raised outside the locks, so two operations on different threads may deliver their events in
    /// another order than they happened; a handler that needs the exact state reads <see cref="State"/>.
    /// </remarks>
    public event EventHandler<VaultState>? StateChanged;

    /// <inheritdoc />
    public VaultState State
    {
        get
        {
            lock (_stateGate)
            {
                return _state;
            }
        }
    }

    /// <summary>Gets the vault file, for the tests.</summary>
    internal VaultFile File => _file;

    /// <inheritdoc />
    public void Create(ReadOnlySpan<char> masterKey)
    {
        RequireLongEnough(masterKey);
        lock (_operationGate)
        {
            if (_file.Exists)
            {
                throw new VaultException(VaultError.AlreadyCreated);
            }

            var dataKey = VaultCrypto.NewDataKey();
            try
            {
                var kdf = NewKdf();
                var document = new VaultDocument(
                    VaultDocument.FormatName,
                    VaultDocument.CurrentVersion,
                    kdf,
                    WrapDataKey(masterKey, kdf, dataKey),
                    SealContent(dataKey, VaultContent.Empty));
                _file.Write(document);
                CommitUnlocked(dataKey, document, VaultContent.Empty, Interlocked.Read(ref _lockGeneration));
            }
            catch
            {
                CryptographicOperations.ZeroMemory(dataKey);
                throw;
            }
        }
    }

    /// <inheritdoc />
    public void Unlock(ReadOnlySpan<char> masterKey)
    {
        var generation = Interlocked.Read(ref _lockGeneration);
        lock (_operationGate)
        {
            var document = _file.Read();
            var dataKey = UnwrapDataKey(masterKey, document);
            try
            {
                var content = OpenContent(dataKey, document.Content);
                document = StrengthenIfWeak(masterKey, document, dataKey);
                CommitUnlocked(dataKey, document, content, generation);
            }
            catch
            {
                CryptographicOperations.ZeroMemory(dataKey);
                throw;
            }
        }
    }

    /// <inheritdoc />
    public void Lock()
    {
        bool changed;
        lock (_stateGate)
        {
            Interlocked.Increment(ref _lockGeneration);
            ClearUnlocked();
            changed = _state == VaultState.Unlocked;
            if (changed)
            {
                _state = VaultState.Locked;
            }
        }

        if (changed)
        {
            StateChanged?.Invoke(this, VaultState.Locked);
        }
    }

    /// <inheritdoc />
    public void ChangeMasterKey(ReadOnlySpan<char> currentMasterKey, ReadOnlySpan<char> newMasterKey)
    {
        RequireLongEnough(newMasterKey);
        lock (_operationGate)
        {
            var document = _file.Read();
            var dataKey = UnwrapDataKey(currentMasterKey, document);
            try
            {
                RequireSameKeyAsUnlocked(dataKey);
                var kdf = NewKdf();
                var changed = document with { Kdf = kdf, DataKey = WrapDataKey(newMasterKey, kdf, dataKey) };
                _file.Write(changed);
                lock (_stateGate)
                {
                    if (_document is not null)
                    {
                        _document = _document with { Kdf = changed.Kdf, DataKey = changed.DataKey };
                    }
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(dataKey);
            }
        }
    }

    /// <summary>Locks the vault, so that the data key is cleared when the application shuts down.</summary>
    public void Dispose() => Lock();

    /// <summary>Reads from the decrypted content without waiting for a key derivation.</summary>
    /// <typeparam name="T">What is read.</typeparam>
    /// <param name="read">Picks what is needed from the content.</param>
    /// <returns>What <paramref name="read"/> returned.</returns>
    /// <exception cref="VaultException"><see cref="VaultError.Locked"/> when the vault is not unlocked.</exception>
    internal T Read<T>(Func<VaultContent, T> read)
    {
        lock (_stateGate)
        {
            return read(_content ?? throw new VaultException(VaultError.Locked));
        }
    }

    /// <summary>
    /// Changes the content and writes the vault file. The change is kept in memory only after the file is written, so a
    /// failed write leaves both as they were.
    /// </summary>
    /// <param name="change">Gives the new content, or the same instance when nothing changes.</param>
    /// <exception cref="VaultException"><see cref="VaultError.Locked"/> when the vault is not unlocked.</exception>
    internal void Update(Func<VaultContent, VaultContent> change)
    {
        lock (_operationGate)
        {
            lock (_stateGate)
            {
                if (_dataKey is null || _document is null || _content is null)
                {
                    throw new VaultException(VaultError.Locked);
                }

                var content = change(_content);
                if (ReferenceEquals(content, _content))
                {
                    return;
                }

                var document = _document with { Content = SealContent(_dataKey, content) };
                _file.Write(document);
                _document = document;
                _content = content;
            }
        }
    }

    /// <summary>Derives a key for a purpose from the data key.</summary>
    /// <param name="purpose">The validated purpose name.</param>
    /// <param name="destination">Receives the key.</param>
    /// <exception cref="VaultException"><see cref="VaultError.Locked"/> when the vault is not unlocked.</exception>
    internal void DerivePurposeKey(string purpose, Span<byte> destination)
    {
        lock (_stateGate)
        {
            VaultCrypto.DerivePurposeKey(
                _dataKey ?? throw new VaultException(VaultError.Locked), purpose, destination);
        }
    }

    /// <summary>Refuses a master key that is too short.</summary>
    /// <param name="masterKey">The proposed master key.</param>
    /// <exception cref="VaultException"><see cref="VaultError.MasterKeyTooShort"/>.</exception>
    private static void RequireLongEnough(ReadOnlySpan<char> masterKey)
    {
        if (!MasterKeyRules.IsLongEnough(masterKey))
        {
            throw new VaultException(VaultError.MasterKeyTooShort);
        }
    }

    /// <summary>Encrypts the data key with the key derived from a master key.</summary>
    /// <param name="masterKey">The master key.</param>
    /// <param name="kdf">The derivation parameters, with a new salt.</param>
    /// <param name="dataKey">The data key.</param>
    /// <returns>The wrapped data key.</returns>
    private static SealedBox WrapDataKey(ReadOnlySpan<char> masterKey, KdfParameters kdf, ReadOnlySpan<byte> dataKey)
    {
        Span<byte> wrappingKey = stackalloc byte[VaultCrypto.KeySize];
        try
        {
            VaultCrypto.DeriveWrappingKey(masterKey, kdf.Salt, kdf.Iterations, wrappingKey);
            return VaultCrypto.Seal(wrappingKey, dataKey, VaultDocument.DataKeyAssociatedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    /// <summary>Decrypts the data key with the key derived from a master key.</summary>
    /// <param name="masterKey">The master key to try.</param>
    /// <param name="document">The checked vault file.</param>
    /// <returns>The data key in a pinned array that the caller owns and clears.</returns>
    /// <exception cref="VaultException">
    /// <see cref="VaultError.WrongMasterKey"/>. AES-GCM cannot tell a wrong key from a changed wrapped key, so a
    /// tampered file is reported the same way.
    /// </exception>
    private static byte[] UnwrapDataKey(ReadOnlySpan<char> masterKey, VaultDocument document)
    {
        Span<byte> wrappingKey = stackalloc byte[VaultCrypto.KeySize];
        var dataKey = VaultCrypto.AllocateSecret(VaultCrypto.KeySize);
        try
        {
            VaultCrypto.DeriveWrappingKey(masterKey, document.Kdf.Salt, document.Kdf.Iterations, wrappingKey);
            VaultCrypto.Open(wrappingKey, document.DataKey, VaultDocument.DataKeyAssociatedData, dataKey);
            return dataKey;
        }
        catch (AuthenticationTagMismatchException exception)
        {
            CryptographicOperations.ZeroMemory(dataKey);
            throw new VaultException(VaultError.WrongMasterKey, exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    /// <summary>Serialises and encrypts the content; the plaintext buffer is cleared afterwards.</summary>
    /// <param name="dataKey">The data key, from which the content key is derived.</param>
    /// <param name="content">The content.</param>
    /// <returns>The encrypted content under a fresh nonce.</returns>
    private static SealedBox SealContent(ReadOnlySpan<byte> dataKey, VaultContent content)
    {
        Span<byte> contentKey = stackalloc byte[VaultCrypto.KeySize];
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(content);
        try
        {
            VaultCrypto.DeriveContentKey(dataKey, contentKey);
            return VaultCrypto.Seal(contentKey, plaintext, VaultDocument.ContentAssociatedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(contentKey);
        }
    }

    /// <summary>Decrypts and deserialises the content; the plaintext buffer is cleared afterwards.</summary>
    /// <param name="dataKey">The data key, from which the content key is derived.</param>
    /// <param name="box">The encrypted content.</param>
    /// <returns>The content.</returns>
    /// <exception cref="VaultException">
    /// <see cref="VaultError.Corrupt"/>: the data key opened but the content did not, so the file has been changed.
    /// </exception>
    private static VaultContent OpenContent(ReadOnlySpan<byte> dataKey, SealedBox box)
    {
        Span<byte> contentKey = stackalloc byte[VaultCrypto.KeySize];
        var plaintext = VaultCrypto.AllocateSecret(box.Ciphertext.Length);
        try
        {
            VaultCrypto.DeriveContentKey(dataKey, contentKey);
            VaultCrypto.Open(contentKey, box, VaultDocument.ContentAssociatedData, plaintext);
            var content = JsonSerializer.Deserialize<VaultContent>(plaintext);
            return content is { ApiKeys: not null } ? content : throw new VaultException(VaultError.Corrupt);
        }
        catch (Exception exception) when (exception is AuthenticationTagMismatchException or JsonException)
        {
            throw new VaultException(VaultError.Corrupt, exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(contentKey);
        }
    }

    /// <summary>Gives new derivation parameters with a fresh salt and the configured iteration count.</summary>
    /// <returns>The parameters.</returns>
    private KdfParameters NewKdf() => new(KdfParameters.AlgorithmName, _iterations, VaultCrypto.NewSalt());

    /// <summary>
    /// Wraps the data key again with the configured iteration count when the file was written with fewer, so that a
    /// raised default reaches old vaults. A failed write keeps the old file, which still opens.
    /// </summary>
    /// <param name="masterKey">The master key that just opened the vault.</param>
    /// <param name="document">The vault file as read.</param>
    /// <param name="dataKey">The data key.</param>
    /// <returns>The document as it now is on disk.</returns>
    private VaultDocument StrengthenIfWeak(ReadOnlySpan<char> masterKey, VaultDocument document, byte[] dataKey)
    {
        if (document.Kdf.Iterations >= _iterations)
        {
            return document;
        }

        var kdf = NewKdf();
        var strengthened = document with { Kdf = kdf, DataKey = WrapDataKey(masterKey, kdf, dataKey) };
        try
        {
            _file.Write(strengthened);
            return strengthened;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return document;
        }
    }

    /// <summary>
    /// Refuses to wrap a data key from disk that differs from the one in memory, which happens when the file was
    /// replaced while the vault was unlocked; wrapping it would make later saves unreadable.
    /// </summary>
    /// <param name="dataKey">The data key unwrapped from the file.</param>
    /// <exception cref="VaultException"><see cref="VaultError.Corrupt"/> when the keys differ.</exception>
    private void RequireSameKeyAsUnlocked(byte[] dataKey)
    {
        lock (_stateGate)
        {
            if (_dataKey is not null && !CryptographicOperations.FixedTimeEquals(_dataKey, dataKey))
            {
                throw new VaultException(VaultError.Corrupt);
            }
        }
    }

    /// <summary>
    /// Takes ownership of a newly opened data key and its content, clearing any key held before, and raises the event.
    /// An unlock that a <see cref="Lock"/> overtook is refused, so that locking always wins.
    /// </summary>
    /// <param name="dataKey">The data key in a pinned array; cleared by the caller if this throws.</param>
    /// <param name="document">The vault file.</param>
    /// <param name="content">The decrypted content.</param>
    /// <param name="generation">The lock generation when the operation started.</param>
    /// <exception cref="VaultException"><see cref="VaultError.Locked"/>: the vault was locked meanwhile.</exception>
    private void CommitUnlocked(byte[] dataKey, VaultDocument document, VaultContent content, long generation)
    {
        bool changed;
        lock (_stateGate)
        {
            if (Interlocked.Read(ref _lockGeneration) != generation)
            {
                // A vault that was created meanwhile exists now, even though it stays locked.
                _state = VaultState.Locked;
                throw new VaultException(VaultError.Locked);
            }

            ClearUnlocked();
            _dataKey = dataKey;
            _document = document;
            _content = content;
            changed = _state != VaultState.Unlocked;
            _state = VaultState.Unlocked;
        }

        if (changed)
        {
            StateChanged?.Invoke(this, VaultState.Unlocked);
        }
    }

    /// <summary>
    /// Clears the data key and drops the content; the caller holds the state gate. The strings in the content cannot
    /// be cleared (a .NET string is immutable); dropping them lets the garbage collector reuse their memory.
    /// </summary>
    private void ClearUnlocked()
    {
        if (_dataKey is not null)
        {
            CryptographicOperations.ZeroMemory(_dataKey);
        }

        _dataKey = null;
        _document = null;
        _content = null;
    }
}
