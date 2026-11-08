using System.Globalization;

namespace Chainsaw.App.Graph;

/// <summary>A stroke in the overview strip: an event at its true place in the whole case.</summary>
/// <param name="X">Where it stands.</param>
/// <param name="Brush">Its colour, as a resource key.</param>
/// <param name="IsHidden">Whether its chip is off.</param>
public sealed record TimelineStripMark(double X, string Brush, bool IsHidden);

/// <summary>A day line in the overview strip, with the date under every week.</summary>
/// <param name="X">Where it stands.</param>
/// <param name="Label">The date, or an empty text.</param>
/// <param name="IsWeek">Whether it starts a week, drawn taller.</param>
public sealed record TimelineStripTick(double X, string Label, bool IsWeek);

/// <summary>
/// The overview strip at the bottom of the view Tidslinje (sketch TL3a): the whole case at its true pace, a stroke per
/// event, and the windows of what is shown above.
/// </summary>
/// <param name="Width">How wide it is laid out.</param>
/// <param name="Ticks">The day lines.</param>
/// <param name="Marks">The events.</param>
/// <param name="Windows">The stretches shown above, as their left and right edges.</param>
public sealed record TimelineStrip(
    double Width,
    IReadOnlyList<TimelineStripTick> Ticks,
    IReadOnlyList<TimelineStripMark> Marks,
    IReadOnlyList<(double X0, double X1)> Windows)
{
    /// <summary>Gets an empty strip.</summary>
    public static TimelineStrip None { get; } = new(0, [], [], []);
}

/// <summary>
/// Places the timeline's events on the lanes and in the overview strip (sketch TL3a), without WPF: an event where its
/// time stands on the scale, bundles of transfers that would share a pixel column in days, and the amount beside a
/// stroke only where there is room for it.
/// </summary>
public static class TimelineLayout
{
    /// <summary>How much room an amount needs beside its stroke before the next one.</summary>
    private const double AmountRoom = 52;

    /// <summary>How much room the value needs under the amount in the hours.</summary>
    private const double ValueRoom = 60;

    /// <summary>How many days apart the strip's dates stand.</summary>
    private const int WeekDays = 7;

    /// <summary>Places the events on the lanes.</summary>
    /// <param name="data">The lanes and events.</param>
    /// <param name="scale">The time along the x axis.</param>
    /// <param name="hidden">The chips that are off.</param>
    /// <param name="selectedId">The chosen event's id, or <see langword="null"/>.</param>
    /// <param name="now">Now.</param>
    /// <param name="valueOf">Gives a transfer's value at the time, or an empty text.</param>
    /// <returns>The picture.</returns>
    public static TimelinePicture Lay(
        TimelineData data,
        TimelineScale scale,
        IReadOnlySet<TimelineFilter> hidden,
        string? selectedId,
        DateTimeOffset now,
        Func<TimelineEvent, string> valueOf)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(scale);
        ArgumentNullException.ThrowIfNull(hidden);
        ArgumentNullException.ThrowIfNull(valueOf);
        var laneOf = data.Lanes.Select((lane, index) => (lane.Key, index))
            .ToDictionary(pair => pair.Key, pair => pair.index, StringComparer.Ordinal);
        var caseLane = laneOf.GetValueOrDefault(TimelineReading.CaseLane, -1);
        var edge = scale.Pieces.Count > 0 ? scale.Pieces[^1].X1 : 0;
        var xs = new List<(TimelineEvent Event, double X)>();
        foreach (var each in data.Events)
        {
            var x = each.Time is { } time ? scale.XOf(time) : (scale.Zoom == TimelineZoom.Days ? edge : null);
            if (x is { } at)
            {
                xs.Add((each, at));
            }
        }

        var transfers = xs.Where(pair => pair.Event.IsTransfer).ToList();
        var order = transfers.Select((pair, index) => (pair.Event.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.Ordinal);
        var largest = data.Events.Where(each => each.IsTransfer && each.Amount is not null)
            .Select(each => (double)each.Amount!.Value.Units).DefaultIfEmpty(1).Max();
        var placed = new List<PlacedEvent>();
        for (var index = 0; index < xs.Count; index++)
        {
            var (each, x) = xs[index];
            var isHidden = hidden.Contains(each.Filter);
            if (each.IsTransfer)
            {
                var next = order[each.Id] + 1;
                var room = next >= transfers.Count ? double.MaxValue : transfers[next].X - x;
                var chosen = each.Id == selectedId;
                var hours = scale.Zoom == TimelineZoom.Hours;
                var amount = room > AmountRoom || hours || chosen ? AmountOf(each) : string.Empty;
                var value = chosen || (hours && room > ValueRoom) ? valueOf(each) : string.Empty;
                var thickness = Thickness(each, largest);
                placed.Add(new PlacedEvent(each, x, laneOf[each.From!], laneOf[each.To!], isHidden, amount, value,
                    thickness));
            }
            else
            {
                placed.Add(new PlacedEvent(each, x, caseLane, laneOf[each.On!], isHidden, AmountOf(each), string.Empty,
                    Thickness(each, largest)));
            }
        }

        var bundles = scale.Zoom == TimelineZoom.Days ? Bundles(placed) : [];
        var selected = data.Events.FirstOrDefault(each => each.Id == selectedId);
        var lit = new[] { selected?.From, selected?.To, selected?.On }.OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        return new TimelinePicture(data.Lanes, placed, bundles, scale, scale.XOf(now), selectedId, lit);
    }

    /// <summary>Lays the overview strip out over the whole case.</summary>
    /// <param name="data">The lanes and events.</param>
    /// <param name="scale">The time shown above.</param>
    /// <param name="hidden">The chips that are off.</param>
    /// <param name="width">How wide the strip is.</param>
    /// <returns>The strip; empty without a confirmed event.</returns>
    public static TimelineStrip Strip(
        TimelineData data, TimelineScale scale, IReadOnlySet<TimelineFilter> hidden, double width)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(scale);
        ArgumentNullException.ThrowIfNull(hidden);
        var times = data.Events.Where(each => each.Time is not null).Select(each => each.Time!.Value).ToList();
        if (times.Count == 0 || width <= 0)
        {
            return TimelineStrip.None;
        }

        var zone = scale.Zone;
        var first = StartOfDay(times.Min(), zone);
        var last = StartOfDay(times.Max(), zone).AddDays(1);
        var span = Math.Max(1, (last - first).TotalSeconds);
        double X(DateTimeOffset time) => Math.Clamp((time - first).TotalSeconds / span, 0, 1) * width;
        var ticks = new List<TimelineStripTick>();
        var days = (int)Math.Round((last - first).TotalDays);
        var step = Math.Max(1, (int)Math.Ceiling(days / (width / 6)));
        for (var day = 0; day <= days; day += step)
        {
            var at = first.AddDays(day);
            var week = day % WeekDays == 0;
            ticks.Add(new TimelineStripTick(X(at), week ? TimelineScale.Date(at, zone) : string.Empty, week));
        }

        var marks = data.Events.Where(each => each.Time is not null)
            .Select(each => new TimelineStripMark(X(each.Time!.Value), TimelineReading.BrushOf(each.Kind),
                hidden.Contains(each.Filter)))
            .ToList();
        var windows = scale.Pieces.Select(piece => (X(piece.From), X(piece.To))).ToList();
        return new TimelineStrip(width, ticks, marks, windows);
    }

    /// <summary>Gives how thick an event's stroke is: from a hair to the thickest for the largest amount.</summary>
    /// <param name="timelineEvent">The event.</param>
    /// <param name="largest">The largest transfer's units.</param>
    /// <returns>The thickness.</returns>
    public static double Thickness(TimelineEvent timelineEvent, double largest)
    {
        ArgumentNullException.ThrowIfNull(timelineEvent);
        const double Thinnest = 1.4, Added = 4.6;
        var units = timelineEvent.Amount is { } amount ? (double)amount.Units : 0;
        return Thinnest + (Added * Math.Clamp(units / Math.Max(1, largest), 0, 1));
    }

    /// <summary>Writes an event's amount as it stands beside its stroke, or an empty text when it has none.</summary>
    /// <param name="timelineEvent">The event.</param>
    /// <returns>The amount.</returns>
    public static string AmountOf(TimelineEvent timelineEvent)
    {
        ArgumentNullException.ThrowIfNull(timelineEvent);
        return timelineEvent.Amount is { } amount ? AmountText.Number(amount) : string.Empty;
    }

    /// <summary>
    /// Gives the bundles: transfers from the same sender in a row that stand closer than
    /// <see cref="TimelinePicture.BundleDistance"/>.
    /// </summary>
    /// <param name="placed">The events placed.</param>
    /// <returns>The bundles of two or more.</returns>
    private static List<TimelineBundle> Bundles(IReadOnlyList<PlacedEvent> placed)
    {
        var runs = new List<List<PlacedEvent>>();
        foreach (var each in placed.Where(p => p.Event.IsTransfer).OrderBy(p => p.X))
        {
            var run = runs.Count > 0 ? runs[^1] : null;
            if (run is not null && each.X - run[^1].X < TimelinePicture.BundleDistance && run[^1].Lane == each.Lane)
            {
                run.Add(each);
            }
            else
            {
                runs.Add([each]);
            }
        }

        return [.. runs.Where(run => run.Count > 1).Select(Bundle)];
    }

    /// <summary>Makes a bundle of a run of transfers.</summary>
    /// <param name="run">The transfers.</param>
    /// <returns>The bundle.</returns>
    private static TimelineBundle Bundle(List<PlacedEvent> run)
    {
        var lanes = run.SelectMany(p => new[] { p.Lane, p.ToLane }).ToList();
        var times = run.Select(p => p.Event.Time).OfType<DateTimeOffset>().ToList();
        var minutes = times.Count > 1 ? (int)Math.Round((times.Max() - times.Min()).TotalMinutes) : 0;
        var tip = $"{run.Count.ToString(CultureInfo.InvariantCulture)} tx på "
            + $"{minutes.ToString(CultureInfo.InvariantCulture)} min · klicka för timmar";
        return new TimelineBundle(
            [.. run.Select(p => p.Event)], (run[0].X + run[^1].X) / 2, run[0].X, run[^1].X, run[0].Lane, lanes.Min(),
            lanes.Max(), tip);
    }

    /// <summary>Gives the start of the day a moment falls on.</summary>
    /// <param name="time">The moment.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>The day's start.</returns>
    private static DateTimeOffset StartOfDay(DateTimeOffset time, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(time, zone);
        return new DateTimeOffset(local.Date, local.Offset);
    }
}
