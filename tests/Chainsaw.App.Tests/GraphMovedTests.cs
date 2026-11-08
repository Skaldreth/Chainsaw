using Chainsaw.App.Graph;

namespace Chainsaw.App.Tests;

/// <summary>
/// What a drag moved (principle 6): only the nodes whose place changed are drawn again over the still ones, and a
/// curve that did not change keeps its instance so that its drawn geometry is used again; without WPF.
/// </summary>
public sealed class GraphMovedTests
{
    [Fact]
    public void Only_the_nodes_whose_place_changed_moved()
    {
        var before = new Dictionary<string, GraphPoint> { ["a"] = new(0, 0), ["b"] = new(200, 0), ["c"] = new(400, 0) };
        var after = new Dictionary<string, GraphPoint> { ["a"] = new(0, 0), ["b"] = new(210, 30), ["d"] = new(0, 90) };

        var moved = GraphMoved.Between(before, after);

        Assert.Equal(["b", "c", "d"], moved.Nodes.Order());
        Assert.False(moved.IsEmpty);
    }

    [Fact]
    public void Nothing_moved_when_every_place_is_the_same()
    {
        var places = new Dictionary<string, GraphPoint> { ["a"] = new(0, 0) };

        Assert.True(GraphMoved.Between(places, new Dictionary<string, GraphPoint>(places)).IsEmpty);
    }

    [Fact]
    public void A_curve_that_did_not_change_keeps_its_instance()
    {
        var still = EdgeCurve.Between(new GraphPoint(0, 0), new GraphPoint(100, 0), hopColumns: false);
        var dragged = EdgeCurve.Between(new GraphPoint(0, 50), new GraphPoint(100, 50), hopColumns: false);
        var before = new Dictionary<string, EdgeCurve> { ["still"] = still, ["dragged"] = dragged };
        var after = new Dictionary<string, EdgeCurve>
        {
            ["still"] = EdgeCurve.Between(new GraphPoint(0, 0), new GraphPoint(100, 0), hopColumns: false),
            ["dragged"] = EdgeCurve.Between(new GraphPoint(0, 80), new GraphPoint(100, 50), hopColumns: false),
            ["new"] = still,
        };

        var kept = GraphMoved.KeepSameCurves(before, after);

        Assert.Same(still, kept["still"]);
        Assert.Same(after["dragged"], kept["dragged"]);
        Assert.Same(still, kept["new"]);
    }

    [Fact]
    public void The_dragged_nodes_count_as_moved_even_where_they_stood_still()
    {
        var places = new Dictionary<string, GraphPoint> { ["a"] = new(0, 0), ["b"] = new(200, 0) };

        var moved = GraphMoved.Between(places, places).With(["b"]);

        Assert.Equal(["b"], moved.Nodes);
    }
}
