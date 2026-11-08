using Chainsaw.App.Graph;
using Chainsaw.App.Motion;

namespace Chainsaw.App.Tests;

/// <summary>
/// The graph's smooth zoom, as in Gawk: it eases to the zoom asked for, the point under the pointer stands still, quick
/// steps add up, it keeps within the limits and it jumps at once without animations; without WPF.
/// </summary>
public sealed class ZoomMotionTests
{
    /// <summary>A frame at 60 Hz.</summary>
    private const double Frame = 1.0 / 60;

    /// <summary>Where the pointer is, on screen.</summary>
    private static readonly GraphPoint Pointer = new(310, 220);

    /// <summary>A viewport panned away from the corner, at its natural size.</summary>
    private static readonly Viewport Start = new(-120, 45, 1);

    [Fact]
    public void A_notch_eases_to_the_wheel_step_and_ends_there()
    {
        var zoom = new ZoomMotion();
        var shown = zoom.Aim(Start, Pointer, ZoomMotion.WheelFactor(120), animate: true);
        Assert.Equal(Start, shown);
        Assert.True(zoom.IsRunning);

        var previous = shown.Zoom;
        var frames = 0;
        while (zoom.IsRunning && frames < 600)
        {
            shown = zoom.Step(shown, Frame);
            Assert.True(shown.Zoom >= previous, "The zoom never turns back on its way.");
            previous = shown.Zoom;
            frames++;
        }

        Assert.False(zoom.IsRunning);
        Assert.Equal(Viewport.WheelStep, shown.Zoom, 12);
        Assert.InRange(frames, 2, 60);
    }

    [Fact]
    public void The_first_frame_moves_only_part_of_the_way()
    {
        var zoom = new ZoomMotion();
        var shown = zoom.Step(zoom.Aim(Start, Pointer, 2, animate: true), Frame);

        Assert.InRange(shown.Zoom, 1.01, 1.9);
    }

    [Fact]
    public void The_point_under_the_pointer_stands_still_in_every_frame()
    {
        var zoom = new ZoomMotion();
        var under = Start.ToSurface(Pointer);
        var shown = zoom.Aim(Start, Pointer, 2.5, animate: true);

        while (zoom.IsRunning)
        {
            shown = zoom.Step(shown, Frame);
            var onScreen = shown.ToScreen(under);
            Assert.Equal(Pointer.X, onScreen.X, 9);
            Assert.Equal(Pointer.Y, onScreen.Y, 9);
        }
    }

    [Fact]
    public void Quick_notches_add_up_to_where_the_zoom_is_going()
    {
        var zoom = new ZoomMotion();
        var shown = zoom.Aim(Start, Pointer, Viewport.WheelStep, animate: true);
        shown = zoom.Step(shown, Frame);
        shown = zoom.Aim(shown, Pointer, Viewport.WheelStep, animate: true);
        shown = zoom.Aim(shown, Pointer, Viewport.WheelStep, animate: true);

        Assert.Equal(Math.Pow(Viewport.WheelStep, 3), zoom.Target!.Value, 12);
        var before = shown.Zoom;
        shown = zoom.Step(shown, Frame);
        Assert.True(shown.Zoom > before, "A new notch carries on from where the zoom is, without a jump back.");
    }

    [Fact]
    public void Small_touchpad_steps_add_up_to_a_notch()
    {
        var zoom = new ZoomMotion();
        var shown = Start;
        for (var step = 0; step < 4; step++)
        {
            shown = zoom.Aim(shown, Pointer, ZoomMotion.WheelFactor(30), animate: true);
        }

        Assert.Equal(Viewport.WheelStep, zoom.Target!.Value, 12);
    }

    [Fact]
    public void A_zoom_turning_back_eases_back_without_a_jump()
    {
        var zoom = new ZoomMotion();
        var shown = zoom.Step(zoom.Aim(Start, Pointer, 2, animate: true), Frame);
        var reached = shown.Zoom;

        shown = zoom.Aim(shown, Pointer, 1 / 4.0, animate: true);
        Assert.Equal(reached, shown.Zoom);
        Assert.Equal(0.5, zoom.Target!.Value, 12);
        shown = zoom.Step(shown, Frame);
        Assert.True(shown.Zoom < reached && shown.Zoom > 0.5);
    }

    [Fact]
    public void The_zoom_keeps_within_the_limits()
    {
        var zoom = new ZoomMotion();
        var shown = Start;
        for (var notch = 0; notch < 40; notch++)
        {
            shown = zoom.Aim(shown, Pointer, Viewport.WheelStep, animate: true);
        }

        Assert.Equal(Viewport.MaximumZoom, zoom.Target!.Value);
        while (zoom.IsRunning)
        {
            shown = zoom.Step(shown, Frame);
            Assert.True(shown.Zoom <= Viewport.MaximumZoom);
        }

        Assert.Equal(Viewport.MaximumZoom, shown.Zoom);
        Assert.Same(shown, zoom.Aim(shown, Pointer, Viewport.WheelStep, animate: true));
        Assert.False(zoom.IsRunning);
    }

    [Fact]
    public void Without_animations_the_zoom_jumps_at_once_around_the_pointer()
    {
        var zoom = new ZoomMotion();
        var under = Start.ToSurface(Pointer);
        var shown = zoom.Aim(Start, Pointer, Viewport.WheelStep, animate: false);

        Assert.False(zoom.IsRunning);
        Assert.Equal(Viewport.WheelStep, shown.Zoom, 12);
        Assert.Equal(Pointer.X, shown.ToScreen(under).X, 9);
        Assert.Equal(Pointer.Y, shown.ToScreen(under).Y, 9);
    }

    [Fact]
    public void The_same_zoom_is_reached_however_the_time_is_cut_into_frames()
    {
        var fast = Ease(0.02, frames: 6);
        var slow = Ease(0.04, frames: 3);

        Assert.Equal(fast, slow, 3);
    }

    [Fact]
    public void A_stopped_zoom_stays_where_it_got_to()
    {
        var zoom = new ZoomMotion();
        var shown = zoom.Step(zoom.Aim(Start, Pointer, 2, animate: true), Frame);
        zoom.Stop();

        Assert.False(zoom.IsRunning);
        Assert.Same(shown, zoom.Step(shown, Frame));
    }

    [Fact]
    public void Easing_toward_lands_on_the_target_when_close()
    {
        Assert.Equal(1.0, Easing.Toward(0.999, 1, Frame, 0.07, snap: 0.002));
        Assert.Equal(0.5, Easing.Toward(0, 1, 0.07 * Math.Log(2), 0.07, snap: 0.002), 12);
    }

    /// <summary>Zooms twice in from the start for a number of frames of a length.</summary>
    /// <param name="frame">The length of each frame, in seconds.</param>
    /// <param name="frames">How many frames.</param>
    /// <returns>The zoom reached.</returns>
    private static double Ease(double frame, int frames)
    {
        var zoom = new ZoomMotion();
        var shown = zoom.Aim(Start, Pointer, 2, animate: true);
        for (var count = 0; count < frames; count++)
        {
            shown = zoom.Step(shown, frame);
        }

        return shown.Zoom;
    }
}
