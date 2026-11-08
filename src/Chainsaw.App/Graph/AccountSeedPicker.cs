using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>
/// Chooses the movements the traced money starts in on Ethereum, from the start points, as <see cref="SeedPicker"/>
/// does for Bitcoin's coins: every movement the graph shows paid to a start address, of every asset, and a start
/// transaction's movements to others than the account that signed it. A movement that leaves an account after another
/// seed's money has reached it is left out, since it would be counted twice.
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
        var ordered = transfers.OrderBy(AccountGraph.ChainOrder).ToList();
        var arrivals = distinct.ToDictionary(seed => seed, seed => Arrival(ordered, seed));
        var kept = distinct.Where(seed => !distinct.Any(other => other != seed
                && other.Amount.Asset == seed.Amount.Asset
                && arrivals[other].TryGetValue(seed.From, out var arrived)
                && arrived.CompareTo(AccountGraph.ChainOrder(seed)) < 0))
            .ToList();
        return [.. kept.Select(AccountSeed.Of)];
    }

    /// <summary>
    /// Gives when the money of a movement can first reach each account in the graph, however many steps on, in the
    /// same asset: a movement carries it on only when it leaves an account after the money arrived there. Only another
    /// seed's reach can leave a seed out, and only when it arrives at the seed's sender before the seed leaves; that
    /// errs towards tracing less, never towards counting twice.
    /// </summary>
    /// <param name="transfers">The graph's movements, in chain order (sorted once for every seed).</param>
    /// <param name="seed">The movement.</param>
    /// <returns>The earliest arrival at each account it reaches, in chain order.</returns>
    private static Dictionary<EthereumAddress, (long, long, string, long, int)> Arrival(
        IReadOnlyList<EthereumTransfer> transfers, EthereumTransfer seed)
    {
        var arrival = new Dictionary<EthereumAddress, (long, long, string, long, int)>
        {
            [seed.To] = AccountGraph.ChainOrder(seed),
        };
        foreach (var transfer in transfers.Where(transfer => transfer.Amount.Asset == seed.Amount.Asset))
        {
            var order = AccountGraph.ChainOrder(transfer);
            if (arrival.TryGetValue(transfer.From, out var at) && at.CompareTo(order) < 0
                && !arrival.ContainsKey(transfer.To))
            {
                arrival[transfer.To] = order;
            }
        }

        return arrival;
    }
}
