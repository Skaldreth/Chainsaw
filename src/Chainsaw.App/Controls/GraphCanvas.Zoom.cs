using System.Windows.Media;
using Chainsaw.App.Graph;
using Chainsaw.App.Motion;

namespace Chainsaw.App.Controls;

/// <summary>
/// The smooth zoom, as in Gawk (the user's wish 2026-11-08): the wheel, the touchpad and + and − ease the surface to
/// the zoom asked for on the frames of the rendering, with the point under the pointer standing still. Without
/// animations in Windows the zoom is a jump. A zoom stops a glide, and a press or a pan stops a zoom where it is, so
/// that the two never pull the surface different ways.
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>The zoom under way: where it is going and around which point.</summary>
    private readonly ZoomMotion _zoom = new();

    /// <summary>Gets whether a zoom is under way on the frames.</summary>
    private bool IsZooming => _frames.IsRunning(GraphMotion.Zoom);

    /// <summary>
    /// Zooms by a factor around a point on screen: eased over the next frames, or at once without animations.
    /// </summary>
    /// <param name="anchor">The point on screen that stays where it is.</param>
    /// <param name="factor">How much; above 1 zooms in.</param>
    private void AimZoom(GraphPoint anchor, double factor)
    {
        StopGlide();
        var before = _viewport;
        _viewport = _zoom.Aim(_viewport, anchor, factor, AnimationsOn);
        if (_zoom.IsRunning)
        {
            StartZoomFrames();
            return;
        }

        // A step against a limit changes nothing, and nothing is stored for it.
        if (IsZooming || _viewport != before)
        {
            EndZoom();
        }
    }

    /// <summary>Starts the zoom's frames, unless they already run.</summary>
    private void StartZoomFrames() => StartMotion(GraphMotion.Zoom);

    /// <summary>Stops a zoom where it is, as when the surface is taken hold of, and stores the place it reached.</summary>
    private void StopZoom()
    {
        if (!IsZooming)
        {
            return;
        }

        EndZoom();
    }

    /// <summary>
    /// Drops a zoom, a glide after a pan and a glide to a found node without storing where they got to, as when the
    /// surface is about to be fitted or shown as the case stored it, so that no frame moves the new place on and the
    /// place stored is the new one.
    /// </summary>
    private void DropZoom()
    {
        DropGlide();
        DropFoundGlide();
        _zoom.Stop();
        UnhookZoomFrames();
    }

    /// <summary>Stops the zoom's frames, if they run.</summary>
    private void UnhookZoomFrames() => _frames.Stop(GraphMotion.Zoom);

    /// <summary>Unhooks the frames and stores the place on screen, once, when a zoom is over.</summary>
    private void EndZoom()
    {
        _zoom.Stop();
        UnhookZoomFrames();
        _model?.ViewportChanged(_viewport);
        Request(GraphChange.View);
    }

    /// <summary>Moves the zoom one step, by the time since the last step.</summary>
    /// <param name="seconds">The time since the last step.</param>
    /// <returns>What changed: the view.</returns>
    private GraphChange ZoomStep(double seconds)
    {
        _viewport = _zoom.Step(_viewport, seconds);
        if (!_zoom.IsRunning)
        {
            EndZoom();
        }

        return GraphChange.View;
    }
}
