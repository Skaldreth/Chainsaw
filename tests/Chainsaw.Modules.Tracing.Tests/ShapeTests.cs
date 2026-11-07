using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Tracing.Tests.Synthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>The graph's shape built straight from transactions: arcs, coins without addresses and coinbase.</summary>
public sealed class ShapeTests
{
    [Fact]
    public void Money_that_turns_back_runs_back_as_an_arc_and_is_traced_through_both_ways()
    {
        var out1 = Tx("out", 900_000).In(Wpkh("a"), 1_000_000).Out(Wpkh("b"), 990_000).Build();
        var back = Tx("back", 900_001).Spend(out1, 0).Out(Wpkh("a"), 980_000).Build();
        var graph = PureGraph.Of([out1, back], TraceStartPoint.ForAddress(Wpkh("a")));

        var forward = graph.Edges.Single(edge => edge.From == "address:" + Wpkh("a").Value);
        var turned = graph.Edges.Single(edge => edge.From == "address:" + Wpkh("b").Value);

        Assert.Equal(EdgeDirection.Forward, forward.Direction);
        Assert.Equal(EdgeDirection.Back, turned.Direction);
        var result = TraceCalculator.Trace(graph, new FifoMethod(), [new TraceSeed(new Outpoint(out1.Txid, 0))]);
        Assert.Equal(new Satoshi(980_000).ToAmount(), result.EndedBy(TrailEndReason.Unspent));
        Assert.Equal(new Satoshi(10_000).ToAmount(), result.EndedBy(TrailEndReason.MinerFee));
    }

    [Fact]
    public void Change_to_the_same_actor_is_no_edge()
    {
        var pay = Tx("pay").In(Wpkh("a"), 1_000_000).Out(Wpkh("b"), 500_000).Out(Wpkh("a"), 490_000).Build();

        var graph = PureGraph.Of([pay]);

        Assert.Single(graph.Edges);
    }

    [Fact]
    public void An_op_return_with_an_amount_can_never_be_spent()
    {
        var burn = Tx("burn").In(Wpkh("a"), 1_000_000).Out(Wpkh("b"), 400_000).Build();
        var withBurn = burn with
        {
            Outputs =
            [
                .. burn.Outputs,
                new BitcoinOutput(1, BitcoinScriptType.OpReturn, null, new Satoshi(590_000), "6a", null),
            ],
            Fee = new Satoshi(10_000),
        };
        var graph = PureGraph.Of([withBurn]);

        var result = TraceCalculator.Trace(graph, new FifoMethod(), [new TraceSeed(new Outpoint(withBurn.Txid, 1))]);

        Assert.Equal(new Satoshi(590_000).ToAmount(), result.EndedBy(TrailEndReason.Unspendable));
        Assert.Equal(GraphNodeKind.Coin, graph.Node("coin:" + new Outpoint(withBurn.Txid, 1))!.Kind);
    }

    [Fact]
    public void A_coin_whose_spending_is_unknown_is_not_expanded_yet()
    {
        var pay = Tx("pay").In(Wpkh("a"), 1_000_000).Out(Wpkh("b"), 990_000).Build();
        var data = PureGraph.Of([pay]);
        var unknown = data with { Spends = new Dictionary<Outpoint, CoinSpend>() };

        var result = TraceCalculator.Trace(unknown, new HaircutMethod(), [new TraceSeed(new Outpoint(pay.Txid, 0))]);

        var end = Assert.Single(result.Ends);
        Assert.Equal(TrailEndReason.NotExpanded, end.Reason);
        Assert.Null(end.Transaction);
    }

    [Fact]
    public void A_method_that_creates_money_is_refused()
    {
        var pay = Tx("pay").In(Wpkh("a"), 1_000_000).Out(Wpkh("b"), 990_000).Build();
        var next = Tx("next", 900_001).Spend(pay, 0).Out(Wpkh("c"), 980_000).Build();
        var graph = PureGraph.Of([pay, next]);

        Assert.Throws<InvalidOperationException>(() => TraceCalculator.Trace(
            graph, new GenerousMethod(), [new TraceSeed(new Outpoint(pay.Txid, 0), new Satoshi(1_000).ToAmount())]));
    }

    [Fact]
    public void A_hint_cannot_claim_the_trace_s_own_reasons()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrailEndHint(TrailEndReason.NotExpanded, "x", "y"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrailEndHint(TrailEndReason.Unspent, "x", "y"));
        Assert.True(TrailEndReason.Exchange.EndsTrail());
        Assert.False(TrailEndReason.Unspent.EndsTrail());
    }

    /// <summary>A broken method that gives every output its whole amount.</summary>
    private sealed class GenerousMethod : IUtxoTracingMethod
    {
        public TracingMethodId Id { get; } = new(TracingMethodKind.Fifo, 99);

        public ChainScope Scope { get; } = ChainScope.AllOf(LedgerModel.Utxo);

        public TransactionTaint Propagate(UtxoFlow flow, IReadOnlyList<TaintSet> inputs) =>
            new([.. flow.Outputs.Select(TaintSet.FirstPart)], 0);
    }
}
