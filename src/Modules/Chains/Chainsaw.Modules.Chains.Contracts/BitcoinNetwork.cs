namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// The Bitcoin network an address or a source belongs to. Addresses carry their network (the version byte or the
/// prefix), so an address from the test network is never mistaken for one on the real chain.
/// </summary>
public enum BitcoinNetwork
{
    /// <summary>The real Bitcoin chain: addresses start with <c>1</c>, <c>3</c> or <c>bc1</c>.</summary>
    Mainnet,

    /// <summary>
    /// The test networks (testnet and signet): addresses start with <c>m</c>, <c>n</c>, <c>2</c> or <c>tb1</c>.
    /// </summary>
    Testnet,
}
