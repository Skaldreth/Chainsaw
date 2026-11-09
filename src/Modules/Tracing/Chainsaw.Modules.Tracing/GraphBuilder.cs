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
    private readonly Dictionary<(GraphEdgeKind Kind, string From, string To, Asset Asset), List<AssetTransfer>> _edges =
        [];

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
        AddAccounts();
        problems.AddRange(_data.Account.Problems);
        var starts = StartNodes(problems);
        var hops = Hops(starts);
        _firstReceived = _edges
            .SelectMany(pair => pair.Value.Select(transfer => (pair.Key.To, transfer.Time)))
            .Where(pair => pair.Time is not null)
            .GroupBy(pair => pair.To, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Min(pair => pair.Time!.Value), StringComparer.Ordinal);
        _expansions = _data.State.Expansions
            .GroupBy(ExpansionKey, StringComparer.Ordinal)
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
            .Select(pair => Edge(pair.Key.Kind, pair.Key.From, pair.Key.To, pair.Key.Asset, pair.Value, hops))
            .OrderBy(edge => edge.FirstTime ?? DateTimeOffset.MaxValue)
            .ThenBy(edge => edge.Key, StringComparer.Ordinal)
            .ToList();
        var account = _data.Account;
        var (swaps, refused) = account.ReadSwaps(SwapEndpoint);
        var graph = new TraceGraph(
            _data.State.StartPoints,
            nodes,
            edges,
            new UtxoGraph(_data.Transactions, Spends(transactions), transactionEnds),
            _data.State.Links,
            _data.State.Expansions,
            problems)
        {
            Account = new AccountGraph(
                account.Transfers, account.Fees, account.Transactions, account.ReadAccounts,
                swaps)
            {
                MayBeOutdated = account.MayBeOutdated,
                PartlyRead = account.PartlyRead,
                RefusedSwaps = refused,
            },
            SwapStops = _data.State.SwapStops,
            Gone = _data.Gone,
            FollowedReplacements = _data.State.Replacements,
        };
        var (piles, clues) = PileRules.Arrange(graph, _data.State);
        return graph with { Piles = piles, HeldBack = clues };
    }

    /// <summary>
    /// Makes a node for every Ethereum address in the graph (the start addresses and those the movements touch) and
    /// an edge per pair of accounts and asset for the movements. The account model's heuristics are not built yet, so
    /// every address is its own actor.
    /// </summary>
    private void AddAccounts()
    {
        var account = _data.Account;
        var addresses = _data.State.StartPoints.Select(start => start.EthereumAddress).OfType<EthereumAddress>()
            .Concat(account.Transfers.SelectMany(transfer => new[] { transfer.From, transfer.To }))
            .Concat(account.Transactions.Values.Select(transaction => transaction.From))
            .Distinct()
            .OrderBy(address => address.Value, StringComparer.Ordinal);
        foreach (var address in addresses)
        {
            AccountNode(address);
        }

        foreach (var transfer in account.Transfers.Where(transfer => transfer.From != transfer.To))
        {
            AddTransfer(
                GraphEdgeKind.Transfer,
                AccountNode(transfer.From).Key,
                AccountNode(transfer.To).Key,
                AssetTransfer.OfEthereum(transfer, account.BlockOf(transfer.Transaction.Chain, transfer.Block)));
        }
    }

    /// <summary>Gives the node of an Ethereum address, making it the first time.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The node.</returns>
    private NodeDraft AccountNode(EthereumAddress address)
    {
        var chainAddress = address.ToChainAddress();
        return AddNode(new NodeDraft(NodeKeys.Account(chainAddress), GraphNodeKind.Address)
        {
            Chain = address.Chain,
            Accounts = [chainAddress],
        });
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
            AddTransfer(GraphEdgeKind.Transfer, Sender(input), node.Key, AssetTransfer.OfCoin(
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

    /// <summary>
    /// Says why an Ethereum address is one the trail must reach by its label (an exchange, a mixer, or a bridge or
    /// swap service to another chain), so that a transaction with it is never read as a swap.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>The label's end and source, for example <c>Börs, uttag funnet (etikett: …)</c>, or null.</returns>
    private string? SwapEndpoint(EthereumAddress address) => SwapEndpoint(_knowledge.AccountEnds, address);

    /// <summary>
    /// Says why an Ethereum address is one the trail must reach by its label, from the ends the labels give; written
    /// once, so that choosing for a swap and building the graph read swaps alike.
    /// </summary>
    /// <param name="ends">Gives why the trail ends at an account.</param>
    /// <param name="address">The address.</param>
    /// <returns>The label's end and source, or <see langword="null"/>.</returns>
    public static string? SwapEndpoint(Func<ChainAddress, IReadOnlyList<TrailEndHint>> ends, EthereumAddress address) =>
        ends(address.ToChainAddress())
            .Where(hint => hint.Reason is TrailEndReason.Exchange or TrailEndReason.Mixer or TrailEndReason.OtherChain)
            .Select(hint => $"{hint.Reason.Describe()} ({hint.Source})")
            .FirstOrDefault();

    /// <summary>Adds an edge to another chain for every link from a coin or an Ethereum movement in the graph.</summary>
    private void AddCrossChainLinks()
    {
        foreach (var link in _data.State.Links)
        {
            if (link.FromTransfer is { } movement)
            {
                AddAccountLink(link, movement);
                continue;
            }

            if (link.From is not { } coin || !_data.Transactions.TryGetValue(coin.Txid, out var entry)
                || coin.Index >= entry.Transaction.Outputs.Count)
            {
                continue;
            }

            var output = entry.Transaction.Outputs[coin.Index];
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
    /// Adds the edge of a link from an Ethereum movement: from the account that received the movement (the bridge or
    /// the swap) to the other chain's transaction, carrying the movement's amount.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <param name="movement">The movement it leaves from.</param>
    private void AddAccountLink(CrossChainLink link, AccountSeed movement)
    {
        if (_data.Account.Transfers.FirstOrDefault(movement.Is) is not { } transfer)
        {
            return;
        }

        var target = AddNode(new NodeDraft(NodeKeys.OtherChain(link.To), GraphNodeKind.OtherChain)
        {
            OtherChain = link.To,
        });
        AddTransfer(
            GraphEdgeKind.ContinuesOnOtherChain,
            AccountNode(transfer.To).Key,
            target.Key,
            AssetTransfer.OfEthereum(transfer, _data.Account.BlockOf(transfer.Transaction.Chain, transfer.Block)));
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
            Unspendable = output.ScriptType == BitcoinScriptType.OpReturn,
        }).Key;

    /// <summary>Gives the transfer of an output.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="output">The output.</param>
    /// <returns>The transfer.</returns>
    private static AssetTransfer Paid(BitcoinTransaction transaction, BitcoinOutput output) =>
        AssetTransfer.OfCoin(
            transaction.Txid, new Outpoint(transaction.Txid, output.Index), output.Amount, transaction.Block);

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

    /// <summary>Adds a transfer to the edge of its asset between two actors.</summary>
    /// <param name="kind">The kind of edge.</param>
    /// <param name="from">The sender's key.</param>
    /// <param name="to">The receiver's key.</param>
    /// <param name="transfer">The transfer.</param>
    private void AddTransfer(GraphEdgeKind kind, string from, string to, AssetTransfer transfer)
    {
        var key = (kind, from, to, transfer.Amount.Asset);
        if (!_edges.TryGetValue(key, out var transfers))
        {
            transfers = [];
            _edges.Add(key, transfers);
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
            if (start.EthereumAddress is { } account)
            {
                starts.Add(AccountNode(account).Key);
            }
            else if (start.EthereumTransaction is { } hash)
            {
                if (_data.Account.Transactions.TryGetValue(hash, out var whole))
                {
                    starts.Add(AccountNode(whole.From).Key);
                }
                else if (!problems.Any(problem => problem.Contains(hash.Value, StringComparison.Ordinal)))
                {
                    problems.Add($"Startpunkten {hash} finns inte i ärendet än; den visas när den har hämtats.");
                }
            }
            else if (start.Address is { } address)
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
            else if (!problems.Any(problem => problem.Contains(start.Txid!.Value, StringComparison.Ordinal)))
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
        // A confirmed transaction in the graph that spends a coin is the surest answer: it is in a block. An
        // unconfirmed one may have been replaced, so a confirmed spender always wins over it.
        var spentInGraph = new Dictionary<Outpoint, BitcoinSpend>();
        foreach (var transaction in transactions.Where(tx => !tx.IsCoinbase))
        {
            foreach (var input in transaction.Inputs)
            {
                var coin = new Outpoint(input.PreviousTxid!, input.PreviousIndex!.Value);
                if (!spentInGraph.TryGetValue(coin, out var earlier) || earlier.Block is null)
                {
                    spentInGraph[coin] = new BitcoinSpend(transaction.Txid, input.Index, transaction.Block);
                }
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
                    ({ Block: not null } confirmed, _) => new CoinSpend(SpendState.Spent, confirmed, false),

                    // An unconfirmed spender can be replaced; the source's newest answer wins when it names
                    // another, and either way the answer may change.
                    ({ } pending, { SpentBy: { } named }) when named.Txid != pending.Txid =>
                        new CoinSpend(SpendState.Spent, named, true),
                    ({ } pending, _) => new CoinSpend(SpendState.Spent, pending, true),
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
            GraphNodeKind.Address when draft.Accounts.Count > 0 =>
                [.. draft.Accounts.SelectMany(_knowledge.AccountEnds).Distinct()],
            GraphNodeKind.Address or GraphNodeKind.Cluster =>
                [.. draft.Addresses.SelectMany(_knowledge.AddressEnds).Distinct()],
            GraphNodeKind.Transaction => transactionEnds.GetValueOrDefault(draft.Transaction!) ?? [],
            _ => [],
        };
        var node = new GraphNode(
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
        return draft.Accounts.Count == 0 ? node : node with { Chain = draft.Chain, Accounts = draft.Accounts };
    }

    /// <summary>
    /// Gives the key an expansion is found by: its subject, with the chain before it on an account chain, since the
    /// same hex on Ethereum and on BSC names two accounts.
    /// </summary>
    /// <param name="expansion">The expansion.</param>
    /// <returns>The key.</returns>
    private static string ExpansionKey(Expansion expansion) => expansion switch
    {
        { EthereumAddress: { } account } => AccountKey(account.Chain, account.Value),
        { EthereumTransaction: { } hash } => AccountKey(hash.Chain, hash.Value),
        _ => expansion.Subject,
    };

    /// <summary>Gives the key of an address or a transaction on an account chain among the expansions.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="value">The address or the transaction, as the chain compares it.</param>
    /// <returns>The key.</returns>
    private static string AccountKey(Chain chain, string value) => chain + ":" + value;

    /// <summary>Says how far a node has been expanded.</summary>
    /// <param name="draft">The node.</param>
    /// <returns>How far.</returns>
    private NodeExpansion Expansion(NodeDraft draft)
    {
        if (draft.Accounts.Count > 0)
        {
            return _expansions.GetValueOrDefault(AccountKey(draft.Accounts[0].Chain, draft.Accounts[0].Value)) switch
            {
                null => NodeExpansion.NotExpanded,
                { IsComplete: true } => NodeExpansion.Expanded,
                _ => NodeExpansion.Partly,
            };
        }

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
            case GraphNodeKind.Coin when draft.Unspendable:
                // Nothing can ever spend it, so the graph already has all there is (an OP_RETURN, such as the
                // witness commitment in every coinbase); it is not shown as waiting to be expanded.
                return NodeExpansion.Expanded;
            default:
                return NodeExpansion.NotExpanded;
        }
    }

    /// <summary>Gives a node's places: for a cluster, the newest place stored under any of its addresses.</summary>
    /// <param name="draft">The node.</param>
    /// <returns>The places by layout.</returns>
    private Dictionary<GraphLayout, NodePosition> Positions(NodeDraft draft)
    {
        var keys = draft.Kind is GraphNodeKind.Address or GraphNodeKind.Cluster && draft.Accounts.Count == 0
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
    /// <param name="asset">The asset its transfers move.</param>
    /// <param name="transfers">The transfers.</param>
    /// <param name="hops">The hops.</param>
    /// <returns>The edge.</returns>
    private static GraphEdge Edge(
        GraphEdgeKind kind,
        string from,
        string to,
        Asset asset,
        List<AssetTransfer> transfers,
        Dictionary<string, int> hops)
    {
        var ordered = transfers
            .OrderBy(transfer => transfer.Height ?? long.MaxValue)
            .ThenBy(transfer => transfer.Transaction.Id, StringComparer.Ordinal)
            .ThenBy(transfer => transfer.Coin?.Index ?? -1)
            .ThenBy(transfer => transfer.Position)
            .ThenBy(transfer => transfer.Movement)
            .ToList();
        var direction = hops.TryGetValue(from, out var fromHop) && hops.TryGetValue(to, out var toHop)
            ? Direction(toHop - fromHop)
            : EdgeDirection.Unordered;
        return new GraphEdge(
            EdgeKey(kind, from, to, asset),
            from,
            to,
            kind,
            ordered.Aggregate(AssetAmount.Zero(asset), (sum, transfer) => sum + transfer.Amount),
            ordered,
            ordered.Select(transfer => transfer.Time).OfType<DateTimeOffset>().Cast<DateTimeOffset?>().Min(),
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

    /// <summary>
    /// Gives an edge's key. An edge of bitcoin has the key it always had, so that what the case remembers about
    /// Bitcoin's edges stays; an edge of another chain's coin adds the chain, and a token's adds its chain and
    /// contract, so that two assets between the same actors never share a key.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="from">The sender's key.</param>
    /// <param name="to">The receiver's key.</param>
    /// <param name="asset">The asset its transfers move.</param>
    /// <returns>The key.</returns>
    internal static string EdgeKey(GraphEdgeKind kind, string from, string to, Asset asset)
    {
        var key = (kind == GraphEdgeKind.Transfer ? "transfer|" : "other-chain|") + from + "|" + to;
        if (asset == Asset.Bitcoin)
        {
            return key;
        }

        return asset.Kind == AssetKind.Native
            ? key + "|" + asset.Chain
            : key + "|" + asset.Chain + ":" + asset.Contract;
    }

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

        /// <summary>
        /// Gets whether the coin can never be spent (<c>OP_RETURN</c>), so that nothing follows it and expanding it
        /// can bring nothing.
        /// </summary>
        public bool Unspendable { get; init; }

        /// <summary>Gets the other chain's transaction.</summary>
        public ChainTransactionRef? OtherChain { get; init; }

        /// <summary>Gets the chain an account is on.</summary>
        public Chain Chain { get; init; } = Chain.Bitcoin;

        /// <summary>Gets the node's addresses on an account chain; empty on Bitcoin.</summary>
        public IReadOnlyList<ChainAddress> Accounts { get; init; } = [];
    }
}
