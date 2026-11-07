namespace Chainsaw.App.Graph;

/// <summary>
/// Finds what is under the pointer and what is on screen, so that the graph handles and draws only that (principle 6).
/// </summary>
public static class GraphHitTest
{
    /// <summary>How close to a line the pointer must be to hit it, in screen pixels.</summary>
    public const double LineTolerance = 6;

    /// <summary>
    /// Gives the node or else the line under a point; a node drawn later lies on top, and a node wins over a line.
    /// </summary>
    /// <param name="scene">The scene.</param>
    /// <param name="places">Each node's top-left corner.</param>
    /// <param name="curves">Each line's curve.</param>
    /// <param name="point">The point on the surface.</param>
    /// <param name="tolerance">How close to a line counts, in the surface's units.</param>
    /// <returns>The node's key or the line's key; both <see langword="null"/> over empty surface.</returns>
    public static (string? Node, string? Edge) At(
        GraphScene scene,
        IReadOnlyDictionary<string, GraphPoint> places,
        IReadOnlyDictionary<string, EdgeCurve> curves,
        GraphPoint point,
        double tolerance)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(curves);
        for (var index = scene.Nodes.Count - 1; index >= 0; index--)
        {
            var key = scene.Nodes[index].Key;
            if (places.TryGetValue(key, out var place) && GraphSizes.NodeFrame(place).Contains(point))
            {
                return (key, null);
            }
        }

        string? nearest = null;
        var best = tolerance;
        foreach (var (key, curve) in curves)
        {
            if (!curve.Bounds().Inflate(tolerance).Contains(point))
            {
                continue;
            }

            var distance = curve.DistanceTo(point);
            if (distance <= best)
            {
                (nearest, best) = (key, distance);
            }
        }

        return (null, nearest);
    }

    /// <summary>Gives the nodes whose frame or marks reach into the visible part of the surface.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="places">Each node's top-left corner.</param>
    /// <param name="visible">The visible part of the surface.</param>
    /// <returns>The visible nodes, in drawing order.</returns>
    public static IEnumerable<NodeView> VisibleNodes(
        GraphScene scene, IReadOnlyDictionary<string, GraphPoint> places, GraphRect visible)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(places);
        return scene.Nodes.Where(node => places.TryGetValue(node.Key, out var place)
            && GraphSizes.NodeFrame(place).Inflate(GraphSizes.MarkReach).Intersects(visible));
    }

    /// <summary>Gives the lines that may cross the visible part of the surface.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="curves">Each line's curve.</param>
    /// <param name="visible">The visible part of the surface.</param>
    /// <returns>The visible lines, in drawing order.</returns>
    public static IEnumerable<EdgeView> VisibleEdges(
        GraphScene scene, IReadOnlyDictionary<string, EdgeCurve> curves, GraphRect visible)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(curves);
        return scene.Edges.Where(edge => curves.TryGetValue(edge.Key, out var curve)
            && curve.Bounds().Inflate(GraphSizes.MarkReach).Intersects(visible));
    }

    /// <summary>Gives every line's curve between its nodes' places.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="places">Each node's top-left corner.</param>
    /// <param name="hopColumns">Whether the graph is sorted in hop columns.</param>
    /// <returns>The curves by the line's key; a line whose ends are not placed, or that loops, is left out.</returns>
    public static IReadOnlyDictionary<string, EdgeCurve> Curves(
        GraphScene scene, IReadOnlyDictionary<string, GraphPoint> places, bool hopColumns)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(places);
        var curves = new Dictionary<string, EdgeCurve>(StringComparer.Ordinal);
        foreach (var edge in scene.Edges)
        {
            if (edge.Edge.From != edge.Edge.To
                && places.TryGetValue(edge.Edge.From, out var from)
                && places.TryGetValue(edge.Edge.To, out var to))
            {
                curves[edge.Key] = EdgeCurve.Between(from, to, hopColumns);
            }
        }

        return curves;
    }
}
