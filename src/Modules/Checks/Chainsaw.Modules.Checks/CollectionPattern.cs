using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// The rule <em>samma insamling</em> of <see cref="PatternCheck"/>: an actor that nothing is known about takes payments
/// from at least <see cref="MinimumPayers"/> different payers without spending in between, and then sweeps them onward
/// in one transaction that spends their coins, from <see cref="SweepShare"/> to <see cref="MostSweep"/> of what it
/// collected, as a scam's collecting address does. One such actor is common (a wallet that consolidates), so the rule
/// says something only when at least two collecting actors sweep to the <em>same</em> actor that nothing is known
/// about (the same cluster or deposit address). A labelled exchange or service, or a hub, as the destination says
/// nothing (it has many customers), and a payment that only forwards money the payer got in the graph (a deposit
/// address sweeping to its hot wallet) is no payer of a collection. A merchant that empties its tills to its cold
/// wallet has the same form, and the text says so.
/// </summary>
internal static class CollectionPattern
{
    /// <summary>The fewest different payers a collection needs.</summary>
    public const int MinimumPayers = 5;

    /// <summary>The smallest share of what was collected that the sweep must carry on.</summary>
    public const double SweepShare = 0.9;

    /// <summary>
    /// The largest share of what was collected that the sweep may carry: a little more than all, for a coin the actor
    /// held before, but a sweep of much more is money from elsewhere.
    /// </summary>
    public const double MostSweep = 1.05;

    /// <summary>Finds the collections that sweep to the same actor.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="analysis">The analysis, for its name in the texts.</param>
    /// <returns>A check per actor that at least two collecting actors sweep to.</returns>
    public static IEnumerable<CheckFinding> Find(GraphShape shape, ICheckAnalysis analysis) =>
        shape.Actors
            .Where(actor => actor.Sort == ActorSort.Unknown)
            .SelectMany(actor => CollectionsOf(shape, actor))
            .Where(collection => MayReceive(shape, shape.Actors[shape.Flows[collection.Sweep[0]].To]))
            .OrderBy(collection => collection.Sweep[0])
            .GroupBy(collection => shape.Flows[collection.Sweep[0]].To)
            .Where(group => group.Select(collection => collection.Collector).Distinct().Count() >= 2)
            .Select(group => Describe(shape, [.. group], analysis));

    /// <summary>Whether sweeps to an actor say something: one that nothing is known about, and not a hub.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="actor">The actor.</param>
    /// <returns>Whether they do.</returns>
    private static bool MayReceive(GraphShape shape, ShapeActor actor) =>
        actor.Sort == ActorSort.Unknown && !PatternActors.IsHub(shape, actor);

    /// <summary>
    /// Finds an actor's collections, all of them: its lines in time order with running totals of what came in since
    /// it last spent, judged at each transaction it spends in.
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <param name="actor">The actor.</param>
    /// <returns>The collections, in time order.</returns>
    private static List<Collection> CollectionsOf(GraphShape shape, ShapeActor actor)
    {
        var lines = shape.In(actor.Index).Concat(shape.Out(actor.Index)).Order().Select(index => shape.Flows[index]);
        var collections = new List<Collection>();
        var payments = new List<ShapeFlow>();
        var payers = new HashSet<int>();
        var collected = Satoshi.Zero;
        var sweep = new List<ShapeFlow>();
        foreach (var line in lines.Where(line => !line.IsShared))
        {
            if (line.To == actor.Index)
            {
                Close();
                if (!PatternActors.Forwards(shape, line))
                {
                    payments.Add(line);
                    payers.Add(line.From);
                    collected += line.Amount;
                }
            }
            else if (sweep.Count == 0 || sweep[0].Txid == line.Txid)
            {
                sweep.Add(line);
            }
            else
            {
                Close();
                sweep.Add(line);
            }
        }

        Close();
        return collections;

        // Judges the transaction the actor spent in against what came in since it last spent, and starts over.
        void Close()
        {
            if (sweep.Count == 0)
            {
                return;
            }

            var swept = sweep.Aggregate(Satoshi.Zero, (sum, flow) => sum + flow.Amount);
            var spent = sweep[0].Spent.ToHashSet();
            var sweptPayers = payments.Where(payment => payment.Paid.Any(spent.Contains))
                .Select(payment => payment.From).Distinct().Count();
            if (sweptPayers >= MinimumPayers
                && collected.Value > 0
                && PatternText.Share(swept, collected) is >= SweepShare and <= MostSweep)
            {
                collections.Add(new Collection(actor.Index, [.. payments.Select(flow => flow.Index)],
                    [.. sweep.OrderByDescending(flow => flow.Amount).Select(flow => flow.Index)], payers.Count));
            }

            payments.Clear();
            payers.Clear();
            collected = Satoshi.Zero;
            sweep.Clear();
        }
    }

    /// <summary>Describes the collections that sweep to one actor.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="group">The collections, the earliest first.</param>
    /// <param name="analysis">The analysis.</param>
    /// <returns>The check.</returns>
    private static CheckFinding Describe(GraphShape shape, IReadOnlyList<Collection> group, ICheckAnalysis analysis)
    {
        var firstSweep = shape.Flows[group[0].Sweep[0]];
        var where = PatternText.Actor(shape.Actors[firstSweep.To]);
        var collectors = group.Select(collection => collection.Collector).Distinct().Count();
        var parts = group.Select(collection => Part(shape, collection)).ToList();
        var payers = group.Sum(collection => collection.Payers);
        var why = $"Analysen {analysis.Named()} ser {CheckText.Count(collectors)} aktörer bland de hämtade tx:erna "
            + $"som samlar in pengar på samma sätt och sveper dem till samma aktör, {where}: minst "
            + $"{CheckText.Count(MinimumPayers)} olika avsändare betalar till en aktör som inte spenderar under tiden, "
            + $"och sedan sveps {CheckText.Percent(SweepShare)} till {CheckText.Percent(MostSweep)} av det insamlade "
            + "vidare i en tx som spenderar deras mynt. " + PatternText.Parts(parts) + " Det är formen hos "
            + "insamlingsadresser i ett bedrägeri som töms till samma plånbok, men det kan också vara en handlare som "
            + "tömmer sina kassor till sin kalla plånbok. Insättningar som bara förs vidare från "
            + "en annan adress i grafen räknas inte som avsändare, och en börs eller tjänst med etikett som mottagare "
            + "säger inget, eftersom den har många kunder.";
        var flows = group.SelectMany(collection => collection.Payments.Concat(collection.Sweep))
            .Select(index => shape.Flows[index]);
        return PatternText.Check(
            "collect:" + firstSweep.ToAddresses[0].Value,
            $"Samma insamling, {CheckText.Count(collectors)} aktörer",
            $"{CheckText.Count(payers)} avsändare samlas och sveps till {where}",
            why,
            flows);
    }

    /// <summary>Describes one collection in a sentence.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="collection">The collection.</param>
    /// <returns>The sentence.</returns>
    private static string Part(GraphShape shape, Collection collection)
    {
        var sweep = shape.Flows[collection.Sweep[0]];
        var payments = collection.Payments.Select(index => shape.Flows[index]).ToList();
        var collected = payments.Aggregate(Satoshi.Zero, (sum, flow) => sum + flow.Amount);
        return $"{PatternText.Actor(shape.Actors[collection.Collector])} tog emot {CheckText.Btc(collected)} BTC "
            + $"från {CheckText.Count(collection.Payers)} avsändare i {CheckText.Count(payments.Count)} tx och svepte "
            + $"{CheckText.Btc(sweep.Amount)} BTC i tx {PatternText.Tx(sweep)}, "
            + $"{PatternText.Between(payments[0], sweep)} efter den första.";
    }

    /// <summary>One collection: the collecting actor, the payments it took and the sweep's lines.</summary>
    /// <param name="Collector">The collecting actor's index.</param>
    /// <param name="Payments">The payments' lines, in time order.</param>
    /// <param name="Sweep">The sweep's lines, the largest first.</param>
    /// <param name="Payers">How many different payers.</param>
    private sealed record Collection(int Collector, IReadOnlyList<int> Payments, IReadOnlyList<int> Sweep, int Payers);
}
