using System.Windows;
using System.Windows.Media;

namespace Chainsaw.App.Controls;

/// <summary>
/// A ring that drains (sketch GR9a, <em>Väntar på källan</em>): a faint whole ring, and over it an arc from the top,
/// clockwise, as long as the share that is left. The chain's mark is laid inside it in XAML.
/// </summary>
public sealed class DrainRing : FrameworkElement
{
    /// <summary>How much is left, from 0 to 1.</summary>
    public static readonly DependencyProperty ShareProperty = DependencyProperty.Register(
        nameof(Share), typeof(double), typeof(DrainRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The arc's colour.</summary>
    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(
        nameof(Ink), typeof(Brush), typeof(DrainRing),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The faint ring's colour.</summary>
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(DrainRing),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The ring's line, as in the sketch.</summary>
    private const double Line = 2;

    /// <summary>Gets or sets how much is left, from 0 to 1.</summary>
    public double Share
    {
        get => (double)GetValue(ShareProperty);
        set => SetValue(ShareProperty, value);
    }

    /// <summary>Gets or sets the arc's colour.</summary>
    public Brush Ink
    {
        get => (Brush)GetValue(InkProperty);
        set => SetValue(InkProperty, value);
    }

    /// <summary>Gets or sets the faint ring's colour.</summary>
    public Brush Track
    {
        get => (Brush)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    /// <summary>Draws the faint ring and the arc that is left.</summary>
    /// <param name="drawingContext">Where to draw.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var radius = (Math.Min(ActualWidth, ActualHeight) - Line) / 2;
        if (radius <= 0)
        {
            return;
        }

        var centre = new Point(ActualWidth / 2, ActualHeight / 2);
        drawingContext.DrawEllipse(null, new Pen(Track, Line), centre, radius, radius);
        var share = Math.Clamp(Share, 0, 1);
        if (share <= 0)
        {
            return;
        }

        var pen = new Pen(Ink, Line) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (share >= 1)
        {
            drawingContext.DrawEllipse(null, pen, centre, radius, radius);
            return;
        }

        var angle = share * 2 * Math.PI;
        var start = new Point(centre.X, centre.Y - radius);
        var end = new Point(centre.X + (radius * Math.Sin(angle)), centre.Y - (radius * Math.Cos(angle)));
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new Size(radius, radius), 0, share > 0.5, SweepDirection.Clockwise, true, true);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }
}
