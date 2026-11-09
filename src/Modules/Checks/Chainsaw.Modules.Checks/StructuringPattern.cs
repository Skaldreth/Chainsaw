using Chainsaw.Modules.Checks.Contracts;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// The rule <em>likadana belopp i följd</em> of <see cref="PatternCheck"/>: an actor nothing is known about, and no hub,
/// pays the same recipient at least <see cref="MinimumRun"/> times in a row among the fetched transactions, in separate
/// transactions within <see cref="Window"/>, amounts that differ by at most <see cref="Tolerance"/> from the first, as
/// when an amount is split to stay under a limit (structuring). Round amounts (two significant digits or fewer, such
/// as 0,01, 0,25 or 1 BTC) are everyday and break a run, and payments below <see cref="MinimumAmount"/> are dust and
/// left to the dust check. The payments must also add up to something (<see cref="AddsUp"/>): the recipient is a
/// labelled exchange or service (a deposit), or it later spends at least <see cref="MinimumRun"/> of the run's coins
/// together, so that a subscription paid daily to someone is no structuring. The limit itself is in money the chain
/// does not know, so the rule reads only that the amounts are alike; it names another actor only when it pays the
/// same recipient the same amount.
/// </summary>
internal static class StructuringPattern
{
    /// <summary>The fewest payments in a row.</summary>
    public const int MinimumRun = 4;

    /// <summary>The most payments a run holds; a longer row gives a new run after it.</summary>
    public const int MaxRun = 50;

    /// <summary>The most a payment's amount may differ from the run's first, as a share of it.</summary>
    public const double Tolerance = 0.01;

    /// <summary>The smallest payment counted, in satoshi.</summary>
    public const long MinimumAmount = 10_000;

    /// <summary>The most significant digits a round amount has.</summary>
    public const int RoundDigits = 2;

    /// <summary>The longest time from a run's first payment to its last.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    /// <summary>Finds the runs of alike amounts.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="analysis">The analysis, for its name in the texts.</param>
    /// <returns>A check per run.</returns>
    public static IEnumerable<CheckFinding> Find(GraphShape shape, ICheckAnalysis analysis)
    {
        var runs = shape.Actors
            .Where(actor => actor.Sort == ActorSort.Unknown && !PatternActors.IsHub(shape, actor))
            .SelectMany(actor => RunsOf(shape, actor))
            .Where(run => AddsUp(shape, run))
            .ToList();
        return runs.Select(run => Describe(shape, run, runs, analysis));
    }

    /// <summary>
    /// Whether a run adds up to something: its recipient is a labelled exchange or service, or one transaction spends
    /// at least <see cref="MinimumRun"/> of the coins it paid.
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <param name="run">The run.</param>
    /// <returns>Whether it does.</returns>
    private static bool AddsUp(GraphShape shape, List<ShapeFlow> run) =>
        shape.Actors[run[0].To].Sort is ActorSort.Exchange or ActorSort.Service
        || run.SelectMany(payment => payment.Paid)
            .SelectMany(coin => shape.SpentBy(coin).Select(index => (Coin: coin, shape.Flows[index].Txid)))
            .Distinct()
            .GroupBy(spend => spend.Txid)
            .Any(spender => spender.Count() >= MinimumRun);

    /// <summary>Whether an amount is round: at most <see cref="RoundDigits"/> significant digits.</summary>
    /// <param name="satoshi">The amount in satoshi; above zero.</param>
    /// <returns>Whether it is round.</returns>
    public static bool IsRound(long satoshi)
    {
        if (satoshi <= 0)
        {
            return true;
        }

        var digits = satoshi;
        while (digits % 10 == 0)
        {
            digits /= 10;
        }

        return digits < 100;
    }

    /// <summary>
    /// Finds an actor's runs to each recipient: each the longest from its first payment, none overlapping, at most
    /// <see cref="MaxRun"/> long.
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <param name="actor">The actor.</param>
    /// <returns>The runs, each its lines in time order.</returns>
    private static IEnumerable<List<ShapeFlow>> RunsOf(GraphShape shape, ShapeActor actor) =>
        shape.Out(actor.Index)
            .Select(index => shape.Flows[index])
            .Where(flow => !flow.IsShared && flow.Block is not null)
            .GroupBy(flow => flow.To)
            .SelectMany(recipient => RunsIn([.. recipient.DistinctBy(flow => flow.Txid)]));

    /// <summary>Splits one recipient's payments, in time order, into runs.</summary>
    /// <param name="payments">The payments.</param>
    /// <returns>The runs.</returns>
    private static List<List<ShapeFlow>> RunsIn(List<ShapeFlow> payments)
    {
        var runs = new List<List<ShapeFlow>>();
        var at = 0;
        while (at < payments.Count)
        {
            var run = new List<ShapeFlow> { payments[at] };
            while (at + run.Count < payments.Count && run.Count < MaxRun
                && Continues(run[0], payments[at + run.Count]))
            {
                run.Add(payments[at + run.Count]);
            }

            if (run.Count >= MinimumRun && run[0].Amount.Value >= MinimumAmount
                && run.All(payment => !IsRound(payment.Amount.Value)))
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

    /// <summary>Describes a run, with the other actors whose runs pay the same recipient the same amount.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="run">The run.</param>
    /// <param name="all">Every run in the case.</param>
    /// <param name="analysis">The analysis.</param>
    /// <returns>The check.</returns>
    private static CheckFinding Describe(
        GraphShape shape, List<ShapeFlow> run, IReadOnlyList<List<ShapeFlow>> all, ICheckAnalysis analysis)
    {
        var payer = shape.Actors[run[0].From];
        var recipient = PatternText.ActorWithSort(shape.Actors[run[0].To]);
        var amounts = string.Join(", ", run.Take(PatternText.MostNamed).Select(flow => CheckText.Btc(flow.Amount)));
        var txids = string.Join(", ", run.Take(PatternText.MostNamed).Select(PatternText.Tx));
        var more = run.Count > PatternText.MostNamed ? " …" : string.Empty;
        var alike = all
            .Where(other => other[0].From != run[0].From && other[0].To == run[0].To && Alike(run[0], other[0]))
            .Select(other => PatternText.Actor(shape.Actors[other[0].From]))
            .Distinct()
            .ToList();
        var others = alike.Count == 0
            ? string.Empty
            : " Samma mottagare får samma belopp i följd också av "
                + string.Join(", ", alike.Take(PatternText.MostNamed)) + ".";
        var why = $"Analysen {analysis.Named()} ser {PatternText.Actor(payer)} betala {recipient} "
            + $"{CheckText.Count(run.Count)} gånger i följd bland de hämtade tx:erna, på "
            + $"{PatternText.Between(run[0], run[^1])} och i skilda tx, belopp som skiljer högst "
            + $"{CheckText.Percent(Tolerance)} från det första ({amounts}{more} BTC, tx {txids}{more}). Jämna belopp "
            + "räknas inte, och betalningarna samlas: mottagaren är en börs eller tjänst, eller spenderar mynten "
            + "tillsammans. Likadana belopp i följd är formen när någon delar upp ett större belopp för att hålla sig "
            + "under en gräns, till exempel en börs gräns för kundkontroll; gränsen räknas i pengar som kedjan inte "
            + $"känner, så analysen läser bara att beloppen är lika.{others}";
        return PatternText.Check(
            "struct:" + run[0].Txid.Value,
            $"Likadana belopp, {CheckText.Count(run.Count)} i följd",
            $"{CheckText.Btc(run[0].Amount)} BTC × {CheckText.Count(run.Count)} till {recipient}",
            why,
            run);
    }
}
