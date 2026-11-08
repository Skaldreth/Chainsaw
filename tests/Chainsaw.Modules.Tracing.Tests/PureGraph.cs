using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// Builds a graph straight from transactions, without a case: every address is its own actor and every output that
/// no given transaction spends is unspent.
/// </summary>
internal static class PureGraph
{
    public static TraceGraph Of(IEnumerable<BitcoinTransaction> transactions, params TraceStartPoint[] starts) =>
        Of(transactions, [], starts);

    /// <summary>Builds the graph with the user's links to other chains.</summary>
    public static TraceGraph Of(
        IEnumerable<BitcoinTransaction> transactions,
        IReadOnlyList<CrossChainLink> links,
        params TraceStartPoint[] starts)
    {
        var list = transactions.ToList();
        var spentBy = list.SelectMany(tx => tx.Inputs.Where(input => !input.IsCoinbase).Select(input =>
                (Coin: new Outpoint(input.PreviousTxid!, input.PreviousIndex!.Value),
                    Spend: new BitcoinSpend(tx.Txid, input.Index, tx.Block))))
            .GroupBy(pair => pair.Coin)
            .ToDictionary(group => group.Key, group => group.OrderBy(pair => pair.Spend.Block is null).First().Spend);
        var data = new GraphData(
            new GraphState(
                starts, [], links, new Dictionary<string, IReadOnlyDictionary<GraphLayout, StoredPosition>>()),
            list.ToDictionary(tx => tx.Txid, tx => new GraphTransaction(tx, LookupStatus.FromCase, null)),
            list.ToDictionary(tx => tx.Txid, tx => new OutputSpendsAnswer(
                [.. tx.Outputs.Select(output => new BitcoinOutputSpend(
                    output.Index, spentBy.GetValueOrDefault(new Outpoint(tx.Txid, output.Index))))],
                false)),
            []);
        return GraphBuilder.Build(data, new GraphKnowledge(address => [address], _ => [], _ => []));
    }
}
