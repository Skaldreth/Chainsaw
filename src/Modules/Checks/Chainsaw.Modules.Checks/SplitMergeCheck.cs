using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Recognises splitting and joining (smurfing, fan-out and fan-in): a transaction from one or two addresses that
/// splits an amount into many parts of about the same size, and a later transaction that joins several of the parts
/// again, directly or after a few hops that only pass them on. Checks with an area of the sort Obfuscation, which say
/// to follow the sum rather than every part; judgements, never facts (principle 2).
/// </summary>
internal sealed class SplitMergeCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "split-merge";

    /// <summary>How many parts of about the same size a split must have.</summary>
    public const int MinimumParts = 5;

    /// <summary>How much the largest part may be above the smallest, as a share: 0.1 is ten percent.</summary>
    public const double PartTolerance = 0.1;

    /// <summary>The most different input addresses a split may have: one owner splits, many owners mix.</summary>
    public const int MostSplitters = 2;

    /// <summary>How many hops back, through transactions that only pass a coin on, a joined part is followed.</summary>
    public const int MostHopsBack = 3;

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Uppdelning";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var coinJoins = input.CoinJoins();
        var splits = input.Transactions
            .Where(transaction => !coinJoins.ContainsKey(transaction.Txid))
            .Select(transaction => (Transaction: transaction, Parts: Parts(transaction)))
            .Where(split => split.Parts.Count >= MinimumParts)
            .ToDictionary(split => split.Transaction.Txid);
        if (splits.Count == 0)
        {
            return [];
        }

        var byId = input.ById();
        var findings = splits.Values.Select(split => DescribeSplit(split.Transaction, split.Parts)).ToList();
        foreach (var transaction in input.Transactions.Where(transaction => !splits.ContainsKey(transaction.Txid)))
        {
            var joined = transaction.Inputs
                .Select(spent => (Input: spent, Part: PartOf(spent, splits, byId)))
                .Where(entry => entry.Part is not null)
                .GroupBy(entry => entry.Part!.Value.Split)
                .Where(group => group.Count() >= 2);
            foreach (var group in joined)
            {
                findings.Add(DescribeJoin(
                    transaction, splits[group.Key], [.. group.Select(entry => entry.Input)], [.. group
                        .SelectMany(entry => entry.Part!.Value.Path)]));
            }
        }

        return [.. findings.OrderBy(finding => finding.Key, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Reads a transaction as a split: from at most <see cref="MostSplitters"/> addresses, the largest group of
    /// outputs whose amounts are within <see cref="PartTolerance"/> of each other.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The parts, in index order; fewer than <see cref="MinimumParts"/> when it is no split.</returns>
    private static IReadOnlyList<BitcoinOutput> Parts(BitcoinTransaction transaction)
    {
        var payers = transaction.InputAddresses().Count;
        var outputs = transaction.PayingOutputs();
        if (payers is 0 or > MostSplitters || outputs.Count < MinimumParts)
        {
            return [];
        }

        var sorted = outputs.Where(output => output.Amount.Value > 0).OrderBy(output => output.Amount.Value).ToList();
        var best = (Start: 0, Count: 0);
        var end = 0;
        for (var start = 0; start < sorted.Count; start++)
        {
            var limit = sorted[start].Amount.Value * (1 + PartTolerance);
            end = Math.Max(end, start);
            while (end + 1 < sorted.Count && sorted[end + 1].Amount.Value <= limit)
            {
                end++;
            }

            if (end - start + 1 > best.Count)
            {
                best = (start, end - start + 1);
            }
        }

        return [.. sorted.Skip(best.Start).Take(best.Count).OrderBy(output => output.Index)];
    }

    /// <summary>
    /// Follows an input back to a part of a split: directly, or through at most <see cref="MostHopsBack"/>
    /// transactions with one input and one output that only pass the coin on.
    /// </summary>
    /// <param name="spent">The input.</param>
    /// <param name="splits">The splits and their parts.</param>
    /// <param name="byId">The graph's transactions.</param>
    /// <returns>The split and the hops on the way, or <see langword="null"/> when it comes from none.</returns>
    private static (Txid Split, IReadOnlyList<BitcoinTransaction> Path)? PartOf(
        BitcoinInput spent,
        Dictionary<Txid, (BitcoinTransaction Transaction, IReadOnlyList<BitcoinOutput> Parts)> splits,
        Dictionary<Txid, BitcoinTransaction> byId)
    {
        var path = new List<BitcoinTransaction>();
        var current = spent;
        for (var hop = 0; hop <= MostHopsBack; hop++)
        {
            if (current is not { PreviousTxid: { } txid, PreviousIndex: { } index })
            {
                return null;
            }

            if (splits.TryGetValue(txid, out var split))
            {
                return split.Parts.Any(part => part.Index == index) ? (txid, path) : null;
            }

            if (!byId.TryGetValue(txid, out var previous) || previous.Inputs.Count != 1
                || previous.PayingOutputs().Count != 1)
            {
                return null;
            }

            path.Add(previous);
            current = previous.Inputs[0];
        }

        return null;
    }

    /// <summary>Describes a split.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="parts">Its parts.</param>
    /// <returns>The check.</returns>
    private CheckFinding DescribeSplit(BitcoinTransaction transaction, IReadOnlyList<BitcoinOutput> parts)
    {
        var sum = parts.Aggregate(Satoshi.Zero, (total, part) => total + part.Amount);
        var count = CheckText.Count(parts.Count);
        var why = $"Analysen {this.Named()} ser att tx {CheckText.Short(transaction.Txid)} delar {CheckText.Btc(sum)} "
            + $"BTC i {count} delar av ungefär samma storlek (högst {CheckText.Percent(PartTolerance)} skillnad), "
            + "från en eller två adresser. Så delar man upp ett belopp för att varje del ska se oskyldig ut och gå "
            + "olika vägar, ofta för att mötas igen vid ett uttag. Följ summan, inte varje del. Det är en bedömning, "
            + "inte ett faktum: en utbetalning till många med lika belopp kan se likadan ut.";
        return new CheckFinding(
            "split:" + transaction.Txid.Value,
            CheckKind.Check,
            AreaSort.Obfuscation,
            $"Uppdelning i {count} delar",
            $"{count} × ≈ {CheckText.Btc(parts[0].Amount)} BTC, summa {CheckText.Btc(sum)} BTC",
            why,
            [.. parts.Select(part => part.Address!).Distinct()],
            [transaction.Txid],
            [CheckBasis.Of(transaction)],
            []);
    }

    /// <summary>Describes a transaction that joins parts of a split again.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="split">The split and its parts.</param>
    /// <param name="joined">The inputs that come from its parts.</param>
    /// <param name="path">The hops between the split and the join.</param>
    /// <returns>The check.</returns>
    private CheckFinding DescribeJoin(
        BitcoinTransaction transaction,
        (BitcoinTransaction Transaction, IReadOnlyList<BitcoinOutput> Parts) split,
        IReadOnlyList<BitcoinInput> joined,
        IReadOnlyList<BitcoinTransaction> path)
    {
        var sum = joined.Aggregate(Satoshi.Zero, (total, spent) => total + (spent.Amount ?? Satoshi.Zero));
        var (count, of) = (CheckText.Count(joined.Count), CheckText.Count(split.Parts.Count));
        var hops = path.Count == 0
            ? "direkt"
            : $"genom {CheckText.Count(path.Count)} mellanled som bara skickar vidare";
        var why = $"Analysen {this.Named()} ser att tx {CheckText.Short(transaction.Txid)} samlar {count} av de "
            + $"{of} delarna ur uppdelningen i tx {CheckText.Short(split.Transaction.Txid)} igen, {hops}. Delarna "
            + "möts alltså, och summan kan följas härifrån i stället för varje del för sig. Det är en bedömning, "
            + "inte ett faktum: att delarna möts säger att de troligen har samma ägare, eller att de når samma tjänst.";
        return new CheckFinding(
            "join:" + transaction.Txid.Value,
            CheckKind.Check,
            AreaSort.Obfuscation,
            $"Delarna samlas igen, {count} av {of}",
            $"{CheckText.Btc(sum)} BTC samlas {hops}",
            why,
            [.. transaction.InputAddresses()],
            [transaction.Txid],
            [
                CheckBasis.Of(transaction),
                CheckBasis.Of(split.Transaction),
                .. path.DistinctBy(hop => hop.Txid).Select(CheckBasis.Of),
            ],
            []);
    }
}
