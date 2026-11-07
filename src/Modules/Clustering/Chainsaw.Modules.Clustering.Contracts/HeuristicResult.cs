using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>What one analysis concluded about one transaction.</summary>
/// <param name="Heuristic">The analysis and its version.</param>
/// <param name="Txid">The transaction.</param>
/// <param name="Proposals">Which addresses have the same owner, and why; empty when none.</param>
/// <param name="Findings">What else was recognised; empty when nothing.</param>
/// <param name="Veto">
/// What the analysis says about the other analyses' proposals for the transaction, or <see langword="null"/>.
/// </param>
public sealed record HeuristicResult(
    HeuristicId Heuristic,
    Txid Txid,
    IReadOnlyList<OwnershipProposal> Proposals,
    IReadOnlyList<HeuristicFinding> Findings,
    OwnershipVeto? Veto = null)
{
    /// <summary>Gives a result where nothing was concluded.</summary>
    /// <param name="heuristic">The analysis.</param>
    /// <param name="txid">The transaction.</param>
    /// <returns>An empty result.</returns>
    public static HeuristicResult Nothing(HeuristicId heuristic, Txid txid) => new(heuristic, txid, [], []);
}
