using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Builds the graph from the data the case holds: one node per actor (a cluster, a single address, a transaction
/// with several owners of its inputs, a coin without an address, a transaction on another chain), one edge per pair
/// of actors with the transfers summed, the spends of every coin, and hops and times from the start points.
/// </summary>
internal sealed class GraphBuilder
{
    /// <summary>The data.</summary>
    private readonly GraphData _data;

    /// <summary>What the builder asks about the actors.</summary>
    private readonly GraphKnowledge _knowledge;

    /// <summary>The node each address belongs to.</summary>
    private readonly Dictionary<BitcoinAddress, NodeDraft> _byAddress = [];

    /// <summary>The nodes by key, in the order they were made.</summary>
    private readonly Dictionary<string, NodeDraft> _nodes = new(StringComparer.Ordinal);

    /// <summary>The time money first came to each node, filled when the edges are done.</summary>
    private Dictionary<string, DateTimeOffset> _firstReceived = new(StringComparer.Ordinal);

    /// <summary>The expansions by address and by transaction.</summary>
    private Dictionary<string, Expansion> _expansions = new(StringComparer.Ordinal);

    /// <summary>The edges' transfers by kind, sender and receiver.</summary>
    private readonly Dictionary<(GraphEdgeKind Kind, string From, string To), List<EdgeTransfer>> _edges = [];

    /// <summary>Creates a builder.</summary>
    /// <param name="data">The data.</param>
    /// <param name="knowledge">What the builder asks about the actors.</param>
    private GraphBuilder(GraphData data, GraphKnowledge knowledge) => (_data, _knowledge) = (data, knowledge);

    /// <summary>Builds the graph.</summary>
    /// <param name="data">The data.</param>
    /// <param name="knowledge">What the builder asks about the actors.</param>
    /// <returns>The graph.</returns>
    public static TraceGraph Build(GraphData data, GraphKnowledge knowledge) =>
        new GraphBuilder(data, knowledge).Build();

    /// <summary>Builds the graph, step by step.</summary>
    /// <returns>The graph.</returns>
    private TraceGraph Build()
    {
        var transactions = _data.Transactions.Values.Select(entry => entry.Transaction)
            .OrderBy(tx => tx.Block?.Height ?? long.MaxValue).ThenBy(tx => tx.Txid.Value, StringComparer.Ordinal)
            .ToList();
        var problems = new List<string>(_data.Problems);
        AddActors(transactions);
        foreach (var transaction in transactions)
        {
            AddTransfers(transaction);
        }

        AddCrossChainLinks();
        var starts = StartNodes(problems);
        var hops = Hops(starts);
        _firstReceived = _edges
            .SelectMany(pair => pair.Value.Select(transfer => (pair.Key.To, transfer.Block?.Time)))
            .Where(pair => pair.Time is not null)
            .GroupBy(pair => pair.To, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Min(pair => pair.Time!.Value), StringComparer.Ordinal);
        _expansions = _data.State.Expansions
            .GroupBy(expansion => expansion.Address?.Value ?? expansion.Txid!.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var transactionEnds = transactions
            .Select(tx => (tx.Txid, Ends: _knowledge.TransactionEnds(tx)))
            .Where(pair => pair.Ends.Count > 0)
            .ToDictionary(pair => pair.Txid, pair => pair.Ends);
        var nodes = _nodes.Values.Select(draft => Finish(draft, starts, hops, transactionEnds))
            .OrderBy(node => node.IsStartPoint ? 0 : 1)
            .ThenBy(node => node.Hop ?? int.MaxValue)
            .ThenBy(node => node.FirstReceived ?? DateTimeOffset.MaxValue)
            .ThenBy(node => node.Key, StringComparer.Ordinal)
            .ToList();
        var edges = _edges
            .Select(pair => Edge(pair.Key.Kind, pair.Key.From, pair.Key.To, pair.Value, hops))
            .OrderBy(edge => edge.FirstTime ?? DateTimeOffset.MaxValue)
            .ThenBy(edge => edge.Key, StringComparer.Ordinal)
            .ToList();
        return new TraceGraph(
            _data.State.StartPoints,
            nodes,
            edges,
            _data.Transactions,
            Spends(transactions),
            transactionEnds,
            _data.State.Links,
            _data.State.Expansions,
            problems);
    }

    /// <summary>
    /// Makes a node for every address in the graph, one per cluster, and the transaction nodes for transactions
    /// whose inputs have more than one owner.
    /// </summary>
    /// <param name="transactions">The graph's transactions.</param>
    private void AddActors(List<BitcoinTransaction> transactions)
    {
        var startAddresses = _data.State.StartPoints.Where(start => start.Address is not null)
            .Select(start => start.Address!).ToHashSet();
        var addresses = startAddresses
            .Concat(transactions.SelectMany(tx => tx.Inputs.Select(input => input.Address)
                .Concat(tx.Outputs.Select(output => output.Address))).OfType<BitcoinAddress>())
            .Distinct()
            .OrderBy(address => address.Value, StringComparer.Ordinal)
            .ToList();
        var inGraph = addresses.ToHashSet();
        foreach (var address in addresses.Where(address => !_byAddress.ContainsKey(address)))
        {
            var cluster = _knowledge.ClusterOf(address);
            var members = cluster.Where(member => inGraph.Contains(member) && !_byAddress.ContainsKey(member))
                .Append(address).Distinct().OrderBy(member => member.Value, StringComparer.Ordinal).ToList();
            var representative = members.FirstOrDefault(startAddresses.Contains) ?? members[0];
            var isCluster = cluster.Count > 1;
            var draft = AddNode(new NodeDraft(
                isCluster ? NodeKeys.Cluster(representative) : NodeKeys.Address(representative),
                isCluster ? GraphNodeKind.Cluster : GraphNodeKind.Address)
            {
                Addresses = members,
                ClusterSize = Math.Max(cluster.Count, 1),
            });
            foreach (var member in members)
            {
                _byAddress[member] = draft;
            }
        }
    }

    /// <summary>Adds the transfers of one transaction to the edges.</summary>
    /// <param name="transaction">The transaction.</param>
    private void AddTransfers(BitcoinTransaction transaction)
    {
        if (transaction.IsCoinbase)
        {
            // New coins have no sender in the graph; the outputs' actors still have their nodes.
            foreach (var output in transaction.Outputs)
            {
                Receiver(transaction, output);
            }

            return;
        }

        var senders = transaction.Inputs.Select(Sender).Distinct(StringComparer.Ordinal).ToList();
        if (senders.Count == 1)
        {
            foreach (var output in transaction.Outputs)
            {
                var receiver = Receiver(transaction, output);
                if (receiver != senders[0])
                {
                    AddTransfer(GraphEdgeKind.Transfer, senders[0], receiver, Paid(transaction, output));
                }
            }

            return;
        }

        var node = AddNode(new NodeDraft(NodeKeys.Transaction(transaction.Txid), GraphNodeKind.Transaction)
        {
            Transaction = transaction.Txid,
        });
        foreach (var input in transaction.Inputs)
        {
            AddTransfer(GraphEdgeKind.Transfer, Sender(input), node.Key, new EdgeTransfer(
                transaction.Txid,
                new Outpoint(input.PreviousTxid!, input.PreviousIndex!.Value),
                input.Amount ?? Satoshi.Zero,
                transaction.Block));
        }

        foreach (var output in transaction.Outputs)
        {
            AddTransfer(GraphEdgeKind.Transfer, node.Key, Receiver(transaction, output), Paid(transaction, output));
        }
    }

    /// <summary>Adds an edge to another chain for every link from a coin in the graph.</summary>
    private void AddCrossChainLinks()
    {
        foreach (var link in _data.State.Links)
        {
            if (!_data.Transactions.TryGetValue(link.From.Txid, out var entry)
                || link.From.Index >= entry.Transaction.Outputs.Count)
            {
                continue;
            }

            var output = entry.Transaction.Outputs[link.From.Index];
            var target = AddNode(new NodeDraft(NodeKeys.OtherChain(link.To), GraphNodeKind.OtherChain)
            {
                OtherChain = link.To,
            });
            AddTransfer(
                GraphEdgeKind.ContinuesOnOtherChain,
                Receiver(entry.Transaction, output),
                target.Key,
                Paid(entry.Transaction, output));
        }
    }

    /// <summary>
    /// Gives the key of the actor that an input spends from, making a coin node when it has no address.
    /// </summary>
    /// <param name="input">The input; not a coinbase.</param>
    /// <returns>The actor's key.</returns>
    private string Sender(BitcoinInput input) => input.Address is { } address
        ? _byAddress[address].Key
        : AddNode(new NodeDraft(
            NodeKeys.Coin(new Outpoint(input.PreviousTxid!, input.PreviousIndex!.Value)), GraphNodeKind.Coin)
        {
            Coin = new Outpoint(input.PreviousTxid!, input.PreviousIndex!.Value),
        }).Key;

    /// <summary>Gives the key of the actor an output pays, making a coin node when it has no address.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="output">The output.</param>
    /// <returns>The actor's key.</returns>
    private string Receiver(BitcoinTransaction transaction, BitcoinOutput output) => output.Address is { } address
        ? _byAddress[address].Key
        : AddNode(new NodeDraft(NodeKeys.Coin(new Outpoint(transaction.Txid, output.Index)), GraphNodeKind.Coin)
        {
            Coin = new Outpoint(transaction.Txid, output.Index),
        }).Key;

    /// <summary>Gives the transfer of an output.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="output">The output.</param>
    /// <returns>The transfer.</returns>
    private static EdgeTransfer Paid(BitcoinTransaction transaction, BitcoinOutput output) =>
        new(transaction.Txid, new Outpoint(transaction.Txid, output.Index), output.Amount, transaction.Block);

    /// <summary>Adds a node unless one with the same key is there.</summary>
    /// <param name="draft">The node.</param>
    /// <returns>The node in the graph.</returns>
    private NodeDraft AddNode(NodeDraft draft)
    {
        if (_nodes.TryGetValue(draft.Key, out var existing))
        {
            return existing;
        }

        _nodes.Add(draft.Key, draft);
        return draft;
    }

    /// <summary>Adds a transfer to the edge between two actors.</summary>
    /// <param name="kind">The kind of edge.</param>
    /// <param name="from">The sender's key.</param>
    /// <param name="to">The receiver's key.</param>
    /// <param name="transfer">The transfer.</param>
    private void AddTransfer(GraphEdgeKind kind, string from, string to, EdgeTransfer transfer)
    {
        if (!_edges.TryGetValue((kind, from, to), out var transfers))
        {
            transfers = [];
            _edges.Add((kind, from, to), transfers);
        }

        if (!transfers.Contains(transfer))
        {
            transfers.Add(transfer);
        }
    }

    /// <summary>
    /// Gives the nodes the start points are in: an address's actor, or the actors that pay into a transaction.
    /// </summary>
    /// <param name="problems">Where a start point that is not in the graph yet is reported.</param>
    /// <returns>The start nodes' keys, in the start points' order.</returns>
    private List<string> StartNodes(List<string> problems)
    {
        var starts = new List<string>();
        foreach (var start in _data.State.StartPoints)
        {
            if (start.Address is { } address)
            {
                starts.Add(_byAddress[address].Key);
            }
            else if (_data.Transactions.TryGetValue(start.Txid!, out var entry) && entry.Transaction.IsCoinbase)
            {
                // New coins have no sender; the trace starts at the actors the coinbase pays.
                starts.AddRange(entry.Transaction.Outputs.Select(output => Receiver(entry.Transaction, output)));
            }
            else if (entry is not null)
            {
                var senders = entry.Transaction.Inputs.Select(Sender).Distinct(StringComparer.Ordinal).ToList();
                starts.AddRange(senders.Count == 1 ? senders : [NodeKeys.Transaction(start.Txid!)]);
            }
            else
            {
                problems.Add($"Startpunkten {start.Txid} finns inte i ärendet än; den visas när den har hämtats.");
            }
        }

        return [.. starts.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Gives every node's hop: the shortest way along the money from a start node, and for the nodes the money does
    /// not reach that way, one hop before or after a neighbour that has one.
    /// </summary>
    /// <param name="starts">The start nodes.</param>
    /// <returns>The hops by node key; a node missing has none.</returns>
    private Dictionary<string, int> Hops(List<string> starts)
    {
        var outgoing = _edges.Keys.GroupBy(edge => edge.From, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.To).Order(StringComparer.Ordinal)
                .ToList(), StringComparer.Ordinal);
        var incoming = _edges.Keys.GroupBy(edge => edge.To, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.From).Order(StringComparer.Ordinal)
                .ToList(), StringComparer.Ordinal);
        var hops = starts.ToDictionary(start => start, _ => 0, StringComparer.Ordinal);
        var order = new List<string>(starts);
        var queue = new Queue<string>(starts);
        while (queue.TryDequeue(out var node))
        {
            foreach (var next in outgoing.GetValueOrDefault(node) ?? [])
            {
                if (hops.TryAdd(next, hops[node] + 1))
                {
                    order.Add(next);
                    queue.Enqueue(next);
                }
            }
        }

        // The rest: senders into the trail one hop before, and receivers of them one hop after.
        queue = new Queue<string>(order);
        while (queue.TryDequeue(out var node))
        {
            foreach (var next in outgoing.GetValueOrDefault(node) ?? [])
            {
                if (hops.TryAdd(next, hops[node] + 1))
                {
                    queue.Enqueue(next);
                }
            }

            foreach (var previous in incoming.GetValueOrDefault(node) ?? [])
            {
                if (hops.TryAdd(previous, hops[node] - 1))
                {
                    queue.Enqueue(previous);
                }
            }
        }

        return hops;
    }

    /// <summary>Gives the spends of every output of every transaction in the graph.</summary>
    /// <param name="transactions">The transactions.</param>
    /// <returns>The spends by coin.</returns>
    private Dictionary<Outpoint, CoinSpend> Spends(List<BitcoinTransaction> transactions)
    {
        // A transaction in the graph that spends a coin is the surest answer: it is on the chain.
        var spentInGraph = new Dictionary<Outpoint, BitcoinSpend>();
        foreach (var transaction in transactions.Where(tx => !tx.IsCoinbase))
        {
            foreach (var input in transaction.Inputs)
            {
                spentInGraph[new Outpoint(input.PreviousTxid!, input.PreviousIndex!.Value)] =
                    new BitcoinSpend(transaction.Txid, input.Index, transaction.Block);
            }
        }

        var spends = new Dictionary<Outpoint, CoinSpend>();
        foreach (var transaction in transactions)
        {
            var answer = _data.OutputSpends.GetValueOrDefault(transaction.Txid);
            foreach (var output in transaction.Outputs)
            {
                var coin = new Outpoint(transaction.Txid, output.Index);
                var asked = answer?.Spends.FirstOrDefault(spend => spend.Index == output.Index);
                spends[coin] = (spentInGraph.GetValueOrDefault(coin), asked) switch
                {
                    // A spender that is not in a block yet can still be replaced by another.
                    ({ } inGraph, _) => new CoinSpend(SpendState.Spent, inGraph, inGraph.Block is null),
                    (null, { } spend) => Asked(spend, answer!.MayBeOutdated),
                    _ => Known(output),
                };
            }
        }

        return spends;
    }

    /// <summary>Reads an answer about a coin from the source.</summary>
    /// <param name="spend">The answer.</param>
    /// <param name="mayBeOutdated">Whether it may have changed since.</param>
    /// <returns>The spend.</returns>
    private static CoinSpend Asked(BitcoinOutputSpend spend, bool mayBeOutdated) => spend.SpentBy is { } by
        ? new CoinSpend(SpendState.Spent, by, mayBeOutdated)
        : new CoinSpend(SpendState.Unspent, null, mayBeOutdated);

    /// <summary>Reads what the transaction itself says about an output that was not asked about.</summary>
    /// <param name="output">The output.</param>
    /// <returns>Spent when the case knew the spender; otherwise unknown.</returns>
    private static CoinSpend Known(BitcoinOutput output) => output.SpentBy is { } by
        ? new CoinSpend(SpendState.Spent, by, false)
        : new CoinSpend(SpendState.Unknown, null, false);

    /// <summary>Finishes a node: its hop, first time, expansion, ends and places.</summary>
    /// <param name="draft">The node.</param>
    /// <param name="starts">The start nodes.</param>
    /// <param name="hops">The hops.</param>
    /// <param name="transactionEnds">The transactions where the trail ends.</param>
    /// <returns>The node.</returns>
    private GraphNode Finish(
        NodeDraft draft,
        List<string> starts,
        Dictionary<string, int> hops,
        Dictionary<Txid, IReadOnlyList<TrailEndHint>> transactionEnds)
    {
        DateTimeOffset? firstReceived = _firstReceived.TryGetValue(draft.Key, out var first) ? first : null;
        IReadOnlyList<TrailEndHint> ends = draft.Kind switch
        {
            GraphNodeKind.Address or GraphNodeKind.Cluster =>
                [.. draft.Addresses.SelectMany(_knowledge.AddressEnds).Distinct()],
            GraphNodeKind.Transaction => transactionEnds.GetValueOrDefault(draft.Transaction!) ?? [],
            _ => [],
        };
        return new GraphNode(
            draft.Key,
            draft.Kind,
            draft.Addresses,
            draft.ClusterSize,
            draft.Transaction,
            draft.Coin,
            draft.OtherChain,
            starts.Contains(draft.Key),
            hops.TryGetValue(draft.Key, out var hop) ? hop : null,
            firstReceived,
            Expansion(draft),
            ends,
            Positions(draft));
    }

    /// <summary>Says how far a node has been expanded.</summary>
    /// <param name="draft">The node.</param>
    /// <returns>How far.</returns>
    private NodeExpansion Expansion(NodeDraft draft)
    {
        switch (draft.Kind)
        {
            case GraphNodeKind.Address or GraphNodeKind.Cluster:
                var states = draft.Addresses
                    .Select(address => _expansions.GetValueOrDefault(address.Value))
                    .ToList();
                if (states.All(state => state is { IsComplete: true }))
                {
                    return NodeExpansion.Expanded;
                }

                return states.Any(state => state is not null) ? NodeExpansion.Partly : NodeExpansion.NotExpanded;
            case GraphNodeKind.Transaction:
                var expanded = _expansions.GetValueOrDefault(draft.Transaction!.Value);
                return expanded switch
                {
                    null => NodeExpansion.NotExpanded,
                    { IsComplete: true } => NodeExpansion.Expanded,
                    _ => NodeExpansion.Partly,
                };
            default:
                return NodeExpansion.NotExpanded;
        }
    }

    /// <summary>Gives a node's places: for a cluster, the newest place stored under any of its addresses.</summary>
    /// <param name="draft">The node.</param>
    /// <returns>The places by layout.</returns>
    private Dictionary<GraphLayout, NodePosition> Positions(NodeDraft draft)
    {
        var keys = draft.Kind is GraphNodeKind.Address or GraphNodeKind.Cluster
            ? draft.Addresses.Select(NodeKeys.Address)
            : [NodeKeys.Position(draft.Key)];
        return keys
            .Select(key => _data.State.Positions.GetValueOrDefault(key))
            .OfType<IReadOnlyDictionary<GraphLayout, StoredPosition>>()
            .SelectMany(byLayout => byLayout)
            .GroupBy(pair => pair.Key)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(pair => pair.Value.UpdatedUtc).First().Value.Position);
    }

    /// <summary>Finishes an edge: its sum, order of transfers, first time and direction.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="from">The sender's key.</param>
    /// <param name="to">The receiver's key.</param>
    /// <param name="transfers">The transfers.</param>
    /// <param name="hops">The hops.</param>
    /// <returns>The edge.</returns>
    private static GraphEdge Edge(
        GraphEdgeKind kind, string from, string to, List<EdgeTransfer> transfers, Dictionary<string, int> hops)
    {
        var ordered = transfers
            .OrderBy(transfer => transfer.Block?.Height ?? long.MaxValue)
            .ThenBy(transfer => transfer.Txid.Value, StringComparer.Ordinal)
            .ThenBy(transfer => transfer.Coin.Index)
            .ToList();
        var direction = hops.TryGetValue(from, out var fromHop) && hops.TryGetValue(to, out var toHop)
            ? Direction(toHop - fromHop)
            : EdgeDirection.Unordered;
        return new GraphEdge(
            EdgeKey(kind, from, to),
            from,
            to,
            kind,
            ordered.Aggregate(Satoshi.Zero, (sum, transfer) => sum + transfer.Amount),
            ordered,
            ordered.Select(transfer => transfer.Block?.Time).OfType<DateTimeOffset>().Cast<DateTimeOffset?>().Min(),
            direction);
    }

    /// <summary>Gives an edge's direction from how many hops it goes.</summary>
    /// <param name="step">The receiver's hop minus the sender's.</param>
    /// <returns>The direction.</returns>
    private static EdgeDirection Direction(int step) => step switch
    {
        > 0 => EdgeDirection.Forward,
        0 => EdgeDirection.Sideways,
        _ => EdgeDirection.Back,
    };

    /// <summary>Gives an edge's key.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="from">The sender's key.</param>
    /// <param name="to">The receiver's key.</param>
    /// <returns>The key.</returns>
    internal static string EdgeKey(GraphEdgeKind kind, string from, string to) =>
        (kind == GraphEdgeKind.Transfer ? "transfer|" : "other-chain|") + from + "|" + to;

    /// <summary>A node while the graph is being built.</summary>
    /// <param name="Key">The node's key.</param>
    /// <param name="Kind">The kind.</param>
    private sealed record NodeDraft(string Key, GraphNodeKind Kind)
    {
        /// <summary>Gets the node's addresses in the graph, sorted.</summary>
        public IReadOnlyList<BitcoinAddress> Addresses { get; init; } = [];

        /// <summary>Gets how many addresses the whole cluster has.</summary>
        public int ClusterSize { get; init; } = 1;

        /// <summary>Gets the transaction of a transaction node.</summary>
        public Txid? Transaction { get; init; }

        /// <summary>Gets the coin of a coin node.</summary>
        public Outpoint? Coin { get; init; }

        /// <summary>Gets the other chain's transaction.</summary>
        public ChainTransactionRef? OtherChain { get; init; }
    }
}
