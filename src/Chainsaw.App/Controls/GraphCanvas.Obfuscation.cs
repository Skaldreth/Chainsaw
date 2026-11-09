using System.Windows;
using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// The obfuscation the graph tones down or folds: the poisoning's red mask at full strength on a toned down node
/// (sketch DM1a), and a row of many hops as one line with a dashed pill and a bead per hop after time (sketch HP1a).
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>How faint dust and a look-alike are drawn.</summary>
    private const double DustOpacity = 0.4;

    /// <summary>The pill's height on a folded row.</summary>
    private const double HopPillHeight = 24;

    /// <summary>The beads' radius.</summary>
    /// <summary>The dashes of a row's pill: that the hops belong together is a judgement.</summary>
    private static readonly DashStyle HopPillDash = Freezing.Frozen(new DashStyle([3, 2], 0));

    private const double BeadRadius = 3.2;

    /// <summary>Draws a look-alike's red mask in its corner at full strength, outside the node's toning down.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="node">The node.</param>
    /// <param name="place">Its top-left corner.</param>
    private void DrawPoisonMark(DrawingContext dc, NodeView node, GraphPoint place)
    {
        if (_model!.PoisoningOf(node.Key) is null)
        {
            return;
        }

        var centre = new Point(
            place.X - MarkOverhang + (MarkSize / 2),
            place.Y + GraphSizes.NodeHeight + MarkOverhang - (MarkSize / 2));
        dc.DrawEllipse(
            Ink("Brush.Kind.Risk"),
            PenOf(Ink("Brush.Background"), 2),
            centre,
            MarkSize / 2,
            MarkSize / 2);
        var corner = new Point(centre.X - (MarkIconSize / 2), centre.Y - (MarkIconSize / 2));
        DrawGlyph(dc, "Icon.Poison", Ink("Brush.Mark.OnSanctioned"), corner, MarkIconSize);
    }

    /// <summary>
    /// Draws every row of many hops: folded as one line with its pill and beads, unfolded with Fäll ihop.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    private void DrawHopRows(DrawingContext dc)
    {
        var model = _model!;
        foreach (var row in model.HopRows)
        {
            if (!model.Places.TryGetValue(row.First, out var first) || !model.Places.TryGetValue(row.Last, out var last)
                || model.Scene?.Node(row.First) is not { } from)
            {
                continue;
            }

            var start = new Point(first.X + from.Width, first.Y + (GraphSizes.NodeHeight / 2));
            var end = new Point(last.X, last.Y + (GraphSizes.NodeHeight / 2));
            if (model.IsUnfolded(row.Key))
            {
                DrawHopPill(dc, HopPillAt(start, end, unfolded: true), "Fäll ihop · " + row.Text, folded: false);
                continue;
            }

            DrawHopLine(dc, row, start, end);
        }
    }

    /// <summary>Draws a folded row's line, its beads after time and its dashed pill in the middle.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="row">The row.</param>
    /// <param name="start">Where the line leaves the first node.</param>
    /// <param name="end">Where it reaches the last.</param>
    private void DrawHopLine(DrawingContext dc, HopRow row, Point start, Point end)
    {
        var ink = Ink("Brush.Accent");
        dc.DrawLine(PenOf(ink, 2.2), start, end);
        foreach (var bead in row.Beads)
        {
            var at = new Point(start.X + ((end.X - start.X) * bead), start.Y + ((end.Y - start.Y) * bead));
            dc.DrawEllipse(Ink("Brush.Background"), PenOf(ink, 1.4), at, BeadRadius, BeadRadius);
        }

        DrawHopPill(dc, HopPillAt(start, end, unfolded: false), row.Text, folded: true);
    }

    /// <summary>Gives where a row's pill stands: on the middle of its line, or above it while it is unfolded.</summary>
    /// <param name="start">The line's start.</param>
    /// <param name="end">The line's end.</param>
    /// <param name="unfolded">Whether the row is unfolded.</param>
    /// <returns>The pill's centre.</returns>
    private static Point HopPillAt(Point start, Point end, bool unfolded) =>
        new((start.X + end.X) / 2, ((start.Y + end.Y) / 2) - (unfolded ? GraphSizes.NodeHeight * 1.6 : 0));

    /// <summary>Draws a row's pill: dashed, since that the hops belong together is a judgement (principle 2).</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="centre">The pill's centre.</param>
    /// <param name="text">Its words.</param>
    /// <param name="folded">Whether the row is folded: the hops' icon goes first.</param>
    private void DrawHopPill(DrawingContext dc, Point centre, string text, bool folded)
    {
        var words = Text(text, 11.5, Ink("Brush.Text"), FontWeights.Normal);
        var icon = folded ? MarkIconSize + 6 : 0;
        var frame = HopPillFrame(centre, words.Width + icon);
        dc.DrawRoundedRectangle(
            Ink("Brush.Surface"),
            PenOf(Ink("Brush.TextDim"), 1, HopPillDash),
            frame,
            HopPillHeight / 2,
            HopPillHeight / 2);
        if (folded)
        {
            DrawGlyph(dc, "Icon.Layout.Hops", Ink("Brush.TextMuted"),
                new Point(frame.X + 10, centre.Y - (MarkIconSize / 2)), MarkIconSize);
        }

        dc.DrawText(words, new Point(frame.X + 10 + icon, centre.Y - (words.Height / 2)));
    }

    /// <summary>Gives a pill's frame around its words.</summary>
    /// <param name="centre">The pill's centre.</param>
    /// <param name="width">The words' width.</param>
    /// <returns>The frame.</returns>
    private static Rect HopPillFrame(Point centre, double width) =>
        new(centre.X - ((width + 20) / 2), centre.Y - (HopPillHeight / 2), width + 20, HopPillHeight);

    /// <summary>Gives the row whose pill is under the pointer, or <see langword="null"/>.</summary>
    /// <param name="surface">The pointer on the surface.</param>
    /// <returns>The row.</returns>
    private HopRow? HopPillAt(GraphPoint surface)
    {
        var model = _model;
        if (model?.Scene is null)
        {
            return null;
        }

        foreach (var row in model.HopRows)
        {
            if (!model.Places.TryGetValue(row.First, out var first) || !model.Places.TryGetValue(row.Last, out var last)
                || model.Scene.Node(row.First) is not { } from)
            {
                continue;
            }

            var start = new Point(first.X + from.Width, first.Y + (GraphSizes.NodeHeight / 2));
            var end = new Point(last.X, last.Y + (GraphSizes.NodeHeight / 2));
            var centre = HopPillAt(start, end, model.IsUnfolded(row.Key));
            var frame = new Rect(centre.X - 70, centre.Y - (HopPillHeight / 2), 140, HopPillHeight);
            if (frame.Contains(new Point(surface.X, surface.Y)))
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>Gives what hovering a row's pill tells: the hops with their waits, or <see langword="null"/>.</summary>
    /// <param name="surface">The pointer on the surface.</param>
    /// <returns>The text.</returns>
    private string? HopTipAt(GraphPoint surface) => HopPillAt(surface) is { } row
        ? string.Join(Environment.NewLine, [row.Text + " · pass-through v1", .. row.Lines, "Klicka för att fälla ut"])
        : null;

    /// <summary>Takes a press on a row's pill: the row folds out to its nodes, or back into one line.</summary>
    /// <param name="surface">The press on the surface.</param>
    /// <returns><see langword="true"/> when the press was on a pill.</returns>
    private bool PressHopPill(GraphPoint surface)
    {
        if (HopPillAt(surface) is not { } row)
        {
            return false;
        }

        _model!.ToggleHopRow(row.Key);
        return true;
    }
}
