using Chainsaw.Modules.Checks.Contracts;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// The rule <em>rundgång</em> of <see cref="PatternCheck"/>: coins that leave an actor nothing is known about and
/// come back to it through at least two others, as when an amount is sent round to look like it came from elsewhere.
/// The way round is a walk (<see cref="PatternWalk"/>) of <see cref="MinimumLines"/> to <see cref="MaxLines"/> lines
/// within <see cref="PatternWalk.Window"/>, where every line spends a coin the line before paid and carries from
/// <see cref="Carry"/>'s least to all of it, and at least <see cref="LeastBack"/> of what left comes back: the same
/// money, less the fees and a cut on the way. It passes only actors nothing is known about and no hub (a deposit at an
/// exchange and a withdrawal is everyday), and at least one of them does more than pass the money on, since coins
/// that only go round one's own addresses are housekeeping.
/// </summary>
internal static class RoundTripPattern
{
    /// <summary>The fewest lines round: two would be a payment and its refund.</summary>
    public const int MinimumLines = 3;

    /// <summary>The most lines round.</summary>
    public const int MaxLines = 5;

    /// <summary>The smallest share of what left that must come back.</summary>
    public const double LeastBack = 0.8;

    /// <summary>How much of a line the next must carry on the way round: at least four fifths, never more.</summary>
    public static readonly PatternWalk.Carry Carry = new(0.8, MayGrow: false);

    /// <summary>Finds the round trips.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="analysis">The analysis, for its name in the texts.</param>
    /// <returns>A check per round trip, each set of transactions once.</returns>
    public static IEnumerable<CheckFinding> Find(GraphShape shape, ICheckAnalysis analysis)
    {
        var rules = new PatternWalk.Rules(MaxLines, actor => MayPass(shape, actor), _ => false, MayReturn: true, Carry);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in shape.Flows)
        {
            if (!MayPass(shape, shape.Actors[start.From]))
            {
                continue;
            }

            var round = PatternWalk.From(shape, start, rules)
                .FirstOrDefault(walk => walk.Length >= MinimumLines && Returns(shape, walk) && Busy(shape, walk));
            if (round is not null && seen.Add(SetOf(shape, round)))
            {
                yield return Describe(shape, round, analysis);
            }
        }
    }

    /// <summary>Whether a way round may start at or pass an actor: one nothing is known about, and not a hub.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="actor">The actor.</param>
    /// <returns>Whether it may.</returns>
    private static bool MayPass(GraphShape shape, ShapeActor actor) =>
        actor.Sort == ActorSort.Unknown && !PatternActors.IsHub(shape, actor);

    /// <summary>Whether at least one actor on the way round does more than pass the money on.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="walk">The way round.</param>
    /// <returns>Whether one does.</returns>
    private static bool Busy(GraphShape shape, int[] walk)
    {
        var lines = walk.ToHashSet();
        return walk[..^1].Any(index => PatternActors.HasOtherBusiness(shape, shape.Flows[index].To, lines));
    }

    /// <summary>
    /// Whether a walk comes back to its first actor with at least <see cref="LeastBack"/> of what left.
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <param name="walk">The walk.</param>
    /// <returns>Whether it is a round trip.</returns>
    private static bool Returns(GraphShape shape, int[] walk)
    {
        var left = shape.Flows[walk[0]];
        var back = shape.Flows[walk[^1]];
        return back.To == left.From && back.Amount.Value >= LeastBack * left.Amount.Value;
    }

    /// <summary>Gives a round trip's transactions as one text, so that the same trip is told once.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="walk">The round trip.</param>
    /// <returns>The txids in order, joined.</returns>
    private static string SetOf(GraphShape shape, int[] walk) =>
        string.Join(',', walk.Select(index => shape.Flows[index].Txid.Value).Order(StringComparer.Ordinal));

    /// <summary>Describes a round trip.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="walk">The round trip.</param>
    /// <param name="analysis">The analysis.</param>
    /// <returns>The check.</returns>
    private static CheckFinding Describe(GraphShape shape, int[] walk, ICheckAnalysis analysis)
    {
        var lines = walk.Select(index => shape.Flows[index]).ToList();
        var home = shape.Actors[lines[0].From];
        var way = string.Join(" → ", lines.Select(line => PatternText.ActorWithSort(shape.Actors[line.To])));
        var txids = string.Join(", ", lines.Select(PatternText.Tx));
        var why = $"Analysen {analysis.Named()} ser {CheckText.Btc(lines[0].Amount)} BTC lämna "
            + $"{PatternText.Actor(home)} och {CheckText.Btc(lines[^1].Amount)} BTC "
            + $"({CheckText.Percent(PatternText.Share(lines[^1].Amount, lines[0].Amount))}) komma tillbaka efter "
            + $"{PatternText.Between(lines[0], lines[^1])}, genom {CheckText.Count(lines.Count - 1)} andra aktörer: "
            + $"{PatternText.Actor(home)} → {way} (tx {txids}), bland de hämtade tx:erna. Varje steg spenderar mynt "
            + $"ur det förra och bär {CheckText.Percent(Carry.Least)} till hela dess belopp, inom "
            + $"{CheckText.Span(PatternWalk.Window)}, och vägen går bara genom aktörer som inget är känt om och som "
            + "inte är knutpunkter för många, minst en av dem med fler affärer än den här. Pengar som skickas runt och "
            + "tillbaka kan vara ett sätt att få dem att se ut att komma någon annanstans ifrån.";
        return PatternText.Check(
            "round:" + lines[0].Txid.Value + ":" + lines[^1].Txid.Value,
            $"Rundgång, {CheckText.Count(lines.Count)} steg",
            $"Tillbaka till {PatternText.Actor(home)} efter {PatternText.Between(lines[0], lines[^1])}",
            why,
            lines);
    }
}
