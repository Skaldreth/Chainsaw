using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// The rule <em>samma väg</em> of <see cref="PatternCheck"/>: money from separate parts of the case that goes the same
/// way, the same sorts of actors in the same order (for example a mixer and then an exchange), as when one person
/// launders the proceeds of several crimes the same way. A way is a walk (<see cref="PatternWalk"/>) of at most
/// <see cref="MaxLines"/> lines from an actor that nothing is known about (or a start point) that passes at least
/// <see cref="MinimumLabelled"/> labelled actors and ends at one. Two ways are alike when their sorts after the first
/// actor are the same; they are separate when they start at different actors and share no transaction, so that money
/// gathered in one place and moved together is no second way.
/// </summary>
internal static class RoutePattern
{
    /// <summary>The most lines in a way.</summary>
    public const int MaxLines = 4;

    /// <summary>The fewest labelled actors a way passes.</summary>
    public const int MinimumLabelled = 2;

    /// <summary>
    /// How much of a line the next must carry on a way: at least half, and more when money is gathered on the way.
    /// </summary>
    public static readonly PatternWalk.Carry Carry = new(0.5, MayGrow: true);

    /// <summary>The most ways a check names, so that a large case does not give one endless check.</summary>
    public const int MostWays = 10;

    /// <summary>Finds the ways that money from separate parts of the case goes alike.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="analysis">The analysis, for its name in the texts.</param>
    /// <returns>A check per way taken at least twice.</returns>
    public static IEnumerable<CheckFinding> Find(GraphShape shape, ICheckAnalysis analysis)
    {
        var ways = new List<(string Signature, int[] Lines)>();
        foreach (var start in shape.Flows)
        {
            if (start.IsShared || shape.Actors[start.From].IsLabelled)
            {
                continue;
            }

            // Of each start, only the shortest way of each signature: a longer one with the same sorts says no more.
            var walks = PatternWalk.From(shape, start, MaxLines, _ => true, mayReturn: false, Carry)
                .Where(walk => Counts(shape, walk))
                .Select(walk => (Signature: SignatureOf(shape, walk), Lines: walk))
                .DistinctBy(way => way.Signature, StringComparer.Ordinal);
            ways.AddRange(walks);
        }

        // A way that is only the tail of another (the middleman's part of the victim's way) is no way of its own.
        var tails = ways
            .SelectMany(way => Enumerable.Range(1, way.Lines.Length - 1).Select(skip => Text(way.Lines[skip..])))
            .ToHashSet(StringComparer.Ordinal);
        return ways
            .Where(way => !tails.Contains(Text(way.Lines)))
            .GroupBy(way => way.Signature, StringComparer.Ordinal)
            .Select(group => Separate(shape, [.. group.Select(way => way.Lines)]))
            .Where(separate => separate.Count >= 2)
            .Select(separate => Describe(shape, separate, analysis));
    }

    /// <summary>Writes a walk's lines as one text, so that walks can be compared.</summary>
    /// <param name="lines">The lines' indexes.</param>
    /// <returns>The indexes, joined.</returns>
    private static string Text(int[] lines) => string.Join(',', lines);

    /// <summary>Whether a walk is a way: it ends at a labelled actor and passes enough of them.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="walk">The walk.</param>
    /// <returns>Whether it counts.</returns>
    private static bool Counts(GraphShape shape, int[] walk) =>
        shape.Actors[shape.Flows[walk[^1]].To].IsLabelled
        && walk.Count(index => shape.Actors[shape.Flows[index].To].IsLabelled) >= MinimumLabelled;

    /// <summary>Gives a way's sorts after its first actor.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="walk">The way.</param>
    /// <returns>For example <c>Unknown&gt;Mixer&gt;Exchange</c>.</returns>
    private static string SignatureOf(GraphShape shape, int[] walk) =>
        string.Join('>', walk.Select(index => SortOf(shape.Actors[shape.Flows[index].To])));

    /// <summary>Gives the sort a way counts an actor as: a start point is one that nothing is known about.</summary>
    /// <param name="actor">The actor.</param>
    /// <returns>The sort.</returns>
    private static ActorSort SortOf(ShapeActor actor) =>
        actor.Sort == ActorSort.Protected ? ActorSort.Unknown : actor.Sort;

    /// <summary>
    /// Picks, the earliest first, the ways that start at different actors and share no transaction with one picked.
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <param name="ways">The ways with the same sorts.</param>
    /// <returns>The separate ways, at most <see cref="MostWays"/>.</returns>
    private static List<int[]> Separate(GraphShape shape, IReadOnlyList<int[]> ways)
    {
        var picked = new List<int[]>();
        var starts = new HashSet<int>();
        var used = new HashSet<Txid>();
        var ordered = ways
            .OrderBy(way => shape.Flows[way[0]].Height)
            .ThenBy(way => shape.Flows[way[0]].Txid.Value, StringComparer.Ordinal)
            .ThenBy(way => way[0]);
        foreach (var way in ordered)
        {
            var txids = way.Select(index => shape.Flows[index].Txid).ToList();
            if (picked.Count < MostWays && !starts.Contains(shape.Flows[way[0]].From) && !txids.Any(used.Contains))
            {
                picked.Add(way);
                starts.Add(shape.Flows[way[0]].From);
                used.UnionWith(txids);
            }
        }

        return picked;
    }

    /// <summary>Describes the separate ways that go alike.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="ways">The ways, the earliest first.</param>
    /// <param name="analysis">The analysis.</param>
    /// <returns>The check.</returns>
    private static CheckFinding Describe(GraphShape shape, IReadOnlyList<int[]> ways, ICheckAnalysis analysis)
    {
        var sorts = string.Join(" → ", ways[0].Select(index => PatternText.Sort(
            SortOf(shape.Actors[shape.Flows[index].To]))));
        var same = ways.Select(way => shape.Flows[way[^1]].To).Distinct().Count() == 1
            ? $" Alla slutar hos samma aktör, {PatternText.ActorWithSort(shape.Actors[shape.Flows[ways[0][^1]].To])}."
            : string.Empty;
        var parts = ways.Select(way => Part(shape, way)).ToList();
        var why = $"Analysen {analysis.Named()} ser pengar från {CheckText.Count(ways.Count)} skilda delar av "
            + $"ärendet gå samma väg: {sorts}. Delarna börjar hos olika aktörer och har ingen tx gemensam, och varje "
            + $"steg kommer efter det förra och bär minst {CheckText.Percent(Carry.Least)} av dess belopp.{same} "
            + PatternText.Parts(parts) + " Samma väg i flera delar kan betyda att samma person tvättar pengarna.";
        var first = shape.Flows[ways[0][0]];
        return PatternText.Check(
            $"route:{SignatureOf(shape, ways[0])}:{first.Txid.Value}",
            $"Samma väg, {CheckText.Count(ways.Count)} delar",
            sorts,
            why,
            ways.SelectMany(way => way).Select(index => shape.Flows[index]));
    }

    /// <summary>Describes one way in a sentence.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="way">The way.</param>
    /// <returns>The sentence.</returns>
    private static string Part(GraphShape shape, int[] way)
    {
        var lines = way.Select(index => shape.Flows[index]).ToList();
        var actors = string.Join(" → ", lines.Select(line => PatternText.ActorWithSort(shape.Actors[line.To])));
        var txids = string.Join(", ", lines.Select(PatternText.Tx));
        return $"Från {PatternText.Actor(shape.Actors[lines[0].From])} gick {CheckText.Btc(lines[0].Amount)} BTC "
            + $"till {actors} på {PatternText.Between(lines[0], lines[^1])} (tx {txids}).";
    }
}
