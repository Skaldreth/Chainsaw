using System.Globalization;
using System.Numerics;
using System.Text;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>Which way exposure is read from the subject (sketch EP1b).</summary>
public enum ExposureDirection
{
    /// <summary>What the subject paid out, and which services it reached: drawn to the right.</summary>
    Out,

    /// <summary>What the subject got, and which services it came from: drawn to the left.</summary>
    In,
}

/// <summary>Where a part of the subject's money ends that did not reach a service (the grey <em>Okänt</em>).</summary>
public enum ExposureRest
{
    /// <summary>
    /// It still lies with an actor on the way whose history the graph holds in full, by the method (only when read
    /// out).
    /// </summary>
    Remains,

    /// <summary>
    /// It is with an actor on the way whose history the graph does not hold in full: not expanded, or with transactions
    /// an expansion held back. Expanding it may tell where the money went.
    /// </summary>
    NotExpanded,

    /// <summary>It went to the miner as a fee (only when read out on a UTXO chain).</summary>
    Fees,

    /// <summary>
    /// It was still on its way when the analysis stopped following a circle: money that went round more times than an
    /// actor is read.
    /// </summary>
    StillMoving,

    /// <summary>
    /// It went on to an actor the analysis does not follow: one beyond the chosen depth, or one that is not an actor
    /// (a pile, what an expansion held back, a coin without an address).
    /// </summary>
    NotReached,

    /// <summary>It came back to the subject on the way.</summary>
    Returned,

    /// <summary>
    /// It came in to an actor on the way before the history the graph holds (only when read in): the graph does not
    /// say where it came from.
    /// </summary>
    BeforeHistory,

    /// <summary>
    /// The trail ends at the transaction it went through (or came from), for example a CoinJoin that is not counted as
    /// a service: the graph's trace stops there, and so does the analysis.
    /// </summary>
    TrailEnds,

    /// <summary>
    /// What the source says about where it went does not agree with the transaction it names, so it is not followed
    /// rather than guessed, as in the graph's trace. Expanding again, or another source, may settle it.
    /// </summary>
    Conflicting,

    /// <summary>
    /// It continues on another chain by the user's link: paid out, a coin linked there whose other chain is not in the
    /// graph; got, the share of a transaction's outputs paid by an input linked there, which by the graph's trace went
    /// on to the other chain and is no source of them.
    /// </summary>
    OtherChain,
}

/// <summary>What to read the exposure of, and how.</summary>
/// <param name="Graph">The graph: its actors, and the transfers between them as facts from the chain.</param>
/// <param name="Subject">The node whose exposure is read, a cluster or an address.</param>
/// <param name="Asset">
/// The subject's asset, whose transfers are the money; amounts in different assets are never added. Money that goes
/// through a swap on an account chain is followed into the other asset and counted by its value in this one
/// (<see cref="SwapValue"/>).
/// </param>
/// <param name="Direction">Paid out or got.</param>
/// <param name="Depth">How many hops are followed, from 1 (only direct) to <see cref="MaxDepth"/>.</param>
/// <param name="Method">The tracing method that says how much of an actor's money goes on with each transfer.</param>
/// <param name="Services">
/// The nodes where money stops and is counted: the actors that a label or a sort makes a service, a victim or
/// another chain. Which they are is the caller's attribution (a judgement), not the analysis's.
/// </param>
public sealed record ExposureRequest(
    TraceGraph Graph,
    string Subject,
    Asset Asset,
    ExposureDirection Direction,
    int Depth,
    TracingMethodKind Method,
    IReadOnlySet<string> Services)
{
    /// <summary>
    /// Gets the case's rates for the swaps' price check, as in the trace (<see cref="SwapValue.CheckPrice"/>): a swap
    /// with an absurd price is no swap, and the money goes on to the pool. <see langword="null"/> tests no price.
    /// </summary>
    public SwapPricing? Pricing { get; init; }

    /// <summary>The deepest the analysis follows: the rings of the view (sketch EP1b).</summary>
    public const int MaxDepth = 3;

    /// <summary>Gets the depth, from 1 to <see cref="MaxDepth"/>.</summary>
    public int Depth { get; } = Depth is >= 1 and <= MaxDepth
        ? Depth
        : throw new ArgumentOutOfRangeException(nameof(Depth), "The depth is 1, 2 or 3.");
}

/// <summary>
/// A service the subject's money reached, or came from: how much by the method, in how many hops, and the way there.
/// </summary>
/// <param name="NodeKey">The service's node.</param>
/// <param name="Amount">How much of the subject's money, as a conclusion by the method.</param>
/// <param name="Hops">The fewest hops on which the money reached it.</param>
/// <param name="Path">The way, from the subject to the service, as node keys.</param>
/// <param name="PathEdges">The graph's lines along the way, in the same order.</param>
/// <param name="Transactions">The transactions on the way that carried the money, in the way's order.</param>
public sealed record ExposureReach(
    string NodeKey,
    AssetAmount Amount,
    int Hops,
    IReadOnlyList<string> Path,
    IReadOnlyList<string> PathEdges,
    IReadOnlyList<ChainTransactionRef> Transactions)
{
    /// <summary>
    /// Gets the swaps on the way, where the money's value followed into another asset by <see cref="SwapValue"/>;
    /// empty when the way went through none.
    /// </summary>
    public IReadOnlyList<ExposureSwap> Swaps { get; init; } = [];
}

/// <summary>A swap on the way to a service, with how much of what went into it was the subject's money.</summary>
/// <param name="Swap">The swap.</param>
/// <param name="Share">
/// The subject's share of what went in (read out) or came out (read in), for example <c>62,2 %</c>, as a conclusion
/// by the method; the same share of the other side follows the value.
/// </param>
public sealed record ExposureSwap(TokenSwap Swap, string Share)
{
    /// <summary>Writes the swap's step, for example <c>0x… (62,2 %)</c>.</summary>
    /// <returns>The step.</returns>
    public override string ToString() => $"{Swap.Transaction.Value} ({Share})";
}

/// <summary>How much of the subject's money moved along one of the graph's lines, by the method.</summary>
/// <param name="EdgeKey">The line.</param>
/// <param name="Near">Its end nearer the subject.</param>
/// <param name="Far">Its end farther from the subject.</param>
/// <param name="Amount">How much, as a conclusion.</param>
public sealed record ExposureFlow(string EdgeKey, string Near, string Far, AssetAmount Amount);

/// <summary>
/// The exposure of a subject (sketch EP1b): which services its money reached or came from, directly and over hops,
/// and what reached none. Every amount is a conclusion by the analysis and the tracing method, never a fact
/// (principle 2): the chain says what moved, not whose money went where.
/// </summary>
/// <param name="Subject">The subject's node.</param>
/// <param name="Direction">Paid out or got.</param>
/// <param name="Depth">How many hops were followed.</param>
/// <param name="Method">The tracing method and its version.</param>
/// <param name="Total">All the subject paid out or got in the asset, along the graph's lines (fact).</param>
/// <param name="Reached">The services, the largest share first.</param>
/// <param name="Rest">What reached no service, per reason; the reasons that have none are left out.</param>
/// <param name="Flows">The money per line, for the picture.</param>
/// <param name="Hops">How many hops from the subject each node the money passed lies.</param>
public sealed record ExposureResult(
    string Subject,
    ExposureDirection Direction,
    int Depth,
    TracingMethodId Method,
    AssetAmount Total,
    IReadOnlyList<ExposureReach> Reached,
    IReadOnlyDictionary<ExposureRest, AssetAmount> Rest,
    IReadOnlyList<ExposureFlow> Flows,
    IReadOnlyDictionary<string, int> Hops)
{
    /// <summary>The analysis's name, as the view and <em>Varför?</em> write it.</summary>
    public const string AnalysisName = "exposure";

    /// <summary>
    /// The analysis's version; raised whenever it may give other amounts. Version 2 stops money where the graph's trace
    /// stops it: at a link to another chain, a transaction where the trail ends, and a spend that does not agree;
    /// version 3 follows the value through a swap on an account chain by <see cref="SwapValue"/>, as the trace does;
    /// version 5 leaves an account chain's transaction the source no longer knows out of every ledger, and stops the
    /// money at one the answers do not settle, as the trace does (sketch GR9a, <see cref="GoneAccountTransaction"/>).
    /// </summary>
    public const int AnalysisVersion = 5;

    /// <summary>Gets the analysis's name and version, for example <c>exposure v5</c>.</summary>
    public static string Analysis { get; } =
        AnalysisName + " v" + AnalysisVersion.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// What <em>Varför?</em> adds about a way through a swap: the exposure does not deduct the part that stays in the
    /// pool when the values in and out differ, which the graph's trace does.
    /// </summary>
    public const string PoolNotDeducted =
        " Poolens del dras inte av här när värdena in och ut skiljer (det gör spåret i grafen), så andelen kan vara "
        + "något för stor.";

    /// <summary>Gets what reached no service, all reasons together.</summary>
    public AssetAmount Unknown => Rest.Values.Aggregate(AssetAmount.Zero(Total.Asset), (sum, part) => sum + part);

    /// <summary>Gets whether the subject paid out or got anything in the asset.</summary>
    public bool HasMoney => Total.Units.Sign > 0;

    /// <summary>Gives an amount's share of the total, from 0 to 1.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>The share; 0 when the total is.</returns>
    public double ShareOf(AssetAmount amount) => Total.Units.IsZero ? 0 : Ratio(amount.Units, Total.Units);

    /// <summary>
    /// Explains a service's share in plain Swedish, so that the user can ask <em>why?</em> (principle 3): the
    /// analysis and the method with their versions, the way and its transactions, and that it is a judgement.
    /// </summary>
    /// <param name="reach">The service.</param>
    /// <returns>The explanation.</returns>
    public string Explain(ExposureReach reach)
    {
        ArgumentNullException.ThrowIfNull(reach);
        var text = new StringBuilder();
        var verb = Direction == ExposureDirection.Out ? "nådde tjänsten" : "kom från tjänsten";
        text.Append(CultureInfo.InvariantCulture,
            $"{reach.Amount.ToDecimalString()} {reach.Amount.Asset.Symbol} av {Total.ToDecimalString()} ");
        text.Append(CultureInfo.InvariantCulture, $"{verb} på {reach.Hops} hopp, räknat med {Analysis} och {Method}");
        text.Append(reach.Transactions.Count == 0
            ? "."
            : string.Create(CultureInfo.InvariantCulture,
                $", genom {reach.Transactions.Count} tx: {string.Join(", ", reach.Transactions.Select(t => t.Id))}."));
        if (reach.Swaps.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture,
                $" Vägen går genom {reach.Swaps.Count} swap(par), i tx {string.Join(", ", reach.Swaps)} med "
                + $"ämnets andel av det som {SwapSides().Known} inom parentes; där följer värdet med enligt "
                + $"{SwapValue.Analysis}: samma andel av det som {SwapSides().Other}, räknat i {Total.Asset.Symbol}.");
            text.Append(PoolNotDeducted);
        }

        text.Append(reach.Amount.Asset.Chain.Model() == LedgerModel.Utxo
            ? " Metoden följer varje mynt genom tx:erna, som i grafen."
            : " Varje aktör räknas som en plånbok där metoden säger hur mycket som följer med varje överföring.");
        text.Append(" Det är en bedömning, inte ett faktum på kedjan.");
        return text.ToString();
    }

    /// <summary>
    /// Gives a swap's sides as the explanation names them: paid out, the subject's share is of what went in and the
    /// same share of what came out follows; got, the other way round.
    /// </summary>
    /// <returns>The side whose share is known, and the side that follows it, in Swedish.</returns>
    private (string Known, string Other) SwapSides() => Direction == ExposureDirection.Out
        ? ("gick in", "kom ut")
        : ("kom ut", "gick in");

    /// <summary>Divides two whole numbers as a share, exactly enough for a percentage.</summary>
    /// <param name="part">The part.</param>
    /// <param name="whole">The whole; not zero.</param>
    /// <returns>The share.</returns>
    private static double Ratio(BigInteger part, BigInteger whole)
    {
        const int Precision = 1_000_000;
        return (double)(part * Precision / whole) / Precision;
    }
}

/// <summary>
/// The analysis <em>exposure</em> (sketch EP1b), a step with a name and a version: given the graph, a subject and a
/// tracing method, it says which services the subject's money reached (paid out) or came from (got), directly and over
/// hops, and what reached none. On a UTXO chain the money is followed coin by coin through the transactions with the
/// method registered for the chain, as the graph traces; on an account chain every actor is read as a wallet whose
/// transfers of one asset stand in time order, and the method says how much of what came in goes on with each.
/// </summary>
public interface IExposureAnalysis
{
    /// <summary>Reads a subject's exposure.</summary>
    /// <param name="request">What to read, and how.</param>
    /// <returns>The exposure; every amount in it is a conclusion.</returns>
    /// <exception cref="InvalidOperationException">No method of the kind is registered.</exception>
    ExposureResult Analyse(ExposureRequest request);
}
