using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Chainsaw.App.Graph;

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

    /// <summary>The time between two frames of the rings.</summary>
    private static readonly TimeSpan RingFrame = TimeSpan.FromMilliseconds(50);

    /// <summary>The time of one beat of the rings.</summary>
    private static readonly TimeSpan RingBeat = TimeSpan.FromSeconds(1.6);

    /// <summary>The dots of a watch line and a ray (dotted, never dashed: dashed always means uncertain).</summary>
    private static readonly DashStyle WatchDots = Frozen(new DashStyle([0.01, 2.6], 0));

    /// <summary>Moves the rings while a hit is unseen; stopped otherwise.</summary>
    private DispatcherTimer? _rings;

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

            var pen = new Pen(GraphPalette.Find(this, "Brush.Watch"), GlowWidth)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            dc.PushOpacity(GlowOpacity);
            dc.DrawGeometry(null, pen, Geometry(curve));
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
                    dc.DrawGeometry(null, DotPen(line.Idle), Geometry(curve));
                    DrawLineEye(dc, curve.Middle, line.Idle);
                    break;
                case WatchLineKind.Hit:
                    DrawHit(dc, line, curve);
                    break;
                default:
                    break;
            }
        }

        foreach (var ray in overlay.Rays)
        {
            if (_model.Places.TryGetValue(ray.NodeKey, out var place)
                && GraphSizes.NodeFrame(place).Inflate(RayLength + LineEyeSize).Intersects(visible))
            {
                DrawRays(dc, ray, place);
            }
        }

        KeepRings(overlay.Moves);
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

        var index = node.Badges.Count;
        var centre = new Point(
            place.X + GraphSizes.NodeWidth + MarkOverhang - (MarkSize / 2) - (index * (MarkSize + 2)),
            place.Y - MarkOverhang + (MarkSize / 2));
        var idle = GraphPalette.Find(this, "Brush.Watch.Idle");
        if (mark.HasNew)
        {
            DrawRings(dc, centre, MarkSize / 2, mark.Idle);
        }

        if (mark.Idle)
        {
            dc.DrawEllipse(
                GraphPalette.Find(this, "Brush.Surface"), new Pen(idle, 1.5), centre, MarkSize / 2, MarkSize / 2);
            DrawGlyph(dc, "Icon.Eye", idle, new Point(centre.X - (MarkIconSize / 2), centre.Y - (MarkIconSize / 2)),
                MarkIconSize);
            return;
        }

        dc.DrawEllipse(
            GraphPalette.Find(this, "Brush.Accent"), new Pen(GraphPalette.Find(this, "Brush.Background"), 2), centre,
            MarkSize / 2, MarkSize / 2);
        DrawGlyph(dc, "Icon.Eye", GraphPalette.Find(this, "Brush.Mark.OnAccent"),
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
            if (!model.Places.TryGetValue(line.From, out var from) || !model.Places.TryGetValue(line.To, out var to))
            {
                continue;
            }

            var curve = EdgeCurve.Between(from, to, model.IsHopColumns);
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

        var accent = GraphPalette.Find(this, line.Idle ? "Brush.Watch.Idle" : "Brush.Accent");
        dc.DrawGeometry(null, new Pen(accent, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            Geometry(curve));
        if (line.Label is not { } label)
        {
            return;
        }

        var text = Text(label, 11, accent, FontWeights.SemiBold);
        var box = new Rect(middle.X - (text.Width / 2) - 5, middle.Y - (text.Height / 2) - 1, text.Width + 10,
            text.Height + 2);
        dc.DrawRoundedRectangle(GraphPalette.Find(this, "Brush.Background"), null, box, 4, 4);
        dc.DrawText(text, new Point(box.X + 5, box.Y + 1));
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
        var tint = GraphPalette.Find(this, idle ? "Brush.Watch.Idle" : "Brush.Watch");
        var phase = AnimationsOn ? _ringPhase : 0;
        for (var ring = 0; ring < 2; ring++)
        {
            var step = (phase + (ring * 0.5)) % 1;
            var reach = radius + (AnimationsOn ? step * RingTravel : ring * RingTravel * 0.8);
            var opacity = AnimationsOn ? 0.6 * (1 - step) : (ring == 0 ? 0.55 : 0.25);
            dc.PushOpacity(opacity);
            dc.DrawEllipse(null, new Pen(tint, ring == 0 ? 1.5 : 1), centre, reach, reach);
            dc.Pop();
        }
    }

    /// <summary>Draws the small eye on a watch line, in the watch's tint, hollow when lookups cannot be made.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="at">The line's middle.</param>
    /// <param name="idle">Whether lookups cannot be made now.</param>
    private void DrawLineEye(DrawingContext dc, GraphPoint at, bool idle)
    {
        var tint = GraphPalette.Find(this, idle ? "Brush.Watch.Idle" : "Brush.Watch");
        var centre = new Point(at.X, at.Y);
        dc.DrawEllipse(GraphPalette.Find(this, "Brush.Background"), new Pen(tint, 1.2), centre, LineEyeSize / 2,
            LineEyeSize / 2);
        const double glyph = 10;
        DrawGlyph(dc, "Icon.Eye", tint, new Point(centre.X - (glyph / 2), centre.Y - (glyph / 2)), glyph);
    }

    /// <summary>Draws the rays at a port for a watch without counterparties, with the eye at their end.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="ray">The rays.</param>
    /// <param name="place">The node's top-left corner.</param>
    private void DrawRays(DrawingContext dc, WatchRay ray, GraphPoint place)
    {
        var port = ray.IsOut
            ? new Point(place.X + GraphSizes.NodeWidth, place.Y + (GraphSizes.NodeHeight / 2))
            : new Point(place.X, place.Y + (GraphSizes.NodeHeight / 2));
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
    private Pen DotPen(bool idle) => new(GraphPalette.Find(this, idle ? "Brush.Watch.Idle" : "Brush.Watch"),
        WatchLineWidth)
    {
        DashStyle = WatchDots,
        DashCap = PenLineCap.Round,
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
    };

    /// <summary>
    /// Starts the rings' frames while something moves and animations are on, and stops them otherwise.
    /// </summary>
    /// <param name="moves">Whether a hit is unseen.</param>
    private void KeepRings(bool moves)
    {
        if (moves && AnimationsOn && IsVisible)
        {
            if (_rings is null)
            {
                _ringStart = DateTime.UtcNow;
                _rings = new DispatcherTimer(RingFrame, DispatcherPriority.Render, OnRingFrame, Dispatcher);
            }

            return;
        }

        _rings?.Stop();
        _rings = null;
    }

    /// <summary>Moves the rings one frame and draws again.</summary>
    /// <param name="sender">The timer.</param>
    /// <param name="e">The arguments.</param>
    private void OnRingFrame(object? sender, EventArgs e)
    {
        _ringPhase = (DateTime.UtcNow - _ringStart).TotalMilliseconds / RingBeat.TotalMilliseconds % 1;
        InvalidateVisual();
    }

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
