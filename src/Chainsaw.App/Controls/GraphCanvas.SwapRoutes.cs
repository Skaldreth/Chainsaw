using System.Windows;
using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// A DEX's router on the line (sketch DX1b): no node of its own, since a swap changes token and not owner, but a small
/// square mark in the top right corner of the swap's pill, grey as everything neutral, with the router's icon or, for
/// an aggregator, the number of steps. Hovering the mark shows the way through the pools in a box; a click opens
/// <em>Varför?</em> in the Guide's panel. The mark is only a view: the trail goes through as before.
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>The side of the router's mark, in surface units.</summary>
    private const double RouteMarkSize = 16;

    /// <summary>The width of the router's box on screen.</summary>
    private const double RouteBoxWidth = 430;

    /// <summary>The height of the way's row in the router's box.</summary>
    private const double RouteStepsHeight = 54;

    /// <summary>Where the router marks stood when the graph was last drawn, by line, in surface units.</summary>
    private Dictionary<string, Rect> _routeMarks = new(StringComparer.Ordinal);

    /// <summary>Where the router marks stand in the drawing under way.</summary>
    private Dictionary<string, Rect> _nextRouteMarks = new(StringComparer.Ordinal);

    /// <summary>The line whose router mark is under the pointer, or <see langword="null"/>.</summary>
    private string? _hoveredRoute;

    /// <summary>
    /// Draws the router's mark in the pill's top right corner: a small rounded square with the router's icon, or the
    /// number of steps for an aggregator, edged in the ground's colour so that it stands free of the pill.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="edge">The line.</param>
    /// <param name="pill">The pill, in surface units.</param>
    private void DrawRouteMark(DrawingContext dc, EdgeView edge, Rect pill)
    {
        if (edge.SwapRoute is not { } route)
        {
            return;
        }

        var mark = new Rect(pill.Right - (RouteMarkSize / 2) - 2, pill.Top - (RouteMarkSize / 2) + 2, RouteMarkSize,
            RouteMarkSize);
        _nextRouteMarks[edge.Key] = mark;
        var ground = Ink("Brush.Background");
        dc.DrawRoundedRectangle(Ink("Brush.TextMuted"), PenOf(ground, 2), mark, 5, 5);
        var centre = new Point(mark.X + (mark.Width / 2), mark.Y + (mark.Height / 2));
        if (route.MarkCount is { } count)
        {
            var text = Text(count, 9.5, ground, FontWeights.Bold);
            dc.DrawText(text, new Point(centre.X - (text.Width / 2), centre.Y - (text.Height / 2)));
            return;
        }

        DrawGlyph(dc, "Icon.Swap.Router", ground, new Point(centre.X - 5, centre.Y - 5), 10);
    }

    /// <summary>
    /// Draws the hovered router mark's box above its pill, in screen units, over everything in the graph (sketch DX1b):
    /// the router's name and what it does, the way through the pools, whether the trail goes through, and the contract
    /// with the transaction.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <returns><see langword="true"/> when a box was drawn.</returns>
    private bool DrawRouteBox(DrawingContext dc)
    {
        if (_hoveredRoute is not { } key || !_routeMarks.TryGetValue(key, out var mark)
            || _model!.Scene?.Edge(key) is not { SwapRoute: { } route } edge)
        {
            return false;
        }

        const double Head = 50;
        const double Status = 28;
        const double Foot = 26;
        var height = Head + RouteStepsHeight + Status + Foot;
        var at = _viewport.ToScreen(new GraphPoint(mark.X + (mark.Width / 2), mark.Y));
        var left = Math.Clamp(at.X - (RouteBoxWidth / 2), 8, Math.Max(8, ActualWidth - RouteBoxWidth - 8));
        var top = Math.Clamp(at.Y - height - 12, 8, Math.Max(8, ActualHeight - height - 8));
        var frame = new Rect(left, top, RouteBoxWidth, height);
        DrawFloatingBox(dc, frame);
        dc.PushClip(new RectangleGeometry(frame, CardRadius, CardRadius));
        DrawRouteHead(dc, route, new Rect(frame.X, frame.Y, frame.Width, Head));
        var rule = PenOf(Ink("Brush.Card.Rule"), 1);
        var steps = new Rect(frame.X, frame.Y + Head, frame.Width, RouteStepsHeight);
        dc.DrawLine(rule, steps.TopLeft, steps.TopRight);
        DrawRouteSteps(dc, route, steps);
        dc.DrawLine(rule, steps.BottomLeft, steps.BottomRight);
        DrawRouteStatus(dc, edge, new Rect(frame.X, steps.Bottom, frame.Width, Status));
        var foot = MonoText(route.Foot, 10.5, Ink("Brush.TextDim"));
        dc.DrawText(foot, new Point(frame.X + MarkBoxPadding, steps.Bottom + Status + ((Foot - foot.Height) / 2) - 3));
        dc.Pop();
        dc.DrawRoundedRectangle(null, PenOf(Ink("Brush.BorderStrong"), 1.5), frame, CardRadius, CardRadius);
        return true;
    }

    /// <summary>Draws the router box's head: the router's icon in a frame, its name and the quiet line under.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="route">The way.</param>
    /// <param name="head">The head's room.</param>
    private void DrawRouteHead(DrawingContext dc, SwapRoute route, Rect head)
    {
        var ink = Ink("Brush.TextMuted");
        var icon = new Rect(head.X + MarkBoxPadding, head.Y + 12, 26, 26);
        dc.DrawRoundedRectangle(null, PenOf(ink, 1.5), icon, 7, 7);
        DrawGlyph(dc, "Icon.Swap.Router", ink, new Point(icon.X + 6, icon.Y + 6), 14);
        var room = head.Width - (2 * MarkBoxPadding) - 36;
        var textLeft = icon.Right + 10;
        dc.DrawText(TrimmedText(route.Title, 13.5, Ink("Brush.Text"), FontWeights.SemiBold, room),
            new Point(textLeft, head.Y + 9));
        dc.DrawText(TrimmedText(route.Sub, 11.5, Ink("Brush.TextMuted"), FontWeights.Normal, room),
            new Point(textLeft, head.Y + 28));
    }

    /// <summary>
    /// Draws the way side by side: each step's token badge and amount, the pool under it in the dim colour, and a
    /// small chevron between the steps; the first and the last step, the money in and out, in the accent.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="route">The way.</param>
    /// <param name="row">The row's room.</param>
    private void DrawRouteSteps(DrawingContext dc, SwapRoute route, Rect row)
    {
        const double Chevron = 18;
        var count = route.Steps.Count;
        var width = (row.Width - (2 * MarkBoxPadding) - ((count - 1) * Chevron)) / count;
        var left = row.X + MarkBoxPadding;
        for (var index = 0; index < count; index++)
        {
            var step = route.Steps[index];
            if (index > 0)
            {
                DrawGlyph(dc, "Icon.ChevronRight", Ink("Brush.TextFaint"), new Point(left + 3, row.Y + 12), 12);
                left += Chevron;
            }

            var ends = index == 0 || index == count - 1;
            var ink = Ink(ends ? "Brush.AccentHover" : "Brush.TextMuted");
            DrawTokenBadge(dc, step.Mark, new Point(left + (AmountLayout.BadgeSize / 2), row.Y + 18));
            var amount = TrimmedText(step.Amount, 12.5, ink, FontWeights.Normal, width - AmountLayout.BadgeSize - 10);
            dc.DrawText(amount, new Point(left + AmountLayout.BadgeSize + 5, row.Y + 18 - (amount.Height / 2)));
            if (step.Pool is { } pool)
            {
                dc.DrawText(TrimmedText(pool, 10.5, Ink("Brush.TextDim"), FontWeights.Normal, width - 4),
                    new Point(left, row.Y + 31));
            }

            left += width;
        }
    }

    /// <summary>
    /// Draws whether the trail goes through the swap, with a check in the success colour or a stop in a quiet one,
    /// and the hint that a click opens <em>Varför?</em>.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="edge">The line.</param>
    /// <param name="row">The row's room.</param>
    private void DrawRouteStatus(DrawingContext dc, EdgeView edge, Rect row)
    {
        var through = SwapRoute.GoesThrough(edge);
        var ink = Ink(through ? "Brush.Success" : "Brush.TextMuted");
        var middle = row.Y + (row.Height / 2);
        DrawGlyph(dc, through ? "Icon.Kind.Check" : "Icon.Swap.Stop", ink,
            new Point(row.X + MarkBoxPadding, middle - 6.5), 13);
        var status = Text(through ? SwapRoute.ThroughText : SwapRoute.StopsText, 12, ink, FontWeights.Normal);
        dc.DrawText(status, new Point(row.X + MarkBoxPadding + 20, middle - (status.Height / 2)));
        var hint = Text(SwapRoute.ClickText, 11, Ink("Brush.TextDim"), FontWeights.Normal);
        dc.DrawText(hint, new Point(row.Right - MarkBoxPadding - hint.Width, middle - (hint.Height / 2)));
    }

    /// <summary>Gives the line whose router mark is under a point.</summary>
    /// <param name="surface">The point on the surface.</param>
    /// <returns>The line's key, or <see langword="null"/>.</returns>
    private string? RouteAt(GraphPoint surface)
    {
        var point = new Point(surface.X, surface.Y);
        foreach (var (key, mark) in _routeMarks)
        {
            if (mark.Contains(point))
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>
    /// Takes where the router marks stand in the lines just drawn as where they stand now, with the pills
    /// (<see cref="KeepSwapPills"/>).
    /// </summary>
    private void KeepRouteMarks()
    {
        (_routeMarks, _nextRouteMarks) = (_nextRouteMarks, _routeMarks);
        _nextRouteMarks.Clear();
    }
}
