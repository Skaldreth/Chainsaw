using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Recognises a service from how it behaves (CLAUDE.md, <em>Attribuera</em>): deposit addresses that each get a
/// payment from a sender of their own and are then swept together into one address (an exchange's hot wallet), and
/// payouts in a batch to many recipients. A check with an area of the sort Service; it says <em>trolig</em>, never
/// which service, and is a judgement, never a fact (principle 2).
/// </summary>
internal sealed class ServiceBehaviourCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "service-behaviour";

    /// <summary>How many deposit addresses a sweep must empty to count: one or two are a wallet's own coins.</summary>
    public const int MinimumDeposits = 3;

    /// <summary>How many recipients a payout must have to be a batch.</summary>
    public const int MinimumBatchRecipients = 8;

    /// <summary>The most different input addresses a batch payout may have: a hot wallet pays from few.</summary>
    public const int MostBatchPayers = 2;

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
        var sweeps = input.Transactions
            .Where(transaction => !coinJoins.ContainsKey(transaction.Txid))
            .Select(transaction => Sweep(transaction, byId))
            .OfType<SweepRead>()
            .GroupBy(sweep => sweep.HotWallet)
            .Select(group => Describe(group.Key, [.. group]))
            .ToList();
        var hotWallets = sweeps.Select(finding => finding.Addresses[0]).ToHashSet();
        var batches = input.Transactions
            .Where(transaction => !coinJoins.ContainsKey(transaction.Txid) && IsBatch(transaction))
            .Select(transaction => DescribeBatch(transaction, hotWallets));
        return [.. sweeps.Concat(batches).OrderBy(finding => finding.Key, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Reads a transaction as a sweep: it pays one address, and at least <see cref="MinimumDeposits"/> of its inputs
    /// spend a deposit, a coin that a transaction in the graph paid to an address that did not pay it, from as many
    /// different senders.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="byId">The graph's transactions.</param>
    /// <returns>The sweep, or <see langword="null"/> when it is none.</returns>
    private static SweepRead? Sweep(BitcoinTransaction transaction, Dictionary<Txid, BitcoinTransaction> byId)
    {
        var paying = transaction.PayingOutputs();
        if (paying.Count != 1)
        {
            return null;
        }

        var deposits = new List<(BitcoinAddress Address, BitcoinTransaction Funding)>();
        foreach (var spent in transaction.Inputs)
        {
            if (spent is { PreviousTxid: { } txid, Address: { } address }
                && byId.TryGetValue(txid, out var funding)
                && !funding.InputAddresses().Contains(address)
                && funding.InputAddresses().FirstOrDefault() is not null)
            {
                deposits.Add((address, funding));
            }
        }

        var senders = deposits.Select(deposit => deposit.Funding.InputAddresses()[0]).Distinct().Count();
        var addresses = deposits.Select(deposit => deposit.Address).Distinct().ToList();
        return addresses.Count >= MinimumDeposits && senders >= MinimumDeposits
            ? new SweepRead(transaction, paying[0].Address!, addresses, [.. deposits.Select(d => d.Funding).Distinct()])
            : null;
    }

    /// <summary>Describes the sweeps into one hot wallet.</summary>
    /// <param name="hotWallet">The address they go to.</param>
    /// <param name="sweeps">The sweeps, in the graph's order.</param>
    /// <returns>The check.</returns>
    private CheckFinding Describe(BitcoinAddress hotWallet, IReadOnlyList<SweepRead> sweeps)
    {
        var deposits = sweeps.SelectMany(sweep => sweep.Deposits).Distinct().ToList();
        var swept = sweeps.Aggregate(Satoshi.Zero, (sum, sweep) => sum + sweep.Transaction.PayingOutputs()[0].Amount);
        var count = CheckText.Count(deposits.Count);
        var times = sweeps.Count == 1 ? "en tx" : CheckText.Count(sweeps.Count) + " tx";
        var why = $"Analysen {this.Named()} ser {count} adresser som var och en fick en betalning från en egen "
            + $"avsändare och sedan tömdes, tillsammans med andra, till {CheckText.Short(hotWallet)} i {times}. "
            + "Så sveper en börs eller en annan tjänst kundernas insättningsadresser till en hot wallet, och insättningsadresserna "
            + "är då kundernas hos tjänsten: en begäran gäller dem. Det är en bedömning, inte ett faktum: en "
            + "plånbok som samlar ihop egna mynt kan se likadan ut, och vilken tjänst det är säger bara en etikett.";
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
                .. sweeps.SelectMany(sweep => sweep.Fundings).Distinct().Select(CheckBasis.Of),
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

    /// <summary>A sweep as read.</summary>
    /// <param name="Transaction">The sweep.</param>
    /// <param name="HotWallet">The address it pays.</param>
    /// <param name="Deposits">The deposit addresses it empties.</param>
    /// <param name="Fundings">The transactions that paid into the deposits.</param>
    private sealed record SweepRead(
        BitcoinTransaction Transaction,
        BitcoinAddress HotWallet,
        IReadOnlyList<BitcoinAddress> Deposits,
        IReadOnlyList<BitcoinTransaction> Fundings);
}
