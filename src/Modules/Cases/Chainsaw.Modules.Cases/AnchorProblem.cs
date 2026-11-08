namespace Chainsaw.Modules.Cases;

/// <summary>Why the anchors of a case are replaced, as recorded in the journal.</summary>
internal enum AnchorProblem
{
    /// <summary>The case no longer reaches one or more anchors.</summary>
    Diverged,

    /// <summary>The anchor file could not be read or its tag did not hold.</summary>
    Unreadable,

    /// <summary>
    /// There was no anchor file although this vault has an envelope of its own in the case, or has written anchors
    /// for it before.
    /// </summary>
    Missing,

    /// <summary>
    /// The anchor file is older than the last one this vault wrote: it was replaced by an older copy, most likely
    /// together with an older case file.
    /// </summary>
    RolledBack,
}
