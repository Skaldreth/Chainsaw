namespace Chainsaw.App.Graph;

/// <summary>A day's name under the strip, at the start of its day.</summary>
/// <param name="X">Where the day starts on the strip.</param>
/// <param name="Name">The day, for example <c>5 mars</c>.</param>
/// <param name="IsAfterLast">Whether the day begins after the last transaction, where nothing more is fetched.</param>
public readonly record struct StripDay(double X, string Name, bool IsAfterLast);

/// <summary>
/// The strip under the graph (sketch TL1a) laid out without WPF: from the start of the first day with a transaction to
/// a little after the last, one stroke per transaction, the days under it and what is under the pointer.
/// </summary>
public sealed class TimeStripLayout
{
    /// <summary>How far past the last transaction the strip runs at least, in hours: the dotted part.</summary>
    public const double LeastTail = 12;

    /// <summary>How far past the last transaction the strip runs, as a share of the time it shows.</summary>
    public const double TailShare = 0.08;

    /// <summary>The steps the days under the strip are named in, so that their names never meet.</summary>
    private static readonly int[] DaySteps = [1, 2, 7, 14, 28, 56, 112, 364];

    /// <summary>The strokes' times, in hours, in order.</summary>
    private readonly double[] _ticks;

    /// <summary>Lays out the strip.</summary>
    /// <param name="axis">The time axis; its origin is where the strip starts.</param>
    /// <param name="ticks">The transactions' times, in order; at least one.</param>
    /// <param name="left">Where the strip starts on screen.</param>
    /// <param name="right">Where the strip ends on screen.</param>
    public TimeStripLayout(TimeAxis axis, IReadOnlyList<DateTimeOffset> ticks, double left, double right)
    {
        ArgumentNullException.ThrowIfNull(axis);
        ArgumentNullException.ThrowIfNull(ticks);
        if (ticks.Count == 0)
        {
            throw new ArgumentException("The strip needs at least one transaction.", nameof(ticks));
        }

        Axis = axis;
        _ticks = [.. ticks.Select(axis.Hours)];
        Left = left;
        Right = Math.Max(left + 1, right);
        LastTick = _ticks.Max();
        To = LastTick + Math.Max(LeastTail, LastTick * TailShare);
    }

    /// <summary>Gets the time axis.</summary>
    public TimeAxis Axis { get; }

    /// <summary>Gets where the strip starts on screen: the start of the first day.</summary>
    public double Left { get; }

    /// <summary>Gets where the strip ends on screen.</summary>
    public double Right { get; }

    /// <summary>Gets the last transaction's time, in hours; after it the strip is dotted.</summary>
    public double LastTick { get; }

    /// <summary>Gets the time the strip ends at, in hours.</summary>
    public double To { get; }

    /// <summary>Gets where the last transaction stands on screen; the strip is whole up to it and dotted after.</summary>
    public double LastTickX => X(LastTick);

    /// <summary>Gives where a time stands on the strip.</summary>
    /// <param name="hours">The time, in hours on the axis.</param>
    /// <returns>The place on screen.</returns>
    public double X(double hours) => Left + ((hours / To) * (Right - Left));

    /// <summary>Gives the time under a place on the strip, kept within the strip.</summary>
    /// <param name="x">The place on screen.</param>
    /// <returns>The time, in hours on the axis.</returns>
    public double HoursAt(double x) => Math.Clamp((x - Left) / (Right - Left), 0, 1) * To;

    /// <summary>Gives the stroke nearest a place on the strip, when one is close enough.</summary>
    /// <param name="x">The place on screen.</param>
    /// <param name="tolerance">How far from the stroke the place may be.</param>
    /// <returns>The stroke's index, or <see langword="null"/> when none is that close.</returns>
    public int? NearestTick(double x, double tolerance)
    {
        int? nearest = null;
        var best = tolerance;
        for (var index = 0; index < _ticks.Length; index++)
        {
            var distance = Math.Abs(X(_ticks[index]) - x);
            if (distance <= best)
            {
                (nearest, best) = (index, distance);
            }
        }

        return nearest;
    }

    /// <summary>
    /// Gives the places to draw the strokes at, one per whole pixel, so that a case with thousands of transactions draws
    /// no more strokes than the strip is wide (principle 6).
    /// </summary>
    /// <returns>Each place with the first stroke there, in order.</returns>
    public IReadOnlyList<(double X, int Tick)> TickColumns()
    {
        var columns = new List<(double, int)>();
        var lastColumn = double.NaN;
        for (var index = 0; index < _ticks.Length; index++)
        {
            var column = Math.Round(X(_ticks[index]));
            if (column != lastColumn)
            {
                columns.Add((column, index));
                lastColumn = column;
            }
        }

        return columns;
    }

    /// <summary>
    /// Gives the days to name under the strip: every day while there is room, otherwise every second, every week and
    /// so on, so that two names never stand closer than a least distance.
    /// </summary>
    /// <param name="leastGap">The least distance between two names on screen.</param>
    /// <returns>The days, in order, from the first day.</returns>
    public IReadOnlyList<StripDay> Days(double leastGap)
    {
        var starts = new List<(double Hours, DateOnly Day)> { (0, Axis.DayOf(0)) };
        starts.AddRange(Axis.DayStarts(0, To));
        var dayWidth = (Right - Left) / Math.Max(1, To / 24);
        var step = DaySteps.FirstOrDefault(days => days * dayWidth >= leastGap, DaySteps[^1]);
        return [.. starts
            .Where((_, index) => index % step == 0)
            .Select(start => new StripDay(X(start.Hours), TimeAxis.DayName(start.Day), start.Hours > LastTick))];
    }
}
