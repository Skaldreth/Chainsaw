using Chainsaw.App.Graph;

namespace Chainsaw.App.Tests;

/// <summary>
/// The graph surface's one frame loop (principle 6): changes asked for are gathered into one per frame, motions step
/// on their own clocks and at their own pace, and the loop is idle when nothing moves; without WPF.
/// </summary>
public sealed class FramePumpTests
{
    /// <summary>A frame's time, 60 frames a second.</summary>
    /// <param name="frame">The frame's number.</param>
    /// <returns>Its time.</returns>
    private static TimeSpan Frame(int frame) => TimeSpan.FromSeconds(frame / 60.0);

    [Fact]
    public void Changes_asked_for_in_a_frame_are_drawn_together_once()
    {
        var pump = new FramePump();
        pump.Request(GraphChange.View);
        pump.Request(GraphChange.Scene);
        pump.Request(GraphChange.View);

        Assert.False(pump.IsIdle);
        Assert.Equal(GraphChange.View | GraphChange.Scene, pump.Tick(Frame(1)));
        Assert.True(pump.IsIdle);
        Assert.Equal(GraphChange.None, pump.Tick(Frame(2)));
    }

    [Fact]
    public void A_motion_steps_by_the_time_since_its_own_last_step_until_it_stops_itself()
    {
        var pump = new FramePump();
        var steps = new List<double>();
        pump.Add(GraphMotion.Glide, seconds =>
        {
            steps.Add(seconds);
            if (steps.Count == 3)
            {
                pump.Stop(GraphMotion.Glide);
            }

            return GraphChange.View;
        });

        pump.Start(GraphMotion.Glide);
        for (var frame = 0; frame < 6; frame++)
        {
            pump.Tick(Frame(frame * 2));
        }

        Assert.Equal(3, steps.Count);
        Assert.Equal(1.0 / 30, steps[1], 6);
        Assert.False(pump.IsRunning(GraphMotion.Glide));
        Assert.True(pump.IsIdle);
    }

    [Fact]
    public void A_slow_motion_steps_only_when_its_least_time_has_passed()
    {
        var pump = new FramePump();
        var steps = 0;
        pump.Add(GraphMotion.Rings, _ =>
        {
            steps++;
            return GraphChange.Scene;
        }, TimeSpan.FromMilliseconds(66));
        pump.Start(GraphMotion.Rings);

        var changes = Enumerable.Range(0, 60).Select(frame => pump.Tick(Frame(frame))).ToList();

        Assert.InRange(steps, 14, 16);
        Assert.Contains(GraphChange.None, changes);
    }

    [Fact]
    public void Starting_a_motion_that_runs_keeps_its_clock()
    {
        var pump = new FramePump();
        var steps = new List<double>();
        pump.Add(GraphMotion.Zoom, seconds =>
        {
            steps.Add(seconds);
            return GraphChange.View;
        });

        pump.Start(GraphMotion.Zoom);
        pump.Tick(Frame(0));
        pump.Start(GraphMotion.Zoom);
        pump.Tick(Frame(3));

        Assert.Equal(3.0 / 60, steps[1], 6);
    }

    [Fact]
    public void A_motion_must_be_known_once_before_it_runs()
    {
        var pump = new FramePump();
        pump.Add(GraphMotion.Cards, _ => GraphChange.Scene);

        Assert.Throws<InvalidOperationException>(() => pump.Add(GraphMotion.Cards, _ => GraphChange.Scene));
        Assert.Throws<InvalidOperationException>(() => pump.Start(GraphMotion.Blast));
    }
}
