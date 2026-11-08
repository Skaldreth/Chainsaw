namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>Something wrong with a case as a whole, found by <see cref="ICaseVerifier"/>.</summary>
public enum CaseProblem
{
    /// <summary>
    /// A page of the encrypted database could not be read or authenticated, or SQLite found its structure unsound
    /// (<c>PRAGMA quick_check</c>). A page changed without the key fails here.
    /// </summary>
    DatabaseUnsound,

    /// <summary>
    /// The case information (id, number, creation time) or the triggers that protect the Cases module's own tables
    /// have been changed, or the journal does not begin with the entry that created this case.
    /// </summary>
    CaseInfoChanged,

    /// <summary>
    /// This computer's record of how far each chain had come (the anchors) cannot be read, has been changed or is
    /// older than the last one this vault wrote, so a rolled-back or shortened case could not be checked.
    /// </summary>
    AnchorsUnreadable,

    /// <summary>
    /// The journal records that the case, when it was opened, did not match what this computer had recorded (an
    /// older copy, a change made outside Chainsaw, or missing or unreadable anchors); the
    /// <see cref="CaseJournalKinds.AnchorsReplaced"/> entries say what.
    /// </summary>
    AnchorsReplaced,
}
