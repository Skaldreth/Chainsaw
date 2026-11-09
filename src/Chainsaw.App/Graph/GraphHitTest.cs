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
            if (places.TryGetValue(key, out var place) && scene.FrameOf(key, place).Contains(point))
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
            && scene.FrameOf(node.Key, place).Inflate(GraphSizes.MarkReach).Intersects(visible));
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

    /// <summary>
    /// Gives every line's curve between its nodes' places; lines between the same two nodes, in either direction, are
    /// bent apart (<see cref="EdgeCurve.ParallelSpacing"/>) so that they never lie on each other, and in the hop
    /// columns a line that would run through a node on its way is bent around it (<see cref="LineRouting"/>).
    /// </summary>
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
        var pairs = scene.Edges
            .Where(edge => edge.Edge.From != edge.Edge.To
                && places.ContainsKey(edge.Edge.From)
                && places.ContainsKey(edge.Edge.To))
            .GroupBy(edge => PairOf(edge.Edge.From, edge.Edge.To));
        var room = hopColumns ? LineRouting.Room(places.Select(pair => scene.FrameOf(pair.Key, pair.Value))) : null;
        foreach (var pair in pairs)
        {
            AddPair([.. pair.OrderBy(edge => edge.Key, StringComparer.Ordinal)], scene, places, room, curves);
        }

        return curves;
    }

    /// <summary>
    /// Works out the curves of the lines between one pair of nodes, bent apart so that they never lie on each other,
    /// and in the hop columns around the nodes on their way.
    /// </summary>
    /// <param name="lines">The pair's lines, in the order of their keys.</param>
    /// <param name="scene">The scene.</param>
    /// <param name="places">Each node's top-left corner; both ends are placed.</param>
    /// <param name="room">The room the nodes take in the hop columns, or <see langword="null"/> on the free surface.</param>
    /// <param name="curves">Where the curves are written, by the line's key.</param>
    /// <param name="lookedIn">
    /// Where the cells of the room each line looked in to be routed are written (<see cref="LineRouting.Route"/>), or
    /// <see langword="null"/>.
    /// </param>
    public static void AddPair(
        IReadOnlyList<EdgeView> lines,
        GraphScene scene,
        IReadOnlyDictionary<string, GraphPoint> places,
        RoomGrid? room,
        IDictionary<string, EdgeCurve> curves,
        IDictionary<string, IReadOnlySet<(int Column, int Row)>>? lookedIn = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(curves);
        for (var index = 0; index < lines.Count; index++)
        {
            var edge = lines[index].Edge;
            var (from, to) = (scene.FrameOf(edge.From, places[edge.From]), scene.FrameOf(edge.To, places[edge.To]));
            var curve = EdgeCurve.Between(from, to, room is not null);
            var offset = (index - ((lines.Count - 1) / 2.0)) * EdgeCurve.ParallelSpacing;
            curve = offset == 0 ? curve : curve.Bent(offset);
            if (room is null)
            {
                curves[lines[index].Key] = curve;
                continue;
            }

            var (routed, cells) = LineRouting.Route(curve, from, to, room);
            curves[lines[index].Key] = routed;
            if (lookedIn is not null)
            {
                lookedIn[lines[index].Key] = cells;
            }
        }
    }

    /// <summary>Gives the two ends of a line in the same order whichever way the money went.</summary>
    /// <param name="from">One end's key.</param>
    /// <param name="to">The other end's key.</param>
    /// <returns>The ends, the lower key first.</returns>
    public static (string, string) PairOf(string from, string to) =>
        string.CompareOrdinal(from, to) <= 0 ? (from, to) : (to, from);
}
