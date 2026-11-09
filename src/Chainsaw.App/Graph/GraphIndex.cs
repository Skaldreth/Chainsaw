namespace Chainsaw.App.Graph;

/// <summary>
/// The graph's nodes and lines in a grid of cells on the surface, as a game engine keeps its world (principle 6): the
/// part on screen, the pointer and a box find what lies there by asking only the cells they touch, instead of going
/// through every node and line. The answers are exactly those of <see cref="GraphHitTest"/>, in the same order; the
/// index only skips what cannot be among them.
/// </summary>
/// <remarks>
/// A node or a line is kept in every cell its frame or its curve's bounds touch; a line so long that it would touch
/// very many cells is kept aside and always looked at, so that a long line never fills the grid. A drag moves only
/// what moved (<see cref="Follow"/>); another scene makes a new index.
/// </remarks>
public sealed class GraphIndex
{
    /// <summary>The side of a cell, in the surface's units: a few nodes wide.</summary>
    public const double CellSize = 512;

    /// <summary>A line touching more cells than this is kept aside and always looked at.</summary>
    private const int MostCells = 64;

    /// <summary>The farthest cell from the origin, so that no coordinate, however large, overflows a cell's number.</summary>
    private const long FarthestCell = 1L << 40;

    /// <summary>
    /// How much a lookup reaches past the part asked for, in the surface's units: the part and a frame grown by the
    /// same margin may round apart by a hair at a cell's border, and the lookup must never miss what lies on it.
    /// </summary>
    private const double LookupPad = 1;

    /// <summary>The scene the index is made for.</summary>
    private readonly GraphScene _scene;

    /// <summary>Each placed node's frame, by its place in the scene's drawing order.</summary>
    private readonly Dictionary<int, GraphRect> _frames = [];

    /// <summary>The nodes in each cell, by their place in the scene's drawing order.</summary>
    private readonly Dictionary<(long X, long Y), List<int>> _nodeCells = [];

    /// <summary>Each curved line, by its place in the scene's order of lines: its key, curve and bounds.</summary>
    private readonly Dictionary<int, (string Key, EdgeCurve Curve, GraphRect Bounds)> _lines = [];

    /// <summary>The lines in each cell, by their place in the scene's order of lines.</summary>
    private readonly Dictionary<(long X, long Y), List<int>> _lineCells = [];

    /// <summary>The lines kept aside because they touch too many cells, by their place in the scene's order.</summary>
    private readonly SortedSet<int> _longLines = [];

    /// <summary>Each curve's place in the order the curves were worked out, for the hit test's ties.</summary>
    private readonly Dictionary<string, int> _curveOrder = new(StringComparer.Ordinal);

    /// <summary>The places of the nodes the index answers for.</summary>
    private IReadOnlyDictionary<string, GraphPoint> _places;

    /// <summary>The curves of the lines the index answers for, in the order they were worked out.</summary>
    private IReadOnlyDictionary<string, EdgeCurve> _curves;

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
            if (places.TryGetValue(scene.Nodes[index].Key, out var place))
            {
                AddNode(index, place);
            }
        }

        OrderCurves();
        for (var index = 0; index < scene.Edges.Count; index++)
        {
            if (curves.TryGetValue(scene.Edges[index].Key, out var curve))
            {
                AddLine(index, curve);
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

    /// <summary>
    /// Follows new places and curves of the same scene, as a drag gives them, by moving only the nodes whose place
    /// changed and the lines whose curve changed (a curve that did not change is the same instance); it answers as an
    /// index made anew would.
    /// </summary>
    /// <param name="scene">The scene.</param>
    /// <param name="places">The new places.</param>
    /// <param name="curves">The new curves.</param>
    /// <returns><see langword="true"/> when it followed; <see langword="false"/> for another scene.</returns>
    public bool Follow(
        GraphScene scene, IReadOnlyDictionary<string, GraphPoint> places, IReadOnlyDictionary<string, EdgeCurve> curves)
    {
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(curves);
        if (!ReferenceEquals(scene, _scene))
        {
            return false;
        }

        for (var index = 0; index < scene.Nodes.Count; index++)
        {
            var key = scene.Nodes[index].Key;
            var had = _places.TryGetValue(key, out var before);
            var has = places.TryGetValue(key, out var after);
            if (had == has && (!has || before == after))
            {
                continue;
            }

            RemoveNode(index);
            if (has)
            {
                AddNode(index, after);
            }
        }

        var reordered = !curves.Keys.SequenceEqual(_curves.Keys);
        for (var index = 0; index < scene.Edges.Count; index++)
        {
            var has = curves.TryGetValue(scene.Edges[index].Key, out var curve);
            var had = _lines.TryGetValue(index, out var line);
            if (had == has && (!has || ReferenceEquals(line.Curve, curve)))
            {
                continue;
            }

            RemoveLine(index);
            if (has)
            {
                AddLine(index, curve!);
            }
        }

        (_places, _curves) = (places, curves);
        if (reordered)
        {
            OrderCurves();
        }

        return true;
    }

    /// <summary>Notes each curve's place in the order the curves were worked out.</summary>
    private void OrderCurves()
    {
        _curveOrder.Clear();
        var order = 0;
        foreach (var key in _curves.Keys)
        {
            _curveOrder[key] = order++;
        }
    }

    /// <summary>Puts a node in the cells its frame touches.</summary>
    /// <param name="index">Its place in the scene's drawing order.</param>
    /// <param name="place">Its top-left corner.</param>
    private void AddNode(int index, GraphPoint place)
    {
        var frame = _scene.FrameOf(_scene.Nodes[index].Key, place);
        _frames[index] = frame;
        AddToCells(_nodeCells, frame, index);
    }

    /// <summary>Takes a node out of the cells it was in.</summary>
    /// <param name="index">Its place in the scene's drawing order.</param>
    private void RemoveNode(int index)
    {
        if (_frames.Remove(index, out var frame))
        {
            RemoveFromCells(_nodeCells, frame, index);
        }
    }

    /// <summary>Puts a line in the cells its bounds touch, or aside when it would touch too many.</summary>
    /// <param name="index">Its place in the scene's order of lines.</param>
    /// <param name="curve">Its curve.</param>
    private void AddLine(int index, EdgeCurve curve)
    {
        var bounds = curve.Bounds();
        _lines[index] = (_scene.Edges[index].Key, curve, bounds);
        if (CellCount(bounds) > MostCells)
        {
            _longLines.Add(index);
        }
        else
        {
            AddToCells(_lineCells, bounds, index);
        }
    }

    /// <summary>Takes a line out of the cells it was in, or from aside.</summary>
    /// <param name="index">Its place in the scene's order of lines.</param>
    private void RemoveLine(int index)
    {
        if (!_lines.Remove(index, out var line))
        {
            return;
        }

        if (!_longLines.Remove(index))
        {
            RemoveFromCells(_lineCells, line.Bounds, index);
        }
    }

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
    private static void AddToCells(Dictionary<(long X, long Y), List<int>> cells, GraphRect area, int index)
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

    /// <summary>Takes an item out of every cell a rectangle touches.</summary>
    /// <param name="cells">The cells.</param>
    /// <param name="area">The rectangle it was put in by.</param>
    /// <param name="index">The item's place in its order.</param>
    private static void RemoveFromCells(Dictionary<(long X, long Y), List<int>> cells, GraphRect area, int index)
    {
        var (left, top, right, bottom) = CellsOf(area);
        for (var x = left; x <= right; x++)
        {
            for (var y = top; y <= bottom; y++)
            {
                if (cells.TryGetValue((x, y), out var items) && items.Remove(index) && items.Count == 0)
                {
                    cells.Remove((x, y));
                }
            }
        }
    }

    /// <summary>
    /// Gives the items in the cells a rectangle touches (reaching a hair past it), each once, in their order.
    /// </summary>
    /// <param name="cells">The cells.</param>
    /// <param name="area">The rectangle.</param>
    /// <returns>The items' places in their order, ascending.</returns>
    private static List<int> InCells(Dictionary<(long X, long Y), List<int>> cells, GraphRect area)
    {
        var (left, top, right, bottom) = CellsOf(area.Inflate(LookupPad));
        var found = new HashSet<int>();
        if (Span(left, right) * Span(top, bottom) > cells.Count)
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
    private static double CellCount(GraphRect area)
    {
        var (left, top, right, bottom) = CellsOf(area);
        return Span(left, right) * Span(top, bottom);
    }

    /// <summary>Gives how many cells lie from one to another, both counted, without overflowing.</summary>
    /// <param name="first">The first cell.</param>
    /// <param name="last">The last cell.</param>
    /// <returns>The count, as a double so that the product of two never overflows.</returns>
    private static double Span(long first, long last) => (double)last - first + 1;

    /// <summary>Gives the cells a rectangle touches, its edges included.</summary>
    /// <param name="area">The rectangle.</param>
    /// <returns>The first and last cell across and down.</returns>
    private static (long Left, long Top, long Right, long Bottom) CellsOf(GraphRect area) => (
        Cell(area.X), Cell(area.Y), Cell(area.Right), Cell(area.Bottom));

    /// <summary>Gives the cell a coordinate lies in, kept within the farthest cell.</summary>
    /// <param name="value">The coordinate.</param>
    /// <returns>The cell's number.</returns>
    private static long Cell(double value) => double.IsNaN(value)
        ? 0
        : (long)Math.Clamp(Math.Floor(value / CellSize), -FarthestCell, FarthestCell);
}
