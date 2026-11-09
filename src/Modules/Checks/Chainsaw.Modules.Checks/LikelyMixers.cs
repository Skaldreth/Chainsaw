using System.Runtime.CompilerServices;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Reads which owners without a label behave like a central mixer (CLAUDE.md, <em>Mixers är vanliga</em>): many
/// independent senders pay into the owner, the owner pays many recipients, and most deposits come out again soon
/// after, to someone who did not deposit, as the deposit minus a usual mixer fee and paid from other coins than the
/// deposit's own. Both <c>mixer-behaviour</c> and <c>mixer-chain</c> read it, so that they never disagree. What it
/// reads is a judgement built on the clustering's owners, never a fact (principle 2).
/// </summary>
/// <remarks>
/// An exchange also takes deposits from many and pays many, but its withdrawals are not, deposit for deposit, the
/// deposit minus a percent to another person a little later; a CoinJoin has many payers and is left out; an ordinary
/// wallet has few senders. The thresholds keep the false alarms in random graphs at none (the facit measures it).
/// </remarks>
internal static class LikelyMixers
{
    /// <summary>How many independent senders (owners) must have paid into the owner.</summary>
    public const int MinimumSenders = 8;

    /// <summary>How many different recipients (owners) the owner must have paid.</summary>
    public const int MinimumRecipients = 8;

    /// <summary>How many deposits must come out again as the deposit minus a usual fee.</summary>
    public const int MinimumMatched = 5;

    /// <summary>The readings already made, so that several analyses over the same input read it once.</summary>
    private static readonly ConditionalWeakTable<CheckInput, IReadOnlyList<LikelyMixer>> Read = [];

    /// <summary>Gives the owners that behave like a mixer, in the order of their owner key.</summary>
    /// <param name="input">The input.</param>
    /// <returns>The likely mixers; empty when none.</returns>
    public static IReadOnlyList<LikelyMixer> Of(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Read.GetValue(input, Build);
    }

    /// <summary>Reads the likely mixers of an input (see <see cref="Of"/>).</summary>
    /// <param name="input">The input.</param>
    /// <returns>The likely mixers.</returns>
    private static IReadOnlyList<LikelyMixer> Build(CheckInput input)
    {
        var excluded = Excluded(input);
        var coinJoins = input.CoinJoins();
        var deposits = new Dictionary<BitcoinAddress, List<MixerDeposit>>();
        var payouts = new Dictionary<BitcoinAddress, List<BitcoinTransaction>>();
        var ordered = input.Transactions
            .Where(transaction => transaction is { Block: not null, IsCoinbase: false }
                && !coinJoins.ContainsKey(transaction.Txid))
            .OrderBy(transaction => transaction.Block!.Height)
            .ThenBy(transaction => transaction.Txid.Value, StringComparer.Ordinal);
        foreach (var transaction in ordered)
        {
            var payers = transaction.InputAddresses().Select(input.OwnerOf).ToHashSet();
            if (payers.Count == 0)
            {
                continue;
            }

            if (payers.Count == 1 && !excluded.Contains(payers.First()))
            {
                Add(payouts, payers.First(), transaction);
            }

            foreach (var output in transaction.PayingOutputs())
            {
                var owner = input.OwnerOf(output.Address!);
                if (!payers.Contains(owner) && !excluded.Contains(owner))
                {
                    Add(deposits, owner, DepositOf(transaction, output));
                }
            }
        }

        var found = new List<LikelyMixer>();
        foreach (var (owner, into) in deposits.OrderBy(entry => entry.Key.Value, StringComparer.Ordinal))
        {
            if (payouts.TryGetValue(owner, out var outOf) && Reading(input, owner, into, outOf) is { } mixer)
            {
                found.Add(mixer);
            }
        }

        return found;
    }

    /// <summary>
    /// Gives the owners never read as a likely mixer: those a label says are a mixer or a service, and the start
    /// points' and victims' owners.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <returns>The owner keys.</returns>
    private static HashSet<BitcoinAddress> Excluded(CheckInput input) =>
    [
        .. input.Mixers.SelectMany(mixer => mixer.Addresses)
            .Concat(input.Services.SelectMany(service => service.Addresses))
            .Concat(input.Protected)
            .Select(input.OwnerOf),
    ];

    /// <summary>Reads one owner's deposits and payouts as a likely mixer, or not.</summary>
    /// <param name="input">The input, for the owners.</param>
    /// <param name="owner">The owner key.</param>
    /// <param name="deposits">What others paid into it, in block order.</param>
    /// <param name="payouts">The transactions only it paid, in block order.</param>
    /// <returns>The likely mixer, or <see langword="null"/> when it does not behave like one.</returns>
    private static LikelyMixer? Reading(
        CheckInput input,
        BitcoinAddress owner,
        IReadOnlyList<MixerDeposit> deposits,
        IReadOnlyList<BitcoinTransaction> payouts)
    {
        var senders = deposits.Select(deposit => SenderOf(input, deposit)).Distinct().Count();
        var recipients = payouts.SelectMany(payout => payout.PayingOutputs())
            .Select(output => input.OwnerOf(output.Address!))
            .Where(recipient => recipient != owner)
            .Distinct()
            .Count();
        if (senders < MinimumSenders || recipients < MinimumRecipients)
        {
            return null;
        }

        var matched = deposits
            .Select(deposit => new LikelyDeposit(deposit, Matches(input, owner, deposit, payouts)))
            .ToList();
        var count = matched.Count(deposit => deposit.Matches.Count > 0);

        // Most deposits must come out again: two in three, and at least the minimum.
        return count >= MinimumMatched && count * 3 >= deposits.Count * 2
            ? new LikelyMixer(owner, matched, senders, recipients)
            : null;
    }

    /// <summary>
    /// Finds the outputs of the owner's later payouts that may be a deposit's money after a usual mixer fee: within
    /// <see cref="MixerOutputsCheck.Window"/>, to someone who neither is the owner nor deposited, of the deposit minus
    /// <see cref="MixerOutputsCheck.UsualFeeLow"/> to <see cref="MixerOutputsCheck.UsualFeeHigh"/>, paid with other
    /// coins than the deposit's own; best first.
    /// </summary>
    /// <param name="input">The input, for the owners.</param>
    /// <param name="owner">The owner key.</param>
    /// <param name="deposit">The deposit.</param>
    /// <param name="payouts">The owner's payouts, in block order.</param>
    /// <returns>The matches; empty when none.</returns>
    private static IReadOnlyList<LikelyPayout> Matches(
        CheckInput input, BitcoinAddress owner, MixerDeposit deposit, IReadOnlyList<BitcoinTransaction> payouts)
    {
        var depositors = deposit.Depositors.Select(input.OwnerOf).ToHashSet();
        var amount = (double)deposit.Output.Amount.Value;
        var matches = new List<LikelyPayout>();
        foreach (var payout in payouts.Where(payout => payout.Block!.Height > deposit.Block.Height))
        {
            var after = payout.Block!.Time - deposit.Block.Time;
            if (after <= TimeSpan.Zero || after > MixerOutputsCheck.Window || SpendsDeposit(payout, deposit))
            {
                continue;
            }

            foreach (var output in payout.PayingOutputs())
            {
                var recipient = input.OwnerOf(output.Address!);
                var fee = (amount - output.Amount.Value) / amount;
                if (recipient == owner || depositors.Contains(recipient)
                    || fee < MixerOutputsCheck.UsualFeeLow || fee > MixerOutputsCheck.UsualFeeHigh)
                {
                    continue;
                }

                var score = MixerOutputsCheck.ScoreOf(
                    MixerOutputsCheck.FeeScore(fee),
                    MixerOutputsCheck.TimeScore(after),
                    MixerOutputsCheck.TypeScore(deposit, output.Address!));
                matches.Add(new LikelyPayout(payout, output, after, score));
            }
        }

        return
        [
            .. matches.OrderByDescending(match => match.Score)
                .ThenBy(match => match.Key, StringComparer.Ordinal),
        ];
    }

    /// <summary>Says whether a payout spends the deposit's own coin, which a mixer avoids to break the link.</summary>
    /// <param name="payout">The payout.</param>
    /// <param name="deposit">The deposit.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool SpendsDeposit(BitcoinTransaction payout, MixerDeposit deposit) =>
        payout.Inputs.Any(spent => spent.PreviousTxid == deposit.Transaction.Txid
            && spent.PreviousIndex == deposit.Output.Index);

    /// <summary>Gives a deposit's sender: the owner of its first input with an address.</summary>
    /// <param name="input">The input, for the owners.</param>
    /// <param name="deposit">The deposit.</param>
    /// <returns>The sender's owner key.</returns>
    private static BitcoinAddress SenderOf(CheckInput input, MixerDeposit deposit) =>
        input.OwnerOf(deposit.Transaction.InputAddresses()[0]);

    /// <summary>Reads an output paid to an owner that did not pay as a deposit into it.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="output">The output.</param>
    /// <returns>The deposit.</returns>
    private static MixerDeposit DepositOf(BitcoinTransaction transaction, BitcoinOutput output)
    {
        var depositors = transaction.InputAddresses().ToHashSet();
        var type = transaction.Inputs.Where(spent => spent.Address is not null)
            .GroupBy(spent => spent.Address!.Type)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .First()
            .Key;
        return new MixerDeposit(transaction, output, depositors, type);
    }

    /// <summary>Adds an item to a list in a dictionary, making the list when new.</summary>
    /// <typeparam name="T">The item's type.</typeparam>
    /// <param name="lists">The lists.</param>
    /// <param name="key">The key.</param>
    /// <param name="item">The item.</param>
    private static void Add<T>(Dictionary<BitcoinAddress, List<T>> lists, BitcoinAddress key, T item)
    {
        if (!lists.TryGetValue(key, out var list))
        {
            lists[key] = list = [];
        }

        list.Add(item);
    }
}

/// <summary>An owner without a label that behaves like a central mixer: a judgement, never a fact.</summary>
/// <param name="Owner">The owner key, as the clustering counts it.</param>
/// <param name="Deposits">Every deposit into it, in block order, each with the payouts that may be its money.</param>
/// <param name="Senders">How many independent senders paid into it.</param>
/// <param name="Recipients">How many different recipients it paid.</param>
internal sealed record LikelyMixer(
    BitcoinAddress Owner, IReadOnlyList<LikelyDeposit> Deposits, int Senders, int Recipients)
{
    /// <summary>Gets the deposits that come out again as the deposit minus a usual fee.</summary>
    public IEnumerable<LikelyDeposit> Matched => Deposits.Where(deposit => deposit.Matches.Count > 0);

    /// <summary>Gets the chain's key for the mixer, so that a chain can say whether a step goes into it again.</summary>
    public string Key => "likely:" + Owner.Value;
}

/// <summary>A deposit into a likely mixer and the payouts that may be its money, best first.</summary>
/// <param name="Deposit">The deposit.</param>
/// <param name="Matches">The payouts; empty when none came out.</param>
internal sealed record LikelyDeposit(MixerDeposit Deposit, IReadOnlyList<LikelyPayout> Matches);

/// <summary>A payout of a likely mixer that may be a deposit's money.</summary>
/// <param name="Transaction">The payout.</param>
/// <param name="Output">Its output.</param>
/// <param name="After">How long after the deposit.</param>
/// <param name="Score">How likely, weighed as <c>mixer-outputs</c> weighs a candidate.</param>
internal sealed record LikelyPayout(BitcoinTransaction Transaction, BitcoinOutput Output, TimeSpan After, double Score)
{
    /// <summary>Gets the payout's coin, <c>txid:index</c>.</summary>
    public string Key => MixerDeposits.CoinKey(Transaction.Txid, Output.Index);

    /// <summary>Gets the payout's address; never null.</summary>
    public BitcoinAddress Address => Output.Address!;
}
