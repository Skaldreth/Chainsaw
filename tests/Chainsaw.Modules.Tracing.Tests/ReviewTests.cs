using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Tracing.Tests.Synthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>Regression tests for what the independent review of the stage found.</summary>
public sealed class ReviewTests : IDisposable
{
    private readonly CaseHarness _harness = new();
    private readonly Flows _flows = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Expanding_an_address_again_with_a_lower_limit_takes_nothing_out_of_the_graph()
    {
        var spender = Tx("spender", 900_002).Spend(_flows.Pay, 1).Out(Wpkh("victim-change"), 19_980_000).Build();
        var again = Tx("again", 900_003).Spend(spender, 0).Out(Wpkh("victim-change"), 19_970_000).Build();
        _harness.Source.Add(_flows.Pay, spender, again);
        await _harness.Expand(Wpkh("victim-change"));

        var result = await _harness.Expand(Wpkh("victim-change"), maximum: 1);

        Assert.Equal(3, result.Graph.Transactions.Count);
        Assert.Equal(NodeExpansion.Partly, result.Graph.NodeOf(Wpkh("victim-change"))!.Expansion);
    }

    [Fact]
    public async Task Showing_the_graph_again_never_asks_the_source()
    {
        await _flows.ExpandInto(_harness);
        var asked = _harness.Source.Lookups;

        await _harness.Load();
        await _harness.Load();

        Assert.Equal(asked, _harness.Source.Lookups);
    }

    [Fact]
    public async Task The_line_into_a_coinjoin_carries_the_amount_that_ends_there()
    {
        await _flows.ExpandInto(_harness);
        var graph = await _harness.Load();
        var mix = graph.Node("tx:" + _flows.Mix.Txid.Value)!;

        foreach (var method in new[] { TracingMethodKind.Fifo, TracingMethodKind.Haircut })
        {
            var result = _harness.Service.Trace(graph, new TraceRequest(
                method, [new TraceSeed(new Outpoint(_flows.Pay.Txid, 0))]));
            var intoMix = graph.Edges.Where(edge => edge.To == mix.Key)
                .Select(edge => result.Edges.SingleOrDefault(traced => traced.EdgeKey == edge.Key))
                .OfType<TracedEdge>()
                .Aggregate(Satoshi.Zero, (sum, edge) => sum + edge.Amount.Amount);

            Assert.Equal(result.EndedBy(TrailEndReason.CoinJoin), intoMix);
            Assert.NotEqual(Satoshi.Zero, intoMix);
        }
    }

    [Fact]
    public void A_spend_record_that_does_not_match_its_transaction_is_its_own_reason()
    {
        var pay = Tx("pay").In(Wpkh("a"), 1_000_000).Out(Wpkh("b"), 990_000).Build();
        var other = Tx("other", 900_001).In(Wpkh("c"), 500_000).Out(Wpkh("d"), 490_000).Build();
        var graph = PureGraph.Of([pay, other]);
        var coin = new Outpoint(pay.Txid, 0);
        var wrong = graph with
        {
            Spends = new Dictionary<Outpoint, CoinSpend>(graph.Spends)
            {
                [coin] = new CoinSpend(SpendState.Spent, new BitcoinSpend(other.Txid, 0, other.Block), false),
            },
        };

        var end = Assert.Single(TraceCalculator.Trace(wrong, new FifoMethod(), [new TraceSeed(coin)]).Ends);

        Assert.Equal(TrailEndReason.Conflicting, end.Reason);
        Assert.False(end.Reason.EndsTrail());
        Assert.Equal(other.Txid, end.Transaction);
    }

    [Fact]
    public void A_spend_by_an_unconfirmed_transaction_may_change()
    {
        var pay = Tx("pay").In(Wpkh("a"), 1_000_000).Out(Wpkh("b"), 990_000).Build();
        var pending = Tx("pending", null).Spend(pay, 0).Out(Wpkh("c"), 980_000).Build();

        var graph = PureGraph.Of([pay, pending]);

        Assert.True(graph.Spends[new Outpoint(pay.Txid, 0)].MayBeOutdated);
    }

    [Fact]
    public void A_copy_with_other_nodes_answers_from_its_own_nodes()
    {
        var pay = Tx("pay").In(Wpkh("a"), 1_000_000).Out(Wpkh("b"), 990_000).Build();
        var graph = PureGraph.Of([pay]);
        Assert.NotNull(graph.NodeOf(Wpkh("b")));

        var empty = graph with { Nodes = [] };

        Assert.Null(empty.NodeOf(Wpkh("b")));
        Assert.Null(empty.Node("address:" + Wpkh("a").Value));
    }

    [Fact]
    public async Task A_source_that_does_not_move_on_is_not_asked_for_ever()
    {
        var spender = Tx("spender", 900_002).Spend(_flows.Pay, 1).Out(Wpkh("victim-change"), 19_980_000).Build();
        _harness.Source.Add(_flows.Pay, spender);
        _harness.Source.RepeatPages = true;

        var result = await _harness.Expand(Wpkh("victim-change"));

        Assert.Equal(ExpansionOutcome.Partly, result.Outcome);
        Assert.Equal(2, result.NewTransactions.Count);
    }

    [Fact]
    public async Task An_address_without_history_is_expanded_with_nothing_in_it()
    {
        var result = await _harness.Expand(Wpkh("never-used"));

        Assert.Equal(ExpansionOutcome.Expanded, result.Outcome);
        Assert.Empty(result.NewTransactions);
        Assert.Single(result.Graph.Expansions);
    }

    [Fact]
    public void A_coinbase_start_starts_at_the_actors_it_pays()
    {
        var coinbase = new BitcoinTransaction(
            Txid("coinbase"),
            Block(900_000),
            [new BitcoinInput(0, true, null, null, null, null, null, 0xFFFFFFFF)],
            [
                new BitcoinOutput(
                    0, BitcoinScriptType.PayToWitnessPublicKeyHash, Wpkh("miner"), new Satoshi(312_500_000), "", null),
            ],
            Satoshi.Zero,
            200,
            800,
            2,
            0);

        var graph = PureGraph.Of([coinbase], TraceStartPoint.ForTransaction(coinbase.Txid));

        Assert.Empty(graph.Problems);
        Assert.True(graph.NodeOf(Wpkh("miner"))!.IsStartPoint);
    }
}
