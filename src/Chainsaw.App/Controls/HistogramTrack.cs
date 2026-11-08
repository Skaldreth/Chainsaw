using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Controls;

/// <summary>What a histogram track's handles do (sketch EX2).</summary>
public enum TrackKind
{
    /// <summary>Two handles set a lowest and a highest (<em>Belopp</em>).</summary>
    Range,

    /// <summary>A band is dragged along (<em>Tid</em>), with the traced arrival as a dashed line.</summary>
    Window,
}

/// <summary>
/// A filter's track with the distribution behind it (sketch EX2): bars per slice, brighter inside what is let through,
/// and either two round handles on a line (the amount) or a band that is dragged along (the time window) with the
/// traced arrival as a dashed accent line. All its numbers are shares from 0 to 1, set by the view model.
/// </summary>
public sealed class HistogramTrack : FrameworkElement
{
    /// <summary>The options of an end: it is drawn again when it changes, and binds both ways by default.</summary>
    private const FrameworkPropertyMetadataOptions RenderedTwoWay =
        FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault;

    /// <summary>The bars.</summary>
    public static readonly DependencyProperty BarsProperty = DependencyProperty.Register(
        nameof(Bars), typeof(IReadOnlyList<HistogramBar>), typeof(HistogramTrack),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The handles' kind.</summary>
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(TrackKind), typeof(HistogramTrack),
        new FrameworkPropertyMetadata(TrackKind.Range, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The low end, or the window's start.</summary>
    public static readonly DependencyProperty LowProperty = DependencyProperty.Register(
        nameof(Low), typeof(double), typeof(HistogramTrack),
        new FrameworkPropertyMetadata(
            0.0, RenderedTwoWay));

    /// <summary>The high end, or the window's end.</summary>
    public static readonly DependencyProperty HighProperty = DependencyProperty.Register(
        nameof(High), typeof(double), typeof(HistogramTrack),
        new FrameworkPropertyMetadata(
            1.0, RenderedTwoWay));

    /// <summary>The traced arrival's place, or a negative number for none.</summary>
    public static readonly DependencyProperty MarkerProperty = DependencyProperty.Register(
        nameof(Marker), typeof(double), typeof(HistogramTrack),
        new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The bars' width.</summary>
    public static readonly DependencyProperty BarWidthProperty = DependencyProperty.Register(
        nameof(BarWidth), typeof(double), typeof(HistogramTrack),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The handles' size (sketch EX2: 12).</summary>
    private const double Handle = 12;

    /// <summary>Where the range's line lies above the bottom.</summary>
    private const double LineFromBottom = 7;

    /// <summary>Where the range's bars stand above the bottom.</summary>
    private const double BarsFromBottom = 14;

    /// <summary>Which handle a drag holds: the low or the high.</summary>
    private bool _holdsLow;

    /// <summary>Creates the track, which takes the pointer anywhere on it.</summary>
    public HistogramTrack()
    {
        Cursor = Cursors.SizeWE;
        Focusable = false;
    }

    /// <summary>
    /// Raised when the window is dragged to a place, from 0 to 1, for the view model to centre it on.
    /// </summary>
    public event EventHandler<double>? WindowMoved;

    /// <summary>Gets or sets the bars.</summary>
    public IReadOnlyList<HistogramBar>? Bars
    {
        get => (IReadOnlyList<HistogramBar>?)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    /// <summary>Gets or sets the handles' kind.</summary>
    public TrackKind Kind
    {
        get => (TrackKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Gets or sets the low end.</summary>
    public double Low
    {
        get => (double)GetValue(LowProperty);
        set => SetValue(LowProperty, value);
    }

    /// <summary>Gets or sets the high end.</summary>
    public double High
    {
        get => (double)GetValue(HighProperty);
        set => SetValue(HighProperty, value);
    }

    /// <summary>Gets or sets the traced arrival's place.</summary>
    public double Marker
    {
        get => (double)GetValue(MarkerProperty);
        set => SetValue(MarkerProperty, value);
    }

    /// <summary>Gets or sets the bars' width.</summary>
    public double BarWidth
    {
        get => (double)GetValue(BarWidthProperty);
        set => SetValue(BarWidthProperty, value);
    }

    /// <summary>Draws the bars and the handles.</summary>
    /// <param name="drawingContext">Where to draw.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var width = ActualWidth;
        var height = ActualHeight;
        var window = Kind == TrackKind.Window;
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (window)
        {
            drawingContext.DrawRoundedRectangle(
                Find("Brush.Background"), new Pen(Find("Brush.Border"), 1), new Rect(0.5, 0.5, width - 1, height - 1),
                6, 6);
        }

        DrawBars(drawingContext, width, height, window);
        if (window)
        {
            DrawWindow(drawingContext, width, height);
        }
        else
        {
            DrawRange(drawingContext, width, height);
        }
    }

    /// <summary>Takes hold of the nearest handle, or of the window, and moves it to the pointer.</summary>
    /// <param name="e">The press.</param>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        var share = ShareAt(e.GetPosition(this).X);
        _holdsLow = Math.Abs(share - Low) < Math.Abs(share - High);
        CaptureMouse();
        MoveTo(share);
        e.Handled = true;
    }

    /// <summary>Moves what is held with the pointer.</summary>
    /// <param name="e">The move.</param>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        if (IsMouseCaptured)
        {
            MoveTo(ShareAt(e.GetPosition(this).X));
        }
    }

    /// <summary>Lets go.</summary>
    /// <param name="e">The release.</param>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <summary>Moves the held handle, or the window, to a share of the track.</summary>
    /// <param name="share">The share.</param>
    private void MoveTo(double share)
    {
        if (Kind == TrackKind.Window)
        {
            WindowMoved?.Invoke(this, share);
        }
        else if (_holdsLow)
        {
            Low = share;
        }
        else
        {
            High = share;
        }
    }

    /// <summary>Gives the share of the track under a point.</summary>
    /// <param name="x">The point's place across.</param>
    /// <returns>From 0 to 1.</returns>
    private double ShareAt(double x) => ActualWidth <= 0 ? 0 : Math.Clamp(x / ActualWidth, 0, 1);

    /// <summary>Draws the bars, brighter inside what is let through.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="width">The track's width.</param>
    /// <param name="height">The track's height.</param>
    /// <param name="window">Whether it is the time's track.</param>
    private void DrawBars(DrawingContext dc, double width, double height, bool window)
    {
        if (Bars is not { Count: > 0 } bars)
        {
            return;
        }

        var inside = Find(window ? "Brush.TextDim" : "Brush.TextFaint");
        var outside = Find(window ? "Brush.BorderStrong" : "Brush.Border");
        var bottom = window ? height - 3 : height - BarsFromBottom;
        var slice = width / bars.Count;
        for (var index = 0; index < bars.Count; index++)
        {
            var bar = bars[index];
            if (bar.Height <= 0)
            {
                continue;
            }

            var rect = new Rect((index * slice) + 0.5, bottom - bar.Height, Math.Min(BarWidth, slice), bar.Height);
            dc.DrawRoundedRectangle(bar.Inside ? inside : outside, null, rect, 1.5, 1.5);
        }
    }

    /// <summary>Draws the range: the line, the part let through and the two handles.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="width">The track's width.</param>
    /// <param name="height">The track's height.</param>
    private void DrawRange(DrawingContext dc, double width, double height)
    {
        var y = height - LineFromBottom;
        dc.DrawRectangle(Find("Brush.Border"), null, new Rect(0, y - 1, width, 2));
        var (low, high) = (Low * width, High * width);
        dc.DrawRectangle(Find("Brush.TextMuted"), null, new Rect(low, y - 1, Math.Max(0, high - low), 2));
        var ring = new Pen(Find("Brush.Background"), 2);
        foreach (var x in new[] { low, high })
        {
            dc.DrawEllipse(Find("Brush.Text"), ring, new Point(x, y), Handle / 2, Handle / 2);
        }
    }

    /// <summary>Draws the window as a band with light edges, and the traced arrival as a dashed accent line.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="width">The track's width.</param>
    /// <param name="height">The track's height.</param>
    private void DrawWindow(DrawingContext dc, double width, double height)
    {
        var (start, end) = (Low * width, Math.Max((Low * width) + 3, High * width));
        dc.PushOpacity(0.07);
        dc.DrawRectangle(Find("Brush.Text"), null, new Rect(start, 0, end - start, height));
        dc.Pop();
        var edge = new Pen(Find("Brush.TextMuted"), 1.5);
        dc.DrawLine(edge, new Point(start, 0), new Point(start, height));
        dc.DrawLine(edge, new Point(end, 0), new Point(end, height));
        if (Marker >= 0)
        {
            var dashed = new Pen(Find("Brush.Accent"), 1.5) { DashStyle = new DashStyle([2, 2], 0) };
            dc.DrawLine(dashed, new Point(Marker * width, 0), new Point(Marker * width, height));
        }
    }

    /// <summary>Finds a brush of the theme.</summary>
    /// <param name="key">The brush's key.</param>
    /// <returns>The brush.</returns>
    private Brush Find(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;
}
