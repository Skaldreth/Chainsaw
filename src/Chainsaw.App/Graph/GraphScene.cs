using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>
/// What the case says about the graph, read off the UI thread: the graph from Tracing, the labels on its actors from
/// Attribution, and the trace by the chosen method.
/// </summary>
/// <param name="Graph">The graph.</param>
/// <param name="Labels">The labels on each node, by the node's key; a node without labels is missing.</param>
/// <param name="Method">The method the trace was asked for.</param>
/// <param name="Seeds">The coins the trace starts in (<see cref="SeedPicker"/>).</param>
/// <param name="Trace">The trace, or <see langword="null"/> when there was nothing to trace or it failed.</param>
/// <param name="TraceProblem">Why there is no trace, in Swedish, or <see langword="null"/>.</param>
public sealed record GraphSnapshot(
    TraceGraph Graph,
    IReadOnlyDictionary<string, IReadOnlyList<Label>> Labels,
    TracingMethodKind Method,
    IReadOnlyList<TraceSeed> Seeds,
    TraceResult? Trace,
    string? TraceProblem)
{
    /// <summary>Gets the movements the trace on Ethereum starts in (<see cref="AccountSeedPicker"/>).</summary>
    public IReadOnlyList<AccountSeed> AccountSeeds { get; init; } = [];

    /// <summary>Gets the trace on Ethereum, per asset, or <see langword="null"/> when there is none.</summary>
    public AccountTraceResult? AccountTrace { get; init; }

    /// <summary>
    /// Gets what each line on Ethereum was worth at the time, by the line's key, when Pricing has a rate for every
    /// movement on it (sketch TK1a: SEK small under the amount); a line without is missing, and shows no value.
    /// </summary>
    public IReadOnlyDictionary<string, EdgeValue> Values { get; init; } = new Dictionary<string, EdgeValue>();

    /// <summary>Gets whether any trace was counted, on any chain.</summary>
    public bool HasTrace => Trace is not null || AccountTrace is not null;
}

/// <summary>What a line was worth when its movements were made, by the rates in the case.</summary>
/// <param name="Rounded">The value, rounded to whole units of the currency.</param>
/// <param name="Currency">The currency's code, for example <c>SEK</c>.</param>
/// <param name="Steps">How it was counted, rate by rate, for <em>Varför?</em>.</param>
public sealed record EdgeValue(decimal Rounded, string Currency, IReadOnlyList<string> Steps)
{
    /// <summary>Gets the value as the line writes it small under the amount, for example <c>≈ 546 000 kr</c>.</summary>
    public string Text => "≈ " + Rounded.ToString("#,##0", System.Globalization.CultureInfo.GetCultureInfo("sv-SE"))
        + (Currency == "SEK" ? " kr" : " " + Currency);
}

/// <summary>How a line is dashed (sketch GR3a): dashed always means uncertain.</summary>
public enum EdgeDash
{
    /// <summary>A whole line: a confirmed transfer.</summary>
    None,

    /// <summary>Short dashes: something it rests on may change, for example an unconfirmed transaction.</summary>
    Uncertain,

    /// <summary>Long dashes: the money continues on another chain, by the user's link.</summary>
    OtherChain,
}

/// <summary>An actor as the graph draws it.</summary>
/// <param name="Node">The actor in Tracing's model.</param>
/// <param name="Look">Its sort and how that is drawn; a start point keeps its own sort (sketch GR5).</param>
/// <param name="Name">Its name: the label's name, or the address, tx or coin shortened.</param>
/// <param name="Count">How many addresses its cluster has, when more than one; otherwise empty.</param>
/// <param name="Badges">The marks in its corner.</param>
/// <param name="IsDim">
/// Whether it is drawn faint, because it is not expanded (sketch GR3a, <em>Ej expanderad</em>).
/// </param>
/// <param name="ChainName">The other chain's short name under its corner, or <see langword="null"/>.</param>
/// <param name="Labels">The labels on it, the best supported first.</param>
/// <param name="In">What came in on the chain along the graph's lines (fact).</param>
/// <param name="Out">What went out on the chain along the graph's lines (fact).</param>
/// <param name="TracedIn">How much of the traced money came in, by the method (conclusion).</param>
/// <param name="Subject">
/// What the node is, written out in full: its first address, the txid, the coin or the other chain's id. The graph
/// writes it under a name in dimmed grey, or as the title of a node without a name, shortened only when it does not
/// fit (CLAUDE.md, <em>Nodernas namn</em>).
/// </param>
/// <param name="IsNamed">Whether a label gives the node a name (a label's or the user's own name).</param>
public sealed record NodeView(
    GraphNode Node,
    SortLook Look,
    string Name,
    string Count,
    IReadOnlyList<NodeBadge> Badges,
    bool IsDim,
    string? ChainName,
    IReadOnlyList<Label> Labels,
    AssetAmount In,
    AssetAmount Out,
    AssetAmount TracedIn,
    string Subject,
    bool IsNamed)
{
    /// <summary>Gets the node's key.</summary>
    public string Key => Node.Key;

    /// <summary>
    /// Gets the balance per token on an account chain (sketch TK1a): in, out and what is left of each asset along the
    /// graph's lines, with its issuer and whether it can be frozen; empty on Bitcoin.
    /// </summary>
    public IReadOnlyList<TokenBalanceRow> Balances { get; init; } = [];

    /// <summary>Gets how much of the traced money came in, per asset, on an account chain (conclusion).</summary>
    public IReadOnlyList<AssetAmount> TracedIns { get; init; } = [];

    /// <summary>Gets whether the trace starts here, drawn as the flag in the top-left corner (sketch GR5).</summary>
    public bool IsStartPoint => Node.IsStartPoint;

    /// <summary>Gets the tooltip: the sort, the marks and how far it is expanded.</summary>
    public string ToolTip
    {
        get
        {
            var parts = new List<string> { Look.Name };
            if (IsStartPoint)
            {
                parts.Add(VisualLanguage.Of(NodeBadge.StartPoint).Name);
            }

            parts.AddRange(Badges.Select(badge => VisualLanguage.Of(badge).Name));
            if (IsDim)
            {
                parts.Add(Node.Expansion == NodeExpansion.Partly ? "Delvis expanderad" : "Inte expanderad");
            }

            return string.Join(" · ", parts);
        }
    }
}

/// <summary>A line as the graph draws it.</summary>
/// <param name="Edge">The line in Tracing's model.</param>
/// <param name="Traced">How much of the traced money it carries, by the method (conclusion); zero when none.</param>
/// <param name="TracedEdge">The trace's own record of it, or <see langword="null"/> when it carries none.</param>
/// <param name="Ink">Its colour.</param>
/// <param name="Dash">How it is dashed.</param>
/// <param name="Width">Its thickness, from the traced amount.</param>
/// <param name="Label">What it carries, as written on it.</param>
public sealed record EdgeView(
    GraphEdge Edge,
    AssetAmount Traced,
    TracedEdge? TracedEdge,
    GraphInk Ink,
    EdgeDash Dash,
    double Width,
    string Label)
{
    /// <summary>Gets the line's key.</summary>
    public string Key => Edge.Key;

    /// <summary>Gets whether the line carries traced money.</summary>
    public bool IsTraced => Traced.Units.Sign > 0;

    /// <summary>Gets the token's mark that stands first in the amount (sketch TK1a), or an empty text.</summary>
    public string Glyph { get; init; } = string.Empty;

    /// <summary>Gets what the line was worth at the time, small under the amount, or <see langword="null"/>.</summary>
    public EdgeValue? Value { get; init; }

    /// <summary>
    /// Gets the swap's pill on the line (sketch TK1a), for example <c>₮ → Ξ</c>, when a transaction on it shows that
    /// the sender swapped what it sent for another asset; otherwise <see langword="null"/>.
    /// </summary>
    public string? Swap { get; init; }

    /// <summary>Gets the swaps the pill rests on.</summary>
    public IReadOnlyList<TokenSwap> Swaps { get; init; } = [];
}

/// <summary>
/// The graph ready to draw: every actor and line with its look (sketches GR3a and GR3c), and the hop columns. It is
/// built off the UI thread from a <see cref="GraphSnapshot"/>, and holds no places, which the view model adds.
/// </summary>
/// <param name="Snapshot">What it was built from.</param>
/// <param name="Nodes">The actors, in the graph's order.</param>
/// <param name="Edges">The lines.</param>
/// <param name="Lanes">The hop columns.</param>
/// <param name="Columns">Each actor's place in the hop columns.</param>
public sealed record GraphScene(
    GraphSnapshot Snapshot,
    IReadOnlyList<NodeView> Nodes,
    IReadOnlyList<EdgeView> Edges,
    IReadOnlyList<HopLane> Lanes,
    IReadOnlyDictionary<string, GraphPoint> Columns)
{
    /// <summary>How many characters of a long name are kept at each end when it is shortened.</summary>
    private const int ShortEnd = 4;

    /// <summary>The nodes by key.</summary>
    private readonly Dictionary<string, NodeView> _nodes =
        Nodes.ToDictionary(node => node.Key, StringComparer.Ordinal);

    /// <summary>The lines by key.</summary>
    private readonly Dictionary<string, EdgeView> _edges =
        Edges.ToDictionary(edge => edge.Key, StringComparer.Ordinal);

    /// <summary>The model's nodes the layout places, when set; otherwise those of the drawn nodes.</summary>
    private readonly IReadOnlyList<GraphNode>? _layoutNodes;

    /// <summary>Gets the model's nodes the layout places: the actors drawn, and the piles' and clues' nodes.</summary>
    public IReadOnlyList<GraphNode> LayoutNodes
    {
        get => _layoutNodes ?? [.. Nodes.Select(node => node.Node)];
        init => _layoutNodes = value;
    }

    /// <summary>Gets the piles drawn (sketch EX2b), by key.</summary>
    public IReadOnlyDictionary<string, PileView> Piles { get; init; } = new Dictionary<string, PileView>();

    /// <summary>Gets the clues drawn (sketch EX1), by key.</summary>
    public IReadOnlyDictionary<string, HeldBackClue> Clues { get; init; } = new Dictionary<string, HeldBackClue>();

    /// <summary>Gets how many outputs that can never be spent and carry nothing the graph has (OP_RETURN, 0 BTC).</summary>
    public int Unspendable { get; init; }

    /// <summary>Gets whether those outputs are drawn; they are hidden by default (the user's decision 2026-11-07).</summary>
    public bool ShowsUnspendable { get; init; }

    /// <summary>Gives the pile a node stands for, when it is one.</summary>
    /// <param name="key">The node's key.</param>
    /// <returns>The pile, or <see langword="null"/>.</returns>
    public PileView? Pile(string? key) => key is not null ? Piles.GetValueOrDefault(key) : null;

    /// <summary>Gives the clue a node stands for, when it is one.</summary>
    /// <param name="key">The node's key.</param>
    /// <returns>The clue, or <see langword="null"/>.</returns>
    public HeldBackClue? Clue(string? key) => key is not null ? Clues.GetValueOrDefault(key) : null;

    /// <summary>Gives the scene again with the unspendable outputs shown or hidden; itself when it already is so.</summary>
    /// <param name="show">Whether they are shown.</param>
    /// <returns>The scene.</returns>
    public GraphScene WithUnspendable(bool show) => show == ShowsUnspendable ? this : Build(Snapshot, show);

    /// <summary>Gets the method the trace was made by, as the user sees it, or the asked method's name.</summary>
    public string MethodText =>
        Snapshot.Trace?.Method.ToString() ?? Snapshot.AccountTrace?.Method.ToString() ?? MethodName(Snapshot.Method);

    /// <summary>Gives a node by its key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The node, or <see langword="null"/>.</returns>
    public NodeView? Node(string? key) => key is not null ? _nodes.GetValueOrDefault(key) : null;

    /// <summary>Gives a line by its key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The line, or <see langword="null"/>.</returns>
    public EdgeView? Edge(string? key) => key is not null ? _edges.GetValueOrDefault(key) : null;

    /// <summary>Gives a method's name as the button shows it.</summary>
    /// <param name="method">The method.</param>
    /// <returns><c>FIFO</c> or <c>haircut</c>.</returns>
    public static string MethodName(TracingMethodKind method) => new TracingMethodId(method, 1).Name;

    /// <summary>
    /// Gets the asset whose sums the actors show and whose traced amounts set the lines' thickness: Bitcoin's coin,
    /// the only chain the graph has. How tokens are shown is sketched before the first account chain is built.
    /// </summary>
    public static Asset Shown { get; } = Chain.Bitcoin.NativeAsset();

    /// <summary>
    /// Builds the scene from a snapshot, folded (<see cref="GraphFold"/>): the piles and clues drawn, and the outputs
    /// that can never be spent hidden unless asked for.
    /// </summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="showUnspendable">Whether the outputs that can never be spent and carry nothing are drawn.</param>
    /// <returns>The scene.</returns>
    public static GraphScene Build(GraphSnapshot snapshot, bool showUnspendable = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var graph = snapshot.Graph;
        var traced = (snapshot.Trace?.Edges ?? []).Concat(snapshot.AccountTrace?.Edges ?? [])
            .ToDictionary(edge => edge.EdgeKey, StringComparer.Ordinal);
        var largest = traced.Values.Select(edge => edge.Amount.Amount)
            .GroupBy(amount => amount.Asset)
            .ToDictionary(group => group.Key, group => group.Max());
        var swaps = graph.Account.Swaps;
        var edges = graph.Edges.Select(edge => BuildEdge(
                edge,
                traced.GetValueOrDefault(edge.Key),
                largest.TryGetValue(edge.Amount.Asset, out var most) ? most : AssetAmount.Zero(edge.Amount.Asset)) with
            {
                Glyph = TokenMarks.Glyph(edge.Amount.Asset),
                Value = snapshot.Values.GetValueOrDefault(edge.Key),
                Swaps = SwapsOn(edge, swaps),
            })
            .Select(edge => edge.Swaps.Count == 0 ? edge : edge with { Swap = SwapText(edge.Swaps) })
            .ToList();
        var nodes = graph.Nodes.Select(node => BuildNode(node, snapshot, edges)).ToList();
        var folding = GraphFold.Apply(graph, nodes, edges, showUnspendable);
        var (lanes, columns) = HopLayout.Arrange(folding.LayoutNodes);
        return new GraphScene(snapshot, folding.Nodes, folding.Edges, lanes, columns)
        {
            LayoutNodes = folding.LayoutNodes,
            Piles = folding.Piles,
            Clues = folding.Clues,
            Unspendable = folding.HiddenUnspendable,
            ShowsUnspendable = showUnspendable,
        };
    }

    /// <summary>
    /// Gives what is near what is chosen (sketch GR4): a node, its lines and the nodes at their other ends; or a line
    /// and its two nodes. Everything else is drawn faint.
    /// </summary>
    /// <param name="nodeKey">The chosen node, or <see langword="null"/>.</param>
    /// <param name="edgeKey">The chosen line, or <see langword="null"/>.</param>
    /// <returns>The near nodes and lines; empty when nothing is chosen.</returns>
    public (IReadOnlySet<string> Nodes, IReadOnlySet<string> Edges) Near(string? nodeKey, string? edgeKey)
    {
        var nodes = new HashSet<string>(StringComparer.Ordinal);
        var edges = new HashSet<string>(StringComparer.Ordinal);
        if (Node(nodeKey) is { } node)
        {
            nodes.Add(node.Key);
            foreach (var edge in Edges.Where(edge => edge.Edge.From == node.Key || edge.Edge.To == node.Key))
            {
                edges.Add(edge.Key);
                nodes.Add(edge.Edge.From);
                nodes.Add(edge.Edge.To);
            }
        }
        else if (Edge(edgeKey) is { } edge)
        {
            edges.Add(edge.Key);
            nodes.Add(edge.Edge.From);
            nodes.Add(edge.Edge.To);
        }

        return (nodes, edges);
    }

    /// <summary>Shortens an Ethereum address so that <c>0x</c> and four digits stay: <c>0x8f3a…e5d9</c>.</summary>
    /// <param name="text">The address.</param>
    /// <returns>The short name.</returns>
    private static string ShortAccount(string text) => text.StartsWith("0x", StringComparison.Ordinal)
        ? string.Concat("0x", Shorten(text[2..]))
        : Shorten(text);

    /// <summary>Shortens a long name to its ends, as the graph writes addresses and txids: <c>bc1q…x0wl</c>.</summary>
    /// <param name="text">The name.</param>
    /// <returns>The short name; a short one unchanged.</returns>
    public static string Shorten(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length <= (2 * ShortEnd) + 2
            ? text
            : string.Concat(text.AsSpan(0, ShortEnd), "…", text.AsSpan(text.Length - ShortEnd));
    }

    /// <summary>Gives a chain's short name, as under a node on another chain.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>For example <c>ETH</c> or <c>TRON</c>.</returns>
    public static string ChainShortName(Chain chain) => chain switch
    {
        Chain.Bitcoin => "BTC",
        Chain.Ethereum => "ETH",
        Chain.Tron => "TRON",
        _ => chain.ToString().ToUpperInvariant(),
    };

    /// <summary>Builds how a line is drawn.</summary>
    /// <param name="edge">The line.</param>
    /// <param name="traced">The trace's record of it, or <see langword="null"/>.</param>
    /// <param name="largest">The largest traced amount on any line.</param>
    /// <returns>The line's view.</returns>
    private static EdgeView BuildEdge(GraphEdge edge, TracedEdge? traced, AssetAmount largest)
    {
        var amount = traced?.Amount.Amount ?? AssetAmount.Zero(edge.Amount.Asset);
        var otherChain = edge.Kind == GraphEdgeKind.ContinuesOnOtherChain;
        var uncertain = traced?.MayBeOutdated == true || edge.Transfers.Any(transfer => transfer.Block is null);
        var dash = (otherChain, uncertain) switch
        {
            (true, _) => EdgeDash.OtherChain,
            (_, true) => EdgeDash.Uncertain,
            _ => EdgeDash.None,
        };
        var ink = (otherChain, amount.Units.Sign > 0) switch
        {
            (true, _) => GraphInk.OtherChain,
            (_, true) => GraphInk.Accent,
            _ => GraphInk.Untraced,
        };
        var label = AmountText.OnLine(edge.Amount, amount);
        if (edge.Amount.Asset != Asset.Bitcoin && TokenMarks.Glyph(edge.Amount.Asset).Length == 0)
        {
            // A token without a mark is named by its symbol, so that a bare number never stands for an unknown asset.
            label += " " + edge.Amount.Asset.Symbol;
        }

        return new EdgeView(edge, amount, traced, ink, dash, EdgeWidth.For(amount, largest), label);
    }

    /// <summary>
    /// Gives the swaps a line carries the sold side of: the line leaves the account that swapped, in the asset it sold,
    /// in the swap's transaction.
    /// </summary>
    /// <param name="edge">The line.</param>
    /// <param name="swaps">The graph's swaps.</param>
    /// <returns>The swaps; empty for most lines.</returns>
    private static IReadOnlyList<TokenSwap> SwapsOn(GraphEdge edge, IReadOnlyList<TokenSwap> swaps) =>
    [
        .. swaps.Where(swap => edge.Kind == GraphEdgeKind.Transfer
            && edge.Amount.Asset == swap.Sold.Asset
            && edge.From == AccountKey(swap.Account)
            && edge.Transfers.Any(transfer => transfer.Transaction.Id == swap.Transaction.Value)),
    ];

    /// <summary>Writes a swap's pill: the sold asset's mark, an arrow and the bought one's (<c>₮ → Ξ</c>).</summary>
    /// <param name="swaps">The swaps on the line, at least one.</param>
    /// <returns>The pill's text; several different swaps on one line are joined.</returns>
    private static string SwapText(IReadOnlyList<TokenSwap> swaps) => string.Join(" · ", swaps
        .Select(swap => $"{MarkOf(swap.Sold.Asset)} → {MarkOf(swap.Bought.Asset)}")
        .Distinct(StringComparer.Ordinal));

    /// <summary>Gives an asset's mark, or its symbol when it has none.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The mark.</returns>
    private static string MarkOf(Asset asset) =>
        TokenMarks.Glyph(asset) is { Length: > 0 } glyph ? glyph : asset.Symbol;

    /// <summary>Gives the key of an account's node, as Tracing writes it.</summary>
    /// <param name="account">The account.</param>
    /// <returns>The key.</returns>
    public static string AccountKey(ChainAddress account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return "account:" + account.Chain + ":" + account.Value;
    }

    /// <summary>Builds how an actor is drawn.</summary>
    /// <param name="node">The actor.</param>
    /// <param name="snapshot">The snapshot, for its labels.</param>
    /// <param name="edges">The lines, for what came in and went out.</param>
    /// <returns>The actor's view.</returns>
    private static NodeView BuildNode(GraphNode node, GraphSnapshot snapshot, IReadOnlyList<EdgeView> edges)
    {
        var labels = snapshot.Labels.GetValueOrDefault(node.Key) ?? [];
        var incoming = edges.Where(edge => edge.Edge.To == node.Key && edge.Edge.Kind == GraphEdgeKind.Transfer);
        var outgoing = edges.Where(edge => edge.Edge.From == node.Key && edge.Edge.Kind == GraphEdgeKind.Transfer);
        var tracedIn = Sum(edges.Where(edge => edge.Edge.To == node.Key).Select(edge => edge.Traced));
        var tracedIns = node.Chain == Chain.Bitcoin
            ? []
            : edges.Where(edge => edge.Edge.To == node.Key && edge.IsTraced)
                .GroupBy(edge => edge.Traced.Asset)
                .Select(group => group.Skip(1).Aggregate(group.First().Traced, (sum, edge) => sum + edge.Traced))
                .ToList();
        var balances = node.Chain == Chain.Bitcoin ? [] : Balances(node, snapshot.Graph, edges);
        var sort = SortOf(node, labels);
        var primary = labels.FirstOrDefault(label => VisualLanguage.SortOf(label.Kind) == sort);
        var badges = new List<NodeBadge>();
        if (VisualLanguage.IsCashOut(sort) && (tracedIn.Units.Sign > 0 || tracedIns.Count > 0))
        {
            badges.Add(NodeBadge.CashOutFound);
        }

        if (primary?.IsOnlyFromOtherCases == true)
        {
            badges.Add(NodeBadge.FromOtherCase);
        }

        if (sort != NodeSort.Sanctioned && labels.Any(label => label.Kind == LabelKind.Sanctioned))
        {
            badges.Add(NodeBadge.Sanctioned);
        }

        if (balances.Any(row => row.CanBeFrozen))
        {
            badges.Add(NodeBadge.CanBeFrozen);
        }

        var expandable = node.Kind is GraphNodeKind.Address or GraphNodeKind.Cluster or GraphNodeKind.Transaction;
        var name = primary?.Name ?? labels.FirstOrDefault(label => label.Name is not null)?.Name;
        return new NodeView(
            node,
            VisualLanguage.Of(sort),
            name ?? NameOf(node),
            node.ClusterSize > 1 ? node.ClusterSize.ToString(System.Globalization.CultureInfo.InvariantCulture) : "",
            badges,
            expandable && node.Expansion != NodeExpansion.Expanded,
            node.OtherChain is { } other ? ChainShortName(other.Chain) : null,
            labels,
            Sum(incoming.Select(edge => edge.Edge.Amount)),
            Sum(outgoing.Select(edge => edge.Edge.Amount)),
            tracedIn,
            SubjectOf(node),
            name is not null)
        {
            Balances = balances,
            TracedIns = tracedIns,
        };
    }

    /// <summary>What a balance row says when the account's history is not read, so nothing is known to be left.</summary>
    public const string NotReadNote = "Inte expanderad: saldot är okänt";

    /// <summary>What a balance row says when the account's history is read only in part.</summary>
    public const string PartlyReadNote = "Delvis expanderad: saldot är okänt";

    /// <summary>
    /// Gives an account's balance per asset along the graph's lines (sketch TK1a), with the fees it paid counted out
    /// of its ether. What is left is only known when the account's history is read (it is expanded); otherwise the
    /// row says so and no snowflake is set, since it would rest on a guess.
    /// </summary>
    /// <param name="node">The account's node.</param>
    /// <param name="graph">The graph.</param>
    /// <param name="edges">The lines.</param>
    /// <returns>The rows, the chain's own coin first, then by symbol.</returns>
    private static List<TokenBalanceRow> Balances(GraphNode node, TraceGraph graph, IReadOnlyList<EdgeView> edges)
    {
        var lines = edges.Where(edge => edge.Edge.Kind == GraphEdgeKind.Transfer).ToList();
        var assets = lines.Where(edge => edge.Edge.From == node.Key || edge.Edge.To == node.Key)
            .Select(edge => edge.Edge.Amount.Asset).Distinct()
            .OrderBy(asset => asset.Kind == AssetKind.Native ? 0 : 1)
            .ThenBy(asset => asset.Symbol, StringComparer.Ordinal)
            .ToList();
        var read = node.Addresses.All(graph.Account.ReadAccounts.Contains);
        var unknown = node.Addresses.Any(graph.Account.PartlyRead.Contains) ? PartlyReadNote : NotReadNote;
        var rows = new List<TokenBalanceRow>();
        foreach (var asset in assets)
        {
            var incoming = Total(asset, lines.Where(edge => edge.Edge.To == node.Key));
            var outgoing = Total(asset, lines.Where(edge => edge.Edge.From == node.Key));
            if (asset.Kind == AssetKind.Native)
            {
                outgoing = graph.Account.Fees.Where(fee => node.Addresses.Contains(fee.Payer.ToChainAddress()))
                    .Aggregate(outgoing, (sum, fee) => sum + fee.Amount);
            }

            var row = TokenMarks.Balance(incoming, outgoing);
            rows.Add(read ? row : row with { Left = "–", Note = unknown, CanBeFrozen = false, FreezeText = null });
        }

        if (!rows.Any(row => row.Name == Asset.Ether.Symbol) && node.Chain == Chain.Ethereum)
        {
            var fees = graph.Account.Fees.Where(fee => node.Addresses.Contains(fee.Payer.ToChainAddress())).ToList();
            if (fees.Count > 0)
            {
                var paid = fees.Aggregate(AssetAmount.Zero(Asset.Ether), (sum, fee) => sum + fee.Amount);
                var row = TokenMarks.Balance(AssetAmount.Zero(Asset.Ether), paid);
                rows.Insert(0, read ? row : row with { Left = "–", Note = unknown });
            }
        }

        return rows;
    }

    /// <summary>Adds the amounts of some lines in one asset.</summary>
    /// <param name="asset">The asset.</param>
    /// <param name="lines">The lines.</param>
    /// <returns>The sum.</returns>
    private static AssetAmount Total(Asset asset, IEnumerable<EdgeView> lines) => lines
        .Where(edge => edge.Edge.Amount.Asset == asset)
        .Aggregate(AssetAmount.Zero(asset), (sum, edge) => sum + edge.Edge.Amount);

    /// <summary>
    /// Gives an actor's sort: its kind in the model, then its labels (a sanction is a mark when another label says
    /// more). Whether the trace starts there never changes it (sketch GR5): the start point is a flag, and the victim
    /// a label.
    /// </summary>
    /// <param name="node">The actor.</param>
    /// <param name="labels">Its labels, the best supported first.</param>
    /// <returns>The sort.</returns>
    private static NodeSort SortOf(GraphNode node, IReadOnlyList<Label> labels)
    {
        switch (node.Kind)
        {
            case GraphNodeKind.OtherChain:
                return NodeSort.OtherChainTransaction;
            case GraphNodeKind.Coin:
                return NodeSort.Coin;
            case GraphNodeKind.Transaction:
                return node.Ends.Any(end => end.Reason == TrailEndReason.CoinJoin)
                    ? NodeSort.CoinJoin
                    : NodeSort.SharedTransaction;
            default:
                break;
        }

        var labelled = labels
            .Select(label => VisualLanguage.SortOf(label.Kind))
            .Where(sort => sort is not NodeSort.Unknown)
            .ToList();
        // A service says most about the money; a sanction is then a mark, and a victim label yields to a service.
        var said = labelled.FirstOrDefault(
            sort => sort is not (NodeSort.Sanctioned or NodeSort.Victim), NodeSort.Unknown);
        if (said != NodeSort.Unknown)
        {
            return said;
        }

        if (labelled.Contains(NodeSort.Sanctioned))
        {
            return NodeSort.Sanctioned;
        }

        if (labelled.Contains(NodeSort.Victim))
        {
            return NodeSort.Victim;
        }

        return node.Kind == GraphNodeKind.Cluster ? NodeSort.Cluster : NodeSort.Unknown;
    }

    /// <summary>Gives what an actor is, written out in full: its first address, the txid, the coin or the id.</summary>
    /// <param name="node">The actor.</param>
    /// <returns>The text; the node's key when it has nothing else.</returns>
    private static string SubjectOf(GraphNode node) => node.Kind switch
    {
        GraphNodeKind.Transaction when node.Transaction is { } txid => txid.Value,
        GraphNodeKind.Coin when node.Coin is { } coin =>
            coin.Txid.Value + ":" + coin.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        GraphNodeKind.OtherChain when node.OtherChain is { } other => other.Id,
        _ when node.GraphAddresses.Count > 0 => node.GraphAddresses[0].Value,
        _ when node.Accounts.Count > 0 => Written(node.Accounts[0]),
        _ => node.Key,
    };

    /// <summary>
    /// Writes an address as its chain's tools do: an Ethereum address with its EIP-55 checksum, others as they are.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>The text.</returns>
    public static string Written(ChainAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.Chain == Chain.Ethereum
            && Chainsaw.Modules.Chains.Contracts.EthereumAddress.TryParse(address.Value, out var ethereum)
            ? ethereum.Checksummed
            : address.Value;
    }

    /// <summary>Gives an actor's name when no label names it: what it is, shortened.</summary>
    /// <param name="node">The actor.</param>
    /// <returns>The name.</returns>
    private static string NameOf(GraphNode node) => node.Kind switch
    {
        GraphNodeKind.Transaction when node.Transaction is { } txid => Shorten(txid.Value),
        GraphNodeKind.Coin when node.Coin is { } coin =>
            Shorten(coin.Txid.Value) + ":" + coin.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        GraphNodeKind.OtherChain when node.OtherChain is { } other => Shorten(other.Id),
        _ when node.GraphAddresses.Count > 0 => Shorten(node.GraphAddresses[0].Value),
        _ when node.Accounts.Count > 0 => ShortAccount(Written(node.Accounts[0])),
        _ => Shorten(node.Key),
    };

    /// <summary>
    /// Adds the amounts of the asset the graph shows (<see cref="Shown"/>); others wait until tokens are sketched.
    /// </summary>
    /// <param name="amounts">The amounts.</param>
    /// <returns>The sum.</returns>
    private static AssetAmount Sum(IEnumerable<AssetAmount> amounts) => amounts
        .Where(amount => amount.Asset == Shown)
        .Aggregate(AssetAmount.Zero(Shown), (sum, amount) => sum + amount);
}
