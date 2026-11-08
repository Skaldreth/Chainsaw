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
    /// Takes a colleague's case file into the cases folder and opens it with its sharing password (sketch ÄR1b). The
    /// password is tried on the file where it lies first, so that a wrong password copies nothing; the copy is never
    /// written over an existing case and is removed again when it cannot be opened. A file already in the folder is
    /// opened where it is. The caller adds an envelope for this vault with <see cref="ICase.AddOperatorAccess"/>.
    /// </summary>
    /// <param name="path">The path of the <c>.cut</c> file; its name is the case number.</param>
    /// <param name="password">The sharing password; the caller clears its buffer after the call.</param>
    /// <returns>The open case; the caller disposes it.</returns>
    /// <exception cref="CaseException">
    /// The same reasons as <see cref="OpenWithPassword"/>, and <see cref="CaseError.AlreadyExists"/> when another file
    /// with the same case number is in the folder.
    /// </exception>
    ICase ImportWithPassword(string path, ReadOnlySpan<char> password);

    /// <summary>
    /// Describes the envelopes in a case file's header without opening the case (sketch ÄR1b): which one is this
    /// vault's, which are other vaults', the sharing envelope, the recovery keys' and those of unknown kinds.
    /// </summary>
    /// <param name="path">The path of the <c>.cut</c> file.</param>
    /// <returns>The envelopes, in file order.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NotFound"/>, <see cref="CaseError.Corrupt"/>, <see cref="CaseError.UnsupportedVersion"/>
    /// or <see cref="CaseError.VaultLocked"/>.
    /// </exception>
    IReadOnlyList<CaseEnvelope> DescribeEnvelopes(string path);

    /// <summary>
    /// Tells whether a case in the folder has a working copy from an interrupted session that does not belong to its
    /// file (the reason <see cref="Open"/> gives <see cref="CaseError.Conflict"/>), without opening it or the vault.
    /// </summary>
    /// <param name="caseNumber">The case number.</param>
    /// <returns>The conflict, or <see langword="null"/> when there is none.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.InvalidCaseNumber"/> or <see cref="CaseError.NotFound"/>.
    /// </exception>
    WorkingCopyConflict? FindConflict(string caseNumber);

    /// <summary>
    /// Chooses the file in a conflict (sketch ÄR1b): moves the working copy and the files that belong to it, unread
    /// and unchanged, to a folder of its own under <c>Återställda</c> in the cases folder, and opens the case file as
    /// it is saved, with a line in the journal. Nothing is removed. A working copy that belongs to the file after all
    /// is recovered as <see cref="Open"/> does, and none is opened as it is.
    /// </summary>
    /// <param name="caseNumber">The case number.</param>
    /// <returns>The open case; the caller disposes it.</returns>
    /// <exception cref="CaseException">The same reasons as <see cref="Open"/>, except a conflict.</exception>
    ICase OpenSettingAsideWorkingCopy(string caseNumber);

    /// <summary>
    /// Chooses the working copy in a conflict (sketch ÄR1b): saves it as a case of its own under a new case number,
    /// with the case file's envelopes and the journal up to the interruption, and opens it with a line in the
    /// journal. The case file is left as it is. The new case keeps the case's id and file key, since its envelopes
    /// (also other vaults' and the recovery keys') can only open that key.
    /// </summary>
    /// <param name="caseNumber">The case number of the file in conflict.</param>
    /// <param name="branchNumber">The case number of the new case.</param>
    /// <returns>The new case, open; the caller disposes it.</returns>
    /// <exception cref="CaseException">
    /// The same reasons as <see cref="Open"/>; <see cref="CaseError.AlreadyExists"/> when the new number is taken, and
    /// <see cref="CaseError.NotFound"/> when there is no working copy in conflict.
    /// </exception>
    ICase SaveWorkingCopyAsCase(string caseNumber, string branchNumber);

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
