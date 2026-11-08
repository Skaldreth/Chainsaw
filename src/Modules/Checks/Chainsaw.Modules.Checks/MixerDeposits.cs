using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// A deposit into a mixer: the output that paid into one of the mixer's known addresses, the depositor's addresses
/// and their most common address type. The mixer analyses (<c>mixer-outputs</c>, <c>mixer-parts</c>,
/// <c>mixer-chain</c>) read deposits and payouts the same way, so that they never disagree about what a deposit is.
/// </summary>
/// <param name="Transaction">The confirmed transaction that paid into the mixer.</param>
/// <param name="Output">The output that paid into it.</param>
/// <param name="Depositors">The depositor's addresses: the inputs that are not the mixer's own.</param>
/// <param name="Type">The depositor's most common address type.</param>
internal sealed record MixerDeposit(
    BitcoinTransaction Transaction, BitcoinOutput Output, IReadOnlySet<BitcoinAddress> Depositors,
    BitcoinScriptType Type)
{
    /// <summary>Gets the deposit's coin as a finding's key, <c>txid:index</c>.</summary>
    public string Key => MixerDeposits.CoinKey(Transaction.Txid, Output.Index);

    /// <summary>Gets the block the deposit is in.</summary>
    public BlockReference Block => Transaction.Block!;
}

/// <summary>A payout that may be a deposit's money after the mixer: a later output and how long after it came.</summary>
/// <param name="Transaction">The later, confirmed transaction.</param>
/// <param name="Output">Its output.</param>
/// <param name="After">How long after the deposit it was paid; positive and within the window.</param>
internal sealed record MixerPayout(BitcoinTransaction Transaction, BitcoinOutput Output, TimeSpan After)
{
    /// <summary>Gets the payout's address; never null for a payout.</summary>
    public BitcoinAddress Address => Output.Address!;

    /// <summary>Gets the payout's coin as a candidate's key, <c>txid:index</c>.</summary>
    public string Key => MixerDeposits.CoinKey(Transaction.Txid, Output.Index);
}

/// <summary>Finds the deposits into the known mixers and the payouts after each one.</summary>
internal static class MixerDeposits
{
    /// <summary>Gives every known mixer address.</summary>
    /// <param name="input">The input.</param>
    /// <returns>The addresses; empty when no mixer is known.</returns>
    public static IReadOnlySet<BitcoinAddress> AddressesOf(CheckInput input) =>
        input.Mixers.SelectMany(known => known.Addresses).ToHashSet();

    /// <summary>Writes a coin as a key, <c>txid:index</c>.</summary>
    /// <param name="txid">The transaction.</param>
    /// <param name="index">The output's index.</param>
    /// <returns>The key.</returns>
    public static string CoinKey(Txid txid, int index) =>
        txid.Value + ":" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Gives the deposits into the mixers, in block order and then by txid and output: every output to a mixer
    /// address in a confirmed transaction with at least one input that is not the mixer's own.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <param name="mixer">The mixers' known addresses.</param>
    /// <returns>The deposits.</returns>
    public static IEnumerable<MixerDeposit> Of(CheckInput input, IReadOnlySet<BitcoinAddress> mixer)
    {
        if (mixer.Count == 0)
        {
            yield break;
        }

        var deposits = input.Transactions
            .Where(transaction => transaction.Block is not null)
            .OrderBy(transaction => transaction.Block!.Height)
            .ThenBy(transaction => transaction.Txid.Value, StringComparer.Ordinal);
        foreach (var deposit in deposits)
        {
            var depositors = deposit.Inputs.Select(i => i.Address).OfType<BitcoinAddress>()
                .Where(address => !mixer.Contains(address)).ToHashSet();
            if (depositors.Count == 0)
            {
                continue;
            }

            var type = deposit.Inputs.Where(i => i.Address is not null && depositors.Contains(i.Address))
                .GroupBy(i => i.Address!.Type).OrderByDescending(group => group.Count()).First().Key;
            foreach (var output in deposit.Outputs.Where(o => o.Address is { } a && mixer.Contains(a)))
            {
                yield return new MixerDeposit(deposit, output, depositors, type);
            }
        }
    }

    /// <summary>
    /// Gives the payouts that may be a deposit's money: outputs of later confirmed transactions that the depositor
    /// did not pay, to an address that is neither the mixer's nor the depositor's, of at most the deposit, within
    /// <see cref="MixerOutputsCheck.Window"/> after it; in the input's order of transactions and outputs.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <param name="deposit">The deposit.</param>
    /// <param name="mixer">The mixers' known addresses.</param>
    /// <returns>The payouts; none for a deposit of nothing.</returns>
    public static IEnumerable<MixerPayout> PayoutsAfter(
        CheckInput input, MixerDeposit deposit, IReadOnlySet<BitcoinAddress> mixer)
    {
        if (deposit.Output.Amount.Value <= 0)
        {
            yield break;
        }

        foreach (var later in input.Transactions.Where(t => t.Block?.Height > deposit.Block.Height))
        {
            if (later.Inputs.Any(i => i.Address is not null && deposit.Depositors.Contains(i.Address)))
            {
                continue;
            }

            var after = later.Block!.Time - deposit.Block.Time;
            if (after <= TimeSpan.Zero || after > MixerOutputsCheck.Window)
            {
                continue;
            }

            foreach (var paid in later.Outputs)
            {
                if (paid.Address is { } address && !mixer.Contains(address) && !deposit.Depositors.Contains(address)
                    && paid.Amount.Value <= deposit.Output.Amount.Value)
                {
                    yield return new MixerPayout(later, paid, after);
                }
            }
        }
    }

    /// <summary>Gives the coins the user approved in an analysis's suggestion about a deposit.</summary>
    /// <param name="input">The input.</param>
    /// <param name="analysis">The analysis and version.</param>
    /// <param name="deposit">The deposit.</param>
    /// <returns>The approved coins' keys, <c>txid:index</c>.</returns>
    public static IReadOnlySet<string> ApprovedCoins(CheckInput input, HeuristicId analysis, MixerDeposit deposit) =>
        input.Approved.Where(a => a.Analysis == analysis && a.FindingKey == deposit.Key)
            .Select(a => CoinKey(a.Txid, a.Output))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Gives the candidates the user dismissed in an analysis's suggestion about a deposit.</summary>
    /// <param name="input">The input.</param>
    /// <param name="analysis">The analysis and version.</param>
    /// <param name="deposit">The deposit.</param>
    /// <returns>The dismissed candidates' keys (coins, for the mixer analyses).</returns>
    public static IReadOnlySet<string> DismissedCoins(CheckInput input, HeuristicId analysis, MixerDeposit deposit) =>
        input.Dismissed.Where(d => d.Analysis == analysis && d.FindingKey == deposit.Key && d.CandidateKey is not null)
            .Select(d => d.CandidateKey!)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Says whether the user dismissed an analysis's whole suggestion about a deposit.</summary>
    /// <param name="input">The input.</param>
    /// <param name="analysis">The analysis and version.</param>
    /// <param name="deposit">The deposit.</param>
    /// <returns><see langword="true"/> when the whole is dismissed.</returns>
    public static bool DismissedWhole(CheckInput input, HeuristicId analysis, MixerDeposit deposit) =>
        input.Dismissed.Any(d => d.Analysis == analysis && d.FindingKey == deposit.Key && d.CandidateKey is null);

    /// <summary>
    /// Gives the transaction in the input that spends a coin, or <see langword="null"/> when none in the graph does.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <param name="txid">The coin's transaction.</param>
    /// <param name="index">The coin's output.</param>
    /// <returns>The spending transaction.</returns>
    public static BitcoinTransaction? SpenderOf(CheckInput input, Txid txid, int index) =>
        input.Transactions.FirstOrDefault(transaction => transaction.Inputs.Any(
            i => i.PreviousTxid == txid && i.PreviousIndex == index));
}
