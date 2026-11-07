using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>
/// Chooses the coins the traced money starts in, from the start points: every coin the graph shows paid to a start
/// address, and the coins a start transaction pays to others than its own payers. A coin that lies downstream of
/// another seed is left out, since Tracing would count it twice.
/// </summary>
/// <remarks>
/// Which coins are traced is the user's choice, not a conclusion (<see cref="TraceResult.Traced"/>). Until the user
/// can choose them in the graph (TODO), this is the choice, and the inspector says so.
/// </remarks>
public static class SeedPicker
{
    /// <summary>Gives the seeds for a graph.</summary>
    /// <param name="graph">The graph.</param>
    /// <returns>The seeds, in the order of the start points; empty when the graph shows no coin to start in.</returns>
    public static IReadOnlyList<TraceSeed> Pick(TraceGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var coins = new List<Outpoint>();
        foreach (var start in graph.StartPoints)
        {
            coins.AddRange(start.Address is { } address ? PaidTo(graph, address) : PaidBy(graph, start.Txid!));
        }

        var distinct = coins.Distinct().ToList();
        var downstream = new HashSet<Txid>();
        foreach (var coin in distinct)
        {
            downstream.UnionWith(Downstream(graph, coin));
        }

        return [.. distinct.Where(coin => !downstream.Contains(coin.Txid)).Select(coin => new TraceSeed(coin))];
    }

    /// <summary>Gives every coin in the graph paid to an address, in the transactions' order by txid.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="address">The address.</param>
    /// <returns>The coins.</returns>
    private static IEnumerable<Outpoint> PaidTo(TraceGraph graph, BitcoinAddress address) =>
        graph.Transactions.Values
            .OrderBy(item => item.Transaction.Txid.Value, StringComparer.Ordinal)
            .SelectMany(item => item.Transaction.Outputs
                .Where(output => output.Address == address && output.Amount.Value > 0)
                .Select(output => new Outpoint(item.Transaction.Txid, output.Index)));

    /// <summary>
    /// Gives the coins a transaction pays to others than its payers; when it only pays back to them, every coin.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="txid">The transaction.</param>
    /// <returns>The coins; none when the transaction is not in the graph.</returns>
    private static IEnumerable<Outpoint> PaidBy(TraceGraph graph, Txid txid)
    {
        if (!graph.Transactions.TryGetValue(txid, out var item))
        {
            return [];
        }

        var transaction = item.Transaction;
        var payers = transaction.Inputs.Select(input => input.Address).OfType<BitcoinAddress>().ToHashSet();
        var paid = transaction.Outputs.Where(output => output.Amount.Value > 0).ToList();
        var toOthers = paid.Where(output => output.Address is null || !payers.Contains(output.Address)).ToList();
        return (toOthers.Count > 0 ? toOthers : paid).Select(output => new Outpoint(txid, output.Index));
    }

    /// <summary>
    /// Gives every transaction in the graph that the money in a coin reaches, however many steps on. It follows every
    /// spend the case knows, also past an end of the trail, so it may leave out a seed that Tracing would accept; that
    /// errs towards tracing less, never towards counting twice.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="coin">The coin.</param>
    /// <returns>The transactions.</returns>
    private static HashSet<Txid> Downstream(TraceGraph graph, Outpoint coin)
    {
        var reached = new HashSet<Txid>();
        var waiting = new Queue<Outpoint>([coin]);
        while (waiting.TryDequeue(out var next))
        {
            if (!graph.Spends.TryGetValue(next, out var spend) || spend.SpentBy is not { } spentBy
                || !graph.Transactions.TryGetValue(spentBy.Txid, out var item) || !reached.Add(spentBy.Txid))
            {
                continue;
            }

            foreach (var output in item.Transaction.Outputs)
            {
                waiting.Enqueue(new Outpoint(spentBy.Txid, output.Index));
            }
        }

        return reached;
    }
}
