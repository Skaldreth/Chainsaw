using System.Globalization;
using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering;

/// <summary>What an account heuristic concludes, before it is sealed.</summary>
/// <param name="Heuristic">The heuristic and its version.</param>
/// <param name="Transactions">The transactions it rests on, ordered by hash.</param>
/// <param name="Accounts">The accounts it says have the same owner; the collector first for a sweep.</param>
/// <param name="Strength">Whether it merges by itself or is a proposal.</param>
/// <param name="Reason">Why, in plain Swedish.</param>
internal sealed record AccountFinding(
    HeuristicId Heuristic,
    IReadOnlyList<EthereumTxHash> Transactions,
    IReadOnlyList<ChainAddress> Accounts,
    EvidenceStrength Strength,
    string Reason)
{
    /// <summary>
    /// Gets the finding's fingerprint: the heuristic, the accounts and the transactions, so that the same conclusion
    /// is sealed once however often the graph is read.
    /// </summary>
    public string Fingerprint =>
        Heuristic + "|" + string.Join(",", Accounts.Select(account => account.Value))
        + "|" + string.Join(",", Transactions.Select(hash => hash.Value));
}

/// <summary>
/// <c>deposit-sweep v1</c>: an account that takes money in and then empties all of it, and only, into one account is
/// used like a service's deposit address; when two or more such accounts empty into the same account, they and it are
/// one service (Victor 2020). It merges by itself, as the clustering of addresses does, and is an assessment.
/// </summary>
internal static class DepositSweepHeuristic
{
    /// <summary>The heuristic's name and version.</summary>
    public static readonly HeuristicId Id = new(AccountHeuristicNames.DepositSweep, 1);

    /// <summary>How long after its last deposit an account may be emptied and still count as swept.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(3);

    /// <summary>
    /// How much of what came in must go to the collector, in thousandths (950 of 1 000, leaving room for a fee taken
    /// from it).
    /// </summary>
    private const int SweptPerMille = 950;

    /// <summary>
    /// How many deposit accounts must empty into the same account before they are taken as a service's.
    /// </summary>
    private const int LeastDeposits = 2;

    /// <summary>Finds the deposit accounts and their collectors among the transfers.</summary>
    /// <param name="transfers">The chain's confirmed transfers.</param>
    /// <returns>A finding per deposit account, each with its collector first.</returns>
    public static IReadOnlyList<AccountFinding> Analyse(IReadOnlyCollection<EthereumTransfer> transfers)
    {
        var sweeps = transfers
            .Where(transfer => transfer.Block is not null && transfer.From != transfer.To)
            .GroupBy(transfer => (Account: transfer.From.ToChainAddress(), transfer.Amount.Asset))
            .Select(group => SweepOf(group.Key.Account, group.Key.Asset, group.ToList(), transfers))
            .OfType<Sweep>()
            .ToList();
        var findings = new List<AccountFinding>();
        foreach (var collector in sweeps.GroupBy(sweep => sweep.Collector))
        {
            var deposits = collector.GroupBy(sweep => sweep.Deposit).Select(group => group.First()).ToList();
            if (deposits.Count < LeastDeposits)
            {
                continue;
            }

            findings.AddRange(deposits.Select(sweep => Finding(sweep, deposits.Count)));
        }

        return findings;
    }

    /// <summary>
    /// Tells whether an account emptied an asset into one account: everything it sent of the asset went there, within
    /// the window after a deposit from someone else, and it was nearly all that came in.
    /// </summary>
    /// <param name="deposit">The account.</param>
    /// <param name="asset">The asset.</param>
    /// <param name="sent">What it sent of the asset.</param>
    /// <param name="all">All the transfers, for what came in.</param>
    /// <returns>The sweep, or <see langword="null"/>.</returns>
    private static Sweep? SweepOf(
        ChainAddress deposit, Asset asset, List<EthereumTransfer> sent, IReadOnlyCollection<EthereumTransfer> all)
    {
        var collectors = sent.Select(transfer => transfer.To.ToChainAddress()).Distinct().ToList();
        if (collectors.Count != 1)
        {
            return null;
        }

        var collector = collectors[0];
        var received = all
            .Where(transfer => transfer.Block is not null && transfer.Amount.Asset == asset
                && transfer.To.ToChainAddress() == deposit && transfer.From.ToChainAddress() != deposit)
            .ToList();
        if (received.Count == 0 || received.Any(transfer => transfer.From.ToChainAddress() == collector))
        {
            return null;
        }

        var lastIn = received.Max(transfer => transfer.Block!.Time);
        var firstOut = sent.Min(transfer => transfer.Block!.Time);
        var inUnits = received.Aggregate(BigInteger.Zero, (sum, transfer) => sum + transfer.Amount.Units);
        var outUnits = sent.Aggregate(BigInteger.Zero, (sum, transfer) => sum + transfer.Amount.Units);
        var nearlyAll = outUnits * 1_000 >= inUnits * SweptPerMille && outUnits <= inUnits;
        var soon = firstOut >= received.Min(transfer => transfer.Block!.Time) && firstOut - lastIn <= Window;
        return nearlyAll && soon
            ? new Sweep(deposit, collector, asset, [.. received, .. sent], firstOut - lastIn)
            : null;
    }

    /// <summary>Makes the finding of one deposit account.</summary>
    /// <param name="sweep">Its sweep.</param>
    /// <param name="deposits">How many deposit accounts empty into the same collector.</param>
    /// <returns>The finding.</returns>
    private static AccountFinding Finding(Sweep sweep, int deposits)
    {
        var hours = Math.Max(0, (int)Math.Round(sweep.Delay.TotalHours));
        var (deposit, collector) = (Short(sweep.Deposit), Short(sweep.Collector));
        var reason = string.Create(CultureInfo.InvariantCulture,
                $"{deposit} tog emot {sweep.Asset.Symbol} och tömdes helt till {collector} inom {hours} h.")
            + string.Create(CultureInfo.InvariantCulture, $" {deposits} adresser töms till samma adress på det sättet,")
            + " som en tjänsts insättningsadresser gör. En bedömning, inte ett faktum.";
        return new AccountFinding(
            Id,
            [.. sweep.Transfers.Select(transfer => transfer.Transaction).Distinct().OrderBy(hash => hash.Value,
                StringComparer.Ordinal)],
            [sweep.Collector, sweep.Deposit],
            EvidenceStrength.Merge,
            reason);
    }

    /// <summary>Shortens an account for a reason, as the graph names it.</summary>
    /// <param name="account">The account.</param>
    /// <returns>For example <c>0x7a90…c41f</c>.</returns>
    internal static string Short(ChainAddress account) =>
        account.Value.Length <= 12 ? account.Value : account.Value[..6] + "…" + account.Value[^4..];

    /// <summary>A deposit account emptied into its collector.</summary>
    /// <param name="Deposit">The deposit account.</param>
    /// <param name="Collector">Where it was emptied.</param>
    /// <param name="Asset">The asset.</param>
    /// <param name="Transfers">What came in and went out.</param>
    /// <param name="Delay">From the last deposit to the sweep.</param>
    private sealed record Sweep(
        ChainAddress Deposit, ChainAddress Collector, Asset Asset, IReadOnlyList<EthereumTransfer> Transfers,
        TimeSpan Delay);
}

/// <summary>
/// <c>gas-funding v1</c>: accounts whose very first money was a little of the chain's own coin from the same account,
/// which they then spent as gas to send tokens, were probably set up by the same person. It is weak (a service pays gas
/// for many), so it is only a proposal the user approves or dismisses (KF2b).
/// </summary>
internal static class GasFundingHeuristic
{
    /// <summary>The heuristic's name and version.</summary>
    public static readonly HeuristicId Id = new(AccountHeuristicNames.GasFunding, 1);

    /// <summary>How long after the gas the account must have used it.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(1);

    /// <summary>The most gas money that counts: 0.01 of the coin, in its smallest units (18 decimals).</summary>
    private static readonly BigInteger MostGas = BigInteger.Pow(10, 16);

    /// <summary>How many funded accounts the same payer must have.</summary>
    private const int LeastFunded = 2;

    /// <summary>Finds the accounts funded with gas by the same account.</summary>
    /// <param name="transfers">The chain's confirmed transfers.</param>
    /// <param name="fees">The fees paid.</param>
    /// <returns>A proposal per payer with two or more funded accounts.</returns>
    public static IReadOnlyList<AccountFinding> Analyse(
        IReadOnlyCollection<EthereumTransfer> transfers, IReadOnlyCollection<EthereumFee> fees)
    {
        var confirmed = transfers.Where(transfer => transfer.Block is not null).ToList();
        var funded = confirmed
            .GroupBy(transfer => transfer.To.ToChainAddress())
            .Select(group => FundingOf(group.Key, confirmed, fees))
            .OfType<(ChainAddress Account, EthereumTransfer Gas)>()
            .ToList();
        var findings = new List<AccountFinding>();
        foreach (var payer in funded.GroupBy(entry => entry.Gas.From.ToChainAddress()))
        {
            var accounts = payer.Select(entry => entry.Account).Distinct().OrderBy(a => a.Value, StringComparer.Ordinal)
                .ToList();
            if (accounts.Count < LeastFunded)
            {
                continue;
            }

            var reason = $"{DepositSweepHeuristic.Short(payer.Key)} betalade den första gasen till {accounts.Count} "
                + "adresser innan någon av dem skickade något. Det tyder på samma ägare men är svagt, eftersom "
                + "en tjänst kan betala gas åt många; därför ett förslag.";
            findings.Add(new AccountFinding(
                Id,
                [.. payer.Select(entry => entry.Gas.Transaction).Distinct()
                    .OrderBy(hash => hash.Value, StringComparer.Ordinal)],
                accounts,
                EvidenceStrength.Suggestion,
                reason));
        }

        return findings;
    }

    /// <summary>
    /// Tells whether an account's first money was a little coin that it then spent as gas to send a token.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <param name="confirmed">The confirmed transfers.</param>
    /// <param name="fees">The fees.</param>
    /// <returns>The account and its gas, or <see langword="null"/>.</returns>
    private static (ChainAddress Account, EthereumTransfer Gas)? FundingOf(
        ChainAddress account, List<EthereumTransfer> confirmed, IReadOnlyCollection<EthereumFee> fees)
    {
        var first = confirmed
            .Where(transfer => transfer.To.ToChainAddress() == account && transfer.From.ToChainAddress() != account)
            .MinBy(transfer => (transfer.Block!.Height, transfer.Position));
        if (first is null || first.Amount.Asset.Kind != AssetKind.Native || first.Amount.Units > MostGas
            || first.Amount.IsZero)
        {
            return null;
        }

        var start = first.Block!.Time;
        var sentToken = confirmed.Any(transfer => transfer.From.ToChainAddress() == account
            && transfer.Amount.Asset.Kind != AssetKind.Native
            && transfer.Block!.Time >= start && transfer.Block.Time - start <= Window);
        var paidGas = fees.Any(fee => fee.Payer.ToChainAddress() == account && fee.Block is { } block
            && block.Time >= start && block.Time - start <= Window);
        return sentToken && paidGas ? (account, first) : null;
    }
}
