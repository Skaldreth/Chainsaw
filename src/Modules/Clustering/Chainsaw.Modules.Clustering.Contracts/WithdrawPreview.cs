using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>
/// What drawing a version of an analysis back would do to the clusters, for the confirmation the user sees before it
/// is drawn back (sketch KV1a). It is worked out from what the case has stored: the version's conclusions stop
/// counting, and the stored conclusions of the version that counts instead count again. What that version concludes
/// about transactions it has never analysed shows only after the next run. Nothing is stored.
/// </summary>
/// <param name="Heuristic">The analysis and version that would be drawn back.</param>
/// <param name="CountsInstead">
/// The highest registered version of the same analysis that is not drawn back, which counts instead.
/// </param>
/// <param name="Changes">The clusters that would change, the largest first.</param>
public sealed record WithdrawPreview(
    HeuristicId Heuristic, HeuristicId CountsInstead, IReadOnlyList<ClusterChange> Changes);

/// <summary>One cluster before and after a change, seen from one of its addresses.</summary>
/// <param name="Anchor">The address the cluster is seen from; it is in both.</param>
/// <param name="Before">The cluster's addresses before.</param>
/// <param name="After">The anchor's cluster's addresses after.</param>
public sealed record ClusterChange(
    BitcoinAddress Anchor, IReadOnlyList<BitcoinAddress> Before, IReadOnlyList<BitcoinAddress> After)
{
    /// <summary>Gets the addresses that leave the cluster.</summary>
    public IReadOnlyList<BitcoinAddress> Leaving => [.. Before.Except(After)];

    /// <summary>Gets the addresses that join the cluster.</summary>
    public IReadOnlyList<BitcoinAddress> Joining => [.. After.Except(Before)];
}
