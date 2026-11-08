using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.App.Settings;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>A graph source the test steers: what it answers, when, and what it was asked.</summary>
internal sealed class TestGraphSource : IGraphSource
{
    public List<TracingMethodKind> Loads { get; } = [];

    public List<IReadOnlyList<ExpansionRequest>> Expansions { get; } = [];

    public List<(string Key, GraphLayout Layout, GraphPoint Place)> Saved { get; } = [];

    public Func<GraphScene> Scene { get; set; } = () => GraphSamples.Scene();

    public TaskCompletionSource<GraphScene>? Waiting { get; set; }

    public Exception? Failure { get; set; }

    public GraphExpansion? Expansion { get; set; }

    /// <summary>A fault every expansion fails with, or <see langword="null"/>.</summary>
    public Exception? ExpandFailure { get; set; }

    public IReadOnlyList<WhyLine> ClusterLines { get; set; } = [new("Länk", GuideTopic.CommonInputOwnership)];

    public Task<GraphScene> LoadAsync(ICase @case, TracingMethodKind method, CancellationToken cancellationToken)
    {
        Loads.Add(method);
        if (Failure is not null)
        {
            return Task.FromException<GraphScene>(Failure);
        }

        return Waiting?.Task ?? Task.FromResult(Scene());
    }

    public Task<GraphExpansion> ExpandAsync(
        ICase @case,
        IReadOnlyList<ExpansionRequest> requests,
        TracingMethodKind method,
        CancellationToken cancellationToken)
    {
        Expansions.Add(requests);
        return ExpandFailure is { } failure
            ? Task.FromException<GraphExpansion>(failure)
            : Task.FromResult(Expansion ?? new GraphExpansion(Scene(), ExpansionOutcome.Expanded, 2, []));
    }

    public Task<IReadOnlyList<WhyLine>> ExplainClusterAsync(
        ICase @case, GraphNode node, CancellationToken cancellationToken) => Task.FromResult(ClusterLines);

    /// <summary>What the selection view opens with, or <see langword="null"/> for a failure.</summary>
    public Func<HeldBackClue, HeldBackChoices>? HeldBack { get; set; }

    /// <summary>The transactions brought in from what was held back, per pick.</summary>
    public List<IReadOnlyCollection<Txid>> Taken { get; } = [];

    public Task<HeldBackChoices> ListHeldBackAsync(
        ICase @case, HeldBackClue clue, GraphScene scene, CancellationToken cancellationToken) =>
        HeldBack is { } make
            ? Task.FromResult(make(clue))
            : Task.FromException<HeldBackChoices>(new InvalidOperationException("Offline"));

    /// <summary>The addresses the picks were brought in at, per pick.</summary>
    public List<BitcoinAddress> TakenAt { get; } = [];

    public Task<GraphExpansion> TakeHeldBackAsync(
        ICase @case,
        IReadOnlyDictionary<BitcoinAddress, IReadOnlyList<Txid>> picks,
        TracingMethodKind method,
        CancellationToken cancellationToken)
    {
        foreach (var (address, transactions) in picks)
        {
            TakenAt.Add(address);
            Taken.Add([.. transactions]);
        }

        var count = picks.Values.Sum(transactions => transactions.Count);
        return Task.FromResult(
            Expansion ?? new GraphExpansion(Scene(), ExpansionOutcome.Expanded, count, []));
    }

    public Task SavePositionAsync(ICase @case, string nodeKey, GraphLayout layout, GraphPoint place)
    {
        Saved.Add((nodeKey, layout, place));
        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }

    /// <summary>The view the case holds.</summary>
    public GraphViewState View { get; set; } = GraphViewState.Default;

    /// <summary>The views stored, in order.</summary>
    public List<GraphViewState> SavedViews { get; } = [];

    /// <summary>Holds the next store of the view until the test lets it go.</summary>
    public TaskCompletionSource? HoldViewSave { get; set; }

    /// <summary>Makes storing the view fail, as without an operator profile.</summary>
    public Exception? ViewFailure { get; set; }

    /// <summary>Makes the next reading of the view wait for (or fail with) what the test gives.</summary>
    public TaskCompletionSource<GraphViewState>? ViewWaiting { get; set; }

    public Task<GraphViewState> LoadViewAsync(ICase @case, CancellationToken cancellationToken)
    {
        var waiting = ViewWaiting;
        ViewWaiting = null;
        return waiting?.Task ?? Task.FromResult(View);
    }

    public Task SaveViewAsync(ICase @case, GraphViewState view)
    {
        SavedViews.Add(view);
        if (ViewFailure is not null)
        {
            return Task.FromException(ViewFailure);
        }

        View = view;
        var hold = HoldViewSave;
        HoldViewSave = null;
        return hold?.Task ?? Task.CompletedTask;
    }

    public List<IReadOnlyCollection<string>> Picked { get; } = [];

    public List<IReadOnlyCollection<string>> Returned { get; } = [];

    public Task PickFromPileAsync(ICase @case, IReadOnlyCollection<string> nodeKeys)
    {
        Picked.Add(nodeKeys);
        return Task.CompletedTask;
    }

    public Task ReturnToPileAsync(ICase @case, IReadOnlyCollection<string> nodeKeys)
    {
        Returned.Add(nodeKeys);
        return Task.CompletedTask;
    }
}

/// <summary>The graph's view model (sketch GR4) without WPF.</summary>
public sealed class GraphViewModelTests
{
    /// <summary>The session, the source and the graph, with a case open unless asked otherwise.</summary>
    private sealed class Rig
    {
        public Rig(bool open = true)
        {
            Workflow = new CaseWorkflow(new TestCaseStore(), Tracing, new TestVault(VaultState.Unlocked), Session);
            Graph = new GraphViewModel(
                Session, Workflow, Source, TestWays.For(Exit), new InlineUi(), Editor, new TestClock(),
                suppliers: Suppliers, switches: Switches);
            Graph.FitWanted += (_, _) => Fits++;
            Graph.GuideWanted += (_, request) => Guides.Add(request);
            Graph.WhyChanged += (_, request) => Followed.Add(request);
            if (open)
            {
                Session.Open(Case);
            }
        }

        public CaseSession Session { get; } = new();

        public TestTracing Tracing { get; } = new();

        public CaseWorkflow Workflow { get; }

        public TestGraphSource Source { get; } = new();

        public TestSuppliers Suppliers { get; } = new();

        public AttributionSwitches Switches { get; } = new();

        public TestGraphEditor Editor { get; } = new();

        public TestExitConnection Exit { get; } = new();

        public TestCase Case { get; } = new("2026-0001");

        public GraphViewModel Graph { get; }

        public int Fits { get; set; }

        public List<GuideRequest> Guides { get; } = [];

        public List<GuideRequest?> Followed { get; } = [];
    }

    /// <summary>Without a case the graph says so, and reads nothing.</summary>
    [Fact]
    public void WithoutCaseNothingIsRead()
    {
        var rig = new Rig(open: false);

        Assert.Null(rig.Graph.Scene);
        Assert.Equal(GraphViewModel.NoCaseText, rig.Graph.EmptyText);
        Assert.Empty(rig.Source.Loads);
        Assert.True(rig.Graph.IsOffline);
        Assert.False(rig.Graph.HasCase);
    }

    /// <summary>The inspector is there only while a case is open, since without one there is nothing to inspect.</summary>
    [Fact]
    public void InspectorFollowsTheOpenCase()
    {
        var rig = new Rig(open: false);

        rig.Session.Open(rig.Case);
        Assert.True(rig.Graph.HasCase);

        rig.Session.Forget();
        Assert.False(rig.Graph.HasCase);
    }

    /// <summary>An opened case is read by FIFO, shown, placed and fitted to the screen.</summary>
    [Fact]
    public void OpenedCaseIsReadAndFitted()
    {
        var rig = new Rig();

        Assert.Equal([TracingMethodKind.Fifo], rig.Source.Loads);
        Assert.Equal(5, rig.Graph.Scene!.Nodes.Count);
        Assert.Equal(string.Empty, rig.Graph.EmptyText);
        Assert.Equal(5, rig.Graph.Places.Count);
        Assert.Equal(5, rig.Graph.Curves.Count);
        Assert.Equal(1, rig.Fits);
        Assert.Equal("FIFO", rig.Graph.MethodText);
        Assert.Contains("FIFO v1", rig.Graph.TraceText, StringComparison.Ordinal);
        Assert.Contains("1,0000 BTC från 1 mynt", rig.Graph.TraceText, StringComparison.Ordinal);
    }

    /// <summary>A case without start points says how to add one.</summary>
    [Fact]
    public void CaseWithoutStartPointsSaysHow()
    {
        var rig = new Rig(open: false);
        var empty = GraphSamples.Graph() with { StartPoints = [], Nodes = [], Edges = [] };
        rig.Source.Scene = () => GraphSamples.Scene(graph: empty);

        rig.Session.Open(rig.Case);

        Assert.Equal(GraphViewModel.NoStartPointsText, rig.Graph.EmptyText);
        Assert.Empty(rig.Graph.Scene!.Nodes);
    }

    /// <summary>A graph that cannot be read says so instead of showing anything.</summary>
    [Fact]
    public void FailedReadingIsSaid()
    {
        var rig = new Rig(open: false);
        rig.Source.Failure = new InvalidOperationException("closed");

        rig.Session.Open(rig.Case);

        Assert.Null(rig.Graph.Scene);
        Assert.Equal(GraphViewModel.LoadFailedText, rig.Graph.EmptyText);
    }

    /// <summary>An answer to an older reading is dropped when the case has changed meanwhile.</summary>
    [Fact]
    public void LateAnswerForAnotherCaseIsDropped()
    {
        var rig = new Rig(open: false);
        var late = new TaskCompletionSource<GraphScene>();
        rig.Source.Waiting = late;
        rig.Session.Open(rig.Case);
        Assert.Equal(GraphViewModel.LoadingText, rig.Graph.EmptyText);

        rig.Session.Forget();
        late.SetResult(GraphSamples.Scene());

        Assert.Null(rig.Graph.Scene);
        Assert.Equal(GraphViewModel.NoCaseText, rig.Graph.EmptyText);
    }

    /// <summary>The method button switches between FIFO and haircut, always shown, and counts again.</summary>
    [Fact]
    public async Task MethodSwitchesAndCountsAgain()
    {
        var rig = new Rig();

        await rig.Graph.ToggleMethodAsync();

        Assert.Equal("haircut", rig.Graph.MethodText);
        Assert.Equal([TracingMethodKind.Fifo, TracingMethodKind.Haircut], rig.Source.Loads);
        Assert.Contains("Klicka för FIFO", rig.Graph.MethodToolTip, StringComparison.Ordinal);
        await rig.Graph.ToggleMethodAsync();
        Assert.Equal(TracingMethodKind.Fifo, rig.Graph.Method);
    }

    /// <summary>The amounts button steps through its three modes.</summary>
    [Fact]
    public void AmountsButtonSteps()
    {
        var rig = new Rig();

        Assert.Equal("Alla belopp", rig.Graph.LabelsText);
        rig.Graph.CycleLabels();
        Assert.Equal("Spårade belopp", rig.Graph.LabelsText);
        rig.Graph.CycleLabels();
        Assert.Equal("Inga belopp", rig.Graph.LabelsText);
    }

    /// <summary>
    /// Dragging in the hop columns moves only up and down; the free surface keeps its own places, and both are
    /// remembered across the switch (sketch GR4).
    /// </summary>
    [Fact]
    public void BothLayoutsRememberTheirPlaces()
    {
        var rig = new Rig();
        var graph = rig.Graph;
        var start = graph.Places[GraphSamples.UKey];
        graph.MoveNode(GraphSamples.UKey, start, new GraphPoint(900, 700));
        Assert.Equal(new GraphPoint(900, 700), graph.Places[GraphSamples.UKey]);

        graph.ShowLayout(GraphLayout.HopColumns);
        var column = graph.Places[GraphSamples.UKey];
        graph.MoveNode(GraphSamples.UKey, column, new GraphPoint(10, 400));
        Assert.Equal(new GraphPoint(column.X, 400), graph.Places[GraphSamples.UKey]);
        Assert.True(graph.Curves[GraphSamples.EK].IsArc);

        graph.ShowLayout(GraphLayout.Free);
        Assert.Equal(new GraphPoint(900, 700), graph.Places[GraphSamples.UKey]);
        graph.ShowLayout(GraphLayout.HopColumns);
        Assert.Equal(new GraphPoint(column.X, 400), graph.Places[GraphSamples.UKey]);
    }

    /// <summary>A dragged node's place is stored in the case for the layout it was dragged in.</summary>
    [Fact]
    public async Task DroppedNodeIsStored()
    {
        var rig = new Rig();
        rig.Graph.MoveNode(GraphSamples.EKey, rig.Graph.Places[GraphSamples.EKey], new GraphPoint(40, 50));

        await rig.Graph.DropNodeAsync(GraphSamples.EKey);

        Assert.Equal([(GraphSamples.EKey, GraphLayout.Free, new GraphPoint(40, 50))], rig.Source.Saved);
    }

    /// <summary>A place that cannot be stored is said, and still holds.</summary>
    [Fact]
    public async Task UnstoredPlaceIsSaid()
    {
        var rig = new Rig();
        rig.Graph.MoveNode(GraphSamples.EKey, rig.Graph.Places[GraphSamples.EKey], new GraphPoint(40, 50));
        rig.Source.Failure = new InvalidOperationException();

        await rig.Graph.DropNodeAsync(GraphSamples.EKey);

        Assert.Equal(GraphViewModel.SaveFailedText, rig.Graph.Notice);
        Assert.Equal(new GraphPoint(40, 50), rig.Graph.Places[GraphSamples.EKey]);
    }

    /// <summary>A click on a node chooses it and fills the inspector; a click on empty surface chooses nothing.</summary>
    [Fact]
    public void ClickChoosesAndInspects()
    {
        var rig = new Rig();
        var place = rig.Graph.Places[GraphSamples.EKey];

        rig.Graph.ChooseAt(new GraphPoint(place.X + 3, place.Y + 3), 6);

        Assert.Equal(GraphSamples.EKey, rig.Graph.SelectedNode);
        Assert.True(rig.Graph.IsFocused);
        Assert.NotNull(rig.Graph.Inspection);
        Assert.True(rig.Graph.CanExpand);
        Assert.Contains(GraphSamples.KKey, rig.Graph.Near.Nodes);

        rig.Graph.ChooseAt(new GraphPoint(-9999, -9999), 6);
        Assert.Null(rig.Graph.SelectedNode);
        Assert.Null(rig.Graph.Inspection);
        Assert.False(rig.Graph.IsFocused);
    }

    /// <summary>A chosen line fills the inspector with the line; it cannot be expanded.</summary>
    [Fact]
    public void ChosenLineIsInspected()
    {
        var rig = new Rig();

        rig.Graph.Choose(null, GraphSamples.VK);

        Assert.Equal(GraphSamples.VK, rig.Graph.SelectedEdge);
        Assert.Contains("→", rig.Graph.Inspection!.Title, StringComparison.Ordinal);
        Assert.False(rig.Graph.CanExpand);
    }

    /// <summary>A chosen cluster gets Clustering's answers added to its Varför?.</summary>
    [Fact]
    public void ChosenClusterIsExplained()
    {
        var rig = new Rig();

        rig.Graph.Choose(GraphSamples.KKey, null);

        Assert.Equal("Länk", rig.Graph.Why[^1].Text);
        Assert.True(rig.Graph.Why.Count > rig.Graph.Inspection!.Why.Count);
    }

    /// <summary>
    /// Varför? asks for the Guide with the chosen thing's answers on top, and nothing chosen asks for nothing; the
    /// legend's ? and the method ask for their sections.
    /// </summary>
    [Fact]
    public void WhyAsksForTheGuide()
    {
        var rig = new Rig();
        rig.Graph.AskWhy();
        Assert.Empty(rig.Guides);

        rig.Graph.Choose(GraphSamples.UKey, null);
        rig.Graph.AskWhy();
        rig.Graph.OpenGuideTopic(GuideTopic.Graph);
        rig.Graph.OpenMethodGuide();

        var why = rig.Guides[0];
        Assert.Equal(GraphSamples.UKey, why.Subject);
        Assert.Equal("Varför? · " + rig.Graph.Inspection!.Title, why.Context!.Label);
        Assert.Equal(rig.Graph.Why.Select(line => line.Text), why.Context.Lines.Select(line => line.Text));
        Assert.Equal(GuideRequest.ForTopic(GuideTopic.Graph), rig.Guides[1]);
        Assert.False(rig.Graph.IsLegendOpen);
        Assert.Equal(GuideTopic.Methods, rig.Guides[2].Topic);
        Assert.Equal("FIFO (först in, först ut)", rig.Guides[2].Heading);
        Assert.Null(rig.Guides[2].Subject);
    }

    /// <summary>Opening or closing a case says so, so that the Guide's panel closes with the old answers.</summary>
    [Fact]
    public async Task CaseChangeTellsThePanel()
    {
        var rig = new Rig(open: false);
        var changes = 0;
        rig.Graph.CaseChanged += (_, _) => changes++;

        rig.Session.Open(rig.Case);
        await rig.Session.CloseAsync();

        Assert.Equal(2, changes);
    }

    /// <summary>The panel can follow: a new choice, and a cluster's answers when they come, say so.</summary>
    [Fact]
    public void ChoosingTellsThePanel()
    {
        var rig = new Rig();

        rig.Graph.Choose(GraphSamples.KKey, null);
        rig.Graph.Choose(null, null);

        Assert.True(rig.Followed.Count >= 2);
        Assert.All(
            rig.Followed.Take(rig.Followed.Count - 1), request => Assert.Equal(GraphSamples.KKey, request!.Subject));
        Assert.Contains("Länk", rig.Followed[^2]!.Context!.Lines.Select(line => line.Text));
        Assert.Null(rig.Followed[^1]);
    }

    /// <summary>Expanding a chosen node asks for its addresses, shows the graph after it and says what came.</summary>
    [Fact]
    public async Task ExpandingAsksForTheNodesAddresses()
    {
        var rig = new Rig();
        rig.Graph.Choose(GraphSamples.KKey, null);

        await rig.Graph.ExpandAsync();

        Assert.Equal(
            [ExpansionRequest.ForAddress(GraphSamples.K1), ExpansionRequest.ForAddress(GraphSamples.K2)],
            rig.Source.Expansions.Single());
        Assert.Equal("2 nya tx i grafen.", rig.Graph.Notice);
        Assert.False(rig.Graph.IsExpanding);
        Assert.Equal(GraphSamples.KKey, rig.Graph.SelectedNode);
    }

    /// <summary>
    /// The suppliers' icon (Chainabuse) shows only while one is on and the node is one address; it asks with the
    /// address and the names that are on, and says what came back.
    /// </summary>
    [Fact]
    public async Task TheSuppliersAreAskedOnlyWhenOneIsOnAndAboutOneAddress()
    {
        var rig = new Rig();
        rig.Exit.Set(new ExitStatus(ExitState.Connected, Guid.NewGuid(), null));
        var toasts = new List<ToastRequest>();
        rig.Graph.ToastWanted += (_, request) => toasts.Add(request);

        rig.Graph.Choose(GraphSamples.UKey, null);
        Assert.False(rig.Graph.IsOffline);
        Assert.DoesNotContain(rig.Graph.NodeTools, tool => tool.Action == NodeMenuAction.AskSuppliers);

        rig.Switches.Set("chainabuse", on: true);
        rig.Graph.Choose(GraphSamples.UKey, null);
        Assert.Contains(rig.Graph.NodeTools, tool => tool.Action == NodeMenuAction.AskSuppliers);
        rig.Graph.Choose(GraphSamples.KKey, null);
        Assert.DoesNotContain(rig.Graph.NodeTools, tool => tool.Action == NodeMenuAction.AskSuppliers);

        rig.Graph.Choose(GraphSamples.UKey, null);
        rig.Graph.AskSuppliers();
        await rig.Suppliers.Asked;

        var (address, enabled) = Assert.Single(rig.Suppliers.Calls);
        Assert.Equal(Chain.Bitcoin, address.Chain);
        Assert.Equal(["chainabuse"], enabled);
        var toast = Assert.Single(toasts);
        Assert.Equal(ToastKind.Information, toast.Kind);
        Assert.Contains(GraphViewModel.NothingReportedText, toast.Title, StringComparison.Ordinal);
    }

    /// <summary>The chosen node's actions over it (sketch IN2b) are only those that apply, Expandera first.</summary>
    [Fact]
    public void TheNodesActionsAreOnlyThoseThatApply()
    {
        var rig = new Rig();

        rig.Graph.Choose(GraphSamples.KKey, null);

        var actions = rig.Graph.NodeTools.Select(tool => tool.Action).ToList();
        Assert.Equal(NodeMenuAction.Expand, actions[0]);
        Assert.Contains(NodeMenuAction.Copy, actions);
        Assert.Equal(rig.Graph.Forms.CanSplit, actions.Contains(NodeMenuAction.Split));
        Assert.True(rig.Graph.HasNodeTools);

        rig.Graph.Choose(null, null);

        Assert.Empty(rig.Graph.NodeTools);
    }

    /// <summary>An expansion is told as a toast of its sort (sketch TO1a).</summary>
    [Fact]
    public async Task AnExpansionIsToldAsAToast()
    {
        var rig = new Rig();
        var toasts = new List<ToastRequest>();
        rig.Graph.ToastWanted += (_, request) => toasts.Add(request);
        rig.Graph.Choose(GraphSamples.KKey, null);

        await rig.Graph.ExpandAsync();

        var toast = Assert.Single(toasts);
        Assert.Equal(ToastKind.Success, toast.Kind);
        Assert.Equal("2 nya tx i grafen.", toast.Title);
    }

    /// <summary>A failed expansion is an error with its fault's message and <em>Försök igen</em>.</summary>
    [Fact]
    public async Task AFailedExpansionIsAnErrorThatCanBeTriedAgain()
    {
        var rig = new Rig();
        var toasts = new List<ToastRequest>();
        rig.Graph.ToastWanted += (_, request) => toasts.Add(request);
        rig.Graph.Choose(GraphSamples.KKey, null);
        rig.Source.ExpandFailure = new InvalidOperationException("Källan svarade fel.");

        await rig.Graph.ExpandAsync();

        var toast = Assert.Single(toasts);
        Assert.Equal(ToastKind.Error, toast.Kind);
        Assert.Equal(GraphViewModel.ExpandFailedText, toast.Title);
        Assert.Equal("Källan svarade fel.", toast.Text);
        Assert.Equal(GraphViewModel.RetryText, toast.ActionText);
        rig.Source.ExpandFailure = null;
        toast.Action!();
        Assert.Equal(2, rig.Source.Expansions.Count);
    }

    /// <summary>Offline, an expansion that brought nothing says why honestly (principle 9).</summary>
    [Fact]
    public async Task OfflineExpansionIsHonest()
    {
        var rig = new Rig();
        rig.Source.Expansion = new GraphExpansion(
            GraphSamples.Scene(), ExpansionOutcome.NotExpanded, 0, ["Ingen utgång är ansluten."]);
        rig.Graph.Choose(GraphSamples.UKey, null);

        await rig.Graph.ExpandAsync();

        Assert.StartsWith("Inte expanderad. Ingen utgång är ansluten.", rig.Graph.Notice, StringComparison.Ordinal);
        Assert.EndsWith(GraphViewModel.OfflineText, rig.Graph.Notice, StringComparison.Ordinal);
    }

    /// <summary>The exit's state is followed: connected is not offline.</summary>
    [Fact]
    public void ExitStateIsFollowed()
    {
        var rig = new Rig();

        rig.Exit.Set(new ExitStatus(ExitState.Connected, Guid.NewGuid(), null));

        Assert.False(rig.Graph.IsOffline);
    }

    /// <summary>A start point added from the command bar reads the graph anew, without fitting it again.</summary>
    [Fact]
    public async Task AddedStartPointReadsTheGraphAgain()
    {
        var rig = new Rig();

        await rig.Workflow.AddStartPointAsync(ChainStartPoint.Recognise(Samples.Address));

        Assert.Equal(2, rig.Source.Loads.Count);
        Assert.Equal(1, rig.Fits);
    }

    /// <summary>The notice for each outcome of an expansion.</summary>
    [Fact]
    public void ExpansionNoticesAreHonest()
    {
        var scene = GraphSamples.Scene();

        Assert.Equal(
            "Inget nytt att hämta.",
            GraphViewModel.ExpansionNotice(new GraphExpansion(scene, ExpansionOutcome.Expanded, 0, []), offline: true));
        Assert.Equal(
            "Delvis expanderad: 3 nya tx. Gränsen nåddes.",
            GraphViewModel.ExpansionNotice(
                new GraphExpansion(scene, ExpansionOutcome.Partly, 3, ["Gränsen nåddes."]), offline: false));
    }

    /// <summary>The legend shows every sort and mark of the visual language.</summary>
    [Fact]
    public void LegendShowsTheVisualLanguage()
    {
        var rig = new Rig();
        rig.Graph.ToggleLegend();

        Assert.True(rig.Graph.IsLegendOpen);
        Assert.Equal(VisualLanguage.Sorts.Count, rig.Graph.LegendSorts.Count);
        rig.Graph.Choose(GraphSamples.VKey, null);
        Assert.False(rig.Graph.IsLegendOpen);
    }

    /// <summary>
    /// While an expansion runs, the method cannot change and a start point's reading waits for it, so that the
    /// expansion is never cut short; the reading follows when it is done (regression).
    /// </summary>
    [Fact]
    public async Task ExpansionIsNotCutShort()
    {
        var rig = new Rig();
        var expansion = new TaskCompletionSource<GraphExpansion>();
        rig.Source.Waiting = null;
        var source = new SlowExpansion(rig.Source, expansion.Task);
        var graph = new GraphViewModel(
            rig.Session, rig.Workflow, source, TestWays.For(rig.Exit), new InlineUi(), rig.Editor, new TestClock());
        graph.Choose(GraphSamples.UKey, null);
        var loadsBefore = rig.Source.Loads.Count;

        var running = graph.ExpandAsync();
        Assert.False(graph.ToggleMethodCommand.CanExecute(null));
        await graph.RefreshAsync();
        Assert.Equal(loadsBefore, rig.Source.Loads.Count);

        expansion.SetResult(new GraphExpansion(GraphSamples.Scene(), ExpansionOutcome.Expanded, 1, []));
        await running;

        Assert.Equal("1 nya tx i grafen.", graph.Notice);
        Assert.Equal(loadsBefore + 1, rig.Source.Loads.Count);
        Assert.True(graph.ToggleMethodCommand.CanExecute(null));
    }

    /// <summary>A reading that started before an expansion cannot bring back the graph without it (regression).</summary>
    [Fact]
    public async Task OlderReadingDoesNotUndoAnExpansion()
    {
        var rig = new Rig();
        rig.Graph.Choose(GraphSamples.UKey, null);
        var late = new TaskCompletionSource<GraphScene>();
        rig.Source.Waiting = late;
        var reading = rig.Graph.RefreshAsync();
        var grown = GraphSamples.Graph() with
        {
            Nodes =
            [
                .. GraphSamples.Graph().Nodes,
                GraphSamples.Node("address:x", GraphNodeKind.Address, [GraphSamples.Address(8)], 3),
            ],
        };
        rig.Source.Expansion = new GraphExpansion(
            GraphSamples.Scene(graph: grown), ExpansionOutcome.Expanded, 1, []);

        await rig.Graph.ExpandAsync();
        late.SetResult(GraphSamples.Scene());
        await reading;

        Assert.NotNull(rig.Graph.Scene!.Node("address:x"));
    }

    /// <summary>A node not moved by the user stays where it first stood on the free surface when the graph grows.</summary>
    [Fact]
    public async Task FreePlacesStayWhenTheGraphGrows()
    {
        var rig = new Rig();
        var before = rig.Graph.Places[GraphSamples.EKey];
        var earlier = GraphSamples.Node("address:x", GraphNodeKind.Address, [GraphSamples.Address(8)], -2);
        var grown = GraphSamples.Graph() with { Nodes = [earlier, .. GraphSamples.Graph().Nodes] };
        rig.Source.Expansion = new GraphExpansion(GraphSamples.Scene(graph: grown), ExpansionOutcome.Expanded, 1, []);
        rig.Graph.Choose(GraphSamples.UKey, null);

        await rig.Graph.ExpandAsync();

        Assert.Equal(before, rig.Graph.Places[GraphSamples.EKey]);
        rig.Graph.ShowLayout(GraphLayout.HopColumns);
        Assert.NotEqual(before.X, rig.Graph.Places[GraphSamples.EKey].X);
    }

    /// <summary>
    /// A node that arrives among the nodes of a column goes below them on the free surface, never on top of one
    /// (regression).
    /// </summary>
    [Fact]
    public async Task NewNodeNeverLandsOnAnOldOne()
    {
        var rig = new Rig();
        var earlier = GraphSamples.Node("address:x", GraphNodeKind.Address, [GraphSamples.Address(8)], 1);
        var nodes = GraphSamples.Graph().Nodes.ToList();
        nodes.Insert(2, earlier);
        var grown = GraphSamples.Graph() with { Nodes = nodes };
        rig.Source.Expansion = new GraphExpansion(GraphSamples.Scene(graph: grown), ExpansionOutcome.Expanded, 1, []);
        rig.Graph.Choose(GraphSamples.UKey, null);

        await rig.Graph.ExpandAsync();

        var frames = rig.Graph.Places.ToDictionary(pair => pair.Key, pair => GraphSizes.NodeFrame(pair.Value));
        var x = frames["address:x"];
        Assert.All(frames.Where(pair => pair.Key != "address:x"), pair => Assert.False(pair.Value.Intersects(x)));
    }

    /// <summary>A place below every standing node of the column, or the column's own place when it is free.</summary>
    [Fact]
    public void BelowStandingFindsAFreeRow()
    {
        var column = new GraphPoint(16, 36);

        Assert.Equal(column, GraphViewModel.BelowStanding(column, [new GraphPoint(400, 36)]));
        Assert.Equal(
            new GraphPoint(16, 164 + GraphSizes.RowSpacing),
            GraphViewModel.BelowStanding(column, [new GraphPoint(16, 36), new GraphPoint(20, 164)]));
    }

    /// <summary>
    /// On the first reading of a case, a node without a stored place never lands on a node the user stored there
    /// (regression).
    /// </summary>
    [Fact]
    public void FirstReadingAvoidsStoredPlaces()
    {
        var rig = new Rig(open: false);
        var graph = GraphSamples.Graph();
        var columns = HopLayout.Arrange(graph.Nodes).Places;
        var onU = columns[GraphSamples.UKey];
        var stored = new Dictionary<GraphLayout, NodePosition> { [GraphLayout.Free] = new(onU.X, onU.Y) };
        var nodes = graph.Nodes.Select(node => node.Key == GraphSamples.EKey ? node with { Positions = stored } : node);
        rig.Source.Scene = () => GraphSamples.Scene(graph: graph with { Nodes = [.. nodes] });

        rig.Session.Open(rig.Case);

        var e = GraphSizes.NodeFrame(rig.Graph.Places[GraphSamples.EKey]);
        Assert.False(GraphSizes.NodeFrame(rig.Graph.Places[GraphSamples.UKey]).Intersects(e));
    }

    /// <summary>A graph source whose expansion waits for the test, and otherwise answers as another source.</summary>
    /// <param name="inner">The other source.</param>
    /// <param name="expansion">The expansion's answer.</param>
    private sealed class SlowExpansion(TestGraphSource inner, Task<GraphExpansion> expansion) : IGraphSource
    {
        public Task<GraphScene> LoadAsync(ICase @case, TracingMethodKind method, CancellationToken cancellationToken) =>
            inner.LoadAsync(@case, method, cancellationToken);

        public Task<GraphExpansion> ExpandAsync(
            ICase @case,
            IReadOnlyList<ExpansionRequest> requests,
            TracingMethodKind method,
            CancellationToken cancellationToken) => expansion;

        public Task<IReadOnlyList<WhyLine>> ExplainClusterAsync(
            ICase @case, GraphNode node, CancellationToken cancellationToken) =>
            inner.ExplainClusterAsync(@case, node, cancellationToken);

        public Task SavePositionAsync(ICase @case, string nodeKey, GraphLayout layout, GraphPoint place) =>
            inner.SavePositionAsync(@case, nodeKey, layout, place);

        public Task<GraphViewState> LoadViewAsync(ICase @case, CancellationToken cancellationToken) =>
            inner.LoadViewAsync(@case, cancellationToken);

        public Task SaveViewAsync(ICase @case, GraphViewState view) => inner.SaveViewAsync(@case, view);

        public Task PickFromPileAsync(ICase @case, IReadOnlyCollection<string> nodeKeys) =>
            inner.PickFromPileAsync(@case, nodeKeys);

        public Task ReturnToPileAsync(ICase @case, IReadOnlyCollection<string> nodeKeys) =>
            inner.ReturnToPileAsync(@case, nodeKeys);

        public Task<HeldBackChoices> ListHeldBackAsync(
            ICase @case, HeldBackClue clue, GraphScene scene, CancellationToken cancellationToken) =>
            inner.ListHeldBackAsync(@case, clue, scene, cancellationToken);

        public Task<GraphExpansion> TakeHeldBackAsync(
            ICase @case,
            IReadOnlyDictionary<BitcoinAddress, IReadOnlyList<Txid>> picks,
            TracingMethodKind method,
            CancellationToken cancellationToken) =>
            inner.TakeHeldBackAsync(@case, picks, method, cancellationToken);
    }
}

/// <summary>The suppliers of attribution, without network: records what is asked and answers that nothing is reported.</summary>
internal sealed class TestSuppliers : IAttributionProviders
{
    private readonly TaskCompletionSource _asked = new();

    public List<(ChainAddress Address, IReadOnlyCollection<string> Enabled)> Calls { get; } = [];

    public Task Asked => _asked.Task;

    public IReadOnlyList<AttributionProviderInfo> Providers { get; } =
        [new("chainabuse", "Chainabuse", "Chainabuse", [Chain.Bitcoin, Chain.Ethereum])];

    public Task<IReadOnlyList<ProviderLookup>> LookupAsync(
        ICase @case, ChainAddress address, IReadOnlyCollection<string> enabled,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((address, enabled));
        _asked.TrySetResult();
        IReadOnlyList<ProviderLookup> answers =
            [new("chainabuse", ProviderLookupStatus.Answered, [], 0, 1, null)];
        return Task.FromResult(answers);
    }
}
