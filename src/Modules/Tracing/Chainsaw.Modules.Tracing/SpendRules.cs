using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// The rules that stop a coin's money on a UTXO chain before the transaction that spends it, written once so that the
/// graph's trace (<see cref="TraceCalculator"/>) and the analysis <em>exposure</em> (<see cref="UtxoExposureRun"/>)
/// always agree about where money stops:
/// <list type="bullet">
/// <item>The user has linked the coin to another chain: its money continues there, not in the spending
/// transaction.</item>
/// <item>What the source says about the spend does not agree with the spending transaction: the money is not followed
/// rather than guessed.</item>
/// <item>The trail ends at the spending transaction, for example a CoinJoin (<see cref="TraceGraph.TransactionEnds"/>).
/// </item>
/// </list>
/// </summary>
internal static class SpendRules
{
    /// <summary>Gives the user's links from a coin to another chain, in force; empty when it has none.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="coin">The coin.</param>
    /// <returns>The links.</returns>
    public static IReadOnlyList<CrossChainLink> Continuations(TraceGraph graph, Outpoint coin) =>
        [.. graph.CrossChainLinks.Where(link => link.From == coin)];

    /// <summary>
    /// Says whether what the graph says about a coin's spend disagrees with the transaction it names: the input is
    /// not there, spends another coin, or carries another amount.
    /// </summary>
    /// <param name="spender">The spending transaction.</param>
    /// <param name="inputIndex">The input the spend names.</param>
    /// <param name="coin">The coin.</param>
    /// <param name="output">The coin's output.</param>
    /// <returns><see langword="true"/> when they disagree.</returns>
    public static bool IsConflicting(BitcoinTransaction spender, int inputIndex, Outpoint coin, BitcoinOutput output)
    {
        ArgumentNullException.ThrowIfNull(spender);
        ArgumentNullException.ThrowIfNull(coin);
        ArgumentNullException.ThrowIfNull(output);
        var inputs = spender.Inputs;
        return inputIndex < 0 || inputIndex >= inputs.Count
            || inputs[inputIndex].PreviousTxid != coin.Txid
            || inputs[inputIndex].PreviousIndex != coin.Index
            || inputs[inputIndex].Amount != output.Amount;
    }

    /// <summary>Gives why the trail ends at a transaction, for example a CoinJoin; empty when it does not.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="txid">The transaction.</param>
    /// <returns>The reasons, the first the one shown.</returns>
    public static IReadOnlyList<TrailEndHint> EndsAt(TraceGraph graph, Txid txid) =>
        graph.TransactionEnds.GetValueOrDefault(txid) ?? [];
}
