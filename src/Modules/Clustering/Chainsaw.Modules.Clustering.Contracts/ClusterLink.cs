namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>A stored proposal as it stands in the clusters now, with the split that stopped it, if any.</summary>
/// <param name="Evidence">The stored proposal.</param>
/// <param name="State">What it does now.</param>
/// <param name="Split">The user's split that stopped it, or <see langword="null"/>.</param>
public sealed record ClusterLink(ClusterEvidence Evidence, LinkState State, ClusterSplit? Split = null);

/// <summary>What a stored proposal does in the clusters now.</summary>
public enum LinkState
{
    /// <summary>It merged its addresses.</summary>
    Merged,

    /// <summary>It is only a suggestion, as the analysis or the weighing of the transaction made it.</summary>
    Suggested,

    /// <summary>Another analysis of the same transaction stopped it (for example a CoinJoin).</summary>
    Stopped,

    /// <summary>
    /// It would merge two addresses the user has split, and it comes from the heuristic the user broke; it never
    /// merges them again.
    /// </summary>
    RefusedBySplit,

    /// <summary>
    /// It would merge two addresses the user has split, but it comes from another heuristic; it is shown as a
    /// suggestion so that the user sees it.
    /// </summary>
    SuggestedAfterSplit,
}
