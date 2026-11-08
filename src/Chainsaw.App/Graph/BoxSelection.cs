namespace Chainsaw.App.Graph;

/// <summary>How a box drawn on the empty surface changes the pick, chosen by the keys held when it starts.</summary>
public enum BoxMode
{
    /// <summary>No key: the box's actors become the pick.</summary>
    Replace,

    /// <summary>Shift: the box's actors are added to the pick.</summary>
    Add,

    /// <summary>Ctrl: each of the box's actors is turned, picked if it was not and taken back if it was.</summary>
    Toggle,
}

/// <summary>
/// The box drawn with the left button on the graph's empty surface to pick several actors, as on the Windows desktop
/// (the user's wish 2026-11-08, built on the user's description of the standard pattern, without a sketch): every
/// actor whose frame the box touches is picked while the box is drawn, and Esc gives back the pick it began with. It
/// is a small state without WPF, so that it is tested; a move that picks the same actors as the one before changes
/// nothing and allocates nothing (principle 6).
/// </summary>
public sealed class BoxSelection
{
    /// <summary>How near the edge the pointer makes the surface scroll while a box is drawn, in pixels.</summary>
    public const double EdgeBand = 28;

    /// <summary>How fast the surface scrolls with the pointer at the edge or past it, in pixels per second.</summary>
    public const double EdgeSpeed = 900;

    /// <summary>The pick when the box began, in its order.</summary>
    private readonly List<string> _before;

    /// <summary>The pick when the box began, for asking.</summary>
    private readonly HashSet<string> _beforeSet;

    /// <summary>The actors the box touches now.</summary>
    private HashSet<string> _hits = new(StringComparer.Ordinal);

    /// <summary>The actors touched at the move before, swapped with <see cref="_hits"/> to allocate nothing.</summary>
    private HashSet<string> _lastHits = new(StringComparer.Ordinal);

    /// <summary>The pick the box gives now.</summary>
    private readonly List<string> _picked = [];

    /// <summary>Starts a box.</summary>
    /// <param name="mode">How it changes the pick.</param>
    /// <param name="before">The pick when it begins.</param>
    public BoxSelection(BoxMode mode, IReadOnlyList<string> before)
    {
        ArgumentNullException.ThrowIfNull(before);
        Mode = mode;
        _before = [.. before];
        _beforeSet = new HashSet<string>(before, StringComparer.Ordinal);
        if (mode != BoxMode.Replace)
        {
            _picked.AddRange(_before);
        }
    }

    /// <summary>Gets how the box changes the pick.</summary>
    public BoxMode Mode { get; }

    /// <summary>Gets the pick when the box began, which Esc gives back.</summary>
    public IReadOnlyList<string> Before => _before;

    /// <summary>Gets the pick the box gives now.</summary>
    public IReadOnlyList<string> Picked => _picked;

    /// <summary>Gives the mode for the keys held: Ctrl turns, Shift adds, none replaces.</summary>
    /// <param name="control">Whether Ctrl is held.</param>
    /// <param name="shift">Whether Shift is held.</param>
    /// <returns>The mode.</returns>
    public static BoxMode ModeOf(bool control, bool shift)
    {
        if (control)
        {
            return BoxMode.Toggle;
        }

        return shift ? BoxMode.Add : BoxMode.Replace;
    }

    /// <summary>Gives the box between two corners, whichever way it was drawn.</summary>
    /// <param name="from">Where it began.</param>
    /// <param name="to">Where the pointer is.</param>
    /// <returns>The box, with no negative side.</returns>
    public static GraphRect Between(GraphPoint from, GraphPoint to) =>
        new(Math.Min(from.X, to.X), Math.Min(from.Y, to.Y), Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));

    /// <summary>Says whether the box picks a node: it does when it touches the node's frame anywhere.</summary>
    /// <param name="box">The box on the surface.</param>
    /// <param name="frame">The node's frame on the surface.</param>
    /// <returns><see langword="true"/> when it touches.</returns>
    public static bool Touches(GraphRect box, GraphRect frame) => box.Intersects(frame);

    /// <summary>
    /// Gives how fast the surface should scroll for the pointer near or past its edge while a box is drawn: zero
    /// inside, then faster towards the edge, at full speed on it and past it.
    /// </summary>
    /// <param name="pointer">The pointer, on screen.</param>
    /// <param name="width">The surface's width on screen.</param>
    /// <param name="height">The surface's height on screen.</param>
    /// <returns>Which way and how fast the view moves, in pixels per second; positive is right and down.</returns>
    public static GraphPoint ScrollSpeed(GraphPoint pointer, double width, double height) =>
        new(AxisSpeed(pointer.X, width), AxisSpeed(pointer.Y, height));

    /// <summary>
    /// Picks with the box as it stands now: the nodes whose frames it touches, combined with the pick it began with
    /// by the mode.
    /// </summary>
    /// <param name="box">The box on the surface.</param>
    /// <param name="nodes">The nodes drawn, in drawing order.</param>
    /// <param name="places">Each node's top-left corner.</param>
    /// <returns><see langword="true"/> when the pick changed since the move before.</returns>
    public bool Update(GraphRect box, IReadOnlyList<NodeView> nodes, IReadOnlyDictionary<string, GraphPoint> places)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(places);
        (_hits, _lastHits) = (_lastHits, _hits);
        _hits.Clear();
        for (var index = 0; index < nodes.Count; index++)
        {
            var key = nodes[index].Key;
            if (places.TryGetValue(key, out var place) && Touches(box, GraphSizes.NodeFrame(place)))
            {
                _hits.Add(key);
            }
        }

        if (_hits.SetEquals(_lastHits))
        {
            return false;
        }

        Combine(nodes);
        return true;
    }

    /// <summary>Works out the pick from the pick it began with and the actors the box touches.</summary>
    /// <param name="nodes">The nodes drawn, in drawing order, which is the order the touched are added in.</param>
    private void Combine(IReadOnlyList<NodeView> nodes)
    {
        _picked.Clear();
        if (Mode != BoxMode.Replace)
        {
            foreach (var key in _before)
            {
                if (Mode == BoxMode.Add || !_hits.Contains(key))
                {
                    _picked.Add(key);
                }
            }
        }

        for (var index = 0; index < nodes.Count; index++)
        {
            var key = nodes[index].Key;
            if (_hits.Contains(key) && (Mode == BoxMode.Replace || !_beforeSet.Contains(key)))
            {
                _picked.Add(key);
            }
        }
    }

    /// <summary>Gives the scrolling speed along one axis.</summary>
    /// <param name="at">The pointer along the axis.</param>
    /// <param name="length">The surface's length along it.</param>
    /// <returns>The speed, negative towards the start.</returns>
    private static double AxisSpeed(double at, double length)
    {
        if (length <= 2 * EdgeBand)
        {
            return 0;
        }

        if (at < EdgeBand)
        {
            return -EdgeSpeed * Math.Min(1, (EdgeBand - at) / EdgeBand);
        }

        if (at > length - EdgeBand)
        {
            return EdgeSpeed * Math.Min(1, (at - (length - EdgeBand)) / EdgeBand);
        }

        return 0;
    }
}
