using System.Windows;
using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// The overview strip at the bottom of the view Tidslinje (sketch TL3a): the whole case at its true pace, a day line
/// per day with the date every week, a stroke per event in its colour, and a quiet frame around what is shown above.
/// Only drawing; the places come from <see cref="TimelineStrip"/>.
/// </summary>
public sealed class TimelineStripView : FrameworkElement
{
    /// <summary>The strip to draw.</summary>
    public static readonly DependencyProperty StripProperty = DependencyProperty.Register(
        nameof(Strip), typeof(TimelineStrip), typeof(TimelineStripView),
        new FrameworkPropertyMetadata(TimelineStrip.None, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Where the strip's line runs.</summary>
    private const double LineY = 18;

    /// <summary>Creates the strip.</summary>
    public TimelineStripView() => Ink = new ChartDrawing(this);

    /// <summary>Gets or sets the strip.</summary>
    public TimelineStrip Strip
    {
        get => (TimelineStrip)GetValue(StripProperty);
        set => SetValue(StripProperty, value);
    }

    /// <summary>Gets the brushes and the text.</summary>
    private ChartDrawing Ink { get; }

    /// <summary>Draws the strip.</summary>
    /// <param name="drawingContext">Where to draw.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var dc = drawingContext;
        var strip = Strip ?? TimelineStrip.None;
        if (strip.Width <= 0)
        {
            return;
        }

        var line = Ink.Pen("Brush.BorderStrong", 1);
        dc.DrawLine(line, new Point(0, LineY), new Point(strip.Width, LineY));
        var dim = Ink.Brush("Brush.TextDim");
        foreach (var tick in strip.Ticks)
        {
            dc.DrawLine(line, new Point(tick.X, tick.IsWeek ? 12 : 15), new Point(tick.X, 21));
            if (tick.Label.Length > 0)
            {
                var text = Ink.Text(tick.Label, 10.5, dim);
                var x = Math.Clamp(tick.X - (text.Width / 2), 0, Math.Max(0, strip.Width - text.Width));
                dc.DrawText(text, new Point(x, 27));
            }
        }

        foreach (var mark in strip.Marks)
        {
            dc.PushOpacity(mark.IsHidden ? 0.15 : 0.9);
            dc.DrawLine(Ink.Pen(mark.Brush, 1.6), new Point(mark.X, 9), new Point(mark.X, 27));
            dc.Pop();
        }

        foreach (var (x0, x1) in strip.Windows)
        {
            var box = new Rect(x0 - 2, 4, Math.Max(6, x1 - x0) + 4, 28);
            dc.PushOpacity(0.05);
            dc.DrawRoundedRectangle(Ink.Brush("Brush.Text"), null, box, 6, 6);
            dc.Pop();
            dc.PushOpacity(0.6);
            dc.DrawRoundedRectangle(null, Ink.Pen("Brush.TextMuted", 1), box, 6, 6);
            dc.Pop();
        }
    }
}
