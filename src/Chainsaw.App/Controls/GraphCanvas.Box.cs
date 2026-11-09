using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Chainsaw.App.Graph;
using Chainsaw.App.Motion;

namespace Chainsaw.App.Controls;

/// <summary>
/// The box that picks several actors, as on the Windows desktop (the user's wish 2026-11-08, built on the user's
/// description of the standard pattern, without a sketch): the left button dragged on the empty surface draws a thin
/// box in the accent, faintly filled, and every actor it touches is picked while it is drawn
/// (<see cref="BoxSelection"/>).
/// Ctrl turns the touched actors, Shift adds them, and Esc gives back the pick it began with. Near the surface's edge
/// the surface scrolls, so that a box can reach past the screen. The selection view and a fanned pile have their own
/// pointer and never draw one.
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>How strong the box's fill is.</summary>
    private const double BoxFillOpacity = 0.1;

    /// <summary>How strong the box's frame is.</summary>
    private const double BoxEdgeOpacity = 0.8;

    /// <summary>Where a press on the empty surface began, on screen, that may become a box; null otherwise.</summary>
    private GraphPoint? _boxFrom;

    /// <summary>How the box of the press changes the pick, from the keys held when it was pressed.</summary>
    private BoxMode _boxMode;

    /// <summary>Whether the press has moved far enough to draw the box.</summary>
    private bool _boxing;

    /// <summary>Where the box began, on the surface, which stays put while the surface scrolls.</summary>
    private GraphPoint _boxAnchor;

    /// <summary>Where the pointer is while the box is drawn, on screen.</summary>
    private GraphPoint _boxPointer;

    /// <summary>
    /// Remembers a press that may become a box: on the empty surface, with the graph shown and no fanned pile.
    /// </summary>
    /// <param name="at">The press, on screen.</param>
    /// <param name="node">The node under it, or <see langword="null"/>.</param>
    private void PrepareBox(GraphPoint at, string? node)
    {
        var free = node is null && _model is { Scene: not null, Fan: null } && _blast == BlastPhase.None;
        _boxFrom = free ? at : null;
        var keys = Keyboard.Modifiers;
        _boxMode = BoxSelection.ModeOf(
            (keys & ModifierKeys.Control) != 0, (keys & ModifierKeys.Shift) != 0);
    }

    /// <summary>Draws the box on with the pointer, once the press has become a drag.</summary>
    /// <param name="at">The pointer, on screen.</param>
    /// <returns><see langword="true"/> when the move belonged to a box.</returns>
    private bool MoveBox(GraphPoint at)
    {
        if (_boxFrom is not { } from || !_gesture.IsDragging)
        {
            return _boxing;
        }

        if (!_boxing)
        {
            _boxing = true;
            _boxAnchor = _viewport.ToSurface(from);
            _model!.BeginBox(_boxMode);
            StartMotion(GraphMotion.BoxScroll);
        }

        _boxPointer = at;
        PickWithBox();
        return true;
    }

    /// <summary>Lets go of the box: the pick stays as it picked.</summary>
    /// <returns><see langword="true"/> when a box was drawn, so that the release is no click.</returns>
    private bool EndBox()
    {
        var drawn = _boxing;
        if (drawn)
        {
            _model?.EndBox();
            _model?.ViewportChanged(_viewport);
        }

        StopBox();
        return drawn;
    }

    /// <summary>Cancels the box (Esc): the pick is given back and the press is let go.</summary>
    /// <returns><see langword="true"/> when a box was being drawn.</returns>
    private bool CancelBox()
    {
        if (!_boxing)
        {
            return false;
        }

        _model?.CancelBox();
        _model?.ViewportChanged(_viewport);
        StopBox();
        if (IsMouseCaptured)
        {
            // Losing the capture ends the press as nothing (OnLostMouseCapture), so the release is no click.
            ReleaseMouseCapture();
        }

        return true;
    }

    /// <summary>Forgets the box and stops scrolling at the edge.</summary>
    private void StopBox()
    {
        if (_boxing)
        {
            _frames.Stop(GraphMotion.BoxScroll);
            Request(GraphChange.Overlay);
        }

        _boxing = false;
        _boxFrom = null;
    }

    /// <summary>Picks with the box as it stands now and draws it again.</summary>
    private void PickWithBox()
    {
        _model?.BoxTo(BoxSelection.Between(_boxAnchor, _viewport.ToSurface(_boxPointer)));
        Request(GraphChange.Overlay);
    }

    /// <summary>
    /// Scrolls the surface one step while the pointer is at its edge, by the time since the last step, and picks
    /// again with the box that grew.
    /// </summary>
    /// <param name="seconds">The time since the last step.</param>
    /// <returns>What changed: the view, or nothing while the pointer is away from the edge.</returns>
    private GraphChange BoxStep(double seconds)
    {
        var speed = BoxSelection.ScrollSpeed(_boxPointer, ActualWidth, ActualHeight);
        if (speed.X == 0 && speed.Y == 0)
        {
            return GraphChange.None;
        }

        _viewport = _viewport.Pan(-speed.X * seconds, -speed.Y * seconds);
        PickWithBox();
        return GraphChange.View;
    }

    /// <summary>Draws the box, on screen: a thin frame in the accent and a faint fill of the same tone.</summary>
    /// <param name="dc">Where to draw, in screen units.</param>
    private void DrawBox(DrawingContext dc)
    {
        if (!_boxing)
        {
            return;
        }

        var accent = Ink("Brush.Accent");
        var from = _viewport.ToScreen(_boxAnchor);
        var box = BoxSelection.Between(from, _boxPointer);

        // Half a pixel in, so that the 1 px frame lies on the pixels and stays sharp.
        var rect = new Rect(
            Math.Round(box.X) + 0.5, Math.Round(box.Y) + 0.5, Math.Round(box.Width), Math.Round(box.Height));
        dc.PushOpacity(BoxFillOpacity);
        dc.DrawRectangle(accent, null, rect);
        dc.Pop();
        dc.PushOpacity(BoxEdgeOpacity);
        dc.DrawRectangle(null, PenOf(accent, 1), rect);
        dc.Pop();
    }
}
