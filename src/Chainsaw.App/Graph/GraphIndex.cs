namespace Chainsaw.App.Graph;

/// <summary>
/// The graph's nodes and lines in a grid of cells by where they stand, so that what is under the pointer and what is on
/// screen is found among what is near instead of among every node and line (principle 6, BACKLOG <em>Grafens
/// prestanda vid tusentals noder</em>). A dragged node and its lines are moved in the grid one by one.
/// </summary>
/// <remarks>
/// Each item keeps the order it is drawn in, so that the topmost node wins a hit and what is on screen is given in the
/// drawing order, as when every node and line was looked through.
/// </remarks>
public sealed class GraphIndex
{
    /// <summary>The side of a cell in the surface's units: about two nodes wide.</summary>
    public const double Cell = 256;

    /// <summary>The nodes.</summary>
    private readonly Grid _nodes = new();

    /// <summary>The lines, by the rectangle that holds each curve.</summary>
    private readonly Grid _edges = new();

    /// <summary>
    /// The lines in the hop columns by the cells of the nodes' room each looked in to be bent around the nodes
    /// (<see cref="LineRouting.Route"/>).
    /// </summary>
    private readonly Dictionary<(int Column, int Row), HashSet<string>> _routes = [];

    /// <summary>The cells each line looked in, so that it can be taken out of them.</summary>
    private readonly Dictionary<string, IReadOnlySet<(int Column, int Row)>> _routeCells =
        new(StringComparer.Ordinal);

    /// <summary>Gets how many nodes are in the index.</summary>
    public int NodeCount => _nodes.Count;

    /// <summary>Gets how many lines are in the index.</summary>
    public int EdgeCount => _edges.Count;

    /// <summary>Puts a node in the index, or moves it there.</summary>
    /// <param name="key">The node's key.</param>
    /// <param name="order">Where it is drawn among the nodes; later lies on top.</param>
    /// <param name="frame">Its frame.</param>
    public void SetNode(string key, int order, GraphRect frame) => _nodes.Set(key, order, frame);

    /// <summary>Puts a line in the index, or moves it there.</summary>
    /// <param name="key">The line's key.</param>
    /// <param name="order">Where it is drawn among the lines.</param>
    /// <param name="bounds">The rectangle that holds its curve.</param>
    public void SetEdge(string key, int order, GraphRect bounds) => _edges.Set(key, order, bounds);

    /// <summary>Keeps the cells of the nodes' room a line looked in to be routed around the nodes.</summary>
    /// <param name="key">The line's key.</param>
    /// <param name="cells">The cells; none for a line that looked in none.</param>
    public void SetRoute(string key, IReadOnlySet<(int Column, int Row)> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (_routeCells.Remove(key, out var before))
        {
            foreach (var cell in before)
            {
                if (_routes.TryGetValue(cell, out var keys) && keys.Remove(key) && keys.Count == 0)
                {
                    _routes.Remove(cell);
                }
            }
        }

        _routeCells[key] = cells;
        foreach (var cell in cells)
        {
            if (!_routes.TryGetValue(cell, out var keys))
            {
                keys = new HashSet<string>(StringComparer.Ordinal);
                _routes[cell] = keys;
            }

            keys.Add(key);
        }
    }

    /// <summary>Gives the lines whose routing looked in some cells, so that a node there may bend them.</summary>
    /// <param name="cells">The cells.</param>
    /// <returns>The lines' keys, each once.</returns>
    public IReadOnlySet<string> RoutesIn(IEnumerable<(int Column, int Row)> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cell in cells)
        {
            if (_routes.TryGetValue(cell, out var keys))
            {
                found.UnionWith(keys);
            }
        }

        return found;
    }

    /// <summary>Gives the frame a node has in the index.</summary>
    /// <param name="key">The node's key.</param>
    /// <returns>The frame, or <see langword="null"/> when the node is not in it.</returns>
    public GraphRect? FrameOf(string key) => _nodes.RectOf(key);

    /// <summary>Gives the nodes whose frame reaches into a rectangle, in drawing order.</summary>
    /// <param name="area">The rectangle.</param>
    /// <returns>The nodes' keys.</returns>
    public IReadOnlyList<string> NodesIn(GraphRect area) => _nodes.In(area);

    /// <summary>Gives the lines whose curve's rectangle reaches into a rectangle, in drawing order.</summary>
    /// <param name="area">The rectangle.</param>
    /// <returns>The lines' keys.</returns>
    public IReadOnlyList<string> EdgesIn(GraphRect area) => _edges.In(area);

    /// <summary>Gives the topmost node whose frame holds a point.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The node's key, or <see langword="null"/>.</returns>
    public string? NodeAt(GraphPoint point) => _nodes.TopmostAt(point);

    /// <summary>Gives the line nearest a point within a distance.</summary>
    /// <param name="point">The point.</param>
    /// <param name="tolerance">How close counts.</param>
    /// <param name="curves">Each line's curve.</param>
    /// <returns>The line's key, or <see langword="null"/> when none is that close.</returns>
    public string? EdgeAt(GraphPoint point, double tolerance, IReadOnlyDictionary<string, EdgeCurve> curves)
    {
        ArgumentNullException.ThrowIfNull(curves);
        string? nearest = null;
        var best = tolerance;
        foreach (var key in _edges.In(new GraphRect(point.X, point.Y, 0, 0).Inflate(tolerance)))
        {
            if (curves.TryGetValue(key, out var curve) && curve.DistanceTo(point) is var distance && distance <= best)
            {
                (nearest, best) = (key, distance);
            }
        }

        return nearest;
    }

    /// <summary>A grid of keyed rectangles, each with its drawing order.</summary>
    private sealed class Grid
    {
        /// <summary>The keys in each cell their rectangle reaches into.</summary>
        private readonly Dictionary<(int Column, int Row), HashSet<string>> _cells = [];

        /// <summary>Each key's rectangle and drawing order.</summary>
        private readonly Dictionary<string, (GraphRect Rect, int Order)> _items = new(StringComparer.Ordinal);

        /// <summary>
        /// The most cells a rectangle is put in; a larger one, as a line across the whole graph, is kept aside and
        /// looked at by every question instead, so that putting it in and taking it out stay quick.
        /// </summary>
        private const int MostCells = 32;

        /// <summary>The keys whose rectangle is too large for the cells (<see cref="MostCells"/>).</summary>
        private readonly HashSet<string> _wide = new(StringComparer.Ordinal);

        /// <summary>Gets how many keys are in the grid.</summary>
        public int Count => _items.Count;

        /// <summary>Puts a key's rectangle in the grid, taking out where it stood before.</summary>
        /// <param name="key">The key.</param>
        /// <param name="order">Its drawing order.</param>
        /// <param name="rect">Its rectangle.</param>
        public void Set(string key, int order, GraphRect rect)
        {
            Remove(key);
            _items[key] = (rect, order);
            if (CellCount(rect) > MostCells)
            {
                _wide.Add(key);
                return;
            }

            foreach (var cell in CellsOf(rect))
            {
                if (!_cells.TryGetValue(cell, out var keys))
                {
                    keys = new HashSet<string>(StringComparer.Ordinal);
                    _cells[cell] = keys;
                }

                keys.Add(key);
            }
        }

        /// <summary>Takes a key out of the grid.</summary>
        /// <param name="key">The key.</param>
        public void Remove(string key)
        {
            if (!_items.Remove(key, out var item) || _wide.Remove(key))
            {
                return;
            }

            foreach (var cell in CellsOf(item.Rect))
            {
                if (_cells.TryGetValue(cell, out var keys) && keys.Remove(key) && keys.Count == 0)
                {
                    _cells.Remove(cell);
                }
            }
        }

        /// <summary>Gives a key's rectangle.</summary>
        /// <param name="key">The key.</param>
        /// <returns>The rectangle, or <see langword="null"/>.</returns>
        public GraphRect? RectOf(string key) => _items.TryGetValue(key, out var item) ? item.Rect : null;

        /// <summary>Gives the keys whose rectangle reaches into an area, in drawing order.</summary>
        /// <param name="area">The area.</param>
        /// <returns>The keys.</returns>
        public IReadOnlyList<string> In(GraphRect area)
        {
            // An area wider than the grid holds, as the whole graph zoomed out, is looked through key by key rather
            // than cell by cell, so that its empty cells cost nothing.
            IEnumerable<string> near = CellCount(area) > _cells.Count ? _items.Keys : Near(area);
            return [.. near
                .Select(key => (Key: key, Item: _items[key]))
                .Where(pair => pair.Item.Rect.Intersects(area))
                .OrderBy(pair => pair.Item.Order)
                .Select(pair => pair.Key)];
        }

        /// <summary>Gives the key with the highest drawing order whose rectangle holds a point.</summary>
        /// <param name="point">The point.</param>
        /// <returns>The key, or <see langword="null"/>.</returns>
        public string? TopmostAt(GraphPoint point)
        {
            var keys = _cells.TryGetValue((Index(point.X), Index(point.Y)), out var found) ? found : [];
            return keys.Concat(_wide)
                .Select(key => (Key: key, Item: _items[key]))
                .Where(pair => pair.Item.Rect.Contains(point))
                .OrderByDescending(pair => pair.Item.Order)
                .Select(pair => pair.Key)
                .FirstOrDefault();
        }

        /// <summary>Gives the keys in the cells an area reaches into, each once.</summary>
        /// <param name="area">The area.</param>
        /// <returns>The keys, which may lie outside the area within their cells.</returns>
        private HashSet<string> Near(GraphRect area)
        {
            var found = new HashSet<string>(_wide, StringComparer.Ordinal);
            foreach (var cell in CellsOf(area))
            {
                if (_cells.TryGetValue(cell, out var keys))
                {
                    found.UnionWith(keys);
                }
            }

            return found;
        }

        /// <summary>Gives how many cells a rectangle reaches into, without walking them.</summary>
        /// <param name="rect">The rectangle.</param>
        /// <returns>The number of cells, as a double so that a huge rectangle cannot overflow it.</returns>
        private static double CellCount(GraphRect rect) =>
            (Math.Floor(rect.Right / Cell) - Math.Floor(rect.X / Cell) + 1)
            * (Math.Floor(rect.Bottom / Cell) - Math.Floor(rect.Y / Cell) + 1);

        /// <summary>Gives the cells a rectangle reaches into.</summary>
        /// <param name="rect">The rectangle.</param>
        /// <returns>The cells.</returns>
        private static IEnumerable<(int Column, int Row)> CellsOf(GraphRect rect)
        {
            var (left, right) = (Index(rect.X), Index(rect.Right));
            var (top, bottom) = (Index(rect.Y), Index(rect.Bottom));
            for (var column = left; column <= right; column++)
            {
                for (var row = top; row <= bottom; row++)
                {
                    yield return (column, row);
                }
            }
        }

        /// <summary>Gives the cell a coordinate falls in.</summary>
        /// <param name="value">The coordinate.</param>
        /// <returns>The cell's index.</returns>
        private static int Index(double value) => (int)Math.Floor(value / Cell);
    }
}
