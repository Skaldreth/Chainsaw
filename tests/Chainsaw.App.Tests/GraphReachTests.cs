using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// The graph's rests against GR4, GR5, GR6c, TK1a and KF1b, without WPF: nodes as wide as their names, lines bent
/// around the nodes in the hop columns, the swap's pill on its line, the chains' headings, the guide on from a lone
/// start point, the mark for the hidden outputs, the double click, the empty surface's menu and the own area's form
/// beside its actors.
/// </summary>
public sealed class GraphReachTests
{
    /// <summary>The session the graph's view model follows.</summary>
    private readonly CaseSession _session = new();

    /// <summary>The source the graph's view model reads.</summary>
    private readonly TestGraphSource _source = new();

    /// <summary>Opens a case in a new graph's view model and reads its graph.</summary>
    /// <returns>The view model.</returns>
    private async Task<GraphViewModel> Open()
    {
        var workflow = new CaseWorkflow(
            new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), _session);
        var graph = new GraphViewModel(
            _session, workflow, _source, TestWays.For(new TestExitConnection()), new InlineUi(),
            new TestGraphEditor(), new TestClock());
        _session.Open(new TestCase("2026-0077"));
        await graph.ReloadAsync(fit: false);
        return graph;
    }

    /// <summary>The graph with only its start point V, not expanded and with no lines (sketch GR6c).</summary>
    /// <returns>The graph.</returns>
    private static TraceGraph LoneStart()
    {
        var graph = GraphSamples.Graph();
        return graph with
        {
            Nodes = [Node(VKey, GraphNodeKind.Address, [V], hop: 0, start: true, expansion: NodeExpansion.NotExpanded)],
            Edges = [],
        };
    }

    [Fact]
    public void A_node_grows_with_its_name_up_to_the_widest_and_never_with_an_address()
    {
        var look = VisualLanguage.Of(NodeSort.Exchange);

        Assert.Equal(GraphSizes.NodeWidth, NodeWidths.For(look, "Börs", isNamed: true, count: string.Empty));
        Assert.Equal(
            GraphSizes.NodeWidth, NodeWidths.For(look, new string('a', 42), isNamed: false, count: string.Empty));
        var named = NodeWidths.For(look, "Misstänktes plånbok", isNamed: true, count: "3");
        Assert.InRange(named, GraphSizes.NodeWidth + 1, NodeWidths.Widest);
        Assert.Equal(NodeWidths.Widest, NodeWidths.For(look, new string('x', 80), isNamed: true, count: string.Empty));
        Assert.Equal(
            GraphSizes.NodeWidth,
            NodeWidths.For(VisualLanguage.Of(NodeSort.Pile), new string('x', 80), isNamed: true, count: string.Empty));
    }

    [Fact]
    public void A_wide_node_is_hit_and_its_line_leaves_from_its_own_right_edge()
    {
        var labels = new Dictionary<string, IReadOnlyList<Chainsaw.Modules.Attribution.Contracts.Label>>
        {
            [VKey] = [Label(V, Chainsaw.Modules.Attribution.Contracts.LabelKind.Victim, "Offret med ett långt namn")],
        };
        var scene = Scene(labels);
        var wide = scene.Node(VKey)!;
        Assert.True(wide.Width > GraphSizes.NodeWidth);

        var places = new Dictionary<string, GraphPoint>
        {
            [VKey] = new(0, 0),
            [KKey] = new(400, 0),
            [UKey] = new(400, 200),
            [WKey] = new(-400, 0),
            [EKey] = new(800, 0),
        };
        var curves = GraphHitTest.Curves(scene, places, hopColumns: false);

        Assert.Equal(wide.Width, curves[VK].Start.X, 6);
        Assert.Equal(VKey, GraphHitTest.At(scene, places, curves, new GraphPoint(wide.Width - 2, 20), 1).Node);
        Assert.Equal(wide.Width, scene.FrameOf(VKey, places[VKey]).Width);
    }

    [Fact]
    public void A_hop_column_with_a_wide_node_moves_the_next_column_along_and_its_band_holds_it()
    {
        GraphNode[] nodes =
        [
            Node("a", GraphNodeKind.Address, [W], hop: 0),
            Node("b", GraphNodeKind.Address, [V], hop: 1),
            Node("c", GraphNodeKind.Address, [U], hop: 2),
        ];

        var (_, plain) = HopLayout.Arrange(nodes);
        var (lanes, wide) = HopLayout.Arrange(nodes, key => key == "b" ? 200 : GraphSizes.NodeWidth);

        Assert.Equal(plain["b"], wide["b"]);
        Assert.Equal(plain["c"].X + (200 - GraphSizes.NodeWidth), wide["c"].X);
        Assert.Equal(wide["b"].X + 200 + HopLayout.ColumnGap, wide["c"].X);
        Assert.Equal(200, lanes[1].Width);
    }

    [Fact]
    public void A_line_into_the_second_column_of_a_crowded_hop_is_bent_around_the_first_columns_nodes()
    {
        var nodes = Enumerable.Range(0, 10)
            .Select(index => Node("address:n" + index, GraphNodeKind.Address, [Address(300 + index)], hop: 1))
            .Prepend(Node(VKey, GraphNodeKind.Address, [V], hop: 0, start: true))
            .ToList();
        var (_, places) = HopLayout.Arrange(nodes);
        var frames = places.ToDictionary(pair => pair.Key, pair => GraphSizes.NodeFrame(pair.Value));
        var room = LineRouting.Room(frames.Values);

        // The ninth actor stands first in the hop's second column, level with the first column's first actor.
        var (from, to) = (frames[VKey], frames["address:n8"]);
        var straight = EdgeCurve.Between(from, to, hopColumns: true);
        var routed = LineRouting.AroundNodes(straight, from, to, room);

        Assert.True(LineRouting.Crosses(straight, from, to, room));
        Assert.False(LineRouting.Crosses(routed, from, to, room));
        Assert.Equal(straight.Start, routed.Start);
        Assert.Equal(straight.End, routed.End);
    }

    [Fact]
    public void A_line_that_crosses_nothing_is_left_as_it_is()
    {
        var scene = Scene();
        var (_, columns) = HopLayout.Arrange(scene.LayoutNodes);
        var curves = GraphHitTest.Curves(scene, columns, hopColumns: true);
        var plain = scene.FrameOf(VKey, columns[VKey]);

        Assert.Equal(
            EdgeCurve.Between(plain, scene.FrameOf(UKey, columns[UKey]), hopColumns: true).Control1,
            curves[VU].Control1);
    }

    [Fact]
    public void The_swap_pill_stands_on_its_line_and_no_amount_lies_on_it()
    {
        var scene = EthSamples.Scene();
        var sold = scene.Edges.Single(edge => edge.Swap is not null);
        var places = scene.Nodes.Select((node, index) => (node.Key, Place: new GraphPoint(index * 300, 0)))
            .ToDictionary(pair => pair.Key, pair => pair.Place);
        var curves = GraphHitTest.Curves(scene, places, hopColumns: false);

        var pill = AmountLayout.SwapBox(sold, curves[sold.Key])!.Value;
        var spots = AmountLayout.Place(scene, places, curves);

        Assert.Equal(curves[sold.Key].Middle, pill.Centre);
        Assert.Equal(new SwapMarks("₮", "Ξ"), Assert.Single(sold.SwapPairs));
        Assert.DoesNotContain(spots.Values, spot => spot.Box.Intersects(pill));
        Assert.Null(AmountLayout.SwapBox(scene.Edges.First(edge => edge.Swap is null), curves[sold.Key]));
    }

    [Fact]
    public void The_chains_parts_have_headings_only_when_the_graph_holds_more_than_one_chain()
    {
        var one = Scene();
        var places = HopLayout.Arrange(one.LayoutNodes).Places;
        Assert.Empty(ChainSections.Of(one, places));

        var graph = GraphSamples.Graph();
        var mixed = Scene(graph: graph with
        {
            Nodes = [.. graph.Nodes.Select(node => node.Key == EKey ? node with { Chain = Chain.Ethereum } : node)],
        });
        var moved = new Dictionary<string, GraphPoint>(HopLayout.Arrange(mixed.LayoutNodes).Places)
        {
            [EKey] = new(800, 600),
        };

        var parts = ChainSections.Of(mixed, moved);

        Assert.Equal([Chain.Bitcoin, Chain.Ethereum], parts.Select(part => part.Chain));
        Assert.Equal(new GraphPoint(800, 600 - ChainSections.HeadingLift), parts[1].HeadingAt);
    }

    [Fact]
    public void A_lone_start_point_gets_the_guide_on_beside_it_and_loses_it_with_its_first_line()
    {
        var scene = Scene(graph: LoneStart());
        var places = new Dictionary<string, GraphPoint> { [VKey] = new(100, 200) };

        var guide = StartGuide.For(scene, places)!;

        Assert.Equal(VKey, guide.StartKey);
        Assert.Equal(guide.Start.Right + StartGuide.ExpandGap, guide.Expand.X);
        Assert.Equal(guide.Start.Centre.Y, guide.Expand.Centre.Y, 6);
        Assert.True(guide.Exit.X > guide.Expand.Right);
        Assert.True(guide.Exit.Y > guide.Expand.Y);
        Assert.Null(StartGuide.For(Scene(), HopLayout.Arrange(Scene().LayoutNodes).Places));
        var expanded = Scene(graph: LoneStart() with
        {
            Nodes = [Node(VKey, GraphNodeKind.Address, [V], hop: 0, start: true)],
        });
        Assert.Null(StartGuide.For(expanded, places));
    }

    [Fact]
    public async Task The_guide_says_honestly_it_is_offline_and_a_click_on_it_expands_the_start_point()
    {
        _source.Scene = () => Scene(graph: LoneStart());
        var graph = await Open();

        // No exit is connected in the test, so the guide says honestly that nothing can be fetched (principle 9).
        Assert.NotNull(graph.StartPointGuide);
        Assert.True(graph.IsOffline);
        Assert.Equal(StartGuide.OfflineText, graph.StartGuideText);
        Assert.True(graph.ExpandAt(graph.StartPointGuide!.StartKey));

        Assert.Equal(VKey, graph.SelectedNode);
        Assert.Single(_source.Expansions);
    }

    [Fact]
    public async Task A_double_click_expands_a_node_but_never_a_pile_or_nothing()
    {
        var graph = await Open();

        Assert.False(graph.ExpandAt(null));
        Assert.True(graph.ExpandAt(UKey));
        Assert.Equal(UKey, graph.SelectedNode);
    }

    [Fact]
    public async Task The_empty_surface_menu_fits_zooms_and_begins_a_comment()
    {
        var graph = await Open();
        var moves = new List<ViewMove>();
        graph.ViewMoveWanted += (_, move) => moves.Add(move);

        graph.OpenSurfaceMenu(new GraphPoint(40, 50));
        Assert.True(graph.IsMenuOpen);
        Assert.Null(graph.MenuNode);
        Assert.Equal(
            [NodeMenuAction.Comment, NodeMenuAction.Fit, NodeMenuAction.ZoomIn, NodeMenuAction.ZoomOut],
            graph.MenuRows.Select(row => row.Action));

        graph.RunMenu(graph.MenuRows.Single(row => row.Action == NodeMenuAction.Fit));
        Assert.False(graph.IsMenuOpen);
        graph.OpenSurfaceMenu(new GraphPoint(40, 50));
        graph.RunMenu(graph.MenuRows.Single(row => row.Action == NodeMenuAction.ZoomOut));
        Assert.Equal([ViewMove.Fit, ViewMove.ZoomOut], moves);

        graph.OpenMenu(VKey);
        Assert.Null(graph.SurfaceMenuAt);
        Assert.Equal(VKey, graph.MenuNode);
    }

    [Fact]
    public async Task The_corner_marks_the_hidden_outputs_and_shows_them_again()
    {
        _source.Scene = () => Scene(graph: PileTests.WithOpReturns());
        var graph = await Open();

        Assert.True(graph.HasUnspendable);
        Assert.False(graph.ShowsUnspendable);
        Assert.Contains("1 utgång utan mottagare", graph.UnspendableToolTip, StringComparison.Ordinal);
        Assert.Contains("visa", graph.UnspendableToolTip, StringComparison.Ordinal);

        graph.ToggleUnspendableCommand.Execute(null);

        Assert.True(graph.ShowsUnspendable);
        Assert.Contains("dölja", graph.UnspendableToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_graph_without_hidden_outputs_has_no_mark()
    {
        var graph = await Open();

        Assert.False(graph.HasUnspendable);
    }

    [Fact]
    public async Task The_own_area_form_is_about_the_picked_actors()
    {
        var graph = await Open();
        Assert.Empty(graph.Checks.FormActors);

        graph.Checks.TogglePick(EKey);
        graph.Checks.TogglePick(UKey);
        graph.Checks.OpenCreate(null);

        Assert.Equal([EKey, UKey], graph.Checks.FormActors);
        graph.Checks.CancelForm();
        Assert.Empty(graph.Checks.FormActors);
    }
}
