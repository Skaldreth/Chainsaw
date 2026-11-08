using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>
/// Stage 15 in the window: the graph opens as it was left (method, layout, amounts and zoom stored in the case), the
/// labels are read once per build, the trace runs asynchronously, and the label library takes in each opened case
/// and gives the graph's addresses the labels from other cases.
/// </summary>
public sealed class Stage15Tests
{
    private static readonly GraphViewState Stored = new(
        TracingMethodKind.Haircut, GraphLayout.HopColumns, GraphAmountLabels.Traced, new GraphViewport(-40, 25, 1.5));

    /// <summary>The graph's view model over a test source with a stored view, with a case open.</summary>
    private sealed class Rig
    {
        public Rig(GraphViewState? view = null)
        {
            Source.View = view ?? GraphViewState.Default;
            Workflow = new CaseWorkflow(
                new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), Session);
            Graph = new GraphViewModel(
                Session,
                Workflow,
                Source,
                TestWays.For(new TestExitConnection()),
                new InlineUi(),
                new TestGraphEditor(),
                new TestClock());
            Session.Open(Case);
        }

        public CaseSession Session { get; } = new();

        public CaseWorkflow Workflow { get; }

        public TestGraphSource Source { get; } = new();

        public TestCase Case { get; } = new("2026-0015");

        public GraphViewModel Graph { get; }
    }

    [Fact]
    public void The_graph_opens_as_it_was_left()
    {
        var rig = new Rig(Stored);

        Assert.Equal(TracingMethodKind.Haircut, rig.Graph.Method);
        Assert.Equal(GraphLayout.HopColumns, rig.Graph.Layout);
        Assert.Equal(AmountLabelMode.Traced, rig.Graph.Labels);
        Assert.Equal([TracingMethodKind.Haircut], rig.Source.Loads);
        Assert.Equal(new Viewport(-40, 25, 1.5), rig.Graph.TakeStoredViewport());
        Assert.Null(rig.Graph.TakeStoredViewport()); // only once; a later graph is fitted
        Assert.Empty(rig.Source.SavedViews);
    }

    [Fact]
    public void Without_a_stored_view_the_defaults_hold_and_the_graph_is_fitted()
    {
        var rig = new Rig();

        Assert.Equal(TracingMethodKind.Fifo, rig.Graph.Method);
        Assert.Equal(GraphLayout.Free, rig.Graph.Layout);
        Assert.Null(rig.Graph.TakeStoredViewport());
    }

    [Fact]
    public async Task Each_change_of_the_view_is_stored_in_the_case()
    {
        var rig = new Rig();

        await rig.Graph.ToggleMethodAsync();
        rig.Graph.ShowLayout(GraphLayout.HopColumns);
        rig.Graph.CycleLabels();
        rig.Graph.ViewportChanged(new Viewport(10, 20, 2));
        rig.Graph.ViewportChanged(new Viewport(10, 20, 2));

        Assert.Equal(4, rig.Source.SavedViews.Count);
        Assert.Equal(
            new GraphViewState(
                TracingMethodKind.Haircut, GraphLayout.HopColumns, GraphAmountLabels.Traced,
                new GraphViewport(10, 20, 2)),
            rig.Source.SavedViews[^1]);
        Assert.Equal(TracingMethodKind.Haircut, rig.Source.SavedViews[0].Method);
    }

    [Fact]
    public void Changes_made_while_the_view_is_stored_are_stored_once_after_it()
    {
        var rig = new Rig();
        var hold = new TaskCompletionSource();
        rig.Source.HoldViewSave = hold;

        rig.Graph.ViewportChanged(new Viewport(1, 1, 1));
        rig.Graph.ViewportChanged(new Viewport(2, 2, 1));
        rig.Graph.ViewportChanged(new Viewport(3, 3, 1));
        Assert.Single(rig.Source.SavedViews);
        hold.SetResult();

        Assert.Equal(2, rig.Source.SavedViews.Count);
        Assert.Equal(new GraphViewport(3, 3, 1), rig.Source.SavedViews[^1].Viewport);
    }

    [Fact]
    public void A_view_that_cannot_be_stored_says_so_and_still_holds()
    {
        var rig = new Rig();
        rig.Source.ViewFailure = new CaseException(CaseError.NoOperatorProfile);

        rig.Graph.ShowLayout(GraphLayout.HopColumns);

        Assert.Equal(GraphViewModel.ViewSaveFailedText, rig.Graph.Notice);
        Assert.Equal(GraphLayout.HopColumns, rig.Graph.Layout);
    }

    [Fact]
    public async Task Without_a_case_nothing_is_stored()
    {
        var rig = new Rig();
        await rig.Session.CloseAsync();

        rig.Graph.ShowLayout(GraphLayout.HopColumns);

        Assert.Empty(rig.Source.SavedViews);
    }

    [Fact]
    public void Viewports_convert_to_and_from_the_stored_form()
    {
        var viewport = new Viewport(-3.5, 7, 0.5);

        Assert.Equal(viewport, Viewport.FromStored(viewport.ToStored()));
        Assert.Equal(Viewport.MaximumZoom, Viewport.FromStored(new GraphViewport(0, 0, 50)).Zoom);
        foreach (var mode in Enum.GetValues<AmountLabelMode>())
        {
            Assert.Equal(mode, AmountLabelModes.FromStored(mode.ToStored()));
        }
    }

    [Fact]
    public async Task The_labels_are_read_once_per_build_and_the_trace_is_asynchronous()
    {
        var tracing = new TestTracing { Graph = GraphSamples.Graph(), Tracer = (_, _) => GraphSamples.Trace() };
        var attribution = new TestAttribution();
        attribution.Labels[GraphSamples.K1] = [GraphSamples.Label(GraphSamples.K1, LabelKind.Exchange, "Börs A")];
        var source = new GraphSource(tracing, attribution, new TestClustering());
        using var stop = new CancellationTokenSource();

        var scene = await source.LoadAsync(new TestCase("2026-0015"), TracingMethodKind.Fifo, stop.Token);

        Assert.Equal(1, attribution.BatchCalls);
        Assert.Equal(0, attribution.SingleCalls);
        Assert.NotNull(scene.Snapshot.Trace);
        Assert.Equal(stop.Token, Assert.Single(tracing.TraceTokens));
    }

    [Fact]
    public async Task The_graph_s_addresses_get_the_library_s_labels_and_the_graph_is_read_again()
    {
        var tracing = new TestTracing { Graph = GraphSamples.Graph(), Tracer = (_, _) => GraphSamples.Trace() };
        var shared = GraphSamples.Label(GraphSamples.K1, LabelKind.Exchange).Entries[0];
        var library = new TestLibrary { ToShare = [shared] };
        var source = new GraphSource(tracing, new TestAttribution(), new TestClustering(), library);

        await source.LoadAsync(new TestCase("2026-0015"), TracingMethodKind.Fifo, CancellationToken.None);
        await source.LoadAsync(new TestCase("2026-0015"), TracingMethodKind.Fifo, CancellationToken.None);

        Assert.Equal(2, library.Shared.Count);
        Assert.Contains(GraphSamples.K1, library.Shared[0]);
        Assert.Equal(3, tracing.Loads); // read again only the first time, when labels came in
    }

    [Fact]
    public async Task The_graph_is_read_again_when_the_library_takes_back_a_withdrawn_copy()
    {
        // A copy whose label was withdrawn where it was set is taken back while nothing new comes in: the graph read
        // before still ends the trail on it, so it must be read again.
        var tracing = new TestTracing { Graph = GraphSamples.Graph(), Tracer = (_, _) => GraphSamples.Trace() };
        var attribution = new TestAttribution();
        attribution.Entries.Add(GraphSamples.Label(GraphSamples.K1, LabelKind.Exchange).Entries[0]);
        var library = new TestLibrary { Sharing = attribution.Entries.Clear };
        var source = new GraphSource(tracing, attribution, new TestClustering(), library);

        await source.LoadAsync(new TestCase("2026-0015"), TracingMethodKind.Fifo, CancellationToken.None);

        Assert.Equal(2, tracing.Loads);
    }

    [Fact]
    public async Task A_library_that_cannot_be_read_never_keeps_the_graph_from_being_shown()
    {
        var tracing = new TestTracing { Graph = GraphSamples.Graph(), Tracer = (_, _) => GraphSamples.Trace() };
        var library = new TestLibrary { Failure = new VaultException(VaultError.Locked) };
        var source = new GraphSource(tracing, new TestAttribution(), new TestClustering(), library);

        var scene = await source.LoadAsync(
            new TestCase("2026-0015"), TracingMethodKind.Fifo, CancellationToken.None);

        Assert.NotEmpty(scene.Nodes);
    }

    [Fact]
    public async Task An_opened_case_is_taken_into_the_library_and_again_before_it_closes()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Cases.AddFile("A1", 10);

        await rig.Workflow.OpenAsync("A1");
        var opened = rig.Session.Current!;
        await rig.Workflow.CloseAsync();

        Assert.Equal([opened, opened], rig.Library.Absorbed);
    }

    [Fact]
    public async Task A_library_that_fails_never_keeps_a_case_from_opening()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Cases.AddFile("A1", 10);
        rig.Library.Failure = new InvalidDataException("Skadad.");

        var reply = await rig.Workflow.OpenAsync("A1");

        Assert.False(reply.IsError);
        Assert.NotNull(rig.Session.Current);
    }

    [Fact]
    public void A_line_says_whether_its_transactions_are_checked_against_their_bytes()
    {
        var graph = GraphSamples.Graph();
        var checkedOne = graph.Transactions[GraphSamples.Tx[1]];
        var transactions = graph.Transactions.ToDictionary(pair => pair.Key, pair => pair.Value);
        transactions[GraphSamples.Tx[1]] = checkedOne with
        {
            Transaction = checkedOne.Transaction with { BytesVerified = true },
        };
        var scene = GraphSamples.Scene(graph: graph with { Transactions = transactions });

        Assert.Equal(InspectorContent.CheckedText, InspectorContent.Checked(scene, [GraphSamples.Tx[1]]));
        Assert.Equal(InspectorContent.UncheckedText, InspectorContent.Checked(scene, [GraphSamples.Tx[2]]));
        Assert.Equal("1 av 2 tx mot bytes", InspectorContent.Checked(scene, [GraphSamples.Tx[1], GraphSamples.Tx[2]]));
        Assert.Equal(InspectorContent.UncheckedText, InspectorContent.Checked(scene, []));
    }

    [Fact]
    public void Nothing_is_stored_before_the_case_s_own_view_is_read()
    {
        var rig = new Rig(Stored);
        var waiting = new TaskCompletionSource<GraphViewState>();
        rig.Source.ViewWaiting = waiting;
        var next = new TestCase("2026-0016");

        rig.Session.Open(next);
        rig.Graph.ViewportChanged(new Viewport(5, 5, 1));
        rig.Graph.ShowLayout(GraphLayout.HopColumns);
        Assert.Empty(rig.Source.SavedViews);
        waiting.SetResult(GraphViewState.Default);
        Assert.Equal(GraphLayout.Free, rig.Graph.Layout); // the case's own view, not the one shown before
        rig.Graph.ShowLayout(GraphLayout.HopColumns);

        var saved = Assert.Single(rig.Source.SavedViews);
        Assert.Equal(TracingMethodKind.Fifo, saved.Method);
    }

    [Fact]
    public void A_view_that_could_not_be_read_is_never_stored_over()
    {
        var rig = new Rig(Stored);
        var failing = new TaskCompletionSource<GraphViewState>();
        failing.SetException(new CaseException(CaseError.Closed));
        rig.Source.ViewWaiting = failing;

        rig.Session.Open(new TestCase("2026-0017"));
        rig.Graph.ShowLayout(GraphLayout.HopColumns);

        Assert.Empty(rig.Source.SavedViews);
    }

    [Fact]
    public void An_expansion_s_notes_are_said_also_when_it_is_whole()
    {
        var result = new GraphExpansion(GraphSamples.Scene(), ExpansionOutcome.Expanded, 2, ["Tx:en låg tidigare …"]);

        Assert.Equal("2 nya tx i grafen. Tx:en låg tidigare …", GraphViewModel.ExpansionNotice(result, null));
    }
}
