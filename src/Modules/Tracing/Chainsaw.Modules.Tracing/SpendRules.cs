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
/// <item>The trail ends at the spending transaction, for example a CoinJoin (<see cref="TraceGraph.TransactionEnds"/>),
/// or one the source no longer knows (<see cref="TraceGraph.Gone"/>).</item>
/// <item>The spending transaction is gone and the user chose to follow its replacer: the money goes on through the
/// replacer instead (<see cref="Replaced"/>).</item>
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

    /// <summary>
    /// Gives the input that spends a coin in place of a gone transaction, when the user chose to follow that replacer
    /// (<see cref="ReplacementFollow"/>) and the replacer is in the graph; the money then goes on through it, as the
    /// user's judgement (principle 2).
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="coin">The coin.</param>
    /// <param name="spentBy">The input the graph says spends the coin.</param>
    /// <returns>The replacer's input, or <see langword="null"/> when the coin is not followed through one.</returns>
    public static BitcoinSpend? Replaced(TraceGraph graph, Outpoint coin, BitcoinSpend spentBy)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(coin);
        ArgumentNullException.ThrowIfNull(spentBy);
        if (!graph.Gone.TryGetValue(spentBy.Txid, out var gone)
            || gone.Replacement?.SpenderOf(coin.Txid, coin.Index) is not { } by)
        {
            return null;
        }

        // A replacer that is gone in turn is never followed, so that one step is all a replacement ever takes.
        var followed = graph.FollowedReplacements.Any(follow => follow.Gone == gone.Txid && follow.Replacer == by.Txid);
        return followed && graph.Transactions.ContainsKey(by.Txid) && !graph.Gone.ContainsKey(by.Txid) ? by : null;
    }
}
