using System.Windows;
using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// The choice per swap (sketch SW1a): the pill <c>₮ → Ξ</c> on the line unfolds in place, on a click, to
/// <em>Följer</em> and <em>Följer inte</em> and a <em>?</em> to the Guide; hovering it shows the arithmetic in a box
/// (in, share, out, what follows, with the transaction and the analysis). When the user chose that the value does not
/// follow, the arrow in the pill is a stop, a person mark sits in its corner, and a short stroke crosses the line
/// after it (drawn with the pill, in <see cref="DrawSwap"/>).
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>How far after the pill the stroke that ends the trail crosses the line, in surface units.</summary>
    private const double SwapStrokeGap = 6;

    /// <summary>The space inside the unfolded choice, around and between its chips.</summary>
    private const double SwapChoicePadding = 5;

    /// <summary>The width of the swap box on screen.</summary>
    private const double SwapBoxWidth = 392;

    /// <summary>The height of the steps' row in the swap box.</summary>
    private const double SwapStepsHeight = 58;

    /// <summary>Where the swap pills stood when the graph was last drawn, by line, in surface units.</summary>
    private Dictionary<string, Rect> _swapPills = new(StringComparer.Ordinal);

    /// <summary>Where the swap pills stand in the drawing under way.</summary>
    private Dictionary<string, Rect> _nextSwapPills = new(StringComparer.Ordinal);

    /// <summary>The unfolded choice's parts, in surface units, as last drawn; <see langword="null"/> when none.</summary>
    private SwapChoiceRects? _swapChoice;

    /// <summary>The pill under the pointer, or <see langword="null"/>.</summary>
    private string? _hoveredSwap;

    /// <summary>
    /// Gives the pill's frame: dashed in the risk's colour when the swap's price is absurd and the value does not
    /// follow, dashed and quiet when the price could not be tested, otherwise solid ("streckat betyder osäkert").
    /// </summary>
    /// <param name="edge">The line.</param>
    /// <param name="open">Whether the pill is unfolded.</param>
    /// <returns>The pen.</returns>
    private Pen SwapFrame(EdgeView edge, bool open)
    {
        var ink = open ? "Brush.BorderHover" : "Brush.BorderStrong";
        if (edge.IsSwapPriceAbsurd)
        {
            ink = "Brush.Kind.Risk";
        }

        var pen = new Pen(GraphPalette.Find(this, ink), 1);
        if (edge.IsSwapPriceAbsurd || edge.IsSwapPriceUntested)
        {
            pen.DashStyle = new DashStyle([3, 2.5], 0);
        }

        return pen;
    }

    /// <summary>
    /// Gives the pill's arrow: a stop when the value does not follow (the user's choice, or an absurd price), the
    /// accent's arrow when it does.
    /// </summary>
    /// <param name="edge">The line.</param>
    /// <returns>The icon's key and its colour.</returns>
    private (string Glyph, Brush Ink) SwapArrow(EdgeView edge)
    {
        if (edge.IsSwapPriceAbsurd)
        {
            return ("Icon.Swap.Stop", GraphPalette.Find(this, "Brush.Kind.Risk"));
        }

        return edge.IsSwapStopped
            ? ("Icon.Swap.Stop", GraphPalette.Find(this, "Brush.TextDim"))
            : ("Icon.ArrowRight", GraphPalette.Find(this, "Brush.AccentHover"));
    }

    /// <summary>
    /// Draws the person mark in the pill's corner: the user's choice, not the default (sketch SW1a, as the own areas
    /// carry one).
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="centre">The mark's centre.</param>
    private void DrawSwapPersonMark(DrawingContext dc, Point centre)
    {
        const double Size = 13;
        dc.DrawEllipse(
            GraphPalette.Find(this, "Brush.SurfaceSelected"),
            new Pen(GraphPalette.Find(this, "Brush.Background"), 1.5),
            centre,
            Size / 2,
            Size / 2);
        DrawGlyph(dc, "Icon.Kind.Person", GraphPalette.Find(this, "Brush.Text"),
            new Point(centre.X - 4.5, centre.Y - 4.5), 9);
    }

    /// <summary>
    /// Draws the unfolded choice after the pill: <em>Följer</em> and <em>Följer inte</em> as small chips, the one in
    /// force lit, and the <em>?</em> that opens the Guide; the amounts keep clear of it.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="edge">The line.</param>
    /// <param name="pill">The pill.</param>
    /// <param name="taken">Where amounts and pills stand.</param>
    private void DrawSwapChoice(DrawingContext dc, EdgeView edge, Rect pill, RoomGrid taken)
    {
        var height = pill.Height;
        var follows = Chip(SwapPill.FollowsText, "Icon.Swap.Follow");
        var stops = Chip(SwapPill.StopsText, "Icon.Swap.Stop");
        var width = (4 * SwapChoicePadding) + follows.Width + stops.Width + height;
        var frame = new Rect(pill.Right + SwapStrokeGap + 4, pill.Y, width, height);
        taken.Add(new GraphRect(frame.X, frame.Y, frame.Width, frame.Height));
        dc.DrawRoundedRectangle(
            GraphPalette.Find(this, "Brush.Floating"),
            new Pen(GraphPalette.Find(this, "Brush.BorderHover"), 1),
            frame,
            height / 2,
            height / 2);
        var followRect = new Rect(frame.X + SwapChoicePadding, frame.Y + 3, follows.Width, height - 6);
        var stopRect = new Rect(followRect.Right + SwapChoicePadding, frame.Y + 3, stops.Width, height - 6);
        DrawChip(dc, follows, followRect, !edge.IsSwapStopped);
        DrawChip(dc, stops, stopRect, edge.IsSwapStopped);
        var guide = new Rect(stopRect.Right + SwapChoicePadding, frame.Y + 3, height - 6, height - 6);
        dc.DrawEllipse(null, new Pen(GraphPalette.Find(this, "Brush.BorderStrong"), 1),
            new Point(guide.X + (guide.Width / 2), guide.Y + (guide.Height / 2)), guide.Width / 2, guide.Height / 2);
        DrawGlyph(dc, "Icon.Question", GraphPalette.Find(this, "Brush.TextMuted"),
            new Point(guide.X + (guide.Width / 2) - 5.5, guide.Y + (guide.Height / 2) - 5.5), 11);
        _swapChoice = new SwapChoiceRects(edge.Key, followRect, stopRect, guide);
    }

    /// <summary>Measures a chip of the unfolded choice: its icon and its words.</summary>
    /// <param name="label">The words.</param>
    /// <param name="icon">The icon's resource key.</param>
    /// <returns>The chip, measured.</returns>
    private SwapChip Chip(string label, string icon)
    {
        var text = CachedText(label, 11.5, GraphPalette.Find(this, "Brush.Text"), FontWeights.Normal);
        return new SwapChip(label, icon, 8 + 12 + 5 + text.Width + 9);
    }

    /// <summary>Draws a chip of the unfolded choice, lit when it is the choice in force.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="chip">The chip.</param>
    /// <param name="frame">Where.</param>
    /// <param name="lit">Whether it is the choice in force.</param>
    private void DrawChip(DrawingContext dc, SwapChip chip, Rect frame, bool lit)
    {
        if (lit)
        {
            dc.DrawRoundedRectangle(
                GraphPalette.Find(this, "Brush.SurfaceSelected"), null, frame, frame.Height / 2, frame.Height / 2);
        }

        var ink = GraphPalette.Find(this, lit ? "Brush.Text" : "Brush.TextMuted");
        var middle = frame.Y + (frame.Height / 2);
        DrawGlyph(dc, chip.Icon, ink, new Point(frame.X + 8, middle - 6), 12);
        var text = CachedText(chip.Label, 11.5, ink, FontWeights.Normal);
        dc.DrawText(text, new Point(frame.X + 8 + 12 + 5, middle - (text.Height / 2)));
    }

    /// <summary>
    /// Draws the hovered pill's box beside it, in screen units, over everything in the graph (sketch SW1a): whether the
    /// value follows and whose choice it is, the steps, and the transaction with the analysis. An unfolded pill shows
    /// its choice instead.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    private void DrawSwapBox(DrawingContext dc)
    {
        (_swapPills, _nextSwapPills) = (_nextSwapPills, _swapPills);
        _nextSwapPills.Clear();
        if (_model!.OpenSwap is null)
        {
            _swapChoice = null;
        }

        if (_hoveredSwap is not { } key || _model.OpenSwap == key || !_swapPills.TryGetValue(key, out var pill)
            || _model.Scene?.Edge(key) is not { } edge || SwapPill.Box(edge) is not { } box)
        {
            return;
        }

        var at = _viewport.ToScreen(new GraphPoint(pill.X + (pill.Width / 2), pill.Bottom));
        var height = 46 + SwapStepsHeight + 30;
        var left = Math.Clamp(at.X - (SwapBoxWidth / 2), 8, Math.Max(8, ActualWidth - SwapBoxWidth - 8));
        var top = Math.Clamp(at.Y + 10, 8, Math.Max(8, ActualHeight - height - 8));
        var frame = new Rect(left, top, SwapBoxWidth, height);
        DrawFloatingBox(dc, frame);
        dc.PushClip(new RectangleGeometry(frame, CardRadius, CardRadius));
        var title = CachedText(box.Title, 13, GraphPalette.Find(this, "Brush.Text"), FontWeights.SemiBold);
        dc.DrawText(title, new Point(frame.X + MarkBoxPadding, frame.Y + 10));
        var sub = CachedText(box.Sub, 11, GraphPalette.Find(this, "Brush.TextDim"), FontWeights.Normal);
        dc.DrawText(sub, new Point(frame.X + MarkBoxPadding, frame.Y + 10 + title.Height));
        DrawSwapSteps(dc, box, new Rect(frame.X, frame.Y + 46, frame.Width, SwapStepsHeight));
        var footTop = frame.Y + 46 + SwapStepsHeight;
        dc.DrawLine(new Pen(GraphPalette.Find(this, "Brush.Card.Rule"), 1), new Point(frame.X, footTop),
            new Point(frame.Right, footTop));
        var foot = CachedText(box.Foot, 11, GraphPalette.Find(this, "Brush.TextDim"), FontWeights.Normal);
        dc.DrawText(foot, new Point(frame.X + MarkBoxPadding, footTop + ((30 - foot.Height) / 2)));
        dc.Pop();
        dc.DrawRoundedRectangle(null, new Pen(GraphPalette.Find(this, "Brush.BorderStrong"), 1.5), frame, CardRadius,
            CardRadius);
    }

    /// <summary>Draws the box's steps side by side: the name, the value and the quiet line under.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="box">The box's content.</param>
    /// <param name="row">The steps' row.</param>
    private void DrawSwapSteps(DrawingContext dc, SwapPillBox box, Rect row)
    {
        dc.DrawLine(new Pen(GraphPalette.Find(this, "Brush.Card.Rule"), 1), row.TopLeft, row.TopRight);
        var width = (row.Width - (2 * MarkBoxPadding)) / box.Steps.Count;
        var left = row.X + MarkBoxPadding;
        foreach (var step in box.Steps)
        {
            var room = width - 8;
            var dim = GraphPalette.Find(this, "Brush.TextDim");
            dc.DrawText(CachedFitted(step.Key, 10.5, dim, FontWeights.Normal, room), new Point(left, row.Y + 8));
            dc.DrawText(CachedFitted(step.Value, 13, GraphPalette.Find(this, "Brush.Text"), FontWeights.Normal, room),
                new Point(left, row.Y + 22));
            dc.DrawText(CachedFitted(step.Sub, 10.5, dim, FontWeights.Normal, room), new Point(left, row.Y + 40));
            left += width;
        }
    }

    /// <summary>
    /// Takes a press on a swap: a choice or the <em>?</em> of the unfolded pill, or the pill itself, which unfolds or
    /// folds it. A press anywhere else folds an unfolded pill and is left to the graph.
    /// </summary>
    /// <param name="surface">The press on the surface.</param>
    /// <returns><see langword="true"/> when the swap took the press.</returns>
    private bool PressSwap(GraphPoint surface)
    {
        var model = _model!;
        var point = new Point(surface.X, surface.Y);
        if (_swapChoice is { } choice && model.OpenSwap == choice.EdgeKey)
        {
            if (choice.Follows.Contains(point) || choice.Stops.Contains(point))
            {
                _ = model.ChooseSwapAsync(choice.EdgeKey, choice.Follows.Contains(point));
                return true;
            }

            if (choice.Guide.Contains(point))
            {
                model.OpenSwapGuide();
                return true;
            }
        }

        if (SwapAt(surface) is { } key && !model.IsChoosingSwap)
        {
            _hoveredSwap = null;
            model.ToggleSwap(key);
            return true;
        }

        if (model.OpenSwap is not null)
        {
            model.ToggleSwap(null);
        }

        return false;
    }

    /// <summary>Follows the pointer over the pills, so that the hovered pill's box opens.</summary>
    /// <param name="surface">The pointer on the surface.</param>
    private void HoverSwap(GraphPoint surface)
    {
        var swap = SwapAt(surface);
        if (swap != _hoveredSwap)
        {
            _hoveredSwap = swap;
            InvalidateVisual();
        }
    }

    /// <summary>Says whether the pointer is over a pill or the unfolded choice, which take the hand.</summary>
    /// <param name="surface">The pointer on the surface.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private bool IsOverSwap(GraphPoint surface)
    {
        var point = new Point(surface.X, surface.Y);
        return SwapAt(surface) is not null || (_swapChoice is { } choice && _model!.OpenSwap == choice.EdgeKey
            && (choice.Follows.Contains(point) || choice.Stops.Contains(point) || choice.Guide.Contains(point)));
    }

    /// <summary>Gives the pill under a point.</summary>
    /// <param name="surface">The point on the surface.</param>
    /// <returns>The line's key, or <see langword="null"/>.</returns>
    private string? SwapAt(GraphPoint surface)
    {
        var point = new Point(surface.X, surface.Y);
        foreach (var (key, pill) in _swapPills)
        {
            if (pill.Contains(point))
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>A chip of the unfolded choice.</summary>
    /// <param name="Label">Its words.</param>
    /// <param name="Icon">Its icon's resource key.</param>
    /// <param name="Width">Its width.</param>
    private sealed record SwapChip(string Label, string Icon, double Width);

    /// <summary>Where the unfolded choice's parts stand, in surface units.</summary>
    /// <param name="EdgeKey">The line.</param>
    /// <param name="Follows">The chip <em>Följer</em>.</param>
    /// <param name="Stops">The chip <em>Följer inte</em>.</param>
    /// <param name="Guide">The <em>?</em>.</param>
    private sealed record SwapChoiceRects(string EdgeKey, Rect Follows, Rect Stops, Rect Guide);
}
