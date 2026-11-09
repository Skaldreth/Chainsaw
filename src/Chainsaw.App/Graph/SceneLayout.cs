namespace Chainsaw.App.Graph;

/// <summary>
/// Where everything in the graph stands in the layout shown: every node's place, every line's curve and where every
/// amount is written, with an index of them by where they stand (<see cref="GraphIndex"/>). It is worked out whole when
/// the graph or the layout changes, and only in part while a node is dragged (principle 6, BACKLOG <em>Grafens
/// prestanda vid tusentals noder</em>): the lines of the dragged nodes, in the hop columns the lines that may have to
/// be bent around them, and those lines' amounts.
/// </summary>
/// <remarks>
/// The places, curves and amounts are new dictionaries after every change, never changed in place, so that what drew
/// or cached the old ones can tell that they changed by their reference.
/// </remarks>
public sealed class SceneLayout
{
    /// <summary>The scene laid out.</summary>
    private readonly GraphScene? _scene;

    /// <summary>Whether the graph is sorted in hop columns.</summary>
    private readonly bool _hopColumns;

    /// <summary>The lines between each pair of nodes, in the order of their keys, by the pair.</summary>
    private readonly Dictionary<(string, string), IReadOnlyList<EdgeView>> _pairs = [];

    /// <summary>The pairs each node is one end of.</summary>
    private readonly Dictionary<string, List<(string, string)>> _pairsOf = new(StringComparer.Ordinal);

    /// <summary>Where each node is drawn among the others, which the index keeps.</summary>
    private readonly Dictionary<string, int> _nodeOrder = new(StringComparer.Ordinal);

    /// <summary>Where each line is drawn among the others.</summary>
    private readonly Dictionary<string, int> _edgeOrder = new(StringComparer.Ordinal);

    /// <summary>The room the nodes take in the hop columns, for the lines to be bent around; none on the free surface.</summary>
    private readonly RoomGrid? _room;

    /// <summary>The room taken around the amounts: the nodes, the swaps' pills and the amounts placed.</summary>
    private readonly RoomGrid? _taken;

    /// <summary>Creates a layout with nothing in it.</summary>
    private SceneLayout()
    {
    }

    /// <summary>Creates the layout of a scene at the given places.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="places">Each node's top-left corner.</param>
    /// <param name="hopColumns">Whether the graph is sorted in hop columns.</param>
    private SceneLayout(GraphScene scene, IReadOnlyDictionary<string, GraphPoint> places, bool hopColumns)
    {
        (_scene, _hopColumns, Places) = (scene, hopColumns, places);
        for (var order = 0; order < scene.Nodes.Count; order++)
        {
            _nodeOrder[scene.Nodes[order].Key] = order;
        }

        for (var order = 0; order < scene.Edges.Count; order++)
        {
            _edgeOrder[scene.Edges[order].Key] = order;
        }

        var lines = scene.Edges.Where(edge => edge.Edge.From != edge.Edge.To
            && places.ContainsKey(edge.Edge.From)
            && places.ContainsKey(edge.Edge.To));
        foreach (var pair in lines.GroupBy(edge => GraphHitTest.PairOf(edge.Edge.From, edge.Edge.To)))
        {
            _pairs[pair.Key] = [.. pair.OrderBy(edge => edge.Key, StringComparer.Ordinal)];
            PairOf(pair.Key.Item1).Add(pair.Key);
            PairOf(pair.Key.Item2).Add(pair.Key);
        }

        var curves = new Dictionary<string, EdgeCurve>(StringComparer.Ordinal);
        _room = hopColumns ? LineRouting.Room(places.Select(pair => scene.FrameOf(pair.Key, pair.Value))) : null;
        var lookedIn = new Dictionary<string, IReadOnlySet<(int Column, int Row)>>(StringComparer.Ordinal);
        foreach (var pair in _pairs.Values)
        {
            GraphHitTest.AddPair(pair, scene, places, _room, curves, lookedIn);
        }

        foreach (var (key, cells) in lookedIn)
        {
            Index.SetRoute(key, cells);
        }

        Curves = curves;
        var amounts = new Dictionary<string, AmountSpot>(StringComparer.Ordinal);
        _taken = AmountLayout.Taken(scene, places, curves, amounts);
        AmountLayout.PlaceInto(scene.Edges, curves, _taken, amounts);
        Amounts = amounts;
        foreach (var (key, place) in places)
        {
            IndexNode(key, place);
        }

        foreach (var (key, curve) in curves)
        {
            Index.SetEdge(key, _edgeOrder[key], curve.Bounds());
        }
    }

    /// <summary>Gets a layout with nothing in it, for when no graph is shown.</summary>
    public static SceneLayout Empty { get; } = new();

    /// <summary>Gets each node's top-left corner.</summary>
    public IReadOnlyDictionary<string, GraphPoint> Places { get; private set; } = new Dictionary<string, GraphPoint>();

    /// <summary>Gets each line's curve; a line whose ends are not placed, or that loops, has none.</summary>
    public IReadOnlyDictionary<string, EdgeCurve> Curves { get; private set; } = new Dictionary<string, EdgeCurve>();

    /// <summary>Gets where each line's amount is written; a line without room for it has none.</summary>
    public IReadOnlyDictionary<string, AmountSpot> Amounts { get; private set; } =
        new Dictionary<string, AmountSpot>();

    /// <summary>Gets the nodes and lines by where they stand.</summary>
    public GraphIndex Index { get; } = new();

    /// <summary>Works out the whole layout of a scene at the given places.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="places">Each node's top-left corner.</param>
    /// <param name="hopColumns">Whether the graph is sorted in hop columns.</param>
    /// <returns>The layout.</returns>
    public static SceneLayout Build(GraphScene scene, IReadOnlyDictionary<string, GraphPoint> places, bool hopColumns)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(places);
        return new SceneLayout(scene, places, hopColumns);
    }

    /// <summary>
    /// Moves some nodes and works out again only what depends on them: their lines, in the hop columns the lines whose
    /// routing looked where the nodes stood or now stand, and those lines' amounts. The other amounts stay where they
    /// were until the next <see cref="Build"/>, which the view model does when the drag ends.
    /// </summary>
    /// <param name="moved">The nodes' new top-left corners; a node not in the layout is left out.</param>
    public void Move(IReadOnlyDictionary<string, GraphPoint> moved)
    {
        ArgumentNullException.ThrowIfNull(moved);
        if (_scene is not { } scene || _taken is not { } taken)
        {
            return;
        }

        var places = new Dictionary<string, GraphPoint>(Places, StringComparer.Ordinal);
        var changed = moved.Where(pair => places.ContainsKey(pair.Key) && places[pair.Key] != pair.Value).ToList();
        if (changed.Count == 0)
        {
            return;
        }

        var near = new HashSet<(string, string)>();
        foreach (var (key, place) in changed)
        {
            var (from, to) = (scene.FrameOf(key, places[key]), scene.FrameOf(key, place));
            near.UnionWith(_pairsOf.GetValueOrDefault(key) ?? []);
            if (_room is not null)
            {
                near.UnionWith(PairsNear(from));
                near.UnionWith(PairsNear(to));
                _room.Remove(from.Inflate(LineRouting.Clearance));
                _room.Add(to.Inflate(LineRouting.Clearance));
            }

            taken.Remove(AmountLayout.NodeRoom(scene, key, places[key]));
            taken.Add(AmountLayout.NodeRoom(scene, key, place));
            places[key] = place;
            IndexNode(key, place);
        }

        var lines = near.SelectMany(pair => _pairs[pair]).ToList();
        var amounts = new Dictionary<string, AmountSpot>(Amounts, StringComparer.Ordinal);
        foreach (var edge in lines)
        {
            Untake(edge, amounts);
        }

        var curves = new Dictionary<string, EdgeCurve>(Curves, StringComparer.Ordinal);
        var lookedIn = new Dictionary<string, IReadOnlySet<(int Column, int Row)>>(StringComparer.Ordinal);
        foreach (var pair in near)
        {
            GraphHitTest.AddPair(_pairs[pair], scene, places, _room, curves, lookedIn);
        }

        foreach (var edge in lines)
        {
            Index.SetEdge(edge.Key, _edgeOrder[edge.Key], curves[edge.Key].Bounds());
            if (lookedIn.TryGetValue(edge.Key, out var cells))
            {
                Index.SetRoute(edge.Key, cells);
            }
            if (AmountLayout.SwapBox(edge, curves[edge.Key]) is { } pill)
            {
                taken.Add(pill);
            }
        }

        AmountLayout.PlaceInto(lines.OrderBy(edge => _edgeOrder[edge.Key]), curves, taken, amounts);
        (Places, Curves, Amounts) = (places, curves, amounts);
    }

    /// <summary>Gives the nodes on screen, in drawing order.</summary>
    /// <param name="visible">The visible part of the surface.</param>
    /// <returns>The nodes whose frame or marks reach into it.</returns>
    public IEnumerable<NodeView> VisibleNodes(GraphRect visible) => _scene is { } scene
        ? Index.NodesIn(visible.Inflate(GraphSizes.MarkReach)).Select(key => scene.Node(key)!)
        : [];

    /// <summary>Gives the lines on screen, in drawing order.</summary>
    /// <param name="visible">The visible part of the surface.</param>
    /// <returns>The lines that may cross it.</returns>
    public IEnumerable<EdgeView> VisibleEdges(GraphRect visible) => _scene is { } scene
        ? Index.EdgesIn(visible.Inflate(GraphSizes.MarkReach)).Select(key => scene.Edge(key)!)
        : [];

    /// <summary>
    /// Gives the node or else the line under a point; a node drawn later lies on top, and a node wins over a line, as
    /// <see cref="GraphHitTest.At"/> but among what is near the point only.
    /// </summary>
    /// <param name="point">The point on the surface.</param>
    /// <param name="tolerance">How close to a line counts, in the surface's units.</param>
    /// <returns>The node's key or the line's key; both <see langword="null"/> over empty surface.</returns>
    public (string? Node, string? Edge) HitTest(GraphPoint point, double tolerance) =>
        Index.NodeAt(point) is { } node ? (node, null) : (null, Index.EdgeAt(point, tolerance, Curves));

    /// <summary>Gives back the room a line's swap pill and amount took, before the line is worked out again.</summary>
    /// <param name="edge">The line.</param>
    /// <param name="amounts">The amounts, which the line's is taken out of.</param>
    private void Untake(EdgeView edge, Dictionary<string, AmountSpot> amounts)
    {
        if (Curves.TryGetValue(edge.Key, out var curve) && AmountLayout.SwapBox(edge, curve) is { } pill)
        {
            _taken!.Remove(pill);
        }

        if (amounts.Remove(edge.Key, out var spot))
        {
            _taken!.Remove(spot.Box);
        }
    }

    /// <summary>Gives the list of pairs a node is one end of, made when first asked for.</summary>
    /// <param name="key">The node's key.</param>
    /// <returns>The list.</returns>
    private List<(string, string)> PairOf(string key)
    {
        if (!_pairsOf.TryGetValue(key, out var pairs))
        {
            pairs = [];
            _pairsOf[key] = pairs;
        }

        return pairs;
    }

    /// <summary>
    /// Gives the pairs whose lines may be routed differently when a node stands in or leaves a frame: those whose
    /// routing looked in a cell of the nodes' room that the frame reaches into (<see cref="LineRouting.Route"/>),
    /// since no other line can meet it.
    /// </summary>
    /// <param name="frame">The node's frame.</param>
    /// <returns>The pairs.</returns>
    private IEnumerable<(string, string)> PairsNear(GraphRect frame) =>
        Index.RoutesIn(_room!.CellsOf(frame.Inflate(LineRouting.Clearance)))
            .Select(key => _scene!.Edge(key)!.Edge)
            .Select(edge => GraphHitTest.PairOf(edge.From, edge.To));

    /// <summary>Puts a drawn node in the index at a place; a node that is only laid out, as a pile's, is not drawn.</summary>
    /// <param name="key">The node's key.</param>
    /// <param name="place">Its top-left corner.</param>
    private void IndexNode(string key, GraphPoint place)
    {
        if (_nodeOrder.TryGetValue(key, out var order))
        {
            Index.SetNode(key, order, _scene!.FrameOf(key, place));
        }
    }
}
