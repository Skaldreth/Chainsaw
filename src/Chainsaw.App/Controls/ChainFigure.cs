using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Chainsaw.App.Figure;

namespace Chainsaw.App.Controls;

/// <summary>
/// The figure under the lock screen's form and the first-run setup (sketches ID8f and LS1b): two chains in the accent and
/// the double helix between them, drawn as shapes, with the particles drifting off the helix. <see cref="BandMotion"/>
/// moves it; this draws it about sixty times a second while it is shown, and feeds it the window's acceleration so the
/// band swings when the window is dragged. With Windows' animations off it stands still at rest and the timer rests,
/// except for the short way to the shell after the right key.
/// </summary>
public sealed class ChainFigure : FrameworkElement
{
    /// <summary>About sixty frames a second, so the swing and the morph run smoothly.</summary>
    private static readonly TimeSpan FrameTime = TimeSpan.FromMilliseconds(16);

    /// <summary>The longest step taken at once, so that a pause never makes the figure jump.</summary>
    private static readonly TimeSpan LongestStep = TimeSpan.FromMilliseconds(50);

    /// <summary>The accent, should the theme not give it.</summary>
    private static readonly Color FallbackAccent = Color.FromRgb(0xC5, 0x5A, 0x3D);

    /// <summary>The red of the pulse after a wrong key, should the theme not give it.</summary>
    private static readonly Color FallbackPulse = Color.FromRgb(0xD7, 0x32, 0x2B);

    /// <summary>The largest window acceleration passed on, so a window snapped across the screen never throws the band.</summary>
    private const double LargestPush = 40000;

    /// <summary>The darker half of a rung, as a share of the accent.</summary>
    private const double DarkRung = 0.62;

    /// <summary>One joint, as a frozen shape in the logo's units.</summary>
    private static readonly Geometry Joint = Frozen(Geometry.Parse(ChainLink.Joint));

    /// <summary>One link's bars below its joint, as a frozen shape in the logo's units.</summary>
    private static readonly Geometry Bars = Frozen(Geometry.Parse(ChainLink.Bars));

    /// <summary>The motion, stepped by the timer.</summary>
    private readonly BandMotion _motion = new(new Random());

    /// <summary>Steps the motion while the figure is shown.</summary>
    private readonly DispatcherTimer _timer;

    /// <summary>Measures the time between two steps.</summary>
    private readonly Stopwatch _clock = new();

    /// <summary>The particles' characters, made once per screen scale.</summary>
    private readonly Dictionary<(char Character, bool IsBase), FormattedText> _glyphs = [];

    /// <summary>The brushes of the accent's shades, by shade and red tint, made once each.</summary>
    private readonly Dictionary<(int Shade, int Tint), SolidColorBrush> _brushes = [];

    /// <summary>The window's place and speed at the last step, to tell its acceleration.</summary>
    private (Point Place, Vector Speed, Vector Push)? _window;

    /// <summary>Creates the figure; it takes no input of its own and never takes focus.</summary>
    public ChainFigure()
    {
        IsHitTestVisible = false;
        Focusable = false;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = FrameTime };
        _timer.Tick += (_, _) => Advance();
        Loaded += (_, _) =>
        {
            SystemParameters.StaticPropertyChanged += OnSystemChanged;
            Restart();
        };
        Unloaded += (_, _) =>
        {
            SystemParameters.StaticPropertyChanged -= OnSystemChanged;
            UpdateTimer();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Restart();
            }
            else
            {
                UpdateTimer();
            }
        };
    }

    /// <summary>Occurs after each step, so the page can fade its form and itself on the way to the shell.</summary>
    public event EventHandler? Stepped;

    /// <summary>Gets the motion, which tells how far the way to the shell has come.</summary>
    public BandMotion Motion => _motion;

    /// <summary>Starts the figure again from the start, as when the lock screen is shown.</summary>
    public void Restart()
    {
        _motion.Restart();
        _motion.IsStill = !SystemParameters.ClientAreaAnimation;
        _window = null;
        UpdateTimer();
        InvalidateVisual();
    }

    /// <summary>A character was typed in the field: a base lets go of the helix.</summary>
    public void Typed() => _motion.Typed();

    /// <summary>A wrong key: the band jerks, pulses red and lets a puff of characters go.</summary>
    public void Wrong()
    {
        _motion.Wrong();
        InvalidateVisual();
    }

    /// <summary>The right key: the lens in the middle becomes the node Nytt ärende where it stands.</summary>
    /// <param name="node">Where the node stands, in this figure's own coordinates.</param>
    public void Right(Rect node)
    {
        _motion.Right(new NodeTarget(node.X, node.Y, node.Width, node.Height, 10));
        UpdateTimer();
        InvalidateVisual();
    }

    /// <summary>Draws the frame: the links, the strands behind, the node's face, the rungs, the strands in front, the particles.</summary>
    /// <param name="drawingContext">The drawing context.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (ActualWidth < 1 || ActualHeight < 1)
        {
            return;
        }

        var frame = _motion.Frame(ActualWidth, ActualHeight);
        var accent = Accent();
        var pulse = TryFindResource("Color.ErrorPulse") is Color red ? red : FallbackPulse;
        Brush Shade(double share) => ShadeBrush(accent, pulse, share, frame.Tint);

        foreach (var link in frame.Links)
        {
            DrawLink(drawingContext, link, Shade(1));
        }

        DrawStrands(drawingContext, frame.Back, Shade);
        if (frame.Node is { } face && face.Alpha > 0)
        {
            var target = face.Target;
            drawingContext.PushOpacity(face.Alpha);
            drawingContext.DrawRoundedRectangle(
                TryFindResource("Brush.Surface") as Brush ?? Brushes.Black,
                null,
                new Rect(target.X, target.Y, target.Width, target.Height),
                target.Radius,
                target.Radius);
            drawingContext.Pop();
        }

        DrawRungs(drawingContext, frame.Rungs, Shade(1), Shade(DarkRung));
        DrawStrands(drawingContext, frame.Front, Shade);
        DrawParticles(drawingContext);
    }

    /// <summary>Makes the characters again for the new scale when the window moves to another screen.</summary>
    /// <param name="oldDpi">The old scale.</param>
    /// <param name="newDpi">The new scale.</param>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _glyphs.Clear();
        base.OnDpiChanged(oldDpi, newDpi);
    }

    /// <summary>Freezes a shape, so that it is shared and drawn without being watched for changes.</summary>
    /// <param name="geometry">The shape.</param>
    /// <returns>The same shape, frozen.</returns>
    private static Geometry Frozen(Geometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Draws one link: its joint and, unless it is the last before the helix, its bars.</summary>
    /// <param name="drawingContext">The drawing context.</param>
    /// <param name="link">The link.</param>
    /// <param name="brush">Its colour.</param>
    private static void DrawLink(DrawingContext drawingContext, LinkPose link, Brush brush)
    {
        if (link.Alpha <= 0.01 || Math.Abs(link.Squash) < 0.01)
        {
            return;
        }

        var place = new Matrix();
        place.Scale(link.Scale * link.Squash, link.Scale);
        place.Rotate(link.Angle);
        place.Translate(link.X, link.Y);
        drawingContext.PushOpacity(link.Alpha);
        drawingContext.PushTransform(new MatrixTransform(place));
        drawingContext.DrawGeometry(brush, null, Joint);
        if (link.HasBars)
        {
            drawingContext.DrawGeometry(brush, null, Bars);
        }

        drawingContext.Pop();
        drawingContext.Pop();
    }

    /// <summary>Draws the stretches of strand, each in its shade and width.</summary>
    /// <param name="drawingContext">The drawing context.</param>
    /// <param name="stretches">The stretches.</param>
    /// <param name="shade">Gives the brush of a shade.</param>
    private static void DrawStrands(
        DrawingContext drawingContext, IEnumerable<StrandStretch> stretches, Func<double, Brush> shade)
    {
        foreach (var stretch in stretches)
        {
            if (stretch.Alpha <= 0.01 || stretch.Points.Count < 2)
            {
                continue;
            }

            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(stretch.Points[0].X, stretch.Points[0].Y), false, false);
                context.PolyLineTo(stretch.Points.Skip(1).Select(p => new Point(p.X, p.Y)).ToList(), true, true);
            }

            geometry.Freeze();
            var pen = new Pen(shade(stretch.Shade), stretch.Width) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            drawingContext.PushOpacity(stretch.Alpha);
            drawingContext.DrawGeometry(null, pen, geometry);
            drawingContext.Pop();
        }
    }

    /// <summary>Draws the rungs: a light half from strand one and a darker half from strand two.</summary>
    /// <param name="drawingContext">The drawing context.</param>
    /// <param name="rungs">The rungs.</param>
    /// <param name="light">The light half's colour.</param>
    /// <param name="dark">The dark half's colour.</param>
    private static void DrawRungs(DrawingContext drawingContext, IEnumerable<RungPose> rungs, Brush light, Brush dark)
    {
        var lightPen = new Pen(light, ChainBand.RungThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var darkPen = new Pen(dark, ChainBand.RungThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        lightPen.Freeze();
        darkPen.Freeze();
        foreach (var rung in rungs)
        {
            if (rung.Alpha <= 0.01)
            {
                continue;
            }

            drawingContext.PushOpacity(rung.Alpha);
            drawingContext.DrawLine(lightPen, new Point(rung.First.X, rung.First.Y), new Point(rung.Middle.X, rung.Middle.Y));
            drawingContext.DrawLine(darkPen, new Point(rung.Middle.X, rung.Middle.Y), new Point(rung.Second.X, rung.Second.Y));
            drawingContext.Pop();
        }
    }

    /// <summary>Gives the accent at a share of its lightness, turned towards the pulse's red by a tint; made once each.</summary>
    /// <param name="accent">The accent.</param>
    /// <param name="pulse">The pulse's red.</param>
    /// <param name="share">The share of the accent's lightness.</param>
    /// <param name="tint">How far it has turned red.</param>
    /// <returns>The frozen brush.</returns>
    private SolidColorBrush ShadeBrush(Color accent, Color pulse, double share, double tint)
    {
        var key = ((int)Math.Round(share * 100), (int)Math.Round(tint * 40));
        if (_brushes.TryGetValue(key, out var known))
        {
            return known;
        }

        byte Mix(byte from, byte to) =>
            (byte)Math.Round(Math.Clamp((from + ((to - from) * key.Item2 / 40.0)) * key.Item1 / 100.0, 0, 255));
        var brush = new SolidColorBrush(Color.FromRgb(Mix(accent.R, pulse.R), Mix(accent.G, pulse.G), Mix(accent.B, pulse.B)));
        brush.Freeze();
        _brushes[key] = brush;
        return brush;
    }

    /// <summary>Gives the theme's accent.</summary>
    /// <returns>The accent.</returns>
    private Color Accent() => TryFindResource("Color.Accent") is Color color ? color : FallbackAccent;

    /// <summary>Draws every particle at its strength, centred on its place.</summary>
    /// <param name="drawingContext">The drawing context.</param>
    private void DrawParticles(DrawingContext drawingContext)
    {
        foreach (var particle in _motion.Particles)
        {
            var alpha = particle.Alpha;
            if (alpha <= 0.01)
            {
                continue;
            }

            var glyph = Glyph(particle.Character, particle.IsBase);
            drawingContext.PushOpacity(alpha);
            drawingContext.DrawText(glyph, new Point(particle.X - (glyph.Width / 2), particle.Y - (glyph.Height / 2)));
            drawingContext.Pop();
        }
    }

    /// <summary>Gives a particle's character in the mono font and the accent, made once per screen scale.</summary>
    /// <param name="character">The character.</param>
    /// <param name="isBase">Whether it is a base, drawn a little bolder.</param>
    /// <returns>The character's text.</returns>
    private FormattedText Glyph(char character, bool isBase)
    {
        if (_glyphs.TryGetValue((character, isBase), out var known))
        {
            return known;
        }

        var family = TryFindResource("Font.Mono") as FontFamily ?? new FontFamily("Consolas");
        var weight = isBase ? FontWeights.SemiBold : FontWeights.Medium;
        var brush = new SolidColorBrush(Accent());
        brush.Freeze();
        var text = new FormattedText(
            character.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), 11, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        _glyphs[(character, isBase)] = text;
        return text;
    }

    /// <summary>Follows Windows' animations being turned on or off.</summary>
    /// <param name="sender">Not used.</param>
    /// <param name="e">The setting that changed.</param>
    private void OnSystemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
        {
            _motion.IsStill = !SystemParameters.ClientAreaAnimation;
            UpdateTimer();
            InvalidateVisual();
        }
    }

    /// <summary>Runs the timer while the figure is shown and moves, and rests it otherwise.</summary>
    private void UpdateTimer()
    {
        var run = IsLoaded && IsVisible && _motion.IsMoving;
        if (run && !_timer.IsEnabled)
        {
            _clock.Restart();
            _timer.Start();
        }
        else if (!run)
        {
            _timer.Stop();
        }
    }

    /// <summary>
    /// Gives the window's acceleration since the last step, smoothed: what a rope hanging in it would feel. It is in
    /// the window's own scale, so a drag across a screen of another scale feels the same.
    /// </summary>
    /// <param name="seconds">The time since the last step.</param>
    /// <returns>The acceleration, in pixels a second².</returns>
    private FigurePoint WindowPush(double seconds)
    {
        if (Window.GetWindow(this) is not { } window || seconds <= 0 || window.WindowState != WindowState.Normal)
        {
            _window = null;
            return default;
        }

        var place = new Point(window.Left, window.Top);
        if (_window is not { } was)
        {
            _window = (place, default, default);
            return default;
        }

        var speed = (place - was.Place) / seconds;
        var push = (was.Push * 0.6) + ((speed - was.Speed) / seconds * 0.4);
        _window = (place, speed, push);
        return new FigurePoint(Math.Clamp(push.X, -LargestPush, LargestPush), Math.Clamp(push.Y, -LargestPush, LargestPush));
    }

    /// <summary>Moves the figure on and draws again, unless the window is minimised.</summary>
    private void Advance()
    {
        var step = _clock.Elapsed < LongestStep ? _clock.Elapsed : LongestStep;
        _clock.Restart();
        if (Window.GetWindow(this) is not { WindowState: not WindowState.Minimized })
        {
            return;
        }

        _motion.Step(step.TotalSeconds, WindowPush(step.TotalSeconds));
        InvalidateVisual();
        Stepped?.Invoke(this, EventArgs.Empty);
        if (!_motion.IsMoving || _motion.IsUnlocked)
        {
            UpdateTimer();
        }
    }
}
