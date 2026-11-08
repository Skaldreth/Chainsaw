using System.Globalization;
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
/// <param name="Deposits">The deposits to it in the graph, in time order; traced ones are taken along at first.</param>
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
    /// <summary>Gets the row's key: the node and the kind, since an address can be a cash-out and frozen.</summary>
    public string Key => $"{Kind}:{NodeKey}";

    /// <summary>Gets the name the row shows: the recipient, or for a freeze the address's actor.</summary>
    public string Name { get; init; } = Target.Name;

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
}

/// <summary>
/// Finds the requests that can be made in the graph shown (sketch BG1b), without WPF: every cash-out (the blue sorts)
/// that traced money reached, and every address where a token is left that its issuer can freeze (sketch TK1a). What
/// a request may hold is decided here: the trail's steps are shortened addresses, never a name, so a victim's name
/// can never reach a request (CLAUDE.md, BG1b).
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

    /// <summary>Finds the requests that can be made: the cash-outs first, then the freezes, in the graph's order.
    /// </summary>
    /// <param name="scene">The graph shown.</param>
    /// <returns>The places.</returns>
    public static IReadOnlyList<RequestExit> Find(GraphScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
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

            if (Freeze(scene, node, nodes, traced) is { } freeze)
            {
                freezes.Add(freeze);
            }
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

    /// <summary>Makes the request for a cash-out that traced money reached; none for any other actor.</summary>
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

        var deposits = Deposits(scene.Snapshot, node, incoming, traced, asset: null);
        var addresses = deposits.Select(deposit => deposit.To).Distinct().ToList();
        var target = new RequestTarget(
            node.Name, kind, node.Node.Chain, addresses.Count > 0 ? addresses : [node.Node.Addresses[0]]);
        return new RequestExit(
            node.Key, node.Look, RequestKind.Information, target, deposits, Trail(scene, node, nodes, traced), null);
    }

    /// <summary>
    /// Makes the freeze for an address where a token is left that its issuer can freeze, as the inspector's snowflake
    /// says (sketch TK1a); none for any other actor.
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <param name="node">The actor.</param>
    /// <param name="nodes">Every actor, by key.</param>
    /// <param name="traced">The lines that carry traced money.</param>
    /// <returns>The place, or <see langword="null"/>.</returns>
    private static RequestExit? Freeze(
        GraphScene scene, NodeView node, Dictionary<string, NodeView> nodes, HashSet<string> traced)
    {
        if (!node.Balances.Any(row => row.CanBeFrozen) || node.Node.Accounts.Count == 0)
        {
            return null;
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
            return new RequestExit(
                node.Key, node.Look, RequestKind.Freeze, target, deposits,
                Trail(scene, node, nodes, traced), new RequestHolding(issuer.Name, left))
            {
                Name = $"{issuer.Name} · {node.Name}",
            };
        }

        return null;
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
                var isTraced = IsTraced(transfer, edge, tracedCoins, traced);
                deposits.Add(new RequestDeposit(
                    transfer.Transaction.Id,
                    ReceiverOf(snapshot.Graph, node, transfer),
                    transfer.Amount,
                    transfer.Time,
                    transfer.Height,
                    transfer.Block?.Hash.Value ?? transfer.AccountBlock?.Hash,
                    isTraced));
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
    private static bool IsTraced(
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
    /// as shortened addresses and only the end by its name, so no name of a person is ever in it.
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <param name="end">The actor.</param>
    /// <param name="nodes">Every actor, by key.</param>
    /// <param name="traced">The lines that carry traced money.</param>
    /// <returns>The trail, or <see langword="null"/> when traced money from no start point reaches it.</returns>
    private static RequestTrail? Trail(
        GraphScene scene, NodeView end, Dictionary<string, NodeView> nodes, HashSet<string> traced)
    {
        var path = PathFromStart(scene.Snapshot.Graph, end.Key, traced);
        if (path is null || path.Count < 2)
        {
            return null;
        }

        var steps = path.Take(path.Count - 1).Select(key => Step(scene.Snapshot.Graph, nodes, key)).ToList();
        steps.Add(end.Name);
        return new RequestTrail(steps, scene.MethodText);
    }

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

    /// <summary>Writes one step of the trail as its address shortened, never a name.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="nodes">Every actor, by key.</param>
    /// <param name="key">The step's key.</param>
    /// <returns>For example <c>bc1q…x0wl</c>.</returns>
    private static string Step(TraceGraph graph, Dictionary<string, NodeView> nodes, string key)
    {
        var addresses = nodes.TryGetValue(key, out var node)
            ? node.Node.Addresses
            : graph.Nodes.FirstOrDefault(other => other.Key == key)?.Addresses ?? [];
        return addresses.Count > 0
            ? GraphScene.Shorten(addresses[0].Value)
            : GraphScene.Shorten(key[(key.IndexOf(':', StringComparison.Ordinal) + 1)..]);
    }

    /// <summary>Writes when a deposit came, for the chips in the view: the local day and time.</summary>
    /// <param name="deposit">The deposit.</param>
    /// <param name="zone">The local zone.</param>
    /// <returns>For example <c>28 okt 21:14</c>, or <c>obekräftad</c>.</returns>
    public static string When(RequestDeposit deposit, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(deposit);
        ArgumentNullException.ThrowIfNull(zone);
        return deposit.Time is { } time
            ? TimeZoneInfo.ConvertTime(time, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "obekräftad";
    }
}
