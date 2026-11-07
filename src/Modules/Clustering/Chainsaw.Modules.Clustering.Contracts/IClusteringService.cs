using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>
/// Runs the analyses over a case's transactions, stores what they conclude, and builds the clusters from it: which
/// addresses have the same owner, why, and what the user has split (principles 2 and 3).
/// </summary>
/// <remarks>
/// Every method takes the open case; nothing is kept between calls. The methods are synchronous and may take time
/// with large cases; call them off the UI thread (principle 6).
/// </remarks>
public interface IClusteringService
{
    /// <summary>Gets the analyses that run on Bitcoin, in the order they run.</summary>
    IReadOnlyList<HeuristicId> Heuristics { get; }

    /// <summary>
    /// Runs every analysis over the transactions and stores what is new, sealed, with one journal entry for the run.
    /// The merges take effect at once (the user's decision 2026-11-06). A result that has not changed since the last
    /// run is not stored again.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="transactions">
    /// The transactions to analyse; they are also what the analyses know around each one, so pass every transaction
    /// the case has fetched that may matter (for example the whole graph).
    /// </param>
    /// <returns>What was stored.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> when something new would be stored, or <see cref="CaseError.Closed"/>.
    /// </exception>
    ClusteringRun Analyse(ICase @case, IReadOnlyCollection<BitcoinTransaction> transactions);

    /// <summary>
    /// Stores the user's decision that two addresses do not have the same owner, sealed, with a journal entry.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="request">The two addresses, the heuristic that merged them and the reason.</param>
    /// <returns>The stored split.</returns>
    /// <exception cref="ArgumentException">
    /// The addresses are the same, the heuristic is not a valid name, or the reason is too long.
    /// </exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    ClusterSplit Split(ICase @case, SplitRequest request);

    /// <summary>
    /// Stores the splits of one breaking out, all or none, each sealed, under one journal entry, so that the breaking
    /// out can be told apart and undone as one (sketch AN1b).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="requests">The splits; at least one.</param>
    /// <returns>
    /// The stored splits, in the order given; they share their <see cref="ClusterSplit.JournalSequence"/>.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// There is no split, or one is not valid (see <see cref="Split(ICase, SplitRequest)"/>).
    /// </exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    IReadOnlyList<ClusterSplit> Split(ICase @case, IReadOnlyList<SplitRequest> requests);

    /// <summary>
    /// Undoes splits (sketch AN1b), all or none: a new sealed record per split, under one journal entry, says that it
    /// no longer holds, and the heuristics may merge the addresses again from now on. Nothing is removed; the splits
    /// stay readable in <see cref="GetSplitHistory(ICase)"/>. An undone split cannot be undone again; the addresses
    /// are split anew instead.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="splitSequences">
    /// The splits' <see cref="ClusterSplit.Sequence"/>, for example every split of one breaking out.
    /// </param>
    /// <param name="reason">Why, at most 1 000 characters; optional (the user's decision 2026-11-07).</param>
    /// <returns>How many were undone; a split that does not exist or is already undone is not counted.</returns>
    /// <exception cref="ArgumentException">The reason is too long.</exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    int UndoSplits(ICase @case, IReadOnlyCollection<long> splitSequences, string? reason);

    /// <summary>
    /// Says what undoing splits would merge, for the confirmation the user sees before they are undone: the
    /// addresses that would join the cluster again and the heuristics that would do it. Nothing is stored.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="splitSequences">The splits' <see cref="ClusterSplit.Sequence"/>.</param>
    /// <param name="from">
    /// The address the user looks from (the chosen cluster's); the addresses that would join its cluster are given.
    /// <see langword="null"/>, or an address in none of the splits' clusters, looks from the first split's first
    /// address.
    /// </param>
    /// <returns>The preview, or <see langword="null"/> when none of the splits exists.</returns>
    UndoSplitPreview? PreviewUndoSplits(
        ICase @case, IReadOnlyCollection<long> splitSequences, BitcoinAddress? from);

    /// <summary>
    /// Reads the history of the splits that involve some addresses: every split with one of them on either side, also
    /// those undone, each with its undoing, oldest first (sketch AN1b, <em>Historik</em>).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The addresses, for a cluster all of its addresses.</param>
    /// <returns>The splits and their undoing.</returns>
    IReadOnlyList<SplitHistoryEntry> GetSplitHistory(ICase @case, IReadOnlyCollection<BitcoinAddress> addresses);

    /// <summary>Reads every split in the case, also those undone, each with its undoing, oldest first.</summary>
    /// <param name="case">The open case.</param>
    /// <returns>The splits and their undoing.</returns>
    IReadOnlyList<SplitHistoryEntry> GetSplitHistory(ICase @case);

    /// <summary>Builds the cluster an address belongs to, explained from that address.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address.</param>
    /// <returns>The cluster; just the address when nothing links it to another.</returns>
    Cluster GetCluster(ICase @case, BitcoinAddress address);

    /// <summary>
    /// Builds the clusters of many addresses at once, reading the case's conclusions once, so that a large graph is
    /// not rebuilt address by address. Addresses in the same cluster share one <see cref="Cluster"/>, explained from
    /// the first of them in the order given.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The addresses.</param>
    /// <returns>
    /// The cluster of each address and of the other members of those clusters; just the address when nothing links
    /// it to another.
    /// </returns>
    IReadOnlyDictionary<BitcoinAddress, Cluster> GetClusters(
        ICase @case, IReadOnlyCollection<BitcoinAddress> addresses);

    /// <summary>Explains why two addresses are in the same cluster: the links on the way between them.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="from">One address.</param>
    /// <param name="to">The other address.</param>
    /// <returns>
    /// The links in order from <paramref name="from"/>, empty when they are the same address, or
    /// <see langword="null"/> when they are not in the same cluster.
    /// </returns>
    IReadOnlyList<ClusterLink>? ExplainConnection(ICase @case, BitcoinAddress from, BitcoinAddress to);

    /// <summary>Gives everything the analyses concluded about one transaction, as stored in the case.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="txid">The transaction.</param>
    /// <returns>The conclusions; empty lists when the transaction has not been analysed.</returns>
    TransactionConclusions GetConclusions(ICase @case, Txid txid);
}
