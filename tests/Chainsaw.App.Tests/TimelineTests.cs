using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>
/// The time line under the graph (sketch TL1a) without WPF: the days, the bands through the actors, the strip and
/// what is drawn faint at a chosen time.
/// </summary>
public sealed class TimelineTests
{
    /// <summary>The made-up graph's first transaction, in block 100.</summary>
    private static readonly DateTimeOffset Tx0 = GraphSamples.Block(100).Time;

    /// <summary>Stockholm, whose days change with summer time.</summary>
    private static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    /// <summary>The axis starts at the first day's local midnight, in the user's time zone.</summary>
    [Fact]
    public void AxisStartsAtTheFirstDaysMidnight()
    {
        var axis = new TimeAxis(new DateTimeOffset(2026, 11, 1, 0, 30, 0, TimeSpan.Zero), Stockholm);

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(1)), axis.Origin);
        Assert.Equal(1.5, axis.Hours(new DateTimeOffset(2026, 11, 1, 0, 30, 0, TimeSpan.Zero)), 6);
        Assert.Equal("1 november 01:30", axis.TimeName(new DateTimeOffset(2026, 11, 1, 0, 30, 0, TimeSpan.Zero)));
    }

    /// <summary>The days between two times are the midnights strictly between them; summer time makes a day short.</summary>
    [Fact]
    public void DaysFollowTheCalendarAcrossSummerTime()
    {
        var axis = new TimeAxis(new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.FromHours(1)), Stockholm);

        var days = axis.DayStarts(10, 60);

        Assert.Equal([24.0, 47.0], days.Select(day => day.Hours));
        Assert.Equal([new DateOnly(2026, 3, 29), new DateOnly(2026, 3, 30)], days.Select(day => day.Day));
        Assert.Empty(axis.DayStarts(24, 30));
        Assert.Equal("29 mars", TimeAxis.DayName(new DateOnly(2026, 3, 29)));
    }

    /// <summary>A day line starts in the gap between the last actor reached by then and the first after.</summary>
    [Fact]
    public void DayLineStartsInTheGap()
    {
        TimedFrame[] actors = [Actor(0, 0, 1), Actor(300, 0, 47)];

        var start = TimeBands.BaseX(actors, 24);

        Assert.Equal(GraphSizes.NodeWidth + ((300 - GraphSizes.NodeWidth) / 2), start!.Value, 6);
        Assert.Null(TimeBands.BaseX(actors, 0.5));
        Assert.Null(TimeBands.BaseX(actors, 48));
    }

    /// <summary>With nothing in the way, as in the hop columns, the line stands straight.</summary>
    [Fact]
    public void LineIsStraightWhenNothingIsInTheWay()
    {
        TimedFrame[] actors = [Actor(0, 0, 1), Actor(300, 0, 47), Actor(0, 200, 2), Actor(300, 200, 46)];

        var line = TimeBands.Line(actors, 24, -72, 312)!;

        Assert.All(line.Points, point => Assert.Equal(216, point.X, 6));
        Assert.Equal(-72, line.Top);
        Assert.Equal(312, line.Bottom, 6);
    }

    /// <summary>
    /// An actor dragged out of its day to the wrong side of the line is passed round: the line bends to keep it on the
    /// later side, and stands as before far from it.
    /// </summary>
    [Fact]
    public void LineBendsRoundAnActorDraggedOutOfItsDay()
    {
        var later = Actor(-100, 400, 40);
        TimedFrame[] actors = [Actor(0, 0, 1), Actor(300, 0, 47), later];

        var line = TimeBands.Line(actors, 24, -72, 512)!;

        var beside = later.Frame.Y + (GraphSizes.NodeHeight / 2);
        Assert.True(line.XAt(beside) <= later.Frame.X - TimeBands.Clearance + 0.001);
        Assert.True(line.XAt(0) >= GraphSizes.NodeWidth + TimeBands.Clearance - 0.001);
        Assert.True(line.XAt(0) > line.XAt(beside) + 50);
    }

    /// <summary>A line gives its place between its points, and only its points near the screen.</summary>
    [Fact]
    public void LineReadsBetweenItsPoints()
    {
        var line = new TimeBandLine(24, [new GraphPoint(0, 0), new GraphPoint(10, 10), new GraphPoint(20, 20)]);

        Assert.Equal(5, line.XAt(5), 6);
        Assert.Equal(0, line.XAt(-50), 6);
        Assert.Equal(20, line.XAt(50), 6);
        Assert.Equal([new GraphPoint(0, 0), new GraphPoint(10, 10)], line.Within(1, 4));
        Assert.Empty(line.Within(30, 40));
    }

    /// <summary>The lines run from above the top actor to below the bottom one, and not at all without actors.</summary>
    [Fact]
    public void ExtentReachesPastTheActors()
    {
        TimedFrame[] actors = [Actor(-40, 0, 1), Actor(300, 0, 47)];

        Assert.Null(TimeBands.Extent([]));
        Assert.Equal((-TimeBands.EndMargin, GraphSizes.NodeHeight + TimeBands.EndMargin), TimeBands.Extent(actors));
    }

    /// <summary>
    /// The graph's times are the chain's: one stroke per confirmed transaction in time order, each actor at its first
    /// money in, and a start point that only sends at its first money out.
    /// </summary>
    [Fact]
    public void TimesComeFromTheChain()
    {
        var times = GraphTimeline.Of(GraphSamples.Scene());

        Assert.Equal(4, times.Ticks.Count);
        Assert.Equal(Enumerable.Range(0, 4).Select(tx => Tx0.AddHours(tx)), times.Ticks.Select(tick => tick.Time));
        Assert.Contains(" → ", times.Ticks[1].Text, StringComparison.Ordinal);
        Assert.EndsWith(" · +1", times.Ticks[1].Text, StringComparison.Ordinal);
        Assert.Equal(Tx0, times.NodeTimes[GraphSamples.WKey]);
        Assert.Equal(Tx0, times.NodeTimes[GraphSamples.VKey]);
        Assert.Equal(Tx0.AddHours(1), times.NodeTimes[GraphSamples.KKey]);
        Assert.Equal(Tx0.AddHours(2), times.NodeTimes[GraphSamples.EKey]);
        Assert.Empty(GraphTimeline.Of(null).Ticks);
    }

    /// <summary>What came after a time is later; what has no confirmed movement belongs to now and is later too.</summary>
    [Fact]
    public void LaterIsAfterTheTime()
    {
        var times = GraphTimeline.Of(GraphSamples.Scene());
        var at = Tx0.AddHours(1);

        Assert.False(times.IsLaterNode(GraphSamples.KKey, at));
        Assert.True(times.IsLaterNode(GraphSamples.EKey, at));
        Assert.False(times.IsLaterEdge(GraphSamples.VK, at));
        Assert.True(times.IsLaterEdge(GraphSamples.KE, at));
        Assert.True(times.IsLaterNode("address:unknown", at));
        Assert.True(times.IsLaterEdge("unknown", at));
    }

    /// <summary>The strip runs from the first day's start to a little after the last transaction.</summary>
    [Fact]
    public void StripRunsFromTheFirstDayPastTheLast()
    {
        var axis = new TimeAxis(Tx0, TimeZoneInfo.Utc);
        var strip = new TimeStripLayout(axis, [Tx0, Tx0.AddHours(30)], 100, 1100);

        Assert.Equal(100, strip.X(0), 6);
        Assert.Equal(42, strip.LastTick, 6);
        Assert.Equal(42 + TimeStripLayout.LeastTail, strip.To, 6);
        Assert.True(strip.LastTickX < 1100);
        Assert.Equal(42, strip.HoursAt(strip.LastTickX), 6);
        Assert.Equal(0, strip.HoursAt(0), 6);
        Assert.Equal(1, strip.NearestTick(strip.X(42) + 3, 5));
        Assert.Null(strip.NearestTick(strip.X(42) + 30, 5));
        Assert.Throws<ArgumentException>(() => new TimeStripLayout(axis, [], 0, 100));
    }

    /// <summary>Strokes in the same pixel are drawn once, so that thousands of transactions cost no more.</summary>
    [Fact]
    public void StrokesShareAPixel()
    {
        var axis = new TimeAxis(Tx0, TimeZoneInfo.Utc);
        var strip = new TimeStripLayout(axis, [Tx0, Tx0.AddSeconds(1), Tx0.AddHours(20)], 0, 100);

        Assert.Equal([0, 2], strip.TickColumns().Select(column => column.Tick));
    }

    /// <summary>Each day is named while there is room, then every second day or week; days after the last are faint.</summary>
    [Fact]
    public void DaysAreNamedWhileThereIsRoom()
    {
        var axis = new TimeAxis(Tx0, TimeZoneInfo.Utc);
        var wide = new TimeStripLayout(axis, [Tx0, Tx0.AddHours(50)], 0, 1000);
        var narrow = new TimeStripLayout(axis, [Tx0, Tx0.AddHours(50)], 0, 160);

        var days = wide.Days(64);
        Assert.Equal(["1 november", "2 november", "3 november", "4 november"], days.Select(day => day.Name));
        Assert.True(days[^1].IsAfterLast);
        Assert.False(days[0].IsAfterLast);
        Assert.True(narrow.Days(64).Count < days.Count);
    }

    /// <summary>
    /// Choosing a time shows the graph as it was: later actors are faint and the head says so; Till nu goes back, and
    /// a graph without transactions shows no time line.
    /// </summary>
    [Fact]
    public void ChosenTimeFadesWhatCameLater()
    {
        var timeline = new GraphTimelineViewModel(TimeZoneInfo.Utc);
        var changes = 0;
        timeline.Changed += (_, _) => changes++;

        timeline.Show(GraphSamples.Scene());
        Assert.True(timeline.IsShown);
        Assert.False(timeline.ToNowCommand.CanExecute(null));
        Assert.False(timeline.IsLaterNode(GraphSamples.EKey));

        timeline.Choose(Tx0.AddHours(1));
        Assert.True(timeline.IsChosen);
        Assert.True(timeline.IsLaterNode(GraphSamples.EKey));
        Assert.True(timeline.IsLaterEdge(GraphSamples.KE));
        Assert.Equal("Grafen vid 1 november 13:00 · det som kom senare är blekt", timeline.HeadText);
        Assert.True(timeline.ToNowCommand.CanExecute(null));

        timeline.ToNowCommand.Execute(null);
        Assert.False(timeline.IsChosen);
        Assert.Equal(string.Empty, timeline.HeadText);
        Assert.False(timeline.IsLaterNode(GraphSamples.EKey));
        Assert.Equal(3, changes);

        timeline.Choose(Tx0);
        timeline.Show(null);
        Assert.False(timeline.IsShown);
        Assert.False(timeline.IsChosen);
        timeline.Choose(Tx0);
        Assert.False(timeline.IsChosen);
    }

    /// <summary>The time under the pointer draws again only when it changes.</summary>
    [Fact]
    public void HoverDrawsAgainOnlyOnChange()
    {
        var timeline = new GraphTimelineViewModel(TimeZoneInfo.Utc);
        timeline.Show(GraphSamples.Scene());
        var changes = 0;
        timeline.Changed += (_, _) => changes++;

        timeline.Hover(Tx0);
        timeline.Hover(Tx0);
        Assert.Equal(Tx0, timeline.Hovered);
        timeline.Hover(null);

        Assert.Equal(2, changes);
    }

    /// <summary>The graph gives the time line each graph it shows, and a new time draws the graph again.</summary>
    [Fact]
    public void GraphFeedsTheTimeline()
    {
        var session = new CaseSession();
        var workflow = new CaseWorkflow(
            new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), session);
        var graph = new GraphViewModel(
            session, workflow, new TestGraphSource(), TestWays.For(new TestExitConnection()), new InlineUi(),
            new TestGraphEditor(), new TestClock());
        session.Open(new TestCase("2026-0001"));
        var redraws = 0;
        graph.Redraw += (_, _) => redraws++;

        Assert.True(graph.Timeline.IsShown);
        graph.Timeline.Choose(Tx0);
        Assert.Equal(1, redraws);

        session.Forget();
        Assert.False(graph.Timeline.IsShown);
        Assert.False(graph.Timeline.IsChosen);
    }

    /// <summary>An actor's frame at a place, with its time.</summary>
    /// <param name="x">Its left edge.</param>
    /// <param name="y">Its top edge.</param>
    /// <param name="hours">When the money came to it.</param>
    /// <returns>The actor.</returns>
    private static TimedFrame Actor(double x, double y, double hours) =>
        new(GraphSizes.NodeFrame(new GraphPoint(x, y)), hours);
}
