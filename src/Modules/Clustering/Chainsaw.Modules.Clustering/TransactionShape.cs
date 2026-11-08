using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering;

/// <summary>Readings of a transaction's shape that several analyses share, so each rule is written once.</summary>
internal static class TransactionShape
{
    /// <summary>A round amount is a whole multiple of this many satoshi (0.001 BTC).</summary>
    public const long RoundUnit = 100_000;

    /// <summary>Says whether an amount is round: a whole multiple of <see cref="RoundUnit"/>.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsRound(Satoshi amount) => amount.Value % RoundUnit == 0;

    /// <summary>
    /// Gives the outputs that pay an address: the ones money can be followed through. OP_RETURN, bare multisig and
    /// non-standard outputs have no address and are left out.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The outputs with an address, in index order.</returns>
    public static IReadOnlyList<BitcoinOutput> PayingOutputs(this BitcoinTransaction transaction) =>
        [.. transaction.Outputs.Where(output => output.Address is not null).OrderBy(output => output.Index)];

    /// <summary>Gives the inputs' addresses, each once, in input order.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The addresses; inputs without an address (coinbase, non-standard) are left out.</returns>
    public static IReadOnlyList<BitcoinAddress> InputAddresses(this BitcoinTransaction transaction) =>
        [.. transaction.Inputs.OrderBy(input => input.Index).Select(input => input.Address)
            .OfType<BitcoinAddress>().Distinct()];

    /// <summary>Gives the smallest input amount, when every input's amount is known.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>
    /// The smallest amount, or <see langword="null"/> when an amount is missing or there are no inputs.
    /// </returns>
    public static Satoshi? SmallestInput(this BitcoinTransaction transaction) =>
        transaction.Inputs.Count == 0 || transaction.Inputs.Any(input => input.Amount is null)
            ? null
            : transaction.Inputs.Min(input => input.Amount!.Value);

    /// <summary>Gives the one script type every input has, when they all have the same.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The type, or <see langword="null"/> when they differ or one is unknown.</returns>
    public static BitcoinScriptType? CommonInputType(this BitcoinTransaction transaction)
    {
        var types = transaction.Inputs.Select(input => input.ScriptType).Distinct().ToList();
        return types.Count == 1 ? types[0] : null;
    }

    /// <summary>Gives the distinct transactions the inputs spend from.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>How many different earlier transactions fund it.</returns>
    public static int FundingTransactions(this BitcoinTransaction transaction) =>
        transaction.Inputs.Select(input => input.PreviousTxid).OfType<Txid>().Distinct().Count();

    /// <summary>
    /// Reads the wallet's fingerprint in a transaction: the version, the kind of locktime and whether any input
    /// signals that the transaction may be replaced (RBF). Wallets differ in these, so two transactions with
    /// different fingerprints were probably made by different wallets; the same fingerprint says much less.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The fingerprint.</returns>
    public static WalletFingerprint Fingerprint(this BitcoinTransaction transaction) => new(
        transaction.Version,
        LockTimeKindOf(transaction.LockTime),
        transaction.Inputs.Any(input => input.Sequence < WalletFingerprint.FinalMinusOne));

    /// <summary>Tells what kind of locktime a transaction has.</summary>
    /// <param name="lockTime">The locktime.</param>
    /// <returns>None, a block height or a time.</returns>
    private static LockTimeKind LockTimeKindOf(uint lockTime) => lockTime switch
    {
        0 => LockTimeKind.None,
        < WalletFingerprint.LockTimeThreshold => LockTimeKind.Height,
        _ => LockTimeKind.Time,
    };
}

/// <summary>A wallet's fingerprint in a transaction (<see cref="TransactionShape.Fingerprint"/>).</summary>
/// <param name="Version">The transaction's version.</param>
/// <param name="LockTime">The kind of locktime.</param>
/// <param name="SignalsReplace">Whether an input signals that the transaction may be replaced (RBF, BIP 125).</param>
internal readonly record struct WalletFingerprint(long Version, LockTimeKind LockTime, bool SignalsReplace)
{
    /// <summary>
    /// The highest sequence that signals RBF is one below this (<c>0xFFFFFFFE</c>, BIP 125); a lower one signals it.
    /// </summary>
    public const uint FinalMinusOne = 0xFFFFFFFE;

    /// <summary>Below this a locktime is a block height, from it a Unix time (the consensus rule).</summary>
    public const uint LockTimeThreshold = 500_000_000;

    /// <summary>Describes the fingerprint in plain Swedish.</summary>
    /// <returns>For example <c>version 2, låst till en blockhöjd, med RBF</c>.</returns>
    public string Describe()
    {
        var lockTime = LockTime switch
        {
            LockTimeKind.None => "utan låsning",
            LockTimeKind.Height => "låst till en blockhöjd",
            _ => "låst till en tid",
        };
        return $"version {Version}, {lockTime}, {(SignalsReplace ? "med RBF" : "utan RBF")}";
    }
}

/// <summary>The kinds of locktime a wallet uses.</summary>
internal enum LockTimeKind
{
    /// <summary>No locktime (0).</summary>
    None,

    /// <summary>A block height, often the current one (anti fee sniping).</summary>
    Height,

    /// <summary>A Unix time.</summary>
    Time,
}
