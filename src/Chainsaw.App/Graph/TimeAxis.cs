using System.Globalization;

namespace Chainsaw.App.Graph;

/// <summary>
/// The time line under the graph (sketch TL1a) counts time as hours from the start of the first day with a
/// transaction, in the user's own time zone, so that times compare as numbers and the days fall where the user's
/// calendar has them. It also writes times and days as the timeline shows them, for example <c>5 mars 12:00</c>.
/// </summary>
public sealed class TimeAxis
{
    /// <summary>The culture the days and months are written in.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>The time zone the days are counted in.</summary>
    private readonly TimeZoneInfo _zone;

    /// <summary>Creates the axis from a first time.</summary>
    /// <param name="first">The earliest time on the axis; the axis starts at the beginning of its day.</param>
    /// <param name="zone">The time zone the days are counted in.</param>
    public TimeAxis(DateTimeOffset first, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        _zone = zone;
        Origin = DayStart(LocalDate(first));
    }

    /// <summary>Gets the start of the first day: hour 0 on the axis.</summary>
    public DateTimeOffset Origin { get; }

    /// <summary>Gives a time as hours from the axis's origin.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The hours, negative before the origin.</returns>
    public double Hours(DateTimeOffset time) => (time - Origin).TotalHours;

    /// <summary>Gives the time at a number of hours from the origin.</summary>
    /// <param name="hours">The hours.</param>
    /// <returns>The time, to the whole minute.</returns>
    public DateTimeOffset Time(double hours) => Origin.AddMinutes(Math.Round(hours * 60));

    /// <summary>
    /// Gives the starts of the days (local midnight) that lie strictly between two times on the axis, in order: where
    /// a band ends and the next begins.
    /// </summary>
    /// <param name="from">The first time, in hours.</param>
    /// <param name="to">The last time, in hours.</param>
    /// <returns>Each day's start in hours, with its date.</returns>
    public IReadOnlyList<(double Hours, DateOnly Day)> DayStarts(double from, double to)
    {
        var days = new List<(double, DateOnly)>();
        var day = LocalDate(Time(from)).AddDays(1);
        for (var hours = Hours(DayStart(day)); hours < to; hours = Hours(DayStart(day)))
        {
            if (hours > from)
            {
                days.Add((hours, day));
            }

            day = day.AddDays(1);
        }

        return days;
    }

    /// <summary>Gives the day a time on the axis falls on, in the axis's time zone.</summary>
    /// <param name="hours">The time, in hours.</param>
    /// <returns>The day.</returns>
    public DateOnly DayOf(double hours) => LocalDate(Time(hours));

    /// <summary>Writes a day as the timeline shows it, for example <c>5 mars</c>.</summary>
    /// <param name="day">The day.</param>
    /// <returns>The text.</returns>
    public static string DayName(DateOnly day) => day.ToString("d MMMM", Swedish);

    /// <summary>Writes a time as the timeline shows it, for example <c>5 mars 12:00</c>, in the axis's time zone.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The text.</returns>
    public string TimeName(DateTimeOffset time) =>
        TimeZoneInfo.ConvertTime(time, _zone).ToString("d MMMM HH:mm", Swedish);

    /// <summary>Gives a time's hour of the day in the axis's time zone.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The hour, 0 to 23.</returns>
    public int HourOf(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, _zone).Hour;

    /// <summary>Gives a time's date in the axis's time zone.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The date.</returns>
    private DateOnly LocalDate(DateTimeOffset time) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, _zone).Date);

    /// <summary>Gives the start of a day (its local midnight) as a moment in time.</summary>
    /// <param name="day">The day.</param>
    /// <returns>The moment.</returns>
    private DateTimeOffset DayStart(DateOnly day)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        // A midnight that a change to summer time skips is taken an hour later, when the day has begun.
        while (_zone.IsInvalidTime(midnight))
        {
            midnight = midnight.AddHours(1);
        }

        return new DateTimeOffset(midnight, _zone.GetUtcOffset(midnight));
    }
}
