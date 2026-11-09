using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Recognises many hops and delay (layering): a row of transactions where each takes one coin and passes almost all
/// of it on to one new address, so that the money only passes through, and how long it lay still between the hops.
/// A hop may also pay a small fee to a service (a labelled service, or the same fee address in several hops), as a
/// layering service takes for each hop. A check with an area of the sort Obfuscation; a judgement, never a fact
/// (principle 2).
/// </summary>
internal sealed class PassThroughCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "pass-through";

    /// <summary>How many hops in a row must only pass the money on to count.</summary>
    public const int MinimumHops = 3;

    /// <summary>The largest share of a hop's coin that may be lost on the way (the fee): 0.02 is two percent.</summary>
    public const double MostLostShare = 0.02;

    /// <summary>The largest share of a hop's coin that a fee to a service may take: 0.03 is three percent.</summary>
    public const double MostServiceFeeShare = 0.03;

    /// <summary>How many hops must pay the same fee address for it to count as a service's, without a label.</summary>
    public const int MinimumFeesToSameAddress = 2;

    /// <summary>
    /// The smallest coin, in satoshi, a hop passes on: below it the coin is dust, which wallets pass on without anyone
    /// laying a trail (random graphs gave long rows of dust before this limit).
    /// </summary>
    public const long SmallestCoin = 10_000;

    /// <summary>A pause between two hops at least this long is a delay.</summary>
    public static readonly TimeSpan DelayFrom = TimeSpan.FromDays(3);

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 2);

    /// <inheritdoc/>
    public string DisplayName => "Många hopp";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var hops = Hops(input).ToDictionary(hop => hop.Transaction.Txid);
        var spenders = input.Spenders();
        var findings = new List<CheckFinding>();
        foreach (var first in hops.Values.Where(hop => !IsContinued(hop, hops)))
        {
            var row = new List<Hop> { first };
            while (row.Count <= hops.Count
                && spenders.TryGetValue((row[^1].Transaction.Txid, row[^1].Onward.Index), out var next)
                && hops.TryGetValue(next.Txid, out var hop))
            {
                row.Add(hop);
            }

            if (row.Count >= MinimumHops)
            {
                findings.Add(Describe(row, input));
            }
        }

        return [.. findings.OrderBy(finding => finding.Key, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Reads the hops: transactions that only pass money on, and those that also pay a small fee to a labelled service
    /// that is not an exchange, or to a fee address that at least <see cref="MinimumFeesToSameAddress"/> such hops pay.
    /// A payment to an exchange is a cash-out, never a fee, and a hop the clustering reads as a peel chain is the peel
    /// chain's (<c>peel-chain-area</c>), so that the same chain is never told twice.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <returns>The hops, in the input's order.</returns>
    private static IEnumerable<Hop> Hops(CheckInput input)
    {
        var services = input.Services.Where(service => !service.IsExchange)
            .SelectMany(service => service.Addresses).Select(input.OwnerOf).ToHashSet();
        var exchanges = input.Services.Where(service => service.IsExchange)
            .SelectMany(service => service.Addresses).Select(input.OwnerOf).ToHashSet();
        var peeled = LatestFindings.Of<PeelChainFinding>(input).Select(found => found.Transaction.Txid).ToHashSet();
        var read = input.Transactions.Select(Read).OfType<Hop>()
            .Where(hop => hop.Fee is not { } fee
                || (!exchanges.Contains(input.OwnerOf(fee.Address!)) && !peeled.Contains(hop.Transaction.Txid)))
            .ToList();
        var feesTo = read.Where(hop => hop.Fee is not null)
            .GroupBy(hop => input.OwnerOf(hop.Fee!.Address!))
            .ToDictionary(group => group.Key, group => group.Count());
        return read.Where(hop => hop.Fee is not { } fee
            || services.Contains(input.OwnerOf(fee.Address!))
            || feesTo[input.OwnerOf(fee.Address!)] >= MinimumFeesToSameAddress);
    }

    /// <summary>
    /// Reads a transaction as a hop: one input of at least <see cref="SmallestCoin"/> and one output (with an
    /// address) of at least all but <see cref="MostLostShare"/> of it; or two outputs, where the smaller is at most
    /// <see cref="MostServiceFeeShare"/> of the coin (a fee) and the two together are at least all but
    /// <see cref="MostLostShare"/>.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The hop, or <see langword="null"/> when it is none.</returns>
    private static Hop? Read(BitcoinTransaction transaction)
    {
        if (transaction.IsCoinbase || transaction.Inputs.Count != 1
            || transaction.Inputs[0].Amount is not { Value: >= SmallestCoin } amount
            || transaction.Outputs.Any(output => output.Address is null))
        {
            return null;
        }

        var kept = amount.Value * (1 - MostLostShare);
        if (transaction.Outputs is [var only])
        {
            return only.Amount.Value >= kept ? new Hop(transaction, only, null) : null;
        }

        if (transaction.Outputs.Count != 2)
        {
            return null;
        }

        var ordered = transaction.Outputs.OrderBy(output => output.Amount.Value).ToList();
        var (fee, onward) = (ordered[0], ordered[1]);
        var isFee = fee.Amount.Value <= amount.Value * MostServiceFeeShare && fee.Amount.Value < onward.Amount.Value;
        return isFee && fee.Amount.Value + onward.Amount.Value >= kept ? new Hop(transaction, onward, fee) : null;
    }

    /// <summary>Says whether a hop's coin came onward from another hop, so that it is not the first of its row.</summary>
    /// <param name="hop">The hop.</param>
    /// <param name="hops">Every hop.</param>
    /// <returns><see langword="true"/> when it continues a row.</returns>
    private static bool IsContinued(Hop hop, Dictionary<Txid, Hop> hops) =>
        hop.Transaction.Inputs[0] is { PreviousTxid: { } previous, PreviousIndex: { } index }
        && hops.TryGetValue(previous, out var before)
        && before.Onward.Index == index;

    /// <summary>Describes a row of hops.</summary>
    /// <param name="row">The hops, in order.</param>
    /// <param name="input">The input, for the services' names.</param>
    /// <returns>The check.</returns>
    private CheckFinding Describe(IReadOnlyList<Hop> row, CheckInput input)
    {
        var transactions = row.Select(hop => hop.Transaction).ToList();
        var gaps = transactions.Zip(transactions.Skip(1))
            .Select(pair => pair.Second.Block is { } later && pair.First.Block is { } earlier
                ? later.Time - earlier.Time
                : TimeSpan.Zero)
            .ToList();
        var longest = gaps.Count == 0 ? TimeSpan.Zero : gaps.Max();
        var delayed = longest >= DelayFrom;
        var span = transactions[^1].Block is { } last && transactions[0].Block is { } first
            ? last.Time - first.Time
            : TimeSpan.Zero;
        var (into, outOf) = (transactions[0].Inputs[0].Amount!.Value, row[^1].Onward.Amount);
        var count = CheckText.Count(row.Count);
        var delay = delayed
            ? $" Pengarna låg still som längst {CheckText.Span(longest)} mellan två hopp: en fördröjning, som gör "
                + "att spåret sträcker sig långt i tiden."
            : string.Empty;
        var why = $"Analysen {this.Named()} ser {count} tx i rad som var och en tar ett mynt och skickar nästan allt "
            + $"vidare till en ny adress, under {CheckText.Span(span)}: pengarna bara passerar.{delay}"
            + FeeText(row, input)
            + " Så lägger man många hopp mellan brottet och uttaget. Hela raden kan läsas som en väg. Det är en "
            + "bedömning, inte ett faktum: en plånbok som flyttar sina pengar mellan egna adresser ser likadan ut.";
        var addresses = transactions.Select(hop => hop.Inputs[0].Address).OfType<BitcoinAddress>()
            .Append(row[^1].Onward.Address!)
            .Distinct();
        // Keyed by the first hop, so that the key holds when the row is followed further forward.
        return new CheckFinding(
            transactions[0].Txid.Value,
            CheckKind.Check,
            AreaSort.Obfuscation,
            delayed ? $"Många hopp, {count} i rad, med fördröjning" : $"Många hopp, {count} i rad",
            $"{CheckText.Btc(into)} BTC in, {CheckText.Btc(outOf)} BTC ut, längsta uppehåll {CheckText.Span(longest)}",
            why,
            [.. addresses],
            [],
            [.. transactions.Select(CheckBasis.Of)],
            []);
    }

    /// <summary>Writes what the row's hops paid in fees to a service, or nothing when none did.</summary>
    /// <param name="row">The hops.</param>
    /// <param name="input">The input, for the services' names.</param>
    /// <returns>The sentence, with a space first, or empty.</returns>
    private static string FeeText(IReadOnlyList<Hop> row, CheckInput input)
    {
        var fees = row.Select(hop => hop.Fee).OfType<BitcoinOutput>().ToList();
        if (fees.Count == 0)
        {
            return string.Empty;
        }

        var sum = fees.Aggregate(Satoshi.Zero, (total, fee) => total + fee.Amount);
        var to = fees.Select(fee => fee.Address!).Distinct().Select(address => NameOf(address, input));
        return $" {CheckText.Count(fees.Count)} av hoppen betalar också en liten avgift, sammanlagt "
            + $"{CheckText.Btc(sum)} BTC, till {string.Join(", ", to)}, som en tjänst som tar betalt för varje hopp. "
            + "Avgiften är inte en del av vägen.";
    }

    /// <summary>Names a fee's address: the label's name of the service, or the address shortened.</summary>
    /// <param name="address">The address.</param>
    /// <param name="input">The input.</param>
    /// <returns>For example <c>tjänsten Lagret</c> or <c>bc1qxy2k…x0wl</c>.</returns>
    private static string NameOf(BitcoinAddress address, CheckInput input)
    {
        var owner = input.OwnerOf(address);
        var service = input.Services.FirstOrDefault(known => known.Addresses.Any(a => input.OwnerOf(a) == owner));
        return service?.Name is { } name ? $"tjänsten {name}" : CheckText.Short(address);
    }

    /// <summary>A hop as read.</summary>
    /// <param name="Transaction">The transaction.</param>
    /// <param name="Onward">The output that carries the money on.</param>
    /// <param name="Fee">The small fee to a service, or <see langword="null"/>.</param>
    private sealed record Hop(BitcoinTransaction Transaction, BitcoinOutput Onward, BitcoinOutput? Fee);
}
