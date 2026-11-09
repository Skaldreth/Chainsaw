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
    /// Gets the finding's fingerprint: the heuristic, the strength and the accounts, never the transactions, so that a
    /// conclusion is sealed once even when the graph later reads more of the same accounts' history.
    /// </summary>
    public string Fingerprint =>
        Heuristic + "|" + Strength + "|" + string.Join(",", Accounts.Select(account => account.Value));

    /// <summary>Orders transaction hashes the same way every time.</summary>
    /// <param name="hashes">The hashes.</param>
    /// <returns>Each once, by text.</returns>
    public static IReadOnlyList<EthereumTxHash> Ordered(IEnumerable<EthereumTxHash> hashes) =>
        [.. hashes.Distinct().OrderBy(hash => hash.Value, StringComparer.Ordinal)];

    /// <summary>Shortens an account for a reason, as the graph names it.</summary>
    /// <param name="account">The account.</param>
    /// <returns>For example <c>0x7a90…c41f</c>.</returns>
    public static string Short(ChainAddress account) =>
        account.Value.Length <= 12 ? account.Value : account.Value[..6] + "…" + account.Value[^4..];
}

/// <summary>A confirmed transfer between two accounts, as the account heuristics read it.</summary>
/// <param name="From">The sender.</param>
/// <param name="To">The receiver.</param>
/// <param name="Transfer">The transfer.</param>
/// <param name="Time">Its block's time.</param>
internal sealed record AccountMove(ChainAddress From, ChainAddress To, EthereumTransfer Transfer, DateTimeOffset Time)
{
    /// <summary>
    /// Gives the confirmed transfers between two accounts, with their times; internal transfers (calls inside
    /// contracts) are kept so that a contract shows itself, but no heuristic counts them as money moved.
    /// </summary>
    /// <param name="activity">What the graph read.</param>
    /// <returns>The moves.</returns>
    public static List<AccountMove> Of(AccountActivity activity) =>
    [
        .. activity.Transfers
            .Where(transfer => transfer.Block is not null && transfer.From != transfer.To)
            .Select(transfer => new AccountMove(
                transfer.From.ToChainAddress(), transfer.To.ToChainAddress(), transfer, transfer.Block!.Time)),
    ];

    /// <summary>Gets whether the transfer was made inside a contract's call.</summary>
    public bool IsInternal => Transfer.Kind == EthereumTransferKind.Internal;

    /// <summary>Gets the move's place on the chain, for the first and the last.</summary>
    public (long Height, long Position) Place => (Transfer.Block!.Height, Transfer.Position);
}

/// <summary>
/// <c>deposit-sweep v1</c>: an account whose whole history is read, that took money in from others and emptied it,
/// within the window after each deposit and only, into one account, is used like a deposit address. Two or more such
/// accounts emptying into an account with a service's label (an exchange) are that service's, and merge by themselves
/// (Victor 2020). Into an account without such a label it is only a proposal: victims who send what they bought to the
/// same fraudster look exactly alike, and are not one owner. A contract is never a collector.
/// </summary>
internal static class DepositSweepHeuristic
{
    /// <summary>The heuristic's name and version.</summary>
    public static readonly HeuristicId Id = new(AccountHeuristicNames.DepositSweep, 1);

    /// <summary>How long after a deposit the account must have emptied it.</summary>
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

    /// <summary>Finds the deposit accounts and their collectors among what the graph read.</summary>
    /// <param name="activity">What the graph read, and what is known about the accounts.</param>
    /// <returns>A finding per deposit account, each with its collector first.</returns>
    public static IReadOnlyList<AccountFinding> Analyse(AccountActivity activity)
    {
        var moves = AccountMove.Of(activity);
        var sweeps = activity.FullyRead
            .Select(account => SweepOf(account, moves, activity.Contracts))
            .OfType<Sweep>()
            .ToList();
        var findings = new List<AccountFinding>();
        foreach (var collector in sweeps.GroupBy(sweep => sweep.Collector))
        {
            var deposits = collector.OrderBy(sweep => sweep.Deposit.Value, StringComparer.Ordinal).ToList();
            if (deposits.Count < LeastDeposits)
            {
                continue;
            }

            var isService = activity.Services.Contains(collector.Key);
            findings.AddRange(deposits.Select(sweep => Finding(sweep, deposits.Count, isService)));
        }

        return findings;
    }

    /// <summary>
    /// Tells whether an account emptied what it took in into one account: everything it sent went there, each
    /// deposit was followed within the window by a sweep, and nearly all of each asset that came in went out.
    /// </summary>
    /// <param name="deposit">The account, whose whole history is read.</param>
    /// <param name="moves">All the moves read.</param>
    /// <param name="contracts">The accounts known to be contracts.</param>
    /// <returns>The sweep, or <see langword="null"/>.</returns>
    private static Sweep? SweepOf(ChainAddress deposit, List<AccountMove> moves, IReadOnlySet<ChainAddress> contracts)
    {
        var sent = moves.Where(move => move.From == deposit).ToList();
        var collectors = sent.Select(move => move.To).Distinct().ToList();
        if (collectors.Count != 1 || sent.Any(move => move.IsInternal))
        {
            return null;
        }

        var collector = collectors[0];
        if (contracts.Contains(collector) || moves.Any(move => move.From == collector && move.IsInternal))
        {
            return null;
        }

        var received = moves.Where(move => move.To == deposit && !move.IsInternal).ToList();
        if (received.Count == 0 || received.Any(move => move.From == collector)
            || !received.All(move => sent.Any(sweep => sweep.Time >= move.Time && sweep.Time - move.Time <= Window))
            || !NearlyAllOf(received, sent))
        {
            return null;
        }

        var delay = received.Max(move => sent.Where(sweep => sweep.Time >= move.Time).Min(sweep => sweep.Time)
            - move.Time);
        return new Sweep(deposit, collector, [.. received, .. sent], delay);
    }

    /// <summary>Tells whether, for every asset that came in or went out, nearly all and no more went out.</summary>
    /// <param name="received">What came in.</param>
    /// <param name="sent">What went out.</param>
    /// <returns><see langword="true"/> when it did.</returns>
    private static bool NearlyAllOf(List<AccountMove> received, List<AccountMove> sent) =>
        received.Concat(sent).Select(move => move.Transfer.Amount.Asset).Distinct().All(asset =>
        {
            var inUnits = Sum(received, asset);
            var outUnits = Sum(sent, asset);
            return outUnits <= inUnits && outUnits * 1_000 >= inUnits * SweptPerMille;
        });

    /// <summary>Sums the units of one asset.</summary>
    /// <param name="moves">The moves.</param>
    /// <param name="asset">The asset.</param>
    /// <returns>The sum.</returns>
    private static BigInteger Sum(List<AccountMove> moves, Asset asset) => moves
        .Where(move => move.Transfer.Amount.Asset == asset)
        .Aggregate(BigInteger.Zero, (sum, move) => sum + move.Transfer.Amount.Units);

    /// <summary>Makes the finding of one deposit account.</summary>
    /// <param name="sweep">Its sweep.</param>
    /// <param name="deposits">How many deposit accounts empty into the same collector.</param>
    /// <param name="isService">Whether the collector has a service's label, so that it merges by itself.</param>
    /// <returns>The finding.</returns>
    private static AccountFinding Finding(Sweep sweep, int deposits, bool isService)
    {
        var hours = (int)Math.Round(sweep.Delay.TotalHours);
        var (deposit, collector) = (AccountFinding.Short(sweep.Deposit), AccountFinding.Short(sweep.Collector));
        var what = string.Create(CultureInfo.InvariantCulture,
                $"{deposit} tog emot pengar och tömde dem helt till {collector},")
            + string.Create(CultureInfo.InvariantCulture, $" inom {hours} h efter varje insättning.")
            + string.Create(CultureInfo.InvariantCulture, $" {deposits} adresser töms till samma adress")
            + " på det sättet.";
        var why = isService
            ? " Mottagaren har en etikett som tjänst, så adresserna är troligen tjänstens insättningsadresser."
            : " Mottagaren har ingen etikett som tjänst; offer som skickar till samma bedragare ser likadana ut,"
                + " därför ett förslag.";
        return new AccountFinding(
            Id,
            AccountFinding.Ordered(sweep.Moves.Select(move => move.Transfer.Transaction)),
            [sweep.Collector, sweep.Deposit],
            isService ? EvidenceStrength.Merge : EvidenceStrength.Suggestion,
            what + why + " En bedömning, inte ett faktum.");
    }

    /// <summary>A deposit account emptied into its collector.</summary>
    /// <param name="Deposit">The deposit account.</param>
    /// <param name="Collector">Where it was emptied.</param>
    /// <param name="Moves">What came in and went out.</param>
    /// <param name="Delay">The longest time from a deposit to its sweep.</param>
    private sealed record Sweep(
        ChainAddress Deposit, ChainAddress Collector, IReadOnlyList<AccountMove> Moves, TimeSpan Delay);
}

/// <summary>
/// <c>gas-funding v1</c>: accounts whose whole history is read, whose very first money was a little of the chain's own
/// coin from the same account, and which then, before sending anything else, spent it as gas to send a token, were
/// probably set up by the same person. It is weak (a service pays gas for many), so it is only a proposal the user
/// approves or dismisses (KF2b); each funded account is proposed with the payer's first funded one.
/// </summary>
internal static class GasFundingHeuristic
{
    /// <summary>The heuristic's name and version.</summary>
    public static readonly HeuristicId Id = new(AccountHeuristicNames.GasFunding, 1);

    /// <summary>How long after the gas the account must have used it.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(1);

    /// <summary>The most gas money that counts: 0.01 of the coin, in its smallest units (18 decimals).</summary>
    private static readonly BigInteger MostGas = BigInteger.Pow(10, 16);

    /// <summary>Finds the accounts funded with gas by the same account.</summary>
    /// <param name="activity">What the graph read, and what is known about the accounts.</param>
    /// <returns>A proposal per funded account after the payer's first, each with that first one.</returns>
    public static IReadOnlyList<AccountFinding> Analyse(AccountActivity activity)
    {
        var moves = AccountMove.Of(activity);
        var funded = activity.FullyRead
            .Select(account => FundingOf(account, moves, activity.Fees))
            .OfType<Funding>()
            .ToList();
        var findings = new List<AccountFinding>();
        foreach (var payer in funded.GroupBy(funding => funding.Gas.From))
        {
            var ordered = payer
                .OrderBy(funding => funding.Gas.Place)
                .ThenBy(funding => funding.Account.Value, StringComparer.Ordinal)
                .ToList();
            var anchor = ordered[0];
            findings.AddRange(ordered.Skip(1).Select(other => Finding(payer.Key, anchor, other)));
        }

        return findings;
    }

    /// <summary>
    /// Tells whether an account's first money was a little coin that it then, before sending anything else, spent as
    /// gas to send a token within the window.
    /// </summary>
    /// <param name="account">The account, whose whole history is read.</param>
    /// <param name="moves">The moves read.</param>
    /// <param name="fees">The fees.</param>
    /// <returns>The funding, or <see langword="null"/>.</returns>
    private static Funding? FundingOf(
        ChainAddress account, List<AccountMove> moves, IReadOnlyCollection<EthereumFee> fees)
    {
        var first = moves.Where(move => move.To == account).MinBy(move => move.Place);
        if (first is null || first.IsInternal || first.Transfer.Amount.Asset.Kind != AssetKind.Native
            || first.Transfer.Amount.IsZero || first.Transfer.Amount.Units > MostGas)
        {
            return null;
        }

        var start = first.Time;
        var sentFirst = moves.Where(move => move.From == account).MinBy(move => move.Place);
        var spentOnToken = sentFirst is not null && sentFirst.Place.CompareTo(first.Place) > 0
            && sentFirst.Time - start <= Window && sentFirst.Transfer.Amount.Asset.Kind != AssetKind.Native;
        var paidGas = fees.Any(fee => fee.Payer.ToChainAddress() == account && fee.Block is { } block
            && block.Time >= start && block.Time - start <= Window);
        return spentOnToken && paidGas ? new Funding(account, first) : null;
    }

    /// <summary>Makes the proposal that an account has the same owner as the payer's first funded one.</summary>
    /// <param name="payer">The account that paid the gas.</param>
    /// <param name="anchor">The payer's first funded account.</param>
    /// <param name="other">Another funded account.</param>
    /// <returns>The proposal.</returns>
    private static AccountFinding Finding(ChainAddress payer, Funding anchor, Funding other)
    {
        var (from, first, second) =
            (AccountFinding.Short(payer), AccountFinding.Short(anchor.Account), AccountFinding.Short(other.Account));
        var reason = string.Create(CultureInfo.InvariantCulture,
                $"{from} betalade den första gasen till både {first} och {second},")
            + " och båda använde den till att skicka en token innan de skickade något annat."
            + " Det tyder på samma ägare men är svagt, eftersom en tjänst kan betala gas åt många; därför ett förslag.";
        return new AccountFinding(
            Id,
            AccountFinding.Ordered([anchor.Gas.Transfer.Transaction, other.Gas.Transfer.Transaction]),
            [anchor.Account, other.Account],
            EvidenceStrength.Suggestion,
            reason);
    }

    /// <summary>An account and the gas that was its first money.</summary>
    /// <param name="Account">The account.</param>
    /// <param name="Gas">The gas.</param>
    private sealed record Funding(ChainAddress Account, AccountMove Gas);
}
