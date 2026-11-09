using Chainsaw.App.Graph;

namespace Chainsaw.App.Tests;

/// <summary>
/// Tests that new nodes come into the free surface cleanly (the user's wish 2026-11-09): receivers in front, senders
/// behind, in the order of what they are tied to, never on a standing node and where their lines cross the least.
/// </summary>
public sealed class ExpansionPlacementTests
{
    /// <summary>Every node is drawn at the graph's width.</summary>
    private static readonly Func<string, double> Width = _ => GraphSizes.NodeWidth;

    /// <summary>A fallback that must not be used.</summary>
    private static readonly Func<string, IReadOnlyDictionary<string, GraphPoint>, GraphPoint> NoFallback =
        (key, _) => throw new InvalidOperationException($"{key} should have been tied to a standing node.");

    /// <summary>An expansion puts the receivers to the right of the node and the senders to its left.</summary>
    [Fact]
    public void ReceiversGoInFrontAndSendersBehind()
    {
        var standing = new Dictionary<string, GraphPoint> { ["a"] = new(1000, 500) };
        var places = ExpansionPlacement.Place(
            ["in1", "out1", "out2", "in2"],
            standing,
            [("in1", "a"), ("in2", "a"), ("a", "out1"), ("a", "out2")],
            Width,
            NoFallback);

        Assert.All(["out1", "out2"], key => Assert.True(places[key].X >= 1000 + GraphSizes.NodeWidth));
        Assert.All(["in1", "in2"], key => Assert.True(places[key].X + GraphSizes.NodeWidth <= 1000));
        Assert.True(places["out1"].Y < places["out2"].Y, "the order by time reads from the top");
        Assert.True(places["in1"].Y < places["in2"].Y, "the order by time reads from the top");
    }

    /// <summary>A fan of receivers is centred on the node it came from.</summary>
    [Fact]
    public void AFanIsCentredOnItsNode()
    {
        var standing = new Dictionary<string, GraphPoint> { ["a"] = new(0, 400) };
        var places = ExpansionPlacement.Place(
            ["b", "c", "d"], standing, [("a", "b"), ("a", "c"), ("a", "d")], Width, NoFallback);

        Assert.Equal(400, places.Values.Average(place => place.Y), 3);
        Assert.Single(places.Values.Select(place => place.X).Distinct());
    }

    /// <summary>New nodes are ordered by the height of what they are tied to, so that their lines do not cross.</summary>
    [Fact]
    public void NewNodesFollowTheHeightOfTheirTies()
    {
        var standing = new Dictionary<string, GraphPoint>
        {
            ["top"] = new(0, 0),
            ["bottom"] = new(0, 600),
        };

        // Listed bottom's first, so that only their ties can put them in the right order.
        var places = ExpansionPlacement.Place(
            ["fromBottom", "fromTop"], standing, [("bottom", "fromBottom"), ("top", "fromTop")], Width, NoFallback);

        Assert.True(places["fromTop"].Y < places["fromBottom"].Y);
    }

    /// <summary>A new node never lands on a standing one.</summary>
    [Fact]
    public void NewNodesNeverCoverStandingOnes()
    {
        var standing = new Dictionary<string, GraphPoint>
        {
            ["a"] = new(0, 300),
            ["there"] = new(GraphSizes.NodeWidth + ExpansionPlacement.ColumnGap, 300),
        };
        var places = ExpansionPlacement.Place(["b"], standing, [("a", "b")], Width, NoFallback);

        var frame = GraphSizes.NodeFrame(places["b"]);
        Assert.All(standing.Values, place => Assert.False(GraphSizes.NodeFrame(place).Intersects(frame)));
    }

    /// <summary>A new node moves away from where its line would cross a standing line, when that costs little.</summary>
    [Fact]
    public void NewNodesAvoidCrossingStandingLines()
    {
        // A line from c, below a, up to d, right of a and level with it: a new receiver of a straight to the right
        // would cut it.
        var right = GraphSizes.NodeWidth + ExpansionPlacement.ColumnGap;
        var standing = new Dictionary<string, GraphPoint>
        {
            ["a"] = new(0, 300),
            ["c"] = new(0, 500),
            ["d"] = new(right, 100),
        };
        var places = ExpansionPlacement.Place(
            ["b"], standing, [("c", "d"), ("a", "b")], Width, NoFallback);

        var line = (new GraphPoint(GraphSizes.NodeWidth, 320), new GraphPoint(places["b"].X, places["b"].Y + 20));
        var other = (new GraphPoint(GraphSizes.NodeWidth, 520), new GraphPoint(right, 120));
        Assert.False(ExpansionPlacement.Crosses(line, other));
    }

    /// <summary>A chain of new nodes is placed outward from what stands, one after the other.</summary>
    [Fact]
    public void AChainOfNewNodesGoesOutward()
    {
        var standing = new Dictionary<string, GraphPoint> { ["a"] = new(0, 0) };
        var places = ExpansionPlacement.Place(["c", "b"], standing, [("a", "b"), ("b", "c")], Width, NoFallback);

        Assert.True(places["b"].X > 0);
        Assert.True(places["c"].X > places["b"].X);
    }

    /// <summary>A node tied to nothing standing gets the caller's place.</summary>
    [Fact]
    public void AnUntiedNodeGetsTheFallback()
    {
        var standing = new Dictionary<string, GraphPoint> { ["a"] = new(0, 0) };
        var places = ExpansionPlacement.Place(
            ["alone"], standing, [], Width, (_, _) => new GraphPoint(42, 42));

        Assert.Equal(new GraphPoint(42, 42), places["alone"]);
    }

    /// <summary>Segments that only meet at an end do not cross; segments that cut each other do.</summary>
    [Fact]
    public void CrossingIsACut()
    {
        var a = (new GraphPoint(0, 0), new GraphPoint(10, 10));
        Assert.True(ExpansionPlacement.Crosses(a, (new GraphPoint(0, 10), new GraphPoint(10, 0))));
        Assert.False(ExpansionPlacement.Crosses(a, (new GraphPoint(0, 0), new GraphPoint(10, 0))));
        var across = (new GraphPoint(-10, 5), new GraphPoint(30, 5));
        Assert.True(ExpansionPlacement.Through(across, new GraphRect(0, 0, 20, 10)));
        Assert.False(ExpansionPlacement.Through(across, new GraphRect(0, 20, 20, 10)));
    }
}
