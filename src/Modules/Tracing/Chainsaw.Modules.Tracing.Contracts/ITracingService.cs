using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

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
    /// <summary>Gets the tracing methods for Bitcoin, the newest version of each kind.</summary>
    IReadOnlyList<TracingMethodId> Methods { get; }

    /// <summary>Gets the tracing methods for the account model on Ethereum, the newest version of each kind.</summary>
    IReadOnlyList<TracingMethodId> AccountMethods { get; }

    /// <summary>Builds the graph from the case: start points, expansions, clusters and the user's links.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The graph.</returns>
    Task<TraceGraph> LoadAsync(ICase @case, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens or closes an account cluster in the graph (sketch KE1): a click on the pile unfolds it to its accounts over
    /// an area, and folding it shows the pile again. Only the view changes; nothing is written to the case.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="key">The cluster's key (<see cref="GraphAccountCluster.Key"/>).</param>
    /// <param name="open">Whether it is unfolded.</param>
    void SetAccountClusterOpen(ICase @case, string key, bool open)
    {
    }

    /// <summary>
    /// Reads an account's whole history into the case, every page down to the first, so that an address the graph
    /// shows only in part (held back at the limit) carries the traced money on (the user's decision 2026-11-09: a
    /// partly read address waits until all of it is read). The graph still draws what the expansion brought in; the
    /// rest stands in the trace's ledgers (<see cref="AccountGraph.Unshown"/>). Every page is sealed as it is read.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="account">The account, on an account chain whose history the graph has expanded.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>Why the whole history could not be read, or <see langword="null"/> when it is in the case.</returns>
    Task<string?> ReadWholeHistoryAsync(
        ICase @case, ChainAddress account, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>("Hela historiken kan inte läsas här.");

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
    /// The target is on Bitcoin, the coin already has a link in force, or the reason is too long.
    /// </exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    CrossChainLink LinkCrossChain(ICase @case, Outpoint from, ChainTransactionRef to, string reason);

    /// <summary>
    /// Links an Ethereum movement (the payment into a bridge or a swap) to the transaction where the money continues on
    /// another chain: the user's judgement, sealed, with a line in the journal.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="from">The movement that went into the bridge or swap.</param>
    /// <param name="to">The transaction on the other chain; not Ethereum.</param>
    /// <param name="reason">Why, at most 1 000 characters.</param>
    /// <returns>The link.</returns>
    /// <exception cref="ArgumentException">
    /// The target is on Ethereum, the movement already has a link in force, or the reason is too long.
    /// </exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    CrossChainLink LinkCrossChain(ICase @case, AccountSeed from, ChainTransactionRef to, string reason);

    /// <summary>
    /// Takes back a link to another chain. Nothing is removed: the link stays sealed, and a new sealed record of the
    /// taking back, with who, when and the reason if one is given, says it no longer counts. A link taken back cannot
    /// be taken back again; it is made anew instead.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="link">The link.</param>
    /// <param name="reason">Why, at most 1 000 characters; optional (the user's decision 2026-11-07, AN1b).</param>
    /// <returns><see langword="true"/> when the link was in force and is now taken back.</returns>
    /// <exception cref="ArgumentException">The reason is too long.</exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    bool UnlinkCrossChain(ICase @case, CrossChainLink link, string? reason);

    /// <summary>
    /// Reads every link to another chain, also those taken back, each with its taking back, oldest first (sketch
    /// AN1b, <em>Historik</em>).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <returns>The links and their taking back.</returns>
    IReadOnlyList<CrossChainLinkHistoryEntry> GetCrossChainHistory(ICase @case);

    /// <summary>
    /// Chooses that the traced value does not follow through a swap (sketch SW1a, <em>Följer inte</em>): the user's
    /// judgement, sealed with who and when, with a line in the journal. The trail then ends in the swap.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="swap">The swap, from <see cref="AccountGraph.Swaps"/>.</param>
    /// <param name="reason">Why, at most 1 000 characters; optional (the user's decision 2026-11-07).</param>
    /// <returns>The choice, or the one already in force when the swap is already not followed.</returns>
    /// <exception cref="ArgumentException">The reason is too long, or the swap is not in the case's graph.</exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    SwapStop StopAtSwap(ICase @case, TokenSwap swap, string? reason);

    /// <summary>
    /// Takes back a choice that the value does not follow through a swap, so that it follows again (the default).
    /// Nothing is removed: the choice stays sealed, and a new sealed record of the taking back says it no longer
    /// counts (sketch AN1b).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="stop">The choice.</param>
    /// <param name="reason">Why, at most 1 000 characters; optional.</param>
    /// <returns><see langword="true"/> when the choice was in force and is now taken back.</returns>
    /// <exception cref="ArgumentException">The reason is too long.</exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    bool FollowSwap(ICase @case, SwapStop stop, string? reason);

    /// <summary>
    /// Reads every choice not to follow a swap, also those taken back, each with its taking back, oldest first
    /// (sketch AN1b, <em>Historik</em>).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <returns>The choices and their taking back.</returns>
    IReadOnlyList<SwapStopHistoryEntry> GetSwapStopHistory(ICase @case);

    /// <summary>
    /// Reads, from the case only, which of the graph's UTXO transactions the answers settle are gone
    /// (<see cref="GoneTransaction.IsSettled"/>): those the expansions leave out of the clustering (sketch HB1a), so
    /// that a decision about a held back conclusion can be checked against the same set.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The settled gone transactions.</returns>
    Task<IReadOnlyCollection<Txid>> GetSettledGoneAsync(ICase @case, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chooses that the trace follows the transaction that spends the coins of a transaction the source no longer
    /// knows (sketch GR9a, <em>Ersatt av</em>): the user's judgement that the replacer carries the same money on,
    /// sealed with who and when, with a line in the journal. The replacer is fetched through the exit (sealed like
    /// every answer) and comes into the graph; the trace then goes on through it, drawn dashed (principle 2).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="gone">The transaction that is gone, as <see cref="TraceGraph.Gone"/> names it.</param>
    /// <param name="replacer">One of the transactions the case knows spends its coins now.</param>
    /// <param name="reason">Why, at most 1 000 characters; optional (the user's decision 2026-11-07).</param>
    /// <param name="cancellationToken">Stops the lookup of the replacer.</param>
    /// <returns>The choice, or the one already in force for the same replacer.</returns>
    /// <exception cref="ArgumentException">
    /// The reason is too long, the transaction is not gone, or the case does not know the replacer spends its coins.
    /// </exception>
    /// <exception cref="ReplacementUnavailableException">
    /// The replacer could not be had from the source or the case; the message says why.
    /// </exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    Task<ReplacementFollow> FollowReplacementAsync(
        ICase @case, Txid gone, Txid replacer, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes back a choice to follow a replacer, so that the trail ends at the gone transaction again. Nothing is
    /// removed: the choice stays sealed, and a new sealed record of the taking back says it no longer counts (sketch
    /// AN1b). The replacer leaves the graph unless an expansion brought it in too; its sealed answers stay in the case.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="follow">The choice.</param>
    /// <param name="reason">Why, at most 1 000 characters; optional.</param>
    /// <returns><see langword="true"/> when the choice was in force and is now taken back.</returns>
    /// <exception cref="ArgumentException">The reason is too long.</exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    bool TakeBackReplacement(ICase @case, ReplacementFollow follow, string? reason);

    /// <summary>Remembers where the user placed a node in a layout (ordinary data, no journal).</summary>
    /// <param name="case">The open case.</param>
    /// <param name="nodeKey">The node's key from <see cref="GraphNode.Key"/>.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="position">The place.</param>
    void SavePosition(ICase @case, string nodeKey, GraphLayout layout, NodePosition position);

    /// <summary>
    /// Remembers where many nodes stand in a layout in one write (ordinary data, no journal), as when the graph is
    /// sorted: either every place is stored or none is.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="positions">The places by the nodes' keys from <see cref="GraphNode.Key"/>.</param>
    void SavePositions(ICase @case, GraphLayout layout, IReadOnlyDictionary<string, NodePosition> positions);

    /// <summary>
    /// Reads what the expansions of an address held back (sketch EX1): its history past the transactions already in
    /// the graph, through the source as any lookup (every response sealed in the case, principle 1), without adding
    /// anything to the graph. Offline, it answers from what the case holds. When the newest
    /// <see cref="HeldBackList.Most"/> do not reach back to <paramref name="around"/>, the history is also read back
    /// from a transaction of the graph's just after it, so that the time round it is covered.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address that was expanded.</param>
    /// <param name="around">
    /// A time the read must also cover, ±<see cref="HeldBackList.AroundArrival"/> (the traced money's arrival), or
    /// <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The transactions held back, newest first.</returns>
    Task<HeldBackList> ListHeldBackAsync(
        ICase @case,
        BitcoinAddress address,
        DateTimeOffset? around = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Brings chosen transactions that an address's expansion held back into the graph (sketch EX2: <em>Plocka
    /// ut</em>), as part of that expansion: their new counterparties lie in the address's pile when they are many. A
    /// transaction that does not touch the address is left out.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address that was expanded.</param>
    /// <param name="transactions">The chosen transactions.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>What came in, and the graph after it.</returns>
    /// <exception cref="ArgumentException">There are no transactions.</exception>
    Task<ExpansionResult> TakeHeldBackAsync(
        ICase @case,
        BitcoinAddress address,
        IReadOnlyCollection<Txid> transactions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Picks actors out of their piles (sketch EX2b), so that the graph draws them as nodes of their own; they stay
    /// picked when the case is opened again. Ordinary data like the places, with no line in the journal: the pile is
    /// arrangement, and every fact stays in the graph whatever is picked (principle 1).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="nodeKeys">The actors' node keys, from <see cref="GraphPile.Members"/>.</param>
    void PickFromPile(ICase @case, IReadOnlyCollection<string> nodeKeys);

    /// <summary>
    /// Puts actors that were picked out back in their piles; an actor that was never picked is left as it is.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="nodeKeys">The actors' node keys, from <see cref="GraphPile.PickedOut"/>.</param>
    void ReturnToPile(ICase @case, IReadOnlyCollection<string> nodeKeys);

    /// <summary>
    /// Traces money through the graph by a method: from the seeds, through every transaction in the graph, to where
    /// it stops. Every traced unit ends somewhere, with a reason.
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
    /// Traces money through the account part of the graph (Ethereum) by a method, per asset: from the seeds, through
    /// every account whose history the graph has read, to where it stops; through a swap the traced value follows into
    /// the other asset by <see cref="SwapValue"/>, unless the user chose that it does not (<see cref="SwapStop"/>).
    /// Every account's share is checked (<c>AccountChecks.Run</c>), so a method that creates or loses money is refused.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="request">The method and the seeds.</param>
    /// <param name="cancellationToken">Stops the trace.</param>
    /// <returns>The traced amounts and the ends, per asset.</returns>
    /// <exception cref="ArgumentException">
    /// The method is not registered for Ethereum, there are no seeds, or a seed is not a movement in the graph or is
    /// given twice.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A method broke its contract, or the seeds lie in each other's flow so that money would be counted twice.
    /// </exception>
    Task<AccountTraceResult> TraceAccountsAsync(
        TraceGraph graph, AccountTraceRequest request, CancellationToken cancellationToken = default);

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

/// <summary>The part of the graph's surface on screen: where its origin is and how far it is zoomed.</summary>
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
    /// <summary>Gets the view of an unchanged graph: FIFO, the free surface, every amount, fitted.</summary>
    public static GraphViewState Default { get; } =
        new(TracingMethodKind.Fifo, GraphLayout.Free, GraphAmountLabels.All, null);

    /// <summary>
    /// Gets whether the graph shows the outputs that can never be spent and carry nothing (OP_RETURN, 0 BTC), which it
    /// hides by default (the user's decision 2026-11-07).
    /// </summary>
    public bool ShowUnspendable { get; init; }
}

/// <summary>What to expand: one Bitcoin address or transaction, or one Ethereum address or transaction.</summary>
/// <param name="Address">The Bitcoin address whose history to bring in, or <see langword="null"/>.</param>
/// <param name="Txid">The Bitcoin transaction to follow forward, or <see langword="null"/>.</param>
/// <param name="MaximumTransactions">
/// The most transactions one expansion may bring in, so that an exchange's address or a batch payout does not flood
/// the graph (principle 7): an address's newest first, and of a transaction's spenders those that receive the most.
/// </param>
public sealed record ExpansionRequest(BitcoinAddress? Address, Txid? Txid, int MaximumTransactions = 50)
{
    /// <summary>Gets the Ethereum address whose history to bring in, or <see langword="null"/>.</summary>
    public EthereumAddress? EthereumAddress { get; init; }

    /// <summary>Gets the Ethereum transaction to bring in with every movement in it, or <see langword="null"/>.</summary>
    public EthereumTxHash? EthereumTransaction { get; init; }

    /// <summary>Gives a request to bring in an Ethereum address's history: its transactions, internal transactions
    /// and token transfers, newest first, up to the limit.</summary>
    /// <param name="address">The address.</param>
    /// <param name="maximumTransactions">The most transactions to bring in.</param>
    /// <returns>The request.</returns>
    public static ExpansionRequest ForEthereumAddress(EthereumAddress address, int maximumTransactions = 50) =>
        new(null, null, maximumTransactions)
        {
            EthereumAddress = address ?? throw new ArgumentNullException(nameof(address)),
        };

    /// <summary>Gives a request to bring in an Ethereum transaction with every movement of value in it.</summary>
    /// <param name="hash">The transaction's hash.</param>
    /// <returns>The request.</returns>
    public static ExpansionRequest ForEthereumTransaction(EthereumTxHash hash) =>
        new(null, null) { EthereumTransaction = hash ?? throw new ArgumentNullException(nameof(hash)) };

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
/// <param name="Problem">
/// What went wrong or was held back, and what the user should know besides (a transaction that left its block, one
/// that could not be checked against its bytes), in Swedish; <see langword="null"/> when there is nothing to say.
/// </param>
/// <param name="Graph">The graph after the expansion.</param>
public sealed record ExpansionResult(
    ExpansionOutcome Outcome, IReadOnlyList<Txid> NewTransactions, string? Problem, TraceGraph Graph)
{
    /// <summary>Gets the Ethereum transactions that were not in the graph before.</summary>
    public IReadOnlyList<EthereumTxHash> NewEthereumTransactions { get; init; } = [];

    /// <summary>Gets how many transactions on any chain came into the graph.</summary>
    public int NewCount => NewTransactions.Count + NewEthereumTransactions.Count;

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

    /// <summary>The user chose that the traced value does not follow through a swap (sketch SW1a).</summary>
    public const string SwapStopped = "tracing.swap-stopped";

    /// <summary>The user took back a choice not to follow a swap, so that the value follows it again.</summary>
    public const string SwapFollowed = "tracing.swap-followed";

    /// <summary>
    /// The user chose that the trace follows the transaction that replaced one the source no longer knows (sketch
    /// GR9a, <em>Ersatt av</em>).
    /// </summary>
    public const string ReplacementFollowed = "tracing.replacement-followed";

    /// <summary>The user took back a choice to follow a replacer, so that the trail ends at the gone one again.</summary>
    public const string ReplacementTakenBack = "tracing.replacement-taken-back";

    /// <summary>The user changed the tracing method the graph's amounts are counted by.</summary>
    public const string MethodChanged = "tracing.method-changed";

    /// <summary>The user threw nodes of the graph in the trash (sketch SO1a); nothing left the case.</summary>
    public const string Trashed = "tracing.trashed";

    /// <summary>The user put nodes back from the trash.</summary>
    public const string Restored = "tracing.restored";

    /// <summary>The user wrote a comment on the graph (sketch KN1a).</summary>
    public const string CommentAdded = "tracing.comment-added";

    /// <summary>The user changed a comment's text.</summary>
    public const string CommentEdited = "tracing.comment-edited";

    /// <summary>The user tied a comment's thread to another node, or untied it.</summary>
    public const string CommentAnchored = "tracing.comment-anchored";

    /// <summary>The user removed a comment.</summary>
    public const string CommentRemoved = "tracing.comment-removed";
}
