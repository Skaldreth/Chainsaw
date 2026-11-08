using System.Globalization;

namespace Chainsaw.App.Graph;

/// <summary>
/// How the held-back transactions lie around the address (sketches EX1a and EX1b, two modes of one view).
/// </summary>
public enum HeldBackMode
{
    /// <summary>On a spiral out from the address, in time or amount order (EX1a).</summary>
    Spiral,

    /// <summary>On an arc over the address: the angle is the time, the distance the amount (EX1b).</summary>
    TimeArc,
}

/// <summary>The spiral's order (sketch EX2).</summary>
public enum HeldBackOrder
{
    /// <summary>The newest innermost.</summary>
    Time,

    /// <summary>The largest innermost.</summary>
    Amount,
}

/// <summary>How a press chooses (sketch EX2).</summary>
public enum HeldBackTool
{
    /// <summary>A click chooses one, Shift-click a run.</summary>
    Click,

    /// <summary>A drawn ring chooses what lies inside it (L).</summary>
    Lasso,
}

/// <summary>Which side's transactions are shown (sketch EX2, <em>Riktning</em>).</summary>
public enum HeldBackDirection
{
    /// <summary>Only what the address received.</summary>
    In,

    /// <summary>Only what it paid.</summary>
    Out,

    /// <summary>Both.</summary>
    All,
}

/// <summary>
/// A dot in the selection view (sketch EX2): a transaction, or a counterparty's transactions when grouped. Its colour
/// is
/// the counterparty's group, its size the amount, filled when the address paid, hollow when it received.
/// </summary>
/// <param name="Choices">The transactions it stands for.</param>
/// <param name="At">Where it lies, relative to the address's centre.</param>
/// <param name="Size">Its diameter.</param>
/// <param name="Btc">The amount in whole bitcoin, summed when grouped.</param>
/// <param name="Time">The newest transaction's time, or <see langword="null"/> while unconfirmed.</param>
/// <param name="IsNear">Whether the amount is near the traced one (sonar in the accent).</param>
public sealed record HeldBackDot(
    IReadOnlyList<HeldBackChoice> Choices, GraphPoint At, double Size, double Btc, DateTimeOffset? Time, bool IsNear)
{
    /// <summary>Gets the first transaction, which speaks for the counterparty.</summary>
    public HeldBackChoice First => Choices[0];

    /// <summary>Gets the dot's key: its first transaction's id.</summary>
    public string Key => First.Key;

    /// <summary>Gets whether the address paid.</summary>
    public bool IsOut => First.IsOut;

    /// <summary>Gets how many transactions it stands for.</summary>
    public int Count => Choices.Count;

    /// <summary>Gets whether it pulses: a negative label, or an amount near the traced one.</summary>
    public bool Pulses => First.IsNegative || IsNear;
}

/// <summary>A word on the selection view's guides: a date, an amount, the traced arrival.</summary>
/// <param name="At">Its centre, relative to the address's centre.</param>
/// <param name="Text">The word.</param>
/// <param name="IsAccent">Whether it is the traced arrival's, in the accent.</param>
public sealed record GuideLabel(GraphPoint At, string Text, bool IsAccent);

/// <summary>
/// The faint guides the dots lie on (sketch EX2): dotted lines (the spiral, the arcs), short solid ticks, the dashed
/// ray of the traced arrival, and their words. Everything relative to the address's centre.
/// </summary>
/// <param name="Dotted">The dotted lines, each a run of points.</param>
/// <param name="Ticks">The short ticks.</param>
/// <param name="Arrival">The traced arrival's ray, or <see langword="null"/>.</param>
/// <param name="Labels">The words.</param>
public sealed record HeldBackGuides(
    IReadOnlyList<IReadOnlyList<GraphPoint>> Dotted,
    IReadOnlyList<(GraphPoint From, GraphPoint To)> Ticks,
    (GraphPoint From, GraphPoint To)? Arrival,
    IReadOnlyList<GuideLabel> Labels)
{
    /// <summary>Gets no guides.</summary>
    public static HeldBackGuides None { get; } = new([], [], null, []);
}

/// <summary>
/// The geometry of the selection view (sketch EX2, with the sizes of its HTML): where a dot lies on the spiral or on
/// the time arc, how large it is, and the guides. Without WPF, so that it is tested.
/// </summary>
public static class HeldBackLayout
{
    /// <summary>The spiral's first radius, clear of the address's card.</summary>
    public const double SpiralStart = 96;

    /// <summary>How far apart the spiral's turns lie.</summary>
    public const double SpiralGap = 36;

    /// <summary>How far along the spiral one dot lies from the next.</summary>
    public const double SpiralStep = 25;

    /// <summary>How far from straight left and right the time arc stops, in radians.</summary>
    public const double ArcMargin = 0.22;

    /// <summary>The time arc's radius for the smallest amounts.</summary>
    public const double ArcInner = 130;

    /// <summary>How much further out the largest amounts lie on the time arc.</summary>
    public const double ArcSpan = 300;

    /// <summary>The smallest dot.</summary>
    public const double DotSmallest = 5;

    /// <summary>How much larger the largest dot is.</summary>
    public const double DotGrowth = 13;

    /// <summary>The smallest dot that stands for several transactions, so that its count can be read.</summary>
    public const double GroupedDot = 16;

    /// <summary>How far a word lies outside the spiral.</summary>
    private const double LabelOffset = 20;

    /// <summary>The time arc's guides for these amounts, in bitcoin.</summary>
    private static readonly double[] AmountGuides = [0.01, 0.1, 1];

    /// <summary>The Swedish months, short.</summary>
    private static readonly string[] Months =
        ["jan", "feb", "mar", "apr", "maj", "jun", "jul", "aug", "sep", "okt", "nov", "dec"];

    /// <summary>Gives where the n-th dot lies on the spiral, the first at the top.</summary>
    /// <param name="index">The dot's place, which may be fractional for the guide.</param>
    /// <returns>The point, relative to the centre.</returns>
    public static GraphPoint SpiralAt(double index)
    {
        var a = SpiralGap / (4 * Math.PI);
        var length = index * SpiralStep;
        var theta = (-SpiralStart + Math.Sqrt((SpiralStart * SpiralStart) + (4 * a * length))) / (2 * a);
        var radius = SpiralStart + (SpiralGap * theta / (2 * Math.PI));
        var angle = theta - (Math.PI / 2);
        return new GraphPoint(radius * Math.Cos(angle), radius * Math.Sin(angle));
    }

    /// <summary>Gives the time arc's angle for a time inside a window: the earliest left, the latest right.</summary>
    /// <param name="time">The time.</param>
    /// <param name="from">The window's start.</param>
    /// <param name="to">The window's end.</param>
    /// <returns>The angle in radians, over the centre.</returns>
    public static double ArcAngle(DateTimeOffset time, DateTimeOffset from, DateTimeOffset to)
    {
        var span = (to - from).TotalSeconds;
        var share = span <= 0 ? 0.5 : Math.Clamp((time - from).TotalSeconds / span, 0, 1);
        return Math.PI + ArcMargin + (share * (Math.PI - (2 * ArcMargin)));
    }

    /// <summary>Gives the time arc's radius for an amount: the larger, the further out.</summary>
    /// <param name="btc">The amount in bitcoin.</param>
    /// <returns>The radius.</returns>
    public static double ArcRadius(double btc) =>
        ArcInner + (ArcSpan * Math.Clamp((Math.Log10(Math.Max(btc, 1e-9)) + 3.2) / 3.75, 0, 1));

    /// <summary>Gives a point on the time arc.</summary>
    /// <param name="angle">The angle.</param>
    /// <param name="radius">The radius.</param>
    /// <returns>The point, relative to the centre.</returns>
    public static GraphPoint OnArc(double angle, double radius) =>
        new(radius * Math.Cos(angle), radius * Math.Sin(angle));

    /// <summary>
    /// Gives a dot's diameter for an amount; a dot for several transactions is never too small to count.
    /// </summary>
    /// <param name="btc">The amount in bitcoin.</param>
    /// <param name="count">How many transactions it stands for.</param>
    /// <returns>The diameter.</returns>
    public static double DotSize(double btc, int count)
    {
        var size = DotSmallest + (DotGrowth * Math.Clamp((Math.Log10(Math.Max(btc, 1e-9)) + 3) / 3.55, 0, 1));
        return count > 1 ? Math.Max(size, GroupedDot) : size;
    }

    /// <summary>
    /// Gives the spiral's guides: the dotted spiral through the dots, and a word where the month (or the day, in a
    /// short window) changes, or where the amount falls below 1, 0,1 and 0,01 BTC.
    /// </summary>
    /// <param name="dots">The dots, in the spiral's order.</param>
    /// <param name="order">The order.</param>
    /// <param name="shortWindow">Whether the window is shorter than twelve days, so that days are named.</param>
    /// <param name="zone">The time zone dates are written in.</param>
    /// <returns>The guides.</returns>
    public static HeldBackGuides SpiralGuides(
        IReadOnlyList<HeldBackDot> dots, HeldBackOrder order, bool shortWindow, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(dots);
        var points = new List<GraphPoint>();
        for (var index = 0.0; index <= Math.Max(1, dots.Count - 1); index += 0.5)
        {
            points.Add(SpiralAt(index));
        }

        var marks = order == HeldBackOrder.Time ? TimeMarks(dots, shortWindow, zone) : AmountMarks(dots);
        var labels = marks.Select(mark =>
        {
            var at = SpiralAt(mark.Index);
            var length = Math.Max(1, Math.Sqrt((at.X * at.X) + (at.Y * at.Y)));
            var outward = new GraphPoint(
                at.X + (at.X / length * LabelOffset), at.Y + (at.Y / length * LabelOffset));
            return new GuideLabel(outward, mark.Text, false);
        }).ToList();
        return new HeldBackGuides([points], [], null, labels);
    }

    /// <summary>
    /// Gives the time arc's guides: dotted arcs at 0,01, 0,1 and 1 BTC with their amounts, a tick and a date per day
    /// (or per month in a long window), and the traced arrival's dashed ray when it lies in the window.
    /// </summary>
    /// <param name="from">The window's start.</param>
    /// <param name="to">The window's end.</param>
    /// <param name="arrival">The traced arrival, or <see langword="null"/>.</param>
    /// <param name="arrivalText">The arrival's word, for example <c>spårad ankomst · 0,92 BTC</c>.</param>
    /// <param name="zone">The time zone dates are written in.</param>
    /// <returns>The guides.</returns>
    public static HeldBackGuides ArcGuides(
        DateTimeOffset from, DateTimeOffset to, DateTimeOffset? arrival, string arrivalText, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var start = Math.PI + ArcMargin;
        var end = (2 * Math.PI) - ArcMargin;
        var dotted = new List<IReadOnlyList<GraphPoint>>();
        var labels = new List<GuideLabel>();
        foreach (var btc in AmountGuides)
        {
            var radius = ArcRadius(btc);
            dotted.Add([.. Enumerable.Range(0, 49).Select(step => OnArc(start + ((end - start) * step / 48), radius))]);
            var tip = OnArc(end, radius);
            labels.Add(new GuideLabel(new GraphPoint(tip.X + 24, tip.Y + 6), ShortBtc(btc) + " BTC", false));
        }

        var outer = ArcRadius(4) + 26;
        var ticks = new List<(GraphPoint, GraphPoint)>();
        foreach (var (time, text) in Ticks(from, to, zone))
        {
            var angle = ArcAngle(time, from, to);
            ticks.Add((OnArc(angle, outer - 14), OnArc(angle, outer - 6)));
            labels.Add(new GuideLabel(OnArc(angle, outer + 12), text, false));
        }

        (GraphPoint, GraphPoint)? ray = null;
        if (arrival is { } at && at >= from && at <= to)
        {
            var angle = ArcAngle(at, from, to);
            ray = (OnArc(angle, 70), OnArc(angle, outer - 4));
            var word = OnArc(angle, outer + 30);
            labels.Add(new GuideLabel(new GraphPoint(word.X, word.Y - 14), arrivalText, true));
        }

        return new HeldBackGuides(dotted, ticks, ray, labels);
    }

    /// <summary>Writes a day as the guides do: <c>27 feb</c>, with the time when asked: <c>27 feb 14:10</c>.</summary>
    /// <param name="time">The time.</param>
    /// <param name="zone">The time zone.</param>
    /// <param name="withTime">Whether the time of day is written.</param>
    /// <returns>The text.</returns>
    public static string Day(DateTimeOffset time, TimeZoneInfo zone, bool withTime)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(time, zone);
        var day = string.Create(CultureInfo.InvariantCulture, $"{local.Day} {Months[local.Month - 1]}");
        return withTime ? day + local.ToString(" HH:mm", CultureInfo.InvariantCulture) : day;
    }

    /// <summary>Writes an amount in bitcoin as briefly as the guides do: <c>0,01</c>, <c>1</c>.</summary>
    /// <param name="btc">The amount.</param>
    /// <returns>The text.</returns>
    public static string ShortBtc(double btc)
    {
        var swedish = CultureInfo.GetCultureInfo("sv-SE");
        if (btc < 0.0001)
        {
            return btc.ToString("0.########", swedish);
        }

        return btc < 0.01 ? btc.ToString("G1", swedish) : btc.ToString(btc < 1 ? "0.##" : "0.#", swedish);
    }

    /// <summary>Writes an amount in bitcoin as the view does: more decimals the smaller it is.</summary>
    /// <param name="btc">The amount.</param>
    /// <returns>For example <c>0,918</c> or <c>4,97</c>.</returns>
    public static string Btc(double btc)
    {
        var format = btc switch
        {
            < 0.0001 => "0.00000000",
            < 0.01 => "0.0000",
            < 1 => "0.000",
            _ => "0.00",
        };
        return btc.ToString(format, CultureInfo.GetCultureInfo("sv-SE"));
    }

    /// <summary>Gives the words of the spiral in time order: where the month or the day changes.</summary>
    /// <param name="dots">The dots.</param>
    /// <param name="shortWindow">Whether days are named.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>The words at their dots' places.</returns>
    private static List<(double Index, string Text)> TimeMarks(
        IReadOnlyList<HeldBackDot> dots, bool shortWindow, TimeZoneInfo zone)
    {
        var marks = new List<(double, string)>();
        string? last = null;
        for (var index = 0; index < dots.Count; index++)
        {
            if (dots[index].Time is not { } time)
            {
                continue;
            }

            var local = TimeZoneInfo.ConvertTime(time, zone);
            var key = shortWindow
                ? local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : local.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            if (key == last)
            {
                continue;
            }

            last = key;
            marks.Add((index, shortWindow ? Day(time, zone, false) : MonthName(local, index == 0)));
        }

        return marks;
    }

    /// <summary>
    /// Gives the words of the spiral in amount order: <c>störst</c>, then where it falls below a guide.
    /// </summary>
    /// <param name="dots">The dots, the largest first.</param>
    /// <returns>The words at their dots' places.</returns>
    private static List<(double Index, string Text)> AmountMarks(IReadOnlyList<HeldBackDot> dots)
    {
        var marks = new List<(double, string)>();
        if (dots.Count > 0)
        {
            marks.Add((0, "störst"));
        }

        foreach (var btc in AmountGuides.Reverse())
        {
            var index = dots.ToList().FindIndex(dot => dot.Btc < btc);
            if (index > 0)
            {
                marks.Add((index, "< " + ShortBtc(btc) + " BTC"));
            }
        }

        return marks;
    }

    /// <summary>Gives the time arc's ticks: each day in a short window, each month's first day in a long one.</summary>
    /// <param name="from">The window's start.</param>
    /// <param name="to">The window's end.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>The times and their words.</returns>
    private static List<(DateTimeOffset Time, string Text)> Ticks(
        DateTimeOffset from, DateTimeOffset to, TimeZoneInfo zone)
    {
        var ticks = new List<(DateTimeOffset, string)>();
        var local = TimeZoneInfo.ConvertTime(from, zone);
        if (to - from < TimeSpan.FromDays(12))
        {
            var last = TimeZoneInfo.ConvertTime(to, zone).DateTime;
            for (var day = local.Date.AddDays(1); day <= last; day = day.AddDays(1))
            {
                var at = new DateTimeOffset(day, zone.GetUtcOffset(day));
                ticks.Add((at, Day(at, zone, false)));
            }

            return ticks;
        }

        var month = new DateTime(local.Year, local.Month, 1).AddMonths(1);
        var first = true;
        while (new DateTimeOffset(month, zone.GetUtcOffset(month)) <= to && ticks.Count < 36)
        {
            var at = new DateTimeOffset(month, zone.GetUtcOffset(month));
            ticks.Add((at, MonthName(TimeZoneInfo.ConvertTime(at, zone), first)));
            first = false;
            month = month.AddMonths(1);
        }

        return ticks;
    }

    /// <summary>Names a month, with the year in January and for the first.</summary>
    /// <param name="time">A time in the month.</param>
    /// <param name="first">Whether it is the first named.</param>
    /// <returns>For example <c>feb</c> or <c>jan 2026</c>.</returns>
    private static string MonthName(DateTimeOffset time, bool first)
    {
        var name = Months[time.Month - 1];
        return time.Month == 1 || first
            ? name + " " + time.Year.ToString(CultureInfo.InvariantCulture)
            : name;
    }
}
