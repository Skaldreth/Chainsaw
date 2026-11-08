using System.Security.Cryptography;
using System.Text.Json;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel.Text;

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
        var generation = Interlocked.Read(ref _lockGeneration);
        bool created;
        bool changed;
        var overtaken = false;
        lock (_operationGate)
        {
            created = !_file.Exists;
            try
            {
                changed = created ? CreateFile(masterKey, generation) : CorrectStateFromFile(exists: true);
            }
            catch (VaultException exception) when (exception.Error == VaultError.Locked)
            {
                // A lock overtook the creation: the file exists now, so the state went from not created to locked.
                changed = true;
                overtaken = true;
            }
        }

        if (overtaken)
        {
            RaiseIfChanged(changed, VaultState.Locked);
            throw new VaultException(VaultError.Locked);
        }

        if (!created)
        {
            RaiseIfChanged(changed, VaultState.Locked);
            throw new VaultException(VaultError.AlreadyCreated);
        }

        RaiseIfChanged(changed, VaultState.Unlocked);
    }

    /// <inheritdoc />
    public void Unlock(ReadOnlySpan<char> masterKey)
    {
        var generation = Interlocked.Read(ref _lockGeneration);
        bool changed;
        VaultDocument? document;
        lock (_operationGate)
        {
            document = TryReadFile();
            changed = document is null
                ? CorrectStateFromFile(exists: false)
                : UnlockWith(masterKey, document, generation);
        }

        if (document is null)
        {
            RaiseIfChanged(changed, VaultState.NotCreated);
            throw new VaultException(VaultError.NotCreated);
        }

        RaiseIfChanged(changed, VaultState.Unlocked);
    }

    /// <inheritdoc />
    public void Lock() => RaiseIfChanged(LockInMemory(), VaultState.Locked);

    /// <inheritdoc />
    public void ChangeMasterKey(ReadOnlySpan<char> currentMasterKey, ReadOnlySpan<char> newMasterKey)
    {
        RequireLongEnough(newMasterKey);
        VaultDocument? document;
        bool changed = false;
        lock (_operationGate)
        {
            document = TryReadFile();
            if (document is null)
            {
                changed = CorrectStateFromFile(exists: false);
            }
            else
            {
                Rewrap(currentMasterKey, newMasterKey, document);
            }
        }

        if (document is null)
        {
            RaiseIfChanged(changed, VaultState.NotCreated);
            throw new VaultException(VaultError.NotCreated);
        }
    }

    /// <summary>
    /// Locks the vault when the application shuts down, without raising <see cref="StateChanged"/>: the handlers may
    /// already be gone, and a failing handler must not stop the container from disposing the rest.
    /// </summary>
    public void Dispose() => LockInMemory();

    /// <summary>Reads from the decrypted content without waiting for a key derivation.</summary>
    /// <typeparam name="T">What is read.</typeparam>
    /// <param name="read">Picks what is needed from the content. It must not block or call the vault.</param>
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
    /// failed write leaves both as they were. The work runs on a snapshot outside the state gate, so readers and
    /// <see cref="Lock"/> never wait for the disk; a lock during the write keeps the file and drops the change from
    /// memory.
    /// </summary>
    /// <param name="change">
    /// Gives the new content, or the same instance when nothing changes. It must not block or call the vault.
    /// </param>
    /// <exception cref="VaultException"><see cref="VaultError.Locked"/> when the vault is not unlocked.</exception>
    internal void Update(Func<VaultContent, VaultContent> change)
    {
        lock (_operationGate)
        {
            var dataKey = VaultCrypto.AllocateSecret(VaultCrypto.KeySize);
            try
            {
                VaultDocument document;
                VaultContent current;
                long generation;
                lock (_stateGate)
                {
                    if (_dataKey is null || _document is null || _content is null)
                    {
                        throw new VaultException(VaultError.Locked);
                    }

                    _dataKey.CopyTo(dataKey, 0);
                    document = _document;
                    current = _content;
                    generation = Interlocked.Read(ref _lockGeneration);
                }

                var content = change(current);
                if (ReferenceEquals(content, current))
                {
                    return;
                }

                document = document with { Content = SealContent(dataKey, content) };
                _file.Write(document);
                lock (_stateGate)
                {
                    if (Interlocked.Read(ref _lockGeneration) == generation)
                    {
                        _document = document;
                        _content = content;
                    }
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(dataKey);
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
            VaultCrypto.DeriveWrappingKey(masterKey, kdf, kdf.Normalizes, wrappingKey);
            return VaultCrypto.Seal(wrappingKey, dataKey, VaultDocument.DataKeyAssociatedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    /// <summary>
    /// Decrypts the data key with the key derived from a master key. A vault written before the normalization is
    /// tried as typed and, when that fails and normalizing would change the key, in normalization form C too, since
    /// the key may first have been typed that way.
    /// </summary>
    /// <param name="masterKey">The master key to try.</param>
    /// <param name="document">The checked vault file.</param>
    /// <returns>The data key in a pinned array that the caller owns and clears.</returns>
    /// <exception cref="VaultException">
    /// <see cref="VaultError.WrongMasterKey"/>. AES-GCM cannot tell a wrong key from a changed wrapped key, so a
    /// tampered file is reported the same way.
    /// </exception>
    private static byte[] UnwrapDataKey(ReadOnlySpan<char> masterKey, VaultDocument document)
    {
        var kdf = document.Kdf;
        if (TryUnwrapDataKey(masterKey, document, kdf.Normalizes) is { } dataKey)
        {
            return dataKey;
        }

        if (!kdf.Normalizes && !SecretText.IsNormalized(masterKey)
            && TryUnwrapDataKey(masterKey, document, normalize: true) is { } normalized)
        {
            return normalized;
        }

        throw new VaultException(VaultError.WrongMasterKey);
    }

    /// <summary>Tries to decrypt the data key with the key derived one way from a master key.</summary>
    /// <param name="masterKey">The master key to try.</param>
    /// <param name="document">The checked vault file.</param>
    /// <param name="normalize">Whether the key is derived in normalization form C.</param>
    /// <returns>The data key in a pinned array that the caller owns and clears, or none when it does not open.</returns>
    private static byte[]? TryUnwrapDataKey(ReadOnlySpan<char> masterKey, VaultDocument document, bool normalize)
    {
        Span<byte> wrappingKey = stackalloc byte[VaultCrypto.KeySize];
        var dataKey = VaultCrypto.AllocateSecret(VaultCrypto.KeySize);
        try
        {
            VaultCrypto.DeriveWrappingKey(masterKey, document.Kdf, normalize, wrappingKey);
            VaultCrypto.Open(wrappingKey, document.DataKey, VaultDocument.DataKeyAssociatedData, dataKey);
            return dataKey;
        }
        catch (AuthenticationTagMismatchException)
        {
            CryptographicOperations.ZeroMemory(dataKey);
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    /// <summary>
    /// Serialises and encrypts the content. The plaintext is written straight into a pinned buffer that is cleared
    /// afterwards, so that no copy of it is left for the garbage collector to move.
    /// </summary>
    /// <param name="dataKey">The data key, from which the content key is derived.</param>
    /// <param name="content">The content.</param>
    /// <returns>The encrypted content under a fresh nonce.</returns>
    private static SealedBox SealContent(ReadOnlySpan<byte> dataKey, VaultContent content)
    {
        Span<byte> contentKey = stackalloc byte[VaultCrypto.KeySize];
        using var plaintext = new PinnedBufferWriter();
        try
        {
            using (var writer = new Utf8JsonWriter(plaintext))
            {
                JsonSerializer.Serialize(writer, content);
            }

            VaultCrypto.DeriveContentKey(dataKey, contentKey);
            return VaultCrypto.Seal(contentKey, plaintext.WrittenSpan, VaultDocument.ContentAssociatedData);
        }
        finally
        {
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

    /// <summary>Wraps the data key again under a new master key; the caller holds the operation gate.</summary>
    /// <param name="currentMasterKey">The master key that opens the file now.</param>
    /// <param name="newMasterKey">The new, checked master key.</param>
    /// <param name="document">The vault file as read.</param>
    private void Rewrap(ReadOnlySpan<char> currentMasterKey, ReadOnlySpan<char> newMasterKey, VaultDocument document)
    {
        var dataKey = UnwrapDataKey(currentMasterKey, document);
        try
        {
            RequireSameKeyAsUnlocked(dataKey);
            var kdf = NewKdf();
            var rewrapped = document with { Kdf = kdf, DataKey = WrapDataKey(newMasterKey, kdf, dataKey) };
            _file.Write(rewrapped);
            lock (_stateGate)
            {
                if (_document is not null)
                {
                    _document = _document with { Kdf = rewrapped.Kdf, DataKey = rewrapped.DataKey };
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <summary>Creates the vault file and unlocks it; the caller holds the operation gate.</summary>
    /// <param name="masterKey">The checked master key.</param>
    /// <param name="generation">The lock generation when the operation started.</param>
    /// <returns>Whether the state changed, so that the caller raises the event outside the gates.</returns>
    private bool CreateFile(ReadOnlySpan<char> masterKey, long generation)
    {
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
            return CommitUnlocked(dataKey, document, VaultContent.Empty, generation);
        }
        catch
        {
            // Only reached before the vault owns the key: CommitUnlocked takes ownership as its last step.
            CryptographicOperations.ZeroMemory(dataKey);
            throw;
        }
    }

    /// <summary>Opens a read vault file with the master key; the caller holds the operation gate.</summary>
    /// <param name="masterKey">The master key.</param>
    /// <param name="document">The vault file as read.</param>
    /// <param name="generation">The lock generation when the operation started.</param>
    /// <returns>Whether the state changed, so that the caller raises the event outside the gates.</returns>
    private bool UnlockWith(ReadOnlySpan<char> masterKey, VaultDocument document, long generation)
    {
        var dataKey = UnwrapDataKey(masterKey, document);
        try
        {
            var content = OpenContent(dataKey, document.Content);
            document = StrengthenIfWeak(masterKey, document, dataKey);
            return CommitUnlocked(dataKey, document, content, generation);
        }
        catch
        {
            // Only reached before the vault owns the key: CommitUnlocked takes ownership as its last step.
            CryptographicOperations.ZeroMemory(dataKey);
            throw;
        }
    }

    /// <summary>Reads the vault file, telling a missing file apart from other failures.</summary>
    /// <returns>The checked document, or <see langword="null"/> when there is no file.</returns>
    private VaultDocument? TryReadFile()
    {
        try
        {
            return _file.Read();
        }
        catch (VaultException exception) when (exception.Error == VaultError.NotCreated)
        {
            return null;
        }
    }

    /// <summary>
    /// Brings the state in line with the file when it was created or removed outside this vault while locked.
    /// </summary>
    /// <param name="exists">Whether the file exists.</param>
    /// <returns>Whether the state changed.</returns>
    private bool CorrectStateFromFile(bool exists)
    {
        lock (_stateGate)
        {
            var observed = exists ? VaultState.Locked : VaultState.NotCreated;
            if (_state == VaultState.Unlocked || _state == observed)
            {
                return false;
            }

            _state = observed;
            return true;
        }
    }

    /// <summary>Clears the key and the content and marks the vault locked, without raising the event.</summary>
    /// <returns>Whether the state changed.</returns>
    private bool LockInMemory()
    {
        lock (_stateGate)
        {
            Interlocked.Increment(ref _lockGeneration);
            ClearUnlocked();
            if (_state != VaultState.Unlocked)
            {
                return false;
            }

            _state = VaultState.Locked;
            return true;
        }
    }

    /// <summary>Raises <see cref="StateChanged"/> when the state changed; called outside both gates.</summary>
    /// <param name="changed">Whether the state changed.</param>
    /// <param name="state">The new state.</param>
    private void RaiseIfChanged(bool changed, VaultState state)
    {
        if (changed)
        {
            StateChanged?.Invoke(this, state);
        }
    }

    /// <summary>
    /// Gives new derivation parameters with a fresh salt, the configured iteration count and normalization form C.
    /// </summary>
    /// <returns>The parameters.</returns>
    private KdfParameters NewKdf() =>
        new(KdfParameters.AlgorithmName, _iterations, VaultCrypto.NewSalt(), KdfParameters.NormalizationName);

    /// <summary>
    /// Wraps the data key again with the configured iteration count and normalization form C when the file was
    /// written with fewer iterations or without the normalization, so that a raised default and the normalization
    /// reach old vaults without locking anyone out: the key that just opened the vault is the one wrapped again. A
    /// failed write keeps the old file, which still opens.
    /// </summary>
    /// <param name="masterKey">The master key that just opened the vault.</param>
    /// <param name="document">The vault file as read.</param>
    /// <param name="dataKey">The data key.</param>
    /// <returns>The document as it now is on disk.</returns>
    private VaultDocument StrengthenIfWeak(ReadOnlySpan<char> masterKey, VaultDocument document, byte[] dataKey)
    {
        // Runs before the commit, so an unlock that a lock overtakes may still have strengthened the file; the file is
        // valid either way.
        if (document.Kdf.Iterations >= _iterations && document.Kdf.Normalizes)
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
    /// Takes ownership of a newly opened data key and its content, clearing any key held before. An unlock that a
    /// <see cref="Lock"/> overtook is refused, so that locking always wins. Taking ownership is the last step and
    /// nothing after it can throw, so a caller that clears the key on failure never clears the vault's own key.
    /// </summary>
    /// <param name="dataKey">The data key in a pinned array; cleared by the caller if this throws.</param>
    /// <param name="document">The vault file.</param>
    /// <param name="content">The decrypted content.</param>
    /// <param name="generation">The lock generation when the operation started.</param>
    /// <returns>Whether the state changed, so that the caller raises the event outside the gates.</returns>
    /// <exception cref="VaultException"><see cref="VaultError.Locked"/>: the vault was locked meanwhile.</exception>
    private bool CommitUnlocked(byte[] dataKey, VaultDocument document, VaultContent content, long generation)
    {
        lock (_stateGate)
        {
            if (Interlocked.Read(ref _lockGeneration) != generation)
            {
                // A vault that was created meanwhile exists now, even though it stays locked.
                _state = VaultState.Locked;
                throw new VaultException(VaultError.Locked);
            }

            ClearUnlocked();
            var changed = _state != VaultState.Unlocked;
            _state = VaultState.Unlocked;
            _dataKey = dataKey;
            _document = document;
            _content = content;
            return changed;
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
