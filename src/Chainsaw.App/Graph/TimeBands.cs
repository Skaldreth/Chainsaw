namespace Chainsaw.App.Graph;

/// <summary>An actor as the time bands see it: where its frame stands and when the money came to it.</summary>
/// <param name="Frame">The actor's frame on the surface.</param>
/// <param name="Hours">When the money came to it, in hours on the <see cref="TimeAxis"/>.</param>
public readonly record struct TimedFrame(GraphRect Frame, double Hours);

/// <summary>
/// A line through the graph at one time (sketch TL1a): every actor the money reached by then stands to its left and
/// every later one to its right. It runs from the top of the actors to their bottom, one point per step down.
/// </summary>
/// <param name="Hours">The time, in hours on the <see cref="TimeAxis"/>.</param>
/// <param name="Points">The line's points, from the top down.</param>
public sealed record TimeBandLine(double Hours, IReadOnlyList<GraphPoint> Points)
{
    /// <summary>Gets where the line begins, at the top.</summary>
    public double Top => Points[0].Y;

    /// <summary>Gets where the line ends, at the bottom.</summary>
    public double Bottom => Points[^1].Y;

    /// <summary>Gives where the line stands at a height, between its points; its ends above and below it.</summary>
    /// <param name="y">The height on the surface.</param>
    /// <returns>The line's horizontal place there.</returns>
    public double XAt(double y)
    {
        if (y <= Top)
        {
            return Points[0].X;
        }

        if (y >= Bottom)
        {
            return Points[^1].X;
        }

        var step = (Bottom - Top) / (Points.Count - 1);
        var index = Math.Min(Points.Count - 2, (int)((y - Top) / step));
        var (above, below) = (Points[index], Points[index + 1]);
        return above.X + ((below.X - above.X) * ((y - above.Y) / (below.Y - above.Y)));
    }

    /// <summary>
    /// Gives the points that are on screen between two heights, with one more at each end so that the drawn line
    /// reaches the screen's edges (principle 6: only what is seen is drawn).
    /// </summary>
    /// <param name="top">The visible top.</param>
    /// <param name="bottom">The visible bottom.</param>
    /// <returns>The points, in order; empty when none of the line is on screen.</returns>
    public IReadOnlyList<GraphPoint> Within(double top, double bottom)
    {
        if (bottom < Top || top > Bottom)
        {
            return [];
        }

        var step = (Bottom - Top) / (Points.Count - 1);
        var first = Math.Clamp((int)Math.Floor((top - Top) / step), 0, Points.Count - 1);
        var last = Math.Clamp((int)Math.Ceiling((bottom - Top) / step), 0, Points.Count - 1);
        return [.. Points.Skip(first).Take(last - first + 1)];
    }
}

/// <summary>
/// The geometry of the time bands over the graph (sketch TL1a), without WPF: where the line between two days, or the
/// line at a chosen time, goes through the actors. The line starts straight at its natural place, is pushed aside
/// where an actor of the wrong side is in the way and is smoothed, so that it bends round actors the user dragged out
/// of their day; in the hop columns, where the actors stand in order, it stays nearly straight.
/// </summary>
public static class TimeBands
{
    /// <summary>The space a line keeps from an actor's frame.</summary>
    public const double Clearance = 18;

    /// <summary>How far above and below an actor a line still leans away from it.</summary>
    public const double Reach = 110;

    /// <summary>How far the line may lean back at the end of <see cref="Reach"/>, so that it bends and never kinks.</summary>
    public const double Lean = 260;

    /// <summary>The step between a line's points, on the surface.</summary>
    public const double Step = 6;

    /// <summary>The most points one line has, so that a very tall graph costs no more than a tall one.</summary>
    public const int MostPoints = 2000;

    /// <summary>How many times a line is smoothed.</summary>
    public const int SmoothingPasses = 80;

    /// <summary>How far past the top and bottom actors a line runs, the part where it fades in and out.</summary>
    public const double EndMargin = 72;

    /// <summary>How far into the line, as a share of its length, it has faded in at the top.</summary>
    public const double FadeIn = 0.12;

    /// <summary>Where, as a share of its length, the line starts to fade out at the bottom.</summary>
    public const double FadeOut = 0.86;

    /// <summary>
    /// The least share of the gap between two actors a line keeps from either, so that a time just after an actor does
    /// not draw the line onto its edge.
    /// </summary>
    private const double LeastShare = 0.15;

    /// <summary>Gives the heights the lines run between: the actors' top and bottom, and the margin to fade in.</summary>
    /// <param name="actors">The actors.</param>
    /// <returns>The top and the bottom, or <see langword="null"/> when there are no actors.</returns>
    public static (double Top, double Bottom)? Extent(IReadOnlyList<TimedFrame> actors)
    {
        ArgumentNullException.ThrowIfNull(actors);
        if (actors.Count == 0)
        {
            return null;
        }

        return (actors.Min(actor => actor.Frame.Y) - EndMargin, actors.Max(actor => actor.Frame.Bottom) + EndMargin);
    }

    /// <summary>
    /// Gives where the line at a time would stand with nothing in the way: at the place, left to right, that leaves
    /// the fewest actors on the wrong side of the time, so that one actor dragged out of its day does not move the
    /// line but is passed round by it. In the gap there, the line stands as far in as the time is between the two
    /// actors on either side.
    /// </summary>
    /// <param name="actors">The actors.</param>
    /// <param name="hours">The time.</param>
    /// <returns>The place, or <see langword="null"/> when every actor is on the same side of the time.</returns>
    public static double? BaseX(IReadOnlyList<TimedFrame> actors, double hours)
    {
        ArgumentNullException.ThrowIfNull(actors);
        if (actors.All(actor => actor.Hours <= hours) || actors.All(actor => actor.Hours > hours))
        {
            return null;
        }

        var sorted = actors.OrderBy(actor => actor.Frame.Centre.X).ThenBy(actor => actor.Hours).ToList();
        var split = BestSplit(sorted, hours);
        var (left, right) = (sorted[split - 1], sorted[split]);
        var share = left.Hours <= hours && right.Hours > hours
            ? Math.Clamp((hours - left.Hours) / (right.Hours - left.Hours), LeastShare, 1 - LeastShare)
            : 0.5;
        if (left.Frame.Right < right.Frame.X)
        {
            return left.Frame.Right + ((right.Frame.X - left.Frame.Right) * share);
        }

        // Frames that overlap leave no gap; the line starts between their middles and bends round them.
        return left.Frame.Centre.X + ((right.Frame.Centre.X - left.Frame.Centre.X) * share);
    }

    /// <summary>Gives the line through the actors at a time, from the top of the extent to its bottom.</summary>
    /// <param name="actors">The actors.</param>
    /// <param name="hours">The time.</param>
    /// <param name="top">Where the line begins.</param>
    /// <param name="bottom">Where the line ends.</param>
    /// <returns>The line, or <see langword="null"/> when every actor is on the same side of the time.</returns>
    public static TimeBandLine? Line(IReadOnlyList<TimedFrame> actors, double hours, double top, double bottom)
    {
        if (BaseX(actors, hours) is not { } start || bottom <= top)
        {
            return null;
        }

        var count = Math.Clamp((int)Math.Ceiling((bottom - top) / Step) + 1, 2, MostPoints);
        var step = (bottom - top) / (count - 1);
        var (low, high) = Bounds(actors, hours, top, step, count);
        var xs = Enumerable.Range(0, count).Select(index => Clamp(start, low[index], high[index])).ToArray();
        for (var pass = 0; pass < SmoothingPasses; pass++)
        {
            xs = Smoothed(xs, low, high);
        }

        return new TimeBandLine(hours, [.. xs.Select((x, index) => new GraphPoint(x, top + (index * step)))]);
    }

    /// <summary>
    /// Gives where, among the actors sorted left to right, a line at a time is best put: the split that leaves the
    /// fewest actors on the wrong side, and of those the widest gap.
    /// </summary>
    /// <param name="sorted">The actors, left to right; at least one on each side of the time.</param>
    /// <param name="hours">The time.</param>
    /// <returns>The index of the first actor right of the line; never the first or past the last.</returns>
    private static int BestSplit(IReadOnlyList<TimedFrame> sorted, double hours)
    {
        // Wrong at a split: the actors left of it that came later, and those right of it reached by then.
        var wrong = sorted.Count(actor => actor.Hours <= hours);
        var best = (Split: 1, Wrong: int.MaxValue, Gap: double.NegativeInfinity);
        for (var split = 1; split < sorted.Count; split++)
        {
            wrong += sorted[split - 1].Hours > hours ? 1 : -1;
            var gap = sorted[split].Frame.Centre.X - sorted[split - 1].Frame.Centre.X;
            if (wrong < best.Wrong || (wrong == best.Wrong && gap > best.Gap))
            {
                best = (split, wrong, gap);
            }
        }

        return best.Split;
    }

    /// <summary>
    /// Gives, for every point of a line, how far left and right it may stand: right of every actor reached by the time
    /// and left of every later one, hard beside an actor and easing away above and below it.
    /// </summary>
    /// <param name="actors">The actors.</param>
    /// <param name="hours">The time.</param>
    /// <param name="top">The line's top.</param>
    /// <param name="step">The distance between its points.</param>
    /// <param name="count">How many points it has.</param>
    /// <returns>The least and the most place of each point.</returns>
    private static (double[] Low, double[] High) Bounds(
        IReadOnlyList<TimedFrame> actors, double hours, double top, double step, int count)
    {
        var low = Enumerable.Repeat(double.NegativeInfinity, count).ToArray();
        var high = Enumerable.Repeat(double.PositiveInfinity, count).ToArray();
        foreach (var actor in actors)
        {
            var frame = actor.Frame;
            var first = Math.Max(0, (int)Math.Floor((frame.Y - Clearance - Reach - top) / step));
            var last = Math.Min(count - 1, (int)Math.Ceiling((frame.Bottom + Clearance + Reach - top) / step));
            var reached = actor.Hours <= hours;
            for (var index = first; index <= last; index++)
            {
                var y = top + (index * step);
                var distance = Math.Max(0, Math.Max(frame.Y - Clearance - y, y - (frame.Bottom + Clearance)));
                if (distance >= Reach)
                {
                    continue;
                }

                var give = Lean * (1 - Math.Cos(distance / Reach * Math.PI / 2));
                if (reached)
                {
                    low[index] = Math.Max(low[index], frame.Right + Clearance - give);
                }
                else
                {
                    high[index] = Math.Min(high[index], frame.X - Clearance + give);
                }
            }
        }

        return (low, high);
    }

    /// <summary>Smooths a line once, each point towards its neighbours, and keeps it within its bounds.</summary>
    /// <param name="xs">The line's places.</param>
    /// <param name="low">The least place of each point.</param>
    /// <param name="high">The most place of each point.</param>
    /// <returns>The smoothed places.</returns>
    private static double[] Smoothed(double[] xs, double[] low, double[] high)
    {
        var next = new double[xs.Length];
        for (var index = 0; index < xs.Length; index++)
        {
            var above = xs[Math.Max(0, index - 1)];
            var below = xs[Math.Min(xs.Length - 1, index + 1)];
            next[index] = Clamp((above + (2 * xs[index]) + below) / 4, low[index], high[index]);
        }

        return next;
    }

    /// <summary>
    /// Keeps a place within its bounds; where an actor on each side leaves no room, the later actor's side wins, as
    /// in the sketch.
    /// </summary>
    /// <param name="x">The place.</param>
    /// <param name="low">The least place.</param>
    /// <param name="high">The most place.</param>
    /// <returns>The place kept within them.</returns>
    private static double Clamp(double x, double low, double high) => Math.Min(Math.Max(x, low), high);
}
