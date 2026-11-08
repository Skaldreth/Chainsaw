using System.Windows;
using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// A small node in the visual language for lists and headers (sketch EP1b): the sort's frame, round or square, whole or
/// dashed, in its colour, with its icon inside. The size is the control's; the icon's line stays 1.5 px on screen.
/// </summary>
public sealed class SortMark : FrameworkElement
{
    /// <summary>The sort to draw.</summary>
    public static readonly DependencyProperty LookProperty = DependencyProperty.Register(
        nameof(Look), typeof(SortLook), typeof(SortMark),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>How much of the frame the icon fills.</summary>
    private const double IconShare = 0.52;

    /// <summary>A square frame's corner, as a share of its size.</summary>
    private const double CornerShare = 0.27;

    /// <summary>A dashed frame, as the graph draws one.</summary>
    private static readonly DashStyle FrameDash = CreateDash();

    /// <summary>Gets or sets the sort.</summary>
    public SortLook? Look
    {
        get => (SortLook?)GetValue(LookProperty);
        set => SetValue(LookProperty, value);
    }

    /// <summary>Draws the frame and the icon.</summary>
    /// <param name="drawingContext">Where to draw.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (Look is not { } look)
        {
            return;
        }

        var size = Math.Min(ActualWidth, ActualHeight);
        var ink = GraphPalette.Brush(this, look.Ink);
        var radius = look.Shape == NodeShape.Round ? size / 2 : size * CornerShare;
        var frame = new Pen(ink, 1.5) { DashStyle = look.IsDashed ? FrameDash : DashStyles.Solid };
        drawingContext.DrawRoundedRectangle(null, frame, new Rect(0.75, 0.75, size - 1.5, size - 1.5), radius, radius);
        if (TryFindResource(look.Icon) is not Geometry glyph)
        {
            return;
        }

        var icon = size * IconShare;
        var scale = icon / GridIcon.GridSize;
        var pen = new Pen(ink, GridIcon.StrokeFor(icon))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        var offset = (size - icon) / 2;
        drawingContext.PushTransform(new MatrixTransform(scale, 0, 0, scale, offset, offset));
        drawingContext.DrawGeometry(null, pen, glyph);
        drawingContext.Pop();
    }

    /// <summary>Makes the frame's dashes, frozen so that they can be shared.</summary>
    /// <returns>The dashes.</returns>
    private static DashStyle CreateDash()
    {
        var dash = new DashStyle(GraphDashes.Frame, 0);
        dash.Freeze();
        return dash;
    }
}
