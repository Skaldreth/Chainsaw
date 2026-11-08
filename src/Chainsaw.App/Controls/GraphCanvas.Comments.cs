using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Controls;

/// <summary>
/// The graph's comments (sketch KN1a) on the surface: the notes in a layer the canvas hosts, panned and zoomed with the
/// graph, and their thin dotted threads drawn under the nodes. N begins a note (about the chosen node, beside it), a
/// double click on the empty surface begins one where it is, Delete removes the chosen note, and the dot at a thread's
/// end is dragged to another node, or to the empty surface to untie it.
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>The thread's width, in pixels on the screen (sketch KN1a: 1.5).</summary>
    private const double ThreadWidth = 1.5;

    /// <summary>The thread's dots: a dot every 5 pixels on the screen (sketch KN1a: 0.1 5 with round caps).</summary>
    private static readonly double[] ThreadDotPixels = [0.1, 5];

    /// <summary>The dot at the thread's end at the node, in pixels on the screen (sketch KN1a: 3).</summary>
    private const double ThreadDotRadius = 3;

    /// <summary>How near the thread's end dot a press takes hold of it, in pixels on the screen.</summary>
    private const double ThreadGrip = 8;

    /// <summary>The notes on the surface, once attached.</summary>
    private CommentLayer? _commentLayer;

    /// <summary>
    /// The notes' layer's transform, made once and changed only when the viewport moves, so that a redraw does not
    /// make a new one (BACKLOG, <em>Kommentarsnoden</em>).
    /// </summary>
    private readonly MatrixTransform _commentTransform = new();

    /// <summary>The note whose thread is being dragged, or <see langword="null"/>.</summary>
    private CommentNote? _threadNote;

    /// <summary>Where the dragged thread's end is, on the surface.</summary>
    private GraphPoint _threadEnd;

    /// <inheritdoc/>
    protected override int VisualChildrenCount => _commentLayer is null ? 0 : 1;

    /// <inheritdoc/>
    protected override Visual GetVisualChild(int index) =>
        index == 0 && _commentLayer is not null ? _commentLayer : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>Measures the notes' layer, which takes the whole surface.</summary>
    /// <param name="availableSize">The room.</param>
    /// <returns>The surface's own size.</returns>
    protected override Size MeasureOverride(Size availableSize)
    {
        _commentLayer?.Measure(availableSize);
        return base.MeasureOverride(availableSize);
    }

    /// <summary>Lays the notes' layer over the whole surface.</summary>
    /// <param name="finalSize">The surface's size.</param>
    /// <returns>The same size.</returns>
    protected override Size ArrangeOverride(Size finalSize)
    {
        _commentLayer?.Arrange(new Rect(finalSize));
        return finalSize;
    }

    /// <summary>Puts the notes' layer on the surface and follows the comments.</summary>
    /// <param name="model">The graph.</param>
    private void AttachComments(GraphViewModel model)
    {
        _commentLayer = new CommentLayer(this);
        AddVisualChild(_commentLayer);
        AddLogicalChild(_commentLayer);
        _commentLayer.NoteResized += (_, _) => InvalidateVisual();
        _commentLayer.Attach(model.Comments, model.FollowCommentLinkCommand);
        InvalidateMeasure();
    }

    /// <summary>
    /// Gives the notes' layer the viewport, so that the notes pan and zoom with the graph, and hides them while the
    /// selection view or a fanned pile covers the graph. A note being written when they are hidden lets go of the
    /// keyboard, which goes to the surface, so that no key types into a note that cannot be seen.
    /// </summary>
    private void PlaceComments()
    {
        if (_commentLayer is null)
        {
            return;
        }

        var matrix = new Matrix(_viewport.Zoom, 0, 0, _viewport.Zoom, _viewport.OffsetX, _viewport.OffsetY);
        if (_commentTransform.Matrix != matrix)
        {
            _commentTransform.Matrix = matrix;
        }

        if (!ReferenceEquals(_commentLayer.RenderTransform, _commentTransform))
        {
            _commentLayer.RenderTransform = _commentTransform;
        }

        var shown = _model?.Scene is not null && _blast == BlastPhase.None && _model.Fan is null;
        _commentLayer.Opacity = shown ? 1 : 0;
        _commentLayer.IsHitTestVisible = shown;
        if (!shown && _commentLayer.IsKeyboardFocusWithin)
        {
            // Not during the drawing itself: the focus moves once the frame is drawn.
            Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);
        }
    }

    /// <summary>Draws each note's thread to its node, and the thread being dragged to the pointer.</summary>
    /// <param name="dc">Where to draw, in the surface's units.</param>
    /// <param name="visible">The part of the surface on screen.</param>
    private void DrawCommentThreads(DrawingContext dc, GraphRect visible)
    {
        if (_model is null || _commentLayer is null)
        {
            return;
        }

        var brush = GraphPalette.Find(this, "Brush.Note.Thread");
        var pen = ThreadPen(brush);
        foreach (var note in _model.Comments.Notes)
        {
            if (ThreadOf(note) is not { } thread)
            {
                continue;
            }

            var (atNote, atNode) = thread;

            if (note == _threadNote)
            {
                atNode = _threadEnd;
            }

            if (!visible.Intersects(GraphRect.Around([atNote, atNode]).Inflate(ThreadGrip)))
            {
                continue;
            }

            dc.DrawLine(pen, new Point(atNote.X, atNote.Y), new Point(atNode.X, atNode.Y));
            var radius = ThreadDotRadius / _viewport.Zoom;
            dc.DrawEllipse(brush, null, new Point(atNode.X, atNode.Y), radius, radius);
        }
    }

    /// <summary>Gives a note's thread: its end at the note and at its node, or none.</summary>
    /// <param name="note">The note.</param>
    /// <returns>The ends, or <see langword="null"/> when the note has no thread to a drawn node.</returns>
    private (GraphPoint AtNote, GraphPoint AtNode)? ThreadOf(CommentNote note)
    {
        if (_commentLayer is null || note.IsInTrash || note.AnchorNode is not { } node || _model is null
            || !_model.Places.TryGetValue(node, out var place))
        {
            return null;
        }

        return CommentGeometry.Thread(_commentLayer.FrameOf(note), _model.FrameAt(node, place));
    }

    /// <summary>Tells the page where a dragged note is, so that the bin grows as it comes near (sketch SO1a).</summary>
    /// <param name="at">The pointer, in this control's coordinates.</param>
    internal void NoteDraggedAt(Point at) => NodeDragged?.Invoke(this, at);

    /// <summary>Ends a note's drag: on the bin it is thrown (sketch SO1a), elsewhere it stays where it was let go.</summary>
    /// <param name="note">The note.</param>
    /// <returns><see langword="true"/> when it was thrown, so that its place is not stored.</returns>
    internal bool DropNoteOnBin(CommentNote note) => _model?.DropNoteOnBin(note) == true;

    /// <summary>Lets the trash shrink back after a note's drag ended without a drop, as when the mouse was lost.</summary>
    internal void ForgetNoteDrag() => _model?.Bin.EndDrag();

    /// <summary>Makes the thread's dotted pen, with the dots measured in pixels on the screen.</summary>
    /// <param name="brush">The thread's colour.</param>
    /// <returns>The pen.</returns>
    private Pen ThreadPen(Brush brush)
    {
        var width = ThreadWidth / _viewport.Zoom;
        return new Pen(brush, width)
        {
            DashStyle = new DashStyle(ThreadDotPixels.Select(length => length / ThreadWidth), 0),
            DashCap = PenLineCap.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
    }

    /// <summary>
    /// The comments' keys, before the graph's own. While a note is written in, its keys are its own and the graph
    /// takes none. N begins a note, Delete removes the chosen one, and Esc lets it go.
    /// </summary>
    /// <param name="e">The key.</param>
    /// <returns><see langword="true"/> when the key was the comments' or the note's.</returns>
    private bool CommentKey(KeyEventArgs e)
    {
        if (_commentLayer?.IsKeyboardFocusWithin == true)
        {
            return true;
        }

        if (_model is null || Keyboard.Modifiers != ModifierKeys.None)
        {
            return false;
        }

        var comments = _model.Comments;
        switch (e.Key)
        {
            case Key.N when _blast == BlastPhase.None && _model.Fan is null:
                _model.CloseMenu();
                _model.BeginComment(PointerOnSurface());
                break;
            case Key.Delete when comments.Selected is { IsEditing: false } chosen:
                // Thrown in the trash (sketch SO1a), from where it is put back; never removed.
                _ = comments.ThrowAsync(chosen);
                break;
            case Key.Escape when comments.Selected is not null && !_model.Checks.IsOpen:
                comments.Select(null);
                break;
            default:
                return false;
        }

        e.Handled = true;
        return true;
    }

    /// <summary>
    /// A double click on the empty surface begins a note there (sketch KN1a), outside the selection view and a pile.
    /// </summary>
    /// <param name="e">The press.</param>
    /// <param name="surface">Where on the surface.</param>
    /// <returns><see langword="true"/> when a note was begun.</returns>
    private bool PressForComment(MouseButtonEventArgs e, GraphPoint surface)
    {
        if (_model is null || e.ClickCount != 2 || _blast != BlastPhase.None || _model.Fan is not null)
        {
            return false;
        }

        var hit = _model.HitTest(surface, Tolerance);
        if (hit.Node is not null || hit.Edge is not null)
        {
            return false;
        }

        return _model.BeginLooseComment(surface) is not null;
    }

    /// <summary>Takes hold of the dot at a thread's end, to tie the thread to another node.</summary>
    /// <param name="surface">Where the press is, on the surface.</param>
    /// <returns><see langword="true"/> when a thread was taken.</returns>
    private bool PressThread(GraphPoint surface)
    {
        if (_model is null)
        {
            return false;
        }

        var reach = ThreadGrip / _viewport.Zoom;
        foreach (var note in _model.Comments.Notes)
        {
            if (ThreadOf(note) is { } thread && thread.AtNode.DistanceTo(surface) <= reach)
            {
                _model.Comments.Select(note);
                _threadNote = note;
                _threadEnd = surface;
                CaptureMouse();
                InvalidateVisual();
                return true;
            }
        }

        return false;
    }

    /// <summary>Moves the dragged thread's end with the pointer, or lets a note under the pointer take it.</summary>
    /// <param name="surface">The pointer on the surface.</param>
    /// <returns><see langword="true"/> when the move was the comments'.</returns>
    private bool MoveForComments(GraphPoint surface)
    {
        if (_threadNote is not null)
        {
            _threadEnd = surface;
            Cursor = Cursors.Hand;
            InvalidateVisual();
            return true;
        }

        if (_commentLayer?.IsMouseOver != true || _gesture.IsPressed || _model is null)
        {
            return false;
        }

        // Over a note, nothing under it is hovered.
        _model.HoverAt(null, Tolerance);
        CloseHoverTip();
        return true;
    }

    /// <summary>Lets go of a dragged thread: tied to the node under the pointer, or untied over the surface.</summary>
    /// <param name="surface">Where it was let go, on the surface.</param>
    /// <returns><see langword="true"/> when a thread was let go.</returns>
    private bool ReleaseThread(GraphPoint surface)
    {
        if (_threadNote is not { } note || _model is null)
        {
            return false;
        }

        _threadNote = null;
        ReleaseMouseCapture();
        var node = _model.Scene?.Node(_model.HitTest(surface, Tolerance).Node)?.Node;
        _ = _model.Comments.TieAsync(note, node);
        InvalidateVisual();
        return true;
    }

    /// <summary>Gives the pointer on the surface, or the screen's middle when the pointer is elsewhere.</summary>
    /// <returns>The point on the surface.</returns>
    private GraphPoint PointerOnSurface()
    {
        var at = IsMouseOver ? Mouse.GetPosition(this) : new Point(ActualWidth / 2, ActualHeight / 2);
        return _viewport.ToSurface(new GraphPoint(at.X, at.Y));
    }
}
