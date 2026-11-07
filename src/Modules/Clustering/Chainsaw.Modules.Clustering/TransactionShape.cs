using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering;

/// <summary>Small readings of a transaction's shape that several analyses share, so each rule is written once.</summary>
internal static class TransactionShape
{
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
}
