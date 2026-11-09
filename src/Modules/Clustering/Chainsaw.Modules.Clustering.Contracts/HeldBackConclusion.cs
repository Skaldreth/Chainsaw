using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>
/// A stored conclusion that the latest run held back (sketch HB1a): the transaction's stored conclusions rest on
/// transactions the run did not know, for example an unconfirmed transaction that was replaced, so the old conclusion
/// still counts until the user decides. Nothing about it is a fact (principle 2).
/// </summary>
/// <param name="Txid">The transaction whose conclusions are held back.</param>
/// <param name="Evidence">The stored proposals of the transaction that count now.</param>
/// <param name="Missing">The transactions the conclusions rest on that the latest run did not know.</param>
/// <param name="Decision">The user's latest decision about it, or <see langword="null"/> while it waits.</param>
public sealed record HeldBackConclusion(
    Txid Txid, IReadOnlyList<ClusterEvidence> Evidence, IReadOnlyList<Txid> Missing, HeldBackDecision? Decision)
{
    /// <summary>
    /// Gets the stored findings of the transaction that count now, for example a CoinJoin that ends the trail; a
    /// release lets them go too.
    /// </summary>
    public IReadOnlyList<StoredFinding> Findings { get; init; } = [];

    /// <summary>
    /// Gets whether it waits for the user: nothing is decided, or what was let stand rested on other missing
    /// transactions than those missing now.
    /// </summary>
    public bool IsWaiting => Decision is not { Choice: ConclusionChoice.Keep } kept
        || Missing.Except(kept.Missing).Any();

    /// <summary>Gets the addresses the held back conclusions merge.</summary>
    public IReadOnlyList<BitcoinAddress> Addresses =>
        [.. Evidence.SelectMany(evidence => evidence.Addresses).Distinct()];
}

/// <summary>What the user decided about a conclusion that is held back.</summary>
public enum ConclusionChoice
{
    /// <summary>The conclusion stands as it is (<em>Låt stå</em>).</summary>
    Keep,

    /// <summary>
    /// The conclusion is let go (<em>Släpp slutsatsen</em>): the transaction's conclusions stored before the decision
    /// stop counting, without being removed, and the next run stores what it concludes now.
    /// </summary>
    Release,

    /// <summary>
    /// The latest decision is taken back (sketch AN1b): a conclusion let go counts again, and one let stand waits
    /// again. The decision stays sealed as it was.
    /// </summary>
    Reopen,
}

/// <summary>The user's sealed decision about a conclusion that was held back (sketch HB1a).</summary>
/// <param name="Sequence">The record's number in the sealed chain <c>clustering.held_back_decisions</c>.</param>
/// <param name="Txid">The transaction whose conclusions were held back.</param>
/// <param name="Missing">The transactions that were missing when it was decided.</param>
/// <param name="Addresses">
/// The addresses the held back conclusions merged when it was decided, so that the history of each can show it also
/// after the conclusion was let go.
/// </param>
/// <param name="Choice">What was decided.</param>
/// <param name="Reason">The user's reason, or <see langword="null"/>; the reason is optional.</param>
/// <param name="JournalSequence">The journal entry that says it.</param>
/// <param name="RecordedUtc">When it was sealed.</param>
/// <param name="Undoes">For <see cref="ConclusionChoice.Reopen"/>, the decision taken back; otherwise none.</param>
public sealed record HeldBackDecision(
    long Sequence,
    Txid Txid,
    IReadOnlyList<Txid> Missing,
    IReadOnlyList<BitcoinAddress> Addresses,
    ConclusionChoice Choice,
    string? Reason,
    long JournalSequence,
    DateTimeOffset RecordedUtc,
    long? Undoes = null);
