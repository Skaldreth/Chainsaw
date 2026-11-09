using System.Globalization;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>How an area's edge is drawn: hel, streckad for a suggestion that waits, prickad for time.</summary>
public enum AreaLine
{
    /// <summary>A whole edge: a check, a decided suggestion or the user's own area.</summary>
    Solid,

    /// <summary>A dashed edge: a suggestion that waits for a decision (dashed always means uncertain).</summary>
    Dashed,

    /// <summary>A dotted edge without fill: time.</summary>
    Dotted,
}

/// <summary>An area behind the actors (sketch KF1b).</summary>
/// <param name="Key">
/// The area's stable key: the check's, <c>own:</c> and the area's id, or the sort and its actors.
/// </param>
/// <param name="Sort">The sort the menu shows it under.</param>
/// <param name="Look">Its icon and tint.</param>
/// <param name="Nodes">The actors it is drawn around, in the graph's order.</param>
/// <param name="Line">How its edge is drawn.</param>
/// <param name="IsFilled">
/// Whether it is filled; an area whose actors already sit on another's fill is only an edge.
/// </param>
/// <param name="Label">Its name, shown on hover and when it is chosen.</param>
/// <param name="Own">The own area it is, or <see langword="null"/>.</param>
/// <param name="CheckKey">The check or suggestion it belongs to, or <see langword="null"/>.</param>
public sealed record AreaView(
    string Key,
    AreaSort Sort,
    AreaLook Look,
    IReadOnlyList<string> Nodes,
    AreaLine Line,
    bool IsFilled,
    string Label,
    OwnArea? Own,
    string? CheckKey)
{
    /// <summary>Gets whether it is the user's own area, drawn with a person on its chip.</summary>
    public bool IsOwn => Own is not null;
}

/// <summary>
/// Builds the areas behind the actors from the graph, the checks and the own areas (sketch KF1b): obfuscation and
/// wallets from the checks, services from the labels, another chain from the user's links, time from the transfers'
/// blocks, and the own areas; at most one fill per actor.
/// </summary>
public static class GraphAreas
{
    /// <summary>How close in time the transfers of a time area are: within two hours of each other.</summary>
    public static readonly TimeSpan TimeWindow = TimeSpan.FromHours(2);

    /// <summary>The fewest actors a time area holds, so that it says more than one line does.</summary>
    public const int FewestInTime = 3;

    /// <summary>The start of an own area's key, before its id.</summary>
    public const string OwnKeyPrefix = "own:";

    /// <summary>Gives an own area's key, which its chip and the trash share.</summary>
    /// <param name="id">The area's id.</param>
    /// <returns>For example <c>own:4a5e…</c>.</returns>
    public static string OwnKey(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        return OwnKeyPrefix + id;
    }

    /// <summary>Gives a check's key, which the list and the areas share.</summary>
    /// <param name="check">The check.</param>
    /// <returns>For example <c>mixer-outputs v1/4a5e…:0</c>.</returns>
    public static string KeyOf(StoredCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return KeyOf(check.Analysis, check.Finding.Key);
    }

    /// <summary>Gives the key of a finding's area, from the analysis and the finding's own key.</summary>
    /// <param name="analysis">The analysis and its version.</param>
    /// <param name="findingKey">The finding's key.</param>
    /// <returns>The key, for example <c>mixer-outputs v1/deposit:0</c>.</returns>
    public static string KeyOf(HeuristicId analysis, string findingKey)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return $"{analysis}/{findingKey}";
    }

    /// <summary>Builds every area, in the order they are drawn, with the fill rule applied.</summary>
    /// <param name="scene">The graph.</param>
    /// <param name="checks">The checks and suggestions.</param>
    /// <param name="own">The own areas.</param>
    /// <param name="zone">The time zone the time areas are written in.</param>
    /// <returns>The areas.</returns>
    public static IReadOnlyList<AreaView> Build(
        GraphScene scene, IReadOnlyList<StoredCheck> checks, IReadOnlyList<OwnArea> own, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(own);
        var areas = new List<AreaView>();
        areas.AddRange(own.Select(area => OfOwn(scene, area)).OfType<AreaView>());
        areas.AddRange(checks.Select(check => OfCheck(scene, check)).OfType<AreaView>());
        areas.AddRange(AccountClusters(scene));
        areas.AddRange(Services(scene));
        areas.AddRange(OtherChains(scene));
        areas.AddRange(Times(scene, zone));
        return Fill(areas);
    }

    /// <summary>
    /// Applies the fill rule: at most one fill per actor, in the order given; an area whose actors already sit on a
    /// fill is drawn as an edge only, and a time area is never filled.
    /// </summary>
    /// <param name="areas">The areas, in the order they are drawn.</param>
    /// <returns>The same areas, each filled or not.</returns>
    public static IReadOnlyList<AreaView> Fill(IEnumerable<AreaView> areas)
    {
        ArgumentNullException.ThrowIfNull(areas);
        var filled = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<AreaView>();
        foreach (var area in areas)
        {
            var fill = area.Line != AreaLine.Dotted && !area.Nodes.Any(filled.Contains);
            if (fill)
            {
                filled.UnionWith(area.Nodes);
            }

            result.Add(area with { IsFilled = fill });
        }

        return result;
    }

    /// <summary>Gives the actors that hold addresses and transactions, in the graph's order.</summary>
    /// <param name="scene">The graph.</param>
    /// <param name="addresses">The addresses.</param>
    /// <param name="transactions">The transactions that are actors of their own.</param>
    /// <returns>The nodes' keys.</returns>
    public static IReadOnlyList<string> NodesOf(
        GraphScene scene, IEnumerable<BitcoinAddress> addresses, IEnumerable<Txid> transactions)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        return NodesOf(scene, addresses.Select(address => address.ToChainAddress()), transactions);
    }

    /// <summary>
    /// Gives the actors that hold addresses on any chain (Bitcoin addresses and Ethereum accounts) and transactions,
    /// in the graph's order.
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <param name="addresses">The addresses, each with its chain.</param>
    /// <param name="transactions">The transactions that are actors of their own.</param>
    /// <returns>The nodes' keys.</returns>
    public static IReadOnlyList<string> NodesOf(
        GraphScene scene, IEnumerable<ChainAddress> addresses, IEnumerable<Txid> transactions)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var graph = scene.Snapshot.Graph;
        var keys = addresses.Select(graph.NodeOf).OfType<GraphNode>().Select(node => node.Key)
            .ToHashSet(StringComparer.Ordinal);
        var txids = transactions.ToHashSet();
        keys.UnionWith(graph.Nodes.Where(n => n.Transaction is { } txid && txids.Contains(txid)).Select(n => n.Key));
        return [.. graph.Nodes.Select(node => node.Key).Where(keys.Contains)];
    }

    /// <summary>Gives the members of an own area for chosen actors: their addresses, or the transaction.</summary>
    /// <param name="scene">The graph.</param>
    /// <param name="nodes">The chosen actors' keys.</param>
    /// <returns>The members.</returns>
    public static IReadOnlyList<string> MembersOf(GraphScene scene, IEnumerable<string> nodes)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return
        [
            .. nodes.Select(scene.Node).OfType<NodeView>().SelectMany(node => MembersOf(node.Node))
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Gives one actor's members: its addresses on any chain (a Bitcoin cluster's or an Ethereum account), or the
    /// transaction when it is an actor of its own.
    /// </summary>
    /// <param name="node">The actor.</param>
    /// <returns>The members.</returns>
    private static IEnumerable<string> MembersOf(GraphNode node)
    {
        if (node.Addresses.Count > 0)
        {
            return node.Addresses.Select(CheckRules.MemberOf);
        }

        return node.Transaction is { } txid ? [CheckRules.MemberOf(txid)] : [];
    }

    /// <summary>Gives a check's area: around its actors, without the candidates the user dismissed.</summary>
    /// <param name="scene">The graph.</param>
    /// <param name="check">The check.</param>
    /// <returns>
    /// The area, or <see langword="null"/> when it has no actor in the graph or the user dismissed it all.
    /// </returns>
    private static AreaView? OfCheck(GraphScene scene, StoredCheck check)
    {
        var finding = check.Finding;
        bool Dismissed(CheckCandidate c) => check.DecisionFor(c.Key)?.Verdict == CheckVerdict.Dismissed;
        var kept = finding.Candidates.Where(c => !Dismissed(c)).Select(c => c.Address).ToHashSet();
        var dismissed = finding.Candidates.Where(Dismissed).Select(c => c.Address).Where(a => !kept.Contains(a))
            .ToHashSet();
        var allDismissed = check.DecisionFor(null)?.Verdict == CheckVerdict.Dismissed
            || (finding.Candidates.Count > 0 && finding.Candidates.All(Dismissed));
        if (finding.Kind == CheckKind.Suggestion && allDismissed)
        {
            return null;
        }

        var nodes = NodesOf(scene, finding.Addresses.Where(a => !dismissed.Contains(a)), finding.Transactions);
        var fewest = finding.Area == AreaSort.Wallet ? 2 : 1;
        if (nodes.Count < fewest)
        {
            return null;
        }

        // A suggestion stays dashed also when decided: an approved candidate is the user's judgement, never a fact.
        var line = finding.Kind == CheckKind.Suggestion ? AreaLine.Dashed : AreaLine.Solid;
        return new AreaView(
            KeyOf(check),
            finding.Area,
            AreaLooks.OfAnalysis(check.Analysis.Name, finding.Area),
            nodes,
            line,
            false,
            finding.Title,
            null,
            KeyOf(check));
    }

    /// <summary>Gives an own area, around the actors that hold its members now.</summary>
    /// <param name="scene">The graph.</param>
    /// <param name="area">The own area.</param>
    /// <returns>
    /// The area, or <see langword="null"/> when none of its actors is in the graph or it lies in the trash (sketch
    /// SO1a), where it is only hidden.
    /// </returns>
    private static AreaView? OfOwn(GraphScene scene, OwnArea area)
    {
        if (scene.Trash.Hides(OwnKey(area.Id)))
        {
            return null;
        }

        var addresses = new List<ChainAddress>();
        var txids = new List<Txid>();
        foreach (var member in area.Members)
        {
            if (CheckRules.AddressOf(member) is { } address)
            {
                addresses.Add(address);
            }
            else if (member.StartsWith(CheckRules.TransactionMember, StringComparison.Ordinal)
                && Txid.TryParseStored(member[CheckRules.TransactionMember.Length..], out var txid))
            {
                txids.Add(txid);
            }
        }

        var nodes = NodesOf(scene, addresses, txids);
        return nodes.Count == 0
            ? null
            : new AreaView(
                OwnKey(area.Id),
                area.Sort,
                new AreaLook(AreaLooks.AreaIcon, area.Tint),
                nodes,
                AreaLine.Solid,
                false,
                area.Name,
                area,
                null);
    }

    /// <summary>Gives an area for each service whose label is on more than one actor.</summary>
    /// <param name="scene">The graph.</param>
    /// <returns>The areas.</returns>
    private static IEnumerable<AreaView> Services(GraphScene scene) =>
        scene.Nodes
            .Select(node => (node.Key, Label: node.Labels.FirstOrDefault(label => label.Name is not null
                && label.Kind.Group() is LabelGroup.Withdrawal or LabelGroup.Spending)))
            .Where(item => item.Label is not null)
            .GroupBy(item => item.Label!.Name!.Trim().ToLowerInvariant(), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => new AreaView(
                "service:" + group.Key,
                AreaSort.Service,
                AreaLooks.Of(AreaSort.Service),
                [.. group.Select(item => item.Key)],
                AreaLine.Solid,
                false,
                $"{group.First().Label!.Name} · {group.Count().ToString(CultureInfo.InvariantCulture)} aktörer",
                null,
                null));

    /// <summary>
    /// Gives an area behind the accounts of each unfolded account cluster (sketch KE1), in the service's tone with a
    /// whole edge; its key is the cluster's, so that a click on its chip folds it back to the pile.
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <returns>The areas.</returns>
    private static IEnumerable<AreaView> AccountClusters(GraphScene scene) =>
        scene.Snapshot.Graph.AccountClusters
            .Where(cluster => cluster.IsOpen)
            .Select(cluster => new AreaView(
                cluster.Key,
                AreaSort.Service,
                AreaLooks.Of(AreaSort.Service),
                NodesOf(scene, cluster.Members, []),
                AreaLine.Solid,
                false,
                AccountClusterLabel(cluster),
                null,
                null))
            .Where(area => area.Nodes.Count > 0);

    /// <summary>Names an account cluster's area: a likely service and how many accounts, as in sketch KE1.</summary>
    /// <param name="cluster">The cluster.</param>
    /// <returns>For example <c>Trolig tjänst · 4 adresser</c>.</returns>
    public static string AccountClusterLabel(GraphAccountCluster cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        return "Trolig tjänst · " + cluster.Members.Count.ToString(CultureInfo.InvariantCulture) + " adresser";
    }

    /// <summary>Tells whether an area is an unfolded account cluster's, which is always drawn.</summary>
    /// <param name="area">The area.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsAccountCluster(AreaView area)
    {
        ArgumentNullException.ThrowIfNull(area);
        return area.Key.StartsWith(GraphAccountCluster.KeyPrefix, StringComparison.Ordinal);
    }

    /// <summary>Gives an area for each place where the money continues on another chain, by the user's link.</summary>
    /// <param name="scene">The graph.</param>
    /// <returns>The areas.</returns>
    private static IEnumerable<AreaView> OtherChains(GraphScene scene) =>
        scene.Edges
            .Where(edge => edge.Edge.Kind == GraphEdgeKind.ContinuesOnOtherChain)
            .Select(edge => new AreaView(
                "chain:" + edge.Key,
                AreaSort.OtherChain,
                AreaLooks.Of(AreaSort.OtherChain),
                [edge.Edge.From, edge.Edge.To],
                AreaLine.Solid,
                false,
                "Annan kedja · " + (scene.Node(edge.Edge.To)?.ChainName ?? "länkad av användaren"),
                null,
                null));

    /// <summary>
    /// Gives an area for each group of actors the money passed within <see cref="TimeWindow"/>: the transfers are
    /// taken in time order, and a transfer joins its two actors' groups when the group's first and last time stay
    /// within the window.
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <param name="zone">The time zone the times are written in.</param>
    /// <returns>The areas.</returns>
    private static IEnumerable<AreaView> Times(GraphScene scene, TimeZoneInfo zone)
    {
        var group = new Dictionary<string, TimeGroup>(StringComparer.Ordinal);
        var edges = scene.Edges
            .Where(edge => edge.Edge.Kind == GraphEdgeKind.Transfer && edge.Edge.FirstTime is not null)
            .OrderBy(edge => edge.Edge.FirstTime)
            .ThenBy(edge => edge.Key, StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            var time = edge.Edge.FirstTime!.Value;
            var from = group.GetValueOrDefault(edge.Edge.From) ?? new TimeGroup([edge.Edge.From], time, time);
            var to = group.GetValueOrDefault(edge.Edge.To) ?? new TimeGroup([edge.Edge.To], time, time);
            var first = Min(Min(from.First, to.First), time);
            var last = Max(Max(from.Last, to.Last), time);
            if (last - first > TimeWindow)
            {
                continue;
            }

            var joined = new TimeGroup([.. from.Nodes.Union(to.Nodes, StringComparer.Ordinal)], first, last);
            foreach (var node in joined.Nodes)
            {
                group[node] = joined;
            }
        }

        var order = scene.Nodes.Select((node, index) => (node.Key, index))
            .ToDictionary(item => item.Key, item => item.index, StringComparer.Ordinal);
        return group.Values.Distinct()
            .Where(found => found.Nodes.Count >= FewestInTime)
            .Select(found => found with { Nodes = [.. found.Nodes.OrderBy(node => order.GetValueOrDefault(node))] })
            .OrderBy(found => found.First)
            .Select(found => new AreaView(
                "time:" + found.Nodes[0],
                AreaSort.Time,
                AreaLooks.Of(AreaSort.Time),
                found.Nodes,
                AreaLine.Dotted,
                false,
                TimeLabel(found, zone),
                null,
                null));
    }

    /// <summary>Writes a time area's name: the window and the first and last time.</summary>
    /// <param name="group">The group.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>For example <c>Inom 2 h · 10:14–12:02</c>.</returns>
    private static string TimeLabel(TimeGroup group, TimeZoneInfo zone)
    {
        var first = TimeZoneInfo.ConvertTime(group.First, zone);
        var last = TimeZoneInfo.ConvertTime(group.Last, zone);
        var span = Math.Max(1, (int)Math.Ceiling((group.Last - group.First).TotalHours));
        return string.Create(
            CultureInfo.InvariantCulture, $"Inom {span} h · {first:HH:mm}–{last:HH:mm}");
    }

    /// <summary>Gives the earlier of two times.</summary>
    /// <param name="a">One.</param>
    /// <param name="b">The other.</param>
    /// <returns>The earlier.</returns>
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a <= b ? a : b;

    /// <summary>Gives the later of two times.</summary>
    /// <param name="a">One.</param>
    /// <param name="b">The other.</param>
    /// <returns>The later.</returns>
    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a >= b ? a : b;

    /// <summary>A group of actors the money passed within the window.</summary>
    /// <param name="Nodes">The actors.</param>
    /// <param name="First">The first transfer's time.</param>
    /// <param name="Last">The last transfer's time.</param>
    private sealed record TimeGroup(IReadOnlyList<string> Nodes, DateTimeOffset First, DateTimeOffset Last);
}
