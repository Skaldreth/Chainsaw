using System.Security.Cryptography;
using System.Text.Json;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Security;

/// <summary>
/// The vault: keeps the data key in pinned memory while unlocked and clears it when locked, and gives the stores in
/// this module guarded access to the decrypted content.
/// </summary>
internal sealed class Vault : IVault, IDisposable
{
    /// <summary>Serialises every operation, so that the key, the content and the file always change together.</summary>
    private readonly Lock _gate = new();

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

    /// <summary>Creates the vault for the folder in the options. Nothing is read until it is unlocked.</summary>
    /// <param name="options">Where the file lives and the iteration count for new master keys.</param>
    public Vault(VaultOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _file = new VaultFile(options.Directory);
        _iterations = options.Iterations;
    }

    /// <inheritdoc />
    public event EventHandler<VaultState>? StateChanged;

    /// <inheritdoc />
    public VaultState State
    {
        get
        {
            lock (_gate)
            {
                return CurrentState();
            }
        }
    }

    /// <summary>Gets the vault file, for the tests.</summary>
    internal VaultFile File => _file;

    /// <summary>Gets the array that holds the data key, so that the tests can see it is cleared on locking.</summary>
    internal byte[]? DataKeyBuffer
    {
        get
        {
            lock (_gate)
            {
                return _dataKey;
            }
        }
    }

    /// <inheritdoc />
    public void Create(ReadOnlySpan<char> masterKey)
    {
        RequireLongEnough(masterKey);
        VaultState before;
        lock (_gate)
        {
            before = CurrentState();
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
                ReplaceUnlocked(dataKey, document, VaultContent.Empty);
            }
            catch
            {
                CryptographicOperations.ZeroMemory(dataKey);
                throw;
            }
        }

        RaiseIfChanged(before, VaultState.Unlocked);
    }

    /// <inheritdoc />
    public void Unlock(ReadOnlySpan<char> masterKey)
    {
        VaultState before;
        lock (_gate)
        {
            before = CurrentState();
            var document = _file.Read();
            var dataKey = UnwrapDataKey(masterKey, document);
            try
            {
                var content = OpenContent(dataKey, document.Content);
                ReplaceUnlocked(dataKey, document, content);
            }
            catch
            {
                CryptographicOperations.ZeroMemory(dataKey);
                throw;
            }
        }

        RaiseIfChanged(before, VaultState.Unlocked);
    }

    /// <inheritdoc />
    public void Lock()
    {
        VaultState before;
        VaultState after;
        lock (_gate)
        {
            before = CurrentState();
            ClearUnlocked();
            after = CurrentState();
        }

        RaiseIfChanged(before, after);
    }

    /// <inheritdoc />
    public void ChangeMasterKey(ReadOnlySpan<char> currentMasterKey, ReadOnlySpan<char> newMasterKey)
    {
        RequireLongEnough(newMasterKey);
        lock (_gate)
        {
            var document = _file.Read();
            var dataKey = UnwrapDataKey(currentMasterKey, document);
            try
            {
                var kdf = NewKdf();
                var changed = document with { Kdf = kdf, DataKey = WrapDataKey(newMasterKey, kdf, dataKey) };
                _file.Write(changed);
                if (_document is not null)
                {
                    _document = changed;
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

    /// <summary>Reads from the decrypted content.</summary>
    /// <typeparam name="T">What is read.</typeparam>
    /// <param name="read">Picks what is needed from the content.</param>
    /// <returns>What <paramref name="read"/> returned.</returns>
    /// <exception cref="VaultException"><see cref="VaultError.Locked"/> when the vault is not unlocked.</exception>
    internal T Read<T>(Func<VaultContent, T> read)
    {
        lock (_gate)
        {
            return read(_content ?? throw new VaultException(VaultError.Locked));
        }
    }

    /// <summary>
    /// Changes the content and writes the vault file. The change is kept in memory only after the file is written, so a
    /// failed write leaves both as they were.
    /// </summary>
    /// <param name="change">Gives the new content from the current one, or the same instance when nothing changes.</param>
    /// <exception cref="VaultException"><see cref="VaultError.Locked"/> when the vault is not unlocked.</exception>
    internal void Update(Func<VaultContent, VaultContent> change)
    {
        lock (_gate)
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

    /// <summary>Derives a key for a purpose from the data key.</summary>
    /// <param name="purpose">The validated purpose name.</param>
    /// <param name="destination">Receives the key.</param>
    /// <exception cref="VaultException"><see cref="VaultError.Locked"/> when the vault is not unlocked.</exception>
    internal void DerivePurposeKey(string purpose, Span<byte> destination)
    {
        lock (_gate)
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
    /// <exception cref="VaultException"><see cref="VaultError.WrongMasterKey"/>.</exception>
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

    /// <summary>Gives the state; the caller holds the gate.</summary>
    /// <returns>Unlocked when the data key is in memory, otherwise whether the file exists.</returns>
    private VaultState CurrentState()
    {
        if (_dataKey is not null)
        {
            return VaultState.Unlocked;
        }

        return _file.Exists ? VaultState.Locked : VaultState.NotCreated;
    }

    /// <summary>Takes ownership of a newly opened data key and its content, clearing any key held before.</summary>
    /// <param name="dataKey">The data key in a pinned array.</param>
    /// <param name="document">The vault file.</param>
    /// <param name="content">The decrypted content.</param>
    private void ReplaceUnlocked(byte[] dataKey, VaultDocument document, VaultContent content)
    {
        ClearUnlocked();
        _dataKey = dataKey;
        _document = document;
        _content = content;
    }

    /// <summary>
    /// Clears the data key and drops the content. The strings in the content cannot be cleared (a .NET string is
    /// immutable); dropping them lets the garbage collector reuse their memory.
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

    /// <summary>Raises <see cref="StateChanged"/> when the state differs; called outside the gate.</summary>
    /// <param name="before">The state before the operation.</param>
    /// <param name="after">The state after it.</param>
    private void RaiseIfChanged(VaultState before, VaultState after)
    {
        if (before != after)
        {
            StateChanged?.Invoke(this, after);
        }
    }
}
