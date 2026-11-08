using System.Globalization;
using System.Runtime.CompilerServices;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Requests.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>
/// A place in the graph a request can be made for (sketch BG1b): a cash-out that traced money reached, with the
/// deposits to it and the trail there, or an address where a token is left that its issuer can freeze (sketch TK1a).
/// </summary>
/// <param name="NodeKey">The actor's node in the graph.</param>
/// <param name="Look">Its sort and how that is drawn, so that the list draws it as the graph does.</param>
/// <param name="Kind">What a request to it asks for.</param>
/// <param name="Target">Who the request is addressed to and the addresses it concerns.</param>
/// <param name="Deposits">
/// The deposits to the addresses traced money reached, in time order: the traced ones, and the others to the same
/// addresses, which go to the same customer; never deposits to the actor's other addresses (CLAUDE.md, BG1b).
/// </param>
/// <param name="Trail">The trail there along traced money, or <see langword="null"/> when none reaches it.</param>
/// <param name="Holding">For a freeze, the token and what is left; otherwise <see langword="null"/>.</param>
public sealed record RequestExit(
    string NodeKey,
    SortLook Look,
    RequestKind Kind,
    RequestTarget Target,
    IReadOnlyList<RequestDeposit> Deposits,
    RequestTrail? Trail,
    RequestHolding? Holding)
{
    /// <summary>
    /// Gets the row's key: the kind, the first address the request concerns and, for a freeze, the token. It does not
    /// change when the graph is expanded, as a cluster's node key may.
    /// </summary>
    public string Key => Holding is { } holding
        ? $"{Kind}:{Target.Key}|{holding.Left.Asset}"
        : $"{Kind}:{Target.Key}";

    /// <summary>Gets the name the row shows: the recipient, or for a freeze the issuer and the address.</summary>
    public string Name { get; init; } = Target.Name;

    /// <summary>Gets all the actor's addresses, which an earlier export to the actor may name.</summary>
    public IReadOnlyList<ChainAddress> NodeAddresses { get; init; } = Target.Addresses;

    /// <summary>
    /// Gets the row's second line (sketch BG1b): the chain, how many deposits and their sum, for example
    /// <c>Bitcoin · 3 insättningar · 0,8550 BTC</c>.
    /// </summary>
    public string Summary
    {
        get
        {
            var count = Deposits.Count == 1 ? "1 insättning" : $"{Deposits.Count} insättningar";
            var parts = new List<string> { Target.Chain.ToString(), count };
            if (Holding is { } holding)
            {
                parts[1] = "kvar " + AmountText.WithUnit(holding.Left);
            }
            else if (Deposits.Count > 0)
            {
                parts.Add(string.Join(" + ", Deposits.GroupBy(deposit => deposit.Amount.Asset)
                    .Select(group => AmountText.WithUnit(group.Aggregate(
                        AssetAmount.Zero(group.Key), (sum, deposit) => sum + deposit.Amount)))));
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Says whether an export was a request to this place: a freeze by its address and token, a request for
    /// information by any of the actor's addresses, so that the mark stays when the graph is expanded.
    /// </summary>
    /// <param name="export">The export.</param>
    /// <returns><see langword="true"/> when it was.</returns>
    public bool IsExportedBy(RequestExport export)
    {
        ArgumentNullException.ThrowIfNull(export);
        if (export.Kind != Kind)
        {
            return false;
        }

        return Holding is { } holding
            ? export.TargetKey == RequestDraft.ExportKeyOf(Target, holding)
            : NodeAddresses.Any(address => RequestTarget.KeyOf(address) == export.TargetKey);
    }
}

/// <summary>
/// Finds the requests that can be made in the graph shown (sketch BG1b), without WPF: every cash-out (the blue sorts)
/// that traced money reached, and every token left at an address that its issuer can freeze (sketch TK1a). What a
/// request may hold is decided here: a name comes only from the label that made the actor a cash-out, and every other
/// step is a shortened address, so a victim's name can never reach a request (CLAUDE.md, BG1b).
/// </summary>
public static class RequestExits
{
    /// <summary>The sorts that are cash-outs (blue): where the money leaves the chain.</summary>
    private static readonly Dictionary<NodeSort, RequestTargetKind> CashOuts = new()
    {
        [NodeSort.Exchange] = RequestTargetKind.Exchange,
        [NodeSort.NoKycExchange] = RequestTargetKind.NoKycExchange,
        [NodeSort.Nested] = RequestTargetKind.Nested,
        [NodeSort.OtcP2P] = RequestTargetKind.OtcP2P,
        [NodeSort.Atm] = RequestTargetKind.Atm,
        [NodeSort.Card] = RequestTargetKind.Card,
    };

    /// <summary>
    /// What has been found per scene, so that the menu, the inspector and the view ask once per graph drawn; a scene is
    /// never changed, and the entry goes with it.
    /// </summary>
    private static readonly ConditionalWeakTable<GraphScene, IReadOnlyList<RequestExit>> Found = [];

    /// <summary>Finds the requests that can be made: the cash-outs first, then the freezes, in the graph's order.
    /// </summary>
    /// <param name="scene">The graph shown.</param>
    /// <returns>The places.</returns>
    public static IReadOnlyList<RequestExit> Find(GraphScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return Found.GetValue(scene, Search);
    }

    /// <summary>Writes when a deposit came, for the chips in the view: the local day and time.</summary>
    /// <param name="deposit">The deposit.</param>
    /// <param name="zone">The local zone.</param>
    /// <returns>For example <c>2026-10-28 21:14</c>, or <c>obekräftad</c>.</returns>
    public static string When(RequestDeposit deposit, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(deposit);
        ArgumentNullException.ThrowIfNull(zone);
        return deposit.Time is { } time
            ? TimeZoneInfo.ConvertTime(time, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "obekräftad";
    }

    /// <summary>Searches a scene for the requests that can be made.</summary>
    /// <param name="scene">The graph shown.</param>
    /// <returns>The places.</returns>
    private static IReadOnlyList<RequestExit> Search(GraphScene scene)
    {
        var nodes = Actors(scene);
        var traced = TracedEdges(scene.Snapshot);
        var exits = new List<RequestExit>();
        var freezes = new List<RequestExit>();
        foreach (var node in nodes.Values)
        {
            if (CashOut(scene, node, nodes, traced) is { } exit)
            {
                exits.Add(exit);
            }

            freezes.AddRange(Freezes(scene, node, nodes, traced));
        }

        return [.. exits, .. freezes];
    }

    /// <summary>Gives the actors drawn, those lying in piles among them, by key, in the graph's order.</summary>
    /// <param name="scene">The graph.</param>
    /// <returns>The actors.</returns>
    private static Dictionary<string, NodeView> Actors(GraphScene scene)
    {
        var nodes = new Dictionary<string, NodeView>(StringComparer.Ordinal);
        foreach (var node in scene.Nodes.Concat(scene.Piles.Values.SelectMany(pile => pile.Cards.Select(c => c.Node))))
        {
            if (node.Node.Kind is not (GraphNodeKind.Pile or GraphNodeKind.HeldBack))
            {
                nodes.TryAdd(node.Key, node);
            }
        }

        return nodes;
    }

    /// <summary>Gives the keys of the lines that carry traced money, on any chain.</summary>
    /// <param name="snapshot">What the graph was built from.</param>
    /// <returns>The keys.</returns>
    private static HashSet<string> TracedEdges(GraphSnapshot snapshot)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in (snapshot.Trace?.Edges ?? []).Concat(snapshot.AccountTrace?.Edges ?? []))
        {
            if (edge.Amount.Amount.Units.Sign > 0)
            {
                keys.Add(edge.EdgeKey);
            }
        }

        return keys;
    }

    /// <summary>
    /// Makes the request for a cash-out that traced money reached; none for any other actor. It concerns only the
    /// addresses traced money reached, and the deposits to them.
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <param name="node">The actor.</param>
    /// <param name="nodes">Every actor, by key.</param>
    /// <param name="traced">The lines that carry traced money.</param>
    /// <returns>The place, or <see langword="null"/>.</returns>
    private static RequestExit? CashOut(
        GraphScene scene, NodeView node, Dictionary<string, NodeView> nodes, HashSet<string> traced)
    {
        if (!CashOuts.TryGetValue(node.Look.Sort, out var kind) || node.Node.Addresses.Count == 0)
        {
            return null;
        }

        var incoming = Incoming(scene.Snapshot.Graph, node.Key).ToList();
        if (!incoming.Any(edge => traced.Contains(edge.Key)))
        {
            return null;
        }

        var all = Deposits(scene.Snapshot, node, incoming, traced, asset: null);
        var reached = Reached(all, incoming, traced);
        if (reached.Count == 0)
        {
            return null;
        }

        var deposits = all.Where(deposit => reached.Contains(deposit.To)).ToList();
        var target = new RequestTarget(SafeName(node), kind, node.Node.Chain, [.. reached]);
        return new RequestExit(
            node.Key, node.Look, RequestKind.Information, target, deposits, Trail(scene, node, nodes, traced), null)
        {
            NodeAddresses = node.Node.Addresses,
        };
    }

    /// <summary>
    /// Gives the addresses traced money reached, in the order they first did: those of the traced deposits, or when no
    /// single deposit can be told traced, those the traced lines paid.
    /// </summary>
    /// <param name="deposits">Every deposit to the actor.</param>
    /// <param name="incoming">The lines into it.</param>
    /// <param name="traced">The lines that carry traced money.</param>
    /// <returns>The addresses.</returns>
    private static List<ChainAddress> Reached(
        IReadOnlyList<RequestDeposit> deposits, IReadOnlyList<GraphEdge> incoming, HashSet<string> traced)
    {
        var reached = deposits.Where(deposit => deposit.IsTraced).Select(deposit => deposit.To).Distinct().ToList();
        if (reached.Count > 0)
        {
            return reached;
        }

        var tracedTransactions = incoming.Where(edge => traced.Contains(edge.Key))
            .SelectMany(edge => edge.Transfers.Select(transfer => transfer.Transaction.Id))
            .ToHashSet(StringComparer.Ordinal);
        return [.. deposits.Where(deposit => tracedTransactions.Contains(deposit.Transaction))
            .Select(deposit => deposit.To).Distinct()];
    }

    /// <summary>
    /// Makes a freeze for every token left at an address that its issuer can freeze, as the inspector's snowflake says
    /// (sketch TK1a); none for any other actor.
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <param name="node">The actor.</param>
    /// <param name="nodes">Every actor, by key.</param>
    /// <param name="traced">The lines that carry traced money.</param>
    /// <returns>The places, one per token.</returns>
    private static IEnumerable<RequestExit> Freezes(
        GraphScene scene, NodeView node, Dictionary<string, NodeView> nodes, HashSet<string> traced)
    {
        if (!node.Balances.Any(row => row.CanBeFrozen) || node.Node.Accounts.Count == 0)
        {
            yield break;
        }

        var graph = scene.Snapshot.Graph;
        var lines = graph.Edges.Where(edge => edge.Kind == GraphEdgeKind.Transfer).ToList();
        foreach (var asset in lines.Where(edge => edge.To == node.Key).Select(edge => edge.Amount.Asset).Distinct())
        {
            if (KnownTokens.IssuerOf(asset) is not { } issuer)
            {
                continue;
            }

            var left = Total(asset, lines.Where(edge => edge.To == node.Key))
                - Total(asset, lines.Where(edge => edge.From == node.Key));
            if (left.Units.Sign <= 0)
            {
                continue;
            }

            var target = new RequestTarget(
                issuer.Name, RequestTargetKind.TokenIssuer, node.Node.Chain, [node.Node.Accounts[0]]);
            var deposits = Deposits(scene.Snapshot, node, Incoming(graph, node.Key), traced, asset);
            yield return new RequestExit(
                node.Key, node.Look, RequestKind.Freeze, target, deposits,
                Trail(scene, node, nodes, traced), new RequestHolding(issuer.Name, left))
            {
                Name = $"{issuer.Name} · {SafeName(node)} · {asset.Symbol}",
                NodeAddresses = node.Node.Addresses,
            };
        }
    }

    /// <summary>
    /// Names an actor for a request: the name of the label that made it a cash-out, and otherwise its address
    /// shortened. Any other label (a victim, the user's own) is never written, since it may name a person.
    /// </summary>
    /// <param name="node">The actor.</param>
    /// <returns>The name.</returns>
    private static string SafeName(NodeView node)
    {
        var label = node.Labels.FirstOrDefault(label => CashOuts.ContainsKey(VisualLanguage.SortOf(label.Kind))
            && VisualLanguage.SortOf(label.Kind) == node.Look.Sort
            && !string.IsNullOrWhiteSpace(label.Name));
        return label?.Name ?? Short(node.Node.Addresses, node.Key);
    }

    /// <summary>Gives the transfer lines into an actor.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="key">The actor's key.</param>
    /// <returns>The lines.</returns>
    private static IEnumerable<GraphEdge> Incoming(TraceGraph graph, string key) =>
        graph.Edges.Where(edge => edge.Kind == GraphEdgeKind.Transfer && edge.To == key);

    /// <summary>Sums the amounts of one asset on lines.</summary>
    /// <param name="asset">The asset.</param>
    /// <param name="lines">The lines.</param>
    /// <returns>The sum.</returns>
    private static AssetAmount Total(Asset asset, IEnumerable<GraphEdge> lines) =>
        lines.Where(edge => edge.Amount.Asset == asset)
            .Aggregate(AssetAmount.Zero(asset), (sum, edge) => sum + edge.Amount);

    /// <summary>
    /// Gives the deposits to an actor, facts from the chain, each marked traced when traced money came in with it: on
    /// Bitcoin by its coin, on an account chain by its line, since the account trace counts per line.
    /// </summary>
    /// <param name="snapshot">What the graph was built from.</param>
    /// <param name="node">The actor.</param>
    /// <param name="incoming">The lines into it.</param>
    /// <param name="traced">The lines that carry traced money.</param>
    /// <param name="asset">Only this asset, or <see langword="null"/> for all.</param>
    /// <returns>The deposits, in time order, unconfirmed last.</returns>
    private static List<RequestDeposit> Deposits(
        GraphSnapshot snapshot,
        NodeView node,
        IEnumerable<GraphEdge> incoming,
        HashSet<string> traced,
        Asset? asset)
    {
        var tracedCoins = (snapshot.Trace?.Coins ?? [])
            .Where(coin => coin.Amount.Amount.Units.Sign > 0)
            .Select(coin => coin.Coin)
            .ToHashSet();
        var deposits = new List<RequestDeposit>();
        foreach (var edge in incoming)
        {
            foreach (var transfer in edge.Transfers.Where(t => asset is null || t.Amount.Asset == asset))
            {
                deposits.Add(new RequestDeposit(
                    transfer.Transaction.Id,
                    ReceiverOf(snapshot.Graph, node, transfer),
                    transfer.Amount,
                    transfer.Time,
                    transfer.Height,
                    transfer.Block?.Hash.Value ?? transfer.AccountBlock?.Hash,
                    IsTraced(transfer, edge, tracedCoins, traced)));
            }
        }

        return [.. deposits.OrderBy(deposit => deposit.Time ?? DateTimeOffset.MaxValue)
            .ThenBy(deposit => deposit.Transaction, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Says whether traced money came in with a transfer. On Bitcoin it is a coin the trace holds, or the only coin
    /// of a traced line; on an account chain every transfer of a traced line, since the account trace counts per line.
    /// </summary>
    /// <param name="transfer">The transfer.</param>
    /// <param name="edge">Its line.</param>
    /// <param name="tracedCoins">The coins the trace holds.</param>
    /// <param name="traced">The lines that carry traced money.</param>
    /// <returns>Whether it is traced.</returns>
    internal static bool IsTraced(
        AssetTransfer transfer, GraphEdge edge, HashSet<Outpoint> tracedCoins, HashSet<string> traced)
    {
        var lineTraced = traced.Contains(edge.Key);
        return transfer.Coin is { } coin
            ? tracedCoins.Contains(coin) || (lineTraced && edge.Transfers.Count == 1)
            : lineTraced;
    }

    /// <summary>Gives the address that received a transfer: a coin's own address, or the account's.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="node">The receiving actor.</param>
    /// <param name="transfer">The transfer.</param>
    /// <returns>The address.</returns>
    private static ChainAddress ReceiverOf(TraceGraph graph, NodeView node, AssetTransfer transfer)
    {
        if (transfer.Coin is { } coin
            && graph.Utxo.Transactions.TryGetValue(coin.Txid, out var known)
            && known.Transaction.Outputs.FirstOrDefault(output => output.Index == coin.Index)?.Address is { } address)
        {
            return address.ToChainAddress();
        }

        return node.Node.Addresses[0];
    }

    /// <summary>
    /// Finds the trail to an actor along traced money: the fewest hops back to a start point. The steps are written
    /// as shortened addresses and only the end by its cash-out name, so no name of a person is ever in it. A
    /// transaction drawn as a node of its own (a CoinJoin) is not an actor and is neither a step nor a hop.
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <param name="end">The actor.</param>
    /// <param name="nodes">Every actor, by key.</param>
    /// <param name="traced">The lines that carry traced money.</param>
    /// <returns>The trail, or <see langword="null"/> when traced money from no start point reaches it.</returns>
    private static RequestTrail? Trail(
        GraphScene scene, NodeView end, Dictionary<string, NodeView> nodes, HashSet<string> traced)
    {
        var graph = scene.Snapshot.Graph;
        var path = PathFromStart(graph, end.Key, traced);
        if (path is null)
        {
            return null;
        }

        var steps = path.Take(path.Count - 1)
            .Select(key => (Key: key, Node: ModelNode(graph, nodes, key)))
            .Where(step => step.Node is not { Kind: GraphNodeKind.Transaction })
            .Select(step => Short(step.Node?.Addresses ?? [], step.Key))
            .ToList();
        steps.Add(SafeName(end));
        return steps.Count < 2
            ? null
            : new RequestTrail(steps, scene.MethodText) { Swaps = SwapsOn(scene, path) };
    }

    /// <summary>
    /// Gives the swaps the traced money's value followed through on a trail (<see cref="SwapValue"/>), so that the
    /// request says the analysis, its share and the swap's transaction where the trail rests on one.
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <param name="path">The trail's node keys, from the start to the end.</param>
    /// <returns>The swaps, each once, in chain order of the trace.</returns>
    private static IReadOnlyList<RequestSwap> SwapsOn(GraphScene scene, IReadOnlyList<string> path)
    {
        var hops = path.Zip(path.Skip(1)).ToHashSet();
        var swaps = scene.Edges
            .Where(edge => hops.Contains((edge.Edge.From, edge.Edge.To)) && edge.TracedEdge is not null)
            .SelectMany(edge => edge.TracedEdge!.Amount.Swaps)
            .Where(conversion => conversion.Follows)
            .Select(conversion => conversion.Swap)
            .ToHashSet();
        return
        [
            .. (scene.Snapshot.AccountTrace?.Swaps ?? []).Where(conversion => swaps.Contains(conversion.Swap))
                .Select(conversion => new RequestSwap(
                    conversion.Swap.Transaction.Value,
                    SwapMarks.Of(conversion.Swap).Text,
                    conversion.ShareText,
                    SwapValue.Analysis)),
        ];
    }

    /// <summary>Gives a step's node in Tracing's model.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="nodes">Every actor drawn, by key.</param>
    /// <param name="key">The step's key.</param>
    /// <returns>The node, or <see langword="null"/> when the graph does not have it.</returns>
    private static GraphNode? ModelNode(TraceGraph graph, Dictionary<string, NodeView> nodes, string key) =>
        nodes.TryGetValue(key, out var node) ? node.Node : graph.Nodes.FirstOrDefault(other => other.Key == key);

    /// <summary>Walks back from an actor along traced lines to the nearest start point.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="end">The actor's key.</param>
    /// <param name="traced">The lines that carry traced money.</param>
    /// <returns>The keys from the start point to the actor, or <see langword="null"/>.</returns>
    private static List<string>? PathFromStart(TraceGraph graph, string end, HashSet<string> traced)
    {
        var into = graph.Edges.Where(edge => traced.Contains(edge.Key))
            .GroupBy(edge => edge.To, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key, group => group.Select(edge => edge.From).ToList(), StringComparer.Ordinal);
        var starts = graph.Nodes.Where(node => node.IsStartPoint).Select(node => node.Key)
            .ToHashSet(StringComparer.Ordinal);
        var cameFrom = new Dictionary<string, string>(StringComparer.Ordinal) { [end] = end };
        var queue = new Queue<string>([end]);
        while (queue.Count > 0)
        {
            var key = queue.Dequeue();
            if (starts.Contains(key) && key != end)
            {
                return Unwind(cameFrom, key, end);
            }

            foreach (var from in into.GetValueOrDefault(key) ?? [])
            {
                if (cameFrom.TryAdd(from, key))
                {
                    queue.Enqueue(from);
                }
            }
        }

        return null;
    }

    /// <summary>Reads the path from the start point to the end out of where each step came from.</summary>
    /// <param name="cameFrom">Each actor's next step towards the end.</param>
    /// <param name="start">The start point.</param>
    /// <param name="end">The end.</param>
    /// <returns>The keys from the start to the end.</returns>
    private static List<string> Unwind(Dictionary<string, string> cameFrom, string start, string end)
    {
        var path = new List<string> { start };
        for (var key = start; key != end; key = cameFrom[key])
        {
            path.Add(cameFrom[key]);
        }

        return path;
    }

    /// <summary>Writes an actor as its first address shortened, never a name; or its key's subject without one.</summary>
    /// <param name="addresses">The actor's addresses.</param>
    /// <param name="key">Its key, for an actor without an address.</param>
    /// <returns>For example <c>bc1q…x0wl</c>.</returns>
    private static string Short(IReadOnlyList<ChainAddress> addresses, string key) =>
        addresses.Count > 0
            ? GraphScene.Shorten(addresses[0].Value)
            : GraphScene.Shorten(key[(key.IndexOf(':', StringComparison.Ordinal) + 1)..]);
}
