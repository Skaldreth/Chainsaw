using Chainsaw.Modules.Checks.Contracts;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// The rule <em>likadana belopp i följd</em> of <see cref="PatternCheck"/>: an actor nothing is known about pays at least
/// <see cref="MinimumRun"/> times in a row, in separate transactions within <see cref="Window"/>, amounts that differ by
/// at most <see cref="Tolerance"/> from the first, as when an amount is split to stay under a limit (structuring). The
/// limit itself is in money the chain does not know, so the rule reads only that the amounts are alike; it compares
/// them with the other runs in the case and says when another actor splits into the same amount. Payments below
/// <see cref="MinimumAmount"/> are dust and left to the dust check.
/// </summary>
internal static class StructuringPattern
{
    /// <summary>The fewest payments in a row.</summary>
    public const int MinimumRun = 4;

    /// <summary>The most a payment's amount may differ from the run's first, as a share of it.</summary>
    public const double Tolerance = 0.01;

    /// <summary>The longest time from a run's first payment to its last.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    /// <summary>The smallest payment counted, in satoshi.</summary>
    public const long MinimumAmount = 10_000;

    /// <summary>Finds the runs of alike amounts.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="analysis">The analysis, for its name in the texts.</param>
    /// <returns>A check per run.</returns>
    public static IEnumerable<CheckFinding> Find(GraphShape shape, ICheckAnalysis analysis)
    {
        var runs = shape.Actors
            .Where(actor => actor.Sort == ActorSort.Unknown)
            .SelectMany(actor => RunsOf(shape, actor))
            .ToList();
        return runs.Select(run => Describe(shape, run, runs, analysis));
    }

    /// <summary>Finds an actor's runs: each the longest from its first payment, none overlapping.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="actor">The actor.</param>
    /// <returns>The runs, each its lines in time order.</returns>
    private static List<List<ShapeFlow>> RunsOf(GraphShape shape, ShapeActor actor)
    {
        // One payment per transaction, the largest, so that a payment and a split change are not two.
        var payments = shape.Out(actor.Index)
            .Select(index => shape.Flows[index])
            .Where(flow => !flow.IsShared && flow.Block is not null)
            .GroupBy(flow => flow.Txid)
            .Select(group => group.MaxBy(flow => flow.Amount)!)
            .ToList();
        var runs = new List<List<ShapeFlow>>();
        var at = 0;
        while (at < payments.Count)
        {
            var run = new List<ShapeFlow> { payments[at] };
            while (at + run.Count < payments.Count && Continues(run[0], payments[at + run.Count]))
            {
                run.Add(payments[at + run.Count]);
            }

            if (run.Count >= MinimumRun && run[0].Amount.Value >= MinimumAmount)
            {
                runs.Add(run);
            }

            at += run.Count;
        }

        return runs;
    }

    /// <summary>Whether a payment continues a run: an alike amount within the window from the first.</summary>
    /// <param name="first">The run's first payment.</param>
    /// <param name="next">The next payment.</param>
    /// <returns>Whether it continues it.</returns>
    private static bool Continues(ShapeFlow first, ShapeFlow next) =>
        Alike(first, next) && next.Block!.Time - first.Block!.Time <= Window;

    /// <summary>Whether two amounts are alike: within the tolerance of the first.</summary>
    /// <param name="first">The first payment.</param>
    /// <param name="other">The other payment.</param>
    /// <returns>Whether they are.</returns>
    private static bool Alike(ShapeFlow first, ShapeFlow other) =>
        Math.Abs(other.Amount.Value - first.Amount.Value) <= Tolerance * first.Amount.Value;

    /// <summary>Describes a run, with the other actors whose runs have the same amount.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="run">The run.</param>
    /// <param name="all">Every run in the case.</param>
    /// <param name="analysis">The analysis.</param>
    /// <returns>The check.</returns>
    private static CheckFinding Describe(
        GraphShape shape, List<ShapeFlow> run, IReadOnlyList<List<ShapeFlow>> all, ICheckAnalysis analysis)
    {
        var payer = shape.Actors[run[0].From];
        var receivers = run.Select(flow => flow.To).Distinct().ToList();
        var to = receivers.Count == 1
            ? $"alla till {PatternText.ActorWithSort(shape.Actors[receivers[0]])}"
            : $"till {CheckText.Count(receivers.Count)} olika mottagare";
        var amounts = string.Join(", ", run.Select(flow => CheckText.Btc(flow.Amount)));
        var txids = string.Join(", ", run.Select(PatternText.Tx));
        var alike = all
            .Where(other => other[0].From != run[0].From && Alike(run[0], other[0]))
            .Select(other => PatternText.Actor(shape.Actors[other[0].From]))
            .Distinct()
            .ToList();
        var others = alike.Count == 0
            ? string.Empty
            : $" Samma belopp delas också upp av {string.Join(", ", alike.Take(PatternText.MostNamed))}, vilket kan "
                + "vara samma person eller samma upplägg.";
        var why = $"Analysen {analysis.Named()} ser {PatternText.Actor(payer)} betala "
            + $"{CheckText.Count(run.Count)} gånger i följd på {PatternText.Between(run[0], run[^1])}, i skilda tx, "
            + $"belopp som skiljer högst {CheckText.Percent(Tolerance)} från det första ({amounts} BTC), {to} "
            + $"(tx {txids}). Likadana belopp i följd är formen när någon delar upp ett större belopp för att "
            + "hålla sig under en gräns, till exempel en börs gräns för kundkontroll; gränsen räknas i pengar som "
            + $"kedjan inte känner, så analysen läser bara att beloppen är lika.{others}";
        return PatternText.Check(
            "struct:" + run[0].Txid.Value,
            $"Likadana belopp, {CheckText.Count(run.Count)} i följd",
            $"{CheckText.Btc(run[0].Amount)} BTC × {CheckText.Count(run.Count)} på "
                + PatternText.Between(run[0], run[^1]),
            why,
            run);
    }
}
