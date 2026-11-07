namespace Chainsaw.Modules.Cases;

/// <summary>Why the anchors of a case are replaced, as recorded in the journal.</summary>
internal enum AnchorProblem
{
    /// <summary>The case no longer reaches one or more anchors.</summary>
    Diverged,

    /// <summary>The anchor file could not be read or its tag did not hold.</summary>
    Unreadable,

    /// <summary>There was no anchor file although this vault has an envelope of its own in the case.</summary>
    Missing,
}
