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
    /// <summary>Gets the analyses that run, in the order they run.</summary>
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
    /// The addresses are the same, the heuristic is not a valid name, or the reason is empty or too long.
    /// </exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    ClusterSplit Split(ICase @case, SplitRequest request);

    /// <summary>Builds the cluster an address belongs to, explained from that address.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address.</param>
    /// <returns>The cluster; just the address when nothing links it to another.</returns>
    Cluster GetCluster(ICase @case, BitcoinAddress address);

    /// <summary>Explains why two addresses are in the same cluster: the links on the way from one to the other.</summary>
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
