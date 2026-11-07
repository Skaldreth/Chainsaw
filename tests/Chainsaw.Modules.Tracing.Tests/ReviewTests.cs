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

    [Fact]
    public void A_replaced_unconfirmed_spender_gives_way_to_the_confirmed_one()
    {
        var pay = Tx("pay").In(Wpkh("a"), 1_000_000).Out(Wpkh("b"), 990_000).Build();
        var stale = Tx("stale", null).Spend(pay, 0).Out(Wpkh("c"), 985_000).Build();
        var real = Tx("real", 900_001).Spend(pay, 0).Out(Wpkh("d"), 980_000).Build();

        var graph = PureGraph.Of([pay, stale, real]);
        var result = TraceCalculator.Trace(graph, new FifoMethod(), [new TraceSeed(new Outpoint(pay.Txid, 0))]);

        Assert.Equal(real.Txid, graph.Spends[new Outpoint(pay.Txid, 0)].SpentBy!.Txid);
        Assert.False(graph.Spends[new Outpoint(pay.Txid, 0)].MayBeOutdated);
        Assert.Equal(real.Txid, Assert.Single(result.Transactions).Txid);
    }

    [Fact]
    public void What_rests_on_an_unconfirmed_transaction_says_it_may_change()
    {
        var pay = Tx("pay").In(Wpkh("a"), 1_000_000).Out(Wpkh("b"), 990_000).Build();
        var pending = Tx("pending", null).Spend(pay, 0).Out(Wpkh("c"), 980_000).Build();
        var graph = PureGraph.Of([pay, pending]);

        var result = TraceCalculator.Trace(graph, new FifoMethod(), [new TraceSeed(new Outpoint(pay.Txid, 0))]);

        Assert.All(result.Ends, end => Assert.True(end.MayBeOutdated));
        Assert.True(Assert.Single(result.Coins, coin => coin.Coin.Txid == pending.Txid).MayBeOutdated);
        Assert.True(Assert.Single(result.Transactions).MayBeOutdated);
        Assert.All(result.Edges.Where(edge => edge.EdgeKey.Contains(Wpkh("c").Value, StringComparison.Ordinal)),
            edge => Assert.True(edge.MayBeOutdated));
    }

    [Fact]
    public void The_line_into_a_transaction_carries_nothing_when_the_spend_record_conflicts()
    {
        var pay = Tx("pay").In(Wpkh("a"), 1_000_000).Out(Wpkh("b"), 990_000).Build();
        var other = Tx("other", 900_001).In(Wpkh("c"), 500_000).In(Wpkh("e"), 400_000).Out(Wpkh("d"), 890_000).Build();
        var graph = PureGraph.Of([pay, other]);
        var coin = new Outpoint(pay.Txid, 0);
        var wrong = graph with
        {
            Spends = new Dictionary<Outpoint, CoinSpend>(graph.Spends)
            {
                [coin] = new CoinSpend(SpendState.Spent, new BitcoinSpend(other.Txid, 0, other.Block), false),
            },
        };

        var result = TraceCalculator.Trace(wrong, new FifoMethod(), [new TraceSeed(coin)]);

        Assert.DoesNotContain(result.Edges, edge => edge.EdgeKey.Contains("tx:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_transaction_expansion_asks_for_its_spends_once()
    {
        _harness.Source.Add([.. _flows.All]);
        var before = _harness.Source.SpendLookups;

        await _harness.Expand(_flows.Peel1);

        // Peel 1 itself, and the two transactions it brought in: Onward and Peel 2.
        Assert.Equal(before + 3, _harness.Source.SpendLookups);
    }

    [Fact]
    public async Task Taking_a_link_back_seals_the_stored_link_whatever_the_copy_says()
    {
        await _flows.ExpandInto(_harness);
        var ethereum = new ChainTransactionRef(Chain.Ethereum, "0x" + new string('c', 64));
        var link = _harness.Service.LinkCrossChain(_harness.Case, new Outpoint(_flows.Peel3.Txid, 1), ethereum, "Ja.");
        var forged = link with { From = new Outpoint(_flows.Pay.Txid, 0) };

        Assert.True(_harness.Service.UnlinkCrossChain(_harness.Case, forged, "Fel."));

        var journal = _harness.Case.ReadJournal().Last();
        Assert.Contains(_flows.Peel3.Txid.Value, journal.Summary, StringComparison.Ordinal);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void An_end_at_a_transaction_that_may_be_replaced_says_it_may_change()
    {
        var pay = Tx("pay").In(Wpkh("a"), 1_000_000).Out(Wpkh("b"), 990_000).Build();
        var pending = Tx("pending-mix", null).Spend(pay, 0).In(Wpkh("x"), 990_000)
            .Out(Wpkh("m1"), 985_000).Out(Wpkh("m2"), 985_000).Build();
        var graph = PureGraph.Of([pay, pending]);
        var hint = new TrailEndHint(TrailEndReason.CoinJoin, "coinjoin v1", "Känd CoinJoin.");
        var marked = graph with
        {
            TransactionEnds = new Dictionary<Txid, IReadOnlyList<TrailEndHint>> { [pending.Txid] = [hint] },
        };

        var end = Assert.Single(
            TraceCalculator.Trace(marked, new FifoMethod(), [new TraceSeed(new Outpoint(pay.Txid, 0))]).Ends);

        Assert.Equal(TrailEndReason.CoinJoin, end.Reason);
        Assert.True(end.MayBeOutdated);
    }

    [Fact]
    public async Task A_coin_continues_on_one_other_chain_at_a_time()
    {
        await _flows.ExpandInto(_harness);
        var coin = new Outpoint(_flows.Peel3.Txid, 1);
        var first = new ChainTransactionRef(Chain.Ethereum, "0x" + new string('d', 64));
        var second = new ChainTransactionRef(Chain.Tron, new string('e', 64));
        var link = _harness.Service.LinkCrossChain(_harness.Case, coin, first, "Först.");

        Assert.Throws<ArgumentException>(() => _harness.Service.LinkCrossChain(_harness.Case, coin, second, "Också."));

        _harness.Service.UnlinkCrossChain(_harness.Case, link, "Fel kedja.");
        _harness.Service.LinkCrossChain(_harness.Case, coin, second, "Rätt.");
        var graph = await _harness.Load();
        var edge = Assert.Single(graph.Edges, edge => edge.Kind == GraphEdgeKind.ContinuesOnOtherChain);
        Assert.Equal(new Satoshi(118_945_000), edge.Amount);
    }

    [Fact]
    public async Task A_transaction_expansion_answered_from_the_case_is_not_complete()
    {
        await _flows.ExpandInto(_harness);
        _harness.Source.Online = false;

        var result = await _harness.Expand(_flows.Peel3);

        Assert.Equal(ExpansionOutcome.Partly, result.Outcome);
        Assert.NotNull(result.Problem);
    }
}
