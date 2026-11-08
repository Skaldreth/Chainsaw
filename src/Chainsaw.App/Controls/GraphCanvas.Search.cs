using System.Windows;
using System.Windows.Media;
using Chainsaw.App.Graph;
using Chainsaw.App.Motion;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Controls;

/// <summary>
/// The command bar's search in the graph (sketch KR1a): the surface glides so that the chosen row's node lands under
/// the bar, and a ring in the accent stands around it with a second that pulses outward, still when Windows has
/// animations turned off. The zoom is kept, unless it is too small to read the node.
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>How long the glide to a found node takes.</summary>
    private const double FoundGlideSeconds = 0.55;

    /// <summary>
    /// Where the found node lands, as a share of the surface's height: under the command bar and its list, as in the
    /// sketch.
    /// </summary>
    private const double FoundHeight = 0.78;

    /// <summary>How far the ring stands outside the node's frame, on the surface.</summary>
    private const double FoundRingGap = 8;

    /// <summary>How far the pulse reaches past the ring at its widest, on the surface.</summary>
    private const double FoundPulseReach = 16;

    /// <summary>The ring's thickness, on the surface.</summary>
    private const double FoundRingThickness = 2;

    /// <summary>Where the glide started, and where it goes.</summary>
    private (Viewport From, Viewport To) _foundGlide = (Viewport.Initial, Viewport.Initial);

    /// <summary>How far into the glide it is, in seconds.</summary>
    private double _foundElapsed;

    /// <summary>Follows the search: a node to glide to.</summary>
    /// <param name="model">The view model.</param>
    private void AttachSearch(GraphViewModel model) => model.FoundWanted += (_, key) => GlideToFound(key);

    /// <summary>Glides the surface so that a node lands under the bar, or jumps there without animations.</summary>
    /// <param name="nodeKey">The node.</param>
    private void GlideToFound(string nodeKey)
    {
        if (_model is null || !_model.Places.TryGetValue(nodeKey, out var place) || ActualWidth <= 0)
        {
            return;
        }

        StopGlide();
        DropZoom();
        var frame = _model.FrameAt(nodeKey, place);
        var zoom = Math.Max(_viewport.Zoom, Viewport.ReadableZoom);
        var centre = new GraphPoint(frame.X + (frame.Width / 2), frame.Y + (frame.Height / 2));
        var target = new Viewport(
            (ActualWidth / 2) - (centre.X * zoom), (ActualHeight * FoundHeight) - (centre.Y * zoom), zoom);
        if (!AnimationsOn)
        {
            EndFoundGlide(target);
            return;
        }

        _foundGlide = (_viewport, target);
        _foundElapsed = 0;
        StartMotion(GraphMotion.FoundGlide);
    }

    /// <summary>Moves the glide one step, eased out, by the time since the last step.</summary>
    /// <param name="seconds">The time since the last step.</param>
    /// <returns>What changed: the view, or the scene when the glide has landed.</returns>
    private GraphChange FoundStep(double seconds)
    {
        _foundElapsed += seconds;
        var t = Math.Min(1, _foundElapsed / FoundGlideSeconds);
        if (t >= 1)
        {
            EndFoundGlide(_foundGlide.To);
            return GraphChange.Scene;
        }

        var eased = 1 - Math.Pow(1 - t, 3);
        var (from, to) = _foundGlide;
        _viewport = new Viewport(
            from.OffsetX + ((to.OffsetX - from.OffsetX) * eased),
            from.OffsetY + ((to.OffsetY - from.OffsetY) * eased),
            from.Zoom + ((to.Zoom - from.Zoom) * eased));
        return GraphChange.View;
    }

    /// <summary>Ends the glide where it was going, and stores the place on screen.</summary>
    /// <param name="target">Where it was going.</param>
    private void EndFoundGlide(Viewport target)
    {
        DropFoundGlide();
        _viewport = target;
        _model?.ViewportChanged(_viewport);
        Redraw();
    }

    /// <summary>Stops the glide where it is, without storing the place.</summary>
    private void DropFoundGlide() => _frames.Stop(GraphMotion.FoundGlide);

    /// <summary>Draws the ring around the found node, and its pulse, on the surface.</summary>
    /// <param name="dc">Where to draw, with the surface's transform pushed.</param>
    private void DrawFound(DrawingContext dc)
    {
        if (_model?.FoundNode is not { } key || !_model.Places.TryGetValue(key, out var place))
        {
            return;
        }

        var frame = _model.FrameAt(key, place);
        var accent = Ink("Brush.Accent");
        var pen = PenOf(accent, FoundRingThickness);
        var radius = NodeRadius + FoundRingGap;
        dc.DrawRoundedRectangle(null, pen, Around(frame, FoundRingGap), radius, radius);
        RingsAt(frame.Inflate(FoundRingGap + FoundPulseReach));
        var step = AnimationsOn ? _ringPhase : 0;
        var reach = FoundRingGap + (step * FoundPulseReach);
        dc.PushOpacity(AnimationsOn ? 0.7 * (1 - step) : 0);
        dc.DrawRoundedRectangle(null, pen, Around(frame, reach), NodeRadius + reach, NodeRadius + reach);
        dc.Pop();
    }

    /// <summary>Gives a node's frame grown on every side.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="by">How much.</param>
    /// <returns>The grown rectangle.</returns>
    private static Rect Around(GraphRect frame, double by) =>
        new(frame.X - by, frame.Y - by, frame.Width + (2 * by), frame.Height + (2 * by));
}
