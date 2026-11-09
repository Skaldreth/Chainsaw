using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>What one run of the analyses stored in the case.</summary>
/// <param name="Journal">
/// The journal entry of the run, or <see langword="null"/> when nothing new was concluded and nothing was stored.
/// </param>
/// <param name="TransactionsAnalysed">How many transactions were analysed.</param>
/// <param name="NewResults">How many results (one per transaction and analysis) were new or had changed.</param>
/// <param name="Merges">How many new proposals merge addresses.</param>
/// <param name="Suggestions">How many new proposals only suggest.</param>
/// <param name="Stopped">How many new proposals another analysis stopped.</param>
/// <param name="CoinJoins">The transactions newly recognised as CoinJoin.</param>
/// <param name="Failed">
/// The transactions where an analysis failed; nothing about them was stored, and what was stored before stands.
/// </param>
/// <param name="HeldBack">
/// The transactions whose stored conclusions rest on transactions this run did not know; their new results were not
/// stored, so that a narrower run never takes back what a wider one concluded. Analyse them with the whole case. A
/// transaction the run was told is gone is among them when it has conclusions of its own (sketch HB1a).
/// </param>
public sealed record ClusteringRun(
    JournalEntry? Journal,
    int TransactionsAnalysed,
    int NewResults,
    int Merges,
    int Suggestions,
    int Stopped,
    IReadOnlyList<Txid> CoinJoins,
    IReadOnlyList<Txid> Failed,
    IReadOnlyList<Txid> HeldBack);

/// <summary>Everything the analyses concluded about one transaction, as stored in the case.</summary>
/// <param name="Txid">The transaction.</param>
/// <param name="Evidence">The stored proposals, newest result per analysis.</param>
/// <param name="Findings">The stored findings, newest result per analysis, with the analysis that made each.</param>
public sealed record TransactionConclusions(
    Txid Txid,
    IReadOnlyList<ClusterEvidence> Evidence,
    IReadOnlyList<StoredFinding> Findings)
{
    /// <summary>Gets the CoinJoin finding that ends the trail here, or <see langword="null"/>.</summary>
    public CoinJoinFinding? TrailEnd =>
        Findings.Select(found => found.Finding).OfType<CoinJoinFinding>().FirstOrDefault(found => found.EndsTrail);
}

/// <summary>A stored finding with where it came from.</summary>
/// <param name="Sequence">The record's number in the sealed chain <c>clustering.evidence</c>.</param>
/// <param name="Heuristic">The analysis and its version.</param>
/// <param name="Finding">The finding.</param>
/// <param name="JournalSequence">The journal entry of the analysis that stored it.</param>
/// <param name="RecordedUtc">When it was stored.</param>
public sealed record StoredFinding(
    long Sequence, HeuristicId Heuristic, HeuristicFinding Finding, long JournalSequence, DateTimeOffset RecordedUtc);
