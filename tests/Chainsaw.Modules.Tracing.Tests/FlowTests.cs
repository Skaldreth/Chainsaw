using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Tracing.Tests.Synthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// The sketches' made-up case end to end: victim → collection → peel chain → exchange, CoinJoin and what is kept,
/// expanded hop by hop in a real case and traced by FIFO and haircut.
/// </summary>
public sealed class FlowTests : IDisposable
{
    private readonly CaseHarness _harness = new();
    private readonly Flows _flows = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Fifo_follows_the_victims_money_to_every_end_with_its_reason()
    {
        var graph = await Expanded();

        var result = Trace(graph, TracingMethodKind.Fifo);

        Assert.Equal(new TracingMethodId(TracingMethodKind.Fifo, 1), result.Method);
        Assert.Equal(Btc(100_000_000), result.Traced);
        Assert.Equal(Btc(20_000_000), result.EndedBy(TrailEndReason.Exchange));
        Assert.Equal(Btc(1_005_000), result.EndedBy(TrailEndReason.CoinJoin));
        Assert.Equal(Btc(68_995_000), result.EndedBy(TrailEndReason.Unspent));
        Assert.Equal(Btc(9_990_000), result.EndedBy(TrailEndReason.NotExpanded));
        Assert.Equal(Btc(10_000), result.EndedBy(TrailEndReason.MinerFee));
        Assert.Equal(result.Traced, Sum(result.Ends));
    }

    [Fact]
    public async Task Each_end_names_where_it_is_and_what_it_rests_on()
    {
        var graph = await Expanded();

        var ends = Trace(graph, TracingMethodKind.Fifo).Ends;

        var exchange = Assert.Single(ends, end => end.Reason == TrailEndReason.Exchange);
        Assert.Equal(new Outpoint(_flows.Peel2.Txid, 0), exchange.Coin);
        Assert.Equal("test-labels v1", Assert.Single(exchange.Basis).Source);
        var coinJoin = Assert.Single(ends, end => end.Reason == TrailEndReason.CoinJoin);
        Assert.Equal(_flows.Mix.Txid, coinJoin.Transaction);
        Assert.Equal("tx:" + _flows.Mix.Txid.Value, coinJoin.NodeKey);
        Assert.Equal("coinjoin v1", Assert.Single(coinJoin.Basis).Source);
        var notExpanded = Assert.Single(ends, end => end.Reason == TrailEndReason.NotExpanded);
        Assert.Equal(_flows.Further.Txid, notExpanded.Transaction);
        Assert.False(notExpanded.Reason.EndsTrail());
        var fee = Assert.Single(ends, end => end.Reason == TrailEndReason.MinerFee);
        Assert.Equal(_flows.Onward.Txid, fee.Transaction);
        Assert.All(ends, end => Assert.False(string.IsNullOrWhiteSpace(end.Explanation)));
    }

    [Fact]
    public async Task The_exchange_ends_the_trail_although_its_sweep_is_in_the_graph()
    {
        var graph = await Expanded();
        Assert.True(graph.Transactions.ContainsKey(_flows.Sweep.Txid));

        var result = Trace(graph, TracingMethodKind.Fifo);

        Assert.DoesNotContain(result.Transactions, tx => tx.Txid == _flows.Sweep.Txid);
        Assert.DoesNotContain(result.Coins, coin => coin.Coin.Txid == _flows.Sweep.Txid);
    }

    [Fact]
    public async Task Haircut_spreads_the_money_and_still_adds_up()
    {
        var graph = await Expanded();

        var result = Trace(graph, TracingMethodKind.Haircut);

        Assert.Equal(new TracingMethodId(TracingMethodKind.Haircut, 1), result.Method);
        Assert.Equal(result.Traced, Sum(result.Ends));

        // Two thirds of the collection is the victim's (100 of 150 BTC-hundredths): the collector gets two thirds of
        // 149 980 000, rounded to the satoshi, and the fee two thirds of 20 000.
        var collect = Assert.Single(result.Transactions, tx => tx.Txid == _flows.Collect.Txid);
        Assert.Equal(Btc(99_986_667), collect.Outputs[0]);
        Assert.Equal(Btc(13_333), collect.Fee);
        Assert.True(result.EndedBy(TrailEndReason.MinerFee).CompareTo(Btc(10_000)) > 0);
    }

    [Fact]
    public async Task A_traced_amount_is_a_conclusion_that_names_its_method_and_transactions()
    {
        var graph = await Expanded();

        var result = Trace(graph, TracingMethodKind.Fifo);

        var kept = Assert.Single(result.Ends, end => end.Reason == TrailEndReason.Unspent).Amount;
        Assert.Equal(new TracingMethodId(TracingMethodKind.Fifo, 1), kept.Method);
        Assert.Equal(
            new[] { _flows.Collect, _flows.Peel1, _flows.Peel2, _flows.Peel3 }.Select(tx => tx.Txid.Value).Order(),
            kept.Through.Select(txid => txid.Value));
        var explanation = kept.Explain();
        Assert.Contains("FIFO v1", explanation, StringComparison.Ordinal);
        Assert.Contains("bedömning", explanation, StringComparison.Ordinal);
        Assert.Contains(_flows.Peel3.Txid.Value, explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_part_of_a_coin_can_be_traced()
    {
        var graph = await Expanded();

        var result = _harness.Service.Trace(graph, new TraceRequest(
            TracingMethodKind.Fifo, [new TraceSeed(new Outpoint(_flows.Pay.Txid, 0), Btc(30_000_000))]));

        // The first 30 000 000: 10 000 000 peeled to the unknown receiver, 20 000 000 to the exchange, none kept.
        Assert.Equal(Btc(20_000_000), result.EndedBy(TrailEndReason.Exchange));
        Assert.Equal(Btc(0), result.EndedBy(TrailEndReason.Unspent));
        Assert.Equal(Btc(30_000_000), Sum(result.Ends));
    }

    [Fact]
    public async Task Clusters_from_clustering_become_one_actor()
    {
        var graph = await Expanded();

        var scammers = graph.NodeOf(Wpkh("scammer-1"));

        Assert.NotNull(scammers);
        Assert.Equal(GraphNodeKind.Cluster, scammers.Kind);
        Assert.Same(scammers, graph.NodeOf(Wpkh("scammer-2")));
        Assert.Equal(GraphNodeKind.Address, graph.NodeOf(Wpkh("collector"))!.Kind);
    }

    [Fact]
    public async Task A_transaction_with_several_owners_is_a_node_of_its_own_and_ends_the_trail_as_coinjoin()
    {
        var graph = await Expanded();

        var mix = graph.Node("tx:" + _flows.Mix.Txid.Value);

        Assert.NotNull(mix);
        Assert.Equal(GraphNodeKind.Transaction, mix.Kind);
        Assert.Equal(TrailEndReason.CoinJoin, Assert.Single(mix.Ends).Reason);
        Assert.Equal(5, graph.Edges.Count(edge => edge.From == mix.Key));
    }

    [Fact]
    public async Task Every_node_knows_its_hop_and_when_the_money_first_came()
    {
        var graph = await Expanded();

        Assert.Equal(0, graph.NodeOf(Wpkh("victim"))!.Hop);
        Assert.True(graph.NodeOf(Wpkh("victim"))!.IsStartPoint);
        Assert.Equal(1, graph.NodeOf(Wpkh("scammer-1"))!.Hop);
        Assert.Equal(2, graph.NodeOf(Wpkh("collector"))!.Hop);
        Assert.Equal(3, graph.NodeOf(Wpkh("peel-1-rest"))!.Hop);
        Assert.Equal(4, graph.NodeOf(Wpkh("exchange-deposit"))!.Hop);
        Assert.Equal(5, graph.NodeOf(Wpkh("kept"))!.Hop);
        Assert.Equal(Block(900_010).Time, graph.NodeOf(Wpkh("collector"))!.FirstReceived);
        Assert.Null(graph.NodeOf(Wpkh("victim"))!.FirstReceived);

        // The other customer only pays into the exchange's sweep: one hop before the exchange.
        Assert.Equal(4, graph.NodeOf(Wpkh("other-customer"))!.Hop);
        Assert.Equal(5, graph.NodeOf(Pkh("exchange-hot"))!.Hop);
    }

    [Fact]
    public async Task Edges_sum_the_transfers_and_carry_the_traced_amount_beside_the_fact()
    {
        var graph = await Expanded();
        var result = Trace(graph, TracingMethodKind.Fifo);

        var peel = graph.Edges.Single(edge =>
            edge.From == graph.NodeOf(Wpkh("collector"))!.Key && edge.To == graph.NodeOf(Wpkh("peel-1-rest"))!.Key);

        Assert.Equal(Btc(139_970_000), peel.Amount);
        Assert.Equal(EdgeDirection.Forward, peel.Direction);
        Assert.Equal(Btc(90_000_000), result.Edges.Single(edge => edge.EdgeKey == peel.Key).Amount.Amount);
    }

    [Fact]
    public async Task Nodes_say_how_far_they_are_expanded()
    {
        await _flows.ExpandInto(_harness);
        await _harness.Expand(Wpkh("kept"));

        var graph = await _harness.Load();

        Assert.Equal(NodeExpansion.Expanded, graph.NodeOf(Wpkh("kept"))!.Expansion);
        Assert.Equal(NodeExpansion.NotExpanded, graph.NodeOf(Wpkh("somewhere"))!.Expansion);
    }

    [Fact]
    public async Task An_address_expansion_stops_at_the_limit_and_says_so()
    {
        _harness.Source.Add([.. _flows.All]);

        var result = await _harness.Expand(Wpkh("collector"), maximum: 1);

        Assert.Equal(ExpansionOutcome.Partly, result.Outcome);
        Assert.Single(result.NewTransactions);
        Assert.NotNull(result.Problem);
        Assert.Equal(NodeExpansion.Partly, result.Graph.NodeOf(Wpkh("collector"))!.Expansion);
    }

    [Fact]
    public async Task An_address_expansion_pages_through_the_whole_history()
    {
        var spender = Tx("spender", 900_002).Spend(_flows.Pay, 1).Out(Wpkh("victim-change"), 19_980_000).Build();
        var again = Tx("again", 900_003).Spend(spender, 0).Out(Wpkh("victim-change"), 19_970_000).Build();
        var more = Tx("more", 900_004).Spend(again, 0).Out(Wpkh("victim-change"), 19_960_000).Build();
        _harness.Source.Add(_flows.Pay, spender, again, more);

        var result = await _harness.Expand(Wpkh("victim-change"));

        Assert.Equal(ExpansionOutcome.Expanded, result.Outcome);
        Assert.Equal(4, result.NewTransactions.Count);
    }

    [Fact]
    public async Task Seeds_that_are_not_valid_are_refused()
    {
        var graph = await Expanded();
        var pay = new Outpoint(_flows.Pay.Txid, 0);

        Assert.Throws<ArgumentException>(() => Request(graph, []));
        Assert.Throws<ArgumentException>(() => Request(graph, [new TraceSeed(pay), new TraceSeed(pay)]));
        Assert.Throws<ArgumentException>(() => Request(graph, [new TraceSeed(pay, Btc(100_000_001))]));
        Assert.Throws<ArgumentException>(() => Request(graph, [new TraceSeed(new Outpoint(Txid("nowhere"), 0))]));
        Assert.Throws<ArgumentException>(() => Request(graph,
            [new TraceSeed(pay), new TraceSeed(new Outpoint(_flows.Collect.Txid, 0))]));
    }

    private void Request(TraceGraph graph, IReadOnlyList<TraceSeed> seeds) =>
        _harness.Service.Trace(graph, new TraceRequest(TracingMethodKind.Fifo, seeds));

    private async Task<TraceGraph> Expanded()
    {
        await _flows.ExpandInto(_harness);
        return await _harness.Load();
    }

    private TraceResult Trace(TraceGraph graph, TracingMethodKind method) => _harness.Service.Trace(
        graph, new TraceRequest(method, [new TraceSeed(new Outpoint(_flows.Pay.Txid, 0))]));

    private static AssetAmount Btc(long satoshi) => new Satoshi(satoshi).ToAmount();

    private static AssetAmount Sum(IEnumerable<TraceEnd> ends) =>
        ends.Aggregate(AssetAmount.Zero(Asset.Bitcoin), (sum, end) => sum + end.Amount.Amount);
}
