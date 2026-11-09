using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>Everything the graph is built from: the state and the facts the case answered with.</summary>
/// <param name="State">The graph's state.</param>
/// <param name="Transactions">The transactions the case could give, by txid.</param>
/// <param name="OutputSpends">The answers about which inputs spend each transaction's outputs, by txid.</param>
/// <param name="Problems">What could not be read, in Swedish.</param>
internal sealed record GraphData(
    GraphState State,
    IReadOnlyDictionary<Txid, GraphTransaction> Transactions,
    IReadOnlyDictionary<Txid, OutputSpendsAnswer> OutputSpends,
    IReadOnlyList<string> Problems)
{
    /// <summary>Gets the Ethereum facts; empty when the graph has none.</summary>
    public AccountData Account { get; init; } = AccountData.Empty;

    /// <summary>
    /// Gets the graph's transactions that the source no longer knows, with what spends their coins now (sketch GR9a);
    /// empty when none is gone.
    /// </summary>
    public IReadOnlyDictionary<Txid, GoneTransaction> Gone { get; init; } = new Dictionary<Txid, GoneTransaction>();
}

/// <summary>The answer about which inputs spend a transaction's outputs.</summary>
/// <param name="Spends">One entry per output, in order.</param>
/// <param name="MayBeOutdated">Whether it may have changed since the case asked.</param>
internal sealed record OutputSpendsAnswer(IReadOnlyList<BitcoinOutputSpend> Spends, bool MayBeOutdated);

/// <summary>What the builder asks about the actors: the clusters and the ends of the trail.</summary>
/// <param name="ClusterOf">Gives the addresses in the same cluster as an address, itself included.</param>
/// <param name="AddressEnds">Gives why the trail ends at an address; empty when it does not.</param>
/// <param name="TransactionEnds">Gives why the trail ends at a transaction; empty when it does not.</param>
internal sealed record GraphKnowledge(
    Func<BitcoinAddress, IReadOnlyCollection<BitcoinAddress>> ClusterOf,
    Func<BitcoinAddress, IReadOnlyList<TrailEndHint>> AddressEnds,
    Func<BitcoinTransaction, IReadOnlyList<TrailEndHint>> TransactionEnds)
{
    /// <summary>Gets why the trail ends at an address on an account chain; empty when it does not.</summary>
    public Func<ChainAddress, IReadOnlyList<TrailEndHint>> AccountEnds { get; init; } = _ => [];

    /// <summary>Gets the account clusters among the graph's accounts, each open or closed (sketch KE1).</summary>
    public IReadOnlyList<GraphAccountCluster> AccountClusters { get; init; } = [];

    /// <summary>Gets the account heuristics' proposals among the graph's accounts.</summary>
    public IReadOnlyList<GraphAccountProposal> AccountProposals { get; init; } = [];
}
