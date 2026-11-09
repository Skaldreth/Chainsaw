using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
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
    /// Gets what each line was worth at the time, by the line's key, when Pricing has a rate for every transfer on it
    /// (sketches TK1a and KU1a: the value small under the amount); a line without is missing, and shows no value.
    /// </summary>
    public IReadOnlyDictionary<string, EdgeValue> Values { get; init; } = new Dictionary<string, EdgeValue>();

    /// <summary>
    /// Gets why a line has no value although its asset can be priced (sketch KU1a: <c>– kr</c> under the amount),
    /// by the line's key; a line that has a value, or whose asset no source prices, is missing.
    /// </summary>
    public IReadOnlyDictionary<string, EdgeValueGap> ValueGaps { get; init; } =
        new Dictionary<string, EdgeValueGap>();

    /// <summary>
    /// Gets the latest rate the case has for each asset on the graph's account chains, which values what is left at an
    /// address in the inspector (sketch TK1a); an asset without a rate is missing, and its balance shows no value.
    /// </summary>
    public IReadOnlyDictionary<Asset, Price> Rates { get; init; } = new Dictionary<Asset, Price>();

    /// <summary>
    /// Gets the addresses that are never folded into a pile (<see cref="GraphFold"/>): the watched and the watches'
    /// counterparties, so that the watch line and its rings always have a node to stand at (sketch BV2a).
    /// </summary>
    public IReadOnlySet<ChainAddress> KeptOutOfPiles { get; init; } = GraphReading.NoAddresses;

    /// <summary>
    /// Gets the case's rates for the swaps' price check, which the exposure uses as the trace did, or
    /// <see langword="null"/> when there are none.
    /// </summary>
    public SwapPricing? SwapPricing { get; init; }

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
        + MoneyText.Unit(Currency);

    /// <summary>
    /// Gets each transfer on the line valued at its own time, in time order, so that <em>Varför?</em> can show the
    /// amount, the rates and their hashes step by step (sketch KU1a); empty where only the steps are known.
    /// </summary>
    public IReadOnlyList<EdgeValuePart> Parts { get; init; } = [];
}

/// <summary>One transfer on a line valued at its time: a fact from the chain and a calculation from rates.</summary>
/// <param name="Transfer">The transfer, with its transaction, block and amount.</param>
/// <param name="Valuation">Its value with the price and the rates it rests on.</param>
public sealed record EdgeValuePart(AssetTransfer Transfer, Valuation Valuation);

/// <summary>
/// Why a line shows no value (sketch KU1a): <c>– kr</c> under the amount with a mark, and on hovering which rate is
/// missing; never a value made up.
/// </summary>
/// <param name="Currency">The currency's code, for example <c>SEK</c>.</param>
/// <param name="Tip">What is missing and why, in Swedish, for the tooltip.</param>
/// <param name="IsOffline">Whether it is missing because no exit is connected, so that the mark says offline.</param>
public sealed record EdgeValueGap(string Currency, string Tip, bool IsOffline)
{
    /// <summary>
    /// Gets whether the source can still be asked for the rate (the case only lacks it), so that the graph fetches it
    /// in the background while the line shows the clock.
    /// </summary>
    public bool IsPending { get; init; }

    /// <summary>Gets what stands under the amount, for example <c>– kr</c>.</summary>
    public string Text => "–" + MoneyText.Unit(Currency);

    /// <summary>Gets the mark's icon: offline, or the clock for a rate that is not fetched or not set yet.</summary>
    public string Icon => IsOffline ? "Icon.Offline" : "Icon.Clock";
}

/// <summary>The lines' values after the rates were asked for again (sketch KU1a).</summary>
/// <param name="Values">The values by line key.</param>
/// <param name="Gaps">Why a line has no value, by line key.</param>
/// <param name="Rates">The latest rate found per asset.</param>
public sealed record GraphRates(
    IReadOnlyDictionary<string, EdgeValue> Values,
    IReadOnlyDictionary<string, EdgeValueGap> Gaps,
    IReadOnlyDictionary<Asset, Price> Rates)
{
    /// <summary>Gets how many lines still wait for a rate the source can be asked for.</summary>
    public int Pending => Gaps.Values.Count(gap => gap.IsPending);

    /// <summary>
    /// Gets whether a source asked Chainsaw to wait while these were fetched (429), so that no more is asked now.
    /// </summary>
    public bool IsThrottled { get; init; }

    /// <summary>
    /// Gets whether a rate for a swap's time arrived that the case did not have, so that the trace is calculated again
    /// with the swaps' price check (<see cref="SwapValue.CheckPrice"/>).
    /// </summary>
    public bool SwapRatesFetched { get; init; }
}

/// <summary>How money in a currency is written in the graph.</summary>
public static class MoneyText
{
    /// <summary>Gives the unit after an amount of money: <c> kr</c> for kronor, else the currency's code.</summary>
    /// <param name="currency">The currency's code, for example <c>SEK</c> or <c>EUR</c>.</param>
    /// <returns>For example <c> kr</c> or <c> EUR</c>.</returns>
    public static string Unit(string currency) => currency == "SEK" ? " kr" : " " + currency;
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

    /// <summary>Gets how wide the node is drawn: wider when its name needs it (sketch GR5).</summary>
    public double Width { get; } = NodeWidths.For(Look, Name, IsNamed, Count);

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

    /// <summary>Gets why the line has no value, when its asset can be priced, or <see langword="null"/>.</summary>
    public EdgeValueGap? ValueGap { get; init; }

    /// <summary>Gets what stands small under the amount: the value, <c>– kr</c>, or an empty text.</summary>
    public string ValueLine => Value?.Text ?? ValueGap?.Text ?? string.Empty;

    /// <summary>Gets whether something stands small under the amount.</summary>
    public bool HasValueLine => ValueLine.Length > 0;

    /// <summary>
    /// Gets the swap's pill on the line (sketch TK1a), for example <c>₮ → Ξ</c>, when a transaction on it shows that
    /// the sender swapped what it sent for another asset; otherwise <see langword="null"/>.
    /// </summary>
    public string? Swap { get; init; }

    /// <summary>Gets the swaps the pill rests on.</summary>
    public IReadOnlyList<TokenSwap> Swaps { get; init; } = [];

    /// <summary>
    /// Gets the pill's pairs of token marks (sketch TK1a: <c>₮ → Ξ</c> as two round badges), one for each different
    /// swap on the line; empty when it carries none.
    /// </summary>
    public IReadOnlyList<SwapMarks> SwapPairs => [.. Swaps.Select(SwapMarks.Of).Distinct()];

    /// <summary>
    /// Gets the user's choices in force that the traced value does not follow through the line's swaps (sketch
    /// SW1a); empty when it follows, the default.
    /// </summary>
    public IReadOnlyList<SwapStop> SwapStops { get; init; } = [];

    /// <summary>Gets what the rule <see cref="SwapValue"/> did at the line's swaps, when traced money reached them.</summary>
    public IReadOnlyList<SwapConversion> SwapConversions { get; init; } = [];

    /// <summary>
    /// Gets whether the value does not follow through the line's swaps: the user chose so for every one of them, so
    /// that the pill shows a stop and the person mark.
    /// </summary>
    public bool IsSwapStopped => Swaps.Count > 0 && Swaps.All(swap => SwapStops.Any(stop => stop.Is(swap)));

    /// <summary>
    /// Gets the price check of the line's first swap (<see cref="SwapValue.CheckPrice"/>), or <see langword="null"/>
    /// when the trace made none.
    /// </summary>
    public SwapPriceCheck? SwapPrice { get; init; }

    /// <summary>
    /// Gets whether the line's swap has an absurd price, so that the value does not follow and there is nothing to
    /// choose: the pill is dashed in the risk's colour.
    /// </summary>
    public bool IsSwapPriceAbsurd => SwapPrice?.IsUnreasonable == true;

    /// <summary>Gets whether the line's swap price could not be tested: the pill is dashed in a quiet tone.</summary>
    public bool IsSwapPriceUntested => Swaps.Count > 0 && SwapPrice?.Verdict is null or SwapPriceVerdict.NotTested;
}

/// <summary>The marks of what was sold and what was bought in a swap, as its pill on the line shows them.</summary>
/// <param name="Sold">The sold token's mark, or its symbol when it has none.</param>
/// <param name="Bought">The bought token's mark, or its symbol when it has none.</param>
public sealed record SwapMarks(string Sold, string Bought)
{
    /// <summary>Gives a swap's marks.</summary>
    /// <param name="swap">The swap.</param>
    /// <returns>The marks.</returns>
    public static SwapMarks Of(TokenSwap swap)
    {
        ArgumentNullException.ThrowIfNull(swap);
        return new(MarkOf(swap.Sold.Asset), MarkOf(swap.Bought.Asset));
    }

    /// <summary>Gets the pill in words, for example <c>₮ → Ξ</c>.</summary>
    public string Text => $"{Sold} → {Bought}";

    /// <summary>
    /// Gives an asset's mark, or its symbol when it has none; a token Chainsaw does not know by its contract gets a
    /// question mark after the symbol, since anyone can call a token USDC (<see cref="UnknownTokenMark"/>).
    /// </summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The mark, for example <c>₮</c>, <c>WETH</c> or <c>USDC?</c>.</returns>
    public static string MarkOf(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (TokenMarks.Glyph(asset) is { Length: > 0 } glyph)
        {
            return glyph;
        }

        var known = asset.Kind != AssetKind.Token || asset == KnownTokens.EthereumWeth
            || KnownTokens.IssuerOf(asset) is not null;
        return known ? asset.Symbol : asset.Symbol + UnknownTokenMark;
    }

    /// <summary>What follows the symbol of a token Chainsaw does not know by its contract.</summary>
    public const string UnknownTokenMark = "?";
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

    /// <summary>
    /// Gets how many outputs that can never be spent and carry nothing the graph has (OP_RETURN, 0 BTC).
    /// </summary>
    public int Unspendable { get; init; }

    /// <summary>
    /// Gets whether those outputs are drawn; they are hidden by default (the user's decision 2026-11-07).
    /// </summary>
    public bool ShowsUnspendable { get; init; }

    /// <summary>Gets what the trash hides (sketch SO1a); empty when nothing.</summary>
    public TrashFilter Trash { get; init; } = TrashFilter.Empty;

    /// <summary>Gets the stubs where lines went to something in the trash.</summary>
    public IReadOnlyList<TrashStub> TrashStubs { get; init; } = [];

    /// <summary>Gets how many drawn nodes the trash hides.</summary>
    public int Trashed { get; init; }

    /// <summary>
    /// Gives the scene again with its lines' values (sketch KU1a), keeping how it draws the outputs that can never be
    /// spent and what the trash hides.
    /// </summary>
    /// <param name="rates">The values and why some lines have none.</param>
    /// <returns>The scene.</returns>
    public GraphScene WithRates(GraphRates rates)
    {
        ArgumentNullException.ThrowIfNull(rates);
        var snapshot = Snapshot with { Values = rates.Values, ValueGaps = rates.Gaps, Rates = rates.Rates };
        return Build(snapshot, ShowsUnspendable, Trash);
    }

    /// <summary>Gets how many lines wait for a rate the source can be asked for (sketch KU1a).</summary>
    public int PendingRates => Snapshot.ValueGaps.Values.Count(gap => gap.IsPending);

    /// <summary>
    /// Gets whether a swap that traced money goes through has not had its price tested, for want of a rate, so that the
    /// rates are asked for and the trace calculated again.
    /// </summary>
    public bool HasUntestedSwapPrices => Snapshot.AccountTrace?.PriceChecks
        .Any(check => check.Verdict == SwapPriceVerdict.NotTested && check.Swap.Time is not null) == true;

    /// <summary>Gives the lines' values as the scene has them, to be changed and shown again.</summary>
    public GraphRates Rates => new(Snapshot.Values, Snapshot.ValueGaps, Snapshot.Rates);

    /// <summary>Gives the scene again with what the trash hides; itself when it is the same filter.</summary>
    /// <param name="trash">The filter.</param>
    /// <returns>The scene.</returns>
    public GraphScene WithTrash(TrashFilter trash) =>
        ReferenceEquals(trash, Trash) ? this : Build(Snapshot, ShowsUnspendable, trash);

    /// <summary>Gives the pile a node stands for, when it is one.</summary>
    /// <param name="key">The node's key.</param>
    /// <returns>The pile, or <see langword="null"/>.</returns>
    public PileView? Pile(string? key) => key is not null ? Piles.GetValueOrDefault(key) : null;

    /// <summary>Gives the clue a node stands for, when it is one.</summary>
    /// <param name="key">The node's key.</param>
    /// <returns>The clue, or <see langword="null"/>.</returns>
    public HeldBackClue? Clue(string? key) => key is not null ? Clues.GetValueOrDefault(key) : null;

    /// <summary>
    /// Gives the scene again with the unspendable outputs shown or hidden; itself when it already is so.
    /// </summary>
    /// <param name="show">Whether they are shown.</param>
    /// <returns>The scene.</returns>
    public GraphScene WithUnspendable(bool show) => show == ShowsUnspendable ? this : Build(Snapshot, show, Trash);

    /// <summary>
    /// Says whether the scene is drawn the way a reading asks: the outputs that can never be spent shown or hidden, and
    /// the same addresses kept out of the piles. The method is not compared, since a new method needs a new trace.
    /// </summary>
    /// <param name="reading">The reading.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool IsDrawnAs(GraphReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return reading.ShowUnspendable == ShowsUnspendable && Snapshot.KeptOutOfPiles.SetEquals(reading.KeptOutOfPiles);
    }

    /// <summary>
    /// Gives the scene again, from the same snapshot, drawn the way a reading asks; itself when it already is. Nothing
    /// is read or traced anew, so it may be built off the UI thread from what is shown. What the trash hides stays
    /// hidden.
    /// </summary>
    /// <param name="reading">The reading.</param>
    /// <returns>The scene.</returns>
    public GraphScene DrawnAs(GraphReading reading)
    {
        if (IsDrawnAs(reading))
        {
            return this;
        }

        var snapshot = Snapshot.KeptOutOfPiles.SetEquals(reading.KeptOutOfPiles)
            ? Snapshot
            : Snapshot with { KeptOutOfPiles = reading.KeptOutOfPiles };

        // What the trash hides is not part of a reading; the scene keeps hiding it (sketch SO1a).
        return Build(snapshot, reading.ShowUnspendable, Trash);
    }

    /// <summary>Gets the method the trace was made by, as the user sees it, or the asked method's name.</summary>
    public string MethodText =>
        Snapshot.Trace?.Method.ToString() ?? Snapshot.AccountTrace?.Method.ToString() ?? MethodName(Snapshot.Method);

    /// <summary>Gives a node by its key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The node, or <see langword="null"/>.</returns>
    public NodeView? Node(string? key) => key is not null ? _nodes.GetValueOrDefault(key) : null;

    /// <summary>Gives how wide a node is drawn (sketch GR5); the graph's width for one not drawn as a node.</summary>
    /// <param name="key">The node's key.</param>
    /// <returns>The width.</returns>
    public double WidthOf(string key) => _nodes.TryGetValue(key, out var node) ? node.Width : GraphSizes.NodeWidth;

    /// <summary>Gives a node's frame at a place, as wide as the node is drawn.</summary>
    /// <param name="key">The node's key.</param>
    /// <param name="place">Its top-left corner.</param>
    /// <returns>The frame.</returns>
    public GraphRect FrameOf(string key, GraphPoint place) => GraphSizes.NodeFrame(place, WidthOf(key));

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
    /// <param name="trash">What the trash hides (sketch SO1a); <see langword="null"/> hides nothing.</param>
    /// <returns>The scene.</returns>
    public static GraphScene Build(GraphSnapshot snapshot, bool showUnspendable = false, TrashFilter? trash = null)
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
                ValueGap = snapshot.ValueGaps.GetValueOrDefault(edge.Key),
                Swaps = SwapsOn(edge, swaps),
            })
            .Select(edge => edge.Swaps.Count == 0 ? edge : WithSwapChoice(edge, graph, snapshot.AccountTrace))
            .ToList();
        // The lines are found by their ends once, so that building every node stays linear in a large graph.
        var into = edges.ToLookup(edge => edge.Edge.To, StringComparer.Ordinal);
        var outOf = edges.ToLookup(edge => edge.Edge.From, StringComparer.Ordinal);
        var fees = graph.Account.Fees.ToLookup(fee => fee.Payer.ToChainAddress());
        var nodes = graph.Nodes.Select(node => BuildNode(node, snapshot, into[node.Key], outOf[node.Key], fees))
            .ToList();
        trash ??= TrashFilter.Empty;
        var folding = GraphFold.Apply(graph, nodes, edges, showUnspendable, snapshot.KeptOutOfPiles, trash);
        var widths = folding.Nodes.ToDictionary(node => node.Key, node => node.Width, StringComparer.Ordinal);
        var (lanes, columns) = HopLayout.Arrange(
            folding.LayoutNodes, key => widths.GetValueOrDefault(key, GraphSizes.NodeWidth));
        return new GraphScene(snapshot, folding.Nodes, folding.Edges, lanes, columns)
        {
            LayoutNodes = folding.LayoutNodes,
            Piles = folding.Piles,
            Clues = folding.Clues,
            Unspendable = folding.HiddenUnspendable,
            ShowsUnspendable = showUnspendable,
            Trash = trash,
            TrashStubs = folding.Stubs,
            Trashed = folding.Trashed,
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
        // A line whose traced money rests on a swap's value following is a judgement (sketch SW1a): dashed.
        var uncertain = traced?.MayBeOutdated == true || traced?.Amount.RestsOnSwap == true
            || edge.Transfers.Any(transfer => transfer.Block is null);
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
            // A token without a mark is named by its symbol (with a question mark when its contract is not known), so
            // that a bare number never stands for an unknown asset.
            label += " " + SwapMarks.MarkOf(edge.Amount.Asset);
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
            && edge.From == AccountKey(swap.Account)
            && edge.Transfers.Any(swap.IsSold)),
    ];

    /// <summary>
    /// Gives a line that carries swaps its pill, the user's choices on them and what the rule did there (sketch SW1a).
    /// </summary>
    /// <param name="edge">The line, with its swaps.</param>
    /// <param name="graph">The graph, with the choices in force.</param>
    /// <param name="trace">The trace on Ethereum, or <see langword="null"/>.</param>
    /// <returns>The line.</returns>
    private static EdgeView WithSwapChoice(EdgeView edge, TraceGraph graph, AccountTraceResult? trace) => edge with
    {
        Swap = SwapText(edge.Swaps),
        SwapStops = [.. graph.SwapStops.Where(stop => edge.Swaps.Any(stop.Is))],
        SwapConversions = [.. edge.Swaps.Select(swap => trace?.SwapOf(swap)).OfType<SwapConversion>()],
        SwapPrice = trace?.PriceOf(edge.Swaps[0]),
    };

    /// <summary>Writes a swap's pill: the sold asset's mark, an arrow and the bought one's (<c>₮ → Ξ</c>).</summary>
    /// <param name="swaps">The swaps on the line, at least one.</param>
    /// <returns>The pill's text; several different swaps on one line are joined.</returns>
    private static string SwapText(IReadOnlyList<TokenSwap> swaps) => string.Join(" · ", swaps
        .Select(SwapMarks.Of)
        .Distinct()
        .Select(marks => marks.Text));

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
    /// <param name="into">The lines that end at the actor.</param>
    /// <param name="outOf">The lines that start at the actor.</param>
    /// <param name="fees">The fees the graph knows, by the account that paid them.</param>
    /// <returns>The actor's view.</returns>
    private static NodeView BuildNode(
        GraphNode node,
        GraphSnapshot snapshot,
        IEnumerable<EdgeView> into,
        IEnumerable<EdgeView> outOf,
        ILookup<ChainAddress, EthereumFee> fees)
    {
        var labels = snapshot.Labels.GetValueOrDefault(node.Key) ?? [];
        var incoming = into.Where(edge => edge.Edge.Kind == GraphEdgeKind.Transfer).ToList();
        var outgoing = outOf.Where(edge => edge.Edge.Kind == GraphEdgeKind.Transfer).ToList();
        var tracedIn = Sum(into.Select(edge => edge.Traced));
        var tracedIns = node.Chain == Chain.Bitcoin
            ? []
            : into.Where(edge => edge.IsTraced)
                .GroupBy(edge => edge.Traced.Asset)
                .Select(group => group.Skip(1).Aggregate(group.First().Traced, (sum, edge) => sum + edge.Traced))
                .ToList();
        var balances = node.Chain == Chain.Bitcoin
            ? []
            : Balances(node, snapshot, incoming, outgoing, [.. node.Addresses.Distinct().SelectMany(a => fees[a])]);
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
        else if (sort != NodeSort.Sanctioned && labels.Any(label => label.Kind == LabelKind.FormerlySanctioned))
        {
            // A sanction in force outweighs a former one; the former is only drawn without it.
            badges.Add(NodeBadge.FormerlySanctioned);
        }

        if (labels.Any(label => label.Kind == LabelKind.Reported))
        {
            badges.Add(NodeBadge.Reported);
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
    /// <param name="snapshot">The graph and the case's latest rate per asset, which values what is left.</param>
    /// <param name="incoming">The transfer lines into the account.</param>
    /// <param name="outgoing">The transfer lines out of the account.</param>
    /// <param name="fees">The fees the account paid.</param>
    /// <returns>The rows, the chain's own coin first, then by symbol.</returns>
    private static List<TokenBalanceRow> Balances(
        GraphNode node,
        GraphSnapshot snapshot,
        IReadOnlyList<EdgeView> incoming,
        IReadOnlyList<EdgeView> outgoing,
        IReadOnlyList<EthereumFee> fees)
    {
        var graph = snapshot.Graph;
        var assets = incoming.Concat(outgoing)
            .Select(edge => edge.Edge.Amount.Asset).Distinct()
            .OrderBy(asset => asset.Kind == AssetKind.Native ? 0 : 1)
            .ThenBy(asset => asset.Symbol, StringComparer.Ordinal)
            .ToList();
        var read = node.Addresses.All(graph.Account.ReadAccounts.Contains);
        var unknown = node.Addresses.Any(graph.Account.PartlyRead.Contains) ? PartlyReadNote : NotReadNote;
        var rows = new List<TokenBalanceRow>();
        foreach (var asset in assets)
        {
            var came = Total(asset, incoming);
            var went = Total(asset, outgoing);
            if (asset.Kind == AssetKind.Native)
            {
                went = fees.Aggregate(went, (sum, fee) => sum + fee.Amount);
            }

            var row = TokenMarks.Balance(came, went, snapshot.Rates.GetValueOrDefault(asset));
            rows.Add(read ? row : Unread(row, unknown));
        }

        if (!rows.Any(row => row.Name == Asset.Ether.Symbol) && node.Chain == Chain.Ethereum)
        {
            if (fees.Count > 0)
            {
                var paid = fees.Aggregate(AssetAmount.Zero(Asset.Ether), (sum, fee) => sum + fee.Amount);
                var row = TokenMarks.Balance(AssetAmount.Zero(Asset.Ether), paid);
                rows.Insert(0, read ? row : Unread(row, unknown));
            }
        }

        return rows;
    }

    /// <summary>
    /// Makes a row say that what is left is not known, because the account's history is not read: no balance, no
    /// value and no snowflake, which would all rest on a guess.
    /// </summary>
    /// <param name="row">The row as the lines in the graph give it.</param>
    /// <param name="note">Why it is not known.</param>
    /// <returns>The row.</returns>
    private static TokenBalanceRow Unread(TokenBalanceRow row, string note) => row with
    {
        Left = TokenMarks.UnknownLeft,
        Note = note,
        CanBeFrozen = false,
        FreezeText = null,
        IsEmpty = false,
        Value = null,
        ValueTip = null,
    };

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
    public static NodeSort SortOf(GraphNode node, IReadOnlyList<Label> labels)
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
