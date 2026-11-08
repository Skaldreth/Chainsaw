using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// The trash on the graph's surface (sketch SO1a): a short dashed stub with <em>i soptunnan</em> where a line went to
/// something thrown, and, while a throw waits for the warning to be answered, a dashed frame around what would follow
/// with it while the rest is faint. Never red, which means risk.
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>How long a stub is, in the surface's units.</summary>
    private const double StubLength = 54;

    /// <summary>How thick a stub is.</summary>
    private const double StubWidth = 1.5;

    /// <summary>The stub label's size.</summary>
    private const double StubLabelSize = 11;

    /// <summary>The gap between a stub's end and its label.</summary>
    private const double StubLabelGap = 5;

    /// <summary>How far the dashed frame around a pending member stands outside the node, in screen pixels.</summary>
    private const double PendingInset = 4;

    /// <summary>How long a watch's hit into the trash is: longer than a stub, so that its amount and rings fit.</summary>
    private const double TrashHitLength = 110;

    /// <summary>The dashes of a stub (sketch SO1a: 3 on, 5 off).</summary>
    private static readonly DashStyle StubDash = Frozen(new DashStyle([2, 3.3], 0));

    /// <summary>The dashes of the frame around what would follow with a throw.</summary>
    private static readonly DashStyle PendingDash = Frozen(new DashStyle([4, 3.3], 0));

    /// <summary>
    /// Raised while a node is dragged, with the pointer in this control's coordinates, so that the page can tell the
    /// bin how near it is.
    /// </summary>
    public event EventHandler<Point>? NodeDragged;

    /// <summary>
    /// The trash's keys, before the others: Enter throws and Esc cancels while the warning is open, Esc closes the
    /// trash's view, and Delete throws what is chosen (also cards chosen in a fanned pile).
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when it was the trash's key.</returns>
    private bool TrashKey(Key key)
    {
        var model = _model!;
        switch (key)
        {
            case Key.Enter when model.HasTrashWarning:
                model.ConfirmThrowCommand.Execute(null);
                return true;
            case Key.Escape when model.HasTrashWarning:
                model.CancelThrow();
                return true;
            case Key.Escape when model.TrashView.IsOpen:
                model.TrashView.Close();
                return true;
            case Key.Delete when !model.TrashView.IsOpen && model.HasChosenToThrow:
                model.ThrowChosen();
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Draws a dragged node, and each actor dragged with it, smaller and a little faint as it nears the bin (sketch
    /// SO1a), shrinking towards the point it is held by, so that it stays under the pointer.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="node">The node.</param>
    /// <param name="place">Its top-left corner.</param>
    /// <param name="near">The nodes near what is chosen.</param>
    /// <returns><see langword="true"/> when it was drawn so; otherwise nothing was drawn.</returns>
    private bool DrawNearBin(DrawingContext dc, NodeView node, GraphPoint place, IReadOnlySet<string> near)
    {
        var model = _model!;
        if (!model.Bin.IsDragging || !_gesture.IsDragging || _gesture.Node is not { } held
            || (node.Key != held && !model.IsInDraggedGroup(node.Key))
            || !model.Places.TryGetValue(held, out var heldAt))
        {
            return false;
        }

        var scale = model.Bin.DraggedScale;
        var grip = new Point(heldAt.X + _gesture.Grip.X, heldAt.Y + _gesture.Grip.Y);
        dc.PushOpacity(model.Bin.DraggedOpacity);
        dc.PushTransform(new ScaleTransform(scale, scale, grip.X, grip.Y));
        DrawNode(dc, node, place, near);
        dc.Pop();
        dc.Pop();
        return true;
    }

    /// <summary>Draws the stubs at the ports of the nodes on screen, in the surface's units.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="visible">The visible part of the surface.</param>
    private void DrawTrashStubs(DrawingContext dc, GraphRect visible)
    {
        if (_model?.Scene is not { TrashStubs.Count: > 0 } scene)
        {
            return;
        }

        var ink = GraphPalette.Find(this, "Brush.TextFaint");
        var pen = new Pen(ink, StubWidth)
        {
            DashStyle = StubDash, DashCap = PenLineCap.Round, StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        foreach (var stub in scene.TrashStubs)
        {
            // A watch's hit into the trash takes the stub's place (DrawTrashHits).
            if (_model.WatchOverlay.HasTrashHit(stub.NodeKey, stub.IsOut)
                || !_model.Places.TryGetValue(stub.NodeKey, out var place)
                || !_model.FrameAt(stub.NodeKey, place).Inflate(StubLength * 2).Intersects(visible))
            {
                continue;
            }

            var y = place.Y + (GraphSizes.NodeHeight / 2);
            var port = stub.IsOut ? place.X + _model.WidthOf(stub.NodeKey) : place.X;
            var end = stub.IsOut ? port + StubLength : port - StubLength;
            dc.DrawLine(pen, new Point(port, y), new Point(end, y));
            // A watch at the node goes on into the trash: the stub says so in the watch's own tone (sketch BV2a).
            var watched = _model.WatchOverlay.MarkOf(stub.NodeKey) is not null;
            var label = watched
                ? Text(TrashStub.WatchedLabel, StubLabelSize, GraphPalette.Find(this, "Brush.Watch"), FontWeights.Normal)
                : Text(TrashStub.Label, StubLabelSize, ink, FontWeights.Normal);
            var x = stub.IsOut ? end + StubLabelGap : end - StubLabelGap - label.Width;
            dc.DrawText(label, new Point(x, y - (label.Height / 2)));
        }
    }

    /// <summary>
    /// Draws each watch's hit into the trash (sketches BV2a and SO1a): a short real line from the watched node's port in
    /// the accent, with the amount on it, rings that move until the hit is seen, and <em>i soptunnan</em> at its end.
    /// Grey when lookups cannot be made.
    /// </summary>
    /// <param name="dc">Where to draw, in the surface's units.</param>
    /// <param name="visible">The visible part of the surface.</param>
    private void DrawTrashHits(DrawingContext dc, GraphRect visible)
    {
        var model = _model!;
        foreach (var hit in model.WatchOverlay.TrashHits)
        {
            if (!model.Places.TryGetValue(hit.NodeKey, out var place)
                || !model.FrameAt(hit.NodeKey, place).Inflate(TrashHitLength * 2).Intersects(visible))
            {
                continue;
            }

            var y = place.Y + (GraphSizes.NodeHeight / 2);
            var port = hit.IsOut ? place.X + GraphSizes.NodeWidth : place.X;
            var end = hit.IsOut ? port + TrashHitLength : port - TrashHitLength;
            var middle = new Point((port + end) / 2, y);
            if (hit.IsNew)
            {
                DrawRings(dc, middle, RingRadius, hit.Idle);
            }

            var ink = GraphPalette.Find(this, hit.Idle ? "Brush.Watch.Idle" : "Brush.Accent");
            var pen = new Pen(ink, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(pen, new Point(port, y), new Point(end, y));
            if (hit.Label is { } amount)
            {
                var text = Text(amount, 11, ink, FontWeights.SemiBold);
                var box = new Rect(
                    middle.X - (text.Width / 2) - 5, y - (text.Height / 2) - 1, text.Width + 10, text.Height + 2);
                dc.DrawRoundedRectangle(GraphPalette.Find(this, "Brush.Background"), null, box, 4, 4);
                dc.DrawText(text, new Point(box.X + 5, box.Y + 1));
            }

            var label = Text(
                TrashStub.Label, StubLabelSize, GraphPalette.Find(this, "Brush.TextFaint"), FontWeights.Normal);
            var x = hit.IsOut ? end + StubLabelGap : end - StubLabelGap - label.Width;
            dc.DrawText(label, new Point(x, y - (label.Height / 2)));
        }
    }

    /// <summary>
    /// Draws a dashed frame around each node that would follow with a throw while its warning is open (sketch SO1a):
    /// the thrown in the bright text colour, the rest in the muted one.
    /// </summary>
    /// <param name="dc">Where to draw, in screen units.</param>
    private void DrawTrashPending(DrawingContext dc)
    {
        if (_model?.TrashWarning is not { } warning)
        {
            return;
        }

        foreach (var row in warning.Rows)
        {
            if (NodeOnScreen(row.Key) is not { } frame)
            {
                continue;
            }

            var ink = GraphPalette.Find(this, row.IsRoot ? "Brush.Text" : "Brush.TextMuted");
            var pen = new Pen(ink, StubWidth) { DashStyle = PendingDash };
            frame.Inflate(PendingInset, PendingInset);
            dc.DrawRoundedRectangle(null, pen, frame, NodeRadius + PendingInset, NodeRadius + PendingInset);
        }
    }
}
