using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using static Chainsaw.Modules.Tracing.Tests.Synthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// Stage 15: an expansion holds back what would flood the graph and says so, checks each transaction it brings in
/// against its bytes, the trace runs off the calling thread and can be stopped, the graph's view is kept in the case,
/// and the ends at the graph's addresses are read once per build.
/// </summary>
public sealed class Stage15Tests : IDisposable
{
    private readonly CaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_batch_payout_brings_in_the_spenders_that_receive_the_most_and_says_what_was_held_back()
    {
        var builder = Tx("payout").In(Wpkh("exchange-hot"), 16_000_000);
        for (var index = 0; index < 5; index++)
        {
            builder.Out(Wpkh($"customer-{index}"), 1_000_000 * (index + 1));
        }

        var payout = builder.Build();
        var spenders = Enumerable.Range(0, 5)
            .Select(index => Tx($"onward-{index}").Spend(payout, index).Out(Wpkh($"next-{index}"), 900_000).Build())
            .ToList();
        _harness.Source.Add([payout, .. spenders]);

        var result = await _harness.Service.ExpandAsync(
            _harness.Case, new ExpansionRequest(null, payout.Txid, MaximumTransactions: 2));

        Assert.Equal(ExpansionOutcome.Partly, result.Outcome);
        Assert.Equal(new ExpansionHoldBack(2, 5), result.HeldBack);
        Assert.Equal([payout.Txid, spenders[4].Txid, spenders[3].Txid], result.NewTransactions);
        Assert.Contains("hölls tillbaka", result.Problem, StringComparison.Ordinal);
        Assert.Equal(3, _harness.Source.Verifications);
    }

    [Fact]
    public async Task A_transaction_within_the_limit_is_expanded_whole()
    {
        var payout = Tx("small-payout").In(Wpkh("payer"), 3_000_000).Out(Wpkh("one"), 1_000_000)
            .Out(Wpkh("two"), 1_000_000).Build();
        var onward = Tx("small-onward").Spend(payout, 0).Out(Wpkh("three"), 900_000).Build();
        _harness.Source.Add(payout, onward);

        var result = await _harness.Expand(payout);

        Assert.Equal(ExpansionOutcome.Expanded, result.Outcome);
        Assert.Null(result.HeldBack);
    }

    [Fact]
    public async Task An_address_history_held_back_says_how_many_there_are_when_the_summary_can()
    {
        var history = Enumerable.Range(0, 4)
            .Select(index => Tx($"history-{index}", 900_000 + index).In(Wpkh($"payer-{index}"), 100_000)
                .Out(Wpkh("busy"), 90_000).Build())
            .ToArray();
        _harness.Source.Add(history);
        _harness.Source.TransactionCounts[Wpkh("busy")] = 4;

        var result = await _harness.Expand(Wpkh("busy"), maximum: 2);

        Assert.Equal(ExpansionOutcome.Partly, result.Outcome);
        Assert.Equal(new ExpansionHoldBack(2, 4), result.HeldBack);
        Assert.Contains("Adressen har 4 tx; de nyaste 2", result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_summary_an_address_held_back_says_there_are_more()
    {
        var history = Enumerable.Range(0, 4)
            .Select(index => Tx($"plain-{index}", 900_000 + index).In(Wpkh($"giver-{index}"), 100_000)
                .Out(Wpkh("plain"), 90_000).Build())
            .ToArray();
        _harness.Source.Add(history);

        var result = await _harness.Expand(Wpkh("plain"), maximum: 2);

        Assert.Equal(new ExpansionHoldBack(2, null), result.HeldBack);
        Assert.Contains("fler än 2 tx", result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_spender_that_contradicts_its_bytes_is_left_out_and_said()
    {
        var payment = Tx("checked-pay").In(Wpkh("victim"), 2_000_000).Out(Wpkh("scammer"), 1_900_000).Build();
        var forged = Tx("forged-onward").Spend(payment, 0).Out(Wpkh("cashout"), 1_800_000).Build();
        _harness.Source.Add(payment, forged);
        _harness.Source.Forged.Add(forged.Txid);

        var result = await _harness.Expand(payment);

        Assert.Equal([payment.Txid], result.NewTransactions);
        Assert.Contains("bytes", result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_trace_runs_off_the_calling_thread_with_the_same_result_and_can_be_stopped()
    {
        var payment = Tx("async-pay").In(Wpkh("victim-a"), 2_000_000).Out(Wpkh("scammer-a"), 1_900_000).Build();
        _harness.Source.Add(payment);
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForTransaction(payment.Txid));
        var graph = (await _harness.Expand(payment)).Graph;
        var request = new TraceRequest(
            TracingMethodKind.Fifo, [new TraceSeed(new Outpoint(payment.Txid, 0), null)]);

        var direct = _harness.Service.Trace(graph, request);
        var background = await _harness.Service.TraceAsync(graph, request);
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        Assert.Equal(direct.Traced, background.Traced);
        Assert.Equal(direct.Ends.Count, background.Ends.Count);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _harness.Service.TraceAsync(graph, request, stop.Token));
    }

    [Fact]
    public void The_view_is_the_default_until_it_is_saved_and_then_survives_reopening()
    {
        Assert.Equal(GraphViewState.Default, _harness.Service.GetView(_harness.Case));
        var view = new GraphViewState(
            TracingMethodKind.Haircut, GraphLayout.HopColumns, GraphAmountLabels.Traced,
            new GraphViewport(-120.5, 40, 0.75));

        Assert.True(_harness.Service.SaveView(_harness.Case, view));
        _harness.Reopen();

        Assert.Equal(view, _harness.Service.GetView(_harness.Case));
    }

    [Fact]
    public void Only_a_change_of_method_writes_the_journal()
    {
        var before = _harness.Case.ReadJournal().Count;
        var zoomed = GraphViewState.Default with { Viewport = new GraphViewport(0, 0, 2) };

        Assert.False(_harness.Service.SaveView(_harness.Case, zoomed));
        Assert.False(_harness.Service.SaveView(_harness.Case, zoomed with { Layout = GraphLayout.HopColumns }));
        Assert.True(_harness.Service.SaveView(_harness.Case, zoomed with { Method = TracingMethodKind.Haircut }));
        Assert.False(_harness.Service.SaveView(_harness.Case, zoomed with { Method = TracingMethodKind.Haircut }));

        var journal = _harness.Case.ReadJournal();
        var line = Assert.Single(journal.Skip(before));
        Assert.Equal(TracingJournalKinds.MethodChanged, line.Kind);
        Assert.Contains("haircut v1", line.Summary, StringComparison.Ordinal);
        Assert.Contains("FIFO v1", line.Summary, StringComparison.Ordinal);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Theory]
    [InlineData(double.NaN, 0, 1)]
    [InlineData(0, double.PositiveInfinity, 1)]
    [InlineData(0, 0, 0)]
    [InlineData(0, 0, -1)]
    public void A_view_that_cannot_be_shown_is_refused(double x, double y, double zoom)
    {
        var view = GraphViewState.Default with { Viewport = new GraphViewport(x, y, zoom) };

        Assert.Throws<ArgumentException>(() => _harness.Service.SaveView(_harness.Case, view));
        Assert.Throws<ArgumentException>(() => _harness.Service.SaveView(
            _harness.Case, GraphViewState.Default with { Method = (TracingMethodKind)99 }));
    }

    [Fact]
    public async Task The_ends_at_the_graph_s_addresses_are_read_once_per_build()
    {
        var payment = Tx("once-pay").In(Wpkh("victim-o"), 2_000_000).Out(Wpkh("exchange-o"), 600_000)
            .Out(Wpkh("other-o"), 600_000).Out(Wpkh("third-o"), 600_000).Build();
        _harness.Source.Add(payment);
        _harness.Labels.Addresses[Wpkh("exchange-o")] = TrailEndReason.Exchange;
        await _harness.Expand(payment);
        var batchBefore = _harness.Labels.BatchCalls;

        var graph = await _harness.Load();

        Assert.Equal(batchBefore + 1, _harness.Labels.BatchCalls);
        Assert.Equal(0, _harness.Labels.SingleCalls);
        Assert.Contains(graph.NodeOf(Wpkh("exchange-o"))!.Ends, hint => hint.Reason == TrailEndReason.Exchange);
    }
}
