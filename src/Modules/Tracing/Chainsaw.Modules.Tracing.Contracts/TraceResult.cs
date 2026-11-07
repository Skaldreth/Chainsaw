using System.Globalization;
using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// A traced amount: how much of the traced money Chainsaw concludes is here, by which method and version, and
/// through which transactions it was counted. It is a conclusion, never a fact (principle 2): the chain does not say
/// whose money went where, and another method gives another amount.
/// </summary>
/// <param name="Amount">The amount the method gives, in the traced asset.</param>
/// <param name="Method">The method and its version.</param>
/// <param name="Through">The transactions the amount was counted through, sorted by txid.</param>
public sealed record TracedAmount(AssetAmount Amount, TracingMethodId Method, IReadOnlyList<Txid> Through)
{
    /// <summary>
    /// Explains the amount in plain Swedish, so that the user can ask <em>why?</em> (principle 3).
    /// </summary>
    /// <returns>For example <c>Spårat belopp 0.30000000 BTC enligt FIFO v1, räknat genom 2 tx: …</c>.</returns>
    public string Explain()
    {
        var text = new StringBuilder();
        text.Append(
            CultureInfo.InvariantCulture,
            $"Spårat belopp {Amount.ToDecimalString()} {Amount.Asset.Symbol} enligt {Method}");
        text.Append(Through.Count == 0
            ? ", direkt ur startpunkten."
            : string.Create(
                CultureInfo.InvariantCulture,
                $", räknat genom {Through.Count} tx: {string.Join(", ", Through.Select(txid => txid.Value))}."));
        text.Append(" Det är en bedömning enligt metoden, inte ett faktum på kedjan.");
        return text.ToString();
    }
}

/// <summary>
/// Where traced money stops, and why. Every traced unit ends in exactly one end, so the ends add up to what was
/// traced: nothing is created and nothing disappears.
/// </summary>
/// <param name="Reason">Why it stops.</param>
/// <param name="Amount">How much stops here.</param>
/// <param name="Coin">The coin it stops in; <see langword="null"/> for a fee.</param>
/// <param name="Transaction">
/// The transaction it stops at: the one that pays the fee, the CoinJoin it is spent into, or the one not yet in the
/// graph that spends it; otherwise <see langword="null"/>.
/// </param>
/// <param name="NodeKey">The node it stops at, when it is one in the graph.</param>
/// <param name="Explanation">Why, in plain Swedish.</param>
/// <param name="Basis">The hints the end rests on; empty when the trace itself decided.</param>
/// <param name="Continuations">The user's links to another chain from this coin; empty otherwise.</param>
/// <param name="MayBeOutdated">
/// Whether what the end rests on may have changed since the case asked, here or on the way to it.
/// </param>
public sealed record TraceEnd(
    TrailEndReason Reason,
    TracedAmount Amount,
    Outpoint? Coin,
    Txid? Transaction,
    string? NodeKey,
    string Explanation,
    IReadOnlyList<TrailEndHint> Basis,
    IReadOnlyList<CrossChainLink> Continuations,
    bool MayBeOutdated);

/// <summary>A coin that holds traced money.</summary>
/// <param name="Coin">The coin.</param>
/// <param name="Address">Its address, when it has one.</param>
/// <param name="NodeKey">The node it belongs to.</param>
/// <param name="Taint">Which of its units are traced.</param>
/// <param name="Amount">How much, as a conclusion.</param>
/// <param name="MayBeOutdated">
/// Whether a step on the way to it may change (an unconfirmed transaction, or an answer the case could not renew).
/// </param>
public sealed record TracedCoin(
    Outpoint Coin, BitcoinAddress? Address, string NodeKey, TaintSet Taint, TracedAmount Amount, bool MayBeOutdated);

/// <summary>
/// How the traced money went through one transaction. The amounts are conclusions by the result's
/// <see cref="TraceResult.Method"/>, which is always shown with them.
/// </summary>
/// <param name="Txid">The transaction.</param>
/// <param name="In">The traced amount that went in.</param>
/// <param name="Outputs">The traced amount of each output, in order (zero where none).</param>
/// <param name="Fee">The traced amount that went to the fee.</param>
/// <param name="MayBeOutdated">Whether the transaction, or a step on the way to it, may change.</param>
public sealed record TracedTransaction(
    Txid Txid, AssetAmount In, IReadOnlyList<AssetAmount> Outputs, AssetAmount Fee, bool MayBeOutdated);

/// <summary>The traced money that moved along one edge of the graph.</summary>
/// <param name="EdgeKey">The edge.</param>
/// <param name="Amount">How much, as a conclusion.</param>
/// <param name="MayBeOutdated">Whether a step the amount rests on may change.</param>
public sealed record TracedEdge(string EdgeKey, TracedAmount Amount, bool MayBeOutdated);

/// <summary>
/// The result of a trace: where the traced money went by one method, and where it stops. Everything in it is a
/// conclusion that names its method (principle 2).
/// </summary>
/// <param name="Method">The method and its version.</param>
/// <param name="Seeds">The coins the money starts in.</param>
/// <param name="Traced">How much was traced from the seeds (what the user chose, not a conclusion).</param>
/// <param name="Coins">The coins that hold traced money, in the order they were reached.</param>
/// <param name="Transactions">The transactions the money went through, in the order they were counted.</param>
/// <param name="Edges">The traced money per edge of the graph; only edges with some.</param>
/// <param name="Ends">Where the money stops; they add up to <see cref="Traced"/>.</param>
public sealed record TraceResult(
    TracingMethodId Method,
    IReadOnlyList<TraceSeed> Seeds,
    AssetAmount Traced,
    IReadOnlyList<TracedCoin> Coins,
    IReadOnlyList<TracedTransaction> Transactions,
    IReadOnlyList<TracedEdge> Edges,
    IReadOnlyList<TraceEnd> Ends)
{
    /// <summary>Gives how much stops for one reason.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>The sum.</returns>
    public AssetAmount EndedBy(TrailEndReason reason) => Ends.Where(end => end.Reason == reason)
        .Aggregate(AssetAmount.Zero(Traced.Asset), (sum, end) => sum + end.Amount.Amount);
}
