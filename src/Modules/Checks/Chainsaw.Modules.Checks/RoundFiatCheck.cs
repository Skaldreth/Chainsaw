using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Round amounts in fiat (<c>round-fiat</c>): a transaction where every output but one is a round amount in SEK, EUR
/// or USD at the block's time, valued with the rates the case already holds. A person who pays usually pays a round
/// amount in the money they think in, so the round outputs are likely the payments and the one that is not is likely
/// the sender's change. It is a weak sign and a check, never a merge: an exchange's price when the coins were bought
/// differs from the source's at the block by up to a few percent, and some amounts are round by chance. It is a
/// judgement, never a fact (principle 2), and says which rates it used, with their sealed responses (principle 1).
/// </summary>
/// <remarks>
/// A transaction whose block time has no rate in the case is not weighed, and a currency without one is said to be
/// missing; nothing is fetched and nothing is guessed (principle 9). The weighing is exact decimal arithmetic in a fixed
/// order, so the same case always gives the same findings.
/// </remarks>
internal sealed class RoundFiatCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "round-fiat";

    /// <summary>
    /// How far an amount may lie from a round number and still count as round: half a percent, about what the price
    /// moves within the hour the rate is the close of. A wider margin makes about every tenth amount round by chance.
    /// </summary>
    public const decimal Tolerance = 0.005m;

    /// <summary>
    /// The leading digits of a round number, times a power of ten: one digit, or 1.5 and 2.5 (for example
    /// <c>1 500</c> and <c>25 000</c>), which people pay as often.
    /// </summary>
    private static readonly IReadOnlyList<decimal> RoundLeads = [1m, 1.5m, 2m, 2.5m, 3m, 4m, 5m, 6m, 7m, 8m, 9m];

    /// <summary>The Swedish culture amounts are written in.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>The analysis's name and version.</summary>
    public static readonly HeuristicId Identity = new(Name, 1);

    /// <inheritdoc/>
    public HeuristicId Id => Identity;

    /// <inheritdoc/>
    public string DisplayName => "Jämna belopp i fiat";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var changes = LatestFindings.Of<ChangeFinding>(input)
            .GroupBy(found => found.Transaction.Txid)
            .ToDictionary(
                group => group.Key, group => new ClusteringChange(group.First().Stored, group.First().Finding));
        var findings = new List<CheckFinding>();
        foreach (var transaction in input.Transactions)
        {
            if (!CasePrices.Weighable(transaction)
                || !input.Prices.TryGetValue(transaction.Txid, out var prices)
                || prices.Count == 0)
            {
                continue;
            }

            if (Weigh(transaction, prices) is { } weighed)
            {
                findings.Add(Describe(transaction, prices, weighed, changes.GetValueOrDefault(transaction.Txid)));
            }
        }

        return findings;
    }

    /// <summary>
    /// Gives the nearest round number to an amount of money, when the amount lies within <see cref="Tolerance"/> of
    /// it and is at least <see cref="Minimum"/>.
    /// </summary>
    /// <param name="value">The amount, exact.</param>
    /// <param name="currency">Its currency.</param>
    /// <returns>The round number and how far the amount lies from it, or <see langword="null"/>.</returns>
    public static RoundHit? Round(decimal value, FiatCurrency currency)
    {
        if (value < Minimum(currency))
        {
            return null;
        }

        var power = 1m;
        while (power * 10m <= value)
        {
            power *= 10m;
        }

        // The next power of ten counts too, so that 9 980 is near 10 000.
        return RoundLeads.Select(lead => lead * power)
            .Append(power * 10m)
            .Select(target => new RoundHit(currency, value, target, Math.Abs(value - target) / target))
            .Where(hit => hit.Deviation <= Tolerance)
            .OrderBy(hit => hit.Deviation)
            .ThenBy(hit => hit.Target)
            .FirstOrDefault();
    }

    /// <summary>
    /// Gives the smallest amount that is weighed in a currency, so that dust and small fees are never taken as a
    /// round payment: 100 kronor, 10 euro or 10 dollars.
    /// </summary>
    /// <param name="currency">The currency.</param>
    /// <returns>The smallest amount.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The currency is unknown.</exception>
    private static decimal Minimum(FiatCurrency currency) => currency switch
    {
        FiatCurrency.Sek => 100m,
        FiatCurrency.Eur or FiatCurrency.Usd => 10m,
        _ => throw new ArgumentOutOfRangeException(nameof(currency), currency, "Unknown currency."),
    };

    /// <summary>
    /// Weighs a transaction's outputs: holds when exactly one output is round in no currency and every other is round
    /// in at least one.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="prices">The prices the case holds at its block time, in the order of the currencies.</param>
    /// <returns>The round outputs with their hits and the likely change, or <see langword="null"/>.</returns>
    private static Weighed? Weigh(BitcoinTransaction transaction, IReadOnlyList<Price> prices)
    {
        var outputs = transaction.PayingOutputs();
        var hits = outputs.Select(output => (
                Output: output,
                Hits: prices.Select(price => Round(price.ValueOf(output.Amount).Value, price.Currency))
                    .OfType<RoundHit>()
                    .ToList()))
            .ToList();
        var notRound = hits.Where(entry => entry.Hits.Count == 0).ToList();
        if (notRound.Count != 1)
        {
            return null;
        }

        var payments = hits.Where(entry => entry.Hits.Count > 0)
            .Select(entry => new RoundOutput(entry.Output, entry.Hits))
            .ToList();
        return new Weighed(payments, notRound[0].Output);
    }

    /// <summary>Describes a transaction whose round outputs point at the change.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="prices">The prices it was weighed with.</param>
    /// <param name="weighed">What the weighing found.</param>
    /// <param name="change">The clustering's change finding for the transaction, if any.</param>
    /// <returns>The check.</returns>
    private CheckFinding Describe(
        BitcoinTransaction transaction,
        IReadOnlyList<Price> prices,
        Weighed weighed,
        ClusteringChange? change)
    {
        var block = transaction.Block!;
        var rates = RatesOf(prices);
        var first = weighed.Payments[0].Hits[0];
        var title = weighed.Payments.Count == 1
            ? $"Jämnt belopp i {first.Currency.Code()}"
            : "Jämna belopp i fiat";
        var summary = weighed.Payments.Count == 1
            ? $"Output {weighed.Payments[0].Output.Index} ≈ {Money(first.Target)} {first.Currency.Code()}, " +
              $"output {weighed.Change.Index} trolig växel"
            : $"{CheckText.Count(weighed.Payments.Count)} outputs jämna, output {weighed.Change.Index} trolig växel";
        string[] parts =
        [
            $"Analysen {this.Named()} värderar outputs i tx {CheckText.Short(transaction.Txid)} till kursen vid block "
            + $"{block.Height.ToString(CultureInfo.InvariantCulture)} ({Time(block.Time)} UTC), med kurser som redan "
            + "finns i ärendet.",
            string.Join(" ", weighed.Payments.Select(DescribeOutput)),
            $"Output {CheckText.Count(weighed.Change.Index)} ({CheckText.Btc(weighed.Change.Amount)} BTC) är inte "
            + "jämn i någon valuta. Den som betalar brukar betala ett jämnt belopp i pengarna den tänker i, så de "
            + "jämna är troliga betalningar och den andra trolig växel tillbaka till avsändaren.",
            Agreement(weighed.Change, change),
            Missing(prices),
            "Kurserna: " + string.Join(" ", rates.Select(rate => $"{rate.Reading} SHA-256 {rate.Sha256}.")),
            "Ett jämnt belopp i fiat är ett svagt tecken: börsens kurs vid köpet kan skilja några procent från "
            + $"källans vid blocket, och vissa belopp är jämna av en slump (inom {Percent(Tolerance)}). Det slår "
            + "aldrig ihop något. Det är en bedömning, inte ett faktum.",
        ];
        var why = string.Join(" ", parts.Where(part => part.Length > 0));
        return new CheckFinding(
            transaction.Txid.Value,
            CheckKind.Check,
            AreaSort.Wallet,
            title,
            summary,
            why,
            [.. transaction.InputAddresses().Append(weighed.Change.Address!).Distinct()],
            [transaction.Txid],
            [CheckBasis.Of(transaction)],
            [])
        {
            Rates = rates,
        };
    }

    /// <summary>Describes one round output, every currency it is round in.</summary>
    /// <param name="round">The output and its hits.</param>
    /// <returns>For example <c>Output 0: 0,0512 BTC = 5 012,34 SEK, 0,2 % från 5 000 SEK.</c></returns>
    private static string DescribeOutput(RoundOutput round)
    {
        var values = round.Hits.Select(hit =>
            $"{Money(Math.Round(hit.Value, 2, MidpointRounding.AwayFromZero))} {hit.Currency.Code()}, "
            + $"{Percent(hit.Deviation)} från {Money(hit.Target)} {hit.Currency.Code()}");
        return $"Output {CheckText.Count(round.Output.Index)}: {CheckText.Btc(round.Output.Amount)} BTC = "
            + string.Join("; ", values) + ".";
    }

    /// <summary>Says whether the clustering's change agrees.</summary>
    /// <param name="likely">The output this analysis takes for the change.</param>
    /// <param name="change">The clustering's change finding, if any.</param>
    /// <returns>The sentence, in Swedish.</returns>
    private static string Agreement(BitcoinOutput likely, ClusteringChange? change)
    {
        if (change is null)
        {
            return "Klustringen har ingen slutsats om växeln i tx:en.";
        }

        var index = CheckText.Count(change.Finding.OutputIndex);
        return change.Finding.OutputIndex == likely.Index
            ? $"Det stöder klustringens {change.Stored.Heuristic}, som också pekar ut output {index} som växel."
            : $"Klustringens {change.Stored.Heuristic} pekar i stället ut output {index} som växel; tecknen går isär.";
    }

    /// <summary>Says which currencies the case has no rate for, so that nothing is taken as weighed that was not.</summary>
    /// <param name="prices">The prices the case holds.</param>
    /// <returns>The sentence, or an empty string when every currency has a rate.</returns>
    private static string Missing(IReadOnlyList<Price> prices)
    {
        var missing = CasePrices.Currencies.Where(currency => prices.All(price => price.Currency != currency))
            .Select(currency => currency.Code())
            .ToList();
        return missing.Count == 0
            ? string.Empty
            : $"Ärendet har ingen kurs i {string.Join(" eller ", missing)} vid blocket, så den valutan vägs inte; "
              + "ingen kurs hämtades eller gissades.";
    }

    /// <summary>
    /// Gives the rates the prices rest on, each sealed response once, in the order they are used, with what was read
    /// from it.
    /// </summary>
    /// <param name="prices">The prices.</param>
    /// <returns>The rates.</returns>
    private static List<CheckRate> RatesOf(IReadOnlyList<Price> prices)
    {
        var rates = new List<CheckRate>();
        foreach (var price in prices)
        {
            var steps = price.Steps;
            var origins = price.Origins;
            for (var i = 0; i < origins.Count; i++)
            {
                var origin = origins[i];
                if (rates.All(rate => rate.Sequence != origin.Sequence))
                {
                    rates.Add(new CheckRate(origin.Source, origin.Sequence, origin.Sha256, steps[i]));
                }
            }
        }

        return rates;
    }

    /// <summary>Writes an amount of money, with decimals only when it has them.</summary>
    /// <param name="value">The amount.</param>
    /// <returns>For example <c>5 000</c> or <c>5 012,34</c>.</returns>
    private static string Money(decimal value) => value.ToString("#,##0.##", Swedish);

    /// <summary>Writes a share as a percentage with one decimal.</summary>
    /// <param name="share">The share.</param>
    /// <returns>For example <c>0,2 %</c>.</returns>
    private static string Percent(decimal share) => CheckText.Percent((double)share);

    /// <summary>Writes a block time.</summary>
    /// <param name="time">The time.</param>
    /// <returns>For example <c>2024-03-01 14:05</c>.</returns>
    private static string Time(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The clustering's change finding for a transaction, of the heuristic's highest version.</summary>
    /// <param name="Stored">The stored finding, for its heuristic and version.</param>
    /// <param name="Finding">The finding.</param>
    private sealed record ClusteringChange(StoredFinding Stored, ChangeFinding Finding);

    /// <summary>What the weighing of a transaction found.</summary>
    /// <param name="Payments">The round outputs, in index order.</param>
    /// <param name="Change">The one output that is round in no currency.</param>
    private sealed record Weighed(IReadOnlyList<RoundOutput> Payments, BitcoinOutput Change);

    /// <summary>An output that is round in at least one currency.</summary>
    /// <param name="Output">The output.</param>
    /// <param name="Hits">Each currency it is round in, in the order of the currencies.</param>
    private sealed record RoundOutput(BitcoinOutput Output, IReadOnlyList<RoundHit> Hits);
}

/// <summary>An amount of money that lies near a round number.</summary>
/// <param name="Currency">The currency.</param>
/// <param name="Value">The amount, exact.</param>
/// <param name="Target">The round number.</param>
/// <param name="Deviation">How far the amount lies from it, as a share of the round number.</param>
internal sealed record RoundHit(FiatCurrency Currency, decimal Value, decimal Target, decimal Deviation);
