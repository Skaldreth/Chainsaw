using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.Modules.Checks.Contracts;

/// <summary>What an analysis runs on: the case's transactions in the graph, the mixers it knows and what the
/// clustering concluded, all read from the case.</summary>
/// <param name="Transactions">The transactions, in a stable order.</param>
/// <param name="Mixers">The actors that labels say are mixers.</param>
/// <param name="Conclusions">What the clustering stored about each transaction that it has analysed.</param>
public sealed record CheckInput(
    IReadOnlyList<BitcoinTransaction> Transactions,
    IReadOnlyList<KnownMixer> Mixers,
    IReadOnlyDictionary<Txid, TransactionConclusions> Conclusions)
{
    /// <summary>Gets an input with nothing in it.</summary>
    public static CheckInput Empty { get; } = new([], [], new Dictionary<Txid, TransactionConclusions>());
}

/// <summary>An actor that a label says is a mixer: its addresses in the graph.</summary>
/// <param name="Addresses">The mixer's known addresses; never empty.</param>
public sealed record KnownMixer(IReadOnlyList<BitcoinAddress> Addresses);
