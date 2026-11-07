using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>The graph's node keys, written in one place so that the builder, the trace and the places agree.</summary>
internal static class NodeKeys
{
    /// <summary>The prefix of a single address's node.</summary>
    public const string AddressPrefix = "address:";

    /// <summary>The prefix of a cluster's node.</summary>
    public const string ClusterPrefix = "cluster:";

    /// <summary>The key of a single address's node.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The key.</returns>
    public static string Address(BitcoinAddress address) => AddressPrefix + address.Value;

    /// <summary>The key of a cluster's node, named by its representative address.</summary>
    /// <param name="representative">The representative address.</param>
    /// <returns>The key.</returns>
    public static string Cluster(BitcoinAddress representative) => ClusterPrefix + representative.Value;

    /// <summary>The prefix of an address's node on an account chain.</summary>
    public const string AccountPrefix = "account:";

    /// <summary>The key of an address's node on an account chain: the chain and the address.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The key, for example <c>account:Ethereum:0x…</c>.</returns>
    public static string Account(ChainAddress address) => AccountPrefix + address.Chain + ":" + address.Value;

    /// <summary>The key of a transaction's node.</summary>
    /// <param name="txid">The transaction.</param>
    /// <returns>The key.</returns>
    public static string Transaction(Txid txid) => "tx:" + txid.Value;

    /// <summary>The key of a coin without an address.</summary>
    /// <param name="coin">The coin.</param>
    /// <returns>The key.</returns>
    public static string Coin(Outpoint coin) => "coin:" + coin;

    /// <summary>The key of a transaction on another chain.</summary>
    /// <param name="reference">The transaction.</param>
    /// <returns>The key.</returns>
    public static string OtherChain(ChainTransactionRef reference) => "chain:" + reference;

    /// <summary>
    /// The key a node's place is stored under. A cluster's place is stored under its representative address, and
    /// read back from any of its addresses, so that it is kept when the cluster grows or another address represents
    /// it.
    /// </summary>
    /// <param name="nodeKey">The node's key.</param>
    /// <returns>The position key.</returns>
    public static string Position(string nodeKey) => nodeKey.StartsWith(ClusterPrefix, StringComparison.Ordinal)
        ? AddressPrefix + nodeKey[ClusterPrefix.Length..]
        : nodeKey;
}
