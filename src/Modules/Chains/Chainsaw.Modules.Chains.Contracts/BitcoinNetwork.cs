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
}

/// <summary>What the UTXO networks say about their chain.</summary>
public static class BitcoinNetworks
{
    /// <summary>Gives the chain a network is on.</summary>
    /// <param name="network">The network.</param>
    /// <returns><see cref="SharedKernel.Chain.Litecoin"/> for Litecoin, otherwise Bitcoin.</returns>
    public static SharedKernel.Chain ChainOf(this BitcoinNetwork network) =>
        network == BitcoinNetwork.Litecoin ? SharedKernel.Chain.Litecoin : SharedKernel.Chain.Bitcoin;
}
