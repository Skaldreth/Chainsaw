using System.Diagnostics;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Tracing.Contracts;
using Xunit.Abstractions;

namespace Chainsaw.App.Tests;

/// <summary>
/// The graph at thousands of actors (principle 6, BACKLOG <em>Grafens prestanda vid tusentals noder</em>): the index
/// finds the same nodes and lines as looking through them all, and a drag works out only what depends on the dragged
/// node, with the same curves as the whole layout. The times are written to the test's output, never asserted, since
/// they depend on the machine.
/// </summary>
/// <param name="output">Where the times are written.</param>
public sealed class GraphScaleTests(ITestOutputHelper output)
{
    /// <summary>How many actors the large graph has.</summary>
    private const int Large = 3000;

    /// <summary>The node dragged in the tests, one deep in the tree with lines both ways.</summary>
    private static readonly string Dragged = GraphScaleSamples.NodeKey(500);

    /// <summary>The large scene, built once since it takes a while.</summary>
    private static readonly Lazy<GraphScene> LargeScene = new(() => GraphScaleSamples.Scene(Large));

    /// <summary>A drag's moves give the same curves as working out the whole layout at the end place.</summary>
    /// <param name="layout">The layout dragged in.</param>
    [Theory]
    [InlineData(GraphLayout.Free)]
    [InlineData(GraphLayout.HopColumns)]
    public void DragGivesTheSameCurvesAsTheWholeLayout(GraphLayout layout)
    {
        var scene = LargeScene.Value;
        var hop = layout == GraphLayout.HopColumns;
        var places = GraphScaleSamples.Places(scene, layout);
        var shown = SceneLayout.Build(scene, places, hop);
        var start = places[Dragged];
        var end = start;
        for (var step = 1; step <= 12; step++)
        {
            end = GraphPlacement.Drag(layout, start, new GraphPoint(start.X - (step * 40), start.Y + (step * 55)));
            shown.Move(new Dictionary<string, GraphPoint> { [Dragged] = end });
        }

        var whole = SceneLayout.Build(scene, new Dictionary<string, GraphPoint>(places) { [Dragged] = end }, hop);
        Assert.Equal(whole.Places, shown.Places);
        Assert.Equal(whole.Curves.Count, shown.Curves.Count);
        Assert.All(whole.Curves, pair => Assert.Equal(pair.Value, shown.Curves[pair.Key]));
    }

    /// <summary>After a drag no amount lies on another or on a node, though only the dragged lines were placed again.</summary>
    [Fact]
    public void DraggedAmountsKeepTheirRoom()
    {
        var scene = LargeScene.Value;
        var places = GraphScaleSamples.Places(scene, GraphLayout.Free);
        var shown = SceneLayout.Build(scene, places, hopColumns: false);
        var to = new GraphPoint(places[Dragged].X + 300, places[Dragged].Y + 90);
        shown.Move(new Dictionary<string, GraphPoint> { [Dragged] = to });
        var moved = shown.Amounts.Where(pair => DraggedLine(scene, pair.Key)).Select(pair => pair.Value.Box).ToList();
        Assert.NotEmpty(moved);
        var frames = shown.Places.Select(pair => scene.FrameOf(pair.Key, pair.Value).Inflate(AmountLayout.NodeMargin));
        Assert.All(moved, box => Assert.DoesNotContain(frames, frame => frame.Intersects(box)));
        Assert.All(moved, box => Assert.Single(shown.Amounts.Values, spot => spot.Box.Intersects(box)));
    }

    /// <summary>The index finds the same node or line under the pointer, and the same on screen, as looking at all.</summary>
    /// <param name="layout">The layout.</param>
    [Theory]
    [InlineData(GraphLayout.Free)]
    [InlineData(GraphLayout.HopColumns)]
    public void IndexFindsWhatLookingThroughAllFinds(GraphLayout layout)
    {
        var scene = LargeScene.Value;
        var hop = layout == GraphLayout.HopColumns;
        var places = GraphScaleSamples.Places(scene, layout);
        var shown = SceneLayout.Build(scene, places, hop);
        var random = new Random(7);
        for (var probe = 0; probe < 300; probe++)
        {
            var point = probe % 3 == 0
                ? shown.Curves.ElementAt(random.Next(shown.Curves.Count)).Value.At(random.NextDouble())
                : new GraphPoint(random.NextDouble() * 6000, random.NextDouble() * 6000);
            Assert.Equal(GraphHitTest.At(scene, places, shown.Curves, point, 6), shown.HitTest(point, 6));
        }

        foreach (var visible in new[] { new GraphRect(0, 0, 1600, 900), new GraphRect(2000, 3000, 800, 600) })
        {
            Assert.Equal(
                GraphHitTest.VisibleNodes(scene, places, visible).Select(node => node.Key),
                shown.VisibleNodes(visible).Select(node => node.Key));
            Assert.Equal(
                GraphHitTest.VisibleEdges(scene, shown.Curves, visible).Select(edge => edge.Key),
                shown.VisibleEdges(visible).Select(edge => edge.Key));
        }
    }

    /// <summary>The index takes a moved node out of where it stood: it is found at its new place only.</summary>
    [Fact]
    public void MovedNodeIsFoundWhereItNowStands()
    {
        var scene = LargeScene.Value;
        var places = GraphScaleSamples.Places(scene, GraphLayout.Free);
        var shown = SceneLayout.Build(scene, places, hopColumns: false);
        var before = scene.FrameOf(Dragged, places[Dragged]).Centre;
        var to = new GraphPoint(-4000, -4000);
        shown.Move(new Dictionary<string, GraphPoint> { [Dragged] = to });
        Assert.NotEqual(Dragged, shown.HitTest(before, 6).Node);
        Assert.Equal(Dragged, shown.HitTest(scene.FrameOf(Dragged, to).Centre, 6).Node);
    }

    /// <summary>
    /// Measures a drag step, a hover and what is on screen, before (every line looked at) and after (the index and the
    /// drag in part), and writes the times; the numbers in DEV-LOG come from here.
    /// </summary>
    /// <param name="layout">The layout.</param>
    [Theory]
    [InlineData(GraphLayout.Free)]
    [InlineData(GraphLayout.HopColumns)]
    public void MeasureLargeGraph(GraphLayout layout)
    {
        var scene = LargeScene.Value;
        var hop = layout == GraphLayout.HopColumns;
        var places = GraphScaleSamples.Places(scene, layout);
        var shown = SceneLayout.Build(scene, places, hop);
        var visible = new GraphRect(0, 0, 1600, 900);
        const int Steps = 10;

        var before = Time(Steps, step =>
        {
            var moved = new Dictionary<string, GraphPoint>(places) { [Dragged] = Step(layout, places, step) };
            var curves = GraphHitTest.Curves(scene, moved, hop);
            AmountLayout.Place(scene, moved, curves);
        });
        var after = Time(Steps, step => shown.Move(new Dictionary<string, GraphPoint>
        {
            [Dragged] = Step(layout, places, step),
        }));
        var hoverBefore = Time(100, step =>
            GraphHitTest.At(scene, places, shown.Curves, new GraphPoint(400 + step, 300 + step), 6));
        var hoverAfter = Time(100, step => shown.HitTest(new GraphPoint(400 + step, 300 + step), 6));
        var screenBefore = Time(100, _ => _ = GraphHitTest.VisibleNodes(scene, places, visible).Count()
            + GraphHitTest.VisibleEdges(scene, shown.Curves, visible).Count());
        var screenAfter = Time(100, _ => _ = shown.VisibleNodes(visible).Count() + shown.VisibleEdges(visible).Count());
        var whole = Time(3, _ => SceneLayout.Build(scene, places, hop));

        output.WriteLine($"{layout}, {scene.Nodes.Count} actors, {scene.Edges.Count} lines (ms, before → after):");
        output.WriteLine($"  drag step {before:F2} → {after:F2}");
        output.WriteLine($"  hover     {hoverBefore:F3} → {hoverAfter:F3}");
        output.WriteLine($"  on screen {screenBefore:F3} → {screenAfter:F3}");
        output.WriteLine($"  the whole layout, when a drag ends or the graph changes: {whole:F1}");
    }

    /// <summary>Says whether a line has the dragged node at one end.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="key">The line's key.</param>
    /// <returns><see langword="true"/> when it has.</returns>
    private static bool DraggedLine(GraphScene scene, string key) =>
        scene.Edge(key) is { } edge && (edge.Edge.From == Dragged || edge.Edge.To == Dragged);

    /// <summary>Gives where the dragged node is at a step of a made-up drag.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="places">The places at the start.</param>
    /// <param name="step">The step.</param>
    /// <returns>The place.</returns>
    private static GraphPoint Step(GraphLayout layout, IReadOnlyDictionary<string, GraphPoint> places, int step)
    {
        var start = places[Dragged];
        return GraphPlacement.Drag(layout, start, new GraphPoint(start.X + (step * 5), start.Y + (step * 7)));
    }

    /// <summary>Gives the mean time of an action run a number of times, in milliseconds.</summary>
    /// <param name="times">How many times.</param>
    /// <param name="action">The action, given the run's number from 1.</param>
    /// <returns>The mean time.</returns>
    private static double Time(int times, Action<int> action)
    {
        var watch = Stopwatch.StartNew();
        for (var step = 1; step <= times; step++)
        {
            action(step);
        }

        return watch.Elapsed.TotalMilliseconds / times;
    }
}
