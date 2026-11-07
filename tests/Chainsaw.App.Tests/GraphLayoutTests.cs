using Chainsaw.App.Graph;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>The graph's geometry without WPF: curves, the viewport, hop columns, places, amounts and hit tests.</summary>
public sealed class GraphLayoutTests
{
    /// <summary>No node has been dragged.</summary>
    private static readonly Dictionary<(string, GraphLayout), GraphPoint> NoMoves = [];

    /// <summary>A forward line runs from the sender's right side to the receiver's left side, its middle halfway.</summary>
    [Fact]
    public void ForwardLineRunsFromRightSideToLeftSide()
    {
        var curve = EdgeCurve.Between(new GraphPoint(0, 0), new GraphPoint(300, 100), hopColumns: false);

        Assert.Equal(new GraphPoint(GraphSizes.NodeWidth, 20), curve.Start);
        Assert.Equal(new GraphPoint(300, 120), curve.End);
        Assert.False(curve.IsArc);
        Assert.Equal(new GraphPoint((GraphSizes.NodeWidth + 300) / 2, 70), curve.Middle);
    }

    /// <summary>On the free surface, a line backwards runs from the sender's left to the receiver's right.</summary>
    [Fact]
    public void BackwardLineOnFreeSurfaceRunsFromLeftSide()
    {
        var curve = EdgeCurve.Between(new GraphPoint(400, 0), new GraphPoint(0, 0), hopColumns: false);

        Assert.Equal(400, curve.Start.X);
        Assert.Equal(GraphSizes.NodeWidth, curve.End.X);
        Assert.False(curve.IsArc);
    }

    /// <summary>In the hop columns, money that turns back is drawn as an arc over both nodes (sketch GR4).</summary>
    [Fact]
    public void MoneyTurningBackInColumnsIsAnArcOverTheTop()
    {
        var curve = EdgeCurve.Between(new GraphPoint(316, 100), new GraphPoint(16, 200), hopColumns: true);

        Assert.True(curve.IsArc);
        Assert.Equal(new GraphPoint(316 + (GraphSizes.NodeWidth / 2), 100), curve.Start);
        Assert.True(curve.Control1.Y < 100 && curve.Control2.Y < 100);
        Assert.True(curve.Middle.Y < 100);
    }

    /// <summary>Two nodes in the same column are joined by an arc too, never by a line through them.</summary>
    [Fact]
    public void SameColumnIsAnArc() =>
        Assert.True(EdgeCurve.Between(new GraphPoint(16, 36), new GraphPoint(16, 164), hopColumns: true).IsArc);

    /// <summary>The distance to a curve is about zero on it and large far from it; the bounds hold the curve.</summary>
    [Fact]
    public void DistanceAndBoundsFollowTheCurve()
    {
        var curve = EdgeCurve.Between(new GraphPoint(0, 0), new GraphPoint(300, 100), hopColumns: false);

        Assert.True(curve.DistanceTo(curve.Middle) < 0.5);
        Assert.True(curve.DistanceTo(new GraphPoint(0, 400)) > 200);
        Assert.True(curve.Bounds().Contains(curve.At(0.3)));
    }

    /// <summary>A point goes to the screen and back unchanged, and zooming keeps the point under the pointer.</summary>
    [Fact]
    public void ViewportKeepsThePointUnderThePointer()
    {
        var viewport = new Viewport(40, -20, 1.5);
        var point = new GraphPoint(120, 80);
        var anchor = new GraphPoint(300, 200);

        Assert.Equal(point, viewport.ToSurface(viewport.ToScreen(point)));
        var zoomed = viewport.ZoomAt(anchor, 1.15);
        var before = viewport.ToSurface(anchor);
        var after = zoomed.ToSurface(anchor);
        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
    }

    /// <summary>The zoom stays within its bounds however far the wheel turns.</summary>
    [Fact]
    public void ZoomIsClamped()
    {
        var viewport = Viewport.Initial;
        for (var i = 0; i < 100; i++)
        {
            viewport = viewport.ZoomAt(new GraphPoint(0, 0), 2);
        }

        Assert.Equal(Viewport.MaximumZoom, viewport.Zoom);
        Assert.Equal(Viewport.MinimumZoom, new Viewport(0, 0, 0.001).Zoom);
    }

    /// <summary>The visible part of the surface follows the pan and the zoom.</summary>
    [Fact]
    public void VisiblePartFollowsPanAndZoom()
    {
        var visible = new Viewport(-100, -50, 2).Visible(800, 600);

        Assert.Equal(new GraphRect(50, 25, 400, 300), visible);
        Assert.Equal(new GraphRect(10, 0, 800, 600), Viewport.Initial.Pan(-10, 0).Visible(800, 600));
    }

    /// <summary>Fitting centres the graph and never zooms in past its natural size; with no room it does nothing.</summary>
    [Fact]
    public void FitCentresWithoutZoomingIn()
    {
        var small = Viewport.Fit(new GraphRect(0, 0, 100, 100), 1000, 800);
        var large = Viewport.Fit(new GraphRect(0, 0, 4000, 1000), 1000, 800);

        Assert.Equal(1, small.Zoom);
        Assert.Equal(new GraphPoint(500, 400), small.ToScreen(new GraphPoint(50, 50)));
        Assert.True(large.Zoom < 1);
        Assert.True(large.Visible(1000, 800).Contains(new GraphPoint(4000, 1000)));
        Assert.Equal(Viewport.Initial, Viewport.Fit(new GraphRect(0, 0, 10, 10), 0, 0));
    }

    /// <summary>The columns start at the earliest hop, are named from the start and keep one for the rest.</summary>
    [Fact]
    public void HopColumnsFollowTheHops()
    {
        GraphNode[] nodes =
        [
            GraphSamples.Node("a", GraphNodeKind.Address, [GraphSamples.W], hop: -1),
            GraphSamples.Node("b", GraphNodeKind.Address, [GraphSamples.V], hop: 0),
            GraphSamples.Node("c", GraphNodeKind.Address, [GraphSamples.U], hop: 1),
            GraphSamples.Node("d", GraphNodeKind.Address, [GraphSamples.E], hop: 1),
            GraphSamples.Node("e", GraphNodeKind.Address, [GraphSamples.K1], hop: null),
        ];

        var (lanes, places) = HopLayout.Arrange(nodes);

        Assert.Equal(["−1", "Start", "+1", HopLayout.NoHopName], lanes.Select(lane => lane.Name));
        Assert.Equal(HopLayout.ColumnX(0), places["a"].X);
        Assert.Equal(HopLayout.ColumnX(2), places["c"].X);
        Assert.Equal(places["c"].Y + GraphSizes.RowSpacing, places["d"].Y);
        Assert.Equal(HopLayout.ColumnX(3), places["e"].X);
        Assert.Equal(GraphSizes.Top, places["e"].Y);
    }

    /// <summary>
    /// The free surface starts from the hop columns, uses the case's place and then the dragged one; the hop columns
    /// keep each node in its column at the height it was given.
    /// </summary>
    [Fact]
    public void EachLayoutRemembersItsPlaces()
    {
        var saved = new Dictionary<GraphLayout, NodePosition>
        {
            [GraphLayout.Free] = new(500, 600),
            [GraphLayout.HopColumns] = new(999, 300),
        };
        GraphNode[] nodes =
        [
            GraphSamples.Node("a", GraphNodeKind.Address, [GraphSamples.V], hop: 0),
            GraphSamples.Node("b", GraphNodeKind.Address, [GraphSamples.U], hop: 1, positions: saved),
        ];
        var (_, columns) = HopLayout.Arrange(nodes);
        var moved = new Dictionary<(string, GraphLayout), GraphPoint> { [("a", GraphLayout.Free)] = new(7, 8) };

        var free = GraphPlacement.Place(nodes, GraphLayout.Free, columns, moved);
        var hops = GraphPlacement.Place(nodes, GraphLayout.HopColumns, columns, moved);

        Assert.Equal(new GraphPoint(7, 8), free["a"]);
        Assert.Equal(new GraphPoint(500, 600), free["b"]);
        Assert.Equal(columns["a"], hops["a"]);
        Assert.Equal(new GraphPoint(columns["b"].X, 300), hops["b"]);
    }

    /// <summary>In the hop columns a node is dragged only up and down, and never above the columns' names.</summary>
    [Fact]
    public void DragInColumnsKeepsTheColumn()
    {
        var start = new GraphPoint(174, 100);

        var dragged = GraphPlacement.Drag(GraphLayout.HopColumns, start, new GraphPoint(900, 250));
        Assert.Equal(new GraphPoint(174, 250), dragged);
        Assert.Equal(GraphPlacement.HighestInColumns, GraphPlacement.Drag(GraphLayout.HopColumns, start, new(0, -50)).Y);
        Assert.Equal(new GraphPoint(900, -50), GraphPlacement.Drag(GraphLayout.Free, start, new GraphPoint(900, -50)));
    }

    /// <summary>Amounts are written in Swedish with at least four decimals, and never rounded to zero.</summary>
    [Fact]
    public void AmountsAreWrittenHonestly()
    {
        Assert.Equal("0,5120", AmountText.Btc(GraphSamples.Btc(0.512m)));
        Assert.Equal("0,00012345", AmountText.Btc(GraphSamples.Btc(0.00012345m)));
        Assert.Equal("0,00000001", AmountText.Btc(new Satoshi(1)));
        Assert.EndsWith("250,0000", AmountText.Btc(GraphSamples.Btc(1250m)), StringComparison.Ordinal);
        Assert.Equal("0,1570 av 0,2400", AmountText.OnLine(GraphSamples.Btc(0.24m), GraphSamples.Btc(0.157m)));
        Assert.Equal("0,2400", AmountText.OnLine(GraphSamples.Btc(0.24m), GraphSamples.Btc(0.24m)));
        Assert.Equal("0,2400", AmountText.OnLine(GraphSamples.Btc(0.24m), Satoshi.Zero));
    }

    /// <summary>The amounts button steps all, traced, none and back, and each mode shows what it says.</summary>
    [Fact]
    public void AmountModesGallraTheLines()
    {
        Assert.Equal(AmountLabelMode.Traced, AmountLabelMode.All.Next());
        Assert.Equal(AmountLabelMode.None, AmountLabelMode.Traced.Next());
        Assert.Equal(AmountLabelMode.All, AmountLabelMode.None.Next());
        Assert.Equal("Spårade belopp", AmountLabelMode.Traced.Describe());

        Assert.True(AmountLabelMode.All.Shows(isTraced: false, isHovered: false, focus: false, isNear: false));
        Assert.False(AmountLabelMode.Traced.Shows(isTraced: false, isHovered: false, focus: false, isNear: false));
        Assert.True(AmountLabelMode.Traced.Shows(isTraced: true, isHovered: false, focus: false, isNear: false));
        Assert.False(AmountLabelMode.None.Shows(isTraced: true, isHovered: false, focus: false, isNear: false));
        Assert.True(AmountLabelMode.None.Shows(isTraced: false, isHovered: true, focus: false, isNear: false));
        Assert.True(AmountLabelMode.None.Shows(isTraced: false, isHovered: false, focus: true, isNear: true));
        Assert.False(AmountLabelMode.All.Shows(isTraced: true, isHovered: false, focus: true, isNear: false));
    }

    /// <summary>A line's thickness is its traced amount against the largest; untraced lines are thin.</summary>
    [Fact]
    public void ThicknessIsTheTracedAmount()
    {
        var largest = GraphSamples.Btc(1m);

        Assert.Equal(EdgeWidth.Untraced, EdgeWidth.For(Satoshi.Zero, largest));
        Assert.Equal(EdgeWidth.ThinnestTraced + EdgeWidth.TracedSpan, EdgeWidth.For(largest, largest));
        Assert.Equal(EdgeWidth.ThinnestTraced + (EdgeWidth.TracedSpan / 2), EdgeWidth.For(GraphSamples.Btc(0.5m), largest));
        Assert.Equal(EdgeWidth.ThinnestTraced + EdgeWidth.TracedSpan, EdgeWidth.For(GraphSamples.Btc(2m), largest));
    }

    /// <summary>A node wins over a line under it, a line is hit near its middle, and empty surface hits nothing.</summary>
    [Fact]
    public void HitTestFindsNodesThenLines()
    {
        var scene = GraphSamples.Scene();
        var places = GraphPlacement.Place(
            scene.Snapshot.Graph.Nodes, GraphLayout.HopColumns, scene.Columns, NoMoves);
        var curves = GraphHitTest.Curves(scene, places, hopColumns: true);
        var v = places[GraphSamples.VKey];

        Assert.Equal((GraphSamples.VKey, null), GraphHitTest.At(scene, places, curves, new(v.X + 5, v.Y + 5), 6));
        Assert.Equal(
            (null, GraphSamples.VU),
            GraphHitTest.At(scene, places, curves, curves[GraphSamples.VU].Middle, 6));
        Assert.Equal((null, null), GraphHitTest.At(scene, places, curves, new GraphPoint(-500, -500), 6));
    }

    /// <summary>Only what reaches into the visible part is drawn (principle 6).</summary>
    [Fact]
    public void OnlyTheVisibleIsDrawn()
    {
        var scene = GraphSamples.Scene();
        var places = GraphPlacement.Place(
            scene.Snapshot.Graph.Nodes, GraphLayout.HopColumns, scene.Columns, NoMoves);
        var curves = GraphHitTest.Curves(scene, places, hopColumns: true);
        var firstColumn = new GraphRect(0, 0, GraphSizes.LaneSpacing - GraphSizes.MarkReach - 20, 1000);

        var nodes = GraphHitTest.VisibleNodes(scene, places, firstColumn).Select(node => node.Key).ToList();
        var edges = GraphHitTest.VisibleEdges(scene, curves, new GraphRect(5000, 5000, 10, 10)).ToList();

        Assert.Equal([GraphSamples.WKey], nodes);
        Assert.Empty(edges);
        var everything = new GraphRect(-1e5, -1e5, 2e5, 2e5);
        Assert.Equal(scene.Edges.Count, GraphHitTest.VisibleEdges(scene, curves, everything).Count());
    }

    /// <summary>A line whose two ends are the same node is not given a curve.</summary>
    [Fact]
    public void LoopsHaveNoCurve()
    {
        var graph = GraphSamples.Graph() with
        {
            Edges = [GraphSamples.Edge("loop", GraphSamples.VKey, GraphSamples.VKey, 1, 0, 0.1m)],
        };
        var scene = GraphSamples.Scene(graph: graph);
        var places = GraphPlacement.Place(
            graph.Nodes, GraphLayout.Free, scene.Columns, new Dictionary<(string, GraphLayout), GraphPoint>());

        Assert.Empty(GraphHitTest.Curves(scene, places, hopColumns: false));
    }
}
