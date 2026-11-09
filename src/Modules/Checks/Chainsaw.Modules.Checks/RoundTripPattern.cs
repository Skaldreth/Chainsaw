using Chainsaw.Modules.Checks.Contracts;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// The rule <em>rundgång</em> of <see cref="PatternCheck"/>: money that leaves an actor nothing is known about and
/// comes back to it through at least two others, as when an amount is sent round to look like it came from elsewhere.
/// The way round is a walk (<see cref="PatternWalk"/>) of <see cref="MinimumLines"/> to <see cref="MaxLines"/> lines
/// that never passes an exchange or another service (a deposit and a withdrawal is everyday), where each line carries
/// from <see cref="Carry"/>'s least to all of the line before, and at least <see cref="LeastBack"/> of what left comes
/// back: the same money, less the fees and a cut on the way.
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
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in shape.Flows)
        {
            if (start.IsShared || shape.Actors[start.From].Sort != ActorSort.Unknown || !MayPass(shape.Actors[start.To]))
            {
                continue;
            }

            var round = PatternWalk.From(shape, start, MaxLines, MayPass, mayReturn: true, Carry)
                .FirstOrDefault(walk => walk.Length >= MinimumLines && Returns(shape, walk));
            if (round is not null && seen.Add(SetOf(shape, round)))
            {
                yield return Describe(shape, round, analysis);
            }
        }
    }

    /// <summary>Whether a way round may pass an actor: not an exchange or another service.</summary>
    /// <param name="actor">The actor.</param>
    /// <returns>Whether it may.</returns>
    private static bool MayPass(ShapeActor actor) => actor.Sort is not (ActorSort.Exchange or ActorSort.Service);

    /// <summary>Whether a walk comes back to its first actor with at least <see cref="LeastBack"/> of what left.</summary>
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
            + $"{PatternText.Actor(home)} → {way} (tx {txids}). Varje steg kommer efter det förra och bär "
            + $"{CheckText.Percent(Carry.Least)} till hela dess belopp, och vägen går inte genom någon börs eller "
            + "tjänst. Pengar som skickas runt och tillbaka kan vara ett sätt att få dem att se ut att komma "
            + "någon annanstans ifrån.";
        return PatternText.Check(
            "round:" + lines[0].Txid.Value,
            $"Rundgång, {CheckText.Count(lines.Count)} steg",
            $"Tillbaka till {PatternText.Actor(home)} efter {PatternText.Between(lines[0], lines[^1])}",
            why,
            lines);
    }
}
