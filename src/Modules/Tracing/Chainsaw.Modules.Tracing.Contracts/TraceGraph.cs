using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// The graph as a model: the actors the money passes and the transfers of an asset between them, built from what the
/// user has expanded (principle 7) and what the case holds. The transfers, amounts and times are facts from the
/// chain; clusters, ends of the trail and the hops are conclusions or arrangement (principle 2).
/// </summary>
/// <remarks>
/// The actors, the edges and their transfers are the same whatever the chain; what only one kind of chain has sits
/// in that model's part (<see cref="Utxo"/> for coins; the account model's part comes with the first account chain).
/// </remarks>
/// <param name="StartPoints">The trace's start points.</param>
/// <param name="Nodes">The actors, in a stable order: the start points first, then by hop and time.</param>
/// <param name="Edges">The transfers between actors, summed per pair, kind and asset.</param>
/// <param name="Utxo">
/// The UTXO model's part: the transactions, whether their coins are spent, and where the trail ends at them.
/// </param>
/// <param name="CrossChainLinks">The user's links to other chains that are in force.</param>
/// <param name="Expansions">What the user has expanded.</param>
/// <param name="Problems">What could not be read, in Swedish; empty when everything was there.</param>
public sealed record TraceGraph(
    IReadOnlyList<TraceStartPoint> StartPoints,
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    UtxoGraph Utxo,
    IReadOnlyList<CrossChainLink> CrossChainLinks,
    IReadOnlyList<Expansion> Expansions,
    IReadOnlyList<string> Problems)
{
    /// <summary>
    /// The lookups and the node list they were built from; a copy made with <c>with</c> and other nodes builds its
    /// own instead of answering from the old ones.
    /// </summary>
    private NodeIndex? _index;

    /// <summary>
    /// Gets every transaction in the graph, with how the case answered for it; setting it in <c>with</c> sets it in
    /// <see cref="Utxo"/>.
    /// </summary>
    public IReadOnlyDictionary<Txid, GraphTransaction> Transactions
    {
        get => Utxo.Transactions;
        init => Utxo = Utxo with { Transactions = value };
    }

    /// <summary>
    /// Gets whether each coin in the graph is spent, and by what; setting it in <c>with</c> sets it in
    /// <see cref="Utxo"/>.
    /// </summary>
    public IReadOnlyDictionary<Outpoint, CoinSpend> Spends
    {
        get => Utxo.Spends;
        init => Utxo = Utxo with { Spends = value };
    }

    /// <summary>
    /// Gets the transactions where the trail ends, with why; setting it in <c>with</c> sets it in <see cref="Utxo"/>.
    /// </summary>
    public IReadOnlyDictionary<Txid, IReadOnlyList<TrailEndHint>> TransactionEnds
    {
        get => Utxo.TransactionEnds;
        init => Utxo = Utxo with { TransactionEnds = value };
    }

    /// <summary>
    /// Gets the account model's part: the movements of value, fees and transactions on Ethereum that the graph holds,
    /// and which accounts' histories it has read. Empty when the graph has no account chain.
    /// </summary>
    public AccountGraph Account { get; init; } = AccountGraph.Empty;

    /// <summary>
    /// Gets the user's choices in force that the traced value does not follow through a swap (sketch SW1a); every
    /// other swap is followed by <see cref="SwapValue"/>.
    /// </summary>
    public IReadOnlyList<SwapStop> SwapStops { get; init; } = [];

    /// <summary>
    /// Gets the piles (sketch EX2b): the many new counterparties of an expansion, kept in one node per side of the
    /// expanded actor. Their members stay in <see cref="Nodes"/> and <see cref="Edges"/>, since they are facts; the
    /// graph draws them in the pile until the user picks them out.
    /// </summary>
    public IReadOnlyList<GraphPile> Piles { get; init; } = [];

    /// <summary>
    /// Gets the graph's transactions that the source no longer knows (sketch GR9a, <em>Finns inte längre</em>), with
    /// what spends their coins now as far as the case knows; the trail ends at each (<see
    /// cref="TrailEndReason.TransactionGone"/>) unless the user chose to follow its replacer.
    /// </summary>
    public IReadOnlyDictionary<Txid, GoneTransaction> Gone { get; init; } = new Dictionary<Txid, GoneTransaction>();

    /// <summary>Gets the user's choices in force to follow the replacer of a gone transaction, oldest first.</summary>
    public IReadOnlyList<ReplacementFollow> FollowedReplacements { get; init; } = [];

    /// <summary>Gets what the expansions held back, one clue per actor (sketch EX1).</summary>
    public IReadOnlyList<HeldBackClue> HeldBack { get; init; } = [];

    /// <summary>Gives a node by its key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The node, or <see langword="null"/>.</returns>
    public GraphNode? Node(string key) => Index().ByKey.GetValueOrDefault(key);

    /// <summary>Gives the node an address belongs to in the graph.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The node, or <see langword="null"/> when the address is not in the graph.</returns>
    public GraphNode? NodeOf(BitcoinAddress address) => Index().ByAddress.GetValueOrDefault(address);

    /// <summary>Gives the node an address on any chain belongs to in the graph.</summary>
    /// <param name="address">The address with its chain.</param>
    /// <returns>The node, or <see langword="null"/> when the address is not in the graph.</returns>
    public GraphNode? NodeOf(ChainAddress address) => Index().ByAccount.GetValueOrDefault(address);

    /// <summary>Gives the lookups for the current nodes, building them when the nodes are new.</summary>
    /// <returns>The lookups.</returns>
    private NodeIndex Index()
    {
        var index = _index;
        if (index is null || !ReferenceEquals(index.Nodes, Nodes))
        {
            index = new NodeIndex(
                Nodes,
                Nodes.ToDictionary(node => node.Key, StringComparer.Ordinal),
                Nodes.SelectMany(node => node.GraphAddresses.Select(address => (address, node)))
                    .ToDictionary(pair => pair.address, pair => pair.node),
                Nodes.SelectMany(node => node.Addresses.Select(address => (address, node)))
                    .ToDictionary(pair => pair.address, pair => pair.node));
            _index = index;
        }

        return index;
    }

    /// <summary>The graph's lookups.</summary>
    /// <param name="Nodes">The node list they were built from.</param>
    /// <param name="ByKey">The nodes by key.</param>
    /// <param name="ByAddress">The nodes by the Bitcoin addresses they hold.</param>
    /// <param name="ByAccount">The nodes by the addresses they hold on any chain.</param>
    private sealed record NodeIndex(
        IReadOnlyList<GraphNode> Nodes,
        Dictionary<string, GraphNode> ByKey,
        Dictionary<BitcoinAddress, GraphNode> ByAddress,
        Dictionary<ChainAddress, GraphNode> ByAccount);
}

/// <summary>What kind of actor a node is; the graph draws the kind as its shape (skiss GR3a).</summary>
public enum GraphNodeKind
{
    /// <summary>
    /// One address that nothing links to another: an actor we know nothing about (round). On an account chain every
    /// address is one, since the account model's heuristics are not built yet.
    /// </summary>
    Address,

    /// <summary>Addresses that Clustering concludes have the same owner: a wallet (square).</summary>
    Cluster,

    /// <summary>
    /// A transaction whose inputs belong to more than one actor (for example a CoinJoin), drawn as a node of its own
    /// because which input paid which output cannot be read.
    /// </summary>
    Transaction,

    /// <summary>A coin whose script has no address (for example P2PK, bare multisig or OP_RETURN).</summary>
    Coin,

    /// <summary>A transaction on another chain that the user linked to (purple).</summary>
    OtherChain,

    /// <summary>
    /// A pile of counterparties (<see cref="GraphPile"/>, sketch EX2b), as the graph draws it; never in
    /// <see cref="TraceGraph.Nodes"/>, whose actors are the pile's members.
    /// </summary>
    Pile,

    /// <summary>
    /// What an expansion held back (<see cref="HeldBackClue"/>, sketch EX1), as the graph draws it; never in
    /// <see cref="TraceGraph.Nodes"/>.
    /// </summary>
    HeldBack,
}

/// <summary>How far a node has been expanded.</summary>
public enum NodeExpansion
{
    /// <summary>Nothing about the node has been expanded; what goes on from it is not in the graph.</summary>
    NotExpanded,

    /// <summary>Some of it: an address's history was cut at the limit, or only some of a cluster's addresses.</summary>
    Partly,

    /// <summary>All of it, as far as the case knew when it was expanded.</summary>
    Expanded,
}

/// <summary>An actor in the graph.</summary>
/// <param name="Key">
/// The node's stable key: <c>address:</c>, <c>cluster:</c> (with the representative address), <c>tx:</c>,
/// <c>coin:</c> or <c>chain:</c> and the subject; <c>account:</c>, the chain and the address on an account chain.
/// </param>
/// <param name="Kind">What kind of actor it is.</param>
/// <param name="GraphAddresses">
/// The node's Bitcoin addresses that occur in the graph, sorted; empty on another chain (see <see cref="Accounts"/>).
/// </param>
/// <param name="ClusterSize">How many addresses the whole cluster has in the case; 1 for a single address.</param>
/// <param name="Transaction">The transaction, for <see cref="GraphNodeKind.Transaction"/>.</param>
/// <param name="Coin">The coin, for <see cref="GraphNodeKind.Coin"/>.</param>
/// <param name="OtherChain">The other chain's transaction, for <see cref="GraphNodeKind.OtherChain"/>.</param>
/// <param name="IsStartPoint">Whether a start point is here.</param>
/// <param name="Hop">
/// How many hops from the start the node lies: 0 for the start, positive along the money, negative before the start;
/// <see langword="null"/> when the graph has no start point that reaches it.
/// </param>
/// <param name="FirstReceived">
/// When money first came to the node in the graph (the block's time); <see langword="null"/> when none has come in a
/// confirmed transaction.
/// </param>
/// <param name="Expansion">How far it has been expanded.</param>
/// <param name="Ends">Why the trail ends here, when it does; empty otherwise.</param>
/// <param name="Positions">Where the user placed the node, per layout; empty when it has not been placed.</param>
public sealed record GraphNode(
    string Key,
    GraphNodeKind Kind,
    IReadOnlyList<BitcoinAddress> GraphAddresses,
    int ClusterSize,
    Txid? Transaction,
    Outpoint? Coin,
    ChainTransactionRef? OtherChain,
    bool IsStartPoint,
    int? Hop,
    DateTimeOffset? FirstReceived,
    NodeExpansion Expansion,
    IReadOnlyList<TrailEndHint> Ends,
    IReadOnlyDictionary<GraphLayout, NodePosition> Positions)
{
    /// <summary>Gets the chain the actor is on; Bitcoin unless it is an account on another chain.</summary>
    public Chain Chain { get; init; } = Chain.Bitcoin;

    /// <summary>
    /// Gets the node's addresses on an account chain (Ethereum), sorted; empty on Bitcoin, whose addresses are
    /// <see cref="GraphAddresses"/>.
    /// </summary>
    public IReadOnlyList<ChainAddress> Accounts { get; init; } = [];

    /// <summary>
    /// Gets the node's addresses in the graph on any chain, each with its chain: what the labels, the history and the
    /// forms name an actor by, whatever the chain.
    /// </summary>
    public IReadOnlyList<ChainAddress> Addresses =>
        Accounts.Count > 0 ? Accounts : [.. GraphAddresses.Select(address => address.ToChainAddress())];
}

/// <summary>The graph's two layouts (skiss GR4); each remembers its places.</summary>
public enum GraphLayout
{
    /// <summary>The free surface, where the actors stand where the user drags them.</summary>
    Free,

    /// <summary>Columns per hop, where the actors are dragged up and down.</summary>
    HopColumns,
}

/// <summary>Where a node stands in a layout.</summary>
/// <param name="X">The horizontal position.</param>
/// <param name="Y">The vertical position.</param>
public readonly record struct NodePosition(double X, double Y);

/// <summary>What kind of edge it is.</summary>
public enum GraphEdgeKind
{
    /// <summary>Money moved on the chain from one actor to another.</summary>
    Transfer,

    /// <summary>The money continues on another chain, linked by the user (<see cref="CrossChainLink"/>).</summary>
    ContinuesOnOtherChain,

    /// <summary>
    /// The thin dashed line from an actor to its clue of what an expansion held back (<see cref="HeldBackClue"/>,
    /// sketch EX1). It carries no money and is never a fact of the chain: the graph draws it, Tracing never holds one.
    /// </summary>
    HeldBack,
}

/// <summary>Which way an edge runs relative to the hops.</summary>
public enum EdgeDirection
{
    /// <summary>To a later hop, along the money.</summary>
    Forward,

    /// <summary>Between actors in the same hop.</summary>
    Sideways,

    /// <summary>Back to an earlier hop: money that turns, drawn as an arc (skiss GR2b and GR4).</summary>
    Back,

    /// <summary>One of the ends has no hop.</summary>
    Unordered,
}

/// <summary>
/// The transfers of one asset from one actor to another, summed. Each transfer and its amount is a fact from the
/// chain; which actor an address belongs to is Clustering's conclusion, so the sum per pair of actors rests on it. An
/// edge that continues on another chain rests on the user's link (a judgement), and its amount is what left on this
/// chain, not what arrived on the other.
/// </summary>
/// <param name="Key">The edge's key: the kind and the two nodes' keys.</param>
/// <param name="From">The sending node's key.</param>
/// <param name="To">The receiving node's key.</param>
/// <param name="Kind">The kind of edge.</param>
/// <param name="Amount">The sum of the transfers, in their asset.</param>
/// <param name="Transfers">The transfers, in time order; all of the same asset.</param>
/// <param name="FirstTime">The time of the first confirmed transfer, or <see langword="null"/>.</param>
/// <param name="Direction">Which way the edge runs relative to the hops.</param>
public sealed record GraphEdge(
    string Key,
    string From,
    string To,
    GraphEdgeKind Kind,
    AssetAmount Amount,
    IReadOnlyList<AssetTransfer> Transfers,
    DateTimeOffset? FirstTime,
    EdgeDirection Direction);

/// <summary>
/// One transfer of an asset along an edge, in one transaction: a fact from the chain. What only one kind of chain has
/// is the chain's own detail: on a UTXO chain the coin the money moved as.
/// </summary>
/// <param name="Transaction">The transaction the money moved in.</param>
/// <param name="Amount">The amount and its asset.</param>
/// <param name="Block">The transaction's block; <see langword="null"/> while it is unconfirmed.</param>
/// <param name="Coin">
/// On a UTXO chain, the coin: an output of <see cref="Transaction"/> when it is paid out, or the earlier output an
/// input spends when it is paid into a transaction node. <see langword="null"/> on an account chain.
/// </param>
public sealed record AssetTransfer(
    ChainTransactionRef Transaction, AssetAmount Amount, BlockReference? Block, Outpoint? Coin)
{
    /// <summary>
    /// Gets how the value moved on an account chain (ether sent by the transaction, by a contract, or a token), or
    /// <see langword="null"/> on a UTXO chain.
    /// </summary>
    public EthereumTransferKind? Movement { get; init; }

    /// <summary>
    /// Gets where in the transaction it moved on an account chain (see <see cref="EthereumTransfer.Position"/>), so that
    /// two movements in one transaction stay apart; 0 on a UTXO chain.
    /// </summary>
    public long Position { get; init; }

    /// <summary>
    /// Gets an account chain's block as the movement's answer gave it: its height and time, and its hash when known.
    /// It is set also when <see cref="Block"/> is not, because the case lacks the block's hash; the movement is
    /// confirmed all the same. <see langword="null"/> on a UTXO chain and while unconfirmed.
    /// </summary>
    public EthereumBlockReference? AccountBlock { get; init; }

    /// <summary>Gets the block's height, with or without its hash; <see langword="null"/> while unconfirmed.</summary>
    public long? Height => Block?.Height ?? AccountBlock?.Height;

    /// <summary>Gets the block's time, with or without its hash; <see langword="null"/> while unconfirmed.</summary>
    public DateTimeOffset? Time => Block?.Time ?? AccountBlock?.Time;

    /// <summary>Gives the transfer of an Ethereum movement, as an edge carries it.</summary>
    /// <param name="transfer">The movement.</param>
    /// <param name="block">Its block with the hash, or <see langword="null"/> while unconfirmed or the hash is not
    /// known.</param>
    /// <returns>The transfer.</returns>
    public static AssetTransfer OfEthereum(EthereumTransfer transfer, BlockReference? block)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        return new AssetTransfer(
            new ChainTransactionRef(Chain.Ethereum, transfer.Transaction.Value), transfer.Amount, block, null)
        {
            Movement = transfer.Kind,
            Position = transfer.Position,
            AccountBlock = transfer.Block,
        };
    }

    /// <summary>Gives the transfer of a Bitcoin coin.</summary>
    /// <param name="txid">The transaction the coin moved in.</param>
    /// <param name="coin">The coin.</param>
    /// <param name="amount">The coin's amount.</param>
    /// <param name="block">The transaction's block, or <see langword="null"/>.</param>
    /// <returns>The transfer.</returns>
    public static AssetTransfer OfCoin(Txid txid, Outpoint coin, Satoshi amount, BlockReference? block) =>
        new(ChainTransactionRef.Of(txid), amount.ToAmount(), block, coin);
}

/// <summary>
/// The UTXO model's part of the graph: the transactions as the chain has them, whether each coin is spent, and the
/// transactions where the trail ends. The tracing methods for coins work on this.
/// </summary>
/// <param name="Transactions">Every transaction in the graph, with how the case answered for it.</param>
/// <param name="Spends">Whether each output of each transaction in the graph is spent, and by what.</param>
/// <param name="TransactionEnds">The transactions where the trail ends, with why (for example a CoinJoin).</param>
public sealed record UtxoGraph(
    IReadOnlyDictionary<Txid, GraphTransaction> Transactions,
    IReadOnlyDictionary<Outpoint, CoinSpend> Spends,
    IReadOnlyDictionary<Txid, IReadOnlyList<TrailEndHint>> TransactionEnds);

/// <summary>A transaction in the graph and how the case answered for it.</summary>
/// <param name="Transaction">The transaction.</param>
/// <param name="Status">How the lookup was answered.</param>
/// <param name="Problem">Why it may be outdated, or <see langword="null"/>.</param>
public sealed record GraphTransaction(BitcoinTransaction Transaction, LookupStatus Status, string? Problem);

/// <summary>Whether a coin is spent, as far as the case knows.</summary>
public enum SpendState
{
    /// <summary>The case does not know; the transaction's spends have not been looked up.</summary>
    Unknown,

    /// <summary>It was unspent when the case asked.</summary>
    Unspent,

    /// <summary>It is spent by <see cref="CoinSpend.SpentBy"/>.</summary>
    Spent,
}

/// <summary>Whether a coin is spent and by which input.</summary>
/// <param name="State">Whether it is spent.</param>
/// <param name="SpentBy">The input that spends it, when it is spent.</param>
/// <param name="MayBeOutdated">Whether the answer may have changed since the case asked.</param>
public sealed record CoinSpend(SpendState State, BitcoinSpend? SpentBy, bool MayBeOutdated);

/// <summary>
/// Something the user expanded: an address's history or a transaction and where its outputs went. On Ethereum the
/// subject and what came in are in the Ethereum properties, and <see cref="Address"/>, <see cref="Txid"/> and
/// <see cref="Transactions"/> are empty.
/// </summary>
/// <param name="Address">The Bitcoin address, or <see langword="null"/> for anything else.</param>
/// <param name="Txid">The Bitcoin transaction, or <see langword="null"/> for anything else.</param>
/// <param name="Transactions">The transactions the expansion brought into the graph.</param>
/// <param name="IsComplete">
/// Whether everything was brought in; <see langword="false"/> when an address's history was cut at the limit or a
/// lookup failed.
/// </param>
/// <param name="ExpandedUtc">When it was expanded.</param>
public sealed record Expansion(
    BitcoinAddress? Address,
    Txid? Txid,
    IReadOnlyList<Txid> Transactions,
    bool IsComplete,
    DateTimeOffset ExpandedUtc)
{
    /// <summary>Gets the Ethereum address whose history was brought in, or <see langword="null"/>.</summary>
    public EthereumAddress? EthereumAddress { get; init; }

    /// <summary>Gets the Ethereum transaction that was brought in, or <see langword="null"/>.</summary>
    public EthereumTxHash? EthereumTransaction { get; init; }

    /// <summary>
    /// Gets the Ethereum transactions the expansion brought into the graph, newest first; empty on Bitcoin, whose are
    /// <see cref="Transactions"/>.
    /// </summary>
    public IReadOnlyList<EthereumTxHash> EthereumTransactions { get; init; } = [];

    /// <summary>
    /// Gets what the newest expansion held back at its limit, or <see langword="null"/> when it brought in everything
    /// it found (or a lookup failed, which <see cref="IsComplete"/> says).
    /// </summary>
    public ExpansionHoldBack? HeldBack { get; init; }

    /// <summary>
    /// Gets the transactions the user picked out of what the expansion held back (sketch EX2), also in
    /// <see cref="Transactions"/>; empty when none were.
    /// </summary>
    public IReadOnlyList<Txid> Chosen { get; init; } = [];

    /// <summary>
    /// Gets when the subject was first expanded, which orders the expansions, so that one expanded again keeps its
    /// place; <see langword="null"/> before it is stored.
    /// </summary>
    public DateTimeOffset? FirstExpandedUtc { get; init; }

    /// <summary>Gets what was expanded, as the case stores it: the address or the transaction.</summary>
    public string Subject =>
        Address?.Value ?? Txid?.Value ?? EthereumAddress?.Value ?? EthereumTransaction!.Value;
}
