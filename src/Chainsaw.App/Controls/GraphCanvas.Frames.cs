using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// The surface's one frame loop (<see cref="FramePump"/>), driven by events: input, the view model's
/// <see cref="ViewModels.GraphViewModel.Redraw"/> and the motions ask for a <see cref="GraphChange"/>, and each frame
/// of the screen does the least work all of them need together: a change of the scene draws it again, a moved view
/// moves the scene's layer and draws what stands over it, and nothing at all is done in a frame where nothing
/// changed. The loop holds the screen's frames only while something is asked for or moves (principle 6).
/// </summary>
/// <remarks>
/// The rules: drawing never asks for a frame and never starts a motion's frames by itself (it only notes what it drew
/// that moves, and the frame loop starts or stops those motions after the scene is drawn); only input, the view model
/// and the motions ask. A motion is a step that moves its state by the seconds since its last step and says what it
/// changed; it stops itself when it is over.
/// </remarks>
public sealed partial class GraphCanvas
{
    /// <summary>The least time between the rings' steps: 15 times a second is enough for them.</summary>
    private static readonly TimeSpan RingFrame = TimeSpan.FromMilliseconds(66);

    /// <summary>The frame loop: what is asked for and what moves.</summary>
    private readonly FramePump _frames = new();

    /// <summary>Whether the loop holds the screen's frames.</summary>
    private bool _framesHooked;

    /// <summary>Makes the surface's motions known to the loop, each with its step.</summary>
    private void SetUpFrames()
    {
        _frames.Add(GraphMotion.Glide, GlideStep);
        _frames.Add(GraphMotion.Zoom, ZoomStep);
        _frames.Add(GraphMotion.FoundGlide, FoundStep);
        _frames.Add(GraphMotion.BoxScroll, BoxStep);
        _frames.Add(GraphMotion.Blast, BlastStep);
        _frames.Add(GraphMotion.Rings, RingStep, RingFrame);
        _frames.Add(GraphMotion.Cards, _ => GraphChange.Scene);
    }

    /// <summary>
    /// Makes the scene's drawing stale, so that the next frame draws it again: everything that changes what the scene
    /// shows calls this.
    /// </summary>
    private void Redraw() => Request(GraphChange.Scene);

    /// <summary>Asks for a change to be drawn in the next frame, together with everything else asked for.</summary>
    /// <param name="change">The change.</param>
    private void Request(GraphChange change)
    {
        _frames.Request(change);
        HoldFrames();
    }

    /// <summary>Starts a motion from the next frame.</summary>
    /// <param name="motion">The motion.</param>
    private void StartMotion(GraphMotion motion)
    {
        _frames.Start(motion);
        HoldFrames();
    }

    /// <summary>Starts or stops a motion, as what is drawn says it should move or not.</summary>
    /// <param name="motion">The motion.</param>
    /// <param name="runs">Whether it should run.</param>
    private void KeepMotion(GraphMotion motion, bool runs)
    {
        if (runs && AnimationsOn && IsVisible)
        {
            StartMotion(motion);
        }
        else
        {
            _frames.Stop(motion);
        }
    }

    /// <summary>Holds the screen's frames, unless the loop already does.</summary>
    private void HoldFrames()
    {
        if (!_framesHooked)
        {
            _framesHooked = true;
            CompositionTarget.Rendering += OnFrame;
        }
    }

    /// <summary>
    /// Does one frame: steps the motions that are due, does the least work the changes of the frame need, and lets go
    /// of the screen's frames when nothing more is asked for or moves.
    /// </summary>
    /// <param name="sender">WPF's rendering.</param>
    /// <param name="e">The frame.</param>
    private void OnFrame(object? sender, EventArgs e)
    {
        // A minimised window draws nothing; the motions wait, and what is asked for is drawn when it comes back.
        if (!IsMinimised(this))
        {
            Apply(_frames.Tick(((RenderingEventArgs)e).RenderingTime));
        }

        if (_frames.IsIdle)
        {
            _framesHooked = false;
            CompositionTarget.Rendering -= OnFrame;
        }
    }

    /// <summary>Does the least work a frame's changes need.</summary>
    /// <param name="change">What changed in the frame.</param>
    private void Apply(GraphChange change)
    {
        if (change.HasFlag(GraphChange.Drag) && !change.HasFlag(GraphChange.Scene) && !DrawDrag())
        {
            change |= GraphChange.Scene;
        }

        if (change.HasFlag(GraphChange.Scene))
        {
            _sceneWindow.Forget();
            _sceneChanged = true;
            InvalidateVisual();
        }
        else if (change != GraphChange.None)
        {
            FollowView();
        }
    }
}
