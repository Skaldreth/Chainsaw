using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using static Chainsaw.App.Controls.Freezing;

namespace Chainsaw.App.Controls;

/// <summary>
/// The selection view of what was held back (sketches EX1a, EX1b and EX2): a click on a clue sends the graph out in a
/// circular blast from it while the address moves to the middle; the held-back transactions come out from the address
/// as dots on a spiral or a time arc, with their guides, marks and sonar; a click chooses, Shift-click a run, a drawn
/// ring (L) everything inside it; and the graph comes back when the view closes. Drawn in screen units, apart from the
/// graph's zoom. Everything stands still when Windows has animations turned off.
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>How far right of the middle the view's centre lies, clear of the filter panel (sketch EX2).</summary>
    private const double ViewRightOfMiddle = 80;

    /// <summary>Where the time arc's centre lies, as a share of the height (sketch EX2: 660 of 834).</summary>
    private const double ArcCentreShare = 0.79;

    /// <summary>The address card's width in the middle.</summary>
    private const double CentreCardWidth = 150;

    /// <summary>The address card's height.</summary>
    private const double CentreCardHeight = 46;

    /// <summary>How far a dot grows under the pointer.</summary>
    private const double DotHoverGrowth = 3;

    /// <summary>The corner marks' size: a ring for one in the graph, a diamond for a label from another case.</summary>
    private const double CornerMark = 7;

    /// <summary>What the address card says while the history is read.</summary>
    private const string ReadingText = "Läser historiken …";

    /// <summary>The fewest points a drawn ring needs before it chooses.</summary>
    private const int LassoPoints = 6;

    /// <summary>The dotted guides' dashes, in multiples of their thickness (sketch EX2: 1 5).</summary>
    private static readonly DashStyle GuideDots = Frozen(new DashStyle([0.1, 5], 0));

    /// <summary>The traced arrival's dashes (sketch EX2: 4 5).</summary>
    private static readonly DashStyle ArrivalDash = Frozen(new DashStyle([3.3, 4.2], 0));

    /// <summary>The lasso's dashes (sketch EX2: 5 4).</summary>
    private static readonly DashStyle LassoDash = Frozen(new DashStyle([3.3, 2.7], 0));

    /// <summary>The dashes of a dot whose amount is uncertain (principle 2: dashed always means uncertain).</summary>
    private static readonly DashStyle UncertainDash = Frozen(new DashStyle([2, 1.6], 0));

    /// <summary>How strongly an uncertain payment out is filled, so that its dashed ring shows.</summary>
    private const double UncertainFill = 0.4;

    /// <summary>The step of the blast.</summary>
    private BlastPhase _blast;

    /// <summary>When the step began.</summary>
    private DateTime _blastStart;

    /// <summary>The clue's centre on screen, where the blast starts.</summary>
    private GraphPoint _blastOrigin;

    /// <summary>The address the clue belongs to, which moves to the middle.</summary>
    private string? _blastAnchor;

    /// <summary>
    /// The drawn ring, relative to the view's centre, while it is drawn; otherwise <see langword="null"/>.
    /// </summary>
    private List<GraphPoint>? _lasso;

    /// <summary>Whether a press in the view is under way.</summary>
    private bool _viewPressed;

    /// <summary>Gets the view's centre on screen in the mode shown.</summary>
    private GraphPoint ViewCentre => _model?.HeldBack is { IsTimeArc: true }
        ? new GraphPoint((ActualWidth / 2) + ViewRightOfMiddle, ActualHeight * ArcCentreShare)
        : new GraphPoint((ActualWidth / 2) + ViewRightOfMiddle, ActualHeight / 2);

    /// <summary>Gets how far the step of the blast has come, from 0 to 1.</summary>
    private double BlastK => _blast switch
    {
        BlastPhase.Out => Progress(HeldBackMotion.OutTime),
        BlastPhase.View => Progress(HeldBackMotion.ViewTime),
        BlastPhase.Back => Progress(HeldBackMotion.BackTime),
        _ => 1,
    };

    /// <summary>Follows the model: the blast, the selection view and the fan.</summary>
    /// <param name="sender">The model.</param>
    /// <param name="e">What changed.</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GraphViewModel.OpeningClue):
            case nameof(GraphViewModel.HeldBack):
                FollowBlast();
                break;
            case nameof(GraphViewModel.Fan):
                FollowFanMotion();
                break;
        }
    }

    /// <summary>
    /// Follows the selection view: a clue being read sends the graph out, the view's choices bring the dots, and a
    /// closed view brings the graph back.
    /// </summary>
    private void FollowBlast()
    {
        var model = _model!;
        var opening = model.OpeningClue is not null || model.HeldBack is not null;
        if (opening && _blast is BlastPhase.None or BlastPhase.Back)
        {
            StartBlast(model.OpeningClue ?? model.HeldBack!.Choices.Clue.Key);
        }

        if (model.HeldBack is not null && _blast == BlastPhase.Out && BlastK >= 1)
        {
            Begin(BlastPhase.View);
        }

        if (!opening && _blast is BlastPhase.Out or BlastPhase.View)
        {
            _lasso = null;
            _viewPressed = false;
            Begin(BlastPhase.Back);
        }

        Redraw();
    }

    /// <summary>Starts the blast from a clue.</summary>
    /// <param name="clueKey">The clue.</param>
    private void StartBlast(string clueKey)
    {
        var model = _model!;
        _blastAnchor = model.Scene?.Clue(clueKey)?.Anchor;
        _blastOrigin = model.Places.TryGetValue(clueKey, out var place)
            ? _viewport.ToScreen(GraphSizes.NodeFrame(place).Centre)
            : ViewCentre;
        CloseHoverTip();
        StopZoom();
        Begin(BlastPhase.Out);
    }

    /// <summary>Begins a step of the blast; without animations it is over at once.</summary>
    /// <param name="phase">The step.</param>
    private void Begin(BlastPhase phase)
    {
        _blast = phase;
        _blastStart = AnimationsOn ? DateTime.UtcNow : DateTime.MinValue;
        if (phase == BlastPhase.Back && !AnimationsOn)
        {
            _blast = BlastPhase.None;
        }

        KeepMoving();
    }

    /// <summary>Gives how far a step has come.</summary>
    /// <param name="length">The step's length.</param>
    /// <returns>From 0 to 1.</returns>
    private double Progress(TimeSpan length) => HeldBackMotion.Unit((DateTime.UtcNow - _blastStart) / length);

    /// <summary>Moves the blast one frame on: out, then the view as soon as its choices are in, then home.</summary>
    /// <returns><see langword="true"/> while something moves.</returns>
    private bool StepBlast()
    {
        switch (_blast)
        {
            case BlastPhase.Out when BlastK >= 1 && _model?.HeldBack is not null:
                Begin(BlastPhase.View);
                return true;
            case BlastPhase.Out:
                return BlastK < 1;
            case BlastPhase.View:
                return BlastK < 1 || (AnimationsOn && _model?.HeldBack?.HasPulsing == true);
            case BlastPhase.Back when BlastK >= 1:
                _blast = BlastPhase.None;
                return false;
            case BlastPhase.Back:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Asks for a frame each time the screen is drawn while something moves (the blast, the pile, the sonar), and
    /// stops asking when nothing does (principle 6).
    /// </summary>
    private void KeepMoving()
    {
        if (AnimationsOn)
        {
            StartMotion(GraphMotion.Blast);
        }

        Redraw();
    }

    /// <summary>Moves the blast and the fan one step, and stops when neither moves or the surface is hidden.</summary>
    /// <param name="seconds">The time since the last step; the two move by the clock of their own.</param>
    /// <returns>What changed: the scene.</returns>
    private GraphChange BlastStep(double seconds)
    {
        var fan = StepFan();
        var blast = StepBlast();
        if ((!fan && !blast) || !IsVisible)
        {
            _frames.Stop(GraphMotion.Blast);
        }

        return GraphChange.Scene;
    }

    /// <summary>Gives where a node is during the blast out or back.</summary>
    /// <param name="key">The node.</param>
    /// <param name="place">Its top-left corner on the surface.</param>
    /// <returns>Its flight and where it flies to, on the surface.</returns>
    private (Flight Flight, GraphPoint Away) FlightOf(string key, GraphPoint place)
    {
        var frame = GraphSizes.NodeFrame(place);
        var home = _viewport.ToScreen(frame.Centre);
        if (key == _blastAnchor)
        {
            var t = HeldBackMotion.EaseInOut(BlastK);
            var share = _blast == BlastPhase.Back ? 1 - t : t;
            var centre = _viewport.ToSurface(ViewCentre);
            var to = new GraphPoint(place.X + (centre.X - frame.Centre.X), place.Y + (centre.Y - frame.Centre.Y));
            return (new Flight(share, 1, 1), to);
        }

        var dx = home.X - _blastOrigin.X;
        var dy = home.Y - _blastOrigin.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        var flight = _blast == BlastPhase.Back
            ? HeldBackMotion.Back(distance, BlastK)
            : HeldBackMotion.Out(distance, BlastK);
        var reach = HeldBackMotion.FlyDistance / Math.Max(1, distance) / _viewport.Zoom;
        return (flight, new GraphPoint(place.X + (dx * reach), place.Y + (dy * reach)));
    }

    /// <summary>
    /// Draws a node where the blast has it: moved out, smaller and fainter; the address on its way to the middle.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="node">The node.</param>
    /// <param name="place">Its top-left corner.</param>
    /// <param name="near">The nodes near what is chosen.</param>
    private void DrawFlying(DrawingContext dc, NodeView node, GraphPoint place, IReadOnlySet<string> near)
    {
        if (_blast is not (BlastPhase.Out or BlastPhase.Back))
        {
            if (!DrawNearBin(dc, node, place, near))
            {
                DrawNode(dc, node, place, near);
            }

            return;
        }

        var (flight, away) = FlightOf(node.Key, place);
        if (flight.Opacity <= 0.01)
        {
            return;
        }

        var at = HeldBackMotion.Lerp(place, away, flight.Travel);
        var centre = GraphSizes.NodeFrame(at).Centre;
        dc.PushOpacity(flight.Opacity);
        dc.PushTransform(new ScaleTransform(flight.Scale, flight.Scale, centre.X, centre.Y));
        DrawNode(dc, node, at, near);
        dc.Pop();
        dc.Pop();
    }

    /// <summary>Says whether the graph itself is hidden, because the selection view stands in its place.</summary>
    private bool GraphHidden => _blast == BlastPhase.View || (_blast == BlastPhase.Out && BlastK >= 1);

    /// <summary>
    /// Draws the blast's rings and the selection view over the graph, in screen units: while the history is read only
    /// the address in the middle, then the guides, the dots and their marks.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    private void DrawHeldBack(DrawingContext dc)
    {
        if (_blast == BlastPhase.Out && AnimationsOn)
        {
            DrawBlastRings(dc);
        }

        if (!GraphHidden)
        {
            return;
        }

        var view = _model?.HeldBack;
        var centre = ViewCentre;
        if (view is null || _blast != BlastPhase.View)
        {
            DrawCentreCard(dc, centre, ReadingText);
            return;
        }

        var k = BlastK;
        dc.PushOpacity(HeldBackMotion.Settle(k));
        DrawGuides(dc, view.Guides, centre);
        dc.Pop();
        DrawDots(dc, view, centre, k);
        DrawCentreCard(dc, centre, view.CentreText);
        DrawLasso(dc, centre);
        if (view.Hovered >= 0 && view.Hovered < view.Dots.Count && k >= 1)
        {
            DrawDotTip(dc, view, view.Dots[view.Hovered], centre);
        }
    }

    /// <summary>Draws the blast's two rings swelling out from the clue.</summary>
    /// <param name="dc">Where to draw.</param>
    private void DrawBlastRings(DrawingContext dc)
    {
        var pen = PenOf(Ink("Brush.TextDim"), 1.5);
        for (var ring = 0; ring < 2; ring++)
        {
            var (diameter, opacity) = HeldBackMotion.Ring(ring, BlastK);
            dc.PushOpacity(opacity);
            dc.DrawEllipse(null, pen, ToPoint(_blastOrigin), diameter / 2, diameter / 2);
            dc.Pop();
        }
    }

    /// <summary>
    /// Draws the guides: the dotted spiral or arcs, the ticks, the traced arrival's ray and the words.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="guides">The guides.</param>
    /// <param name="centre">The view's centre.</param>
    private void DrawGuides(DrawingContext dc, HeldBackGuides guides, GraphPoint centre)
    {
        var faint = Ink("Brush.BorderStrong");
        var dots = PenOf(faint, 1.2, GuideDots, PenLineCap.Round);
        foreach (var run in guides.Dotted.Where(run => run.Count > 1))
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(At(centre, run[0]), false, false);
                context.PolyLineTo([.. run.Skip(1).Select(point => At(centre, point))], true, true);
            }

            geometry.Freeze();
            dc.DrawGeometry(null, dots, geometry);
        }

        var tick = PenOf(faint, 1);
        foreach (var (from, to) in guides.Ticks)
        {
            dc.DrawLine(tick, At(centre, from), At(centre, to));
        }

        if (guides.Arrival is { } ray)
        {
            var dashed = PenOf(Ink("Brush.Accent"), 1.2, ArrivalDash);
            dc.DrawLine(dashed, At(centre, ray.From), At(centre, ray.To));
        }

        foreach (var label in guides.Labels)
        {
            var ink = Ink(label.IsAccent ? "Brush.Accent" : "Brush.TextDim");
            var text = Text(label.Text, 10.5, ink, FontWeights.Normal);
            var at = At(centre, label.At);
            var box = new Rect(at.X - (text.Width / 2) - 4, at.Y - (text.Height / 2), text.Width + 8, text.Height);
            dc.DrawRoundedRectangle(Ink("Brush.Background"), null, box, 3, 3);
            dc.DrawText(text, new Point(box.X + 4, box.Y));
        }
    }

    /// <summary>
    /// Draws the dots coming out from the address: filled for what the address paid, hollow for what it received, in
    /// the counterparty's group's colour, larger the more moved; the accent ring when chosen; a count when grouped;
    /// the sonar and the corner marks once they are out.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="view">The view.</param>
    /// <param name="centre">The view's centre.</param>
    /// <param name="k">How far the dots have come out.</param>
    private void DrawDots(DrawingContext dc, HeldBackViewModel view, GraphPoint centre, double k)
    {
        var background = Ink("Brush.Background");
        var accent = PenOf(Ink("Brush.Accent"), 1.5);
        var visible = new Rect(0, 0, ActualWidth, ActualHeight);
        for (var index = 0; index < view.Dots.Count; index++)
        {
            var dot = view.Dots[index];
            var arrived = HeldBackMotion.DotArrival(index, view.Dots.Count, k);
            var at = At(centre, HeldBackMotion.Lerp(new GraphPoint(0, 0), dot.At, arrived));
            if (!visible.Contains(at))
            {
                continue;
            }

            var ink = InkOf(HeldBackChoice.InkOf(dot.First.Group));
            var size = dot.Size + (view.Hovered == index ? DotHoverGrowth : 0);
            dc.PushOpacity(arrived);
            if (arrived > 0.9)
            {
                DrawSonar(dc, dot, at, index);
            }

            DrawDotBody(dc, dot, ink, background, at, size);
            if (view.IsChosen(dot))
            {
                dc.DrawEllipse(null, accent, at, (size / 2) + 2.75, (size / 2) + 2.75);
            }

            if (dot.Count > 1)
            {
                var count = Text(dot.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), 10,
                    dot.IsOut ? background : ink, FontWeights.Bold);
                dc.DrawText(count, new Point(at.X - (count.Width / 2), at.Y - (count.Height / 2)));
            }

            if (arrived > 0.9)
            {
                DrawCornerMarks(dc, dot, at, size);
            }

            dc.Pop();
        }
    }

    /// <summary>
    /// Draws a dot: filled for a payment out, hollow for one in; an uncertain amount (the address's share of a
    /// transaction others paid into too) has a dashed ring and a faint fill.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="dot">The dot.</param>
    /// <param name="ink">Its group's colour.</param>
    /// <param name="background">The surface's colour, inside a hollow dot.</param>
    /// <param name="at">Its centre on screen.</param>
    /// <param name="size">Its diameter.</param>
    private void DrawDotBody(
        DrawingContext dc, HeldBackDot dot, Brush ink, Brush background, Point at, double size)
    {
        if (!dot.IsUncertain)
        {
            dc.DrawEllipse(dot.IsOut ? ink : background, PenOf(ink, 1.5), at, size / 2, size / 2);
            return;
        }

        var ring = PenOf(ink, 1.5, UncertainDash);
        dc.DrawEllipse(background, null, at, size / 2, size / 2);
        if (dot.IsOut)
        {
            dc.PushOpacity(UncertainFill);
            dc.DrawEllipse(ink, null, at, size / 2, size / 2);
            dc.Pop();
        }

        dc.DrawEllipse(null, ring, at, size / 2, size / 2);
    }

    /// <summary>
    /// Draws a dot's sonar (sketch EX2): red for a negative label, the accent for an amount near the traced one; a wave
    /// that swells and fades, each dot a little out of step, and a still ring when animations are off.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="dot">The dot.</param>
    /// <param name="at">Its centre on screen.</param>
    /// <param name="index">Its place, which sets its step.</param>
    private void DrawSonar(DrawingContext dc, HeldBackDot dot, Point at, int index)
    {
        var inks = new List<string>();
        if (dot.IsNegative)
        {
            inks.Add("Brush.Kind.Risk");
        }

        if (dot.IsNear)
        {
            inks.Add("Brush.Accent");
        }

        var seconds = (DateTime.UtcNow - _blastStart).TotalSeconds;
        foreach (var (key, order) in inks.Select((key, order) => (key, order)))
        {
            var offset = ((index % 7) * 0.31) + (order * 0.43);
            var phase = (seconds + offset) / HeldBackMotion.SonarTime.TotalSeconds % 1;
            var (scale, opacity) = AnimationsOn ? HeldBackMotion.Sonar(phase) : (1.45, 0.6);
            var radius = (dot.Size + 6) / 2 * scale;
            dc.PushOpacity(opacity);
            dc.DrawEllipse(null, PenOf(Ink(key), 1.5), at, radius, radius);
            dc.Pop();
        }
    }

    /// <summary>
    /// Draws a dot's corner marks: a small hollow ring when the counterparty is already in the graph, a diamond when
    /// its label came from another case.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="dot">The dot.</param>
    /// <param name="at">Its centre on screen.</param>
    /// <param name="size">Its diameter.</param>
    private void DrawCornerMarks(DrawingContext dc, HeldBackDot dot, Point at, double size)
    {
        var corner = new Point(at.X + (size * 0.42), at.Y - (size * 0.42));
        if (dot.First.InGraph)
        {
            dc.DrawEllipse(
                Ink("Brush.Background"),
                PenOf(Ink("Brush.Text"), 1.5),
                corner,
                CornerMark / 2,
                CornerMark / 2);
            corner.Offset(CornerMark + 1, 0);
        }

        if (dot.First.FromOtherCase)
        {
            var half = CornerMark / 2;
            var diamond = new StreamGeometry();
            using (var context = diamond.Open())
            {
                context.BeginFigure(new Point(corner.X, corner.Y - half), true, true);
                context.PolyLineTo(
                    [new Point(corner.X + half, corner.Y), new Point(corner.X, corner.Y + half),
                        new Point(corner.X - half, corner.Y)],
                    true,
                    true);
            }

            diamond.Freeze();
            dc.DrawGeometry(Ink("Brush.TextMuted"), null, diamond);
        }
    }

    /// <summary>Draws the address in the middle (sketch EX2): its icon, its name and what was held back.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="centre">The view's centre.</param>
    /// <param name="line">The second line.</param>
    private void DrawCentreCard(DrawingContext dc, GraphPoint centre, string line)
    {
        var node = _model?.Scene?.Node(_blastAnchor);
        var title = node?.Subject ?? string.Empty;
        var name = Text(title, 12.5, Ink("Brush.Text"), FontWeights.SemiBold);
        var sub = Text(line, 11, Ink("Brush.TextDim"), FontWeights.Normal);
        var width = Math.Max(CentreCardWidth, NodePadding + IconBox + NodeGap + Math.Max(name.Width, sub.Width) + 12);
        var card = new Rect(centre.X - (width / 2), centre.Y - (CentreCardHeight / 2), width, CentreCardHeight);
        dc.DrawRoundedRectangle(
            Ink("Brush.SurfaceRaised"),
            PenOf(Ink("Brush.BorderHover"), 1),
            card,
            NodeRadius,
            NodeRadius);
        var look = node?.Look ?? VisualLanguage.Of(NodeSort.Unknown);
        var iconCorner = new Point(card.X + NodePadding, card.Y + ((card.Height - IconBox) / 2));
        DrawSmallIcon(dc, look, InkOf(look.Ink), iconCorner, IconBox, IconSize);
        var left = iconCorner.X + IconBox + NodeGap;
        var top = card.Y + ((card.Height - name.Height - sub.Height + 2) / 2);
        dc.DrawText(name, new Point(left, top));
        dc.DrawText(sub, new Point(left, top + name.Height - 2));
    }

    /// <summary>Draws the ring being drawn, dashed in the accent with a faint fill.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="centre">The view's centre.</param>
    private void DrawLasso(DrawingContext dc, GraphPoint centre)
    {
        if (_lasso is not { Count: > 1 } ring)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(At(centre, ring[0]), true, true);
            context.PolyLineTo([.. ring.Skip(1).Select(point => At(centre, point))], true, true);
        }

        geometry.Freeze();
        var accent = Ink("Brush.Accent");
        dc.PushOpacity(0.06);
        dc.DrawGeometry(accent, null, geometry);
        dc.Pop();
        dc.DrawGeometry(null, PenOf(accent, 1.5, LassoDash, join: PenLineJoin.Round), geometry);
    }

    /// <summary>
    /// Draws a dot's tip above it (sketch EX2): what moved and to or from whom, when, and why it is marked, each mark
    /// in its colour.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="view">The view.</param>
    /// <param name="dot">The dot.</param>
    /// <param name="centre">The view's centre.</param>
    private void DrawDotTip(DrawingContext dc, HeldBackViewModel view, HeldBackDot dot, GraphPoint centre)
    {
        var (head, sub, lines) = view.TipOf(dot);
        var texts = new List<(FormattedText Text, Brush? Dot)>
        {
            (Text(head, 12, Ink("Brush.Text"), FontWeights.Normal), null),
            (Text(sub, 11, Ink("Brush.TextDim"), FontWeights.Normal), null),
        };
        texts.AddRange(lines.Select(line =>
        {
            var ink = InkOf(line.Ink);
            return (Text(line.Text, 11, ink, FontWeights.Normal), (Brush?)ink);
        }));
        const double Pad = 10;
        const double DotRoom = 12;
        var width = texts.Max(one => one.Text.Width + (one.Dot is null ? 0 : DotRoom)) + (2 * Pad);
        var height = texts.Sum(one => one.Text.Height + 3) + 11;
        var at = At(centre, dot.At);
        var box = new Rect(at.X - (width / 2), at.Y - (dot.Size / 2) - 8 - height, width, height);
        box.X = Math.Clamp(box.X, 4, Math.Max(4, ActualWidth - width - 4));
        box.Y = Math.Max(4, box.Y);
        dc.DrawRoundedRectangle(
            Ink("Brush.SurfaceRaised"),
            PenOf(Ink("Brush.BorderStrong"), 1),
            box,
            7,
            7);
        var y = box.Y + 7;
        foreach (var (text, mark) in texts)
        {
            var x = box.X + Pad;
            if (mark is not null)
            {
                dc.DrawEllipse(mark, null, new Point(x + 3, y + (text.Height / 2)), 3, 3);
                x += DotRoom;
            }

            dc.DrawText(text, new Point(x, y));
            y += text.Height + 3;
        }
    }

    /// <summary>Takes a press while the selection view is open: the start of a click or of a drawn ring.</summary>
    /// <param name="at">The press on screen.</param>
    /// <returns><see langword="true"/> when the view took it.</returns>
    private bool PressHeldBack(GraphPoint at)
    {
        if (_blast == BlastPhase.None)
        {
            return false;
        }

        if (_model?.HeldBack is { IsLasso: true } && _blast == BlastPhase.View)
        {
            _lasso = [Relative(at)];
        }

        _viewPressed = true;
        CaptureMouse();
        return true;
    }

    /// <summary>Moves the pointer in the view: draws the ring on, or lights the dot under it.</summary>
    /// <param name="at">The pointer on screen.</param>
    /// <returns><see langword="true"/> when the view took it.</returns>
    private bool MoveHeldBack(GraphPoint at)
    {
        if (_blast == BlastPhase.None)
        {
            return false;
        }

        if (_model?.HeldBack is not { } view || _blast != BlastPhase.View)
        {
            return true;
        }

        if (_lasso is not null)
        {
            _lasso.Add(Relative(at));
            Redraw();
            return true;
        }

        var hit = view.DotAt(Relative(at));
        view.Hover(hit);
        Cursor = view.IsLasso ? Cursors.Cross : HandOver(hit);
        return true;
    }

    /// <summary>Gives the hand over a dot, else the arrow.</summary>
    /// <param name="hit">The dot, or −1.</param>
    /// <returns>The cursor.</returns>
    private static Cursor? HandOver(int hit) => hit >= 0 ? Cursors.Hand : null;

    /// <summary>Lets go in the view: a drawn ring chooses what is inside it, a click the dot under it.</summary>
    /// <param name="at">Where it was let go, on screen.</param>
    /// <returns><see langword="true"/> when the view took it.</returns>
    private bool ReleaseHeldBack(GraphPoint at)
    {
        if (!_viewPressed)
        {
            return _blast != BlastPhase.None;
        }

        _viewPressed = false;
        ReleaseMouseCapture();
        var ring = _lasso;
        _lasso = null;
        if (_model?.HeldBack is not { } view || _blast != BlastPhase.View)
        {
            return true;
        }

        if (ring is { Count: > LassoPoints })
        {
            view.Lasso(ring);
        }
        else
        {
            view.Click(view.DotAt(Relative(at)), (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
        }

        Redraw();
        return true;
    }

    /// <summary>The view's keys: Esc closes it, Enter picks the chosen out, L draws rings.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when the view took it.</returns>
    private bool HeldBackKey(Key key)
    {
        // Esc while the history is still read stops the read (the user's decision 2026-11-08).
        if (key == Key.Escape && _model?.StopHeldBackRead() == true)
        {
            return true;
        }

        if (_model?.HeldBack is not { } view)
        {
            return false;
        }

        switch (key)
        {
            case Key.Escape:
                view.Cancel();
                return true;
            case Key.Enter:
                view.Take();
                return true;
            case Key.L:
                view.UseLasso();
                return true;
            default:
                return true;
        }
    }

    /// <summary>Gives a screen point relative to the view's centre.</summary>
    /// <param name="at">The point on screen.</param>
    /// <returns>The point relative to the centre.</returns>
    private GraphPoint Relative(GraphPoint at)
    {
        var centre = ViewCentre;
        return new GraphPoint(at.X - centre.X, at.Y - centre.Y);
    }

    /// <summary>Gives a point relative to the view's centre on screen.</summary>
    /// <param name="centre">The centre.</param>
    /// <param name="point">The point relative to it.</param>
    /// <returns>The point on screen.</returns>
    private static Point At(GraphPoint centre, GraphPoint point) => new(centre.X + point.X, centre.Y + point.Y);
}
