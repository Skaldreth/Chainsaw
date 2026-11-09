using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Recognises splitting and joining (smurfing, fan-out and fan-in): a transaction from one or two addresses that
/// splits an amount into many parts of about the same size, or one owner that pays such parts to many recipients in
/// several transactions within a short time, and a later transaction that joins several of the parts again, directly
/// or after hops that only pass them on. Checks with an area of the sort Obfuscation, which say to follow the sum
/// rather than every part; judgements, never facts (principle 2).
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

    /// <summary>
    /// How many hops back, through transactions that only pass a coin on, a joined part is followed: a split's parts
    /// often take long ways before they meet.
    /// </summary>
    public const int MostHopsBack = 12;

    /// <summary>The smallest part, in satoshi, of a split spread over several transactions.</summary>
    public const long SmallestSpreadPart = 10_000;

    /// <summary>How long a split spread over several transactions may take, from its first part to its last.</summary>
    public static readonly TimeSpan SpreadWindow = TimeSpan.FromHours(24);

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 2);

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
            .Select(split => new SplitRead(
                "split:" + split.Transaction.Txid.Value,
                [split.Transaction],
                [.. split.Parts.Select(part => (split.Transaction, part))]))
            .ToList();
        var inOne = splits.SelectMany(split => split.Transactions).Select(t => t.Txid).ToHashSet();
        splits.AddRange(Spread(input, coinJoins, inOne));
        if (splits.Count == 0)
        {
            return [];
        }

        var byId = input.ById();
        var partOf = new Dictionary<(Txid, int), SplitRead>();
        foreach (var split in splits)
        {
            foreach (var (transaction, part) in split.Parts)
            {
                partOf.TryAdd((transaction.Txid, part.Index), split);
            }
        }

        var findings = splits.Select(Describe).ToList();
        foreach (var transaction in input.Transactions)
        {
            var joined = transaction.Inputs
                .Select(spent => (Input: spent, Part: PartOf(spent, partOf, byId)))
                .Where(entry => entry.Part is not null && !entry.Part.Value.Split.Transactions.Contains(transaction))
                .GroupBy(entry => entry.Part!.Value.Split)
                .Where(group => group.Count() >= 2);
            foreach (var group in joined)
            {
                findings.Add(DescribeJoin(
                    transaction, group.Key, [.. group.Select(entry => entry.Input)], [.. group
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

        var band = Band(outputs.Where(output => output.Amount.Value > 0).ToList(), output => output.Amount);
        return [.. band.OrderBy(output => output.Index)];
    }

    /// <summary>
    /// Gives the largest group of items whose amounts are within <see cref="PartTolerance"/> of the smallest of them.
    /// </summary>
    /// <typeparam name="T">The kind of item.</typeparam>
    /// <param name="items">The items.</param>
    /// <param name="amount">An item's amount.</param>
    /// <returns>The group, by amount; the first such group when several are as large.</returns>
    private static List<T> Band<T>(IReadOnlyList<T> items, Func<T, Satoshi> amount)
    {
        var sorted = items.OrderBy(item => amount(item).Value).ToList();
        var best = (Start: 0, Count: 0);
        var end = 0;
        for (var start = 0; start < sorted.Count; start++)
        {
            var limit = amount(sorted[start]).Value * (1 + PartTolerance);
            end = Math.Max(end, start);
            while (end + 1 < sorted.Count && amount(sorted[end + 1]).Value <= limit)
            {
                end++;
            }

            if (end - start + 1 > best.Count)
            {
                best = (start, end - start + 1);
            }
        }

        return [.. sorted.Skip(best.Start).Take(best.Count)];
    }

    /// <summary>
    /// Finds splits spread over several transactions: one owner, as the clustering counts it, that within
    /// <see cref="SpreadWindow"/> pays at least <see cref="MinimumParts"/> different recipients parts of about the same
    /// size, in at least two transactions, and for whom the parts are at least half of what it paid in the window (so
    /// that a service paying many customers different amounts is not one). A labelled service or mixer is left out.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <param name="coinJoins">The CoinJoins, which are no one owner's payments.</param>
    /// <param name="inOne">The transactions that are splits by themselves.</param>
    /// <returns>The spread splits.</returns>
    private static IEnumerable<SplitRead> Spread(
        CheckInput input, IReadOnlyDictionary<Txid, CoinJoinFinding> coinJoins, IReadOnlySet<Txid> inOne)
    {
        var labelled = input.Mixers.SelectMany(mixer => mixer.Addresses)
            .Concat(input.Services.SelectMany(service => service.Addresses))
            .Select(input.OwnerOf)
            .ToHashSet();
        var byPayer = input.Transactions
            .Where(transaction => transaction is { Block: not null, IsCoinbase: false }
                && !coinJoins.ContainsKey(transaction.Txid) && !inOne.Contains(transaction.Txid))
            .Select(transaction => (Transaction: transaction,
                Payers: transaction.InputAddresses().Select(input.OwnerOf).Distinct().ToList()))
            .Where(entry => entry.Payers.Count == 1 && !labelled.Contains(entry.Payers[0]))
            .SelectMany(entry => entry.Transaction.PayingOutputs()
                .Where(output => input.OwnerOf(output.Address!) != entry.Payers[0])
                .Select(output => new SpreadPayment(entry.Payers[0], entry.Transaction, output,
                    input.OwnerOf(output.Address!))))
            .GroupBy(payment => payment.Payer)
            .OrderBy(group => group.Key.Value, StringComparer.Ordinal);
        foreach (var payer in byPayer)
        {
            var payments = payer.OrderBy(payment => payment.Transaction.Block!.Height)
                .ThenBy(payment => payment.Transaction.Txid.Value, StringComparer.Ordinal)
                .ThenBy(payment => payment.Output.Index)
                .ToList();
            var used = new HashSet<SpreadPayment>();
            for (var first = 0; first < payments.Count; first++)
            {
                if (used.Contains(payments[first]))
                {
                    continue;
                }

                var start = payments[first].Transaction.Block!.Time;
                var window = payments.Skip(first)
                    .TakeWhile(payment => payment.Transaction.Block!.Time - start <= SpreadWindow)
                    .Where(payment => !used.Contains(payment))
                    .ToList();
                var band = Band(
                        window.Where(payment => payment.Output.Amount.Value >= SmallestSpreadPart).ToList(),
                        payment => payment.Output.Amount)
                    .DistinctBy(payment => payment.Recipient)
                    .OrderBy(payment => window.IndexOf(payment))
                    .ToList();
                var transactions = band.Select(payment => payment.Transaction).Distinct().ToList();
                if (band.Count < MinimumParts || transactions.Count < 2 || band.Count * 2 < window.Count)
                {
                    continue;
                }

                used.UnionWith(band);
                yield return new SplitRead(
                    "spread:" + transactions[0].Txid.Value,
                    transactions,
                    [.. band.Select(payment => (payment.Transaction, payment.Output))]);
            }
        }
    }

    /// <summary>
    /// Follows an input back to a part of a split: directly, or through at most <see cref="MostHopsBack"/>
    /// transactions with one input and one output that only pass the coin on.
    /// </summary>
    /// <param name="spent">The input.</param>
    /// <param name="partOf">The split each part belongs to, by the part's coin.</param>
    /// <param name="byId">The graph's transactions.</param>
    /// <returns>The split and the hops on the way, or <see langword="null"/> when it comes from none.</returns>
    private static (SplitRead Split, IReadOnlyList<BitcoinTransaction> Path)? PartOf(
        BitcoinInput spent,
        Dictionary<(Txid, int), SplitRead> partOf,
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

            if (partOf.TryGetValue((txid, index), out var split))
            {
                return (split, path);
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

    /// <summary>Describes a split, in one transaction or spread over several.</summary>
    /// <param name="split">The split.</param>
    /// <returns>The check.</returns>
    private CheckFinding Describe(SplitRead split)
    {
        var parts = split.Parts.Select(part => part.Output).ToList();
        var sum = parts.Aggregate(Satoshi.Zero, (total, part) => total + part.Amount);
        var count = CheckText.Count(parts.Count);
        var smallest = parts.Min(part => part.Amount.Value);
        var tolerance = $"högst {CheckText.Percent(PartTolerance)} skillnad";
        string why;
        string title;
        if (split.Transactions.Count == 1)
        {
            why = $"Analysen {this.Named()} ser att tx {CheckText.Short(split.Transactions[0].Txid)} delar "
                + $"{CheckText.Btc(sum)} BTC i {count} delar av ungefär samma storlek ({tolerance}), från en eller två "
                + "adresser.";
            title = $"Uppdelning i {count} delar";
        }
        else
        {
            var span = split.Transactions[^1].Block!.Time - split.Transactions[0].Block!.Time;
            why = $"Analysen {this.Named()} ser att samma ägare, som klustringen räknar den, betalar {count} olika "
                + $"mottagare delar av ungefär samma storlek ({tolerance}), sammanlagt {CheckText.Btc(sum)} BTC, i "
                + $"{CheckText.Count(split.Transactions.Count)} tx inom {CheckText.Span(span)}, och att delarna är "
                + "det mesta ägaren betalade under tiden. Att dela upp i flera tx gör att ingen enskild tx ser ut "
                + "som en uppdelning.";
            title = $"Uppdelning i {count} delar över {CheckText.Count(split.Transactions.Count)} tx";
        }

        why += " Så delar man upp ett belopp för att varje del ska se oskyldig ut och gå olika vägar, ofta för att "
            + "mötas igen vid ett uttag. Följ summan, inte varje del. Det är en bedömning, inte ett faktum: en "
            + "utbetalning till många med lika belopp kan se likadan ut.";
        return new CheckFinding(
            split.Key,
            CheckKind.Check,
            AreaSort.Obfuscation,
            title,
            $"{count} × ≈ {CheckText.Btc(new Satoshi(smallest))} BTC, summa {CheckText.Btc(sum)} BTC",
            why,
            [.. parts.Select(part => part.Address!).Distinct()],
            [.. split.Transactions.Select(transaction => transaction.Txid)],
            [.. split.Transactions.Select(CheckBasis.Of)],
            []);
    }

    /// <summary>Describes a transaction that joins parts of a split again.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="split">The split.</param>
    /// <param name="joined">The inputs that come from its parts.</param>
    /// <param name="path">The hops between the split and the join.</param>
    /// <returns>The check.</returns>
    private CheckFinding DescribeJoin(
        BitcoinTransaction transaction,
        SplitRead split,
        IReadOnlyList<BitcoinInput> joined,
        IReadOnlyList<BitcoinTransaction> path)
    {
        var sum = joined.Aggregate(Satoshi.Zero, (total, spent) => total + (spent.Amount ?? Satoshi.Zero));
        var (count, of) = (CheckText.Count(joined.Count), CheckText.Count(split.Parts.Count));
        var hops = path.Count == 0
            ? "direkt"
            : $"genom {CheckText.Count(path.Count)} mellanled som bara skickar vidare";
        var where = split.Transactions.Count == 1
            ? $"tx {CheckText.Short(split.Transactions[0].Txid)}"
            : $"{CheckText.Count(split.Transactions.Count)} tx från tx {CheckText.Short(split.Transactions[0].Txid)}";
        var why = $"Analysen {this.Named()} ser att tx {CheckText.Short(transaction.Txid)} samlar {count} av de "
            + $"{of} delarna ur uppdelningen i {where} igen, {hops}. Delarna möts alltså, och summan kan följas "
            + "härifrån i stället för varje del för sig. Det är en bedömning, inte ett faktum: att delarna möts säger "
            + "att de troligen har samma ägare, eller att de når samma tjänst.";
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
                .. split.Transactions.Select(CheckBasis.Of),
                .. path.DistinctBy(hop => hop.Txid).Select(CheckBasis.Of),
            ],
            []);
    }

    /// <summary>A split as read: in one transaction or spread over several.</summary>
    /// <param name="Key">The finding's key: <c>split:</c> or <c>spread:</c> and the first transaction.</param>
    /// <param name="Transactions">The transactions that pay the parts, in time order.</param>
    /// <param name="Parts">The parts, each with its transaction.</param>
    private sealed record SplitRead(
        string Key,
        IReadOnlyList<BitcoinTransaction> Transactions,
        IReadOnlyList<(BitcoinTransaction Transaction, BitcoinOutput Output)> Parts);

    /// <summary>A payment by one owner to another, as a possible part of a spread split.</summary>
    /// <param name="Payer">The paying owner.</param>
    /// <param name="Transaction">The transaction.</param>
    /// <param name="Output">The output.</param>
    /// <param name="Recipient">The paid owner.</param>
    private sealed record SpreadPayment(
        BitcoinAddress Payer, BitcoinTransaction Transaction, BitcoinOutput Output, BitcoinAddress Recipient);
}
