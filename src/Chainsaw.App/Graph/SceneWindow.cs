namespace Chainsaw.App.Graph;

/// <summary>
/// Decides when the graph's surface must draw its scene again and when the drawing it made last can be moved instead
/// (principle 6). The scene is drawn on the surface's own units, over the part on screen and a margin around it; a pan
/// or a glide inside that margin only moves the drawing, and a zoom under way only scales it, so that WPF sends the
/// render thread a new transform instead of every node, line and amount again. Everything else that changes what is
/// drawn (a node chosen, hovered or moved, a new graph, a frame of an animation) makes the drawing stale.
/// </summary>
/// <remarks>
/// While a zoom is under way the drawing is scaled as it stands, so that the lines and the icons grow and shrink with
/// it for a moment; it is drawn again at its own zoom when the zoom ends, which is also when the lines become exactly
/// 1.5 px again. Without WPF, so that the rules are tested on their own.
/// </remarks>
public sealed class SceneWindow
{
    /// <summary>
    /// How much is drawn around the part on screen, as a share of its width and height on each side: room for a pan or
    /// a zoom out of a step or two before the scene is drawn again, at about twice the area on screen.
    /// </summary>
    public const double Margin = 0.25;

    /// <summary>The part of the surface the drawing covers, or <see langword="null"/> while it is stale.</summary>
    private GraphRect? _region;

    /// <summary>The zoom the drawing was made at.</summary>
    private double _zoom;

    /// <summary>Gets the part of the surface the drawing covers, or <see langword="null"/> while it is stale.</summary>
    public GraphRect? Region => _region;

    /// <summary>
    /// Says whether the drawing made last still shows the surface as it should be now: nothing has made it stale, it
    /// covers the part on screen, and it was made at this zoom or a zoom is under way.
    /// </summary>
    /// <param name="visible">The part of the surface on screen now.</param>
    /// <param name="zoom">The zoom now.</param>
    /// <param name="zooming">Whether a zoom is under way, when the drawing may be scaled as it stands.</param>
    /// <returns><see langword="true"/> when the drawing can be moved instead of made again.</returns>
    public bool Covers(GraphRect visible, double zoom, bool zooming)
    {
        if (_region is not { } region || visible.Width <= 0 || visible.Height <= 0)
        {
            return false;
        }

        // A tiny difference is a rounding of the same zoom, not a new one.
        var sameZoom = Math.Abs(zoom - _zoom) <= _zoom * 1e-9;
        return (sameZoom || zooming) && Within(visible, region);
    }

    /// <summary>
    /// Gives the part of the surface to draw the scene over now, the part on screen and a margin around it, and
    /// remembers it with the zoom as what the new drawing covers.
    /// </summary>
    /// <param name="visible">The part of the surface on screen.</param>
    /// <param name="zoom">The zoom.</param>
    /// <param name="margin">
    /// The margin, as a share of the part on screen on each side; 0 while something on the surface moves and the
    /// scene is drawn again in every frame anyway, so that those frames draw no more than is seen.
    /// </param>
    /// <returns>The part to draw.</returns>
    public GraphRect Take(GraphRect visible, double zoom, double margin)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(margin);
        var region = new GraphRect(
            visible.X - (visible.Width * margin),
            visible.Y - (visible.Height * margin),
            visible.Width * (1 + (2 * margin)),
            visible.Height * (1 + (2 * margin)));
        _region = region;
        _zoom = zoom;
        return region;
    }

    /// <summary>Makes the drawing stale, so that the next frame draws the scene again.</summary>
    public void Forget() => _region = null;

    /// <summary>Says whether one rectangle lies wholly inside another.</summary>
    /// <param name="inner">The rectangle that should lie inside.</param>
    /// <param name="outer">The rectangle around it.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool Within(GraphRect inner, GraphRect outer) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
}
