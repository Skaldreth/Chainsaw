using System.Globalization;

namespace Chainsaw.App.Graph;

/// <summary>How far the view Tidslinje is zoomed (sketch TL3a): the whole case in days, or a window of hours.</summary>
public enum TimelineZoom
{
    /// <summary>Days: the whole case, with the empty stretches between cut out.</summary>
    Days,

    /// <summary>Hours: a window around a moment, an hour at a time.</summary>
    Hours,
}

/// <summary>A stretch of time laid out along the x axis.</summary>
/// <param name="From">Where it starts.</param>
/// <param name="To">Where it ends.</param>
/// <param name="X0">Its left edge.</param>
/// <param name="X1">Its right edge.</param>
public sealed record TimelinePiece(DateTimeOffset From, DateTimeOffset To, double X0, double X1);

/// <summary>An empty stretch cut out of the days, drawn as a narrow band with a zigzag and its length.</summary>
/// <param name="X0">Its left edge.</param>
/// <param name="X1">Its right edge.</param>
/// <param name="Days">How many empty days it stands for.</param>
public sealed record TimelineCut(double X0, double X1, int Days)
{
    /// <summary>Gets what stands above it, for example <c>18 d</c>.</summary>
    public string Text => Days.ToString(CultureInfo.InvariantCulture) + " d";
}

/// <summary>A dotted line across the lanes: the start of a day, or an hour.</summary>
/// <param name="X">Where it stands.</param>
/// <param name="Label">What stands above it, or an empty text.</param>
/// <param name="Strong">The part of the label in the brighter ink (the day's number), or an empty text.</param>
/// <param name="IsDayStart">Whether it is midnight, drawn firmer in the hours.</param>
public sealed record TimeTick(double X, string Label, string Strong, bool IsDayStart);

/// <summary>
/// Lays time out along the x axis of the view Tidslinje (sketch TL3a), without WPF. In days the whole case is shown,
/// a day at a time; the empty stretches longer than <see cref="LongestGap"/> are cut out and marked. In hours a window
/// of <see cref="HoursShown"/> hours around a moment is shown.
/// </summary>
public sealed class TimelineScale
{
    /// <summary>How many empty days in a row are kept before a stretch is cut out.</summary>
    public const int LongestGap = 2;

    /// <summary>How wide a cut is drawn.</summary>
    public const double CutWidth = 40;

    /// <summary>How many hours the hours show.</summary>
    public const int HoursShown = 14;

    /// <summary>How narrow a day may be before only some days are labelled.</summary>
    private const double LabelRoom = 64;

    /// <summary>The weekdays as the view writes them, Sunday first as <see cref="DayOfWeek"/> counts.</summary>
    private static readonly string[] Weekdays = ["sön", "mån", "tis", "ons", "tor", "fre", "lör"];

    /// <summary>The months as the view writes them.</summary>
    private static readonly string[] Months =
        ["jan", "feb", "mar", "apr", "maj", "jun", "jul", "aug", "sep", "okt", "nov", "dec"];

    /// <summary>Makes a scale.</summary>
    /// <param name="zoom">How far it is zoomed.</param>
    /// <param name="pieces">The stretches shown.</param>
    /// <param name="cuts">The stretches cut out.</param>
    /// <param name="ticks">The dotted lines.</param>
    /// <param name="zone">The time zone the days are counted in.</param>
    private TimelineScale(
        TimelineZoom zoom,
        IReadOnlyList<TimelinePiece> pieces,
        IReadOnlyList<TimelineCut> cuts,
        IReadOnlyList<TimeTick> ticks,
        TimeZoneInfo zone)
    {
        (Zoom, Pieces, Cuts, Ticks, Zone) = (zoom, pieces, cuts, ticks, zone);
    }

    /// <summary>Gets how far it is zoomed.</summary>
    public TimelineZoom Zoom { get; }

    /// <summary>Gets the stretches shown, left to right.</summary>
    public IReadOnlyList<TimelinePiece> Pieces { get; }

    /// <summary>Gets the stretches cut out.</summary>
    public IReadOnlyList<TimelineCut> Cuts { get; }

    /// <summary>Gets the dotted lines.</summary>
    public IReadOnlyList<TimeTick> Ticks { get; }

    /// <summary>Gets the time zone the days are counted in.</summary>
    public TimeZoneInfo Zone { get; }

    /// <summary>Gets where the shown time starts.</summary>
    public DateTimeOffset From => Pieces.Count > 0 ? Pieces[0].From : DateTimeOffset.MinValue;

    /// <summary>Gets where the shown time ends.</summary>
    public DateTimeOffset To => Pieces.Count > 0 ? Pieces[^1].To : DateTimeOffset.MinValue;

    /// <summary>Gives where a time stands, or <see langword="null"/> when it is not shown.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The x, or <see langword="null"/>.</returns>
    public double? XOf(DateTimeOffset time)
    {
        foreach (var piece in Pieces)
        {
            if (time >= piece.From && time <= piece.To)
            {
                var share = (time - piece.From).TotalSeconds / Math.Max(1, (piece.To - piece.From).TotalSeconds);
                return piece.X0 + (share * (piece.X1 - piece.X0));
            }
        }

        return null;
    }

    /// <summary>Lays out whole days around the times, cutting out the long empty stretches between them.</summary>
    /// <param name="times">The times of the events.</param>
    /// <param name="zone">The time zone the days are counted in.</param>
    /// <param name="width">How wide the room is.</param>
    /// <returns>The scale; empty when there are no times.</returns>
    public static TimelineScale Days(IEnumerable<DateTimeOffset> times, TimeZoneInfo zone, double width)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(zone);
        var days = times.Select(time => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, zone).DateTime))
            .Distinct().Order().ToList();
        if (days.Count == 0)
        {
            return new TimelineScale(TimelineZoom.Days, [], [], [], zone);
        }

        var runs = Runs(days);
        var dayCount = runs.Sum(run => run.End.DayNumber - run.Start.DayNumber + 1);
        var dayWidth = Math.Max(1, (width - ((runs.Count - 1) * CutWidth)) / dayCount);
        var every = Math.Max(1, (int)Math.Ceiling(LabelRoom / dayWidth));
        var pieces = new List<TimelinePiece>();
        var cuts = new List<TimelineCut>();
        var ticks = new List<TimeTick>();
        var x = 0.0;
        var labelled = 0;
        for (var index = 0; index < runs.Count; index++)
        {
            var (start, end) = runs[index];
            if (index > 0)
            {
                cuts.Add(new TimelineCut(x, x + CutWidth, start.DayNumber - runs[index - 1].End.DayNumber - 1));
                x += CutWidth;
            }

            var length = end.DayNumber - start.DayNumber + 1;
            var right = x + (length * dayWidth);
            pieces.Add(new TimelinePiece(Midnight(start, zone), Midnight(end.AddDays(1), zone), x, right));
            for (var day = start; day <= end.AddDays(1); day = day.AddDays(1))
            {
                var shown = day <= end && labelled++ % every == 0;
                ticks.Add(new TimeTick(
                    x + ((day.DayNumber - start.DayNumber) * dayWidth),
                    shown ? DayLabel(day) : string.Empty, shown ? Number(day.Day) : string.Empty, true));
            }

            x += length * dayWidth;
        }

        return new TimelineScale(TimelineZoom.Days, pieces, cuts, ticks, zone);
    }

    /// <summary>Lays out a window of hours around a moment.</summary>
    /// <param name="centre">The moment in the middle.</param>
    /// <param name="zone">The time zone the hours are counted in.</param>
    /// <param name="width">How wide the room is.</param>
    /// <returns>The scale.</returns>
    public static TimelineScale Hours(DateTimeOffset centre, TimeZoneInfo zone, double width)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(centre, zone);
        var startLocal = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, DateTimeKind.Unspecified)
            .AddHours(-(HoursShown / 2));
        var from = Local(startLocal, zone);
        var to = from.AddHours(HoursShown);
        var hourWidth = width / HoursShown;
        var ticks = new List<TimeTick>();
        for (var hour = 0; hour <= HoursShown; hour++)
        {
            var at = startLocal.AddHours(hour);
            var x = hour * hourWidth;
            if (at.Hour == 0)
            {
                ticks.Add(new TimeTick(x, DayLabel(DateOnly.FromDateTime(at)), Number(at.Day), true));
            }
            else
            {
                var label = at.Hour % 2 == 0 ? at.Hour.ToString("00", CultureInfo.InvariantCulture) : string.Empty;
                ticks.Add(new TimeTick(x, label, string.Empty, false));
            }
        }

        return new TimelineScale(TimelineZoom.Hours, [new TimelinePiece(from, to, 0, width)], [], ticks, zone);
    }

    /// <summary>Writes a day as the view does, for example <c>tis 13 okt</c>.</summary>
    /// <param name="day">The day.</param>
    /// <returns>The text.</returns>
    public static string DayLabel(DateOnly day) =>
        $"{Weekdays[(int)day.DayOfWeek]} {Number(day.Day)} {Months[day.Month - 1]}";

    /// <summary>Writes a moment as the inspector does, for example <c>tis 13 okt 2026 11:30</c>.</summary>
    /// <param name="time">The moment.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>The text.</returns>
    public static string Long(DateTimeOffset time, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(time, zone);
        return $"{DayLabel(DateOnly.FromDateTime(local.DateTime))} {local.Year.ToString(CultureInfo.InvariantCulture)} "
            + local.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Writes a moment shortly, for example <c>13 okt 11:30</c>.</summary>
    /// <param name="time">The moment.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>The text.</returns>
    public static string Short(DateTimeOffset time, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(time, zone);
        return $"{Number(local.Day)} {Months[local.Month - 1]} "
            + local.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Writes a day without the weekday, for example <c>13 okt</c>.</summary>
    /// <param name="time">The moment.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>The text.</returns>
    public static string Date(DateTimeOffset time, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(time, zone);
        return $"{Number(local.Day)} {Months[local.Month - 1]}";
    }

    /// <summary>Writes a moment's hour shortly, for example <c>15 okt 20</c>.</summary>
    /// <param name="time">The moment.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>The text.</returns>
    public static string DateHour(DateTimeOffset time, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(time, zone);
        return Date(time, zone) + " " + local.ToString("HH", CultureInfo.InvariantCulture);
    }

    /// <summary>Writes a moment in UTC, for example <c>09:30 UTC</c>.</summary>
    /// <param name="time">The moment.</param>
    /// <returns>The text.</returns>
    public static string Utc(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";

    /// <summary>Groups days into runs, where no more than <see cref="LongestGap"/> empty days lie between two.</summary>
    /// <param name="days">The days, in order.</param>
    /// <returns>The runs.</returns>
    private static List<(DateOnly Start, DateOnly End)> Runs(IReadOnlyList<DateOnly> days)
    {
        var runs = new List<(DateOnly Start, DateOnly End)> { (days[0], days[0]) };
        foreach (var day in days.Skip(1))
        {
            var (start, end) = runs[^1];
            if (day.DayNumber - end.DayNumber - 1 <= LongestGap)
            {
                runs[^1] = (start, day);
            }
            else
            {
                runs.Add((day, day));
            }
        }

        return runs;
    }

    /// <summary>Gives the start of a day in a time zone.</summary>
    /// <param name="day">The day.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>The moment.</returns>
    private static DateTimeOffset Midnight(DateOnly day, TimeZoneInfo zone) =>
        Local(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone);

    /// <summary>Gives the moment of a local time in a time zone.</summary>
    /// <param name="local">The local time.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>The moment.</returns>
    private static DateTimeOffset Local(DateTime local, TimeZoneInfo zone)
    {
        var offset = zone.IsInvalidTime(local) ? zone.BaseUtcOffset : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    /// <summary>Writes a number as the view does.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The text.</returns>
    private static string Number(int number) => number.ToString(CultureInfo.InvariantCulture);
}
