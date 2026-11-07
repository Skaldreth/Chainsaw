namespace Chainsaw.SharedKernel;

/// <summary>
/// How a chain keeps its money: the two kinds of chain Chainsaw has a contract for. What differs between them (coins
/// or balances, and the tracing methods and heuristics that follow from that) sits behind one contract per model, so
/// that a new chain of a known model is mostly a new source (principle 8).
/// </summary>
public enum LedgerModel
{
    /// <summary>
    /// Unspent outputs (UTXO), as Bitcoin, Litecoin and Zcash's transparent part: money is coins that are spent
    /// whole, and a transaction's inputs are earlier coins and its outputs new ones.
    /// </summary>
    Utxo,

    /// <summary>
    /// Accounts, as Ethereum, TRON and Solana: every address has a balance per asset, and a transaction moves an
    /// amount from one balance to another. Money that comes in mixes with what is already there.
    /// </summary>
    Account,
}

/// <summary>What Chainsaw knows about each chain it names: its model and its native coin.</summary>
public static class ChainModels
{
    /// <summary>Gives the model a chain keeps its money in.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>The model.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The chain is not one Chainsaw knows.</exception>
    public static LedgerModel Model(this Chain chain) => chain switch
    {
        Chain.Bitcoin => LedgerModel.Utxo,
        Chain.Ethereum or Chain.Tron => LedgerModel.Account,
        _ => throw new ArgumentOutOfRangeException(nameof(chain), chain, "Unknown chain."),
    };

    /// <summary>Gives a chain's native coin: the one its fees are paid in.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>The coin: BTC with 8 decimals, ETH with 18 or TRX with 6.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The chain is not one Chainsaw knows.</exception>
    public static Asset NativeAsset(this Chain chain) => chain switch
    {
        Chain.Bitcoin => Asset.Bitcoin,
        Chain.Ethereum => Asset.Ether,
        Chain.Tron => Asset.Tronix,
        _ => throw new ArgumentOutOfRangeException(nameof(chain), chain, "Unknown chain."),
    };
}
