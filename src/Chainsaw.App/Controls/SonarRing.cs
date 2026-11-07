using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace Chainsaw.App.Controls;

/// <summary>
/// The sonar of sketch SN1a: no ring and no line, but a soft, blurred glow in the accent's tone that swells slowly out
/// of the node's own rounded outline, grows more blurred as it widens and dissolves; one wave at a time, then a rest,
/// over a faint glow that always lies behind the node. It marks the step the user stands at (sketch GR6c). Put it in
/// the same grid cell as the node, before it: it takes no room of its own, is arranged to the node's size and draws
/// outside and behind it, so the glow always stands centred on the node. It shows only the faint still glow when
/// Windows has animations turned off, and runs only while it is shown.
/// </summary>
public sealed class SonarRing : FrameworkElement
{
    /// <summary>The glow's colour, normally the accent.</summary>
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(SonarRing),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender, OnLookChanged));

    /// <summary>The corner radius of the node's outline, which the glow starts from and keeps as it grows.</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(SonarRing),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>How long, in seconds, one wave and its rest take together (SN1a's slider, 6 s).</summary>
    private const double PeriodSeconds = 6;

    /// <summary>The share of the period the wave swells and dissolves in; the rest of it is the pause.</summary>
    private const double WaveShare = 0.72;

    /// <summary>The share of the period at which the wave is at its strongest.</summary>
    private const double PeakShare = 0.36;

    /// <summary>How far, in pixels, the wave swells out from the node's outline before it is gone.</summary>
    private const double Reach = 30;

    /// <summary>How visible the wave is at its strongest.</summary>
    private const double PeakOpacity = 0.42;

    /// <summary>The glow's blur in pixels; the wave starts at half of it and ends at one and a half.</summary>
    private const double Blur = 18;

    /// <summary>How far out from the outline the faint resting glow stands.</summary>
    private const double RestReach = 2;

    /// <summary>The resting glow's blur, as a share of <see cref="Blur"/>.</summary>
    private const double RestBlurShare = 0.8;

    /// <summary>The resting glow's opacity while the wave runs, as a share of <see cref="PeakOpacity"/>.</summary>
    private const double RestShare = 0.32;

    /// <summary>The resting glow's opacity when the wave stands still, as a share of <see cref="PeakOpacity"/>.</summary>
    private const double StillShare = 0.45;

    /// <summary>
    /// How many filled outlines make up one blurred glow. WPF has no cheap blur for a drawing, so the blur is laid as
    /// outlines from inside to outside the edge whose opacities add up to a soft falloff.
    /// </summary>
    private const int BlurLayers = 10;

    /// <summary>How long, in seconds, the glow takes to fade in when the wave starts, so it never pops up.</summary>
    private const double EntrySeconds = 0.6;

    /// <summary>The frozen colour the glow is filled with, made again only when the colour changes.</summary>
    private Brush? _fill;

    /// <summary>When the wave started, on the render clock; null while it does not run.</summary>
    private TimeSpan? _start;

    /// <summary>The render clock's time of the frame last drawn, so a frame is drawn once.</summary>
    private TimeSpan _lastFrame;

    /// <summary>How far into the wave, in seconds, the frame to draw is.</summary>
    private double _elapsed;

    /// <summary>Whether the element follows the render clock.</summary>
    private bool _running;

    /// <summary>Creates the glow, which never takes the mouse or room in the layout.</summary>
    public SonarRing()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
        Focusable = false;
        IsVisibleChanged += (_, _) => Run();

        // Windows' setting and the render clock are static events, so the glow listens only while it is in the
        // window and is never held by them.
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

    /// <summary>Gets or sets the glow's colour.</summary>
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

    /// <summary>Takes no room, so that the node alone decides the cell's size and the glow follows it.</summary>
    /// <param name="availableSize">The room offered.</param>
    /// <returns>An empty size.</returns>
    protected override Size MeasureOverride(Size availableSize) => default;

    /// <summary>Draws the resting glow and, while the wave runs, the wave swelling out over it.</summary>
    /// <param name="drawingContext">Where to draw.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        var fill = _fill ??= Freeze(Stroke);
        if (!_running)
        {
            DrawGlow(drawingContext, fill, RestReach, Blur * RestBlurShare, PeakOpacity * StillShare);
            return;
        }

        var entry = Math.Min(1, _elapsed / EntrySeconds);
        DrawGlow(drawingContext, fill, RestReach, Blur * RestBlurShare, entry * PeakOpacity * RestShare);

        var phase = _elapsed / PeriodSeconds % 1;
        if (phase >= WaveShare)
        {
            return;
        }

        var growth = EaseInOut(phase / WaveShare);
        DrawGlow(drawingContext, fill, Reach * growth, Blur * (0.5 + growth), entry * PeakOpacity * WaveOpacity(phase));
    }

    /// <summary>How visible the wave is at a point in the period: it rises to its peak and then dissolves.</summary>
    /// <param name="phase">Where in the period, from 0 to <see cref="WaveShare"/>.</param>
    /// <returns>The share of the peak opacity, from 0 to 1.</returns>
    private static double WaveOpacity(double phase) => phase < PeakShare
        ? EaseInOut(phase / PeakShare)
        : 1 - EaseInOut((phase - PeakShare) / (WaveShare - PeakShare));

    /// <summary>A gentle ease in and out, the sketch's <c>cubic-bezier(.37, 0, .63, 1)</c> as a sine.</summary>
    /// <param name="t">The progress, from 0 to 1.</param>
    /// <returns>The eased progress.</returns>
    private static double EaseInOut(double t) => (1 - Math.Cos(Math.PI * Math.Clamp(t, 0, 1))) / 2;

    /// <summary>Drops the colour when it changes, so the next frame makes a new one.</summary>
    /// <param name="d">The glow.</param>
    /// <param name="e">The change.</param>
    private static void OnLookChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SonarRing)d)._fill = null;

    /// <summary>Makes a frozen copy of the colour, so drawing it every frame costs nothing.</summary>
    /// <param name="brush">The colour.</param>
    /// <returns>The frozen colour.</returns>
    private static Brush Freeze(Brush brush)
    {
        var frozen = brush.CloneCurrentValue();
        frozen.Freeze();
        return frozen;
    }

    /// <summary>
    /// Draws one blurred glow in the outline's shape: filled outlines from <paramref name="blur"/> inside the edge at
    /// <paramref name="distance"/> to as far outside it, so that the overlap is strongest in the middle and fades out.
    /// </summary>
    /// <param name="drawingContext">Where to draw.</param>
    /// <param name="fill">The frozen colour.</param>
    /// <param name="distance">How far out from the node's edge the glow's edge stands.</param>
    /// <param name="blur">How soft the glow's edge is, in pixels.</param>
    /// <param name="opacity">How visible the glow is.</param>
    private void DrawGlow(DrawingContext drawingContext, Brush fill, double distance, double blur, double opacity)
    {
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0 || opacity <= 0)
        {
            return;
        }

        drawingContext.PushOpacity(opacity / BlurLayers);
        for (var layer = 0; layer < BlurLayers; layer++)
        {
            var spread = distance - blur + 2 * blur * (layer + 0.5) / BlurLayers;
            var inset = Math.Max(spread, -Math.Min(size.Width, size.Height) / 2);
            var bounds = new Rect(-inset, -inset, size.Width + 2 * inset, size.Height + 2 * inset);
            var radius = Math.Max(0, CornerRadius + inset);
            drawingContext.DrawRoundedRectangle(fill, null, bounds, radius, radius);
        }

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

    /// <summary>Runs the wave while the glow is shown and animations are on; otherwise the glow stands still.</summary>
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

    /// <summary>Stops the wave and lets the faint resting glow stand still.</summary>
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
