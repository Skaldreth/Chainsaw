using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Recognises a service from how it behaves (CLAUDE.md, <em>Attribuera</em>): deposit addresses that each get a
/// payment from a sender of their own and are then swept together into one address (an exchange's hot wallet), again
/// and again, and payouts in a batch to many recipients. A check with an area of the sort Service; it says
/// <em>trolig</em>, never which service, and is a judgement, never a fact (principle 2).
/// </summary>
/// <remarks>
/// A scammer who collects several victims' payments and sweeps them together has the same shape on a small scale, so
/// the sweeps must be several, from many independent senders and over time, and an address that a start point or a
/// victim (a label) paid is never counted as a customer's deposit.
/// </remarks>
internal sealed class ServiceBehaviourCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "service-behaviour";

    /// <summary>How many deposit addresses a sweep must empty to count at all.</summary>
    public const int MinimumDepositsPerSweep = 2;

    /// <summary>How many sweeps must go to the same address: a service sweeps again and again.</summary>
    public const int MinimumSweeps = 2;

    /// <summary>How many deposit addresses the sweeps into one address must empty in all.</summary>
    public const int MinimumDeposits = 10;

    /// <summary>
    /// How many independent senders (owners, as the clustering counts them) must have paid the deposits.
    /// </summary>
    public const int MinimumSenders = 10;

    /// <summary>How many recipients a payout must have to be a batch.</summary>
    public const int MinimumBatchRecipients = 8;

    /// <summary>The most different input addresses a batch payout may have: a hot wallet pays from few.</summary>
    public const int MostBatchPayers = 2;

    /// <summary>
    /// How long the payments into the deposits must be spread over: a service has customers over time.
    /// </summary>
    public static readonly TimeSpan MinimumSpread = TimeSpan.FromDays(1);

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Tjänst ur beteende";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var byId = input.ById();
        var coinJoins = input.CoinJoins();
        var protectedOwners = input.Protected.Select(input.OwnerOf).ToHashSet();
        var sweeps = input.Transactions
            .Where(transaction => !coinJoins.ContainsKey(transaction.Txid))
            .Select(transaction => Sweep(transaction, byId, input, protectedOwners))
            .OfType<SweepRead>()
            .GroupBy(sweep => sweep.HotWallet)
            .Where(group => IsService([.. group]))
            .Select(group => Describe(group.Key, [.. group]))
            .ToList();
        var hotWallets = sweeps.Select(finding => finding.Addresses[0]).ToHashSet();
        var batches = input.Transactions
            .Where(transaction => !coinJoins.ContainsKey(transaction.Txid) && IsBatch(transaction))
            .Select(transaction => DescribeBatch(transaction, hotWallets));
        return [.. sweeps.Concat(batches).OrderBy(finding => finding.Key, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Reads a transaction as a sweep: it pays one address, and at least <see cref="MinimumDepositsPerSweep"/> of its
    /// inputs spend a deposit, a coin that a transaction in the graph paid to an address that did not pay it. A
    /// deposit that is, or was paid by, a start point or a victim is left out.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="byId">The graph's transactions.</param>
    /// <param name="input">The input, for the owners.</param>
    /// <param name="protectedOwners">The owners of the start points and the victims.</param>
    /// <returns>The sweep, or <see langword="null"/> when it is none.</returns>
    private static SweepRead? Sweep(
        BitcoinTransaction transaction,
        Dictionary<Txid, BitcoinTransaction> byId,
        CheckInput input,
        IReadOnlySet<BitcoinAddress> protectedOwners)
    {
        var paying = transaction.PayingOutputs();
        if (paying.Count != 1)
        {
            return null;
        }

        var deposits = new List<Deposit>();
        foreach (var spent in transaction.Inputs)
        {
            if (spent is { PreviousTxid: { } txid, Address: { } address }
                && byId.TryGetValue(txid, out var funding)
                && funding.InputAddresses() is [var sender, ..] payers
                && !payers.Contains(address)
                && !protectedOwners.Contains(input.OwnerOf(address))
                && payers.All(payer => !protectedOwners.Contains(input.OwnerOf(payer))))
            {
                deposits.Add(new Deposit(address, funding, input.OwnerOf(sender)));
            }
        }

        return deposits.Select(deposit => deposit.Address).Distinct().Count() >= MinimumDepositsPerSweep
            ? new SweepRead(transaction, paying[0].Address!, deposits)
            : null;
    }

    /// <summary>
    /// Says whether the sweeps into one address look like a service: at least <see cref="MinimumSweeps"/> sweeps,
    /// <see cref="MinimumDeposits"/> deposit addresses from <see cref="MinimumSenders"/> independent senders, paid
    /// over at least <see cref="MinimumSpread"/>.
    /// </summary>
    /// <param name="sweeps">The sweeps.</param>
    /// <returns><see langword="true"/> when they do.</returns>
    private static bool IsService(IReadOnlyList<SweepRead> sweeps)
    {
        var deposits = sweeps.SelectMany(sweep => sweep.Deposits).ToList();
        var times = deposits.Select(deposit => deposit.Funding.Block?.Time).OfType<DateTimeOffset>().ToList();
        return sweeps.Count >= MinimumSweeps
            && deposits.Select(deposit => deposit.Address).Distinct().Count() >= MinimumDeposits
            && deposits.Select(deposit => deposit.Sender).Distinct().Count() >= MinimumSenders
            && times.Count > 0
            && times.Max() - times.Min() >= MinimumSpread;
    }

    /// <summary>Describes the sweeps into one hot wallet.</summary>
    /// <param name="hotWallet">The address they go to.</param>
    /// <param name="sweeps">The sweeps, in the graph's order.</param>
    /// <returns>The check.</returns>
    private CheckFinding Describe(BitcoinAddress hotWallet, IReadOnlyList<SweepRead> sweeps)
    {
        var all = sweeps.SelectMany(sweep => sweep.Deposits).ToList();
        var deposits = all.Select(deposit => deposit.Address).Distinct().ToList();
        var senders = CheckText.Count(all.Select(deposit => deposit.Sender).Distinct().Count());
        var swept = sweeps.Aggregate(Satoshi.Zero, (sum, sweep) => sum + sweep.Transaction.PayingOutputs()[0].Amount);
        var count = CheckText.Count(deposits.Count);
        var times = CheckText.Count(sweeps.Count) + " tx";
        var why = $"Analysen {this.Named()} ser {count} adresser som fick betalningar från {senders} olika "
            + $"avsändare över tid och sedan tömdes, tillsammans med andra, till {CheckText.Short(hotWallet)} i "
            + $"{times}. Så sveper en börs eller en annan tjänst kundernas insättningsadresser till en hot wallet. Är "
            + "det en tjänst är insättningsadresserna troligen kundernas hos den, och en begäran kan gälla dem. Det "
            + "är en bedömning, inte ett faktum: en plånbok som samlar ihop egna mynt kan se likadan ut, adresser som "
            + "en startpunkt eller ett offer betalade räknas inte, och vilken tjänst det är säger bara en etikett.";
        return new CheckFinding(
            "sweep:" + hotWallet.Value,
            CheckKind.Check,
            AreaSort.Service,
            $"Trolig tjänst, {count} insättningar",
            $"Sveps till samma adress i {times}, {CheckText.Btc(swept)} BTC",
            why,
            [hotWallet, .. deposits],
            [],
            [
                .. sweeps.Select(sweep => CheckBasis.Of(sweep.Transaction)),
                .. all.Select(deposit => deposit.Funding).Distinct().Select(CheckBasis.Of),
            ],
            []);
    }

    /// <summary>
    /// Says whether a transaction is a payout in a batch: at least <see cref="MinimumBatchRecipients"/> recipients,
    /// paid from at most <see cref="MostBatchPayers"/> addresses, with amounts that are not mostly equal (equal parts
    /// are a split, <c>split-merge</c>).
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool IsBatch(BitcoinTransaction transaction)
    {
        var paying = transaction.PayingOutputs();
        var payers = transaction.InputAddresses();
        var equal = paying.GroupBy(output => output.Amount).Max(group => (int?)group.Count()) ?? 0;
        return paying.Count >= MinimumBatchRecipients
            && payers.Count is > 0 and <= MostBatchPayers
            && equal * 2 < paying.Count;
    }

    /// <summary>Describes a payout in a batch.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="hotWallets">The addresses this run saw sweeps go to.</param>
    /// <returns>The check.</returns>
    private CheckFinding DescribeBatch(BitcoinTransaction transaction, IReadOnlySet<BitcoinAddress> hotWallets)
    {
        var payers = transaction.InputAddresses();
        var recipients = CheckText.Count(transaction.PayingOutputs().Count);
        var hot = payers.Any(hotWallets.Contains)
            ? " Den betalar från samma adress som svepen går till, vilket stärker bilden av en tjänst."
            : string.Empty;
        var why = $"Analysen {this.Named()} ser att tx {CheckText.Short(transaction.Txid)} betalar {recipients} "
            + $"mottagare på en gång från {CheckText.Count(payers.Count)} adress(er). Så betalar en börs eller en "
            + $"annan tjänst ut till många kunder samtidigt.{hot} Det är en bedömning, inte ett faktum: en "
            + "lönebetalning eller en vanlig plånbok som betalar många kan se likadan ut.";
        return new CheckFinding(
            "batch:" + transaction.Txid.Value,
            CheckKind.Check,
            AreaSort.Service,
            "Trolig utbetalning i batch",
            $"{recipients} mottagare, {CheckText.Btc(transaction.TotalOut)} BTC",
            why,
            [.. payers],
            [transaction.Txid],
            [CheckBasis.Of(transaction)],
            []);
    }

    /// <summary>A deposit as read.</summary>
    /// <param name="Address">The deposit address.</param>
    /// <param name="Funding">The transaction that paid into it.</param>
    /// <param name="Sender">The owner of its first payer, as the clustering counts it.</param>
    private sealed record Deposit(BitcoinAddress Address, BitcoinTransaction Funding, BitcoinAddress Sender);

    /// <summary>A sweep as read.</summary>
    /// <param name="Transaction">The sweep.</param>
    /// <param name="HotWallet">The address it pays.</param>
    /// <param name="Deposits">The deposits it empties.</param>
    private sealed record SweepRead(
        BitcoinTransaction Transaction, BitcoinAddress HotWallet, IReadOnlyList<Deposit> Deposits);
}
