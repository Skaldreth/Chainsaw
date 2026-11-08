using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Checks.Contracts;
using static Chainsaw.App.Controls.Freezing;

namespace Chainsaw.App.Controls;

/// <summary>
/// The surface's areas behind the actors (sketch KF1b), the dashed lines to a mixer's candidates and the actors chosen
/// for an own area: drawn only where they are on screen (principle 6).
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>How strong an area's fill is.</summary>
    private const double AreaFillOpacity = 0.06;

    /// <summary>How strong the chosen area's fill is.</summary>
    private const double ChosenFillOpacity = 0.1;

    /// <summary>How strong an area's edge is.</summary>
    private const double AreaEdgeOpacity = 0.35;

    /// <summary>The size of the chip's icon.</summary>
    private const double ChipIconSize = 11;

    /// <summary>The dashes of a suggestion's area.</summary>
    private static readonly DashStyle AreaDash = Frozen(new DashStyle([5, 4], 0));

    /// <summary>The dots of a time area.</summary>
    private static readonly DashStyle AreaDots = Frozen(new DashStyle([0.1, 3.5], 0));

    /// <summary>The dashes of a candidate's line.</summary>
    private static readonly DashStyle CandidateDash = Frozen(new DashStyle([4, 3.5], 0));

    /// <summary>The areas as last drawn, for the pointer.</summary>
    private IReadOnlyList<AreaPlace> _areas = [];

    /// <summary>Draws the areas' fills and edges behind the lines and the actors.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="visible">The visible part of the surface.</param>
    private void DrawAreas(DrawingContext dc, GraphRect visible)
    {
        var model = _model!;
        _areas = AreaGeometry.Place(model.Checks.Areas, model.Places, model.FrameAt);
        foreach (var place in _areas.Where(place => place.Frame.Intersects(visible)))
        {
            var tint = Ink(AreaLooks.BrushKey(place.Area.Look.Tint));
            var chosen = model.Checks.ChosenArea == place.Area.Key || model.Checks.Selected?.Key == place.Area.CheckKey;
            var rect = new Rect(place.Frame.X, place.Frame.Y, place.Frame.Width, place.Frame.Height);
            if (place.Area.IsFilled)
            {
                dc.PushOpacity(chosen ? ChosenFillOpacity : AreaFillOpacity);
                dc.DrawRoundedRectangle(tint, null, rect, AreaGeometry.Radius, AreaGeometry.Radius);
                dc.Pop();
            }

            var dash = place.Area.Line switch
            {
                AreaLine.Dashed => AreaDash,
                AreaLine.Dotted => AreaDots,
                _ => null,
            };
            var pen = PenOf(tint, chosen ? 1.5 : 1, dash, PenLineCap.Round);
            dc.PushOpacity(chosen ? 0.8 : AreaEdgeOpacity);
            dc.DrawRoundedRectangle(null, pen, rect, AreaGeometry.Radius, AreaGeometry.Radius);
            dc.Pop();
        }
    }

    /// <summary>
    /// Draws the dashed lines to a mixer's candidates: a ranked candidate has its number at its node (sketch KF2b), and
    /// one without a rank the score in the middle of its line.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="visible">The visible part of the surface.</param>
    private void DrawCandidates(DrawingContext dc, GraphRect visible)
    {
        var model = _model!;
        var accent = Ink("Brush.Accent");
        foreach (var line in model.Checks.CandidateLines)
        {
            if (model.CurveBetween(line.From, line.To) is not { } curve
                || !curve.Bounds().Inflate(GraphSizes.MarkReach).Intersects(visible))
            {
                continue;
            }

            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(ToPoint(curve.Start), false, false);
                context.BezierTo(ToPoint(curve.Control1), ToPoint(curve.Control2), ToPoint(curve.End), true, true);
            }

            geometry.Freeze();
            var pen = PenOf(accent, 1 + (2 * line.Score), CandidateDash, PenLineCap.Round);
            dc.PushOpacity(line.IsApproved || line.IsChosen ? 1 : 0.35 + (0.65 * line.Score));
            dc.DrawGeometry(null, pen, geometry);
            if (line.Rank > 0)
            {
                DrawCandidateMark(dc, line, model.FrameAt(line.To, model.Places[line.To]), accent);
            }
            else
            {
                DrawScore(dc, line, ToPoint(curve.Middle), accent);
            }

            dc.Pop();
        }
    }

    /// <summary>
    /// Draws a candidate's mark at its node (sketch KF2b): the rank in a disc on the node's top-left corner, as the
    /// panel's table numbers it, filled in accent for the chosen one; once the user has approved it, a green check in
    /// the top-right corner, after the node's other marks. The line stays dashed all the same: an approved candidate is
    /// the user's judgement, never a fact (principle 2).
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="line">The candidate's line.</param>
    /// <param name="node">The candidate's frame.</param>
    /// <param name="accent">The accent.</param>
    private void DrawCandidateMark(DrawingContext dc, CandidateLine line, GraphRect node, Brush accent)
    {
        const double Radius = 10;
        const double Inset = 2;
        var ground = Ink("Brush.Background");
        if (line.IsApproved)
        {
            const double CheckRadius = MarkSize / 2;
            var marks = CornerMarks(line.To);
            var right = node.Right + MarkOverhang - CheckRadius - (marks * (MarkSize + 2));
            var at = new Point(right, node.Y - MarkOverhang + CheckRadius);
            dc.DrawEllipse(Ink("Brush.Success"), PenOf(ground, 2), at, CheckRadius, CheckRadius);
            DrawGlyph(dc, "Icon.Check", ground, new Point(at.X - (MarkIconSize / 2), at.Y - (MarkIconSize / 2)),
                MarkIconSize);
            return;
        }

        var centre = new Point(node.X + Inset, node.Y + Inset);
        var edge = line.IsChosen ? accent : Ink("Brush.TextMuted");
        dc.DrawEllipse(line.IsChosen ? accent : Ink("Brush.Surface"), PenOf(edge, 1.5), centre,
            Radius, Radius);
        var text = Text(
            line.Rank.ToString(CultureInfo.InvariantCulture), 11, Ink("Brush.Text"),
            FontWeights.SemiBold);
        dc.DrawText(text, new Point(centre.X - (text.Width / 2), centre.Y - (text.Height / 2)));
    }

    /// <summary>Gives how many marks already stand in a node's top-right corner: its badges and a watch's eye.</summary>
    /// <param name="nodeKey">The node.</param>
    /// <returns>How many.</returns>
    private int CornerMarks(string nodeKey)
    {
        var model = _model!;
        var badges = model.Scene?.Node(nodeKey) is { } node ? model.MarksOf(node).Count : 0;
        return badges + (model.WatchOverlay.MarkOf(nodeKey) is null ? 0 : 1);
    }

    /// <summary>Writes a candidate's score in a small dashed box in the middle of its line.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="line">The candidate's line.</param>
    /// <param name="middle">The line's middle.</param>
    /// <param name="accent">The accent.</param>
    private void DrawScore(DrawingContext dc, CandidateLine line, Point middle, Brush accent)
    {
        var text = Text(line.ScoreText, 10.5, accent, FontWeights.SemiBold);
        var box = new Rect(
            middle.X - (text.Width / 2) - 5, middle.Y - (text.Height / 2) - 1, text.Width + 10, text.Height + 2);
        dc.DrawRoundedRectangle(
            Ink("Brush.Background"), PenOf(accent, 1, CandidateDash), box, 4, 4);
        dc.DrawText(text, new Point(box.X + 5, box.Y + 1));
    }

    /// <summary>
    /// Draws the areas' chips on top of the actors: the icon, a person on an own area, the name when chosen, and the
    /// pen and the bin beside a chosen own area.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="visible">The visible part of the surface.</param>
    private void DrawAreaChips(DrawingContext dc, GraphRect visible)
    {
        var model = _model!;
        var ground = Ink("Brush.Background");
        foreach (var place in _areas.Where(place => place.Frame.Intersects(visible)))
        {
            var tint = Ink(AreaLooks.BrushKey(place.Area.Look.Tint));
            var chip = place.Chip;
            var chosen = model.Checks.ChosenArea == place.Area.Key || model.Checks.Selected?.Key == place.Area.CheckKey;
            var rect = new Rect(chip.X, chip.Y, chip.Width, chip.Height);
            var edge = PenOf(tint, 1, place.Area.Line == AreaLine.Dashed ? AreaDash : null);
            dc.DrawRoundedRectangle(ground, edge, rect, chip.Height / 2, chip.Height / 2);
            var iconTop = chip.Y + ((chip.Height - ChipIconSize) / 2);
            DrawGlyph(dc, place.Area.Look.Icon, tint, new Point(chip.X + 6, iconTop), ChipIconSize);
            if (place.Area.IsOwn)
            {
                DrawGlyph(
                    dc, "Icon.Kind.Person", Ink("Brush.Text"),
                    new Point(chip.X + 6 + ChipIconSize + 1, iconTop + 2), 8);
            }

            if (chosen)
            {
                DrawAreaName(dc, place, tint);
            }

            if (chosen && place.Area.IsOwn)
            {
                DrawTool(dc, place.RenameButton, "Icon.Pen");
                DrawTool(dc, place.RemoveButton, "Icon.Trash");
            }
        }
    }

    /// <summary>Writes a chosen area's name to the left of its chip.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="place">The area.</param>
    /// <param name="tint">Its tint.</param>
    private void DrawAreaName(DrawingContext dc, AreaPlace place, Brush tint)
    {
        var text = Text(place.Area.Label, 11, Ink("Brush.Text"), FontWeights.Normal);
        var box = new Rect(
            place.Chip.X - text.Width - 16, place.Chip.Y, text.Width + 12, place.Chip.Height);
        dc.DrawRoundedRectangle(
            Ink("Brush.Background"), PenOf(tint, 1), box, box.Height / 2, box.Height / 2);
        dc.DrawText(text, new Point(box.X + 6, box.Y + ((box.Height - text.Height) / 2)));
    }

    /// <summary>Draws a tool beside a chosen own area's chip.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="where">The tool's square.</param>
    /// <param name="icon">Its icon's resource key.</param>
    private void DrawTool(DrawingContext dc, GraphRect where, string icon)
    {
        var rect = new Rect(where.X, where.Y, where.Width, where.Height);
        dc.DrawRoundedRectangle(
            Ink("Brush.SurfaceRaised"),
            PenOf(Ink("Brush.BorderStrong"), 1),
            rect,
            where.Height / 2,
            where.Height / 2);
        DrawGlyph(
            dc, icon, Ink("Brush.TextMuted"),
            new Point(where.X + ((where.Width - ChipIconSize) / 2), where.Y + ((where.Height - ChipIconSize) / 2)),
            ChipIconSize);
    }

    /// <summary>Handles a press on an area's chip or tool, before the actors are asked.</summary>
    /// <param name="surface">The press on the surface.</param>
    /// <returns><see langword="true"/> when a chip or a tool took it.</returns>
    private bool PressArea(GraphPoint surface)
    {
        var checks = _model!.Checks;
        if (AreaGeometry.HitTest(_areas, surface, checks.ChosenArea) is not { } hit)
        {
            return false;
        }

        switch (hit.Part)
        {
            case AreaPart.Rename:
                checks.OpenRename(hit.Area.Key);
                break;
            case AreaPart.Remove:
                checks.OpenRemove(hit.Area.Key);
                break;
            default:
                checks.ChooseArea(checks.ChosenArea == hit.Area.Key ? null : hit.Area.Key);
                break;
        }

        return true;
    }

    /// <summary>Fits the given actors to the screen, as a check's <em>Visa i grafen</em> asks.</summary>
    /// <param name="nodes">The actors.</param>
    private void FitTo(IReadOnlyList<string> nodes)
    {
        var frames = nodes
            .Where(_model!.Places.ContainsKey)
            .Select(node => _model.FrameAt(node, _model.Places[node]))
            .ToList();
        if (frames.Count == 0)
        {
            return;
        }

        var bounds = frames.Skip(1).Aggregate(frames[0], (all, frame) => all.Union(frame));
        DropZoom();
        _viewport = Viewport.Fit(bounds.Inflate(AreaGeometry.TimePadding), ActualWidth, ActualHeight);
        _model.ViewportChanged(_viewport);
        InvalidateVisual();
    }

    /// <summary>
    /// The keys of the checks and areas, before the graph's own: Esc closes the area form, the menu and the panel in
    /// that order; with a suggestion shown J puts it later in the queue, K steps back, Enter approves and X dismisses,
    /// and in the overview Enter starts going through the queue (sketch KF2b); Z draws an own area around the chosen
    /// actors (sketch KF1b). The question mark comes as text (<see cref="OnTextInput"/>); Ctrl+Z is the page's.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when it was the checks' key.</returns>
    private bool CheckKey(Key key)
    {
        var model = _model!;
        var checks = model.Checks;
        switch (key)
        {
            case Key.Escape when checks.IsFormOpen:
                checks.CancelForm();
                return true;
            case Key.Escape when checks.IsMenuOpen:
                checks.ToggleMenu();
                return true;
            case Key.Escape when checks.IsOpen:
                checks.Close();
                return true;
            case Key.Escape when checks.Picked.Count > 0 || checks.ChosenArea is not null:
                checks.ClearPicked();
                checks.ChooseArea(null);
                return true;
            case Key.J when checks.IsOpen && checks.IsReview:
                checks.Later();
                return true;
            case Key.K when checks.IsOpen && checks.IsReview:
                checks.Previous();
                return true;
            case Key.Enter when checks.IsOpen && !checks.IsSettled:
                _ = checks.ApproveAsync();
                return true;
            case Key.X when checks.IsOpen && checks.IsReview:
                _ = checks.DismissAsync();
                return true;
            case Key.Z:
                checks.OpenCreate(model.SelectedNode);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Says whether a key is left for the text it makes: on a Swedish keyboard the question mark is Shift and plus,
    /// which must not zoom while the list is open.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when it is left for <see cref="OnTextInput"/>.</returns>
    private bool LeftForText(Key key) =>
        key is Key.OemPlus && _model?.Checks.IsOpen == true && (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

    /// <summary>
    /// The question mark asks <em>Varför?</em> about the suggestion shown while the panel is open (sketch KF2b).
    /// </summary>
    /// <param name="e">The text.</param>
    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnTextInput(e);
        if (e.Text == "?" && _model?.Checks.IsOpen == true)
        {
            _model.Checks.AskWhy();
            e.Handled = true;
        }
    }
}
