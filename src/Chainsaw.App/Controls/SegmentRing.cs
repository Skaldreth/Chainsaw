using System.Windows;
using System.Windows.Media;

namespace Chainsaw.App.Controls;

/// <summary>
/// The segmented ring of <em>Allt avgjort</em> (sketch KF2b): one arc per suggestion the user has decided, green for an
/// approved one and faint for a dismissed one, with a small gap between them; a whole faint ring when none is decided.
/// The check inside is a <see cref="GridIcon"/> laid over it in XAML.
/// </summary>
public sealed class SegmentRing : FrameworkElement
{
    /// <summary>The segments, in order: <see langword="true"/> for an approved suggestion.</summary>
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IReadOnlyList<bool>), typeof(SegmentRing),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The ring's line, as in the sketch.</summary>
    private const double Line = 4;

    /// <summary>The gap between two arcs, in degrees.</summary>
    private const double Gap = 14;

    /// <summary>Gets or sets the segments.</summary>
    public IReadOnlyList<bool>? Segments
    {
        get => (IReadOnlyList<bool>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    /// <summary>Draws the ring.</summary>
    /// <param name="drawingContext">Where to draw.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var radius = (Math.Min(ActualWidth, ActualHeight) - Line) / 2;
        if (radius <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var segments = Segments ?? [];
        var faint = GraphPalette.Find(this, "Brush.TextFaint");
        if (segments.Count == 0)
        {
            drawingContext.DrawEllipse(null, new Pen(faint, Line), center, radius, radius);
            return;
        }

        var green = GraphPalette.Find(this, "Brush.Success");
        var share = 360.0 / segments.Count;
        var gap = segments.Count == 1 ? 0 : Gap;
        for (var index = 0; index < segments.Count; index++)
        {
            var start = (index * share) + (gap / 2);
            var pen = new Pen(segments[index] ? green : faint, Line)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            drawingContext.DrawGeometry(null, pen, Arc(center, radius, start, share - gap));
        }
    }

    /// <summary>Gives an arc clockwise from the top.</summary>
    /// <param name="center">The ring's center.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="start">Where it starts, in degrees from the top.</param>
    /// <param name="sweep">How far it goes, in degrees.</param>
    /// <returns>The arc.</returns>
    private static Geometry Arc(Point center, double radius, double start, double sweep)
    {
        if (sweep >= 359.9)
        {
            return new EllipseGeometry(center, radius, radius);
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(PointOn(center, radius, start), false, false);
            context.ArcTo(
                PointOn(center, radius, start + sweep),
                new Size(radius, radius),
                0,
                sweep > 180,
                SweepDirection.Clockwise,
                true,
                false);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>Gives the point on the ring at an angle from the top, clockwise.</summary>
    /// <param name="center">The center.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="degrees">The angle.</param>
    /// <returns>The point.</returns>
    private static Point PointOn(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(center.X + (radius * Math.Sin(radians)), center.Y - (radius * Math.Cos(radians)));
    }
}
