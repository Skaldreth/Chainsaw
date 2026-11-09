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
        Chain.Bitcoin or Chain.Litecoin => LedgerModel.Utxo,
        Chain.Ethereum or Chain.Tron or Chain.Bsc or Chain.Solana => LedgerModel.Account,
        _ => throw new ArgumentOutOfRangeException(nameof(chain), chain, "Unknown chain."),
    };

    /// <summary>Gives a chain's native coin: the one its fees are paid in.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>
    /// The coin: BTC with 8 decimals, ETH with 18, TRX with 6, LTC with 8, BNB with 18 or SOL with 9.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">The chain is not one Chainsaw knows.</exception>
    public static Asset NativeAsset(this Chain chain) => chain switch
    {
        Chain.Bitcoin => Asset.Bitcoin,
        Chain.Ethereum => Asset.Ether,
        Chain.Tron => Asset.Tronix,
        Chain.Litecoin => Asset.Litecoin,
        Chain.Bsc => Asset.Bnb,
        Chain.Solana => Asset.Sol,
        _ => throw new ArgumentOutOfRangeException(nameof(chain), chain, "Unknown chain."),
    };

    /// <summary>
    /// Says whether a chain runs Ethereum's virtual machine with Ethereum's addresses: <c>0x</c> and 20 bytes in hex,
    /// compared in lower case (the mixed case is only a checksum), and transactions named by <c>0x</c> and 32 bytes.
    /// TRON runs the same machine but writes its addresses in Base58Check, so it is not one of them.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <returns><see langword="true"/> for Ethereum and BSC.</returns>
    public static bool IsEvm(this Chain chain) => chain is Chain.Ethereum or Chain.Bsc;

    /// <summary>
    /// Gives a chain's name as people write it, for text the user and a receiver read (a request, a report):
    /// <c>Bitcoin</c>, <c>Ethereum</c>, <c>TRON</c>, <c>Litecoin</c>, <c>BSC</c> or <c>Solana</c>.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <returns>The name.</returns>
    public static string DisplayName(this Chain chain) => chain switch
    {
        Chain.Tron => "TRON",
        Chain.Bsc => "BSC",
        _ => chain.ToString(),
    };
}
