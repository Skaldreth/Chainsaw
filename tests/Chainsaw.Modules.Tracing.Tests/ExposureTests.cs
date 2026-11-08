using System.Globalization;
using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// The analysis <em>exposure v1</em> (sketch EP1b) against a key of synthetic transfers: what reaches a service
/// directly and over hops by FIFO and haircut, what reaches none and why, read out and in, and that the services and the
/// rest always add up to what the subject paid or got.
/// </summary>
public sealed class ExposureTests
{
    private const long Btc = 100_000_000;

    private static readonly DateTimeOffset Start = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static readonly ExposureAnalysis Analysis = new(
        [new AccountFifoMethod(), new AccountHaircutMethod(), new FifoMethod(), new HaircutMethod()]);

    private int _tx;

    [Fact]
    public void DirectPaymentsReachTheServiceAndTheRestIsNotReached()
    {
        var graph = Graph(Pay("S", "E", 0.5m, 1), Pay("S", "U", 0.3m, 2));

        var result = Out(graph, depth: 1, TracingMethodKind.Fifo, "E");

        Assert.Equal(80_000_000, (long)result.Total.Units);
        var exchange = Assert.Single(result.Reached);
        Assert.Equal(("E", 50_000_000L, 1), (exchange.NodeKey, (long)exchange.Amount.Units, exchange.Hops));
        Assert.Equal(0.625, result.ShareOf(exchange.Amount), 6);
        Assert.Equal(30_000_000, (long)result.Rest[ExposureRest.NotReached].Units);
    }

    [Fact]
    public void FifoSendsTheSubjectsMoneyOnAfterWhatCameBefore()
    {
        // K had 0.4 from O before 0.6 came from S; then 0.5 to the exchange and 0.5 to the mixer.
        var graph = TwoHops();

        var result = Out(graph, depth: 2, TracingMethodKind.Fifo, "E", "M");

        Assert.Equal([("M", 50_000_000L), ("E", 10_000_000L)], Amounts(result));
        Assert.All(result.Reached, reach => Assert.Equal(2, reach.Hops));
        Assert.False(result.Rest.ContainsKey(ExposureRest.Remains));
        Assert.Equal(new TracingMethodId(TracingMethodKind.Fifo, 1), result.Method);
    }

    [Fact]
    public void HaircutSpreadsTheSubjectsShareOverWhatLeaves()
    {
        var result = Out(TwoHops(), depth: 2, TracingMethodKind.Haircut, "E", "M");

        Assert.Equal([("E", 30_000_000L), ("M", 30_000_000L)], Amounts(result));
        Assert.Equal(new TracingMethodId(TracingMethodKind.Haircut, 1), result.Method);
    }

    [Fact]
    public void TheDepthStopsTheMoneyAtTheFirstRing()
    {
        var result = Out(TwoHops(), depth: 1, TracingMethodKind.Fifo, "E", "M");

        Assert.Empty(result.Reached);
        Assert.Equal(60_000_000, (long)result.Rest[ExposureRest.NotReached].Units);
    }

    [Fact]
    public void WhatStaysWithAnActorIsSaidAsRemaining()
    {
        var graph = Graph(Pay("S", "K", 1m, 1), Pay("K", "E", 0.4m, 2));

        var result = Out(graph, depth: 2, TracingMethodKind.Fifo, "E");

        Assert.Equal([("E", 40_000_000L)], Amounts(result));
        Assert.Equal(60_000_000, (long)result.Rest[ExposureRest.Remains].Units);
    }

    [Fact]
    public void MoneyThatComesBackToTheSubjectIsReturned()
    {
        var graph = Graph(Pay("S", "K", 1m, 1), Pay("K", "S", 0.4m, 2), Pay("K", "E", 0.6m, 3));

        var result = Out(graph, depth: 3, TracingMethodKind.Fifo, "E");

        Assert.Equal([("E", 60_000_000L)], Amounts(result));
        Assert.Equal(40_000_000, (long)result.Rest[ExposureRest.Returned].Units);
    }

    [Fact]
    public void ReadInFifoFindsWhereTheSubjectsMoneyCameFrom()
    {
        // The victim's 0.7 came to K before 0.3 from O; K sent 0.5 to S. The exchange X paid S 0.2 directly.
        var graph = ReadInCase();

        var result = In(graph, depth: 2, TracingMethodKind.Fifo, "V", "X");

        Assert.Equal(70_000_000, (long)result.Total.Units);
        Assert.Equal([("V", 50_000_000L), ("X", 20_000_000L)], Amounts(result));
        Assert.Equal(["S", "K", "V"], result.Reached[0].Path);
        Assert.Equal(2, result.Reached[0].Hops);
        Assert.Empty(result.Rest);
    }

    [Fact]
    public void ReadInHaircutSharesWhatWasSentByWhatCameIn()
    {
        var result = In(ReadInCase(), depth: 2, TracingMethodKind.Haircut, "V", "X");

        Assert.Equal([("V", 35_000_000L), ("X", 20_000_000L)], Amounts(result));
        Assert.Equal(15_000_000, (long)result.Rest[ExposureRest.NotReached].Units);
    }

    [Fact]
    public void ReadInWhatCameBeforeTheGraphsHistoryIsSaid()
    {
        var graph = Graph(Pay("V", "K", 0.4m, 1), Pay("K", "S", 1m, 2));

        var result = In(graph, depth: 2, TracingMethodKind.Fifo, "V");

        Assert.Equal([("V", 40_000_000L)], Amounts(result));
        Assert.Equal(60_000_000, (long)result.Rest[ExposureRest.BeforeHistory].Units);
    }

    [Fact]
    public void TheWayHasTheLinesAndTheTransactionsInOrder()
    {
        var graph = TwoHops();

        var mixer = Out(graph, depth: 2, TracingMethodKind.Fifo, "E", "M").Reached.Single(r => r.NodeKey == "M");

        Assert.Equal(["S", "K", "M"], mixer.Path);
        Assert.Equal(["S>K", "K>M"], mixer.PathEdges);
        Assert.Equal([Tx(2), Tx(4)], mixer.Transactions.Select(t => t.Id));
    }

    [Fact]
    public void ATransactionDrawnAsANodeAddsNoHop()
    {
        var graph = Graph(
            [Node("S"), Node("T", GraphNodeKind.Transaction), Node("E")],
            Pay("S", "T", 1m, 1), Pay("T", "E", 1m, 1));

        var exchange = Assert.Single(Out(graph, depth: 1, TracingMethodKind.Fifo, "E").Reached);

        Assert.Equal(1, exchange.Hops);
        Assert.Equal(["S", "T", "E"], exchange.Path);
    }

    [Fact]
    public void AnotherAssetIsNeverAdded()
    {
        var ether = new GraphEdge(
            "S>E2", "S", "E2", GraphEdgeKind.Transfer, new AssetAmount(Asset.Ether, 5),
            [new AssetTransfer(new ChainTransactionRef(Chain.Ethereum, "0x" + Tx(99)), new AssetAmount(Asset.Ether, 5),
                null, null)],
            null, EdgeDirection.Forward);
        var graph = Graph(Pay("S", "E", 0.5m, 1));
        graph = graph with { Nodes = [.. graph.Nodes, Node("E2")], Edges = [.. graph.Edges, ether] };

        var result = Out(graph, depth: 1, TracingMethodKind.Fifo, "E", "E2");

        Assert.Equal(["E"], result.Reached.Select(reach => reach.NodeKey));
        Assert.Equal(50_000_000, (long)result.Total.Units);
    }

    [Fact]
    public void ASubjectWithoutTransfersHasNoMoney()
    {
        var result = Out(Graph(Pay("A", "E", 1m, 1)), depth: 3, TracingMethodKind.Fifo, "E");

        Assert.False(result.HasMoney);
        Assert.Empty(result.Reached);
        Assert.Empty(result.Rest);
    }

    [Fact]
    public void TheDepthIsOneToThree()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Request(Graph(), ExposureDirection.Out, 0,
            TracingMethodKind.Fifo));
        Assert.Throws<ArgumentOutOfRangeException>(() => Request(Graph(), ExposureDirection.Out, 4,
            TracingMethodKind.Fifo));
    }

    [Fact]
    public void TheExplanationNamesTheAnalysisTheMethodAndTheTransactions()
    {
        var result = Out(TwoHops(), depth: 2, TracingMethodKind.Fifo, "E", "M");

        var text = result.Explain(result.Reached[0]);

        Assert.Contains("exposure v1", text, StringComparison.Ordinal);
        Assert.Contains("FIFO v1", text, StringComparison.Ordinal);
        Assert.Contains(Tx(4), text, StringComparison.Ordinal);
        Assert.Contains("bedömning", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void TheServicesAndTheRestAlwaysAddUpToTheTotal(int seed)
    {
        var random = new Random(seed);
        var keys = Enumerable.Range(0, 9).Select(n => "N" + n.ToString(CultureInfo.InvariantCulture)).ToList();
        var pays = Enumerable.Range(0, 40).Select(minute =>
        {
            var from = keys[random.Next(keys.Count)];
            var to = keys[random.Next(keys.Count)];
            return Pay(from, to, random.Next(1, 100) / 100m, minute);
        }).ToArray();
        var graph = Graph(pays);
        foreach (var direction in Enum.GetValues<ExposureDirection>())
        {
            foreach (var method in Enum.GetValues<TracingMethodKind>())
            {
                for (var depth = 1; depth <= ExposureRequest.MaxDepth; depth++)
                {
                    var result = Analysis.Analyse(Request(graph, direction, depth, method, "N7", "N8") with
                    {
                        Subject = "N0",
                    });
                    var sum = result.Reached.Aggregate(BigInteger.Zero, (s, r) => s + r.Amount.Units)
                        + result.Unknown.Units;
                    Assert.Equal(result.Total.Units, sum);
                }
            }
        }
    }

    /// <summary>S paid K 0.6 after O paid K 0.4; K paid the exchange E 0.5 and the mixer M 0.5.</summary>
    private TraceGraph TwoHops() => Graph(
        Pay("O", "K", 0.4m, 1), Pay("S", "K", 0.6m, 2), Pay("K", "E", 0.5m, 3), Pay("K", "M", 0.5m, 4));

    /// <summary>The victim V paid K 0.7 before O paid 0.3; K paid S 0.5, and the exchange X paid S 0.2.</summary>
    private TraceGraph ReadInCase() => Graph(
        Pay("V", "K", 0.7m, 1), Pay("O", "K", 0.3m, 2), Pay("K", "S", 0.5m, 3), Pay("X", "S", 0.2m, 4));

    private static ExposureResult Out(TraceGraph graph, int depth, TracingMethodKind method, params string[] services) =>
        Analysis.Analyse(Request(graph, ExposureDirection.Out, depth, method, services));

    private static ExposureResult In(TraceGraph graph, int depth, TracingMethodKind method, params string[] services) =>
        Analysis.Analyse(Request(graph, ExposureDirection.In, depth, method, services));

    private static ExposureRequest Request(
        TraceGraph graph, ExposureDirection direction, int depth, TracingMethodKind method, params string[] services) =>
        new(graph, "S", Asset.Bitcoin, direction, depth, method, services.ToHashSet(StringComparer.Ordinal));

    private static List<(string, long)> Amounts(ExposureResult result) =>
        [.. result.Reached.Select(reach => (reach.NodeKey, (long)reach.Amount.Units))];

    private Payment Pay(string from, string to, decimal btc, int minute) =>
        new(from, to, (long)(btc * Btc), minute, ++_tx);

    private static string Tx(int n) => n.ToString("x64", CultureInfo.InvariantCulture);

    private static TraceGraph Graph(params Payment[] pays)
    {
        var keys = pays.SelectMany(pay => new[] { pay.From, pay.To }).Append("S").Distinct().ToList();
        return Graph([.. keys.Select(key => Node(key))], pays);
    }

    private static TraceGraph Graph(IReadOnlyList<GraphNode> nodes, params Payment[] pays)
    {
        var edges = pays.GroupBy(pay => (pay.From, pay.To)).Select(group =>
        {
            var transfers = group.Select(Transfer).ToList();
            return new GraphEdge(
                group.Key.From + ">" + group.Key.To,
                group.Key.From,
                group.Key.To,
                GraphEdgeKind.Transfer,
                transfers.Aggregate(AssetAmount.Zero(Asset.Bitcoin), (sum, t) => sum + t.Amount),
                transfers,
                transfers[0].Time,
                EdgeDirection.Forward);
        }).ToList();
        return new TraceGraph(
            [], nodes, edges,
            new UtxoGraph(
                new Dictionary<Txid, GraphTransaction>(),
                new Dictionary<Outpoint, CoinSpend>(),
                new Dictionary<Txid, IReadOnlyList<TrailEndHint>>()),
            [], [], []);
    }

    private static AssetTransfer Transfer(Payment pay) => new(
        new ChainTransactionRef(Chain.Bitcoin, Tx(pay.Tx)),
        new AssetAmount(Asset.Bitcoin, pay.Satoshi),
        new BlockReference(900_000 + pay.Minute, BlockHash.Parse(Tx(1000 + pay.Minute)), Start.AddMinutes(pay.Minute)),
        null);

    private static GraphNode Node(string key, GraphNodeKind kind = GraphNodeKind.Cluster) => new(
        key, kind, [], 1, null, null, null, false, null, null, NodeExpansion.Expanded, [],
        new Dictionary<GraphLayout, NodePosition>());

    private sealed record Payment(string From, string To, long Satoshi, int Minute, int Tx);
}
