using System.Globalization;

namespace Chainsaw.App.Graph;

/// <summary>A day's name under the strip, at the start of its day.</summary>
/// <param name="X">Where the day starts on the strip.</param>
/// <param name="Name">The day, for example <c>5 mars</c>.</param>
/// <param name="IsAfterLast">Whether the day begins after the last transaction, where nothing more is fetched.</param>
public readonly record struct StripDay(double X, string Name, bool IsAfterLast);

/// <summary>A mark on the strip inside the lens: a day's start or an hour, with its name when there is room.</summary>
/// <param name="X">Where it stands on the strip.</param>
/// <param name="Name">The day or the hour, for example <c>29 sep</c> or <c>06</c>, or <see langword="null"/>.</param>
/// <param name="IsDay">Whether it is a day's start, drawn taller.</param>
public readonly record struct StripMark(double X, string? Name, bool IsDay);

/// <summary>
/// The selection view's time window as the strip's lens (sketch TL2a): its first and last hour on the strip's axis.
/// </summary>
/// <param name="From">The window's start, in hours on the axis.</param>
/// <param name="To">The window's end, in hours on the axis.</param>
public readonly record struct StripWindow(double From, double To);

/// <summary>
/// The strip under the graph (sketches TL1a and TL2a) laid out without WPF: from the start of the first day with a
/// transaction to the newest block the case knows, or a little after the last transaction when it knows none; one
/// stroke per transaction, the days under it and what is under the pointer. With the selection view's time window it
/// is a lens: the window takes more than half the strip, with days and hours, and the rest is pressed together on its
/// sides with only the weeks left.
/// </summary>
public sealed class TimeStripLayout
{
    /// <summary>The share of the strip the window takes under the lens (sketch TL2a).</summary>
    public const double LensShare = 0.56;

    /// <summary>The least hours the lens holds, so that a window round a single moment still has room.</summary>
    private const double LeastWindow = 1;

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
    /// <param name="window">The selection view's window as a lens, or <see langword="null"/> for an even strip.</param>
    /// <param name="newestBlock">
    /// The newest block's time in hours on the axis, where the strip ends when it lies after the last transaction; or
    /// <see langword="null"/> when the case knows none.
    /// </param>
    public TimeStripLayout(
        TimeAxis axis, IReadOnlyList<DateTimeOffset> ticks, double left, double right, StripWindow? window = null,
        double? newestBlock = null)
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
        NewestBlock = newestBlock > LastTick ? newestBlock : null;
        To = NewestBlock ?? LastTick + Math.Max(LeastTail, LastTick * TailShare);
        Window = Clamp(window);
    }

    /// <summary>Gets the newest block's time in hours, where the strip ends, or <see langword="null"/>.</summary>
    public double? NewestBlock { get; }

    /// <summary>Gets the lens's window within the strip, or <see langword="null"/> for an even strip.</summary>
    public StripWindow? Window { get; }

    /// <summary>Gets where the strip ends on screen: the newest block, when it is known.</summary>
    public double EndX => X(To);

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
    public double X(double hours)
    {
        var width = Right - Left;
        if (Window is not { } window)
        {
            return Left + ((hours / To) * width);
        }

        var (lensLeft, lensWidth) = Lens(window);
        if (hours < window.From)
        {
            return Left + (window.From <= 0 ? 0 : hours / window.From * lensLeft);
        }

        if (hours <= window.To)
        {
            return Left + lensLeft + ((hours - window.From) / (window.To - window.From) * lensWidth);
        }

        var rest = width - lensLeft - lensWidth;
        return Left + lensLeft + lensWidth + (To <= window.To ? rest : (hours - window.To) / (To - window.To) * rest);
    }

    /// <summary>Gives the time under a place on the strip, kept within the strip.</summary>
    /// <param name="x">The place on screen.</param>
    /// <returns>The time, in hours on the axis.</returns>
    public double HoursAt(double x)
    {
        var share = Math.Clamp((x - Left) / (Right - Left), 0, 1);
        if (Window is not { } window)
        {
            return share * To;
        }

        var offset = share * (Right - Left);
        var (lensLeft, lensWidth) = Lens(window);
        if (offset < lensLeft)
        {
            return lensLeft <= 0 ? window.From : offset / lensLeft * window.From;
        }

        if (offset <= lensLeft + lensWidth)
        {
            return window.From + ((offset - lensLeft) / lensWidth * (window.To - window.From));
        }

        var rest = Right - Left - lensLeft - lensWidth;
        return rest <= 0 ? To : window.To + ((offset - lensLeft - lensWidth) / rest * (To - window.To));
    }

    /// <summary>
    /// Gives the marks inside the lens: each day's start, named, and the hours between them, as often as there is
    /// room (every hour, every third or every sixth), named when they are far enough apart.
    /// </summary>
    /// <returns>The marks, in order; none without a lens.</returns>
    public IReadOnlyList<StripMark> LensMarks()
    {
        if (Window is not { } window)
        {
            return [];
        }

        var perHour = (X(window.To) - X(window.From)) / (window.To - window.From);
        var (step, named) = perHour switch
        {
            > 30 => (1, 1),
            > 5 => (3, 6),
            _ => (6, 0),
        };
        var marks = new List<StripMark>();
        for (var hour = Math.Ceiling(window.From / step) * step; hour <= window.To; hour += step)
        {
            var isDay = Axis.DayOf(hour) != Axis.DayOf(hour - 0.5);
            marks.Add(new StripMark(X(hour), MarkName(hour, isDay, named), isDay));
        }

        return marks;
    }

    /// <summary>Names a mark in the lens: a day by its date, an hour when hours are named that often.</summary>
    /// <param name="hours">The mark's time, in hours on the axis.</param>
    /// <param name="isDay">Whether it is a day's start.</param>
    /// <param name="every">How many hours apart the hours are named; 0 when none are.</param>
    /// <returns>For example <c>29 september</c> or <c>06</c>, or <see langword="null"/>.</returns>
    private string? MarkName(double hours, bool isDay, int every)
    {
        if (isDay)
        {
            return TimeAxis.DayName(Axis.DayOf(hours));
        }

        var hour = Axis.HourOf(Axis.Time(hours));
        return every > 0 && hour % every == 0 ? hour.ToString("00", CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Gives where the lens starts on the strip and how wide it is.</summary>
    /// <param name="window">The window.</param>
    /// <returns>Its left edge from the strip's start, and its width.</returns>
    private (double Left, double Width) Lens(StripWindow window)
    {
        var width = Right - Left;
        var outside = window.From + (To - window.To);
        if (outside <= 0)
        {
            return (0, width);
        }

        var focus = LensShare * width;
        return ((width - focus) * window.From / outside, focus);
    }

    /// <summary>Keeps a window within the strip, and drops one that lies outside it.</summary>
    /// <param name="window">The window.</param>
    /// <returns>The window within the strip, or <see langword="null"/>.</returns>
    private StripWindow? Clamp(StripWindow? window)
    {
        if (window is not { } asked || asked.To <= 0 || asked.From >= To)
        {
            return null;
        }

        var from = Math.Max(0, asked.From);
        var to = Math.Min(To, Math.Max(asked.To, from + LeastWindow));
        return to <= from ? null : new StripWindow(from, to);
    }

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
        if (Window is { } window)
        {
            return WeeksAround(window, leastGap);
        }

        var starts = new List<(double Hours, DateOnly Day)> { (0, Axis.DayOf(0)) };
        starts.AddRange(Axis.DayStarts(0, To));
        var dayWidth = (Right - Left) / Math.Max(1, To / 24);
        var step = DaySteps.FirstOrDefault(days => days * dayWidth >= leastGap, DaySteps[^1]);
        return [.. starts
            .Where((_, index) => index % step == 0)
            .Select(start => new StripDay(X(start.Hours), TimeAxis.DayName(start.Day), start.Hours > LastTick))];
    }

    /// <summary>
    /// Gives the days named outside the lens (sketch TL2a): only the weeks' Mondays, never closer than a least distance
    /// to each other or to the lens, whose own days are named by <see cref="LensMarks"/>.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="leastGap">The least distance between two names on screen.</param>
    /// <returns>The days, in order.</returns>
    private List<StripDay> WeeksAround(StripWindow window, double leastGap)
    {
        var lensFrom = X(window.From) - (leastGap / 2);
        var lensTo = X(window.To) + (leastGap / 2);
        var days = new List<StripDay>();
        var last = double.NegativeInfinity;
        foreach (var (hours, day) in Axis.DayStarts(0, To))
        {
            var x = X(hours);
            var outside = x < lensFrom || x > lensTo;
            if (day.DayOfWeek == DayOfWeek.Monday && outside && x - last >= leastGap && EndX - x >= leastGap / 2)
            {
                days.Add(new StripDay(x, TimeAxis.DayName(day), hours > LastTick));
                last = x;
            }
        }

        return days;
    }
}
