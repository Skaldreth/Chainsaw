using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

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
        return Task.FromResult(Expansion ?? new GraphExpansion(Scene(), ExpansionOutcome.Expanded, 2, []));
    }

    public Task<IReadOnlyList<WhyLine>> ExplainClusterAsync(
        ICase @case, GraphNode node, CancellationToken cancellationToken) => Task.FromResult(ClusterLines);

    public Task SavePositionAsync(ICase @case, string nodeKey, GraphLayout layout, GraphPoint place)
    {
        Saved.Add((nodeKey, layout, place));
        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
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
            Graph = new GraphViewModel(Session, Workflow, Source, Exit, new InlineUi());
            Graph.FitWanted += (_, _) => Fits++;
            Graph.GuideWanted += (_, topic) => Guides.Add(topic);
            if (open)
            {
                Session.Open(Case);
            }
        }

        public CaseSession Session { get; } = new();

        public TestTracing Tracing { get; } = new();

        public CaseWorkflow Workflow { get; }

        public TestGraphSource Source { get; } = new();

        public TestExitConnection Exit { get; } = new();

        public TestCase Case { get; } = new("2026-0001");

        public GraphViewModel Graph { get; }

        public int Fits { get; set; }

        public List<GuideTopic> Guides { get; } = [];
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
    }

    /// <summary>An opened case is read by FIFO, shown, placed and fitted to the screen.</summary>
    [Fact]
    public void OpenedCaseIsReadAndFitted()
    {
        var rig = new Rig();

        Assert.Equal([TracingMethodKind.Fifo], rig.Source.Loads);
        Assert.True(rig.Graph.HasGraph);
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
        Assert.False(rig.Graph.HasGraph);
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
        Assert.False(rig.Graph.IsLoading);
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

    /// <summary>Varför? opens and closes, and a section of the Guide is asked for.</summary>
    [Fact]
    public void WhyLinksToTheGuide()
    {
        var rig = new Rig();
        rig.Graph.ToggleWhy();
        Assert.True(rig.Graph.IsWhyOpen);

        rig.Graph.OpenGuide(new WhyLine("x", GuideTopic.Methods));
        rig.Graph.OpenGuide(new WhyLine("y", null));
        rig.Graph.OpenGuideTopic(GuideTopic.Graph);

        Assert.Equal([GuideTopic.Methods, GuideTopic.Graph], rig.Guides);
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
}
