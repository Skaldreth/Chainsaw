using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// The trace as a model: the start points, what the user has expanded, the graph built from it, and the traced
/// amounts by a chosen method. The graph only grows when the user expands it (principle 7).
/// </summary>
/// <remarks>
/// <para>
/// Every method takes the open case; nothing is kept between calls. The graph's state (start points, expansions,
/// places) is ordinary data in the case that may change, since it is an aid and not a basis; the facts it shows are
/// the sealed responses from Chains, and the clusters are the sealed conclusions from Clustering. The user's links to
/// other chains are judgements and are sealed.
/// </para>
/// <para>
/// The asynchronous methods do their work off the calling thread (principle 6). Without an exit, the graph is built
/// from what the case holds and says honestly what is missing (principle 9).
/// </para>
/// </remarks>
public interface ITracingService
{
    /// <summary>Gets the registered tracing methods, the newest version of each kind.</summary>
    IReadOnlyList<TracingMethodId> Methods { get; }

    /// <summary>Builds the graph from the case: start points, expansions, clusters and the user's links.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The graph.</returns>
    Task<TraceGraph> LoadAsync(ICase @case, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a start point, with a line in the journal. The graph shows it at once; what goes on from it appears when
    /// it is expanded. Adding one that is already there does nothing.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="startPoint">The start point.</param>
    /// <returns><see langword="true"/> when it was added.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    bool AddStartPoint(ICase @case, TraceStartPoint startPoint);

    /// <summary>Removes a start point, with a line in the journal; nothing happens when it is not there.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="startPoint">The start point.</param>
    /// <returns><see langword="true"/> when it was removed.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    bool RemoveStartPoint(ICase @case, TraceStartPoint startPoint);

    /// <summary>
    /// Expands the graph by one step: an address's history, or a transaction and the transactions that spend its
    /// outputs. The step reads through <see cref="IBitcoinSource"/>, so every response is sealed in the case; the
    /// clustering analyses then run over the graph's transactions.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="request">What to expand.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>What the step brought in, and the graph after it.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> when the analyses have something new to store, or
    /// <see cref="CaseError.Closed"/>.
    /// </exception>
    Task<ExpansionResult> ExpandAsync(
        ICase @case, ExpansionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Links a Bitcoin coin to the transaction where the money continues on another chain: the user's judgement,
    /// sealed, with a line in the journal.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="from">The coin that went into the bridge or swap.</param>
    /// <param name="to">The transaction on the other chain; not Bitcoin.</param>
    /// <param name="reason">Why, at most 1 000 characters.</param>
    /// <returns>The link.</returns>
    /// <exception cref="ArgumentException">
    /// The target is on Bitcoin, the coin already has a link in force, or the reason is empty or too long.
    /// </exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    CrossChainLink LinkCrossChain(ICase @case, Outpoint from, ChainTransactionRef to, string reason);

    /// <summary>
    /// Takes back a link to another chain. The link stays sealed; a sealed record of the removal says it is no
    /// longer in force.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="link">The link.</param>
    /// <param name="reason">Why, at most 1 000 characters.</param>
    /// <returns><see langword="true"/> when the link was in force and is now taken back.</returns>
    /// <exception cref="ArgumentException">The reason is empty or too long.</exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    bool UnlinkCrossChain(ICase @case, CrossChainLink link, string reason);

    /// <summary>Remembers where the user placed a node in a layout (ordinary data, no journal).</summary>
    /// <param name="case">The open case.</param>
    /// <param name="nodeKey">The node's key from <see cref="GraphNode.Key"/>.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="position">The place.</param>
    void SavePosition(ICase @case, string nodeKey, GraphLayout layout, NodePosition position);

    /// <summary>
    /// Traces money through the graph by a method: from the seeds, through every transaction in the graph, to where
    /// it stops. Every traced satoshi ends somewhere, with a reason.
    /// </summary>
    /// <param name="graph">The graph, from <see cref="LoadAsync"/> or <see cref="ExpandAsync"/>.</param>
    /// <param name="request">The method and the seeds.</param>
    /// <returns>The traced amounts and the ends.</returns>
    /// <exception cref="ArgumentException">
    /// The method is not registered, there are no seeds, a seed's coin is not in the graph or is given twice, its
    /// amount is not within the coin, or a seed lies downstream of another seed.
    /// </exception>
    TraceResult Trace(TraceGraph graph, TraceRequest request);

    /// <summary>
    /// Traces money through the graph as <see cref="Trace"/> does, off the calling thread and stoppable between
    /// steps, so that a large graph never holds up the interface (principle 6).
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="request">The method and the seeds.</param>
    /// <param name="cancellationToken">Stops the trace.</param>
    /// <returns>The traced amounts and the ends.</returns>
    /// <exception cref="ArgumentException">As for <see cref="Trace"/>.</exception>
    /// <exception cref="OperationCanceledException">The trace was stopped.</exception>
    Task<TraceResult> TraceAsync(
        TraceGraph graph, TraceRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads how the user left the graph: the tracing method, the layout, which amounts the lines carry and the part
    /// of the surface on screen, so that the graph opens as it was left.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <returns>The view, or the defaults (FIFO, the free surface, every amount, fitted) when none is stored.</returns>
    GraphViewState GetView(ICase @case);

    /// <summary>
    /// Stores how the user left the graph. The layout, the amounts and the part on screen are ordinary data like the
    /// places; a change of tracing method also gets a line in the journal, since the traced amounts the user sees
    /// rest on it (principle 2).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="view">The view.</param>
    /// <returns><see langword="true"/> when the tracing method changed (and the journal has a line).</returns>
    /// <exception cref="ArgumentException">A value is not known or not finite.</exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> when the method changed, or <see cref="CaseError.Closed"/>.
    /// </exception>
    bool SaveView(ICase @case, GraphViewState view);
}

/// <summary>Which amounts the graph's lines carry (principle 7: the user can thin them out when it grows).</summary>
public enum GraphAmountLabels
{
    /// <summary>Every line carries its amount.</summary>
    All,

    /// <summary>Only the lines that carry traced money.</summary>
    Traced,

    /// <summary>No line; the amount shows on hover and when something is chosen.</summary>
    None,
}

/// <summary>The part of the graph's surface on screen: where the surface's origin is and how far it is zoomed.</summary>
/// <param name="OffsetX">The surface's origin from the left of the screen, in screen units.</param>
/// <param name="OffsetY">The surface's origin from the top of the screen, in screen units.</param>
/// <param name="Zoom">How far it is zoomed; 1 is natural size.</param>
public sealed record GraphViewport(double OffsetX, double OffsetY, double Zoom);

/// <summary>How the user left the graph.</summary>
/// <param name="Method">The tracing method the amounts are counted by.</param>
/// <param name="Layout">The free surface or the hop columns.</param>
/// <param name="AmountLabels">Which amounts the lines carry.</param>
/// <param name="Viewport">The part on screen, or <see langword="null"/> to fit the graph to the screen.</param>
public sealed record GraphViewState(
    TracingMethodKind Method, GraphLayout Layout, GraphAmountLabels AmountLabels, GraphViewport? Viewport)
{
    /// <summary>Gets the view of a graph the user has not changed: FIFO, the free surface, every amount, fitted.</summary>
    public static GraphViewState Default { get; } =
        new(TracingMethodKind.Fifo, GraphLayout.Free, GraphAmountLabels.All, null);
}

/// <summary>What to expand.</summary>
/// <param name="Address">The address whose history to bring in, or <see langword="null"/>.</param>
/// <param name="Txid">The transaction to follow forward, or <see langword="null"/>.</param>
/// <param name="MaximumTransactions">
/// The most transactions one expansion may bring in, so that an exchange's address or a batch payout does not flood
/// the graph (principle 7): an address's newest first, and of a transaction's spenders those that receive the most.
/// </param>
public sealed record ExpansionRequest(BitcoinAddress? Address, Txid? Txid, int MaximumTransactions = 50)
{
    /// <summary>Gives a request to expand an address.</summary>
    /// <param name="address">The address.</param>
    /// <param name="maximumTransactions">The most transactions to bring in.</param>
    /// <returns>The request.</returns>
    public static ExpansionRequest ForAddress(BitcoinAddress address, int maximumTransactions = 50) =>
        new(address ?? throw new ArgumentNullException(nameof(address)), null, maximumTransactions);

    /// <summary>Gives a request to follow a transaction forward.</summary>
    /// <param name="txid">The transaction.</param>
    /// <returns>The request.</returns>
    public static ExpansionRequest ForTransaction(Txid txid) =>
        new(null, txid ?? throw new ArgumentNullException(nameof(txid)));
}

/// <summary>How an expansion went.</summary>
public enum ExpansionOutcome
{
    /// <summary>Everything was brought in.</summary>
    Expanded,

    /// <summary>
    /// Some of it: an address's history or a transaction's spenders were held back at the limit, or a lookup on the
    /// way failed; the problem says which.
    /// </summary>
    Partly,

    /// <summary>
    /// Nothing could be brought in: there is no exit and the case does not have it, the lookup failed, or the source
    /// does not know it. The graph is unchanged and the node stays <em>not expanded yet</em>.
    /// </summary>
    NotExpanded,
}

/// <summary>What an expansion brought in.</summary>
/// <param name="Outcome">How it went.</param>
/// <param name="NewTransactions">The transactions that were not in the graph before.</param>
/// <param name="Problem">What went wrong or was held back, in Swedish, or <see langword="null"/>.</param>
/// <param name="Graph">The graph after the expansion.</param>
public sealed record ExpansionResult(
    ExpansionOutcome Outcome, IReadOnlyList<Txid> NewTransactions, string? Problem, TraceGraph Graph)
{
    /// <summary>
    /// Gets what the expansion held back so that the graph does not flood (principle 7), or <see langword="null"/>
    /// when it brought in everything it found.
    /// </summary>
    public ExpansionHoldBack? HeldBack { get; init; }
}

/// <summary>What an expansion held back at its limit, said honestly (principles 7 and 9).</summary>
/// <param name="Taken">How many transactions it brought in.</param>
/// <param name="Available">
/// How many there are in all, when the source says (an address's summary, or the spenders of a transaction's
/// outputs); <see langword="null"/> when it is only known that there are more.
/// </param>
public sealed record ExpansionHoldBack(int Taken, long? Available);

/// <summary>The journal codes the Tracing module writes.</summary>
public static class TracingJournalKinds
{
    /// <summary>A start point was added.</summary>
    public const string StartPointAdded = "tracing.start-point-added";

    /// <summary>A start point was removed.</summary>
    public const string StartPointRemoved = "tracing.start-point-removed";

    /// <summary>The user linked a coin to a transaction on another chain.</summary>
    public const string CrossChainLinked = "tracing.cross-chain-linked";

    /// <summary>The user took back a link to another chain.</summary>
    public const string CrossChainUnlinked = "tracing.cross-chain-unlinked";

    /// <summary>The user changed the tracing method the graph's amounts are counted by.</summary>
    public const string MethodChanged = "tracing.method-changed";
}
