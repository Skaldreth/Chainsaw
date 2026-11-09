using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// The rule <em>samma insamling</em> of <see cref="PatternCheck"/>: an actor that nothing is known about takes payments
/// from at least <see cref="MinimumPayers"/> different payers without spending in between, and then sweeps them onward
/// in one transaction, from <see cref="SweepShare"/> to <see cref="MostSweep"/> of what it collected, as a scam's
/// collecting address does. One such actor is common (a wallet that consolidates), so the rule says something only when at least two parts of the case collect
/// the same way and sweep to the same place: the same actor, or a labelled actor of the same sort (for example two
/// exchanges).
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

    /// <summary>Finds the collections that look alike.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="analysis">The analysis, for its name in the texts.</param>
    /// <returns>A check per group of at least two alike collections.</returns>
    public static IEnumerable<CheckFinding> Find(GraphShape shape, ICheckAnalysis analysis)
    {
        var collections = shape.Actors
            .Where(actor => actor.Sort == ActorSort.Unknown)
            .Select(actor => FirstOf(shape, actor))
            .OfType<Collection>()
            .OrderBy(collection => shape.Flows[collection.Sweep[0]].Height)
            .ThenBy(collection => shape.Actors[collection.Collector].Key.Value, StringComparer.Ordinal);
        return collections
            .GroupBy(collection => SignatureOf(shape, collection), StringComparer.Ordinal)
            .Where(group => group.Count() >= 2)
            .Select(group => Describe(shape, group.Key, [.. group], analysis));
    }

    /// <summary>Finds an actor's first collection.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="actor">The actor.</param>
    /// <returns>The collection, or <see langword="null"/> when it never collects and sweeps.</returns>
    private static Collection? FirstOf(GraphShape shape, ShapeActor actor)
    {
        var outs = shape.Out(actor.Index).Select(index => shape.Flows[index])
            .Where(flow => !flow.IsShared)
            .GroupBy(flow => flow.Txid)
            .Select(group => group.ToList())
            .ToList();
        var ins = shape.In(actor.Index).Select(index => shape.Flows[index]).ToList();
        long collectedSince = long.MinValue;
        foreach (var sweep in outs)
        {
            var at = sweep[0].Height;
            var payments = ins
                .Where(flow => !flow.IsShared && flow.Height > collectedSince && flow.Height <= at
                    && flow.Txid != sweep[0].Txid)
                .ToList();
            var payers = payments.Select(flow => flow.From).Distinct().Count();
            var collected = payments.Aggregate(Satoshi.Zero, (sum, flow) => sum + flow.Amount);
            var swept = sweep.Aggregate(Satoshi.Zero, (sum, flow) => sum + flow.Amount);
            if (payers >= MinimumPayers
                && collected.Value > 0
                && PatternText.Share(swept, collected) is >= SweepShare and <= MostSweep)
            {
                return new Collection(actor.Index, [.. payments.Select(flow => flow.Index)],
                    [.. sweep.OrderByDescending(flow => flow.Amount).Select(flow => flow.Index)], payers);
            }

            collectedSince = at;
        }

        return null;
    }

    /// <summary>
    /// Gives where a collection sweeps to, as alike collections share it: the sort of a labelled actor, or the actor.
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <param name="collection">The collection.</param>
    /// <returns>For example <c>sort:Exchange</c> or <c>actor:bc1q…</c>.</returns>
    private static string SignatureOf(GraphShape shape, Collection collection)
    {
        var to = shape.Actors[shape.Flows[collection.Sweep[0]].To];
        return to.IsLabelled ? "sort:" + to.Sort : "actor:" + to.Key.Value;
    }

    /// <summary>Describes a group of alike collections.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="signature">Where they sweep to.</param>
    /// <param name="group">The collections, the earliest first.</param>
    /// <param name="analysis">The analysis.</param>
    /// <returns>The check.</returns>
    private static CheckFinding Describe(
        GraphShape shape, string signature, IReadOnlyList<Collection> group, ICheckAnalysis analysis)
    {
        var to = shape.Actors[shape.Flows[group[0].Sweep[0]].To];
        var where = to.IsLabelled ? $"en {PatternText.Sort(to.Sort)}" : PatternText.Actor(to);
        var parts = group.Select(collection => Part(shape, collection)).ToList();
        var payers = group.Sum(collection => collection.Payers);
        var why = $"Analysen {analysis.Named()} ser {CheckText.Count(group.Count)} delar av ärendet som samlar in "
            + $"pengar på samma sätt: minst {CheckText.Count(MinimumPayers)} olika avsändare betalar till en aktör som "
            + $"inte spenderar under tiden, och sedan sveps {CheckText.Percent(SweepShare)} till "
            + $"{CheckText.Percent(MostSweep)} av det insamlade vidare i en tx till "
            + $"{where}. " + PatternText.Parts(parts) + " Det är formen hos en insamlingsadress i ett bedrägeri, och "
            + "flera likadana delar kan vara samma gärningsperson.";
        var flows = group.SelectMany(collection => collection.Payments.Concat(collection.Sweep))
            .Select(index => shape.Flows[index]);
        var first = shape.Actors[group[0].Collector];
        return PatternText.Check(
            $"collect:{signature}:{first.Key.Value}",
            $"Samma insamling, {CheckText.Count(group.Count)} delar",
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
            + $"{CheckText.Btc(sweep.Amount)} BTC till {PatternText.ActorWithSort(shape.Actors[sweep.To])} i tx "
            + $"{PatternText.Tx(sweep)}, {PatternText.Between(payments[0], sweep)} efter den första.";
    }

    /// <summary>One collection: the collecting actor, the payments it took and the sweep's lines.</summary>
    /// <param name="Collector">The collecting actor's index.</param>
    /// <param name="Payments">The payments' lines, in time order.</param>
    /// <param name="Sweep">The sweep's lines, the largest first.</param>
    /// <param name="Payers">How many different payers.</param>
    private sealed record Collection(int Collector, IReadOnlyList<int> Payments, IReadOnlyList<int> Sweep, int Payers);
}
