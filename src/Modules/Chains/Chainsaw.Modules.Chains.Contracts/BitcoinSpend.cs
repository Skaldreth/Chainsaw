namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>The input that spends an output.</summary>
/// <param name="Txid">The spending transaction.</param>
/// <param name="InputIndex">The index of the spending input in that transaction.</param>
/// <param name="Block">The spending transaction's block; <see langword="null"/> while it is unconfirmed.</param>
public sealed record BitcoinSpend(Txid Txid, int InputIndex, BlockReference? Block);

/// <summary>Whether one output of a transaction has been spent, as the source reported it.</summary>
/// <param name="Index">The output's index.</param>
/// <param name="SpentBy">The input that spends it, or <see langword="null"/> when it is unspent.</param>
public sealed record BitcoinOutputSpend(int Index, BitcoinSpend? SpentBy)
{
    /// <summary>
    /// Gets whether the source could not say who spent the output (for example an address with more transactions than
    /// were read); <see cref="SpentBy"/> is then <see langword="null"/>, which does not mean unspent.
    /// </summary>
    public bool IsUnknown { get; init; }
}
