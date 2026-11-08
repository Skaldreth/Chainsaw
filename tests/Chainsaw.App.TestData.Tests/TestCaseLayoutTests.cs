// The test case and its made-up source exist only in a Debug build.
#if DEBUG
using Chainsaw.App.Graph;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.TestData.Tests;

/// <summary>
/// The test case's graph is laid out with air (TODO 34, principle 7): on the free surface and in the hop columns, as
/// built and with the hub expanded, no two nodes overlap, every amount has a place on its line, and no amount lies on
/// another amount or on a node.
/// </summary>
public sealed class TestCaseLayoutTests : IDisposable
{
    /// <summary>The least space between two nodes' frames, so that the graph is airy and never crowded.</summary>
    private const double LeastGap = 24;

    /// <summary>No node has been dragged.</summary>
    private static readonly Dictionary<(string, GraphLayout), GraphPoint> NoMoves = [];

    private readonly Harness _harness = new();

    /// <summary>Removes the harness's temporary folder.</summary>
    public void Dispose() => _harness.Dispose();

    /// <summary>The layouts and whether the hub is expanded, every combination.</summary>
    public static TheoryData<GraphLayout, bool> Layouts() => new()
    {
        { GraphLayout.Free, false },
        { GraphLayout.HopColumns, false },
        { GraphLayout.Free, true },
        { GraphLayout.HopColumns, true },
    };

    /// <summary>No two nodes overlap, and each keeps a gap to the others.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="hubExpanded">Whether the hub is expanded, which brings its piles and held-back clue.</param>
    [Theory]
    [MemberData(nameof(Layouts))]
    public async Task No_two_nodes_overlap(GraphLayout layout, bool hubExpanded)
    {
        var (scene, places, _) = await ArrangeAsync(layout, hubExpanded);

        var frames = places.Select(pair => (pair.Key, Frame: scene.FrameOf(pair.Key, pair.Value))).ToList();
        for (var i = 0; i < frames.Count; i++)
        {
            for (var j = i + 1; j < frames.Count; j++)
            {
                Assert.False(
                    frames[i].Frame.Inflate(LeastGap / 2).Intersects(frames[j].Frame.Inflate(LeastGap / 2)),
                    $"{frames[i].Key} and {frames[j].Key} are closer than {LeastGap}.");
            }
        }
    }

    /// <summary>Every amount has a place, on no other amount and on no node.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="hubExpanded">Whether the hub is expanded.</param>
    [Theory]
    [MemberData(nameof(Layouts))]
    public async Task Every_amount_has_a_place_and_no_two_amounts_collide(GraphLayout layout, bool hubExpanded)
    {
        var (scene, places, spots) = await ArrangeAsync(layout, hubExpanded);

        var labelled = scene.Edges.Where(edge => edge.Label.Length > 0).ToList();
        Assert.NotEmpty(labelled);
        Assert.All(labelled, edge => Assert.True(spots.ContainsKey(edge.Key), $"{edge.Label} has no place."));
        var boxes = spots.ToList();
        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                Assert.False(boxes[i].Value.Box.Intersects(boxes[j].Value.Box), $"{boxes[i].Key} meets {boxes[j].Key}.");
            }

            foreach (var (node, place) in places)
            {
                Assert.False(
                    boxes[i].Value.Box.Intersects(scene.FrameOf(node, place)),
                    $"The amount of {boxes[i].Key} lies on {node}.");
            }
        }
    }

    /// <summary>Builds the test case and lays out its graph as a newly opened case shows it.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="hubExpanded">Whether to expand the hub first.</param>
    /// <returns>The scene, every node's place and every amount's place.</returns>
    private async Task<(GraphScene Scene, IReadOnlyDictionary<string, GraphPoint> Places,
        IReadOnlyDictionary<string, AmountSpot> Spots)> ArrangeAsync(GraphLayout layout, bool hubExpanded)
    {
        var builder = _harness.Get<TestCaseBuilder>();
        var build = await builder.BuildAsync(builder.NextNumber());
        using var @case = _harness.Get<ICaseStore>().Open(build.CaseNumber);
        if (hubExpanded)
        {
            await _harness.Get<ITracingService>().ExpandAsync(
                @case, ExpansionRequest.ForAddress(_harness.Get<TestActors>().Hub.Address!));
        }

        var source = new GraphSource(
            _harness.Get<ITracingService>(),
            _harness.Get<IAttributionService>(),
            _harness.Get<IClusteringService>(),
            _harness.Get<ILabelLibrary>());
        var scene = await source.LoadAsync(@case, TracingMethodKind.Fifo, CancellationToken.None);
        var places = GraphPlacement.Place(scene.LayoutNodes, layout, scene.Columns, NoMoves);
        var curves = GraphHitTest.Curves(scene, places, layout == GraphLayout.HopColumns);
        return (scene, places, AmountLayout.Place(scene, places, curves));
    }
}
#endif
