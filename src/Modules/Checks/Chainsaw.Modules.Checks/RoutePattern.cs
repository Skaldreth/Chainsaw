using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// The rule <em>samma väg</em> of <see cref="PatternCheck"/>: money from separate parts of the case that is passed on
/// the same way to the same labelled actor, as when one person moves the proceeds of several crimes through mules to
/// the same exchange or mixer. A way is a walk (<see cref="PatternWalk"/>) that follows the coins from an actor nothing
/// is known about (or a start point) through at least two others nothing is known about, none of them a hub, and
/// ends at the first labelled actor it reaches, within <see cref="PatternWalk.Window"/>; it is never followed through
/// a labelled actor, since what comes out of an exchange or a mixer is other coins. Two ways are alike when their last
/// line pays the same addresses of the labelled actor (the same deposit address, never just the same exchange, which
/// has many customers) through the same number of steps; they are separate when they start at different actors and
/// share no transaction, so that money gathered in one place and moved together is no second way.
/// </summary>
internal static class RoutePattern
{
    /// <summary>The fewest lines in a way: the first actor, two that pass the money on, and the labelled end.</summary>
    public const int MinimumLines = 3;

    /// <summary>The most lines in a way.</summary>
    public const int MaxLines = 4;

    /// <summary>The most ways a check names, so that a large case does not give one endless check.</summary>
    public const int MostWays = 10;

    /// <summary>
    /// How much of a line the next must carry on a way: at least half, and more when money is gathered on the way.
    /// </summary>
    public static readonly PatternWalk.Carry Carry = new(0.5, MayGrow: true);

    /// <summary>Finds the ways that money from separate parts of the case goes alike.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="analysis">The analysis, for its name in the texts.</param>
    /// <returns>A check per way taken at least twice.</returns>
    public static IEnumerable<CheckFinding> Find(GraphShape shape, ICheckAnalysis analysis)
    {
        var rules = new PatternWalk.Rules(
            MaxLines,
            actor => MayPass(shape, actor) || actor.IsLabelled,
            actor => actor.IsLabelled,
            MayReturn: false,
            Carry);
        var ways = new List<(string Signature, int[] Lines)>();
        foreach (var start in shape.Flows)
        {
            var origin = shape.Actors[start.From];
            if (origin.Sort is not (ActorSort.Unknown or ActorSort.Protected) || PatternActors.IsHub(shape, origin))
            {
                continue;
            }

            // Of each start, only the shortest way to each end: a longer one says no more.
            ways.AddRange(PatternWalk.From(shape, start, rules)
                .Where(walk => walk.Length >= MinimumLines && shape.Actors[shape.Flows[walk[^1]].To].IsLabelled)
                .Select(walk => (Signature: SignatureOf(shape, walk), Lines: walk))
                .DistinctBy(way => way.Signature, StringComparer.Ordinal));
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

    /// <summary>Whether a way may pass an actor on the way: one nothing is known about, and not a hub.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="actor">The actor.</param>
    /// <returns>Whether it may.</returns>
    private static bool MayPass(GraphShape shape, ShapeActor actor) =>
        actor.Sort == ActorSort.Unknown && !PatternActors.IsHub(shape, actor);

    /// <summary>Writes a walk's lines as one text, so that walks can be compared.</summary>
    /// <param name="lines">The lines' indexes.</param>
    /// <returns>The indexes, joined.</returns>
    private static string Text(int[] lines) => string.Join(',', lines);

    /// <summary>
    /// Gives what makes two ways alike: the addresses their last line pays (the deposit address) and how many steps.
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <param name="walk">The way.</param>
    /// <returns>For example <c>bc1q…&gt;3</c>.</returns>
    private static string SignatureOf(GraphShape shape, int[] walk) =>
        string.Join(',', shape.Flows[walk[^1]].ToAddresses.Select(address => address.Value)
            .Order(StringComparer.Ordinal)) + ">" + CheckText.Count(walk.Length);

    /// <summary>
    /// Picks, the earliest first, the ways that start at different actors and share no transaction with one picked.
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <param name="ways">The ways that end alike.</param>
    /// <returns>The separate ways, at most <see cref="MostWays"/>.</returns>
    private static List<int[]> Separate(GraphShape shape, IReadOnlyList<int[]> ways)
    {
        var picked = new List<int[]>();
        var starts = new HashSet<int>();
        var used = new HashSet<Txid>();
        foreach (var way in ways.OrderBy(way => way[0]))
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
        var last = shape.Flows[ways[0][^1]];
        var end = PatternText.ActorWithSort(shape.Actors[last.To]);
        var steps = CheckText.Count(ways[0].Length);
        var parts = ways.Select(way => Part(shape, way)).ToList();
        var why = $"Analysen {analysis.Named()} ser mynt från {CheckText.Count(ways.Count)} skilda delar av ärendet, "
            + $"bland de hämtade tx:erna, föras vidare på samma sätt till samma adress hos {end} "
            + $"({CheckText.Short(last.ToAddresses[0])}): {steps} steg, där varje steg "
            + "spenderar mynt ur det förra, bär minst "
            + $"{CheckText.Percent(Carry.Least)} av dess belopp och kommer inom {CheckText.Span(PatternWalk.Window)} "
            + "från det första, genom aktörer som inget är känt om. Delarna börjar hos olika aktörer och har ingen tx "
            + "gemensam. Vägen följs bara fram till den första aktören med etikett. " + PatternText.Parts(parts)
            + " Samma väg i flera delar kan betyda att samma person för pengarna vidare.";
        return PatternText.Check(
            $"route:{last.ToAddresses[0].Value}>{steps}",
            $"Samma väg, {CheckText.Count(ways.Count)} delar",
            $"{steps} steg till {end}",
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
