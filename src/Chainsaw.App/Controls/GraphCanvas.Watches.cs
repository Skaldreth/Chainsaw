using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Chainsaw.App.Graph;
using static Chainsaw.App.Controls.Freezing;

namespace Chainsaw.App.Controls;

/// <summary>
/// The graph's surface, the watches' part (sketch BV2a): the eye in a watched node's corner, the thin dotted watch
/// lines in the watch's own quiet tint with a small eye halfway, the soft glow on lines in the watched direction, the
/// short dotted rays at the port when every counterparty counts, and a hit's line with its amount and rings that move
/// until the hit is seen (still when Windows has animations off). Grey when lookups cannot be made. Only what is on
/// screen is drawn.
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>The watch line's thickness.</summary>
    private const double WatchLineWidth = 2.4;

    /// <summary>The glow's thickness on a line in the watched direction.</summary>
    private const double GlowWidth = 13;

    /// <summary>How faint the glow is.</summary>
    private const double GlowOpacity = 0.16;

    /// <summary>The small eye's disc on a watch line.</summary>
    private const double LineEyeSize = 16;

    /// <summary>A ray's length at the port.</summary>
    private const double RayLength = 30;

    /// <summary>A ray's angle from the port's straight line, in degrees.</summary>
    private const double RayAngle = 28;

    /// <summary>The innermost ring's radius around a hit.</summary>
    private const double RingRadius = 20;

    /// <summary>How far the rings move outwards in one beat.</summary>
    private const double RingTravel = 12;

    /// <summary>The time of one beat of the rings.</summary>
    private static readonly TimeSpan RingBeat = TimeSpan.FromSeconds(1.6);

    /// <summary>The dashes of a tip from the background that waits for Chainsaw's own lookup: uncertain.</summary>
    private static readonly DashStyle PendingDashes = Frozen(new DashStyle([2, 1.6], 0));

    /// <summary>The dots of a watch line and a ray (dotted, never dashed: dashed always means uncertain).</summary>
    private static readonly DashStyle WatchDots = Frozen(new DashStyle([0.01, 2.6], 0));

    /// <summary>Whether moving rings were drawn on screen this frame, so that the frames run only then.</summary>
    private bool _ringsOnScreen;

    /// <summary>Where in a beat the rings are, from 0 to 1.</summary>
    private double _ringPhase;

    /// <summary>When the rings started, so that a beat takes the same time whatever the frames.</summary>
    private DateTime _ringStart = DateTime.UtcNow;

    /// <summary>Gets whether the rings may move: Windows has animations on.</summary>
    private static bool AnimationsOn => SystemParameters.ClientAreaAnimation;

    /// <summary>Draws the glows, under the lines.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="visible">The visible part of the surface.</param>
    private void DrawWatchGlows(DrawingContext dc, GraphRect visible)
    {
        foreach (var (line, curve) in VisibleWatchLines(visible).Where(pair => pair.Line.Kind == WatchLineKind.Glow))
        {
            if (line.Idle)
            {
                continue;
            }

            var pen = PenOf(Ink("Brush.Watch"), GlowWidth, ends: PenLineCap.Round);
            dc.PushOpacity(GlowOpacity);
            dc.DrawGeometry(null, pen, CurveGeometry(curve));
            dc.Pop();
        }
    }

    /// <summary>Draws the watch lines, the hits and the rays, over the lines.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="visible">The visible part of the surface.</param>
    private void DrawWatchMarks(DrawingContext dc, GraphRect visible)
    {
        var overlay = _model!.WatchOverlay;
        foreach (var (line, curve) in VisibleWatchLines(visible))
        {
            switch (line.Kind)
            {
                case WatchLineKind.Guard:
                    dc.DrawGeometry(null, DotPen(line.Idle), CurveGeometry(curve));
                    DrawLineEye(dc, curve.Middle, line.Idle);
                    break;
                case WatchLineKind.Hit:
                    DrawHit(dc, line, curve);
                    break;
                case WatchLineKind.Pending:
                    DrawPending(dc, curve);
                    break;
                default:
                    break;
            }
        }

        DrawTrashHits(dc, visible);
        foreach (var ray in overlay.Rays)
        {
            if (_model.Places.TryGetValue(ray.NodeKey, out var place)
                && _model.FrameAt(ray.NodeKey, place).Inflate(RayLength + LineEyeSize).Intersects(visible))
            {
                DrawRays(dc, ray, _model.FrameAt(ray.NodeKey, place));
            }
        }
    }

    /// <summary>
    /// Draws a watched node's eye in its corner, after its other marks: an accent disc with a dark eye, or a hollow
    /// grey ring when lookups cannot be made; with moving rings while it has unseen hits.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="node">The node.</param>
    /// <param name="place">Its top-left corner.</param>
    private void DrawWatchMark(DrawingContext dc, NodeView node, GraphPoint place)
    {
        if (_model!.WatchOverlay.MarkOf(node.Key) is not { } mark)
        {
            return;
        }

        var index = _model.MarksOf(node).Count;
        var centre = new Point(
            place.X + node.Width + MarkOverhang - (MarkSize / 2) - (index * (MarkSize + 2)),
            place.Y - MarkOverhang + (MarkSize / 2));
        var idle = Ink("Brush.Watch.Idle");
        if (mark.Idle)
        {
            dc.DrawEllipse(
                Ink("Brush.Surface"), PenOf(idle, 1.5), centre, MarkSize / 2, MarkSize / 2);
            DrawGlyph(dc, "Icon.Eye", idle, new Point(centre.X - (MarkIconSize / 2), centre.Y - (MarkIconSize / 2)),
                MarkIconSize);
            return;
        }

        dc.DrawEllipse(
            Ink("Brush.Accent"), PenOf(Ink("Brush.Background"), 2), centre,
            MarkSize / 2, MarkSize / 2);
        DrawGlyph(dc, "Icon.Eye", Ink("Brush.Mark.OnAccent"),
            new Point(centre.X - (MarkIconSize / 2), centre.Y - (MarkIconSize / 2)), MarkIconSize);
    }

    /// <summary>Gives the watch lines whose curves are on screen, with their curves.</summary>
    /// <param name="visible">The visible part of the surface.</param>
    /// <returns>The lines and curves.</returns>
    private IEnumerable<(WatchLine Line, EdgeCurve Curve)> VisibleWatchLines(GraphRect visible)
    {
        var model = _model!;
        foreach (var line in model.WatchOverlay.Lines)
        {
            if (model.CurveBetween(line.From, line.To) is not { } curve)
            {
                continue;
            }

            if (curve.Bounds().Inflate(RingRadius + RingTravel).Intersects(visible))
            {
                yield return (line, curve);
            }
        }
    }

    /// <summary>
    /// Draws a hit: a real line in the accent with its amount, and rings that move while it is unseen.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="line">The hit.</param>
    /// <param name="curve">Its curve.</param>
    private void DrawHit(DrawingContext dc, WatchLine line, EdgeCurve curve)
    {
        var middle = new Point(curve.Middle.X, curve.Middle.Y);
        if (line.IsNew)
        {
            DrawRings(dc, middle, RingRadius, line.Idle);
        }

        var accent = Ink(line.Idle ? "Brush.Watch.Idle" : "Brush.Accent");
        dc.DrawGeometry(null, PenOf(accent, 2.5, ends: PenLineCap.Round), CurveGeometry(curve));
        if (line.Label is not { } label)
        {
            return;
        }

        var text = Text(label, 11, accent, FontWeights.SemiBold);
        var box = new Rect(middle.X - (text.Width / 2) - 5, middle.Y - (text.Height / 2) - 1, text.Width + 10,
            text.Height + 2);
        dc.DrawRoundedRectangle(Ink("Brush.Background"), null, box, 4, 4);
        dc.DrawText(text, new Point(box.X + 5, box.Y + 1));
    }

    /// <summary>
    /// Draws a tip from the background that waits for Chainsaw's own lookup: a grey dashed line with a hollow eye
    /// halfway, never the hit's real line, amount or rings; what it is is said on hovering the eye.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="curve">Its curve.</param>
    private void DrawPending(DrawingContext dc, EdgeCurve curve)
    {
        var pen = PenOf(Ink("Brush.Watch.Idle"), 2, PendingDashes);
        dc.DrawGeometry(null, pen, CurveGeometry(curve));
        DrawLineEye(dc, curve.Middle, idle: true);
    }

    /// <summary>Gives what a waiting tip's eye says on hovering, or <see langword="null"/> when not on one.</summary>
    /// <param name="surface">The pointer on the surface.</param>
    /// <returns>The tip.</returns>
    private string? PendingTipAt(GraphPoint surface)
    {
        var model = _model!;
        foreach (var line in model.WatchOverlay.Lines.Where(line => line.Kind == WatchLineKind.Pending))
        {
            if (model.CurveBetween(line.From, line.To) is { } curve
                && Math.Abs(curve.Middle.X - surface.X) <= LineEyeSize / 2
                && Math.Abs(curve.Middle.Y - surface.Y) <= LineEyeSize / 2)
            {
                return line.Tip;
            }
        }

        return null;
    }

    /// <summary>
    /// Draws two rings in the watch's tint around a point: moving outwards and fading while animations are on, still
    /// at their resting radii otherwise.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="centre">The point.</param>
    /// <param name="radius">The innermost radius.</param>
    /// <param name="idle">Whether lookups cannot be made now (grey).</param>
    private void DrawRings(DrawingContext dc, Point centre, double radius, bool idle)
    {
        RingsAt(new GraphRect(centre.X - (radius * 3), centre.Y - (radius * 3), radius * 6, radius * 6));
        var tint = Ink(idle ? "Brush.Watch.Idle" : "Brush.Watch");
        var phase = AnimationsOn ? _ringPhase : 0;
        for (var ring = 0; ring < 2; ring++)
        {
            var step = (phase + (ring * 0.5)) % 1;
            var reach = radius + (AnimationsOn ? step * RingTravel : ring * RingTravel * 0.8);
            var opacity = AnimationsOn ? 0.6 * (1 - step) : (ring == 0 ? 0.55 : 0.25);
            dc.PushOpacity(opacity);
            dc.DrawEllipse(null, PenOf(tint, ring == 0 ? 1.5 : 1), centre, reach, reach);
            dc.Pop();
        }
    }

    /// <summary>Draws the small eye on a watch line, in the watch's tint, hollow when lookups cannot be made.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="at">The line's middle.</param>
    /// <param name="idle">Whether lookups cannot be made now.</param>
    private void DrawLineEye(DrawingContext dc, GraphPoint at, bool idle)
    {
        var tint = Ink(idle ? "Brush.Watch.Idle" : "Brush.Watch");
        var centre = new Point(at.X, at.Y);
        dc.DrawEllipse(Ink("Brush.Background"), PenOf(tint, 1.2), centre, LineEyeSize / 2,
            LineEyeSize / 2);
        const double glyph = 10;
        DrawGlyph(dc, "Icon.Eye", tint, new Point(centre.X - (glyph / 2), centre.Y - (glyph / 2)), glyph);
    }

    /// <summary>Draws the rays at a port for a watch without counterparties, with the eye at their end.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="ray">The rays.</param>
    /// <param name="frame">The node's frame.</param>
    private void DrawRays(DrawingContext dc, WatchRay ray, GraphRect frame)
    {
        var port = ray.IsOut
            ? new Point(frame.Right, frame.Y + (GraphSizes.NodeHeight / 2))
            : new Point(frame.X, frame.Y + (GraphSizes.NodeHeight / 2));
        var straight = ray.IsOut ? 0 : 180;
        var pen = DotPen(ray.Idle);
        foreach (var turn in new[] { -RayAngle, RayAngle })
        {
            dc.DrawLine(pen, port, Along(port, straight + turn, RayLength));
        }

        var eye = Along(port, straight + (ray.IsOut ? -52 : 52), RayLength + 10);
        DrawLineEye(dc, new GraphPoint(eye.X, eye.Y), ray.Idle);
    }

    /// <summary>Gives the pen of a watch line or a ray.</summary>
    /// <param name="idle">Whether lookups cannot be made now (grey).</param>
    /// <returns>The pen.</returns>
    private Pen DotPen(bool idle) =>
        PenOf(Ink(idle ? "Brush.Watch.Idle" : "Brush.Watch"), WatchLineWidth, WatchDots, PenLineCap.Round,
            PenLineCap.Round);

    /// <summary>
    /// Starts the rings while moving rings are on screen and animations are on, and stops them otherwise, so that the
    /// graph is drawn again only while something it shows moves (principle 6).
    /// </summary>
    /// <param name="moves">Whether moving rings were drawn on screen.</param>
    private void KeepRings(bool moves)
    {
        if (moves && !_frames.IsRunning(GraphMotion.Rings))
        {
            _ringStart = DateTime.UtcNow;
        }

        KeepMotion(GraphMotion.Rings, moves);
    }

    /// <summary>Moves the rings one step, by the time since they started.</summary>
    /// <param name="seconds">The time since the last step; the beat follows the clock.</param>
    /// <returns>
    /// What changed: the lines, where the watches' rings are, and what stands over the nodes, where the search's ring
    /// and the guide's sonar are; the nodes keep their drawing.
    /// </returns>
    private GraphChange RingStep(double seconds)
    {
        _ringPhase = (DateTime.UtcNow - _ringStart).TotalMilliseconds / RingBeat.TotalMilliseconds % 1;
        return GraphChange.Lines | GraphChange.OverNodes;
    }

    /// <summary>
    /// Says whether an element's window is minimised: a minimised window stays visible to WPF, but nothing is drawn
    /// for it, so a timer's frames are skipped (as for the lock screen's figure).
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns><see langword="true"/> when its window is minimised.</returns>
    internal static bool IsMinimised(DependencyObject element) =>
        Window.GetWindow(element) is { WindowState: WindowState.Minimized };

    /// <summary>Gives the point a distance from another in a direction.</summary>
    /// <param name="from">The point.</param>
    /// <param name="degrees">The direction, clockwise from the right.</param>
    /// <param name="length">The distance.</param>
    /// <returns>The point.</returns>
    private static Point Along(Point from, double degrees, double length)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(from.X + (Math.Cos(radians) * length), from.Y + (Math.Sin(radians) * length));
    }

    /// <summary>Gives a curve's geometry.</summary>
    /// <param name="curve">The curve.</param>
    /// <returns>The frozen geometry.</returns>
    private static StreamGeometry Geometry(EdgeCurve curve)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToPoint(curve.Start), false, false);
            context.BezierTo(ToPoint(curve.Control1), ToPoint(curve.Control2), ToPoint(curve.End), true, true);
        }

        geometry.Freeze();
        return geometry;
    }
}
