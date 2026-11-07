namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>
/// The cases folder: creates, opens and lists case files. A case file (<c>&lt;case number&gt;.cut</c>) is an
/// encrypted SQLite database behind an unencrypted header with key envelopes, see <see cref="ICase"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every method that opens or creates a case needs an unlocked vault, derives keys and copies the database, so it
/// can take a noticeable time; call it off the UI thread. An open case is closed when the vault is locked.
/// </para>
/// <para>
/// Besides <see cref="CaseException"/>, the methods can throw <see cref="IOException"/> or
/// <see cref="UnauthorizedAccessException"/> when the folder cannot be read or written.
/// </para>
/// </remarks>
public interface ICaseStore
{
    /// <summary>Gets the folder that holds the case files.</summary>
    string Directory { get; }

    /// <summary>Lists the case files in the folder without opening them.</summary>
    /// <returns>The files, ordered by case number; empty when the folder does not exist yet.</returns>
    IReadOnlyList<CaseFileInfo> List();

    /// <summary>
    /// Creates a new case with a new random file key, an envelope for this vault, the current schema and a first
    /// journal entry, saves it and leaves it open.
    /// </summary>
    /// <param name="caseNumber">The case number; it becomes the file name.</param>
    /// <returns>The open case; the caller disposes it.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.InvalidCaseNumber"/>, <see cref="CaseError.AlreadyExists"/>,
    /// <see cref="CaseError.InUse"/>, <see cref="CaseError.VaultLocked"/> or
    /// <see cref="CaseError.NoOperatorProfile"/>.
    /// </exception>
    ICase Create(string caseNumber);

    /// <summary>
    /// Opens a case in the folder with this vault's envelope, brings its schema up to date and recovers the working
    /// copy of an interrupted session.
    /// </summary>
    /// <param name="caseNumber">The case number.</param>
    /// <returns>The open case; the caller disposes it.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.InvalidCaseNumber"/>, <see cref="CaseError.NotFound"/>, <see cref="CaseError.NoAccess"/>,
    /// <see cref="CaseError.Corrupt"/>, <see cref="CaseError.UnsupportedVersion"/>, <see cref="CaseError.InUse"/>,
    /// <see cref="CaseError.Conflict"/>, <see cref="CaseError.VaultLocked"/> or
    /// <see cref="CaseError.NoOperatorProfile"/>.
    /// </exception>
    ICase Open(string caseNumber);

    /// <summary>
    /// Opens a case file anywhere, for example a colleague's, with its sharing password. The vault must still be
    /// unlocked, since the journal records who opened it.
    /// </summary>
    /// <param name="path">The path of the <c>.cut</c> file.</param>
    /// <param name="password">The sharing password; the caller clears its buffer after the call.</param>
    /// <returns>The open case; the caller disposes it.</returns>
    /// <exception cref="CaseException">The same reasons as <see cref="Open"/>.</exception>
    ICase OpenWithPassword(string path, ReadOnlySpan<char> password);

    /// <summary>
    /// Gets the organisation's public recovery key built into this Chainsaw, or <see langword="null"/> when none is
    /// configured. Every case created or opened while a key is configured gets an envelope for it; without one, cases
    /// can only be opened with their own envelopes, and the user interface says so.
    /// </summary>
    RecoveryPublicKey? RecoveryKey { get; }

    /// <summary>
    /// Opens a case file anywhere with the organisation's private recovery key, for the administrator's tool, when
    /// the operator has lost the master key. The vault must still be unlocked, since the journal records the
    /// opening; the entry also names the administrator and the key's fingerprint.
    /// </summary>
    /// <param name="path">The path of the <c>.cut</c> file.</param>
    /// <param name="privateKey">The private P-384 key; the caller keeps and disposes it.</param>
    /// <param name="administrator">The name of the administrator who opens the case, for the journal.</param>
    /// <returns>The open case; the caller disposes it.</returns>
    /// <exception cref="CaseException">
    /// The same reasons as <see cref="Open"/>; <see cref="CaseError.NoAccess"/> when the file has no envelope that
    /// the key opens.
    /// </exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The key is not a P-384 key.</exception>
    ICase OpenWithRecoveryKey(
        string path, System.Security.Cryptography.ECDiffieHellman privateKey, string administrator);

    /// <summary>
    /// Reads which recovery keys a case file has envelopes for, from its unencrypted header, without opening it and
    /// without the vault.
    /// </summary>
    /// <param name="path">The path of the <c>.cut</c> file.</param>
    /// <returns>The fingerprints (see <see cref="RecoveryPublicKey.Fingerprint"/>), in file order.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NotFound"/>, <see cref="CaseError.Corrupt"/> or <see cref="CaseError.UnsupportedVersion"/>.
    /// </exception>
    IReadOnlyList<string> ReadRecoveryFingerprints(string path);
}
