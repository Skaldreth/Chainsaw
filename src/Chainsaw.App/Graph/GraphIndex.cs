namespace Chainsaw.App.Graph;

/// <summary>
/// The graph's nodes and lines in a grid of cells on the surface, as a game engine keeps its world (principle 6): the
/// part on screen, the pointer and a box find what lies there by asking only the cells they touch, instead of going
/// through every node and line. The answers are exactly those of <see cref="GraphHitTest"/>, in the same order; the
/// index only skips what cannot be among them.
/// </summary>
/// <remarks>
/// A node or a line is kept in every cell its frame or its curve's bounds touch; a line so long that it would touch
/// very many cells is kept aside and always looked at, so that a long line never fills the grid. The index is made
/// anew whenever the places or the curves change; it holds no state of its own beyond them.
/// </remarks>
public sealed class GraphIndex
{
    /// <summary>The side of a cell, in the surface's units: a few nodes wide.</summary>
    public const double CellSize = 512;

    /// <summary>A line touching more cells than this is kept aside and always looked at.</summary>
    private const int MostCells = 64;

    /// <summary>The scene the index is made for.</summary>
    private readonly GraphScene _scene;

    /// <summary>The places of the nodes.</summary>
    private readonly IReadOnlyDictionary<string, GraphPoint> _places;

    /// <summary>The curves of the lines, in the order they were worked out.</summary>
    private readonly IReadOnlyDictionary<string, EdgeCurve> _curves;

    /// <summary>Each placed node's frame, by its place in the scene's drawing order.</summary>
    private readonly Dictionary<int, GraphRect> _frames = [];

    /// <summary>The nodes in each cell, by their place in the scene's drawing order.</summary>
    private readonly Dictionary<(int X, int Y), List<int>> _nodeCells = [];

    /// <summary>Each curved line, by its place in the scene's order of lines: its key, curve and bounds.</summary>
    private readonly Dictionary<int, (string Key, EdgeCurve Curve, GraphRect Bounds)> _lines = [];

    /// <summary>The lines in each cell, by their place in the scene's order of lines.</summary>
    private readonly Dictionary<(int X, int Y), List<int>> _lineCells = [];

    /// <summary>The lines kept aside because they touch too many cells, by their place in the scene's order.</summary>
    private readonly List<int> _longLines = [];

    /// <summary>Each curve's place in the order the curves were worked out, for the hit test's ties.</summary>
    private readonly Dictionary<string, int> _curveOrder = new(StringComparer.Ordinal);

    /// <summary>Makes the index of a scene with its places and curves.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="places">Each node's top-left corner.</param>
    /// <param name="curves">Each line's curve.</param>
    public GraphIndex(
        GraphScene scene, IReadOnlyDictionary<string, GraphPoint> places, IReadOnlyDictionary<string, EdgeCurve> curves)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(curves);
        (_scene, _places, _curves) = (scene, places, curves);
        for (var index = 0; index < scene.Nodes.Count; index++)
        {
            var key = scene.Nodes[index].Key;
            if (places.TryGetValue(key, out var place))
            {
                var frame = scene.FrameOf(key, place);
                _frames[index] = frame;
                AddToCells(_nodeCells, frame, index);
            }
        }

        var order = 0;
        foreach (var key in curves.Keys)
        {
            _curveOrder[key] = order++;
        }

        for (var index = 0; index < scene.Edges.Count; index++)
        {
            var key = scene.Edges[index].Key;
            if (curves.TryGetValue(key, out var curve))
            {
                var bounds = curve.Bounds();
                _lines[index] = (key, curve, bounds);
                if (CellCount(bounds) > MostCells)
                {
                    _longLines.Add(index);
                }
                else
                {
                    AddToCells(_lineCells, bounds, index);
                }
            }
        }
    }

    /// <summary>
    /// Gives the nodes whose frame or marks reach into a part of the surface, in drawing order, as
    /// <see cref="GraphHitTest.VisibleNodes"/> does.
    /// </summary>
    /// <param name="visible">The part of the surface.</param>
    /// <returns>The nodes.</returns>
    public IReadOnlyList<NodeView> VisibleNodes(GraphRect visible)
    {
        var shown = new List<NodeView>();
        foreach (var index in InCells(_nodeCells, visible.Inflate(GraphSizes.MarkReach)))
        {
            if (_frames[index].Inflate(GraphSizes.MarkReach).Intersects(visible))
            {
                shown.Add(_scene.Nodes[index]);
            }
        }

        return shown;
    }

    /// <summary>
    /// Gives the lines that may cross a part of the surface, in the scene's order, as
    /// <see cref="GraphHitTest.VisibleEdges"/> does.
    /// </summary>
    /// <param name="visible">The part of the surface.</param>
    /// <returns>The lines.</returns>
    public IReadOnlyList<EdgeView> VisibleEdges(GraphRect visible)
    {
        var shown = new List<EdgeView>();
        foreach (var index in LinesNear(visible.Inflate(GraphSizes.MarkReach)))
        {
            if (_lines[index].Bounds.Inflate(GraphSizes.MarkReach).Intersects(visible))
            {
                shown.Add(_scene.Edges[index]);
            }
        }

        return shown;
    }

    /// <summary>
    /// Gives the node or the line under a point, as <see cref="GraphHitTest.At"/> does: the node drawn last whose frame
    /// holds the point, else the nearest line within the tolerance, the later of two equally near.
    /// </summary>
    /// <param name="point">The point on the surface.</param>
    /// <param name="tolerance">How close to a line counts, in the surface's units.</param>
    /// <returns>The node's or the line's key; both <see langword="null"/> over empty surface.</returns>
    public (string? Node, string? Edge) At(GraphPoint point, double tolerance)
    {
        var here = new GraphRect(point.X, point.Y, 0, 0);
        var nodes = InCells(_nodeCells, here);
        for (var at = nodes.Count - 1; at >= 0; at--)
        {
            if (_frames[nodes[at]].Contains(point))
            {
                return (_scene.Nodes[nodes[at]].Key, null);
            }
        }

        string? nearest = null;
        var best = tolerance;
        var near = LinesNear(here.Inflate(tolerance))
            .Select(index => _lines[index])
            .OrderBy(line => _curveOrder[line.Key]);
        foreach (var (key, curve, bounds) in near)
        {
            if (!bounds.Inflate(tolerance).Contains(point))
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

    /// <summary>Says whether the index was made for this scene, these places and these curves.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="places">The places.</param>
    /// <param name="curves">The curves.</param>
    /// <returns><see langword="true"/> when it was.</returns>
    public bool IsFor(
        GraphScene scene,
        IReadOnlyDictionary<string, GraphPoint> places,
        IReadOnlyDictionary<string, EdgeCurve> curves) =>
        ReferenceEquals(scene, _scene) && ReferenceEquals(places, _places) && ReferenceEquals(curves, _curves);

    /// <summary>Gives the lines in the cells a part of the surface touches and the long lines, in the scene's order.</summary>
    /// <param name="area">The part of the surface.</param>
    /// <returns>Their places in the scene's order of lines.</returns>
    private List<int> LinesNear(GraphRect area)
    {
        var found = InCells(_lineCells, area);
        if (_longLines.Count == 0)
        {
            return found;
        }

        found.AddRange(_longLines);
        found.Sort();
        return found;
    }

    /// <summary>Puts an item in every cell a rectangle touches.</summary>
    /// <param name="cells">The cells.</param>
    /// <param name="area">The rectangle.</param>
    /// <param name="index">The item's place in its order.</param>
    private static void AddToCells(Dictionary<(int X, int Y), List<int>> cells, GraphRect area, int index)
    {
        var (left, top, right, bottom) = CellsOf(area);
        for (var x = left; x <= right; x++)
        {
            for (var y = top; y <= bottom; y++)
            {
                if (!cells.TryGetValue((x, y), out var items))
                {
                    items = [];
                    cells[(x, y)] = items;
                }

                items.Add(index);
            }
        }
    }

    /// <summary>Gives the items in the cells a rectangle touches, each once, in their order.</summary>
    /// <param name="cells">The cells.</param>
    /// <param name="area">The rectangle.</param>
    /// <returns>The items' places in their order, ascending.</returns>
    private static List<int> InCells(Dictionary<(int X, int Y), List<int>> cells, GraphRect area)
    {
        var (left, top, right, bottom) = CellsOf(area);
        var found = new HashSet<int>();
        if ((long)(right - left + 1) * (bottom - top + 1) > cells.Count)
        {
            // A part wider than the grid holds: going through the cells that are there is quicker.
            foreach (var ((x, y), items) in cells)
            {
                if (x >= left && x <= right && y >= top && y <= bottom)
                {
                    found.UnionWith(items);
                }
            }
        }
        else
        {
            for (var x = left; x <= right; x++)
            {
                for (var y = top; y <= bottom; y++)
                {
                    if (cells.TryGetValue((x, y), out var items))
                    {
                        found.UnionWith(items);
                    }
                }
            }
        }

        var ordered = found.ToList();
        ordered.Sort();
        return ordered;
    }

    /// <summary>Gives how many cells a rectangle touches.</summary>
    /// <param name="area">The rectangle.</param>
    /// <returns>The count.</returns>
    private static long CellCount(GraphRect area)
    {
        var (left, top, right, bottom) = CellsOf(area);
        return (long)(right - left + 1) * (bottom - top + 1);
    }

    /// <summary>Gives the cells a rectangle touches, its edges included.</summary>
    /// <param name="area">The rectangle.</param>
    /// <returns>The first and last cell across and down.</returns>
    private static (int Left, int Top, int Right, int Bottom) CellsOf(GraphRect area) => (
        Cell(area.X), Cell(area.Y), Cell(area.Right), Cell(area.Bottom));

    /// <summary>Gives the cell a coordinate lies in.</summary>
    /// <param name="value">The coordinate.</param>
    /// <returns>The cell's number.</returns>
    private static int Cell(double value) => (int)Math.Clamp(Math.Floor(value / CellSize), int.MinValue, int.MaxValue);
}
