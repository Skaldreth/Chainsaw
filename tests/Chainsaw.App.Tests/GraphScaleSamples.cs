using Chainsaw.App.Graph;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>
/// A made-up graph of thousands of actors (never real addresses or transactions), to measure and test how the graph
/// behaves when it is large (principle 6): a tree where each actor pays three others, and every seventh actor also
/// pays one far back in the tree, so that lines run across the surface.
/// </summary>
internal static class GraphScaleSamples
{
    /// <summary>How many actors each actor pays in the tree.</summary>
    private const int Branching = 3;

    /// <summary>How often an actor also pays one far back: every this many.</summary>
    private const int CrossEvery = 7;

    /// <summary>Gives a node's key.</summary>
    /// <param name="index">The node's number.</param>
    /// <returns>The key.</returns>
    public static string NodeKey(int index) => "address:n" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Gives the scene of a graph of the given number of actors.</summary>
    /// <param name="count">How many actors.</param>
    /// <returns>The scene.</returns>
    public static GraphScene Scene(int count)
    {
        var nodes = new List<GraphNode>(count);
        var edges = new List<GraphEdge>();
        var hops = new int[count];
        for (var index = 0; index < count; index++)
        {
            var parent = (index - 1) / Branching;
            hops[index] = index == 0 ? 0 : hops[parent] + 1;
            nodes.Add(GraphSamples.Node(
                NodeKey(index), GraphNodeKind.Address, [AddressOf(index)], hops[index], start: index == 0));
            if (index == 0)
            {
                continue;
            }

            edges.Add(Line(edges.Count, parent, index));
            if (index % CrossEvery == 0 && index / 2 != parent)
            {
                edges.Add(Line(edges.Count, index / 2, index));
            }
        }

        var graph = new TraceGraph(
            [TraceStartPoint.ForAddress(AddressOf(0))], nodes, edges,
            new UtxoGraph(
                new Dictionary<Txid, GraphTransaction>(),
                new Dictionary<Outpoint, CoinSpend>(),
                new Dictionary<Txid, IReadOnlyList<TrailEndHint>>()),
            [], [], []);
        return GraphScene.Build(new GraphSnapshot(
            graph,
            new Dictionary<string, IReadOnlyList<Chainsaw.Modules.Attribution.Contracts.Label>>(),
            TracingMethodKind.Fifo,
            [],
            null,
            null));
    }

    /// <summary>Gives the places of the scene's hop columns, as the graph first lays it out.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="layout">The layout.</param>
    /// <returns>Each node's top-left corner.</returns>
    public static IReadOnlyDictionary<string, GraphPoint> Places(GraphScene scene, GraphLayout layout) =>
        GraphPlacement.Place(
            scene.LayoutNodes, layout, scene.Columns, new Dictionary<(string, GraphLayout), GraphPoint>());

    /// <summary>A made-up address from a number, over four bytes of it.</summary>
    /// <param name="index">The number.</param>
    /// <returns>The address.</returns>
    private static BitcoinAddress AddressOf(int index)
    {
        var bytes = new byte[20];
        BitConverter.GetBytes(index + 1).CopyTo(bytes, 0);
        return BitcoinAddress.FromWitness(0, bytes, BitcoinNetwork.Mainnet);
    }

    /// <summary>A line of one transfer from one actor to another in a transaction of its own.</summary>
    /// <param name="number">The line's number, which is also its transaction's.</param>
    /// <param name="from">The sender's number.</param>
    /// <param name="to">The receiver's number.</param>
    /// <returns>The line.</returns>
    private static GraphEdge Line(int number, int from, int to)
    {
        var txid = GraphSamples.TxidOf(1000 + number);
        var amount = 0.01m + (number % 97 / 1000m);
        var block = GraphSamples.Block(100 + (number % 500));
        return new GraphEdge(
            "line:" + number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            NodeKey(from),
            NodeKey(to),
            GraphEdgeKind.Transfer,
            GraphSamples.Btc(amount),
            [AssetTransfer.OfCoin(txid, new Outpoint(txid, 0), Satoshi.FromBitcoin(amount), block)],
            block.Time,
            EdgeDirection.Forward);
    }
}
