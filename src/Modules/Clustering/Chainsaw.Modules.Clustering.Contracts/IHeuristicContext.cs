using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>
/// What the case knows around a transaction: the other transactions that have been fetched. An analysis may only use
/// what is here and in the transaction, so that the same data always gives the same conclusion.
/// </summary>
public interface IHeuristicContext
{
    /// <summary>Gets how many transactions the context knows of.</summary>
    int Count { get; }

    /// <summary>Finds a transaction the case knows of.</summary>
    /// <param name="txid">The transaction.</param>
    /// <returns>The transaction, or <see langword="null"/> when the case does not know it.</returns>
    BitcoinTransaction? Find(Txid txid);

    /// <summary>Finds the transaction the case knows of that spends an output.</summary>
    /// <param name="txid">The output's transaction.</param>
    /// <param name="outputIndex">The output's index.</param>
    /// <returns>The spending transaction, or <see langword="null"/> when the case knows of none.</returns>
    BitcoinTransaction? FindSpender(Txid txid, int outputIndex);

    /// <summary>
    /// Finds a transaction the case knows of that came before the given one and has the address (as input or
    /// output): confirmed in a lower block, or confirmed when the given one is not.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="transaction">The transaction to look before.</param>
    /// <returns>
    /// The earliest such transaction, or <see langword="null"/>; that means only that the case knows of none, the
    /// address may still have been used.
    /// </returns>
    BitcoinTransaction? SeenBefore(BitcoinAddress address, BitcoinTransaction transaction);
}
