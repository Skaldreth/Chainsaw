using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace Chainsaw.App.Controls;

/// <summary>
/// Soft rings that grow out from a node's own rounded outline and fade, again and again, like sonar: it marks the step
/// the user stands at in the empty graph (sketch GR6c). Put it in the same grid cell as the node: it takes no room of
/// its own, is arranged to the node's size and draws outside it, so the rings always stand centred on the node.
/// It shows one faint, still ring when Windows has animations turned off, and runs only while it is shown.
/// </summary>
public sealed class SonarRing : FrameworkElement
{
    /// <summary>The rings' colour, normally the accent.</summary>
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(SonarRing),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender, OnLookChanged));

    /// <summary>The corner radius of the node's outline, which the rings start from and keep as they grow.</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(SonarRing),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>How many rings are under way at once, spread evenly over one wave.</summary>
    private const int RingCount = 3;

    /// <summary>How long, in seconds, one ring takes from the node's edge until it has faded away.</summary>
    private const double WaveSeconds = 2.7;

    /// <summary>How far, in pixels, a ring grows out from the node's outline before it is gone.</summary>
    private const double Reach = 22;

    /// <summary>How visible a ring is as it leaves the node's edge.</summary>
    private const double StartOpacity = 0.35;

    /// <summary>The rings' line width, thin like the node's own frame.</summary>
    private const double StrokeWidth = 1.5;

    /// <summary>How far out the one still ring stands when Windows has animations turned off.</summary>
    private const double StillReach = 6;

    /// <summary>How visible the still ring is.</summary>
    private const double StillOpacity = 0.2;

    /// <summary>The exponent of the ease-out: a ring leaves quickly and slows as it fades.</summary>
    private const double EaseExponent = 3;

    /// <summary>How long, in seconds, the rings take to fade in when the wave starts, so they never pop up.</summary>
    private const double EntrySeconds = 0.6;

    /// <summary>The frozen pen the rings are drawn with, made again only when the colour changes.</summary>
    private Pen? _pen;

    /// <summary>When the wave started, on the render clock; null while it does not run.</summary>
    private TimeSpan? _start;

    /// <summary>The render clock's time of the frame last drawn, so a frame is drawn once.</summary>
    private TimeSpan _lastFrame;

    /// <summary>How far into the wave, in seconds, the frame to draw is.</summary>
    private double _elapsed;

    /// <summary>Whether the element follows the render clock.</summary>
    private bool _running;

    /// <summary>Creates the rings, which never take the mouse or room in the layout.</summary>
    public SonarRing()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
        Focusable = false;
        IsVisibleChanged += (_, _) => Run();

        // Windows' setting and the render clock are static events, so the rings listen only while they are in the
        // window and are never held by them.
        Loaded += (_, _) =>
        {
            SystemParameters.StaticPropertyChanged += OnSystemChanged;
            Run();
        };
        Unloaded += (_, _) =>
        {
            SystemParameters.StaticPropertyChanged -= OnSystemChanged;
            Stop();
        };
    }

    /// <summary>Gets or sets the rings' colour.</summary>
    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>Gets or sets the corner radius of the node's outline.</summary>
    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Takes no room, so that the node alone decides the cell's size and the rings follow it.</summary>
    /// <param name="availableSize">The room offered.</param>
    /// <returns>An empty size.</returns>
    protected override Size MeasureOverride(Size availableSize) => default;

    /// <summary>Draws the rings around the outline, or the one still ring when the wave does not run.</summary>
    /// <param name="drawingContext">Where to draw.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        var pen = _pen ??= MakePen(Stroke);
        if (!_running)
        {
            DrawRing(drawingContext, pen, StillReach, StillOpacity);
            return;
        }

        var entry = Math.Min(1, _elapsed / EntrySeconds);
        for (var ring = 0; ring < RingCount; ring++)
        {
            var progress = (_elapsed / WaveSeconds + (double)ring / RingCount) % 1;
            var eased = 1 - Math.Pow(1 - progress, EaseExponent);
            DrawRing(drawingContext, pen, Reach * eased, entry * StartOpacity * (1 - progress));
        }
    }

    /// <summary>Drops the pen when the colour changes, so the next frame makes a new one.</summary>
    /// <param name="d">The rings.</param>
    /// <param name="e">The change.</param>
    private static void OnLookChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SonarRing)d)._pen = null;

    /// <summary>Makes the frozen pen in the given colour.</summary>
    /// <param name="brush">The colour.</param>
    /// <returns>The pen.</returns>
    private static Pen MakePen(Brush brush)
    {
        var pen = new Pen(brush, StrokeWidth);
        pen.Freeze();
        return pen;
    }

    /// <summary>Draws one ring a distance out from the outline, keeping the outline's shape as it grows.</summary>
    /// <param name="drawingContext">Where to draw.</param>
    /// <param name="pen">The frozen pen.</param>
    /// <param name="distance">How far out from the node's edge the ring stands.</param>
    /// <param name="opacity">How visible the ring is.</param>
    private void DrawRing(DrawingContext drawingContext, Pen pen, double distance, double opacity)
    {
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0 || opacity <= 0)
        {
            return;
        }

        var bounds = new Rect(-distance, -distance, size.Width + 2 * distance, size.Height + 2 * distance);
        var radius = CornerRadius + distance;
        drawingContext.PushOpacity(opacity);
        drawingContext.DrawRoundedRectangle(null, pen, bounds, radius, radius);
        drawingContext.Pop();
    }

    /// <summary>Starts or stops the wave after Windows' animations were turned on or off.</summary>
    /// <param name="sender">The system parameters.</param>
    /// <param name="e">Which parameter changed.</param>
    private void OnSystemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
        {
            Dispatcher.BeginInvoke(Run);
        }
    }

    /// <summary>Runs the wave while the rings are shown and animations are on; otherwise they stand still.</summary>
    private void Run()
    {
        if (!IsLoaded || !IsVisible || !SystemParameters.ClientAreaAnimation)
        {
            Stop();
            return;
        }

        if (_running)
        {
            return;
        }

        _running = true;
        _start = null;
        CompositionTarget.Rendering += OnFrame;
        InvalidateVisual();
    }

    /// <summary>Stops the wave and lets one faint ring stand still.</summary>
    private void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        CompositionTarget.Rendering -= OnFrame;
        InvalidateVisual();
    }

    /// <summary>Moves the wave on to the render clock's time, once per frame.</summary>
    /// <param name="sender">The render clock.</param>
    /// <param name="e">The frame's arguments.</param>
    private void OnFrame(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs frame || frame.RenderingTime == _lastFrame)
        {
            return;
        }

        _lastFrame = frame.RenderingTime;
        _start ??= frame.RenderingTime;
        _elapsed = (frame.RenderingTime - _start.Value).TotalSeconds;
        InvalidateVisual();
    }
}
