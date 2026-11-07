using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chainsaw.App.Controls;

/// <summary>
/// Lays out the empty graph's hub (sketch GR6c): the first child in the middle and the others evenly on an ellipse
/// around it, the first of them straight above, with a faint dotted line from the middle to each. The ellipse shrinks
/// with the space, so that a small window still shows every node.
/// </summary>
public sealed class RingPanel : Panel
{
    /// <summary>Identifies <see cref="RadiusX"/>.</summary>
    public static readonly DependencyProperty RadiusXProperty = DependencyProperty.Register(
        nameof(RadiusX), typeof(double), typeof(RingPanel),
        new FrameworkPropertyMetadata(360d, FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>Identifies <see cref="RadiusY"/>.</summary>
    public static readonly DependencyProperty RadiusYProperty = DependencyProperty.Register(
        nameof(RadiusY), typeof(double), typeof(RingPanel),
        new FrameworkPropertyMetadata(250d, FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>Identifies <see cref="LineBrush"/>.</summary>
    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(RingPanel),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The space kept between a node on the ring and the panel's edge.</summary>
    private const double EdgeSpace = 12;

    /// <summary>Where each child's centre was put at the last arrange, for the lines.</summary>
    private readonly List<Point> _centres = [];

    /// <summary>Gets or sets the ellipse's largest half width.</summary>
    public double RadiusX
    {
        get => (double)GetValue(RadiusXProperty);
        set => SetValue(RadiusXProperty, value);
    }

    /// <summary>Gets or sets the ellipse's largest half height.</summary>
    public double RadiusY
    {
        get => (double)GetValue(RadiusYProperty);
        set => SetValue(RadiusYProperty, value);
    }

    /// <summary>Gets or sets the colour of the dotted lines from the middle.</summary>
    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    /// <summary>Measures every child at its own size and takes all the space given.</summary>
    /// <param name="availableSize">The space.</param>
    /// <returns>The space, or the children's when it is endless.</returns>
    protected override Size MeasureOverride(Size availableSize)
    {
        var largest = new Size();
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            largest.Width = Math.Max(largest.Width, child.DesiredSize.Width);
            largest.Height = Math.Max(largest.Height, child.DesiredSize.Height);
        }

        return new Size(
            double.IsInfinity(availableSize.Width) ? largest.Width : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? largest.Height : availableSize.Height);
    }

    /// <summary>Puts the first child in the middle and the others on the ellipse.</summary>
    /// <param name="finalSize">The space.</param>
    /// <returns>The space.</returns>
    protected override Size ArrangeOverride(Size finalSize)
    {
        _centres.Clear();
        var middle = new Point(finalSize.Width / 2, finalSize.Height / 2);
        var ring = InternalChildren.Count - 1;
        var widest = 0d;
        var highest = 0d;
        for (var index = 1; index < InternalChildren.Count; index++)
        {
            widest = Math.Max(widest, InternalChildren[index].DesiredSize.Width);
            highest = Math.Max(highest, InternalChildren[index].DesiredSize.Height);
        }

        var radiusX = Math.Max(0, Math.Min(RadiusX, middle.X - (widest / 2) - EdgeSpace));
        var radiusY = Math.Max(0, Math.Min(RadiusY, middle.Y - (highest / 2) - EdgeSpace));
        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var child = InternalChildren[index];
            var centre = middle;
            if (index > 0)
            {
                var angle = (-Math.PI / 2) + ((index - 1) * 2 * Math.PI / ring);
                centre = new Point(middle.X + (Math.Cos(angle) * radiusX), middle.Y + (Math.Sin(angle) * radiusY));
            }

            _centres.Add(centre);
            var size = child.DesiredSize;
            child.Arrange(new Rect(new Point(centre.X - (size.Width / 2), centre.Y - (size.Height / 2)), size));
        }

        InvalidateVisual();
        return finalSize;
    }

    /// <summary>Draws a faint dotted line from the middle to each node on the ring, under the nodes.</summary>
    /// <param name="drawingContext">Where to draw.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (_centres.Count < 2)
        {
            return;
        }

        var pen = new Pen(LineBrush, 1.5) { DashStyle = new DashStyle([0, 4], 0), DashCap = PenLineCap.Round };
        pen.Freeze();
        for (var index = 1; index < _centres.Count; index++)
        {
            drawingContext.DrawLine(pen, _centres[0], _centres[index]);
        }
    }
}
