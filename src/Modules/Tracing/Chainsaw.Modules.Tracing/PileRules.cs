using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// The rules that keep an expansion from flooding the graph (principle 7, sketches EX1 and EX2b): the many new
/// counterparties of an expanded actor go into a pile on their side of it, and what an expansion held back at its
/// limit becomes a clue <c>+N tx</c>. Both are arrangement, worked out anew from the case each time the graph is
/// built; nothing is removed from the graph.
/// </summary>
/// <remarks>
/// <para>
/// An actor belongs to the first thing that brought it into the graph: the start points, then the expansions in the
/// order the case holds them. So a counterparty that was already in the graph never moves into a later pile.
/// </para>
/// <para>
/// Some actors never go into a pile, because the user would lose sight of what matters: a start point, an actor that
/// is expanded or that the user placed, one where the trail ends (an exchange, a mixer, a sanction), and a transaction
/// on another chain the user linked to.
/// </para>
/// </remarks>
internal static class PileRules
{
    /// <summary>Gives the piles and the clues of a graph.</summary>
    /// <param name="graph">The graph, built without them.</param>
    /// <param name="state">The graph's state: the expansions, what was picked out and the places.</param>
    /// <returns>The piles and the clues, in the order of the expansions.</returns>
    public static (IReadOnlyList<GraphPile> Piles, IReadOnlyList<HeldBackClue> Clues) Arrange(
        TraceGraph graph, GraphState state)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(state);
        var facts = new TransferIndex(graph);
        var claimed = StartClaims(graph, state, facts);
        var candidates = new Dictionary<(string Anchor, PileSide Side), List<GraphNode>>();
        var anchors = new List<string>();
        foreach (var expansion in state.Expansions)
        {
            var transactions = TransactionsOf(expansion);
            var anchor = AnchorOf(graph, expansion);
            if (anchor is null)
            {
                claimed.UnionWith(transactions.SelectMany(facts.Touched));
                continue;
            }

            claimed.Add(anchor.Key);
            var touched = transactions.SelectMany(facts.Touched).ToHashSet(StringComparer.Ordinal);
            var brought = graph.Nodes.Where(node => !claimed.Contains(node.Key) && touched.Contains(node.Key)).ToList();
            claimed.UnionWith(brought.Select(node => node.Key));
            foreach (var node in brought.Where(MayLieInPile))
            {
                var side = facts.SideOf(node.Key, anchor.Key, transactions);
                if (!candidates.TryGetValue((anchor.Key, side), out var list))
                {
                    list = [];
                    candidates.Add((anchor.Key, side), list);
                    anchors.Add(anchor.Key);
                }

                list.Add(node);
            }
        }

        var piles = candidates
            .Where(pair => pair.Value.Count > GraphPile.MostLoose)
            .OrderBy(pair => anchors.IndexOf(pair.Key.Anchor))
            .ThenBy(pair => pair.Key.Side)
            .Select(pair => Pile(pair.Key.Anchor, pair.Key.Side, pair.Value, state))
            .ToList();
        return (piles, Clues(graph, state));
    }

    /// <summary>Gives a pile's key: the side and the expanded actor's position key.</summary>
    /// <param name="anchor">The expanded actor's node key.</param>
    /// <param name="side">The side.</param>
    /// <returns>For example <c>pile:out:address:bc1q…</c>.</returns>
    public static string PileKey(string anchor, PileSide side) =>
        GraphPile.KeyPrefix + side.ToString().ToLowerInvariant() + ":" + NodeKeys.Position(anchor);

    /// <summary>Gives a clue's key: the actor's position key.</summary>
    /// <param name="anchor">The actor's node key.</param>
    /// <returns>For example <c>held-back:address:bc1q…</c>.</returns>
    public static string ClueKey(string anchor) => HeldBackClue.KeyPrefix + NodeKeys.Position(anchor);

    /// <summary>
    /// Gives the actors that belong to the start: the start points and every actor in a start point's transaction.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="state">The graph's state.</param>
    /// <param name="facts">The transfers by transaction.</param>
    /// <returns>The claimed actors' keys.</returns>
    private static HashSet<string> StartClaims(TraceGraph graph, GraphState state, TransferIndex facts)
    {
        var claimed = graph.Nodes.Where(node => node.IsStartPoint).Select(node => node.Key)
            .ToHashSet(StringComparer.Ordinal);
        var startTransactions = state.StartPoints
            .Select(start => start.Txid?.Value ?? start.EthereumTransaction?.Value)
            .OfType<string>();
        claimed.UnionWith(startTransactions.SelectMany(facts.Touched));
        return claimed;
    }

    /// <summary>Says whether an actor may lie in a pile (see the remarks on <see cref="PileRules"/>).</summary>
    /// <param name="node">The actor.</param>
    /// <returns><see langword="true"/> when it may.</returns>
    private static bool MayLieInPile(GraphNode node) =>
        node.Kind is not (GraphNodeKind.OtherChain or GraphNodeKind.Pile or GraphNodeKind.HeldBack)
        && !node.IsStartPoint
        && node.Expansion == NodeExpansion.NotExpanded
        && node.Ends.Count == 0
        && node.Positions.Count == 0;

    /// <summary>Gives the transactions an expansion brought in, by the id their transfers carry.</summary>
    /// <param name="expansion">The expansion.</param>
    /// <returns>The ids.</returns>
    private static List<string> TransactionsOf(Expansion expansion) =>
    [
        .. expansion.Transactions.Select(txid => txid.Value)
            .Concat(expansion.EthereumTransactions.Select(hash => hash.Value)),
    ];

    /// <summary>
    /// Gives the actor an expansion was made at: the address's node; for a transaction, its own node when it has one,
    /// else the actor that paid into it. An Ethereum transaction brought in whole has none, since it is one
    /// transaction and never floods the graph.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="expansion">The expansion.</param>
    /// <returns>The actor, or <see langword="null"/>.</returns>
    private static GraphNode? AnchorOf(TraceGraph graph, Expansion expansion)
    {
        if (expansion.Address is { } address)
        {
            return graph.NodeOf(address);
        }

        if (expansion.EthereumAddress is { } account)
        {
            return graph.NodeOf(account.ToChainAddress());
        }

        if (expansion.Txid is not { } txid)
        {
            return null;
        }

        if (graph.Node(NodeKeys.Transaction(txid)) is { } own)
        {
            return own;
        }

        return graph.Transactions.TryGetValue(txid, out var entry)
            ? entry.Transaction.Inputs.Select(input => input.Address).OfType<Chains.Contracts.BitcoinAddress>()
                .Select(graph.NodeOf).OfType<GraphNode>().FirstOrDefault()
            : null;
    }

    /// <summary>Makes a pile of the actors on one side of an expanded actor, apart from those picked out.</summary>
    /// <param name="anchor">The expanded actor's node key.</param>
    /// <param name="side">The side.</param>
    /// <param name="members">The actors.</param>
    /// <param name="state">The graph's state.</param>
    /// <returns>The pile.</returns>
    private static GraphPile Pile(string anchor, PileSide side, List<GraphNode> members, GraphState state)
    {
        var picked = members.Where(node => NodeKeys.Members(node).Any(state.Picked.Contains)).ToList();
        var key = PileKey(anchor, side);
        return new GraphPile(
            key,
            anchor,
            side,
            [.. members.Except(picked).Select(node => node.Key)],
            [.. picked.Select(node => node.Key)],
            PlacesOf(key, state));
    }

    /// <summary>
    /// Gives the clues: one per actor with an expansion held back at its limit, with how many are left when every
    /// source said how many there are, and the lookups that bring in the next part.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="state">The graph's state.</param>
    /// <returns>The clues.</returns>
    private static List<HeldBackClue> Clues(TraceGraph graph, GraphState state)
    {
        var clues = new List<(string Anchor, long? Remaining, ExpansionRequest Next)>();
        foreach (var expansion in state.Expansions)
        {
            if (expansion.HeldBack is not { } heldBack || AnchorOf(graph, expansion) is not { } anchor)
            {
                continue;
            }

            var taken = Math.Max(heldBack.Taken, expansion.Transactions.Count + expansion.EthereumTransactions.Count);
            long? remaining = heldBack.Available is { } available ? available - taken : null;
            if (remaining <= 0 || NextOf(expansion, taken + HeldBackClue.NextPart) is not { } next)
            {
                continue;
            }

            clues.Add((anchor.Key, remaining, next));
        }

        return
        [
            .. clues.GroupBy(clue => clue.Anchor, StringComparer.Ordinal).Select(group => new HeldBackClue(
                ClueKey(group.Key),
                group.Key,
                group.All(clue => clue.Remaining is not null) ? group.Sum(clue => clue.Remaining!.Value) : null,
                [.. group.Select(clue => clue.Next)],
                PlacesOf(ClueKey(group.Key), state))),
        ];
    }

    /// <summary>Gives the lookup that expands the same subject again with a higher limit.</summary>
    /// <param name="expansion">The expansion.</param>
    /// <param name="limit">The new limit.</param>
    /// <returns>The lookup, or <see langword="null"/> for an Ethereum transaction, which is never held back.</returns>
    private static ExpansionRequest? NextOf(Expansion expansion, int limit)
    {
        if (expansion.Address is { } address)
        {
            return ExpansionRequest.ForAddress(address, limit);
        }

        if (expansion.Txid is { } txid)
        {
            return ExpansionRequest.ForTransaction(txid) with { MaximumTransactions = limit };
        }

        return expansion.EthereumAddress is { } account ? ExpansionRequest.ForEthereumAddress(account, limit) : null;
    }

    /// <summary>Gives the places the case holds for a pile or a clue, by its key.</summary>
    /// <param name="key">The key.</param>
    /// <param name="state">The graph's state.</param>
    /// <returns>The places by layout; empty when the user has not placed it.</returns>
    private static Dictionary<GraphLayout, NodePosition> PlacesOf(string key, GraphState state) =>
        state.Positions.TryGetValue(key, out var stored)
            ? stored.ToDictionary(pair => pair.Key, pair => pair.Value.Position)
            : [];

    /// <summary>The graph's transfers by transaction: who takes part in each, who pays in it and when it was.</summary>
    private sealed class TransferIndex
    {
        /// <summary>The actors that take part in each transaction.</summary>
        private readonly Dictionary<string, HashSet<string>> _touched = new(StringComparer.Ordinal);

        /// <summary>The actors that pay in each transaction.</summary>
        private readonly Dictionary<string, HashSet<string>> _senders = new(StringComparer.Ordinal);

        /// <summary>When each transaction was confirmed; missing while unconfirmed.</summary>
        private readonly Dictionary<string, DateTimeOffset> _times = new(StringComparer.Ordinal);

        /// <summary>Indexes the graph's transfers.</summary>
        /// <param name="graph">The graph.</param>
        public TransferIndex(TraceGraph graph)
        {
            foreach (var edge in graph.Edges.Where(edge => edge.Kind == GraphEdgeKind.Transfer))
            {
                foreach (var transfer in edge.Transfers)
                {
                    var id = transfer.Transaction.Id;
                    Add(_touched, id, edge.From);
                    Add(_touched, id, edge.To);
                    Add(_senders, id, edge.From);
                    if (transfer.Time is { } time)
                    {
                        _times[id] = time;
                    }
                }
            }
        }

        /// <summary>Gives the actors that take part in a transaction.</summary>
        /// <param name="id">The transaction's id.</param>
        /// <returns>Their keys; empty when it moved nothing between actors.</returns>
        public IEnumerable<string> Touched(string id) =>
            _touched.TryGetValue(id, out var keys) ? keys : [];

        /// <summary>Says whether an actor takes part in a transaction.</summary>
        /// <param name="id">The transaction's id.</param>
        /// <param name="nodeKey">The actor's key.</param>
        /// <returns><see langword="true"/> when it does.</returns>
        public bool Touches(string id, string nodeKey) =>
            _touched.TryGetValue(id, out var keys) && keys.Contains(nodeKey);

        /// <summary>
        /// Gives the side of an expanded actor a counterparty lies on, by the first of the expansion's transactions it
        /// takes part in: out when the expanded actor pays in it, in otherwise.
        /// </summary>
        /// <param name="nodeKey">The counterparty.</param>
        /// <param name="anchor">The expanded actor.</param>
        /// <param name="transactions">The expansion's transactions.</param>
        /// <returns>The side.</returns>
        public PileSide SideOf(string nodeKey, string anchor, IReadOnlyList<string> transactions)
        {
            var first = transactions.Where(id => Touches(id, nodeKey))
                .OrderBy(id => _times.TryGetValue(id, out var time) ? time : DateTimeOffset.MaxValue)
                .ThenBy(id => id, StringComparer.Ordinal)
                .First();
            return _senders.TryGetValue(first, out var senders) && senders.Contains(anchor)
                ? PileSide.Out
                : PileSide.In;
        }

        /// <summary>Adds a key to a transaction's set.</summary>
        /// <param name="sets">The sets.</param>
        /// <param name="id">The transaction's id.</param>
        /// <param name="key">The key.</param>
        private static void Add(Dictionary<string, HashSet<string>> sets, string id, string key)
        {
            if (!sets.TryGetValue(id, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                sets.Add(id, set);
            }

            set.Add(key);
        }
    }
}
