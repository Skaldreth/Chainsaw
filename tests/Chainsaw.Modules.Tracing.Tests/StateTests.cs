using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Tracing.Tests.Synthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>The graph's state in the case: start points, places, links to other chains, and offline.</summary>
public sealed class StateTests : IDisposable
{
    private static readonly ChainTransactionRef EthereumTx = new(Chain.Ethereum, "0x" + new string('a', 64));

    private readonly CaseHarness _harness = new();
    private readonly Flows _flows = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_start_point_is_shown_at_once_and_written_in_the_journal()
    {
        var start = TraceStartPoint.ForAddress(Wpkh("scammer-1"));

        Assert.True(_harness.Service.AddStartPoint(_harness.Case, start));
        Assert.False(_harness.Service.AddStartPoint(_harness.Case, start));

        var graph = await _harness.Load();
        var node = Assert.Single(graph.Nodes);
        Assert.True(node.IsStartPoint);
        Assert.Equal(0, node.Hop);
        Assert.Equal(NodeExpansion.NotExpanded, node.Expansion);
        Assert.Contains(_harness.Case.ReadJournal(), entry => entry.Kind == TracingJournalKinds.StartPointAdded);
        Assert.Equal(0, _harness.Source.Lookups);
    }

    [Fact]
    public async Task A_start_point_can_be_removed_with_a_line_in_the_journal()
    {
        var start = TraceStartPoint.ForAddress(Wpkh("scammer-1"));
        _harness.Service.AddStartPoint(_harness.Case, start);

        Assert.True(_harness.Service.RemoveStartPoint(_harness.Case, start));
        Assert.False(_harness.Service.RemoveStartPoint(_harness.Case, start));

        Assert.Empty((await _harness.Load()).Nodes);
        Assert.Contains(_harness.Case.ReadJournal(), entry => entry.Kind == TracingJournalKinds.StartPointRemoved);
    }

    [Fact]
    public async Task Places_are_kept_per_layout_and_survive_reopening()
    {
        await _flows.ExpandInto(_harness);
        var graph = await _harness.Load();
        var collector = graph.NodeOf(Wpkh("collector"))!;
        var scammers = graph.NodeOf(Wpkh("scammer-1"))!;
        var journalLength = _harness.Case.ReadJournal().Count;

        _harness.Service.SavePosition(_harness.Case, collector.Key, GraphLayout.Free, new NodePosition(10, 20));
        _harness.Service.SavePosition(_harness.Case, collector.Key, GraphLayout.HopColumns, new NodePosition(2, -5));
        _harness.Service.SavePosition(_harness.Case, scammers.Key, GraphLayout.Free, new NodePosition(-3, 4));
        _harness.Reopen();
        _harness.Source.Online = false;
        graph = await _harness.Load();

        Assert.Equal(new NodePosition(10, 20), graph.NodeOf(Wpkh("collector"))!.Positions[GraphLayout.Free]);
        Assert.Equal(new NodePosition(2, -5), graph.NodeOf(Wpkh("collector"))!.Positions[GraphLayout.HopColumns]);
        Assert.Equal(new NodePosition(-3, 4), graph.NodeOf(Wpkh("scammer-2"))!.Positions[GraphLayout.Free]);
        Assert.Equal(journalLength, _harness.Case.ReadJournal().Count);
    }

    [Fact]
    public void A_place_must_be_finite()
    {
        Assert.Throws<ArgumentException>(() => _harness.Service.SavePosition(
            _harness.Case, "address:x", GraphLayout.Free, new NodePosition(double.NaN, 0)));
    }

    [Fact]
    public async Task A_link_to_another_chain_ends_the_trail_there_with_the_continuation()
    {
        await _flows.ExpandInto(_harness);
        var kept = new Outpoint(_flows.Peel3.Txid, 1);

        var link = _harness.Service.LinkCrossChain(
            _harness.Case, kept, EthereumTx, "Swap-tjänsten visar samma belopp.");
        var graph = await _harness.Load();
        var result = Trace(graph);

        var end = Assert.Single(result.Ends, end => end.Reason == TrailEndReason.OtherChain);
        Assert.Equal(new Satoshi(68_995_000), end.Amount.Amount);
        Assert.Equal(link, Assert.Single(end.Continuations));
        var other = graph.Node("chain:" + EthereumTx);
        Assert.NotNull(other);
        Assert.Equal(GraphNodeKind.OtherChain, other.Kind);
        var edge = Assert.Single(graph.Edges, edge => edge.Kind == GraphEdgeKind.ContinuesOnOtherChain);
        Assert.Equal(other.Key, edge.To);
        Assert.Equal(new Satoshi(68_995_000), result.Edges.Single(traced => traced.EdgeKey == edge.Key).Amount.Amount);
        Assert.Contains(_harness.Case.ReadJournal(), entry => entry.Kind == TracingJournalKinds.CrossChainLinked);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task A_link_taken_back_stays_sealed_and_the_money_is_where_it_was()
    {
        await _flows.ExpandInto(_harness);
        var link = _harness.Service.LinkCrossChain(
            _harness.Case, new Outpoint(_flows.Peel3.Txid, 1), EthereumTx, "Troligt.");

        Assert.True(_harness.Service.UnlinkCrossChain(_harness.Case, link, "Fel tx."));
        Assert.False(_harness.Service.UnlinkCrossChain(_harness.Case, link, "Igen."));

        var graph = await _harness.Load();
        Assert.Empty(graph.CrossChainLinks);
        Assert.Equal(new Satoshi(68_995_000), Trace(graph).EndedBy(TrailEndReason.Unspent));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_link_needs_another_chain_and_a_reason()
    {
        var coin = new Outpoint(_flows.Pay.Txid, 0);

        Assert.Throws<ArgumentException>(() => _harness.Service.LinkCrossChain(
            _harness.Case, coin, new ChainTransactionRef(Chain.Bitcoin, new string('b', 64)), "Skäl."));
        Assert.Throws<ArgumentException>(() => _harness.Service.LinkCrossChain(_harness.Case, coin, EthereumTx, " "));
        Assert.Throws<ArgumentException>(() => new ChainTransactionRef(Chain.Ethereum, new string('a', 64)));
        Assert.Throws<ArgumentException>(() => new ChainTransactionRef(Chain.Tron, "0x" + new string('a', 64)));
    }

    [Fact]
    public async Task A_bridge_without_a_link_ends_as_another_chain_without_continuation()
    {
        await _flows.ExpandInto(_harness);
        _harness.Labels.Addresses[Wpkh("kept")] = TrailEndReason.OtherChain;

        var end = Assert.Single(Trace(await _harness.Load()).Ends, end => end.Reason == TrailEndReason.OtherChain);

        Assert.Empty(end.Continuations);
        Assert.Equal(TrailEndReason.OtherChain, Assert.Single(end.Basis).Reason);
    }

    [Fact]
    public async Task Offline_the_graph_comes_from_the_case_and_what_is_missing_is_not_expanded_yet()
    {
        await _flows.ExpandInto(_harness);
        _harness.Source.Online = false;

        var graph = await _harness.Load();
        var result = Trace(graph);

        Assert.Equal(
            new Satoshi(100_000_000), result.Ends.Aggregate(Satoshi.Zero, (sum, end) => sum + end.Amount.Amount));
        var notExpanded = Assert.Single(result.Ends, end => end.Reason == TrailEndReason.NotExpanded);
        Assert.Equal(_flows.Further.Txid, notExpanded.Transaction);
        var kept = Assert.Single(result.Ends, end => end.Reason == TrailEndReason.Unspent);
        Assert.True(kept.MayBeOutdated);
        Assert.Contains("kan ha flyttats", kept.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Offline_an_expansion_of_something_new_changes_nothing_and_says_why()
    {
        await _flows.ExpandInto(_harness);
        _harness.Source.Online = false;

        var result = await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForTransaction(_flows.Further.Txid));

        Assert.Equal(ExpansionOutcome.NotExpanded, result.Outcome);
        Assert.Empty(result.NewTransactions);
        Assert.Contains("Offline", result.Problem, StringComparison.Ordinal);
        Assert.False(result.Graph.Transactions.ContainsKey(_flows.Further.Txid));
        Assert.Equal(TrailEndReason.NotExpanded, Assert.Single(Trace(result.Graph).Ends,
            end => end.Transaction == _flows.Further.Txid).Reason);
    }

    [Fact]
    public async Task A_start_transaction_not_in_the_case_is_reported_not_made_up()
    {
        _harness.Source.Online = false;
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForTransaction(_flows.Pay.Txid));

        var graph = await _harness.Load();

        Assert.Empty(graph.Nodes);
        Assert.Contains(graph.Problems, problem => problem.Contains(_flows.Pay.Txid.Value, StringComparison.Ordinal));
    }

    private TraceResult Trace(TraceGraph graph) => _harness.Service.Trace(
        graph, new TraceRequest(TracingMethodKind.Fifo, [new TraceSeed(new Outpoint(_flows.Pay.Txid, 0))]));
}
