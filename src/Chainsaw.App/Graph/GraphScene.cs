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
    string? TraceProblem);

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
/// <param name="Look">Its sort and how that is drawn.</param>
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
public sealed record NodeView(
    GraphNode Node,
    SortLook Look,
    string Name,
    string Count,
    IReadOnlyList<NodeBadge> Badges,
    bool IsDim,
    string? ChainName,
    IReadOnlyList<Label> Labels,
    Satoshi In,
    Satoshi Out,
    Satoshi TracedIn)
{
    /// <summary>Gets the node's key.</summary>
    public string Key => Node.Key;

    /// <summary>Gets the tooltip: the sort, the marks and how far it is expanded.</summary>
    public string ToolTip
    {
        get
        {
            var parts = new List<string> { Look.Name };
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
    Satoshi Traced,
    TracedEdge? TracedEdge,
    GraphInk Ink,
    EdgeDash Dash,
    double Width,
    string Label)
{
    /// <summary>Gets the line's key.</summary>
    public string Key => Edge.Key;

    /// <summary>Gets whether the line carries traced money.</summary>
    public bool IsTraced => Traced.Value > 0;
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

    /// <summary>Gets the method the trace was made by, as the user sees it, or the asked method's name.</summary>
    public string MethodText => Snapshot.Trace?.Method.ToString() ?? MethodName(Snapshot.Method);

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

    /// <summary>Builds the scene from a snapshot.</summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <returns>The scene.</returns>
    public static GraphScene Build(GraphSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var graph = snapshot.Graph;
        var traced = (snapshot.Trace?.Edges ?? []).ToDictionary(edge => edge.EdgeKey, StringComparer.Ordinal);
        var largest = traced.Values.Select(edge => edge.Amount.Amount).DefaultIfEmpty(Satoshi.Zero).Max();
        var edges = graph.Edges.Select(edge => BuildEdge(edge, traced.GetValueOrDefault(edge.Key), largest)).ToList();
        var nodes = graph.Nodes.Select(node => BuildNode(node, snapshot, edges)).ToList();
        var (lanes, columns) = HopLayout.Arrange(graph.Nodes);
        return new GraphScene(snapshot, nodes, edges, lanes, columns);
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
    private static EdgeView BuildEdge(GraphEdge edge, TracedEdge? traced, Satoshi largest)
    {
        var amount = traced?.Amount.Amount ?? Satoshi.Zero;
        var otherChain = edge.Kind == GraphEdgeKind.ContinuesOnOtherChain;
        var uncertain = traced?.MayBeOutdated == true || edge.Transfers.Any(transfer => transfer.Block is null);
        var dash = otherChain ? EdgeDash.OtherChain : uncertain ? EdgeDash.Uncertain : EdgeDash.None;
        var ink = otherChain ? GraphInk.OtherChain : amount.Value > 0 ? GraphInk.Accent : GraphInk.Untraced;
        return new EdgeView(
            edge, amount, traced, ink, dash, EdgeWidth.For(amount, largest), AmountText.OnLine(edge.Amount, amount));
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
        var sort = SortOf(node, labels);
        var primary = labels.FirstOrDefault(label => VisualLanguage.SortOf(label.Kind) == sort);
        var badges = new List<NodeBadge>();
        if (VisualLanguage.IsCashOut(sort) && tracedIn.Value > 0)
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

        var expandable = node.Kind is GraphNodeKind.Address or GraphNodeKind.Cluster or GraphNodeKind.Transaction;
        return new NodeView(
            node,
            VisualLanguage.Of(sort),
            primary?.Name ?? labels.FirstOrDefault(label => label.Name is not null)?.Name ?? NameOf(node),
            node.ClusterSize > 1 ? node.ClusterSize.ToString(System.Globalization.CultureInfo.InvariantCulture) : "",
            badges,
            expandable && node.Expansion != NodeExpansion.Expanded,
            node.OtherChain is { } other ? ChainShortName(other.Chain) : null,
            labels,
            Sum(incoming.Select(edge => edge.Edge.Amount)),
            Sum(outgoing.Select(edge => edge.Edge.Amount)),
            tracedIn);
    }

    /// <summary>
    /// Gives an actor's sort: its kind in the model, then its labels (a sanction is a mark when another label says
    /// more), then whether the trace starts there.
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
        var said = labelled.FirstOrDefault(sort => sort != NodeSort.Sanctioned, NodeSort.Unknown);
        if (said != NodeSort.Unknown)
        {
            return said;
        }

        if (labelled.Contains(NodeSort.Sanctioned))
        {
            return NodeSort.Sanctioned;
        }

        if (node.IsStartPoint)
        {
            return NodeSort.Victim;
        }

        return node.Kind == GraphNodeKind.Cluster ? NodeSort.Cluster : NodeSort.Unknown;
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
        _ => Shorten(node.Key),
    };

    /// <summary>Adds amounts.</summary>
    /// <param name="amounts">The amounts.</param>
    /// <returns>The sum.</returns>
    private static Satoshi Sum(IEnumerable<Satoshi> amounts) => amounts.Aggregate(Satoshi.Zero, (sum, a) => sum + a);
}
