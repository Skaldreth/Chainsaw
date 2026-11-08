using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>
/// A card in a pile (sketch EX2b): an actor that lies in it, and what moved between it and the pile's actor.
/// </summary>
/// <param name="Node">The actor, as the graph would draw it.</param>
/// <param name="Amount">
/// What moved straight between the actor and the pile's actor, in the pile's asset (a fact); zero when the actor only
/// took part in the same transactions, as a payer's change does.
/// </param>
/// <param name="Time">When money first moved between them, or <see langword="null"/> while unconfirmed.</param>
public sealed record PileCard(NodeView Node, AssetAmount Amount, DateTimeOffset? Time)
{
    /// <summary>Gets the actor's key.</summary>
    public string Key => Node.Key;

    /// <summary>Gets the transactions that moved money between it and the pile's actor, by id.</summary>
    public IReadOnlySet<string> TransactionIds { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Gets the card's second line (sketch EX2b): <c>0,038 BTC · 1 tx</c>.</summary>
    public string Line => string.Create(
        CultureInfo.InvariantCulture, $"{GraphFold.PileAmount(Amount)} · {TransactionIds.Count} tx");
}

/// <summary>
/// A pile as the graph draws it (sketch EX2b): the pile, its cards and what moved between it and its actor.
/// </summary>
/// <param name="Pile">The pile in Tracing's model.</param>
/// <param name="Cards">The actors that lie in it, in the graph's order.</param>
/// <param name="Sum">
/// What moved between the pile and its actor, in the pile's asset: bitcoin on Bitcoin, and on an account chain the
/// asset most of the pile's transfers move.
/// </param>
public sealed record PileView(GraphPile Pile, IReadOnlyList<PileCard> Cards, AssetAmount Sum)
{
    /// <summary>Gets the pile's key.</summary>
    public string Key => Pile.Key;
}

/// <summary>What the graph draws after folding: the nodes, the lines and the piles and clues among them.</summary>
/// <param name="Nodes">The nodes drawn: the actors not hidden, and a node per pile and clue.</param>
/// <param name="Edges">The lines drawn, the lines of a pile's cards summed into one per pile.</param>
/// <param name="LayoutNodes">The model's nodes for the layout, the piles' and clues' among them.</param>
/// <param name="Piles">The piles drawn, by key.</param>
/// <param name="Clues">The clues drawn, by key.</param>
/// <param name="HiddenUnspendable">How many outputs that can never be spent and carry nothing are hidden.</param>
public sealed record GraphFolding(
    IReadOnlyList<NodeView> Nodes,
    IReadOnlyList<EdgeView> Edges,
    IReadOnlyList<GraphNode> LayoutNodes,
    IReadOnlyDictionary<string, PileView> Piles,
    IReadOnlyDictionary<string, HeldBackClue> Clues,
    int HiddenUnspendable)
{
    /// <summary>Gets the stubs where lines went to something in the trash (sketch SO1a).</summary>
    public IReadOnlyList<TrashStub> Stubs { get; init; } = [];

    /// <summary>Gets how many drawn nodes the trash hides.</summary>
    public int Trashed { get; init; }
}

/// <summary>
/// Folds the graph so that it never grows out of hand (principle 7): the actors in a pile are drawn as one square node
/// at the actor that was expanded (sketch EX2b), what an expansion held back is a dashed clue <c>+N tx</c> beside it
/// (sketch EX1), and the outputs that can never be spent and carry nothing (OP_RETURN, 0 BTC) are hidden unless the
/// user shows them (the user's decision 2026-11-07). Nothing leaves the model; only the drawing folds.
/// </summary>
/// <remarks>
/// An actor that matters is never folded into a pile, whatever Tracing's rule says: one with a label (an exchange, a
/// victim, a sanction), one that traced money reaches, since the victims' money must always be seen, and one that is
/// watched or is a watch's counterparty, so that the eye, the watch line and its rings keep their node (sketch BV2a).
/// </remarks>
public static class GraphFold
{
    /// <summary>How thick a pile's line is on another asset (sketch EX2b: a thick line with the sum).</summary>
    public const double PileLineWidth = 3.5;

    /// <summary>The thinnest a pile's line of bitcoin is.</summary>
    private const double PileLineThinnest = 1.5;

    /// <summary>How much thicker a pile's line of bitcoin grows.</summary>
    private const double PileLineGrowth = 2.5;

    /// <summary>The satoshi at which a pile's line is at its thickest, 1,5 BTC.</summary>
    private const double PileLineFullAt = 150_000_000;

    /// <summary>A pile on the out side, after its count: <c>12 mottagare</c>.</summary>
    public const string OutPileWord = "mottagare";

    /// <summary>A pile on the in side, after its count: <c>12 avsändare</c>.</summary>
    public const string InPileWord = "avsändare";

    /// <summary>The prefix of a summed line's key.</summary>
    public const string FoldedEdgePrefix = "fold|";

    /// <summary>The prefix of a clue's thin line's key.</summary>
    public const string ClueEdgePrefix = "clue|";

    /// <summary>What a clue says under its count.</summary>
    public const string ClueSubject = "Hölls tillbaka";

    /// <summary>Folds a graph's nodes and lines.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="nodes">Every actor as the graph would draw it.</param>
    /// <param name="edges">Every line as the graph would draw it.</param>
    /// <param name="showUnspendable">Whether the outputs that can never be spent are shown.</param>
    /// <param name="keptOut">
    /// The addresses never folded into a pile, the watched and the watches' counterparties; none when
    /// <see langword="null"/>.
    /// </param>
    /// <param name="trash">
    /// What the trash hides (sketch SO1a): its actors, piles and clues are not drawn, a pile's cards in the trash leave
    /// the pile, and a line to anything in it becomes a stub; <see langword="null"/> hides nothing.
    /// </param>
    /// <returns>What is drawn.</returns>
    public static GraphFolding Apply(
        TraceGraph graph,
        IReadOnlyList<NodeView> nodes,
        IReadOnlyList<EdgeView> edges,
        bool showUnspendable,
        IReadOnlySet<ChainAddress>? keptOut = null,
        TrashFilter? trash = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        keptOut ??= GraphReading.NoAddresses;
        trash ??= TrashFilter.Empty;
        var thrown = nodes.Where(node => trash.Hides(node.Node)).Select(node => node.Key)
            .ToHashSet(StringComparer.Ordinal);
        var byKey = nodes.ToDictionary(node => node.Key, StringComparer.Ordinal);
        var unspendable = nodes.Where(node => IsEmptyUnspendable(graph, node.Node))
            .Select(node => node.Key).ToHashSet(StringComparer.Ordinal);
        var removed = showUnspendable ? [] : unspendable;
        var drawnAs = new Dictionary<string, string>(StringComparer.Ordinal);
        var piles = new List<(GraphPile Pile, List<NodeView> Cards)>();
        foreach (var pile in graph.Piles)
        {
            if (!byKey.ContainsKey(pile.Anchor) || removed.Contains(pile.Anchor))
            {
                continue;
            }

            var cards = pile.Members.Select(byKey.GetValueOrDefault).OfType<NodeView>()
                .Where(card => !removed.Contains(card.Key) && !thrown.Contains(card.Key) && MayFold(card, keptOut))
                .ToList();
            if (cards.Count == 0)
            {
                continue;
            }

            if (trash.Hides(pile.Key))
            {
                // A pile in the trash takes its cards with it: they are neither drawn loose nor in a pile.
                thrown.Add(pile.Key);
            }
            else
            {
                piles.Add((pile, cards));
            }

            foreach (var card in cards)
            {
                drawnAs[card.Key] = pile.Key;
            }
        }

        var (lines, stubs) = Stub(Reroute(edges, drawnAs, removed), thrown);
        var byEnds = edges.ToLookup(edge => (edge.Edge.From, edge.Edge.To));
        var pileViews = piles.ToDictionary(
            pair => pair.Pile.Key,
            pair =>
            {
                var asset = PileAsset(byKey[pair.Pile.Anchor].Node, pair.Pile.Side, pair.Cards, byEnds);
                return new PileView(
                    pair.Pile,
                    [.. pair.Cards.Select(card => CardOf(card, pair.Pile.Anchor, asset, byEnds))],
                    SumBetween(lines, pair.Pile, asset));
            },
            StringComparer.Ordinal);
        var drawn = nodes.Where(node => !drawnAs.ContainsKey(node.Key) && !removed.Contains(node.Key)
            && !thrown.Contains(node.Key)).ToList();
        var layout = drawn.Select(node => node.Node).ToList();
        foreach (var pile in pileViews.Values)
        {
            var node = PileNode(pile, byKey[pile.Pile.Anchor].Node);
            drawn.Add(node);
            layout.Add(node.Node);
        }

        var clues = graph.HeldBack.Where(clue => byKey.ContainsKey(clue.Anchor) && !removed.Contains(clue.Anchor)
                && !thrown.Contains(clue.Anchor) && !trash.Hides(clue.Key))
            .ToDictionary(clue => clue.Key, StringComparer.Ordinal);
        foreach (var clue in clues.Values)
        {
            var node = ClueNode(clue, byKey[clue.Anchor].Node);
            drawn.Add(node);
            layout.Add(node.Node);
            lines.Add(ClueLine(clue));
        }

        return new GraphFolding(drawn, lines, layout, pileViews, clues, unspendable.Count)
        {
            Stubs = stubs,
            Trashed = thrown.Count,
        };
    }

    /// <summary>
    /// Takes the lines to the trash out of the drawn lines: a line with one end in the trash becomes a stub at the
    /// other end's port, once per node and side; a line with both ends in it goes.
    /// </summary>
    /// <param name="lines">The lines drawn so far.</param>
    /// <param name="thrown">The drawn keys in the trash.</param>
    /// <returns>The lines left, and the stubs.</returns>
    private static (List<EdgeView> Lines, List<TrashStub> Stubs) Stub(List<EdgeView> lines, HashSet<string> thrown)
    {
        if (thrown.Count == 0)
        {
            return (lines, []);
        }

        var kept = new List<EdgeView>();
        var stubs = new List<TrashStub>();
        foreach (var line in lines)
        {
            var fromGone = thrown.Contains(line.Edge.From);
            var toGone = thrown.Contains(line.Edge.To);
            if (!fromGone && !toGone)
            {
                kept.Add(line);
                continue;
            }

            if (fromGone == toGone)
            {
                continue;
            }

            var stub = toGone ? new TrashStub(line.Edge.From, true) : new TrashStub(line.Edge.To, false);
            if (!stubs.Contains(stub))
            {
                stubs.Add(stub);
            }
        }

        return (kept, stubs);
    }

    /// <summary>
    /// Says whether a node is an output that can never be spent and carries nothing: an OP_RETURN of 0 BTC, such as
    /// the witness commitment every coinbase has. One that burns money is a fact worth seeing and is never hidden.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsEmptyUnspendable(TraceGraph graph, GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);
        if (node is not { Kind: GraphNodeKind.Coin, Coin: { } coin }
            || !graph.Transactions.TryGetValue(coin.Txid, out var entry)
            || coin.Index >= entry.Transaction.Outputs.Count)
        {
            return false;
        }

        var output = entry.Transaction.Outputs[coin.Index];
        return output.ScriptType == BitcoinScriptType.OpReturn && output.Amount == Satoshi.Zero;
    }

    /// <summary>
    /// Says whether an actor may be folded into its pile: not when it is labelled, traced money reaches it, or one of
    /// its addresses is watched or a watch's counterparty.
    /// </summary>
    /// <param name="card">The actor.</param>
    /// <param name="keptOut">The addresses never folded into a pile.</param>
    /// <returns><see langword="true"/> when it may.</returns>
    private static bool MayFold(NodeView card, IReadOnlySet<ChainAddress> keptOut) =>
        card.Labels.Count == 0 && card.TracedIn.Units.Sign == 0 && card.TracedIns.Count == 0
        && !card.Node.Addresses.Any(keptOut.Contains);

    /// <summary>
    /// Draws every line between the nodes that are drawn: a line to a hidden output goes, a line inside a pile goes,
    /// and the lines to and from a pile's cards are summed per pair, kind and asset into one.
    /// </summary>
    /// <param name="edges">Every line.</param>
    /// <param name="drawnAs">The pile each folded actor is drawn as.</param>
    /// <param name="removed">The hidden outputs.</param>
    /// <returns>The lines drawn, in the order of the first line each stands for.</returns>
    private static List<EdgeView> Reroute(
        IReadOnlyList<EdgeView> edges, Dictionary<string, string> drawnAs, IReadOnlySet<string> removed)
    {
        var result = new List<EdgeView>();
        var folded = new Dictionary<string, List<EdgeView>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var edge in edges)
        {
            if (removed.Contains(edge.Edge.From) || removed.Contains(edge.Edge.To))
            {
                continue;
            }

            var from = drawnAs.GetValueOrDefault(edge.Edge.From) ?? edge.Edge.From;
            var to = drawnAs.GetValueOrDefault(edge.Edge.To) ?? edge.Edge.To;
            if (from == edge.Edge.From && to == edge.Edge.To)
            {
                result.Add(edge);
                continue;
            }

            if (from == to)
            {
                continue;
            }

            var key = FoldedEdgePrefix + edge.Edge.Kind + "|" + from + "|" + to + "|" + edge.Edge.Amount.Asset;
            if (!folded.TryGetValue(key, out var group))
            {
                group = [];
                folded.Add(key, group);
                order.Add(key);
                result.Add(edge);
            }

            group.Add(edge);
        }

        var merged = order.ToDictionary(
            key => folded[key][0].Key, key => Merge(key, folded[key], drawnAs), StringComparer.Ordinal);
        return [.. result.Select(edge => merged.GetValueOrDefault(edge.Key) ?? edge)];
    }

    /// <summary>Sums the lines between a pile and another node into one thick line with the sum.</summary>
    /// <param name="key">The summed line's key.</param>
    /// <param name="group">The lines, all of one kind and asset.</param>
    /// <param name="drawnAs">The pile each folded actor is drawn as.</param>
    /// <returns>The line.</returns>
    private static EdgeView Merge(string key, List<EdgeView> group, Dictionary<string, string> drawnAs)
    {
        var first = group[0];
        var asset = first.Edge.Amount.Asset;
        var amount = group.Aggregate(AssetAmount.Zero(asset), (sum, edge) => sum + edge.Edge.Amount);
        var traced = group.Where(edge => edge.Traced.Asset == asset)
            .Aggregate(AssetAmount.Zero(asset), (sum, edge) => sum + edge.Traced);
        var transfers = group.SelectMany(edge => edge.Edge.Transfers)
            .OrderBy(transfer => transfer.Height ?? long.MaxValue)
            .ThenBy(transfer => transfer.Transaction.Id, StringComparer.Ordinal)
            .ToList();
        var model = new GraphEdge(
            key,
            drawnAs.GetValueOrDefault(first.Edge.From) ?? first.Edge.From,
            drawnAs.GetValueOrDefault(first.Edge.To) ?? first.Edge.To,
            first.Edge.Kind,
            amount,
            transfers,
            group.Select(edge => edge.Edge.FirstTime).Min(),
            first.Edge.Direction);
        var dash = group.Any(edge => edge.Dash == EdgeDash.Uncertain) ? EdgeDash.Uncertain : first.Dash;
        var ink = traced.Units.Sign > 0 ? GraphInk.Accent : first.Ink;
        var count = transfers.Select(transfer => transfer.Transaction.Id).Distinct(StringComparer.Ordinal).Count();
        var label = amount.Asset == GraphScene.Shown
            ? string.Create(CultureInfo.InvariantCulture, $"{BtcText(amount)} BTC · {count} tx")
            : AmountText.OnLine(amount, traced);
        return new EdgeView(model, traced, null, ink, dash, PileWidth(amount), label)
        {
            Glyph = first.Glyph,
        };
    }

    /// <summary>
    /// Gives a pile's line's thickness (sketch EX2b): thick, and thicker the more it carries, up to 1,5 BTC.
    /// </summary>
    /// <param name="amount">What the line carries.</param>
    /// <returns>The thickness.</returns>
    public static double PileWidth(AssetAmount amount) => amount.Asset == GraphScene.Shown
        ? PileLineThinnest + (PileLineGrowth * Math.Clamp((double)amount.Units / PileLineFullAt, 0, 1))
        : PileLineWidth;

    /// <summary>
    /// Writes what moved in a pile with its unit: bitcoin as the piles always did (<c>4,97 BTC</c>), another asset as
    /// the lines write it (<c>1 250,0000 USDT</c>), so that a pile on Ethereum never says 0 BTC.
    /// </summary>
    /// <param name="amount">The amount.</param>
    /// <returns>The text.</returns>
    public static string PileAmount(AssetAmount amount) =>
        amount.Asset == GraphScene.Shown ? BtcText(amount) + " BTC" : AmountText.WithUnit(amount);

    /// <summary>
    /// Gives the asset a pile counts in: bitcoin at an actor on Bitcoin; on an account chain the asset most of the
    /// transfers on the pile's side move (out of the actor for payees, into it for payers), or the chain's own coin
    /// when none does.
    /// </summary>
    /// <param name="anchor">The pile's actor.</param>
    /// <param name="side">The pile's side.</param>
    /// <param name="cards">The cards.</param>
    /// <param name="byEnds">Every line, by its two ends.</param>
    /// <returns>The asset.</returns>
    private static Asset PileAsset(
        GraphNode anchor,
        PileSide side,
        IReadOnlyList<NodeView> cards,
        ILookup<(string From, string To), EdgeView> byEnds)
    {
        if (anchor.Chain == Chain.Bitcoin)
        {
            return GraphScene.Shown;
        }

        return cards.SelectMany(card => side == PileSide.Out
                ? byEnds[(anchor.Key, card.Key)]
                : byEnds[(card.Key, anchor.Key)])
            .GroupBy(edge => edge.Edge.Amount.Asset)
            .OrderByDescending(group => group.Sum(edge => edge.Edge.Transfers.Count))
            .ThenBy(group => group.Key.Kind)
            .Select(group => group.Key)
            .FirstOrDefault() ?? anchor.Chain.NativeAsset();
    }

    /// <summary>Writes a bitcoin amount as the piles do: <c>4,97</c>.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>The number.</returns>
    private static string BtcText(AssetAmount amount) =>
        HeldBackLayout.Btc((double)amount.Units / Satoshi.PerBitcoin);

    /// <summary>Makes a card: the actor, and what moved straight between it and the pile's actor, and when.</summary>
    /// <param name="node">The actor.</param>
    /// <param name="anchor">The pile's actor.</param>
    /// <param name="asset">The pile's asset.</param>
    /// <param name="byEnds">Every line, by its two ends, so that a pile of many cards is not quadratic.</param>
    /// <returns>The card.</returns>
    private static PileCard CardOf(
        NodeView node, string anchor, Asset asset, ILookup<(string From, string To), EdgeView> byEnds)
    {
        var between = byEnds[(node.Key, anchor)].Concat(byEnds[(anchor, node.Key)])
            .Where(edge => edge.Edge.Amount.Asset == asset)
            .ToList();
        var amount = between.Aggregate(AssetAmount.Zero(asset), (sum, edge) => sum + edge.Edge.Amount);
        var time = between.Select(edge => edge.Edge.FirstTime).Min() ?? node.Node.FirstReceived;
        var transactions = between.SelectMany(edge => edge.Edge.Transfers)
            .Select(transfer => transfer.Transaction.Id).ToHashSet(StringComparer.Ordinal);
        return new PileCard(node, amount, time) { TransactionIds = transactions };
    }

    /// <summary>
    /// Adds what moved between a pile and its actor on the pile's own side, in the pile's asset: out of the actor for
    /// a pile of payees, into it for a pile of payers, so that the sum never mixes the two directions.
    /// </summary>
    /// <param name="lines">The lines drawn.</param>
    /// <param name="pile">The pile.</param>
    /// <param name="asset">The pile's asset.</param>
    /// <returns>The sum.</returns>
    private static AssetAmount SumBetween(IReadOnlyList<EdgeView> lines, GraphPile pile, Asset asset)
    {
        var (from, to) = pile.Side == PileSide.Out ? (pile.Anchor, pile.Key) : (pile.Key, pile.Anchor);
        return lines.Where(edge => edge.Edge.From == from && edge.Edge.To == to)
            .Select(edge => edge.Edge.Amount)
            .Where(amount => amount.Asset == asset)
            .Aggregate(AssetAmount.Zero(asset), (sum, amount) => sum + amount);
    }

    /// <summary>Counts the transactions between a pile's cards and its actor.</summary>
    /// <param name="cards">The cards.</param>
    /// <returns>How many distinct transactions; a transaction shared by several cards counts once.</returns>
    public static int TransactionsOf(IReadOnlyList<PileCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        return cards.SelectMany(card => card.TransactionIds).Distinct(StringComparer.Ordinal).Count();
    }

    /// <summary>Makes a pile's node: square like a deck, named by its side, with the count and the sum.</summary>
    /// <param name="pile">The pile.</param>
    /// <param name="anchor">The actor it lies at.</param>
    /// <returns>The node's view.</returns>
    private static NodeView PileNode(PileView pile, GraphNode anchor)
    {
        var hop = anchor.Hop is { } at ? at + (pile.Pile.Side == PileSide.In ? -1 : 1) : (int?)null;
        var model = Synthetic(pile.Key, GraphNodeKind.Pile, pile.Cards.Count, hop, pile.Pile.Positions);
        var zero = AssetAmount.Zero(pile.Sum.Asset);
        var word = pile.Pile.Side == PileSide.In ? InPileWord : OutPileWord;
        return new NodeView(
            model,
            VisualLanguage.Of(NodeSort.Pile),
            string.Create(CultureInfo.InvariantCulture, $"{pile.Cards.Count} {word}"),
            string.Empty,
            [],
            false,
            null,
            [],
            pile.Pile.Side == PileSide.In ? zero : pile.Sum,
            pile.Pile.Side == PileSide.In ? pile.Sum : zero,
            zero,
            string.Create(
                CultureInfo.InvariantCulture, $"{PileAmount(pile.Sum)} · {TransactionsOf(pile.Cards)} tx"),
            true);
    }

    /// <summary>Makes a clue's node: round and dashed, <c>+N tx</c>, in its actor's column.</summary>
    /// <param name="clue">The clue.</param>
    /// <param name="anchor">The actor.</param>
    /// <returns>The node's view.</returns>
    private static NodeView ClueNode(HeldBackClue clue, GraphNode anchor)
    {
        var model = Synthetic(clue.Key, GraphNodeKind.HeldBack, 1, anchor.Hop, clue.Positions);
        var zero = AssetAmount.Zero(GraphScene.Shown);
        return new NodeView(
            model, VisualLanguage.Of(NodeSort.HeldBack), ClueName(clue), "", [], false, null, [], zero, zero, zero,
            ClueLine(clue.Chosen, clue.Oldest), true);
    }

    /// <summary>Names a clue: <c>+1 709 tx</c>, or <c>+ fler tx</c> when the sources did not say how many.</summary>
    /// <param name="clue">The clue.</param>
    /// <returns>The name.</returns>
    public static string ClueName(HeldBackClue clue)
    {
        ArgumentNullException.ThrowIfNull(clue);
        return clue.Remaining is { } remaining
            ? "+" + remaining.ToString("#,##0", CultureInfo.GetCultureInfo("sv-SE")) + " tx"
            : "+ fler tx";
    }

    /// <summary>
    /// Gives the line under a clue (sketch EX2b): how many were picked out and the oldest of them, or, before any,
    /// that what was held back is older than what came in; empty when no time is known.
    /// </summary>
    /// <param name="chosen">How many were picked out.</param>
    /// <param name="oldest">The date to say, from <see cref="HeldBackClue.Oldest"/>.</param>
    /// <returns>For example <c>12 plockade · äldst 2025-08-04</c> or <c>äldre än 2026-03-02</c>.</returns>
    public static string ClueLine(int chosen, DateTimeOffset? oldest)
    {
        var date = oldest?.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (chosen > 0)
        {
            var picked = string.Create(CultureInfo.InvariantCulture, $"{chosen} plockade");
            return date is null ? picked : picked + " · äldst " + date;
        }

        return date is null ? ClueSubject : "äldre än " + date;
    }

    /// <summary>Makes the thin dashed line from an actor to its clue, which carries no amount.</summary>
    /// <param name="clue">The clue.</param>
    /// <returns>The line.</returns>
    private static EdgeView ClueLine(HeldBackClue clue)
    {
        var zero = AssetAmount.Zero(GraphScene.Shown);
        var model = new GraphEdge(
            ClueEdgePrefix + clue.Key, clue.Anchor, clue.Key, GraphEdgeKind.HeldBack, zero, [], null,
            EdgeDirection.Unordered);
        return new EdgeView(model, zero, null, GraphInk.Untraced, EdgeDash.Uncertain, EdgeWidth.Untraced, "");
    }

    /// <summary>Makes the model's node for a pile or a clue, which the graph draws but Tracing never holds.</summary>
    /// <param name="key">The key.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="count">How many it stands for.</param>
    /// <param name="hop">The hop it is laid out in.</param>
    /// <param name="positions">Where the user placed it.</param>
    /// <returns>The node.</returns>
    private static GraphNode Synthetic(
        string key, GraphNodeKind kind, int count, int? hop, IReadOnlyDictionary<GraphLayout, NodePosition> positions) =>
        new(key, kind, [], count, null, null, null, false, hop, null, NodeExpansion.Expanded, [], positions);
}
