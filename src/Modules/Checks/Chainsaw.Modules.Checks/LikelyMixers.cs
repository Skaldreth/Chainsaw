using System.Runtime.CompilerServices;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Reads which owners without a label behave like a central mixer (CLAUDE.md, <em>Mixers är vanliga</em>): many
/// independent senders pay into the owner, the owner pays many recipients, and most deposits come out again soon
/// after, whole or in two parts, to someone who did not deposit, as the deposit minus a
/// usual mixer fee and paid from other coins than the deposit's own. Both <c>mixer-behaviour</c> and
/// <c>mixer-chain</c> read it, so that they never disagree. What it reads is a judgement built on the clustering's
/// owners, never a fact (principle 2).
/// </summary>
/// <remarks>
/// <para>
/// An exchange or a payment service also takes from many and pays many, and with enough payouts some of them always
/// land near some deposit minus a few percent. So the reading is strict:
/// </para>
/// <list type="bullet">
/// <item>each payout output explains at most one deposit (assigned greedily, the best score first);</item>
/// <item>most of the owner's payout outputs that lie in a deposit's window must be explained by deposits
/// (<see cref="MinimumExplained"/>); payouts far from any deposit in the graph are of deposits it does not hold;</item>
/// <item>most of the explaining outputs go to addresses that receive only once in the graph
/// (<see cref="MinimumOneTime"/>), as a mixer pays fresh addresses and a processor pays its merchants again;</item>
/// <item>the same matching with the deposits' amounts scaled (<see cref="BaselineFactors"/>), where nothing but chance
/// can match, must explain clearly fewer on average: the difference must be at least <see cref="MinimumMatched"/> and
/// <see cref="BaselineSigmas"/> times the square root of the baseline, a margin that grows with chance's own spread,
/// so that a busy mixer passes and a busy exchange does not;</item>
/// <item>a payment processor forwards almost at once and keeps the same fee: when nearly every match comes out within
/// <see cref="ProcessorDelay"/> with nearly the same fee (<see cref="ProcessorFeeSpread"/>), it is not a mixer.</item>
/// </list>
/// </remarks>
internal static class LikelyMixers
{
    /// <summary>How many independent senders (owners) must have paid into the owner.</summary>
    public const int MinimumSenders = 8;

    /// <summary>How many different recipients (owners) the owner must have paid.</summary>
    public const int MinimumRecipients = 8;

    /// <summary>How many deposits must come out again as the deposit minus a usual fee.</summary>
    public const int MinimumMatched = 5;

    /// <summary>The least share of the owner's payout outputs that the deposits must explain.</summary>
    public const double MinimumExplained = 0.5;

    /// <summary>The least share of the explaining outputs that go to an address that receives only once.</summary>
    public const double MinimumOneTime = 0.6;

    /// <summary>
    /// How many standard deviations of chance (taken as the square root of the baseline's count) the real count must
    /// lie above the baseline.
    /// </summary>
    public const double BaselineSigmas = 2.5;

    /// <summary>The share of matches that, coming out fast with the same fee, makes the owner a processor.</summary>
    public const double ProcessorShare = 0.9;

    /// <summary>
    /// The most the fees of a processor's matches differ, between the tenth and ninetieth percentile, as a share of
    /// the deposit: a quarter of a percentage point.
    /// </summary>
    public const double ProcessorFeeSpread = 0.0025;

    /// <summary>How soon after the deposit a processor forwards: an hour, six blocks.</summary>
    public static readonly TimeSpan ProcessorDelay = TimeSpan.FromHours(1);

    /// <summary>How many outputs soonest after a deposit are tried as parts.</summary>
    public const int MostPartPool = 8;

    /// <summary>What a match in parts loses in score against a whole output, since parts match by chance more.</summary>
    private const double PartsPenalty = 0.1;

    /// <summary>
    /// The factors the deposits' amounts are scaled by for the baseline: far enough from 1 that a real mixer's
    /// payouts, deposit minus 0.5–5 %, never match a scaled amount.
    /// </summary>
    public static readonly IReadOnlyList<double> BaselineFactors = [0.8, 0.88, 1.12, 1.25];

    /// <summary>The readings already made, so that several analyses over the same input read it once.</summary>
    private static readonly ConditionalWeakTable<CheckInput, IReadOnlyList<LikelyMixer>> Readings = [];

    /// <summary>Gives the owners that behave like a mixer, in the order of their owner key.</summary>
    /// <param name="input">The input.</param>
    /// <returns>The likely mixers; empty when none.</returns>
    public static IReadOnlyList<LikelyMixer> Of(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Readings.GetValue(input, Build);
    }

    /// <summary>Reads the likely mixers of an input (see <see cref="Of"/>).</summary>
    /// <param name="input">The input.</param>
    /// <returns>The likely mixers.</returns>
    private static IReadOnlyList<LikelyMixer> Build(CheckInput input)
    {
        var excluded = Excluded(input);
        var coinJoins = input.CoinJoins();
        var deposits = new Dictionary<BitcoinAddress, List<MixerDeposit>>();
        var payouts = new Dictionary<BitcoinAddress, List<LikelyPayout>>();
        var received = new Dictionary<BitcoinAddress, int>();
        var ordered = input.Transactions
            .Where(transaction => transaction is { Block: not null, IsCoinbase: false }
                && !coinJoins.ContainsKey(transaction.Txid))
            .OrderBy(transaction => transaction.Block!.Height)
            .ThenBy(transaction => transaction.Txid.Value, StringComparer.Ordinal);
        foreach (var transaction in ordered)
        {
            var payers = transaction.InputAddresses().Select(input.OwnerOf).ToHashSet();
            foreach (var output in transaction.PayingOutputs())
            {
                received[output.Address!] = received.GetValueOrDefault(output.Address!) + 1;
            }

            if (payers.Count == 0)
            {
                continue;
            }

            Read(transaction, payers, input, excluded, deposits, payouts);
        }

        var found = new List<LikelyMixer>();
        foreach (var (owner, into) in deposits.OrderBy(entry => entry.Key.Value, StringComparer.Ordinal))
        {
            if (payouts.TryGetValue(owner, out var outOf) && Reading(input, owner, into, outOf, received) is { } mixer)
            {
                found.Add(mixer);
            }
        }

        return found;
    }

    /// <summary>Reads one transaction's deposits into owners, and its payouts when one owner pays.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="payers">Its payers' owners.</param>
    /// <param name="input">The input, for the owners.</param>
    /// <param name="excluded">The owners never read as a likely mixer.</param>
    /// <param name="deposits">The deposits by owner, to add to.</param>
    /// <param name="payouts">The payout outputs by owner, in block order, to add to.</param>
    private static void Read(
        BitcoinTransaction transaction,
        IReadOnlySet<BitcoinAddress> payers,
        CheckInput input,
        IReadOnlySet<BitcoinAddress> excluded,
        Dictionary<BitcoinAddress, List<MixerDeposit>> deposits,
        Dictionary<BitcoinAddress, List<LikelyPayout>> payouts)
    {
        var payer = payers.Count == 1 ? payers.First() : null;
        foreach (var output in transaction.PayingOutputs())
        {
            var owner = input.OwnerOf(output.Address!);
            if (payer is not null && owner != payer && !excluded.Contains(payer))
            {
                Add(payouts, payer, new LikelyPayout(transaction, output, owner));
            }

            if (!payers.Contains(owner) && !excluded.Contains(owner))
            {
                Add(deposits, owner, DepositOf(transaction, output));
            }
        }
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
    /// <param name="payouts">The outputs it paid others, in block order.</param>
    /// <param name="received">How many times each address received in the graph.</param>
    /// <returns>The likely mixer, or <see langword="null"/> when it does not behave like one.</returns>
    private static LikelyMixer? Reading(
        CheckInput input,
        BitcoinAddress owner,
        IReadOnlyList<MixerDeposit> deposits,
        IReadOnlyList<LikelyPayout> payouts,
        IReadOnlyDictionary<BitcoinAddress, int> received)
    {
        var senders = deposits.Select(deposit => SenderOf(input, deposit)).Distinct().Count();
        var recipients = payouts.Select(payout => payout.Recipient).Distinct().Count();
        if (senders < MinimumSenders || recipients < MinimumRecipients)
        {
            return null;
        }

        var index = new PayoutIndex(payouts);
        var depositors = deposits.Select(deposit => deposit.Depositors.Select(input.OwnerOf).ToHashSet()).ToList();
        var matched = Assign(deposits, depositors, index, 1.0);
        var count = matched.Count(deposit => deposit.Match is not null);
        if (count < MinimumMatched || count * 2 < deposits.Count)
        {
            return null;
        }

        var explaining = matched.Select(deposit => deposit.Match).OfType<LikelyMatch>()
            .SelectMany(match => match.Outputs)
            .ToList();
        var oneTime = explaining.Count(payout => received.GetValueOrDefault(payout.Address) == 1);
        var inWindows = InWindows(deposits, payouts);
        if (explaining.Count < inWindows * MinimumExplained || oneTime < explaining.Count * MinimumOneTime
            || IsProcessor(matched))
        {
            return null;
        }

        var baseline = BaselineFactors.Average(factor => Assign(deposits, depositors, index, factor)
            .Count(deposit => deposit.Match is not null));
        return count - baseline >= Math.Max(MinimumMatched, BaselineSigmas * Math.Sqrt(baseline))
            ? new LikelyMixer(owner, matched, senders, recipients, baseline)
            : null;
    }

    /// <summary>
    /// Counts the payout outputs that lie in some deposit's window (within <see cref="MixerOutputsCheck.Window"/>
    /// after it): only they can be explained by the deposits the graph holds.
    /// </summary>
    /// <param name="deposits">The deposits, in block order.</param>
    /// <param name="payouts">The payout outputs, in block order.</param>
    /// <returns>The count.</returns>
    private static int InWindows(IReadOnlyList<MixerDeposit> deposits, IReadOnlyList<LikelyPayout> payouts)
    {
        var times = deposits.Select(deposit => deposit.Block.Time).Order().ToArray();
        return payouts.Count(payout =>
        {
            var at = payout.Transaction.Block!.Time;
            var before = Array.BinarySearch(times, at);
            var latest = (before < 0 ? ~before : before + 1) - 1;
            return latest >= 0 && at - times[latest] <= MixerOutputsCheck.Window;
        });
    }

    /// <summary>
    /// Says whether the matches look like a payment processor's: nearly all (<see cref="ProcessorShare"/>) come out
    /// within <see cref="ProcessorDelay"/>, and their fees lie within <see cref="ProcessorFeeSpread"/> of each other.
    /// A mixer delays and varies its fee to break the link; a processor forwards at once at its fixed rate.
    /// </summary>
    /// <param name="matched">The deposits with their matches.</param>
    /// <returns><see langword="true"/> when they do.</returns>
    private static bool IsProcessor(IReadOnlyList<LikelyDeposit> matched)
    {
        var found = matched.Where(deposit => deposit.Match is not null).ToList();
        var fast = found.Count(deposit =>
            deposit.Match!.Outputs[^1].Transaction.Block!.Time - deposit.Deposit.Block.Time <= ProcessorDelay);
        var fees = found.Select(deposit => deposit.Fee).Order().ToList();
        var spread = Percentile(fees, 0.9) - Percentile(fees, 0.1);
        return fast >= found.Count * ProcessorShare && spread <= ProcessorFeeSpread;
    }

    /// <summary>Gives a percentile of sorted values, the nearest rank.</summary>
    /// <param name="sorted">The values, smallest first; not empty.</param>
    /// <param name="share">The percentile, from 0 to 1.</param>
    /// <returns>The value.</returns>
    public static double Percentile(IReadOnlyList<double> sorted, double share) =>
        sorted[(int)Math.Clamp(Math.Round(share * (sorted.Count - 1)), 0, sorted.Count - 1)];

    /// <summary>
    /// Assigns each deposit the payouts that may be its money, with each payout output used at most once: every
    /// possible match is weighed, and the best score is assigned first.
    /// </summary>
    /// <param name="deposits">The deposits, in block order.</param>
    /// <param name="depositors">Each deposit's depositors' owners.</param>
    /// <param name="index">The owner's payouts, by time and by amount.</param>
    /// <param name="factor">What the deposits' amounts are scaled by: 1 for the real reading, else a baseline.</param>
    /// <returns>Each deposit with its match, or none.</returns>
    private static List<LikelyDeposit> Assign(
        IReadOnlyList<MixerDeposit> deposits,
        IReadOnlyList<HashSet<BitcoinAddress>> depositors,
        PayoutIndex index,
        double factor)
    {
        var candidates = new List<(int Deposit, LikelyMatch Match)>();
        for (var at = 0; at < deposits.Count; at++)
        {
            candidates.AddRange(Matches(deposits[at], depositors[at], index, factor).Select(match => (at, match)));
        }

        var assigned = new LikelyMatch?[deposits.Count];
        var used = new HashSet<string>(StringComparer.Ordinal);
        var order = candidates.OrderByDescending(candidate => candidate.Match.Score)
            .ThenBy(candidate => candidate.Deposit)
            .ThenBy(candidate => candidate.Match.Key, StringComparer.Ordinal);
        foreach (var (deposit, match) in order)
        {
            if (assigned[deposit] is null && match.Outputs.All(output => !used.Contains(output.Key)))
            {
                assigned[deposit] = match;
                used.UnionWith(match.Outputs.Select(output => output.Key));
            }
        }

        return [.. deposits.Select((deposit, index) => new LikelyDeposit(deposit, assigned[index]))];
    }

    /// <summary>
    /// Says whether a payout may be a deposit's money, whatever its amount: within
    /// <see cref="MixerOutputsCheck.Window"/> after it, to someone who neither is the owner nor deposited, paid with
    /// other coins than the deposit's own.
    /// </summary>
    /// <param name="deposit">The deposit.</param>
    /// <param name="depositors">The depositors' owners.</param>
    /// <param name="payout">The payout.</param>
    /// <returns><see langword="true"/> when it may.</returns>
    private static bool MayBe(MixerDeposit deposit, IReadOnlySet<BitcoinAddress> depositors, LikelyPayout payout)
    {
        var after = payout.Transaction.Block!.Time - deposit.Block.Time;
        return after > TimeSpan.Zero && after <= MixerOutputsCheck.Window
            && payout.Transaction.Block!.Height > deposit.Block.Height && !depositors.Contains(payout.Recipient)
            && !SpendsDeposit(payout.Transaction, deposit);
    }

    /// <summary>
    /// Finds the matches a deposit may have among the owner's payouts (<see cref="MayBe"/>): one output found by its
    /// amount, or a pair of the <see cref="MostPartPool"/> soonest, summing to the (scaled)
    /// deposit minus <see cref="MixerOutputsCheck.UsualFeeLow"/> to <see cref="MixerOutputsCheck.UsualFeeHigh"/>.
    /// </summary>
    /// <param name="deposit">The deposit.</param>
    /// <param name="depositors">The depositors' owners.</param>
    /// <param name="index">The owner's payouts, by time and by amount.</param>
    /// <param name="factor">What the deposit's amount is scaled by.</param>
    /// <returns>The matches.</returns>
    private static IEnumerable<LikelyMatch> Matches(
        MixerDeposit deposit, IReadOnlySet<BitcoinAddress> depositors, PayoutIndex index, double factor)
    {
        var amount = deposit.Output.Amount.Value * factor;
        var lowest = (long)Math.Ceiling(amount * (1 - MixerOutputsCheck.UsualFeeHigh));
        var at = Array.BinarySearch(index.Amounts, lowest);
        for (var place = at < 0 ? ~at : at; place < index.Amounts.Length && IsFee(amount, index.Amounts[place]);
             place++)
        {
            if (MayBe(deposit, depositors, index.ByAmount[place]))
            {
                yield return Weigh(deposit, [index.ByAmount[place]], amount, 0);
            }
        }

        var pool = new List<LikelyPayout>(MostPartPool);
        var first = Array.BinarySearch(index.Times, deposit.Block.Time.AddTicks(1));
        for (var place = first < 0 ? ~first : first;
             place < index.Times.Length && pool.Count < MostPartPool
                && index.Times[place] - deposit.Block.Time <= MixerOutputsCheck.Window;
             place++)
        {
            var payout = index.Soonest[place];
            var paid = payout.Output.Amount.Value;
            if (paid >= amount * MixerOutputsCheck.MostFee && paid < amount && MayBe(deposit, depositors, payout))
            {
                pool.Add(payout);
            }
        }

        foreach (var parts in Combinations(pool, amount))
        {
            yield return Weigh(deposit, parts, amount, PartsPenalty);
        }
    }

    /// <summary>
    /// Gives every pair of outputs from different transactions whose sum is the amount minus a usual fee. Only pairs:
    /// with three parts, sums of other users' payouts hit the band by chance about as often as the real ones.
    /// </summary>
    /// <param name="pool">The outputs to combine, soonest first.</param>
    /// <param name="amount">The deposit's (scaled) amount.</param>
    /// <returns>The pairs.</returns>
    private static IEnumerable<IReadOnlyList<LikelyPayout>> Combinations(IReadOnlyList<LikelyPayout> pool, double amount)
    {
        for (var a = 0; a < pool.Count; a++)
        {
            for (var b = a + 1; b < pool.Count; b++)
            {
                if (pool[a].Transaction.Txid != pool[b].Transaction.Txid
                    && IsFee(amount, pool[a].Output.Amount.Value + pool[b].Output.Amount.Value))
                {
                    yield return [pool[a], pool[b]];
                }
            }
        }
    }

    /// <summary>Says whether what came out is the amount minus a usual mixer fee.</summary>
    /// <param name="amount">What went in.</param>
    /// <param name="paid">What came out.</param>
    /// <returns><see langword="true"/> when the fee is usual.</returns>
    private static bool IsFee(double amount, long paid)
    {
        var fee = (amount - paid) / amount;
        return fee >= MixerOutputsCheck.UsualFeeLow && fee <= MixerOutputsCheck.UsualFeeHigh;
    }

    /// <summary>Weighs a match as <c>mixer-outputs</c> weighs a candidate, from its last payout.</summary>
    /// <param name="deposit">The deposit.</param>
    /// <param name="outputs">The outputs, soonest first.</param>
    /// <param name="amount">The deposit's (scaled) amount.</param>
    /// <param name="penalty">What the score loses.</param>
    /// <returns>The match.</returns>
    private static LikelyMatch Weigh(
        MixerDeposit deposit, IReadOnlyList<LikelyPayout> outputs, double amount, double penalty)
    {
        var paid = outputs.Sum(output => output.Output.Amount.Value);
        var after = outputs[^1].Transaction.Block!.Time - deposit.Block.Time;
        var score = MixerOutputsCheck.ScoreOf(
            MixerOutputsCheck.FeeScore((amount - paid) / amount),
            MixerOutputsCheck.TimeScore(after),
            MixerOutputsCheck.TypeScore(deposit, outputs[0].Address));
        return new LikelyMatch(outputs, Math.Round(score - penalty, 2, MidpointRounding.AwayFromZero));
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
/// <param name="Deposits">Every deposit into it, in block order, each with the payouts assigned to it.</param>
/// <param name="Senders">How many independent senders paid into it.</param>
/// <param name="Recipients">How many different recipients it paid.</param>
/// <param name="Baseline">How many deposits the scaled baselines explained on average, by chance.</param>
internal sealed record LikelyMixer(
    BitcoinAddress Owner, IReadOnlyList<LikelyDeposit> Deposits, int Senders, int Recipients, double Baseline)
{
    /// <summary>Gets the deposits that come out again as the deposit minus a usual fee.</summary>
    public IEnumerable<LikelyDeposit> Matched => Deposits.Where(deposit => deposit.Match is not null);

    /// <summary>Gets the chain's key for the mixer, so that a chain can say whether a step goes into it again.</summary>
    public string Key => "likely:" + Owner.Value;
}

/// <summary>An owner's payouts, in block order and by amount, to find a deposit's quickly.</summary>
internal sealed class PayoutIndex
{
    /// <summary>Indexes the payouts.</summary>
    /// <param name="soonest">The payouts, in block order.</param>
    public PayoutIndex(IReadOnlyList<LikelyPayout> soonest)
    {
        Soonest = soonest;
        Times = [.. soonest.Select(payout => payout.Transaction.Block!.Time)];
        ByAmount = [.. soonest.OrderBy(payout => payout.Output.Amount.Value)];
        Amounts = [.. ByAmount.Select(payout => payout.Output.Amount.Value)];
    }

    /// <summary>Gets the payouts, in block order.</summary>
    public IReadOnlyList<LikelyPayout> Soonest { get; }

    /// <summary>Gets their block times, in the same order.</summary>
    public DateTimeOffset[] Times { get; }

    /// <summary>Gets the payouts, smallest first.</summary>
    public IReadOnlyList<LikelyPayout> ByAmount { get; }

    /// <summary>Gets their amounts, smallest first, to search.</summary>
    public long[] Amounts { get; }
}

/// <summary>A deposit into a likely mixer and the payouts assigned to it as its money.</summary>
/// <param name="Deposit">The deposit.</param>
/// <param name="Match">The payouts, or <see langword="null"/> when none came out.</param>
internal sealed record LikelyDeposit(MixerDeposit Deposit, LikelyMatch? Match)
{
    /// <summary>Gets the share of the deposit that did not come out in the match; 0 without one.</summary>
    public double Fee => Match is { } match
        ? (double)(Deposit.Output.Amount.Value - match.Amount.Value) / Deposit.Output.Amount.Value
        : 0;
}

/// <summary>One or more payouts of a likely mixer that together may be a deposit's money.</summary>
/// <param name="Outputs">The payout outputs, soonest first.</param>
/// <param name="Score">How likely, weighed as <c>mixer-outputs</c> weighs a candidate (less for parts).</param>
internal sealed record LikelyMatch(IReadOnlyList<LikelyPayout> Outputs, double Score)
{
    /// <summary>Gets a stable key: the outputs' coins.</summary>
    public string Key => string.Join(",", Outputs.Select(output => output.Key));

    /// <summary>Gets what the outputs hold together.</summary>
    public Satoshi Amount => new(Outputs.Sum(output => output.Output.Amount.Value));

    /// <summary>Gets whether the deposit came out in parts.</summary>
    public bool IsParts => Outputs.Count > 1;
}

/// <summary>An output a likely mixer paid to another owner.</summary>
/// <param name="Transaction">The payout.</param>
/// <param name="Output">Its output.</param>
/// <param name="Recipient">The paid owner.</param>
internal sealed record LikelyPayout(BitcoinTransaction Transaction, BitcoinOutput Output, BitcoinAddress Recipient)
{
    /// <summary>Gets the payout's coin, <c>txid:index</c>.</summary>
    public string Key => MixerDeposits.CoinKey(Transaction.Txid, Output.Index);

    /// <summary>Gets the payout's address; never null.</summary>
    public BitcoinAddress Address => Output.Address!;
}
