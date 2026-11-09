using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>
/// Says which node a conclusion held back concerns (sketch HB1a): one with an address it merged, or, for a
/// transaction with only findings (for example a CoinJoin that ends the trail), one with a line through it.
/// </summary>
public static class HeldBackMatch
{
    /// <summary>What the transaction whose conclusions are held back is called among what they were built on.</summary>
    public const string ItselfText = "tx:en själv";

    /// <summary>Says whether a held back conclusion, or a decision about one, concerns a node.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="node">The node.</param>
    /// <param name="addresses">The addresses the conclusion merged.</param>
    /// <param name="txid">The transaction whose conclusions are held back.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public static bool Touches(
        GraphScene scene, NodeView node, IReadOnlyList<BitcoinAddress> addresses, Txid txid)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(txid);
        if (addresses.Count > 0)
        {
            return addresses.Any(node.Node.GraphAddresses.Contains);
        }

        var transaction = ChainTransactionRef.Of(txid);
        return scene.Edges.Any(edge => (edge.Edge.From == node.Key || edge.Edge.To == node.Key)
            && edge.Edge.Transfers.Any(transfer => transfer.Transaction == transaction));
    }

    /// <summary>
    /// Writes what a held back conclusion was built on, shortened: the transaction itself is named as such, since a
    /// gone transaction's own conclusions rest on it (the expansions leave a gone transaction out of the clustering).
    /// </summary>
    /// <param name="txid">The transaction whose conclusions are held back.</param>
    /// <param name="missing">The transactions that were missing.</param>
    /// <returns>The words, for example <c>tx:en själv (ab12…cd34), ef56…7890</c>.</returns>
    public static string Basis(Txid txid, IReadOnlyList<Txid> missing)
    {
        ArgumentNullException.ThrowIfNull(txid);
        ArgumentNullException.ThrowIfNull(missing);
        return string.Join(", ", missing.Select(each => each == txid
            ? $"{ItselfText} ({GraphScene.Shorten(each.Value)})"
            : GraphScene.Shorten(each.Value)));
    }
}
