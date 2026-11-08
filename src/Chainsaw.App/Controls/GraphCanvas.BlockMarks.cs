using System.Windows;
using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// The block marks (sketch GR9a): a round mark in the corner of a line's amount when a transaction on it left the block
/// the case saw it in, amber when it moved or waits in the mempool, red when the source no longer knows it, with its
/// amount struck through. Hovering or a click shows the box with before and now, height and hash.
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>The box's width on screen.</summary>
    private const double MarkBoxWidth = 340;

    /// <summary>A row's height in the box.</summary>
    private const double MarkRowHeight = 34;

    /// <summary>The space inside the box, left and right.</summary>
    private const double MarkBoxPadding = 14;

    /// <summary>Where the marks stood when the graph was last drawn, by line, in surface units.</summary>
    private Dictionary<string, Point> _markCentres = new(StringComparer.Ordinal);

    /// <summary>Where the marks stand in the drawing under way.</summary>
    private Dictionary<string, Point> _nextMarkCentres = new(StringComparer.Ordinal);

    /// <summary>The mark under the pointer, or <see langword="null"/>.</summary>
    private string? _hoveredMark;

    /// <summary>Where the open box's <em>?</em> stood on screen, so that a click on it opens the Guide.</summary>
    private Rect? _markGuide;

    /// <summary>
    /// Draws a line's block mark in the corner of its amount, and strikes the amount through when the transaction is
    /// gone; called where the amount is drawn, in surface units.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="edgeKey">The line.</param>
    /// <param name="box">The amount's box.</param>
    private void DrawBlockMark(DrawingContext dc, string edgeKey, Rect box)
    {
        if (!_model!.BlockMarksByLine.TryGetValue(edgeKey, out var mark))
        {
            return;
        }

        if (mark.IsGone)
        {
            var y = box.Y + 1 + ((box.Height - 2) / 2);
            dc.DrawLine(new Pen(GraphPalette.Find(this, "Brush.TextDim"), 1), new Point(box.X + 5, y),
                new Point(box.Right - 5, y));
        }

        var centre = new Point(box.Right, box.Top);
        _nextMarkCentres[edgeKey] = centre;
        DrawMarkDisc(dc, mark, centre, MarkSize);
    }

    /// <summary>Draws a mark's disc with its icon.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="mark">The mark.</param>
    /// <param name="centre">Its centre.</param>
    /// <param name="size">Its diameter.</param>
    private void DrawMarkDisc(DrawingContext dc, BlockMark mark, Point centre, double size)
    {
        var (disc, ink) = mark.IsGone ? ("Brush.Error", "Brush.Mark.OnSanctioned") : ("Brush.Pending", "Brush.Mark.OnPending");
        dc.DrawEllipse(GraphPalette.Find(this, disc), new Pen(GraphPalette.Find(this, "Brush.Background"), 2), centre,
            size / 2, size / 2);
        var icon = size * MarkIconSize / MarkSize;
        DrawGlyph(dc, mark.Icon, GraphPalette.Find(this, ink), new Point(centre.X - (icon / 2), centre.Y - (icon / 2)),
            icon);
    }

    /// <summary>
    /// Draws the open or hovered mark's box beside it, in screen units, over everything in the graph: the title, before
    /// (struck through) and now with height, hash and when it was seen, and what it means.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    private void DrawBlockMarkBox(DrawingContext dc)
    {
        (_markCentres, _nextMarkCentres) = (_nextMarkCentres, _markCentres);
        _nextMarkCentres.Clear();
        _markGuide = null;
        var model = _model!;
        var key = _hoveredMark ?? model.OpenBlockMark;
        if (key is null || !model.BlockMarksByLine.TryGetValue(key, out var mark)
            || !_markCentres.TryGetValue(key, out var centre))
        {
            return;
        }

        var at = _viewport.ToScreen(new GraphPoint(centre.X, centre.Y));
        var foot = Wrapped(mark.Foot, MarkBoxWidth - (2 * MarkBoxPadding));
        var height = 40 + (mark.Rows.Count * MarkRowHeight) + foot.Height + 18;
        var left = Math.Min(at.X + 16, ActualWidth - MarkBoxWidth - 8);
        var top = Math.Clamp(at.Y - 12, 8, Math.Max(8, ActualHeight - height - 8));
        var frame = new Rect(left, top, MarkBoxWidth, height);
        DrawFloatingBox(dc, frame);
        dc.PushClip(new RectangleGeometry(frame, CardRadius, CardRadius));
        DrawMarkBoxHead(dc, mark, frame);
        var rowTop = frame.Y + 40;
        foreach (var row in mark.Rows)
        {
            DrawMarkRow(dc, row, frame, rowTop);
            rowTop += MarkRowHeight;
        }

        var footBox = new Rect(frame.X, rowTop, frame.Width, frame.Bottom - rowTop);
        dc.DrawRectangle(GraphPalette.Find(this, "Brush.Surface"), null, footBox);
        dc.DrawLine(new Pen(GraphPalette.Find(this, "Brush.Card.Rule"), 1), footBox.TopLeft, footBox.TopRight);
        dc.DrawText(foot, new Point(frame.X + MarkBoxPadding, rowTop + 9));
        dc.Pop();
        dc.DrawRoundedRectangle(null, new Pen(GraphPalette.Find(this, "Brush.BorderStrong"), 1.5), frame, CardRadius,
            CardRadius);
    }

    /// <summary>Draws a floating box's shadow and ground.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="frame">The box.</param>
    private void DrawFloatingBox(DrawingContext dc, Rect frame)
    {
        var shadow = frame;
        shadow.Offset(0, 8);
        dc.PushOpacity(0.35);
        dc.DrawRoundedRectangle(Brushes.Black, null, shadow, CardRadius, CardRadius);
        dc.Pop();
        dc.DrawRoundedRectangle(GraphPalette.Find(this, "Brush.Floating"), null, frame, CardRadius, CardRadius);
    }

    /// <summary>Draws the box's head: the mark, its title and the <em>?</em> that opens the Guide.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="mark">The mark.</param>
    /// <param name="frame">The box.</param>
    private void DrawMarkBoxHead(DrawingContext dc, BlockMark mark, Rect frame)
    {
        var middle = frame.Y + 22;
        DrawMarkDisc(dc, mark, new Point(frame.X + MarkBoxPadding + 9, middle), MarkSize);
        var title = CachedText(mark.Title, 13.5, GraphPalette.Find(this, "Brush.Text"), FontWeights.SemiBold);
        dc.DrawText(title, new Point(frame.X + MarkBoxPadding + 27, middle - (title.Height / 2)));
        var guide = new Rect(frame.Right - 10 - 24, middle - 12, 24, 24);
        dc.DrawEllipse(null, new Pen(GraphPalette.Find(this, "Brush.BorderStrong"), 1.5),
            new Point(guide.X + 12, guide.Y + 12), 11.25, 11.25);
        DrawScreenGlyph(dc, "Icon.Question", GraphPalette.Find(this, "Brush.TextMuted"), new Point(guide.X + 6, guide.Y + 6), 12);
        _markGuide = guide;
    }

    /// <summary>Draws one row of the box: the block, the height, the hash and when it was seen.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="row">The row.</param>
    /// <param name="frame">The box.</param>
    /// <param name="top">The row's top.</param>
    private void DrawMarkRow(DrawingContext dc, BlockMarkRow row, Rect frame, double top)
    {
        dc.DrawLine(new Pen(GraphPalette.Find(this, "Brush.Card.Rule"), 1), new Point(frame.X, top),
            new Point(frame.Right, top));
        var middle = top + (MarkRowHeight / 2);
        var ink = GraphPalette.Find(this, row.IsBefore ? "Brush.TextDim" : "Brush.Text");
        var left = frame.X + MarkBoxPadding;
        DrawScreenGlyph(dc, "Icon.Block", ink, new Point(left, middle - 6.5), 13);
        var height = Struck(Text(row.Height, 12.5, ink, FontWeights.Normal), row.IsBefore);
        dc.DrawText(height, new Point(left + 23, middle - (height.Height / 2)));
        var hashInk = GraphPalette.Find(this, row.IsBefore ? "Brush.TextDim" : "Brush.TextMuted");
        var hash = Struck(MonoText(row.Hash, 11.5, hashInk), row.IsBefore);
        dc.DrawText(hash, new Point(left + 85, middle - (hash.Height / 2)));
        var whenLeft = left + 85 + hash.Width + 10;
        var room = Math.Max(1, frame.Right - MarkBoxPadding - whenLeft);
        var when = CachedFitted(row.WhenAndWhere, 11, GraphPalette.Find(this, "Brush.TextDim"), FontWeights.Normal, room);
        dc.DrawText(when, new Point(frame.Right - MarkBoxPadding - when.Width, middle - (when.Height / 2)));
    }

    /// <summary>Takes a press on a block mark (opens or closes its box) or on the open box's <em>?</em>.</summary>
    /// <param name="screen">The press on screen.</param>
    /// <param name="surface">The press on the surface.</param>
    /// <returns><see langword="true"/> when a mark took the press.</returns>
    private bool PressBlockMark(GraphPoint screen, GraphPoint surface)
    {
        if (_markGuide is { } guide && guide.Contains(new Point(screen.X, screen.Y)))
        {
            _model!.OpenBlockMarkGuide();
            return true;
        }

        if (MarkAt(surface) is not { } key)
        {
            return false;
        }

        _model!.ToggleBlockMark(key);
        return true;
    }

    /// <summary>Follows the pointer over the marks, so that the hovered mark's box opens.</summary>
    /// <param name="surface">The pointer on the surface.</param>
    private void HoverBlockMark(GraphPoint surface)
    {
        var mark = MarkAt(surface);
        if (mark != _hoveredMark)
        {
            _hoveredMark = mark;
            InvalidateVisual();
        }
    }

    /// <summary>Gives what a mark says on hovering, or <see langword="null"/> when the pointer is not on one.</summary>
    /// <param name="surface">The pointer on the surface.</param>
    /// <returns>The tip.</returns>
    private string? BlockMarkTipAt(GraphPoint surface) =>
        MarkAt(surface) is { } key && _model!.BlockMarksByLine.TryGetValue(key, out var mark) ? mark.Tip : null;

    /// <summary>Gives the mark under a point.</summary>
    /// <param name="surface">The point on the surface.</param>
    /// <returns>The line's key, or <see langword="null"/>.</returns>
    private string? MarkAt(GraphPoint surface)
    {
        var reach = (MarkSize / 2) + (2 / _viewport.Zoom);
        foreach (var (key, centre) in _markCentres)
        {
            if (Math.Abs(centre.X - surface.X) <= reach && Math.Abs(centre.Y - surface.Y) <= reach)
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>Strikes a text through when it is the earlier answer.</summary>
    /// <param name="text">The text.</param>
    /// <param name="struck">Whether to strike it.</param>
    /// <returns>The text.</returns>
    private static FormattedText Struck(FormattedText text, bool struck)
    {
        if (struck)
        {
            text.SetTextDecorations(TextDecorations.Strikethrough);
        }

        return text;
    }

    /// <summary>Writes a hash in the mono font, as the inspector writes hashes.</summary>
    /// <param name="text">The hash.</param>
    /// <param name="size">The size.</param>
    /// <param name="ink">The colour.</param>
    /// <returns>The text.</returns>
    private FormattedText MonoText(string text, double size, Brush ink)
    {
        var family = TryFindResource("Font.Mono") as FontFamily ?? new FontFamily("Consolas");
        return new FormattedText(
            text,
            System.Globalization.CultureInfo.GetCultureInfo("sv-SE"),
            FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size,
            ink,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    /// <summary>Writes a text that wraps within a width, in the dim colour.</summary>
    /// <param name="text">The text.</param>
    /// <param name="width">The width.</param>
    /// <returns>The text.</returns>
    private FormattedText Wrapped(string text, double width)
    {
        var wrapped = Text(text, 11.5, GraphPalette.Find(this, "Brush.TextDim"), FontWeights.Normal);
        wrapped.MaxTextWidth = width;
        wrapped.LineHeight = 16.5;
        return wrapped;
    }

    /// <summary>Draws an icon in screen units, where the zoom does not apply, with the line 1.5 px.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="key">The icon's resource key.</param>
    /// <param name="ink">Its colour.</param>
    /// <param name="corner">Its top-left corner.</param>
    /// <param name="size">Its size.</param>
    private void DrawScreenGlyph(DrawingContext dc, string key, Brush ink, Point corner, double size)
    {
        if (TryFindResource(key) is not Geometry glyph)
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
}
