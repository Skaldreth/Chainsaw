namespace Chainsaw.App.Graph;

/// <summary>What a press on the graph's surface comes to when the pointer is let go or lost.</summary>
public enum GestureEnd
{
    /// <summary>Nothing: no press, or the surface was panned.</summary>
    Nothing,

    /// <summary>A click: choose what is under the pointer.</summary>
    Click,

    /// <summary>A node was dragged: store where it was left.</summary>
    DropNode,
}

/// <summary>
/// The press, drag and click on the graph's surface as a small state machine without WPF, so that it is tested: a
/// press on a node that moves becomes a drag of the node, a press on empty surface that moves pans it, and a press
/// that does not move is a click. Releasing and losing the pointer both end the press exactly once.
/// </summary>
public sealed class GraphGesture
{
    /// <summary>How far the pointer must move before a press becomes a drag, in screen pixels.</summary>
    public const double DragThreshold = 3;

    /// <summary>Where the pointer was pressed on screen, or <see langword="null"/> while it is not pressed.</summary>
    private GraphPoint? _pressedAt;

    /// <summary>Where the pointer was last, on screen.</summary>
    private GraphPoint _last;

    /// <summary>Gets the node the press took hold of, or <see langword="null"/> over empty surface.</summary>
    public string? Node { get; private set; }

    /// <summary>Gets where the node stood when it was taken hold of.</summary>
    public GraphPoint NodePlace { get; private set; }

    /// <summary>Gets where on the node the pointer took hold, on the surface, from its top-left corner.</summary>
    public GraphPoint Grip { get; private set; }

    /// <summary>Gets whether the press has become a drag.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>Gets whether the pointer is pressed.</summary>
    public bool IsPressed => _pressedAt is not null;

    /// <summary>Starts a press.</summary>
    /// <param name="screen">Where on screen.</param>
    /// <param name="node">The node under the pointer, or <see langword="null"/>.</param>
    /// <param name="nodePlace">The node's place, when there is one.</param>
    /// <param name="grip">Where on the node the pointer is, on the surface.</param>
    public void Press(GraphPoint screen, string? node, GraphPoint nodePlace, GraphPoint grip)
    {
        (_pressedAt, _last, Node, NodePlace, Grip, IsDragging) = (screen, screen, node, nodePlace, grip, false);
    }

    /// <summary>Moves the pressed pointer.</summary>
    /// <param name="screen">Where on screen.</param>
    /// <returns>How far to pan the surface, on screen; zero unless the empty surface is dragged.</returns>
    public (double Dx, double Dy) Move(GraphPoint screen)
    {
        if (_pressedAt is not { } pressed)
        {
            return (0, 0);
        }

        IsDragging |= screen.DistanceTo(pressed) >= DragThreshold;
        var pan = IsDragging && Node is null ? (screen.X - _last.X, screen.Y - _last.Y) : (0, 0);
        _last = screen;
        return pan;
    }

    /// <summary>Ends the press, when the pointer is let go or lost; a second call gives nothing.</summary>
    /// <returns>What the press came to, and the node it held.</returns>
    public (GestureEnd End, string? Node) End()
    {
        if (_pressedAt is null)
        {
            return (GestureEnd.Nothing, null);
        }

        var node = Node;
        var end = IsDragging switch
        {
            false => GestureEnd.Click,
            true when node is not null => GestureEnd.DropNode,
            _ => GestureEnd.Nothing,
        };
        (_pressedAt, Node, IsDragging) = (null, null, false);
        return (end, node);
    }
}
