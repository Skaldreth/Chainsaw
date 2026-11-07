using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Chainsaw.App.Figure;

namespace Chainsaw.App.Controls;

/// <summary>
/// The figure behind the lock screen and the first-run setup (sketch ID8c): the chain in the accent that turns into a
/// double helix, drawn as shapes, with the particles drifting off the helix. The shapes are laid out by
/// <see cref="DnaFigure"/> once per size and kept as one frozen drawing; only the particles move, about thirty times a
/// second, while the figure is shown and Windows has its animations on. Otherwise they stand still and the timer rests.
/// </summary>
public sealed class ChainFigure : FrameworkElement
{
    /// <summary>About thirty frames a second, as the sketch.</summary>
    private static readonly TimeSpan FrameTime = TimeSpan.FromMilliseconds(33);

    /// <summary>The longest step the particles take at once, so that a pause never makes them jump.</summary>
    private static readonly TimeSpan LongestStep = TimeSpan.FromMilliseconds(100);

    /// <summary>The accent, should the theme not give it.</summary>
    private static readonly Color FallbackAccent = Color.FromRgb(0xE0, 0x70, 0xB0);

    /// <summary>One joint, as a frozen shape in the logo's units.</summary>
    private static readonly Geometry Joint = Frozen(Geometry.Parse(ChainLink.Joint));

    /// <summary>One link's bars below its joint, as a frozen shape in the logo's units.</summary>
    private static readonly Geometry Bars = Frozen(Geometry.Parse(ChainLink.Bars));

    /// <summary>The particles, moved by the timer.</summary>
    private readonly DnaParticles _particles = new(new Random());

    /// <summary>Moves the particles while the figure is shown.</summary>
    private readonly DispatcherTimer _timer;

    /// <summary>Measures the time between two steps.</summary>
    private readonly Stopwatch _clock = new();

    /// <summary>The particles' characters, made once per screen scale.</summary>
    private readonly Dictionary<(char Character, bool IsBase), FormattedText> _glyphs = [];

    /// <summary>The figure for the current size, or <see langword="null"/> before the first frame.</summary>
    private DnaFigure? _figure;

    /// <summary>The figure's shapes, drawn under the particles.</summary>
    private Drawing? _drawing;

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
            UpdateTimer();
        };
        Unloaded += (_, _) =>
        {
            SystemParameters.StaticPropertyChanged -= OnSystemChanged;
            UpdateTimer();
        };
        IsVisibleChanged += (_, _) => UpdateTimer();
    }

    /// <summary>Draws the figure's shapes and the particles over them.</summary>
    /// <param name="drawingContext">The drawing context.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (ActualWidth < 1 || ActualHeight < 1)
        {
            return;
        }

        if (_figure is null || _figure.Width != ActualWidth || _figure.Height != ActualHeight)
        {
            _figure = new DnaFigure(ActualWidth, ActualHeight);
            _drawing = Compose(_figure, Accent());
            _particles.Fill(_figure);
        }

        drawingContext.DrawDrawing(_drawing);
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

    /// <summary>Gives a colour darkened by a factor.</summary>
    /// <param name="color">The colour.</param>
    /// <param name="factor">The factor, below 1.</param>
    /// <returns>The darker colour.</returns>
    private static Color Darker(Color color, double factor) => Color.FromRgb(
        (byte)Math.Round(color.R * factor), (byte)Math.Round(color.G * factor), (byte)Math.Round(color.B * factor));

    /// <summary>Gives a frozen brush.</summary>
    /// <param name="color">Its colour.</param>
    /// <returns>The brush.</returns>
    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Puts the figure's shapes together, back to front as the sketch: the chain, the strands behind (darker), the
    /// rungs (light from strand one, darker from strand two) and the strands in front.
    /// </summary>
    /// <param name="figure">The figure.</param>
    /// <param name="accent">The accent.</param>
    /// <returns>The frozen drawing.</returns>
    private static Drawing Compose(DnaFigure figure, Color accent)
    {
        var light = Brush(accent);
        var group = new DrawingGroup();
        foreach (var joint in figure.Joints)
        {
            var place = new Matrix();
            place.Scale(joint.Scale, joint.Scale);
            place.Rotate(joint.Angle);
            place.Translate(joint.X, joint.Y);
            var link = new DrawingGroup { Transform = new MatrixTransform(place) };
            link.Children.Add(new GeometryDrawing(light, null, Joint));
            link.Children.Add(new GeometryDrawing(light, null, Bars));
            group.Children.Add(link);
        }

        AddStrands(group, figure.Back, Brush(Darker(accent, 0.55)));
        var rungs = Pen(light, DnaFigure.RungThickness, PenLineCap.Round);
        var darkRungs = Pen(Brush(Darker(accent, 0.62)), DnaFigure.RungThickness, PenLineCap.Round);
        foreach (var rung in figure.Rungs)
        {
            group.Children.Add(new GeometryDrawing(null, rungs, Line(rung.First, rung.Middle)));
            group.Children.Add(new GeometryDrawing(null, darkRungs, Line(rung.Middle, rung.Second)));
        }

        AddStrands(group, figure.Front, light);
        group.Freeze();
        return group;
    }

    /// <summary>Adds the stretches of strand in one colour.</summary>
    /// <param name="group">The drawing.</param>
    /// <param name="stretches">The stretches.</param>
    /// <param name="brush">Their colour.</param>
    private static void AddStrands(DrawingGroup group, IEnumerable<IReadOnlyList<FigurePoint>> stretches, Brush brush)
    {
        var pen = Pen(brush, DnaFigure.StrandThickness, PenLineCap.Flat);
        foreach (var stretch in stretches)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(stretch[0].X, stretch[0].Y), false, false);
                context.PolyLineTo(stretch.Skip(1).Select(p => new Point(p.X, p.Y)).ToList(), true, true);
            }

            geometry.Freeze();
            group.Children.Add(new GeometryDrawing(null, pen, geometry));
        }
    }

    /// <summary>Gives a frozen pen with round joins.</summary>
    /// <param name="brush">Its colour.</param>
    /// <param name="thickness">Its thickness.</param>
    /// <param name="cap">Its ends.</param>
    /// <returns>The pen.</returns>
    private static Pen Pen(Brush brush, double thickness, PenLineCap cap)
    {
        var pen = new Pen(brush, thickness) { LineJoin = PenLineJoin.Round, StartLineCap = cap, EndLineCap = cap };
        pen.Freeze();
        return pen;
    }

    /// <summary>Gives a frozen straight line.</summary>
    /// <param name="from">Its start.</param>
    /// <param name="to">Its end.</param>
    /// <returns>The line.</returns>
    private static Geometry Line(FigurePoint from, FigurePoint to) =>
        Frozen(new LineGeometry(new Point(from.X, from.Y), new Point(to.X, to.Y)));

    /// <summary>Gives the theme's accent.</summary>
    /// <returns>The accent.</returns>
    private Color Accent() => TryFindResource("Color.Accent") is Color color ? color : FallbackAccent;

    /// <summary>Draws every particle at its strength, centred on its place.</summary>
    /// <param name="drawingContext">The drawing context.</param>
    private void DrawParticles(DrawingContext drawingContext)
    {
        foreach (var particle in _particles.Items)
        {
            var alpha = particle.Alpha;
            if (alpha <= 0.01)
            {
                continue;
            }

            var glyph = Glyph(particle.Character, particle.IsBase);
            drawingContext.PushOpacity(alpha);
            drawingContext.DrawText(glyph, new Point(particle.X - glyph.Width / 2, particle.Y - glyph.Height / 2));
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
        var text = new FormattedText(
            character.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), 11, Brush(Accent()),
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
            UpdateTimer();
        }
    }

    /// <summary>Runs the timer while the figure is shown and Windows animates, and rests it otherwise.</summary>
    private void UpdateTimer()
    {
        var run = IsLoaded && IsVisible && SystemParameters.ClientAreaAnimation;
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

    /// <summary>Moves the particles on and draws again, unless the window is minimised.</summary>
    private void Advance()
    {
        var step = _clock.Elapsed < LongestStep ? _clock.Elapsed : LongestStep;
        _clock.Restart();
        if (_figure is null || Window.GetWindow(this) is not { WindowState: not WindowState.Minimized })
        {
            return;
        }

        _particles.Step(_figure, step.TotalSeconds);
        InvalidateVisual();
    }
}
