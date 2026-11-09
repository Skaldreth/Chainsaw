using Chainsaw.App.Graph;

namespace Chainsaw.App.Tests;

/// <summary>
/// The grid of cells that finds what is on screen or under the pointer (principle 6): it answers exactly as going
/// through the whole graph does, in the same order, wherever the nodes stand and however long the lines are.
/// </summary>
public sealed class GraphIndexTests
{
    [Theory]
    [InlineData(1, 600)]
    [InlineData(2, 4000)]
    [InlineData(3, 60000)]
    public void The_index_answers_as_going_through_the_whole_graph(int seed, double spread)
    {
        var random = new Random(seed);
        var scene = GraphSamples.Scene();
        var places = scene.Nodes.ToDictionary(
            node => node.Key,
            _ => new GraphPoint((random.NextDouble() - 0.5) * spread, (random.NextDouble() - 0.5) * spread),
            StringComparer.Ordinal);
        var curves = GraphHitTest.Curves(scene, places, hopColumns: false);
        var index = new GraphIndex(scene, places, curves);

        for (var probe = 0; probe < 400; probe++)
        {
            var point = probe % 4 == 0 && curves.Count > 0
                ? curves.Values.ElementAt(random.Next(curves.Count)).Middle
                : new GraphPoint((random.NextDouble() - 0.5) * spread, (random.NextDouble() - 0.5) * spread);
            var tolerance = 1 + (random.NextDouble() * 40);
            Assert.Equal(GraphHitTest.At(scene, places, curves, point, tolerance), index.At(point, tolerance));

            var (width, height) = (random.NextDouble() * spread / 2, random.NextDouble() * spread / 2);
            var area = new GraphRect(point.X, point.Y, width, height);
            Assert.Equal(
                GraphHitTest.VisibleNodes(scene, places, area).Select(node => node.Key),
                index.VisibleNodes(area).Select(node => node.Key));
            Assert.Equal(
                GraphHitTest.VisibleEdges(scene, curves, area).Select(edge => edge.Key),
                index.VisibleEdges(area).Select(edge => edge.Key));
        }
    }

    [Fact]
    public void A_node_on_a_cell_border_is_found_from_both_sides()
    {
        var scene = GraphSamples.Scene();
        var places = scene.Nodes.ToDictionary(
            node => node.Key, _ => new GraphPoint(-10_000, -10_000), StringComparer.Ordinal);
        var key = scene.Nodes[0].Key;
        places[key] = new GraphPoint(GraphIndex.CellSize - 20, 0);
        var curves = GraphHitTest.Curves(scene, places, hopColumns: false);
        var index = new GraphIndex(scene, places, curves);

        Assert.Equal(key, index.At(new GraphPoint(GraphIndex.CellSize - 10, 10), 1).Node);
        Assert.Equal(key, index.At(new GraphPoint(GraphIndex.CellSize + 10, 10), 1).Node);
    }

    [Fact]
    public void The_index_is_for_the_scene_places_and_curves_it_was_made_with()
    {
        var scene = GraphSamples.Scene();
        var places = scene.Nodes.ToDictionary(node => node.Key, _ => new GraphPoint(0, 0), StringComparer.Ordinal);
        var curves = GraphHitTest.Curves(scene, places, hopColumns: false);
        var index = new GraphIndex(scene, places, curves);

        Assert.True(index.IsFor(scene, places, curves));
        Assert.False(index.IsFor(scene, new Dictionary<string, GraphPoint>(places), curves));
    }

    [Fact]
    public void An_index_that_follows_a_drag_answers_as_one_made_anew()
    {
        var random = new Random(7);
        var scene = GraphSamples.Scene();
        var places = scene.Nodes.ToDictionary(
            node => node.Key,
            _ => new GraphPoint(random.NextDouble() * 3000, random.NextDouble() * 3000),
            StringComparer.Ordinal);
        var curves = GraphHitTest.Curves(scene, places, hopColumns: false);
        var index = new GraphIndex(scene, places, curves);

        for (var step = 0; step < 30; step++)
        {
            var moved = new Dictionary<string, GraphPoint>(places, StringComparer.Ordinal);
            var key = scene.Nodes[random.Next(scene.Nodes.Count)].Key;
            moved[key] = new GraphPoint(random.NextDouble() * 3000, random.NextDouble() * 3000);
            var newCurves = GraphMoved.KeepSameCurves(curves, GraphHitTest.Curves(scene, moved, hopColumns: false));
            (places, curves) = (moved, newCurves);

            Assert.True(index.Follow(scene, places, curves));
            Assert.True(index.IsFor(scene, places, curves));
            var area = new GraphRect(random.NextDouble() * 2000, random.NextDouble() * 2000, 900, 700);
            var point = new GraphPoint(random.NextDouble() * 3000, random.NextDouble() * 3000);
            Assert.Equal(
                GraphHitTest.VisibleNodes(scene, places, area).Select(node => node.Key),
                index.VisibleNodes(area).Select(node => node.Key));
            Assert.Equal(
                GraphHitTest.VisibleEdges(scene, curves, area).Select(edge => edge.Key),
                index.VisibleEdges(area).Select(edge => edge.Key));
            Assert.Equal(GraphHitTest.At(scene, places, curves, point, 12), index.At(point, 12));
        }
    }

    [Fact]
    public void Absurd_places_never_overflow_or_hang_the_grid()
    {
        var scene = GraphSamples.Scene();
        var places = scene.Nodes.ToDictionary(node => node.Key, _ => new GraphPoint(0, 0), StringComparer.Ordinal);
        places[scene.Nodes[0].Key] = new GraphPoint(1e300, -1e300);
        var curves = GraphHitTest.Curves(scene, places, hopColumns: false);
        var index = new GraphIndex(scene, places, curves);
        var everything = new GraphRect(-1e301, -1e301, 2e301, 2e301);

        Assert.Equal(
            GraphHitTest.VisibleEdges(scene, curves, everything).Select(edge => edge.Key),
            index.VisibleEdges(everything).Select(edge => edge.Key));
        Assert.Equal(GraphHitTest.At(scene, places, curves, new(5, 5), 6), index.At(new(5, 5), 6));
    }

    [Fact]
    public void Another_scene_is_not_followed()
    {
        var scene = GraphSamples.Scene();
        var places = scene.Nodes.ToDictionary(node => node.Key, _ => new GraphPoint(0, 0), StringComparer.Ordinal);
        var curves = GraphHitTest.Curves(scene, places, hopColumns: false);
        var index = new GraphIndex(scene, places, curves);

        Assert.False(index.Follow(GraphSamples.Scene(), places, curves));
    }
}
