using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>
/// A made-up trace to draw (never real addresses or transactions): a payer W pays the start V (tx0), V pays the
/// cluster K (K1 and K2) and the unknown U (tx1), K pays the exchange E (tx2), and E pays a little back to K (tx3).
/// </summary>
internal static class GraphSamples
{
    /// <summary>The payer before the start.</summary>
    public static readonly BitcoinAddress W = Address(1);

    /// <summary>The start point.</summary>
    public static readonly BitcoinAddress V = Address(2);

    /// <summary>The cluster's first address.</summary>
    public static readonly BitcoinAddress K1 = Address(3);

    /// <summary>The cluster's second address.</summary>
    public static readonly BitcoinAddress K2 = Address(4);

    /// <summary>The unknown that gets the rest.</summary>
    public static readonly BitcoinAddress U = Address(5);

    /// <summary>The exchange.</summary>
    public static readonly BitcoinAddress E = Address(6);

    /// <summary>The transactions, 0 to 3.</summary>
    public static readonly Txid[] Tx = [.. Enumerable.Range(0, 4).Select(TxidOf)];

    /// <summary>The method of the made-up trace.</summary>
    public static readonly TracingMethodId Fifo = new(TracingMethodKind.Fifo, 1);

    /// <summary>The nodes' keys.</summary>
    public const string WKey = "address:w", VKey = "address:v", KKey = "cluster:k", UKey = "address:u", EKey = "address:e";

    /// <summary>The lines' keys.</summary>
    public const string WV = "w-v", VK = "v-k", VU = "v-u", KE = "k-e", EK = "e-k";

    /// <summary>A made-up address over one repeated byte.</summary>
    /// <param name="value">The byte.</param>
    /// <returns>The address.</returns>
    public static BitcoinAddress Address(int value) => BitcoinAddress.FromWitness(
        0, Enumerable.Repeat((byte)value, 20).ToArray(), BitcoinNetwork.Mainnet);

    /// <summary>A made-up txid from a number.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The txid.</returns>
    public static Txid TxidOf(int value) => Txid.Parse((value + 1).ToString("x64", CultureInfo.InvariantCulture));

    /// <summary>Bitcoin as satoshi.</summary>
    /// <param name="bitcoin">The amount in bitcoin.</param>
    /// <returns>The amount.</returns>
    public static AssetAmount Btc(decimal bitcoin) => Satoshi.FromBitcoin(bitcoin).ToAmount();

    /// <summary>A made-up block.</summary>
    /// <param name="height">Its height.</param>
    /// <returns>The block.</returns>
    public static BlockReference Block(long height) => new(
        height,
        BlockHash.Parse((height * 7).ToString("x64", CultureInfo.InvariantCulture)),
        new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero).AddHours(height - 100));

    /// <summary>Gives the graph with the start at V.</summary>
    /// <param name="vExpansion">How far V is expanded.</param>
    /// <returns>The graph.</returns>
    public static TraceGraph Graph(NodeExpansion vExpansion = NodeExpansion.Expanded)
    {
        var tx0 = Transaction(0, [(Address(9), 1.01m)], [(V, 1.0m)]);
        var tx1 = Transaction(1, [(V, 1.0m)], [(K1, 0.6m), (U, 0.39m)]);
        var tx2 = Transaction(2, [(K1, 0.6m)], [(E, 0.5m), (K2, 0.09m)]);
        var tx3 = Transaction(3, [(E, 0.03m)], [(K2, 0.02m)]);
        var transactions = new[] { tx0, tx1, tx2, tx3 }.ToDictionary(
            tx => tx.Txid, tx => new GraphTransaction(tx, LookupStatus.FromCase, null));
        var spends = new Dictionary<Outpoint, CoinSpend>
        {
            [new Outpoint(Tx[0], 0)] = Spent(1),
            [new Outpoint(Tx[1], 0)] = Spent(2),
            [new Outpoint(Tx[1], 1)] = new CoinSpend(SpendState.Unspent, null, false),
        };
        GraphNode[] nodes =
        [
            Node(WKey, GraphNodeKind.Address, [W], hop: -1),
            Node(VKey, GraphNodeKind.Address, [V], hop: 0, start: true, expansion: vExpansion),
            Node(KKey, GraphNodeKind.Cluster, [K1, K2], hop: 1, size: 2),
            Node(UKey, GraphNodeKind.Address, [U], hop: 1, expansion: NodeExpansion.NotExpanded),
            Node(EKey, GraphNodeKind.Address, [E], hop: 2),
        ];
        GraphEdge[] edges =
        [
            Edge(WV, WKey, VKey, 0, 0, 1.0m),
            Edge(VK, VKey, KKey, 1, 0, 0.6m),
            Edge(VU, VKey, UKey, 1, 1, 0.39m),
            Edge(KE, KKey, EKey, 2, 0, 0.5m),
            Edge(EK, EKey, KKey, 3, 0, 0.02m, EdgeDirection.Back),
        ];
        return new TraceGraph(
            [TraceStartPoint.ForAddress(V)], nodes, edges,
            new UtxoGraph(transactions, spends, new Dictionary<Txid, IReadOnlyList<TrailEndHint>>()), [], [], []);
    }

    /// <summary>Gives the made-up trace: all of V's coin, 0.6 to K and 0.5 on to E.</summary>
    /// <returns>The trace.</returns>
    public static TraceResult Trace() => new(
        Fifo,
        [new TraceSeed(new Outpoint(Tx[0], 0))],
        Btc(1.0m),
        [],
        [],
        [Traced(WV, 1.0m), Traced(VK, 0.6m), Traced(VU, 0.39m), Traced(KE, 0.5m)],
        []);

    /// <summary>Gives the scene of the made-up trace, with labels if any.</summary>
    /// <param name="labels">The labels by node key.</param>
    /// <param name="graph">The graph, or the default one.</param>
    /// <param name="trace">The trace, or the default one.</param>
    /// <returns>The scene.</returns>
    public static GraphScene Scene(
        IReadOnlyDictionary<string, IReadOnlyList<Label>>? labels = null,
        TraceGraph? graph = null,
        TraceResult? trace = null)
    {
        graph ??= Graph();
        return GraphScene.Build(new GraphSnapshot(
            graph,
            labels ?? new Dictionary<string, IReadOnlyList<Label>>(),
            TracingMethodKind.Fifo,
            SeedPicker.Pick(graph),
            trace ?? Trace(),
            null));
    }

    /// <summary>A label of a kind on an address, from one origin.</summary>
    /// <param name="address">The address.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="name">The name.</param>
    /// <param name="source">How the case got it.</param>
    /// <param name="who">Who set it.</param>
    /// <returns>The label.</returns>
    public static Label Label(
        BitcoinAddress address, LabelKind kind, string? name = null, LabelSource source = LabelSource.Own,
        string who = "Utredare")
    {
        var origin = new LabelOrigin(
            Guid.NewGuid().ToString("N"), source, source == LabelSource.OtherCase ? LabelSource.Own : source,
            "egen", "1", new OperatorProfile(who, "Analytiker", "Enheten"),
            new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero), source == LabelSource.OtherCase ? null : "Sett",
            null);
        var subject = LabelSubject.ForAddress(address);
        return new Label(subject, kind, name, [new LabelEntry(1, subject, kind, name, origin, false, 1)]);
    }

    /// <summary>A node.</summary>
    /// <param name="key">Its key.</param>
    /// <param name="kind">Its kind.</param>
    /// <param name="addresses">Its addresses.</param>
    /// <param name="hop">Its hop.</param>
    /// <param name="start">Whether a start point is here.</param>
    /// <param name="expansion">How far it is expanded.</param>
    /// <param name="size">The cluster's size.</param>
    /// <param name="ends">Why the trail ends here.</param>
    /// <param name="positions">Where the user placed it.</param>
    /// <returns>The node.</returns>
    public static GraphNode Node(
        string key,
        GraphNodeKind kind,
        IReadOnlyList<BitcoinAddress> addresses,
        int? hop,
        bool start = false,
        NodeExpansion expansion = NodeExpansion.Expanded,
        int size = 1,
        IReadOnlyList<TrailEndHint>? ends = null,
        IReadOnlyDictionary<GraphLayout, NodePosition>? positions = null) => new(
        key, kind, addresses, size, null, null, null, start, hop, null, expansion, ends ?? [],
        positions ?? new Dictionary<GraphLayout, NodePosition>());

    /// <summary>A line with one transfer of output <paramref name="index"/> of a transaction.</summary>
    /// <param name="key">Its key.</param>
    /// <param name="from">The sender's key.</param>
    /// <param name="to">The receiver's key.</param>
    /// <param name="tx">The transaction's number.</param>
    /// <param name="index">The output.</param>
    /// <param name="amount">The amount in bitcoin.</param>
    /// <param name="direction">Which way it runs.</param>
    /// <param name="confirmed">Whether the transaction is in a block.</param>
    /// <returns>The line.</returns>
    public static GraphEdge Edge(
        string key, string from, string to, int tx, int index, decimal amount,
        EdgeDirection direction = EdgeDirection.Forward, bool confirmed = true)
    {
        var block = confirmed ? Block(100 + tx) : null;
        return new GraphEdge(
            key, from, to, GraphEdgeKind.Transfer, Btc(amount),
            [AssetTransfer.OfCoin(Tx[tx], new Outpoint(Tx[tx], index), Satoshi.FromBitcoin(amount), block)],
            block?.Time,
            direction);
    }

    /// <summary>A traced line.</summary>
    /// <param name="key">The line's key.</param>
    /// <param name="amount">The traced amount in bitcoin.</param>
    /// <param name="outdated">Whether it may be outdated.</param>
    /// <returns>The traced line.</returns>
    public static TracedEdge Traced(string key, decimal amount, bool outdated = false) =>
        new(key, new TracedAmount(Btc(amount), Fifo, [Tx[0]]), outdated);

    /// <summary>A transaction with inputs and outputs as addresses and amounts, in block 100 + its number.</summary>
    /// <param name="number">Its number.</param>
    /// <param name="inputs">The inputs.</param>
    /// <param name="outputs">The outputs.</param>
    /// <returns>The transaction.</returns>
    public static BitcoinTransaction Transaction(
        int number, (BitcoinAddress Address, decimal Amount)[] inputs, (BitcoinAddress Address, decimal Amount)[] outputs)
    {
        var ins = inputs.Select((input, index) => new BitcoinInput(
            index, false, TxidOf(90 + number), index, input.Address, BitcoinScriptType.PayToWitnessPublicKeyHash,
            Satoshi.FromBitcoin(input.Amount), 0)).ToList();
        var outs = outputs.Select((output, index) => new BitcoinOutput(
            index, BitcoinScriptType.PayToWitnessPublicKeyHash, output.Address, Satoshi.FromBitcoin(output.Amount), "", null)).ToList();
        var fee = Satoshi.FromBitcoin(inputs.Sum(input => input.Amount) - outputs.Sum(output => output.Amount));
        return new BitcoinTransaction(TxidOf(number), Block(100 + number), ins, outs, fee, 200, 800, 2, 0);
    }

    /// <summary>A coin spent by input 0 of a transaction.</summary>
    /// <param name="tx">The spending transaction's number.</param>
    /// <returns>The spend.</returns>
    private static CoinSpend Spent(int tx) =>
        new(SpendState.Spent, new BitcoinSpend(Tx[tx], 0, Block(100 + tx)), false);
}
