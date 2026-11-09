using Chainsaw.App.Graph;

namespace Chainsaw.App.Tests;

/// <summary>The performance HUD's numbers over the last second of the graph's frames, without WPF.</summary>
public sealed class FrameStatsTests
{
    /// <summary>Gives a time in milliseconds.</summary>
    /// <param name="milliseconds">The milliseconds.</param>
    /// <returns>The time.</returns>
    private static TimeSpan Ms(double milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    [Fact]
    public void Nothing_measured_gives_zeros()
    {
        var summary = new FrameStats().Summary();

        Assert.Equal(0, summary.FramesPerSecond);
        Assert.Equal(0, summary.LongestInterval);
        Assert.All(summary.Stages, stage => Assert.Equal(0, stage.Average));
    }

    [Fact]
    public void Frames_per_second_and_the_longest_gap_come_from_the_screen_s_times()
    {
        var stats = new FrameStats();
        foreach (var at in new[] { 0, 16, 32, 48, 80 })
        {
            stats.Begin(Ms(at));
        }

        var summary = stats.Summary();

        Assert.Equal(4 / 0.080, summary.FramesPerSecond, 6);
        Assert.Equal(20, summary.Interval, 6);
        Assert.Equal(32, summary.LongestInterval, 6);
    }

    [Fact]
    public void A_second_begin_at_the_same_time_goes_on_with_the_same_frame()
    {
        var stats = new FrameStats();
        stats.Begin(Ms(0));
        stats.Add(FrameStage.Lines, 2);
        stats.Begin(Ms(0));
        stats.Add(FrameStage.Lines, 3);

        var lines = stats.Summary().Stages.Single(stage => stage.Stage == FrameStage.Lines);

        Assert.Equal(5, lines.Average);
        Assert.Equal(5, lines.Longest);
    }

    [Fact]
    public void Each_part_has_its_average_and_longest_over_the_frames()
    {
        var stats = new FrameStats();
        stats.Begin(Ms(0));
        stats.Add(FrameStage.Nodes, 1);
        stats.Begin(Ms(16));
        stats.Add(FrameStage.Nodes, 5);

        var nodes = stats.Summary().Stages.Single(stage => stage.Stage == FrameStage.Nodes);

        Assert.Equal(3, nodes.Average);
        Assert.Equal(5, nodes.Longest);
    }

    [Fact]
    public void A_frame_counts_as_the_most_it_drew()
    {
        var stats = new FrameStats();
        stats.Begin(Ms(0));
        stats.Mark(FrameWork.Full);
        stats.Mark(FrameWork.View);
        stats.Begin(Ms(16));
        stats.Mark(FrameWork.Partial);
        stats.Begin(Ms(32));

        var work = stats.Summary().Work;

        Assert.Equal(1, work[FrameWork.Full]);
        Assert.Equal(1, work[FrameWork.Partial]);
        Assert.Equal(0, work[FrameWork.View]);
        Assert.Equal(1, work[FrameWork.None]);
    }

    [Fact]
    public void The_most_nodes_and_lines_a_frame_drew_are_kept()
    {
        var stats = new FrameStats();
        stats.Begin(Ms(0));
        stats.Drew(10, 0);
        stats.Drew(0, 40);
        stats.Begin(Ms(16));
        stats.Drew(3, 2);

        var summary = stats.Summary();

        Assert.Equal(10, summary.Nodes);
        Assert.Equal(40, summary.Edges);
    }

    [Fact]
    public void Only_the_last_second_is_counted()
    {
        var stats = new FrameStats();
        for (var at = 0; at <= 2000; at += 100)
        {
            stats.Begin(Ms(at));
            stats.Add(FrameStage.Total, at < 1000 ? 50 : 1);
        }

        var total = stats.Summary().Stages.Single(stage => stage.Stage == FrameStage.Total);

        Assert.Equal(1, total.Longest);
    }

    [Fact]
    public void A_pause_begins_a_new_run_so_that_the_rest_is_not_a_stutter()
    {
        var stats = new FrameStats();
        stats.Begin(Ms(0));
        stats.Begin(Ms(16));
        stats.Begin(Ms(16) + FrameStats.Pause + Ms(1));
        stats.Begin(Ms(32) + FrameStats.Pause + Ms(1));

        Assert.Equal(16, stats.Summary().LongestInterval, 6);
    }

    [Fact]
    public void Nothing_is_added_before_the_first_frame()
    {
        var stats = new FrameStats();
        stats.Add(FrameStage.Lines, 9);
        stats.Mark(FrameWork.Full);
        stats.Drew(5, 5);

        var summary = stats.Summary();

        Assert.Equal(0, summary.Nodes);
        Assert.All(summary.Work.Values, count => Assert.Equal(0, count));
    }
}
