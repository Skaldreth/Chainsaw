using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Chainsaw.App.Controls;

/// <summary>
/// What the drawn charts (Tidslinje, Flöde, Exponering, a suggestion's flow) share: the theme's brushes, text in the
/// page's font with a halo of the background, and icons of the 24 grid with the line kept 1.5 px on screen, as
/// <see cref="GridIcon"/>.
/// </summary>
/// <param name="owner">The control that draws, whose resources and font are used.</param>
internal sealed class ChartDrawing(FrameworkElement owner)
{
    /// <summary>How wide the halo behind a text is.</summary>
    private const double HaloWidth = 4;

    /// <summary>Gives a brush of the theme by its resource key.</summary>
    /// <param name="key">The key, for example <c>Brush.Accent</c>.</param>
    /// <returns>The brush.</returns>
    public Brush Brush(string key) => GraphPalette.Find(owner, key);

    /// <summary>Makes a pen.</summary>
    /// <param name="key">The brush's key.</param>
    /// <param name="thickness">The thickness.</param>
    /// <param name="dashes">The dashes, in multiples of the thickness, or <see langword="null"/> for a whole line.</param>
    /// <returns>The pen, with round ends.</returns>
    public Pen Pen(string key, double thickness, double[]? dashes = null)
    {
        var pen = new Pen(Brush(key), thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            DashCap = PenLineCap.Round,
        };
        if (dashes is not null)
        {
            pen.DashStyle = new DashStyle(dashes, 0);
        }

        pen.Freeze();
        return pen;
    }

    /// <summary>Lays out a text in the page's font.</summary>
    /// <param name="text">The text.</param>
    /// <param name="size">The size.</param>
    /// <param name="ink">The ink.</param>
    /// <param name="weight">The weight; normal by default.</param>
    /// <returns>The text.</returns>
    public FormattedText Text(string text, double size, Brush ink, FontWeight? weight = null) => new(
        text,
        CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,
        new Typeface(
            TextElement.GetFontFamily(owner), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
        size,
        ink,
        VisualTreeHelper.GetDpi(owner).PixelsPerDip);

    /// <summary>Draws a text with a halo of the background, so that lines under it never cross the letters.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="formatted">The text.</param>
    /// <param name="origin">Its top-left corner.</param>
    public void Halo(DrawingContext dc, FormattedText formatted, Point origin)
    {
        var outline = formatted.BuildGeometry(origin);
        var pen = new Pen(Brush("Brush.Background"), HaloWidth) { LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(null, pen, outline);
        dc.DrawText(formatted, origin);
    }

    /// <summary>Draws an icon of the 24 grid at a size, with the line kept 1.5 px on screen.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="key">The icon's resource key.</param>
    /// <param name="ink">Its colour.</param>
    /// <param name="corner">Its top-left corner.</param>
    /// <param name="size">Its size.</param>
    public void Glyph(DrawingContext dc, string key, Brush ink, Point corner, double size)
    {
        if (owner.TryFindResource(key) is not Geometry glyph)
        {
            return;
        }

        var scale = size / GridIcon.GridSize;
        var pen = new Pen(ink, GridIcon.StrokeFor(size))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, corner.X, corner.Y));
        dc.DrawGeometry(null, pen, glyph);
        dc.Pop();
    }

    /// <summary>
    /// Draws a small node in the visual language: its frame in the sort's shape and colour with its icon.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="look">The sort's look.</param>
    /// <param name="centre">Its centre.</param>
    /// <param name="size">Its size.</param>
    public void Mark(DrawingContext dc, Graph.SortLook look, Point centre, double size)
    {
        ArgumentNullException.ThrowIfNull(look);
        var ink = GraphPalette.Brush(owner, look.Ink);
        var half = size / 2;
        var radius = look.Shape == Graph.NodeShape.Round ? half : size * 0.25;
        var frame = new Pen(ink, 1.3);
        if (look.IsDashed)
        {
            frame.DashStyle = new DashStyle([2.5, 2], 0);
        }

        dc.DrawRoundedRectangle(
            Brush("Brush.Surface"), frame, new Rect(centre.X - half, centre.Y - half, size, size), radius, radius);
        var icon = size * 0.56;
        Glyph(dc, look.Icon, ink, new Point(centre.X - (icon / 2), centre.Y - (icon / 2)), icon);
    }
}
