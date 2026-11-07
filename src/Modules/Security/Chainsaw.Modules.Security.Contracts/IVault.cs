namespace Chainsaw.Modules.Security.Contracts;

/// <summary>
/// The vault: a file under the application data folder that holds Chainsaw's secrets (API keys, the operator profile),
/// encrypted with a random data key. The data key is wrapped with a key derived from the master key, which is never
/// stored. The data key is in memory only while the vault is unlocked.
/// </summary>
/// <remarks>
/// <para>
/// The master key is taken as <see cref="ReadOnlySpan{T}"/> of <see cref="char"/>, never as <see cref="string"/>: a
/// string cannot be cleared and stays in memory until the garbage collector reuses it, while the caller can clear a
/// <c>char[]</c> as soon as the call returns.
/// </para>
/// <para>
/// The key derivation is deliberately slow (PBKDF2 with many iterations), so <see cref="Create"/>,
/// <see cref="Unlock"/> and <see cref="ChangeMasterKey"/> take a noticeable time. Call them off the UI thread.
/// The methods are thread-safe. Reading <see cref="State"/>, reading the stores and <see cref="Lock"/> never wait
/// for a key derivation, and a lock that happens during an unlock wins.
/// </para>
/// <para>
/// Besides <see cref="VaultException"/>, the methods that touch the file can throw <see cref="IOException"/> or
/// <see cref="UnauthorizedAccessException"/> when the folder cannot be read or written; the vault is then unchanged.
/// </para>
/// </remarks>
public interface IVault
{
    /// <summary>Gets the current state of the vault.</summary>
    VaultState State { get; }

    /// <summary>Raised after <see cref="State"/> has changed. It may be raised on any thread.</summary>
    event EventHandler<VaultState>? StateChanged;

    /// <summary>Creates a new, empty vault with a master key and leaves it unlocked.</summary>
    /// <param name="masterKey">
    /// The new master key, at least <see cref="MasterKeyRules.MinimumLength"/> characters.
    /// </param>
    /// <exception cref="VaultException">
    /// <see cref="VaultError.MasterKeyTooShort"/> or <see cref="VaultError.AlreadyCreated"/>.
    /// </exception>
    void Create(ReadOnlySpan<char> masterKey);

    /// <summary>
    /// Opens the vault with the master key. Unlocking an unlocked vault checks the key again. A vault written with
    /// fewer iterations than the current default is wrapped again with the default.
    /// </summary>
    /// <param name="masterKey">The master key.</param>
    /// <exception cref="VaultException">
    /// <see cref="VaultError.WrongMasterKey"/> (also when the wrapped key in the file was changed, since the two cannot
    /// be told apart), <see cref="VaultError.NotCreated"/>, <see cref="VaultError.Corrupt"/>,
    /// <see cref="VaultError.UnsupportedVersion"/> or <see cref="VaultError.Locked"/> when the vault was locked while
    /// it was being opened.
    /// </exception>
    void Unlock(ReadOnlySpan<char> masterKey);

    /// <summary>Clears the data key and the decrypted content from memory; locking twice is harmless.</summary>
    void Lock();

    /// <summary>
    /// Replaces the master key. Only the data key is wrapped again, with a new salt, so the content and every key
    /// derived from the data key stay the same. The state does not change.
    /// </summary>
    /// <param name="currentMasterKey">The master key that opens the vault now.</param>
    /// <param name="newMasterKey">
    /// The new master key, at least <see cref="MasterKeyRules.MinimumLength"/> characters.
    /// </param>
    /// <exception cref="VaultException">
    /// <see cref="VaultError.WrongMasterKey"/>, <see cref="VaultError.MasterKeyTooShort"/>,
    /// <see cref="VaultError.NotCreated"/>, <see cref="VaultError.Corrupt"/> (also when the file was replaced while the
    /// vault was unlocked) or <see cref="VaultError.UnsupportedVersion"/>.
    /// </exception>
    void ChangeMasterKey(ReadOnlySpan<char> currentMasterKey, ReadOnlySpan<char> newMasterKey);
}
