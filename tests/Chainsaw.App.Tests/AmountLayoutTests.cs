using Chainsaw.App.Graph;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>
/// The graph's air (TODO 34): amounts stand on their lines without lying on each other or on a node, lines between the
/// same two nodes are bent apart, and fitting never zooms out past readable.
/// </summary>
public sealed class AmountLayoutTests
{
    /// <summary>No node has been dragged.</summary>
    private static readonly Dictionary<(string, GraphLayout), GraphPoint> NoMoves = [];

    /// <summary>A line with room around it has its amount halfway, centred on the line.</summary>
    [Fact]
    public void A_free_amount_stands_halfway()
    {
        var (scene, places, curves) = Arrange(GraphLayout.Free);

        var spots = AmountLayout.Place(scene, places, curves);

        var spot = spots[GraphSamples.VU];
        Assert.Equal(curves[GraphSamples.VU].Middle, spot.Anchor);
        Assert.Equal(spot.Anchor.X, spot.Box.Centre.X, 6);
    }

    /// <summary>Every amount of the sample graph has a place, on no other amount and on no node.</summary>
    [Theory]
    [InlineData(GraphLayout.Free)]
    [InlineData(GraphLayout.HopColumns)]
    public void No_amount_lies_on_another_or_on_a_node(GraphLayout layout)
    {
        var (scene, places, curves) = Arrange(layout);

        var spots = AmountLayout.Place(scene, places, curves).Values.ToList();

        Assert.Equal(scene.Edges.Count(edge => edge.Label.Length > 0), spots.Count);
        for (var i = 0; i < spots.Count; i++)
        {
            Assert.DoesNotContain(places.Values, place => GraphSizes.NodeFrame(place).Intersects(spots[i].Box));
            for (var j = i + 1; j < spots.Count; j++)
            {
                Assert.False(spots[i].Box.Intersects(spots[j].Box));
            }
        }
    }

    /// <summary>An amount whose middle would lie on a node moves along its line until it is clear of it.</summary>
    [Fact]
    public void An_amount_on_a_node_moves_along_its_line()
    {
        var (scene, places, curves) = Arrange(GraphLayout.Free);
        var middle = curves[GraphSamples.VU].Middle;
        var crowded = new Dictionary<string, GraphPoint>(places)
        {
            ["in-the-way"] = new(middle.X - (GraphSizes.NodeWidth / 2), middle.Y - (GraphSizes.NodeHeight / 2)),
        };

        var spot = AmountLayout.Place(scene, crowded, curves)[GraphSamples.VU];

        Assert.NotEqual(middle, spot.Anchor);
        Assert.False(GraphSizes.NodeFrame(crowded["in-the-way"]).Intersects(spot.Box));
        Assert.True(curves[GraphSamples.VU].DistanceTo(spot.Anchor) <= AmountLayout.TextHeight);
    }

    /// <summary>An amount with no free place on its line is left for hovering, never laid on something.</summary>
    [Fact]
    public void An_amount_without_room_is_left_for_hovering()
    {
        var (scene, places, curves) = Arrange(GraphLayout.Free);
        var bounds = curves[GraphSamples.VU].Bounds().Inflate(40);
        var crowded = new Dictionary<string, GraphPoint>(places);
        for (var x = bounds.X; x < bounds.Right; x += GraphSizes.NodeWidth)
        {
            for (var y = bounds.Y; y < bounds.Bottom; y += GraphSizes.NodeHeight)
            {
                crowded[$"wall {x} {y}"] = new GraphPoint(x, y);
            }
        }

        Assert.False(AmountLayout.Place(scene, crowded, curves).ContainsKey(GraphSamples.VU));
    }

    /// <summary>A longer amount takes more room, and a value and a swap make it taller.</summary>
    [Fact]
    public void The_box_follows_what_the_amount_says()
    {
        var edge = GraphSamples.Scene().Edge(GraphSamples.VU)!;
        var anchor = new GraphPoint(100, 100);

        var plain = AmountLayout.BoxAt(edge, anchor);
        var longer = AmountLayout.BoxAt(edge with { Label = edge.Label + " av 2,0000" }, anchor);
        var valued = AmountLayout.BoxAt(edge with { Value = new EdgeValue(546_000, "SEK", []), Swap = "₮ → Ξ" }, anchor);

        Assert.True(longer.Width > plain.Width);
        Assert.Equal(
            plain.Height + AmountLayout.ValueHeight + AmountLayout.SwapHeight, valued.Height, 6);
        Assert.Equal(anchor.Y - (AmountLayout.TextHeight / 2) - AmountLayout.PaddingY, plain.Y, 6);
    }

    /// <summary>Money that goes back and forth between two nodes is drawn as two lines bent apart.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lines_between_the_same_two_nodes_are_bent_apart(bool hopColumns)
    {
        var (scene, places, _) = Arrange(hopColumns ? GraphLayout.HopColumns : GraphLayout.Free);

        var curves = GraphHitTest.Curves(scene, places, hopColumns);

        var there = curves[GraphSamples.KE];
        var back = curves[GraphSamples.EK];
        Assert.NotEqual(there.Middle, back.Middle);
        if (!hopColumns)
        {
            // On the free surface the two lines share their ends, so only the bend keeps them apart.
            Assert.Equal(EdgeCurve.ParallelSpacing, Math.Abs(there.Middle.Y - back.Middle.Y), 6);
        }
    }

    /// <summary>Bending keeps the ends and moves the middle by the distance asked.</summary>
    [Fact]
    public void Bending_moves_only_the_middle()
    {
        var curve = EdgeCurve.Between(new GraphPoint(0, 0), new GraphPoint(300, 100), hopColumns: false);

        var bent = curve.Bent(-14);

        Assert.Equal(curve.Start, bent.Start);
        Assert.Equal(curve.End, bent.End);
        Assert.Equal(curve.Middle.Y - 14, bent.Middle.Y, 6);
    }

    /// <summary>
    /// A graph too large for the screen is fitted no smaller than readable, from its top-left corner where the money
    /// starts; one that fits is centred.
    /// </summary>
    [Fact]
    public void Fitting_stops_at_readable_and_shows_the_start()
    {
        var content = new GraphRect(16, 36, 4000, 1000);

        var fitted = Viewport.Fit(content, 1000, 800);

        Assert.Equal(Viewport.ReadableZoom, fitted.Zoom);
        Assert.Equal(new GraphPoint(Viewport.FitMargin, Viewport.FitMargin), fitted.ToScreen(new(16, 36)));
        var middling = Viewport.Fit(new GraphRect(0, 0, 1100, 500), 1000, 800);
        Assert.True(middling.Zoom is > Viewport.ReadableZoom and < 1);
        Assert.Equal(500, middling.ToScreen(new GraphPoint(550, 250)).X, 6);
    }

    /// <summary>Lays out the sample graph in a layout.</summary>
    /// <param name="layout">The layout.</param>
    /// <returns>The scene, the places and the curves.</returns>
    private static (GraphScene Scene, IReadOnlyDictionary<string, GraphPoint> Places,
        IReadOnlyDictionary<string, EdgeCurve> Curves) Arrange(GraphLayout layout)
    {
        var scene = GraphSamples.Scene();
        var places = GraphPlacement.Place(scene.LayoutNodes, layout, scene.Columns, NoMoves);
        return (scene, places, GraphHitTest.Curves(scene, places, layout == GraphLayout.HopColumns));
    }
}
