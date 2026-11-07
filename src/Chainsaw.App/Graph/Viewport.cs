using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>
/// The part of the graph's surface that is on screen: where the surface is panned to and how far it is zoomed. The
/// graph draws only what the viewport shows (principle 6).
/// </summary>
/// <param name="OffsetX">Where the surface's origin is on screen, horizontally.</param>
/// <param name="OffsetY">Where the surface's origin is on screen, vertically.</param>
/// <param name="Zoom">How many screen pixels one unit of the surface takes.</param>
public sealed record Viewport(double OffsetX, double OffsetY, double Zoom)
{
    /// <summary>The farthest the graph zooms out.</summary>
    public const double MinimumZoom = 0.2;

    /// <summary>The farthest the graph zooms in.</summary>
    public const double MaximumZoom = 3;

    /// <summary>How much one notch of the wheel zooms.</summary>
    public const double WheelStep = 1.15;

    /// <summary>The space kept around the graph when it is fitted to the screen, in screen pixels.</summary>
    public const double FitMargin = 48;

    /// <summary>Gets the viewport at zoom 1 with the surface's origin in the corner.</summary>
    public static Viewport Initial { get; } = new(0, 0, 1);

    /// <summary>Gets the zoom, kept within <see cref="MinimumZoom"/> and <see cref="MaximumZoom"/>.</summary>
    public double Zoom { get; } = Math.Clamp(Zoom, MinimumZoom, MaximumZoom);

    /// <summary>Gives the viewport as the case stores it with the graph's view.</summary>
    /// <returns>The stored part on screen.</returns>
    public GraphViewport ToStored() => new(OffsetX, OffsetY, Zoom);

    /// <summary>Reads a viewport the case stores with the graph's view.</summary>
    /// <param name="stored">The stored part on screen.</param>
    /// <returns>The viewport, its zoom kept within the limits.</returns>
    public static Viewport FromStored(GraphViewport stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return new Viewport(stored.OffsetX, stored.OffsetY, stored.Zoom);
    }

    /// <summary>Gives where a point of the surface is on screen.</summary>
    /// <param name="point">The point on the surface.</param>
    /// <returns>The point on screen.</returns>
    public GraphPoint ToScreen(GraphPoint point) => new((point.X * Zoom) + OffsetX, (point.Y * Zoom) + OffsetY);

    /// <summary>Gives the point of the surface under a point on screen.</summary>
    /// <param name="point">The point on screen.</param>
    /// <returns>The point on the surface.</returns>
    public GraphPoint ToSurface(GraphPoint point) => new((point.X - OffsetX) / Zoom, (point.Y - OffsetY) / Zoom);

    /// <summary>Gives the part of the surface a screen of a size shows.</summary>
    /// <param name="width">The screen's width.</param>
    /// <param name="height">The screen's height.</param>
    /// <returns>The visible part of the surface.</returns>
    public GraphRect Visible(double width, double height)
    {
        var corner = ToSurface(new GraphPoint(0, 0));
        return new GraphRect(corner.X, corner.Y, Math.Max(width, 0) / Zoom, Math.Max(height, 0) / Zoom);
    }

    /// <summary>Gives the viewport moved by a distance on screen, as when the surface is dragged.</summary>
    /// <param name="dx">How far right, in screen pixels.</param>
    /// <param name="dy">How far down, in screen pixels.</param>
    /// <returns>The moved viewport.</returns>
    public Viewport Pan(double dx, double dy) => this with { OffsetX = OffsetX + dx, OffsetY = OffsetY + dy };

    /// <summary>
    /// Gives the viewport zoomed by a factor around a point on screen, which stays where it is, as under the pointer
    /// when the wheel turns.
    /// </summary>
    /// <param name="anchor">The point on screen that stays.</param>
    /// <param name="factor">How much to zoom; above 1 zooms in.</param>
    /// <returns>The zoomed viewport.</returns>
    public Viewport ZoomAt(GraphPoint anchor, double factor)
    {
        var fixedPoint = ToSurface(anchor);
        var zoom = Math.Clamp(Zoom * factor, MinimumZoom, MaximumZoom);
        return new Viewport(anchor.X - (fixedPoint.X * zoom), anchor.Y - (fixedPoint.Y * zoom), zoom);
    }

    /// <summary>
    /// Gives the viewport that shows a part of the surface whole on a screen of a size, centred, never zoomed in past 1
    /// so that a small graph keeps its natural size.
    /// </summary>
    /// <param name="content">The part of the surface to show.</param>
    /// <param name="width">The screen's width.</param>
    /// <param name="height">The screen's height.</param>
    /// <returns>The viewport.</returns>
    public static Viewport Fit(GraphRect content, double width, double height)
    {
        var room = (Width: width - (2 * FitMargin), Height: height - (2 * FitMargin));
        if (room.Width <= 0 || room.Height <= 0)
        {
            return Initial;
        }

        var zoom = Math.Clamp(
            Math.Min(1, Math.Min(room.Width / Math.Max(content.Width, 1), room.Height / Math.Max(content.Height, 1))),
            MinimumZoom,
            MaximumZoom);
        var centre = content.Centre;
        return new Viewport((width / 2) - (centre.X * zoom), (height / 2) - (centre.Y * zoom), zoom);
    }
}
