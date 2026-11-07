using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>
/// A stored proposal that addresses have the same owner: which analysis made it, from which transaction, when and
/// why. It is sealed in the case and never changed; a later analysis of the same transaction by the same heuristic
/// and version replaces it only by being newer.
/// </summary>
/// <param name="Sequence">The record's number in the sealed chain <c>clustering.evidence</c>.</param>
/// <param name="Heuristic">The analysis and its version.</param>
/// <param name="Txid">The transaction it builds on.</param>
/// <param name="Addresses">The addresses it says have the same owner.</param>
/// <param name="Strength">How much it counts, after the other analyses of the transaction were weighed in.</param>
/// <param name="Reason">Why, in plain Swedish.</param>
/// <param name="Adjustment">
/// Why the strength is lower than the analysis gave it (for example a CoinJoin in the same transaction), or
/// <see langword="null"/>.
/// </param>
/// <param name="ChangeOutput">The output the proposal takes to be change, or <see langword="null"/>.</param>
/// <param name="JournalSequence">The journal entry of the analysis that stored it: who ran it and when.</param>
/// <param name="RecordedUtc">When it was stored.</param>
public sealed record ClusterEvidence(
    long Sequence,
    HeuristicId Heuristic,
    Txid Txid,
    IReadOnlyList<BitcoinAddress> Addresses,
    EvidenceStrength Strength,
    string Reason,
    string? Adjustment,
    int? ChangeOutput,
    long JournalSequence,
    DateTimeOffset RecordedUtc);
