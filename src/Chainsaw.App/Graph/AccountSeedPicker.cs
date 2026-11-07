using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>
/// Chooses the movements the traced money starts in on Ethereum, from the start points, as <see cref="SeedPicker"/>
/// does for Bitcoin's coins: every movement the graph shows paid to a start address, of every asset, and a start
/// transaction's movements to others than the account that signed it. A movement that leaves an account the money of
/// another seed reaches is left out, since it would be counted twice.
/// </summary>
/// <remarks>Which movements are traced is the user's choice, not a conclusion; until it can be chosen in the graph
/// (TODO), this is the choice, and the inspector says so.</remarks>
public static class AccountSeedPicker
{
    /// <summary>Gives the seeds for a graph.</summary>
    /// <param name="graph">The graph.</param>
    /// <returns>
    /// The seeds, in the order of the start points; empty when the graph shows no movement to start in.
    /// </returns>
    public static IReadOnlyList<AccountSeed> Pick(TraceGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var transfers = graph.Account.Transfers.Where(transfer => transfer.From != transfer.To).ToList();
        var chosen = new List<EthereumTransfer>();
        foreach (var start in graph.StartPoints)
        {
            if (start.EthereumAddress is { } address)
            {
                chosen.AddRange(transfers.Where(transfer => transfer.To == address));
            }
            else if (start.EthereumTransaction is { } hash
                && graph.Account.Transactions.TryGetValue(hash, out var transaction))
            {
                chosen.AddRange(transfers.Where(transfer =>
                    transfer.Transaction == hash && transfer.To != transaction.From));
            }
        }

        var distinct = chosen.Distinct().ToList();
        var reached = new HashSet<EthereumAddress>();
        foreach (var seed in distinct)
        {
            reached.UnionWith(Reached(transfers, seed));
        }

        return [.. distinct.Where(seed => !reached.Contains(seed.From)).Select(AccountSeed.Of)];
    }

    /// <summary>
    /// Gives every account the money of a movement can reach in the graph, however many steps on, in the same asset
    /// and not earlier in the chain's order. It errs towards tracing less, never towards counting twice.
    /// </summary>
    /// <param name="transfers">The graph's movements.</param>
    /// <param name="seed">The movement.</param>
    /// <returns>The accounts.</returns>
    private static HashSet<EthereumAddress> Reached(IReadOnlyList<EthereumTransfer> transfers, EthereumTransfer seed)
    {
        var reached = new HashSet<EthereumAddress>();
        var waiting = new Queue<(EthereumAddress Account, (long, string, long, int) After)>();
        waiting.Enqueue((seed.To, AccountGraph.ChainOrder(seed)));
        while (waiting.TryDequeue(out var next))
        {
            if (!reached.Add(next.Account))
            {
                continue;
            }

            foreach (var transfer in transfers.Where(transfer => transfer.From == next.Account
                && transfer.Amount.Asset == seed.Amount.Asset
                && AccountGraph.ChainOrder(transfer).CompareTo(next.After) > 0))
            {
                waiting.Enqueue((transfer.To, AccountGraph.ChainOrder(transfer)));
            }
        }

        return reached;
    }
}
