using System.Globalization;
using System.Numerics;

namespace Chainsaw.App.Graph;

/// <summary>A column of the Sankey: the sources, or a hop.</summary>
/// <param name="X">The left edge of its bars.</param>
/// <param name="Title">What stands above it, for example <c>Offren</c> or <c>2 hopp</c>.</param>
public sealed record FlowColumn(double X, string Title);

/// <summary>An actor's bar in the Sankey, as tall as the money through it.</summary>
/// <param name="Key">The actor's key.</param>
/// <param name="X">Its left edge.</param>
/// <param name="Y">Its top.</param>
/// <param name="Height">Its height.</param>
/// <param name="Name">The actor's name.</param>
/// <param name="Line">What stands under the name: the amount through it, and <c>förslag</c> on an uncertain way.</param>
/// <param name="Look">How the actor's sort is drawn: the icon by the name.</param>
/// <param name="Brush">The bar's colour, as a resource key.</param>
/// <param name="IsTerminal">Whether money ends at it at a service, so that its words stand to the right.</param>
/// <param name="IsSanctioned">Whether the sanction mark stands at its corner.</param>
public sealed record FlowBar(
    string Key, double X, double Y, double Height, string Name, string Line, SortLook Look, string Brush,
    bool IsTerminal, bool IsSanctioned)
{
    /// <summary>Gets whether the bar is drawn faint and dashed, as an uncertain actor (a suggestion).</summary>
    public bool IsDashed => Look.IsDashed;
}

/// <summary>A strand of a band: one part of the money on one step, from one bar to the next.</summary>
/// <param name="Lot">The part.</param>
/// <param name="StepKey">The step, <c>from&gt;to</c>.</param>
/// <param name="X0">Where it leaves.</param>
/// <param name="Y0">Its top where it leaves.</param>
/// <param name="X1">Where it arrives.</param>
/// <param name="Y1">Its top where it arrives.</param>
/// <param name="Height">Its height.</param>
/// <param name="Brush">Its colour: where the money ended, or grey when it is not the victims'.</param>
/// <param name="IsGuess">Whether the step is uncertain, drawn striped and dashed.</param>
public sealed record FlowStrand(
    FlowLot Lot, string StepKey, double X0, double Y0, double X1, double Y1, double Height, string Brush,
    bool IsGuess)
{
    /// <summary>Gives the top of the strand at an x between its ends, along the soft curve it is drawn as.</summary>
    /// <param name="x">The x.</param>
    /// <returns>The top there.</returns>
    public double TopAt(double x)
    {
        var share = Math.Clamp((x - X0) / Math.Max(1, X1 - X0), 0, 1);
        var eased = share * share * (3 - (2 * share));
        return Y0 + ((Y1 - Y0) * eased);
    }
}

/// <summary>What stays at an actor: a short stub to the right of its bar, in the accent.</summary>
/// <param name="Key">The actor's key.</param>
/// <param name="X">Its left edge.</param>
/// <param name="Y">Its top.</param>
/// <param name="Height">Its height.</param>
/// <param name="Text">What stands beside it, for example <c>ligger kvar · 0,0900</c>.</param>
public sealed record FlowStub(string Key, double X, double Y, double Height, string Text);

/// <summary>What a click in the Sankey chose: a step between two actors, or an actor.</summary>
/// <param name="Key">The step's key <c>from&gt;to</c>, or the actor's key.</param>
/// <param name="IsStep">Whether it is a step.</param>
/// <param name="ToolTip">What the pointer's tooltip says.</param>
public sealed record FlowHit(string Key, bool IsStep, string ToolTip);

/// <summary>The Sankey laid out (sketch FL1a): the columns, the bars, the strands and the stubs.</summary>
/// <param name="Width">How wide it is laid out.</param>
/// <param name="Height">How tall.</param>
/// <param name="Columns">The columns, left to right.</param>
/// <param name="Bars">The actors' bars.</param>
/// <param name="Strands">The strands.</param>
/// <param name="Stubs">What stays.</param>
/// <param name="Hidden">How many small parts are too thin to draw.</param>
public sealed record FlowPicture(
    double Width,
    double Height,
    IReadOnlyList<FlowColumn> Columns,
    IReadOnlyList<FlowBar> Bars,
    IReadOnlyList<FlowStrand> Strands,
    IReadOnlyList<FlowStub> Stubs,
    int Hidden)
{
    /// <summary>Gets an empty picture.</summary>
    public static FlowPicture None { get; } = new(0, 0, [], [], [], [], 0);

    /// <summary>Gets whether anything is drawn.</summary>
    public bool HasBars => Bars.Count > 0;

    /// <summary>Gives what lies under a point: a bar with its words, or a band between two bars.</summary>
    /// <param name="x">The point's x.</param>
    /// <param name="y">The point's y.</param>
    /// <returns>The hit, or <see langword="null"/>.</returns>
    public FlowHit? At(double x, double y)
    {
        foreach (var bar in Bars)
        {
            if (x >= bar.X - 4 && x <= bar.X + FlowLayout.BarWidth + 4 && y >= bar.Y - 4 && y <= bar.Y + bar.Height + 4)
            {
                return new FlowHit(bar.Key, false, bar.Name + " · " + bar.Line);
            }
        }

        foreach (var strand in Strands)
        {
            var top = strand.TopAt(x);
            if (x > strand.X0 && x < strand.X1 && y >= top - 2 && y <= top + Math.Max(4, strand.Height) + 2)
            {
                return new FlowHit(strand.StepKey, true, string.Empty);
            }
        }

        return null;
    }
}

/// <summary>
/// Lays the flow out as a Sankey (sketch FL1a), without WPF: a column per hop from the sources on the left, a bar per
/// actor as tall as the money through it, and the parts as strands stacked at both ends so that the bands do not
/// cross more than they must. Parts too thin to see are left out and counted.
/// </summary>
public static class FlowLayout
{
    /// <summary>How wide a bar is.</summary>
    public const double BarWidth = 12;

    /// <summary>Where the bars start below the columns' titles.</summary>
    public const double Top = 56;

    /// <summary>How wide the room right of the last column is kept for the terminals' words.</summary>
    public const double WordsRoom = 132;

    /// <summary>How long a stub of what stays is.</summary>
    public const double StubLength = 34;

    /// <summary>The share of the victims' money below which a part is too thin to draw.</summary>
    public const double Thinnest = 0.002;

    /// <summary>The largest gap between two bars of a column.</summary>
    private const double LargestGap = 46;

    /// <summary>The smallest gap between two bars of a column.</summary>
    private const double SmallestGap = 10;

    /// <summary>The lowest a bar is drawn, so that a small actor can still be seen and clicked.</summary>
    private const double LowestBar = 3;

    /// <summary>The order of the ends in a stack, as the side panel lists them.</summary>
    private static readonly FlowEnd[] EndOrder =
        [FlowEnd.CashOut, FlowEnd.Spent, FlowEnd.Risk, FlowEnd.OtherChain, FlowEnd.Kept];

    /// <summary>What the layout needs to know about an actor: its name and sort.</summary>
    /// <param name="Name">Its name.</param>
    /// <param name="Look">How its sort is drawn.</param>
    /// <param name="IsSanctioned">Whether it carries the sanction mark.</param>
    public sealed record Actor(string Name, SortLook Look, bool IsSanctioned);

    /// <summary>Lays the flow out.</summary>
    /// <param name="flow">The flow.</param>
    /// <param name="actors">The actors by key.</param>
    /// <param name="width">How wide the room is.</param>
    /// <param name="height">How tall.</param>
    /// <param name="write">Writes the amount of some parts in the unit shown.</param>
    /// <returns>The picture; empty when none of the victims' money moved.</returns>
    public static FlowPicture Lay(
        FlowResult flow,
        IReadOnlyDictionary<string, Actor> actors,
        double width,
        double height,
        Func<IEnumerable<FlowLot>, string> write)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(write);
        var total = flow.VictimUnits;
        if (total.Sign <= 0 || width <= 0 || height <= 0)
        {
            return FlowPicture.None;
        }

        var shown = flow.Lots.Where(lot => (double)lot.Units >= (double)total * Thinnest && Known(lot, actors)).ToList();
        var hidden = flow.Lots.Count - shown.Count;
        var columnOf = Columns(shown);
        var count = columnOf.Values.DefaultIfEmpty(0).Max() + 1;
        var through = Through(shown);
        var span = count > 1 ? (width - WordsRoom - BarWidth) / (count - 1) : 0;
        var members = Enumerable.Range(0, count)
            .Select(column => columnOf.Where(pair => pair.Value == column).Select(pair => pair.Key).ToList())
            .ToList();
        var gap = Gap(members, height);
        var scale = Scale(members, through, height, gap);
        var places = Place(members, shown, through, scale, gap, height, span);
        var strands = Strands(shown, places, scale, flow.Steps);
        var bars = Bars(shown, places, through, actors, write);
        var stubs = Stubs(shown, places, scale, write);
        var columns = Enumerable.Range(0, count)
            .Select(column => new FlowColumn(column * span, Title(column, members[column], shown)))
            .ToList();
        return new FlowPicture(width, height, columns, bars, strands, stubs, hidden);
    }

    /// <summary>Gives whether every actor of a part's path is known to the layout.</summary>
    /// <param name="lot">The part.</param>
    /// <param name="actors">The actors.</param>
    /// <returns><see langword="true"/> when all are.</returns>
    private static bool Known(FlowLot lot, IReadOnlyDictionary<string, Actor> actors) =>
        lot.Path.All(actors.ContainsKey);

    /// <summary>
    /// Gives each actor its column: the furthest it stands from a source in any part's path, so that every step runs
    /// to the right as far as it can.
    /// </summary>
    /// <param name="lots">The parts drawn.</param>
    /// <returns>The columns by actor.</returns>
    private static Dictionary<string, int> Columns(IReadOnlyList<FlowLot> lots)
    {
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var lot in lots)
        {
            for (var index = 0; index < lot.Path.Count; index++)
            {
                columns[lot.Path[index]] = Math.Max(columns.GetValueOrDefault(lot.Path[index], 0), index);
            }
        }

        // A source never stands right of where its money goes.
        foreach (var source in lots.Select(lot => lot.Path[0]).Distinct())
        {
            columns[source] = 0;
        }

        return columns;
    }

    /// <summary>Gives how much money passes each actor.</summary>
    /// <param name="lots">The parts drawn.</param>
    /// <returns>The units by actor.</returns>
    private static Dictionary<string, BigInteger> Through(IReadOnlyList<FlowLot> lots)
    {
        var through = new Dictionary<string, BigInteger>(StringComparer.Ordinal);
        foreach (var lot in lots)
        {
            foreach (var key in lot.Path.Distinct())
            {
                through[key] = through.GetValueOrDefault(key) + lot.Units;
            }
        }

        return through;
    }

    /// <summary>Gives the gap between bars: smaller when a column holds many.</summary>
    /// <param name="members">The actors per column.</param>
    /// <param name="height">How tall the room is.</param>
    /// <returns>The gap.</returns>
    private static double Gap(IReadOnlyList<List<string>> members, double height)
    {
        var most = members.Max(column => column.Count);
        return most <= 1 ? LargestGap : Math.Clamp((height - Top) * 0.4 / (most - 1), SmallestGap, LargestGap);
    }

    /// <summary>Gives how tall a unit is: the fullest column fills the room below the titles.</summary>
    /// <param name="members">The actors per column.</param>
    /// <param name="through">The money through each actor.</param>
    /// <param name="height">How tall the room is.</param>
    /// <param name="gap">The gap between bars.</param>
    /// <returns>The height of a unit.</returns>
    private static double Scale(
        IReadOnlyList<List<string>> members, Dictionary<string, BigInteger> through, double height, double gap)
    {
        var best = double.MaxValue;
        foreach (var column in members.Where(column => column.Count > 0))
        {
            var units = (double)column.Aggregate(BigInteger.Zero, (sum, key) => sum + through[key]);
            var room = height - Top - 10 - (gap * (column.Count - 1)) - (LowestBar * column.Count);
            if (units > 0)
            {
                best = Math.Min(best, Math.Max(1, room) / units);
            }
        }

        return best == double.MaxValue ? 0 : best;
    }

    /// <summary>
    /// Places the bars: the victims first in the sources' column, and in every other column by where the money into
    /// each comes from, so that the bands cross as little as they can; each column centred in the room.
    /// </summary>
    /// <param name="members">The actors per column.</param>
    /// <param name="lots">The parts drawn.</param>
    /// <param name="through">The money through each actor.</param>
    /// <param name="scale">The height of a unit.</param>
    /// <param name="gap">The gap between bars.</param>
    /// <param name="height">How tall the room is.</param>
    /// <param name="span">How far apart the columns are.</param>
    /// <returns>Each actor's corner and height.</returns>
    private static Dictionary<string, (double X, double Y, double Height)> Place(
        IReadOnlyList<List<string>> members,
        IReadOnlyList<FlowLot> lots,
        Dictionary<string, BigInteger> through,
        double scale,
        double gap,
        double height,
        double span)
    {
        var places = new Dictionary<string, (double X, double Y, double Height)>(StringComparer.Ordinal);
        for (var column = 0; column < members.Count; column++)
        {
            var order = column == 0
                ? [.. members[0].OrderByDescending(key => lots.Any(lot => lot.IsVictim && lot.Source == key))
                    .ThenByDescending(key => through[key])]
                : members[column].OrderBy(key => Barycentre(key, lots, places)).ToList();
            var heights = order.Select(key => Math.Max(LowestBar, (double)through[key] * scale)).ToList();
            var total = heights.Sum() + (gap * (order.Count - 1));
            var y = Math.Max(Top, ((height - total) / 2) + 10);
            for (var index = 0; index < order.Count; index++)
            {
                places[order[index]] = (column * span, y, heights[index]);
                y += heights[index] + gap;
            }
        }

        return places;
    }

    /// <summary>Gives the mean height of the bars the money into an actor comes from, weighted by the money.</summary>
    /// <param name="key">The actor.</param>
    /// <param name="lots">The parts drawn.</param>
    /// <param name="places">The bars placed so far.</param>
    /// <returns>The height, or a large number when none is placed yet.</returns>
    private static double Barycentre(
        string key, IReadOnlyList<FlowLot> lots, Dictionary<string, (double X, double Y, double Height)> places)
    {
        double sum = 0, weight = 0;
        foreach (var lot in lots)
        {
            for (var index = 1; index < lot.Path.Count; index++)
            {
                if (lot.Path[index] == key && places.TryGetValue(lot.Path[index - 1], out var from))
                {
                    sum += (from.Y + (from.Height / 2)) * (double)lot.Units;
                    weight += (double)lot.Units;
                }
            }
        }

        return weight > 0 ? sum / weight : double.MaxValue;
    }

    /// <summary>
    /// Gives the strands, stacked at both ends: where they leave, by the height of where they go, then by where the
    /// money ended; where they arrive, by the height of where they come from.
    /// </summary>
    /// <param name="lots">The parts drawn.</param>
    /// <param name="places">The bars.</param>
    /// <param name="scale">The height of a unit.</param>
    /// <param name="flowSteps">The steps, which say which are uncertain.</param>
    /// <returns>The strands.</returns>
    private static List<FlowStrand> Strands(
        IReadOnlyList<FlowLot> lots,
        Dictionary<string, (double X, double Y, double Height)> places,
        double scale,
        IReadOnlyDictionary<string, FlowStep> flowSteps)
    {
        var steps = lots
            .SelectMany(lot => lot.Path.Skip(1).Select((to, index) => (Lot: lot, From: lot.Path[index], To: to)))
            .ToList();
        var outAt = places.ToDictionary(pair => pair.Key, pair => pair.Value.Y, StringComparer.Ordinal);
        var inAt = places.ToDictionary(pair => pair.Key, pair => pair.Value.Y, StringComparer.Ordinal);
        var tops = new Dictionary<(FlowLot, string, string), (double Y0, double Y1)>();
        foreach (var step in steps.OrderBy(s => places[s.From].X).ThenBy(s => places[s.To].Y).ThenBy(s => Rank(s.Lot)))
        {
            tops[(step.Lot, step.From, step.To)] = (outAt[step.From], 0);
            outAt[step.From] += (double)step.Lot.Units * scale;
        }

        foreach (var step in steps.OrderBy(s => places[s.To].X).ThenBy(s => places[s.From].Y).ThenBy(s => Rank(s.Lot)))
        {
            var key = (step.Lot, step.From, step.To);
            tops[key] = (tops[key].Y0, inAt[step.To]);
            inAt[step.To] += (double)step.Lot.Units * scale;
        }

        return
        [
            .. steps.Select(step =>
            {
                var (y0, y1) = tops[(step.Lot, step.From, step.To)];
                var stepKey = FlowResult.StepKey(step.From, step.To);
                return new FlowStrand(
                    step.Lot, stepKey, places[step.From].X + BarWidth, y0, places[step.To].X, y1,
                    (double)step.Lot.Units * scale, BrushOf(step.Lot), IsBeyondGuess(step.Lot, step.To, flowSteps));
            }),
        ];
    }

    /// <summary>
    /// Gives whether a step of a part lies at or beyond its first uncertain step, so that only the way beyond, say, a
    /// mixer's suggested exit is striped (sketch FL1a).
    /// </summary>
    /// <param name="lot">The part.</param>
    /// <param name="to">The step's receiver.</param>
    /// <param name="steps">The steps, which say which are uncertain.</param>
    /// <returns><see langword="true"/> when the step is uncertain.</returns>
    public static bool IsBeyondGuess(FlowLot lot, string to, IReadOnlyDictionary<string, FlowStep> steps)
    {
        ArgumentNullException.ThrowIfNull(lot);
        ArgumentNullException.ThrowIfNull(steps);
        for (var index = 1; index < lot.Path.Count; index++)
        {
            var key = FlowResult.StepKey(lot.Path[index - 1], lot.Path[index]);
            if (steps.TryGetValue(key, out var step) && step.IsGuess)
            {
                return true;
            }

            if (lot.Path[index] == to)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>Gives a part's place in a stack: by where it ended, the victims' before the rest.</summary>
    /// <param name="lot">The part.</param>
    /// <returns>The rank.</returns>
    private static int Rank(FlowLot lot) => (Array.IndexOf(EndOrder, lot.End) * 2) + (lot.IsVictim ? 0 : 1);

    /// <summary>Gives a strand's colour: where the victims' money ended, or grey for money that is not theirs.</summary>
    /// <param name="lot">The part.</param>
    /// <returns>The brush's resource key.</returns>
    public static string BrushOf(FlowLot lot)
    {
        ArgumentNullException.ThrowIfNull(lot);
        return lot.IsVictim ? GraphInkKeys.Of(FlowReading.InkOf(lot.End)) : GraphInkKeys.Of(GraphInk.Untraced);
    }

    /// <summary>Gives the bars, coloured: a victim in the accent, another source grey, a terminal by its end.</summary>
    /// <param name="lots">The parts drawn.</param>
    /// <param name="places">Where each bar stands.</param>
    /// <param name="through">The money through each actor.</param>
    /// <param name="actors">The actors.</param>
    /// <param name="write">Writes the amount of some parts.</param>
    /// <returns>The bars.</returns>
    private static List<FlowBar> Bars(
        IReadOnlyList<FlowLot> lots,
        Dictionary<string, (double X, double Y, double Height)> places,
        Dictionary<string, BigInteger> through,
        IReadOnlyDictionary<string, Actor> actors,
        Func<IEnumerable<FlowLot>, string> write)
    {
        var bars = new List<FlowBar>();
        foreach (var (key, (x, y, height)) in places)
        {
            var actor = actors[key];
            var ends = lots.Where(lot => lot.Last == key && lot.End != FlowEnd.Kept).ToList();
            var isSource = lots.Any(lot => lot.Source == key);
            var isVictim = lots.Any(lot => lot.Source == key && lot.IsVictim);
            var guessed = lots.Any(lot => lot.IsGuess && lot.Path.Contains(key) && lot.Path[0] != key);
            var line = write(lots.Where(lot => lot.Path.Contains(key))) + (guessed ? " · förslag" : string.Empty);
            var brush = (isSource, isVictim, ends.Count > 0) switch
            {
                (true, true, _) => GraphInkKeys.Of(GraphInk.Accent),
                (true, false, _) => GraphInkKeys.Of(GraphInk.Unknown),
                (false, _, true) => GraphInkKeys.Of(FlowReading.InkOf(ends[0].End)),
                _ => "Brush.TextMuted",
            };
            bars.Add(new FlowBar(key, x, y, height, actor.Name, line, actor.Look, brush, ends.Count > 0 && !isSource,
                actor.IsSanctioned));
        }

        return bars;
    }

    /// <summary>Gives the stubs of what stays at each actor, after the strands that leave it.</summary>
    /// <param name="lots">The parts drawn.</param>
    /// <param name="places">Where each bar stands.</param>
    /// <param name="scale">The height of a unit.</param>
    /// <param name="write">Writes the amount of some parts.</param>
    /// <returns>The stubs.</returns>
    private static List<FlowStub> Stubs(
        IReadOnlyList<FlowLot> lots,
        Dictionary<string, (double X, double Y, double Height)> places,
        double scale,
        Func<IEnumerable<FlowLot>, string> write)
    {
        var stubs = new List<FlowStub>();
        foreach (var group in lots.Where(lot => lot.End == FlowEnd.Kept && lot.IsVictim).GroupBy(lot => lot.Last))
        {
            var (x, y, _) = places[group.Key];
            var units = group.Aggregate(BigInteger.Zero, (sum, lot) => sum + lot.Units);
            var leaving = lots.Where(lot => lot.Last != group.Key && lot.Path.Contains(group.Key))
                .Aggregate(BigInteger.Zero, (sum, lot) => sum + lot.Units);
            var top = y + ((double)leaving * scale);
            stubs.Add(new FlowStub(group.Key, x + BarWidth, top, Math.Max(2, (double)units * scale),
                "ligger kvar · " + write(group)));
        }

        return stubs;
    }

    /// <summary>Gives a column's title: the victims in the first, the hops after.</summary>
    /// <param name="column">The column.</param>
    /// <param name="members">The actors in it.</param>
    /// <param name="lots">The parts drawn.</param>
    /// <returns>The title.</returns>
    private static string Title(int column, IReadOnlyList<string> members, IReadOnlyList<FlowLot> lots)
    {
        if (column > 0)
        {
            return column.ToString(CultureInfo.InvariantCulture) + " hopp";
        }

        return members.Any(key => lots.Any(lot => lot.IsVictim && lot.Source == key)) ? "Offren" : "Källorna";
    }
}
