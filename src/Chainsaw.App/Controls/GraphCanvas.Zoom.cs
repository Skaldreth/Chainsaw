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

    /// <summary>The time between the zoom's frames.</summary>
    private readonly FrameClock _zoomClock = new();

    /// <summary>Whether the zoom's frames are hooked to the rendering.</summary>
    private bool _zoomFrames;

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
        if (_zoomFrames || _viewport != before)
        {
            EndZoom();
        }
    }

    /// <summary>Hooks the zoom's frames to the rendering, unless they already are.</summary>
    private void StartZoomFrames()
    {
        if (_zoomFrames)
        {
            return;
        }

        _zoomFrames = true;
        _zoomClock.Reset();
        CompositionTarget.Rendering += OnZoomFrame;
    }

    /// <summary>Stops a zoom where it is, as when the surface is taken hold of, and stores the place it reached.</summary>
    private void StopZoom()
    {
        if (!_zoomFrames)
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

    /// <summary>Unhooks the zoom's frames from the rendering, if they are hooked.</summary>
    private void UnhookZoomFrames()
    {
        if (_zoomFrames)
        {
            _zoomFrames = false;
            CompositionTarget.Rendering -= OnZoomFrame;
        }
    }

    /// <summary>Unhooks the frames and stores the place on screen, once, when a zoom is over.</summary>
    private void EndZoom()
    {
        _zoom.Stop();
        UnhookZoomFrames();
        _model?.ViewportChanged(_viewport);
        InvalidateVisual();
    }

    /// <summary>Moves the zoom one frame, by the rendering time rather than counting frames.</summary>
    /// <param name="sender">The rendering.</param>
    /// <param name="e">The frame.</param>
    private void OnZoomFrame(object? sender, EventArgs e)
    {
        if (_zoomClock.Tick(((RenderingEventArgs)e).RenderingTime) is not { } seconds)
        {
            return;
        }

        _viewport = _zoom.Step(_viewport, seconds);
        MoveView();
        if (!_zoom.IsRunning)
        {
            EndZoom();
        }
    }
}
