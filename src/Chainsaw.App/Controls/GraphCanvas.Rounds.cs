using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.App.Controls;

/// <summary>
/// Round four's marks on the nodes: the quiet ring of a conclusion that is held back (sketch HB1a), and what leaves
/// or comes back to a cluster while a withdrawal is being confirmed (sketch KV1a).
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>How far under the node the ghost of what leaves or comes back stands (sketch KV1a).</summary>
    private const double GhostGap = 34;

    /// <summary>The ghost's height.</summary>
    private const double GhostHeight = 40;

    /// <summary>The ghost's width.</summary>
    private const double GhostWidth = 150;

    /// <summary>The dashes of what is uncertain or only previewed.</summary>
    private static readonly DashStyle GhostDash = Freezing.Frozen(new DashStyle([3, 2.5], 0));

    /// <summary>The dots of the line from a cluster to what leaves it or comes back.</summary>
    private static readonly DashStyle GhostLineDots = Freezing.Frozen(new DashStyle([1, 3], 0));

    /// <summary>
    /// Draws round four's marks on a node: the held back ring in the lower corner, and the ghost of a withdrawal's
    /// preview under it.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="node">The node.</param>
    /// <param name="place">Its top-left corner.</param>
    private void DrawRoundMarks(DrawingContext dc, NodeView node, GraphPoint place)
    {
        if (_model!.HeldBackOf(node) is not null)
        {
            DrawHeldMark(dc, HeldMarkCentre(node, place));
        }

        if (_model.ClusterWhy.ChangeOf(node) is { } change)
        {
            DrawGhost(dc, node, place, change);
        }
    }

    /// <summary>Gives the centre of a node's held back ring: the lower right corner, left of an hourglass.</summary>
    /// <param name="node">The node.</param>
    /// <param name="place">Its top-left corner.</param>
    /// <returns>The centre.</returns>
    private Point HeldMarkCentre(NodeView node, GraphPoint place)
    {
        var shift = _model!.IsWaitingNode(node.Key) ? MarkSize + 2 : 0;
        return new Point(
            place.X + node.Width + MarkOverhang - (MarkSize / 2) - shift,
            place.Y + GraphSizes.NodeHeight + MarkOverhang - (MarkSize / 2));
    }

    /// <summary>
    /// Draws the held back ring (sketch HB1a): a dashed yellow ring with a pause, quiet, never blinking.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="centre">The ring's centre.</param>
    private void DrawHeldMark(DrawingContext dc, Point centre)
    {
        var yellow = Ink("Brush.Kind.Spent");
        dc.DrawEllipse(Ink("Brush.Background"), null, centre, MarkSize / 2, MarkSize / 2);
        var ring = (MarkSize / 2) - 1.5;
        dc.DrawEllipse(null, PenOf(yellow, 1.2, GhostDash), centre, ring, ring);
        var corner = new Point(centre.X - (MarkIconSize / 2), centre.Y - (MarkIconSize / 2));
        DrawGlyph(dc, "Icon.Pause", yellow, corner, MarkIconSize);
    }

    /// <summary>
    /// Draws what leaves a cluster or comes back to it under its node (sketch KV1a): a dashed node with the first
    /// address and how many go with it, yellow for leaving and green for coming back, on a dotted line.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="node">The cluster's node.</param>
    /// <param name="place">Its top-left corner.</param>
    /// <param name="change">The cluster's change.</param>
    private void DrawGhost(DrawingContext dc, NodeView node, GraphPoint place, ClusterChange change)
    {
        var leaving = change.Leaving;
        var joining = change.Joining;
        if (leaving.Count == 0 && joining.Count == 0)
        {
            return;
        }

        var grows = leaving.Count == 0;
        var ink = Ink(grows ? "Brush.Success" : "Brush.Kind.Spent");
        var first = grows ? joining[0] : leaving[0];
        var centreX = place.X + (node.Width / 2);
        var top = place.Y + GraphSizes.NodeHeight + GhostGap;
        var line = PenOf(ink, 1, GhostLineDots);
        dc.DrawLine(line, new Point(centreX, place.Y + GraphSizes.NodeHeight), new Point(centreX, top));
        var frame = new Rect(centreX - (GhostWidth / 2), top, GhostWidth, GhostHeight);
        dc.DrawRoundedRectangle(
            Ink("Brush.Background"),
            PenOf(ink, 1.2, GhostDash),
            frame,
            GhostHeight / 2,
            GhostHeight / 2);
        var name = Text(GraphScene.Shorten(first.Value), 13, Ink("Brush.Text"), FontWeights.Normal);
        var sub = Text(GhostWords(change, node.Name), 11, Ink("Brush.TextDim"), FontWeights.Normal);
        dc.DrawText(name, new Point(frame.X + 18, frame.Y + 5));
        dc.DrawText(sub, new Point(frame.X + 18, frame.Y + 5 + name.Height));
    }

    /// <summary>
    /// Writes the ghost's quiet line: <em>+1 in i K5</em> for what comes back, <em>+2 lämnar K1</em> for what leaves.
    /// </summary>
    /// <param name="change">The cluster's change.</param>
    /// <param name="name">The cluster's name.</param>
    /// <returns>The line.</returns>
    private static string GhostWords(ClusterChange change, string name)
    {
        if (change.Leaving.Count == 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"+{change.Joining.Count} in i {name}");
        }

        return change.Leaving.Count == 1
            ? "lämnar " + name
            : string.Create(CultureInfo.InvariantCulture, $"+{change.Leaving.Count - 1} lämnar {name}");
    }

    /// <summary>Gives the node whose held back ring is under the pointer, or <see langword="null"/>.</summary>
    /// <param name="surface">The pointer on the surface.</param>
    /// <returns>The node.</returns>
    private NodeView? HeldMarkAt(GraphPoint surface)
    {
        var model = _model;
        if (model?.Scene is not { } scene)
        {
            return null;
        }

        foreach (var node in scene.Nodes)
        {
            if (model.Places.TryGetValue(node.Key, out var place) && model.HeldBackOf(node) is not null)
            {
                var centre = HeldMarkCentre(node, place);
                var (dx, dy) = (surface.X - centre.X, surface.Y - centre.Y);
                if ((dx * dx) + (dy * dy) <= MarkSize * MarkSize / 4)
                {
                    return node;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gives what hovering a held back ring tells, or <see langword="null"/> when none is under the pointer.
    /// </summary>
    /// <param name="surface">The pointer on the surface.</param>
    /// <returns>The text.</returns>
    private string? HeldTipAt(GraphPoint surface) =>
        HeldMarkAt(surface) is { } node && _model!.HeldBackOf(node) is { } held
            ? string.Join(Environment.NewLine, GraphViewModel.HeldBackLines(held))
            : null;

    /// <summary>Takes a press on a held back ring: the node is chosen and its history opens (sketch HB1a).</summary>
    /// <param name="surface">The press on the surface.</param>
    /// <returns><see langword="true"/> when the press was on a ring.</returns>
    private bool PressHeldMark(GraphPoint surface)
    {
        if (HeldMarkAt(surface) is not { } node)
        {
            return false;
        }

        _model!.OpenHeldBack(node.Key);
        return true;
    }
}
