using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Chainsaw.App.Controls;

/// <summary>
/// An icon from <c>Themes/Icons.xaml</c>, drawn from its 24 x 24 grid at <see cref="Size"/>. The line is always 1.5 px
/// on screen whatever the size, because a fixed line would be scaled with the icon (the rule from Gawk). The icon draws
/// itself rather than through a Viewbox, so that every end of a line, scaled to the screen, is moved to the middle of
/// a device pixel: a 1.5 px line then covers one pixel fully instead of two pixels by half, which looked blurred.
/// </summary>
public sealed class GridIcon : FrameworkElement
{
    /// <summary>The width of the icons' grid.</summary>
    public const double GridSize = 24;

    /// <summary>The line's width on screen, in pixels.</summary>
    public const double ScreenStroke = 1.5;

    /// <summary>Identifies <see cref="Data"/>.</summary>
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(GridIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnShapeChanged));

    /// <summary>Identifies <see cref="Size"/>.</summary>
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(GridIcon),
        new FrameworkPropertyMetadata(
            20d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            OnSizeChanged));

    /// <summary>Identifies <see cref="Foreground"/>, inherited as text's colour is.</summary>
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(GridIcon),
        new FrameworkPropertyMetadata(
            Brushes.Gray, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies <see cref="Stroke"/>, the line's width on the grid.</summary>
    private static readonly DependencyPropertyKey StrokePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Stroke), typeof(double), typeof(GridIcon), new FrameworkPropertyMetadata(StrokeFor(20d)));

    /// <summary>Identifies <see cref="Stroke"/>.</summary>
    public static readonly DependencyProperty StrokeProperty = StrokePropertyKey.DependencyProperty;

    /// <summary>The shape as last drawn, scaled and snapped to the screen's pixels, kept until something changes.</summary>
    private Geometry? _drawn;

    /// <summary>Sets the default style from <c>Themes/Controls.xaml</c>, and never takes the mouse or the keyboard.</summary>
    static GridIcon()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(GridIcon), new FrameworkPropertyMetadata(typeof(GridIcon)));
        FocusableProperty.OverrideMetadata(typeof(GridIcon), new FrameworkPropertyMetadata(false));
        IsHitTestVisibleProperty.OverrideMetadata(typeof(GridIcon), new UIPropertyMetadata(false));
    }

    /// <summary>Gets or sets the icon's shape, a geometry on the 24 x 24 grid.</summary>
    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>Gets or sets the icon's size on screen in pixels: 20 in the rail, 16 in buttons, at least 12.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Gets or sets the line's colour.</summary>
    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Gets the line's width on the grid that gives <see cref="ScreenStroke"/> on screen.</summary>
    public double Stroke => (double)GetValue(StrokeProperty);

    /// <summary>Works out the line's width on the grid for a size on screen: 1.8 at 20 px, 2.25 at 16 px.</summary>
    /// <param name="size">The icon's size on screen.</param>
    /// <returns>The width to draw with on the 24 x 24 grid.</returns>
    public static double StrokeFor(double size) => ScreenStroke * GridSize / Math.Max(size, 1);

    /// <summary>
    /// Scales a shape from the 24 grid to a size on screen and moves every end of a line or curve to the middle of a
    /// device pixel; the curves' control points are scaled only, so that circles stay round.
    /// </summary>
    /// <param name="shape">The shape on the 24 grid.</param>
    /// <param name="size">The size on screen, in device-independent pixels.</param>
    /// <param name="dpiX">Device pixels per device-independent pixel across.</param>
    /// <param name="dpiY">Device pixels per device-independent pixel down.</param>
    /// <returns>The shape to draw, in device-independent pixels.</returns>
    public static Geometry Snap(Geometry shape, double size, double dpiX, double dpiY)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var scale = size / GridSize;
        Point Scaled(Point point) => new(point.X * scale, point.Y * scale);
        Point Snapped(Point point) => new(
            (Math.Floor(point.X * scale * dpiX) + 0.5) / dpiX,
            (Math.Floor(point.Y * scale * dpiY) + 0.5) / dpiY);

        var source = PathGeometry.CreateFromGeometry(shape);
        var result = new PathGeometry { FillRule = source.FillRule };
        foreach (var figure in source.Figures)
        {
            var copy = new PathFigure { StartPoint = Snapped(figure.StartPoint), IsClosed = figure.IsClosed };
            foreach (var segment in figure.Segments)
            {
                copy.Segments.Add(SnapSegment(segment, Scaled, Snapped, scale));
            }

            result.Figures.Add(copy);
        }

        result.Freeze();
        return result;
    }

    /// <summary>Measures the icon as its size.</summary>
    /// <param name="availableSize">The space.</param>
    /// <returns>The icon's square.</returns>
    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    /// <summary>Draws the shape with a 1.5 px line, centred in the space and snapped to the screen's pixels.</summary>
    /// <param name="drawingContext">Where to draw.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Data is not { } data)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        _drawn ??= Snap(data, Size, dpi.DpiScaleX, dpi.DpiScaleY);
        var pen = new Pen(Foreground, ScreenStroke)
        {
            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        var left = Math.Round((RenderSize.Width - Size) / 2 * dpi.DpiScaleX) / dpi.DpiScaleX;
        var top = Math.Round((RenderSize.Height - Size) / 2 * dpi.DpiScaleY) / dpi.DpiScaleY;
        drawingContext.PushTransform(new TranslateTransform(left, top));
        drawingContext.DrawGeometry(null, pen, _drawn);
        drawingContext.Pop();
    }

    /// <summary>Draws again for the new screen when the window moves to a screen with another scale.</summary>
    /// <param name="oldDpi">The former scale.</param>
    /// <param name="newDpi">The new scale.</param>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _drawn = null;
        InvalidateVisual();
    }

    /// <summary>Scales one segment, snapping the points where a line or curve ends.</summary>
    /// <param name="segment">The segment on the grid.</param>
    /// <param name="scaled">Scales a point.</param>
    /// <param name="snapped">Scales a point and moves it to the middle of a device pixel.</param>
    /// <param name="scale">The scale, for an arc's radii.</param>
    /// <returns>The segment on screen.</returns>
    private static PathSegment SnapSegment(
        PathSegment segment, Func<Point, Point> scaled, Func<Point, Point> snapped, double scale) => segment switch
    {
        LineSegment line => new LineSegment(snapped(line.Point), line.IsStroked),
        PolyLineSegment lines => new PolyLineSegment(lines.Points.Select(snapped), lines.IsStroked),
        BezierSegment curve => new BezierSegment(
            scaled(curve.Point1), scaled(curve.Point2), snapped(curve.Point3), curve.IsStroked),
        QuadraticBezierSegment curve => new QuadraticBezierSegment(
            scaled(curve.Point1), snapped(curve.Point2), curve.IsStroked),
        PolyBezierSegment curves => new PolyBezierSegment(
            curves.Points.Select((point, index) => index % 3 == 2 ? snapped(point) : scaled(point)), curves.IsStroked),
        PolyQuadraticBezierSegment curves => new PolyQuadraticBezierSegment(
            curves.Points.Select((point, index) => index % 2 == 1 ? snapped(point) : scaled(point)), curves.IsStroked),
        ArcSegment arc => new ArcSegment(
            snapped(arc.Point), new Size(arc.Size.Width * scale, arc.Size.Height * scale), arc.RotationAngle,
            arc.IsLargeArc, arc.SweepDirection, arc.IsStroked),
        _ => segment.Clone(),
    };

    /// <summary>Forgets the drawn shape when the shape changes.</summary>
    /// <param name="d">The icon.</param>
    /// <param name="e">The change.</param>
    private static void OnShapeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((GridIcon)d)._drawn = null;

    /// <summary>Keeps the line's width in step with the size, and forgets the drawn shape.</summary>
    /// <param name="d">The icon.</param>
    /// <param name="e">The new size.</param>
    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((GridIcon)d)._drawn = null;
        d.SetValue(StrokePropertyKey, StrokeFor((double)e.NewValue));
    }
}
