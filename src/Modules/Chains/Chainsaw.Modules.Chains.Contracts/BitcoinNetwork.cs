namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// The network of the UTXO model an address or a source belongs to: Bitcoin's own networks, and Litecoin, which keeps
/// Bitcoin's model and script with its own version bytes and prefix. Addresses carry their network (the version byte
/// or the prefix), so an address from the test network or from Litecoin is never mistaken for one on Bitcoin.
/// </summary>
public enum BitcoinNetwork
{
    /// <summary>The real Bitcoin chain: addresses start with <c>1</c>, <c>3</c> or <c>bc1</c>.</summary>
    Mainnet,

    /// <summary>
    /// The test networks (testnet and signet): addresses start with <c>m</c>, <c>n</c>, <c>2</c> or <c>tb1</c>.
    /// </summary>
    Testnet,

    /// <summary>
    /// Litecoin's main network, a chain of its own (<see cref="SharedKernel.Chain.Litecoin"/>): addresses start with
    /// <c>L</c>, <c>M</c> (and the older <c>3</c>, read in Litecoin's own form) or <c>ltc1</c>.
    /// </summary>
    Litecoin,

    /// <summary>
    /// Zcash's main network, its transparent part (<see cref="SharedKernel.Chain.Zcash"/>): addresses start with
    /// <c>t1</c> (P2PKH) or <c>t3</c> (P2SH), with two version bytes. Its shielded pools have their own addresses,
    /// which are not addresses in this model.
    /// </summary>
    Zcash,
}

/// <summary>What the UTXO networks say about their chain.</summary>
public static class BitcoinNetworks
{
    /// <summary>Gives the chain a network is on.</summary>
    /// <param name="network">The network.</param>
    /// <returns>Litecoin or Zcash for their networks, otherwise Bitcoin.</returns>
    public static SharedKernel.Chain ChainOf(this BitcoinNetwork network) => network switch
    {
        BitcoinNetwork.Litecoin => SharedKernel.Chain.Litecoin,
        BitcoinNetwork.Zcash => SharedKernel.Chain.Zcash,
        _ => SharedKernel.Chain.Bitcoin,
    };

    /// <summary>Gives the main network of a chain of unspent outputs.</summary>
    /// <param name="chain">The chain: Bitcoin, Litecoin or Zcash.</param>
    /// <returns>The network.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The chain is not a UTXO chain Chainsaw knows.</exception>
    public static BitcoinNetwork MainnetOf(SharedKernel.Chain chain) => chain switch
    {
        SharedKernel.Chain.Bitcoin => BitcoinNetwork.Mainnet,
        SharedKernel.Chain.Litecoin => BitcoinNetwork.Litecoin,
        SharedKernel.Chain.Zcash => BitcoinNetwork.Zcash,
        _ => throw new ArgumentOutOfRangeException(nameof(chain), chain, "Not a chain of unspent outputs."),
    };
}
