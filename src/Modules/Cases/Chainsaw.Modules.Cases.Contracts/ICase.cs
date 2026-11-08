namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>
/// An open case. Everything in it is read and written through <see cref="Read{T}"/> and <see cref="Write{T}"/>, which
/// run one at a time; the journal and every other sealed table can only be added to.
/// </summary>
/// <remarks>
/// <para>
/// The case works on an encrypted working copy next to the <c>.cut</c> file. <see cref="Save"/> writes the working
/// copy back into the case file; closing saves too. The case also saves itself in the background after writes, once
/// it has been quiet for a few seconds (and at the latest a minute after a write while writes keep coming), so that a
/// file that is handed on is fresh. Every save writes a new file next to the case file and puts it in place in one
/// move, so the case file is never half-written. A working copy left by an interrupted session is recovered the next
/// time the case is opened, with an entry in the journal.
/// </para>
/// <para>
/// The methods are thread-safe and synchronous; call them off the UI thread. After <see cref="Close"/> (also when the
/// vault is locked) they throw a <see cref="CaseException"/> with <see cref="CaseError.Closed"/>.
/// </para>
/// </remarks>
public interface ICase : IDisposable
{
    /// <summary>Gets what the case says about itself.</summary>
    CaseInfo Info { get; }

    /// <summary>
    /// Gets the case's title, the last one set with <see cref="SetTitle"/>; empty when it has none. Read without
    /// touching the database, so it can be read on the UI thread, also after the case has closed.
    /// </summary>
    string Title { get; }

    /// <summary>Gets the path of the case file.</summary>
    string Path { get; }

    /// <summary>Gets whether the case has been closed.</summary>
    bool IsClosed { get; }

    /// <summary>Gets whether the file has a sharing envelope that opens with a password.</summary>
    bool HasSharingPassword { get; }

    /// <summary>
    /// Gets the fingerprints of the recovery keys the file has envelopes for (see
    /// <see cref="RecoveryPublicKey.Fingerprint"/>); empty when no recovery key has been configured since the case
    /// was created.
    /// </summary>
    IReadOnlyList<string> RecoveryFingerprints { get; }

    /// <summary>
    /// Runs work that only reads, in one read transaction; any attempt to write is refused by SQLite.
    /// </summary>
    /// <typeparam name="T">What the work returns.</typeparam>
    /// <param name="work">The work; it must not keep the reader after it returns.</param>
    /// <returns>What the work returned.</returns>
    T Read<T>(Func<ICaseReader, T> work);

    /// <summary>
    /// Runs work in one write transaction that is committed when the work returns and rolled back when it throws.
    /// Journal entries and sealed records added in the work are part of the same transaction.
    /// </summary>
    /// <typeparam name="T">What the work returns.</typeparam>
    /// <param name="work">The work; it must not keep the transaction after it returns.</param>
    /// <returns>What the work returned.</returns>
    /// <exception cref="AggregateException">
    /// The work failed and so did running the rest of a reader it left open; the work's failure comes first.
    /// </exception>
    T Write<T>(Func<ICaseTransaction, T> work);

    /// <summary>Adds one entry to the journal in a transaction of its own.</summary>
    /// <param name="draft">What happened.</param>
    /// <returns>The sealed entry.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    JournalEntry AppendJournal(JournalDraft draft);

    /// <summary>
    /// Sets the case's title (see <see cref="CaseTitleRules"/>): the title is sealed in the chain <c>cases.title</c>
    /// with a line in the journal, in one transaction, so that every earlier title stays. Setting the title it already
    /// has does nothing.
    /// </summary>
    /// <param name="title">The title; it is normalized first, and an empty title means none.</param>
    /// <returns><see langword="true"/> when the title changed.</returns>
    /// <exception cref="ArgumentException">The title is too long or holds control characters.</exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/>, <see cref="CaseError.Corrupt"/> when the title's table has been
    /// changed outside Chainsaw, or <see cref="CaseError.Closed"/>.
    /// </exception>
    bool SetTitle(string title);

    /// <summary>Reads journal entries in order.</summary>
    /// <param name="fromSequence">The first sequence number to read; the journal starts at 1.</param>
    /// <param name="maximumCount">The most entries to read.</param>
    /// <returns>The entries, in sequence order.</returns>
    IReadOnlyList<JournalEntry> ReadJournal(long fromSequence = 1, int maximumCount = int.MaxValue);

    /// <summary>
    /// Adds a sharing envelope that opens the case with a password, replacing an earlier one, and saves the file.
    /// The password is derived with PBKDF2-SHA512 like the master key, so this takes a noticeable time.
    /// </summary>
    /// <param name="password">
    /// The password, at least <see cref="SharingPasswordRules.MinimumLength"/> characters.
    /// </param>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.PasswordTooShort"/>, <see cref="CaseError.TooManyEnvelopes"/> or
    /// <see cref="CaseError.Closed"/>.
    /// </exception>
    void SetSharingPassword(ReadOnlySpan<char> password);

    /// <summary>Removes the sharing envelope, if there is one, and saves the file.</summary>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.WouldLoseAccess"/> when this vault has no envelope of its own in the file (add one with
    /// <see cref="AddOperatorAccess"/> first), or <see cref="CaseError.Closed"/>.
    /// </exception>
    void RemoveSharingPassword();

    /// <summary>
    /// Adds an envelope for this vault, so that a case opened with its sharing password can later be opened without
    /// it, and saves the file. Nothing happens when this vault already has one.
    /// </summary>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.VaultLocked"/>, <see cref="CaseError.TooManyEnvelopes"/> or
    /// <see cref="CaseError.Closed"/>.
    /// </exception>
    void AddOperatorAccess();

    /// <summary>
    /// Describes the envelopes in the file's header (sketch ÄR1b, the tab <em>Kuvert</em>), in file order: which one
    /// is this vault's, which are other vaults', the sharing envelope, the recovery keys' and those of unknown kinds.
    /// Nothing is changed. An envelope is this vault's only when it opens to this case's file key.
    /// </summary>
    /// <returns>The envelopes.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.VaultLocked"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    IReadOnlyList<CaseEnvelope> DescribeEnvelopes();

    /// <summary>Writes the working copy back into the case file, atomically.</summary>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.Closed"/>, or <see cref="CaseError.LockedWhileSaving"/> when the vault was locked while the
    /// file was written: the file is saved, but the case is closing.
    /// </exception>
    void Save();

    /// <summary>
    /// Saves, closes the database, removes the working copy and clears the file key from memory. Closing twice is
    /// harmless. If the save fails, the working copy is kept so that the next opening recovers it, and the failure
    /// is thrown.
    /// </summary>
    void Close();
}
