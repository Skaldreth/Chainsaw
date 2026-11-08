using System.Numerics;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Requests.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>
/// Finds a report's trails between the actors marked in the graph (sketch RP1b), without WPF. A trail is everything
/// that leads to one end along traced money between marked actors: every start, every branch and money that turns
/// back, so that nothing marked is dropped silently. An end is a marked actor the traced money reaches where it leaves
/// the chain or the trail ends (a cash-out, a risk, spending, another chain), or that the money leaves for no other
/// marked actor it does not come back from. A transaction drawn as a node of its own (a CoinJoin) is passed through
/// and is never a step or a hop.
/// </summary>
/// <param name="scene">The graph.</param>
/// <param name="actors">The marked actors, in the graph's order.</param>
/// <param name="rateAt">The case's rate of an asset at a time, or <see langword="null"/> when it has none.</param>
internal sealed class ReportTrails(
    GraphScene scene, IReadOnlyList<NodeView> actors, Func<Asset, DateTimeOffset, Price?> rateAt)
{
    /// <summary>The marked actors by key.</summary>
    private readonly Dictionary<string, NodeView> _marked =
        actors.ToDictionary(node => node.Key, StringComparer.Ordinal);

    /// <summary>The traced lines a trail may follow, by the node they go into.</summary>
    private readonly Dictionary<string, List<EdgeView>> _into = new(StringComparer.Ordinal);

    /// <summary>The traced lines a trail may follow, by the node they come from.</summary>
    private readonly Dictionary<string, List<EdgeView>> _outOf = new(StringComparer.Ordinal);

    /// <summary>The coins the trace holds, which tell a traced transfer on Bitcoin.</summary>
    private readonly HashSet<Outpoint> _tracedCoins = [.. (scene.Snapshot.Trace?.Coins ?? [])
        .Where(coin => coin.Amount.Amount.Units.Sign > 0).Select(coin => coin.Coin)];

    /// <summary>The keys of the lines that carry traced money.</summary>
    private readonly HashSet<string> _traced = [.. scene.Edges.Where(e => e.IsTraced).Select(e => e.Key)];

    /// <summary>Finds the trails, one per end, in the graph's order of their ends.</summary>
    /// <returns>The trails.</returns>
    public IReadOnlyList<ReportTrail> Find()
    {
        foreach (var edge in scene.Edges.Where(IsFollowed))
        {
            Add(_into, edge.Edge.To, edge);
            Add(_outOf, edge.Edge.From, edge);
        }

        var trails = new List<ReportTrail>();
        var ends = new List<string>();
        foreach (var end in actors.Where(IsEnd))
        {
            // Of actors the money circles between, with nowhere else to go, the first stands for them all.
            if (EndOf(end) == ReportTrailEnd.Open && ends.Any(other => Circles(other, end.Key)))
            {
                continue;
            }

            ends.Add(end.Key);
            if (Trail(end) is { } trail)
            {
                trails.Add(trail);
            }
        }

        return trails;
    }

    /// <summary>Says how a trail ends, by its end's sort and marks.</summary>
    /// <param name="end">The end.</param>
    /// <returns>The end.</returns>
    internal static ReportTrailEnd EndOf(NodeView end)
    {
        if (end.Badges.Contains(NodeBadge.Sanctioned) || end.Look.Sort == NodeSort.Sanctioned)
        {
            return ReportTrailEnd.Sanctioned;
        }

        return end.Look.Ink switch
        {
            GraphInk.CashOut => ReportTrailEnd.CashOut,
            GraphInk.Risk or GraphInk.Sanctioned => ReportTrailEnd.Risk,
            GraphInk.Spent => ReportTrailEnd.Spent,
            GraphInk.OtherChain => ReportTrailEnd.OtherChain,
            _ => ReportTrailEnd.Open,
        };
    }

    /// <summary>Adds a line to an index.</summary>
    /// <param name="index">The index.</param>
    /// <param name="key">The node.</param>
    /// <param name="edge">The line.</param>
    private static void Add(Dictionary<string, List<EdgeView>> index, string key, EdgeView edge)
    {
        if (!index.TryGetValue(key, out var list))
        {
            index[key] = list = [];
        }

        list.Add(edge);
    }

    /// <summary>
    /// Says whether a trail may follow a line: a transfer of traced money between marked actors, or through a
    /// transaction node.
    /// </summary>
    /// <param name="edge">The line.</param>
    /// <returns><see langword="true"/> when it may.</returns>
    private bool IsFollowed(EdgeView edge) =>
        edge.IsTraced && edge.Edge.Kind == GraphEdgeKind.Transfer
        && IsPassable(edge.Edge.From) && IsPassable(edge.Edge.To);

    /// <summary>Says whether a trail may pass a node: a marked actor, or a transaction node.</summary>
    /// <param name="key">The node.</param>
    /// <returns><see langword="true"/> when it may.</returns>
    private bool IsPassable(string key) =>
        _marked.ContainsKey(key) || scene.Node(key)?.Node.Kind == GraphNodeKind.Transaction;

    /// <summary>
    /// Says whether a marked actor ends a trail: traced money reaches it, and it is where the money leaves the chain or
    /// the trail ends, or every marked actor the money goes on to sends it back.
    /// </summary>
    /// <param name="node">The actor.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private bool IsEnd(NodeView node) =>
        _into.ContainsKey(node.Key)
        && (EndOf(node) != ReportTrailEnd.Open || Onward(node.Key).All(other => Onward(other).Contains(node.Key)));

    /// <summary>Says whether the money circles between two actors: each reaches the other.</summary>
    /// <param name="first">The one.</param>
    /// <param name="second">The other.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private bool Circles(string first, string second) =>
        Onward(first).Contains(second) && Onward(second).Contains(first);

    /// <summary>Gives the other marked actors the traced money goes on to from one.</summary>
    /// <param name="key">The actor.</param>
    /// <returns>The actors.</returns>
    private HashSet<string> Onward(string key) =>
        [.. Reach(key, _outOf, edge => edge.Edge.To).Where(other => other != key && _marked.ContainsKey(other))];

    /// <summary>Gives every node reached from one along the followed lines, in one direction.</summary>
    /// <param name="start">The node.</param>
    /// <param name="index">The lines out of each node in the direction walked.</param>
    /// <param name="next">The node at a line's far end.</param>
    /// <returns>The nodes reached, without the start unless the walk comes back to it.</returns>
    private static IEnumerable<string> Reach(
        string start, Dictionary<string, List<EdgeView>> index, Func<EdgeView, string> next)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>([start]);
        while (queue.Count > 0)
        {
            foreach (var edge in index.GetValueOrDefault(queue.Dequeue()) ?? [])
            {
                var key = next(edge);
                if (seen.Add(key))
                {
                    queue.Enqueue(key);
                    yield return key;
                }
            }
        }
    }

    /// <summary>
    /// Makes the trail to an end: every followed line between the nodes the money reaches the end from, its starts,
    /// a step per transfer into a marked actor, and how much of the traced money reached the end.
    /// </summary>
    /// <param name="end">The end.</param>
    /// <returns>The trail, or <see langword="null"/> when no transfer is found.</returns>
    private ReportTrail? Trail(NodeView end)
    {
        var feeding = Reach(end.Key, _into, edge => edge.Edge.From).Append(end.Key).ToHashSet(StringComparer.Ordinal);
        var lines = feeding.SelectMany(key => _outOf.GetValueOrDefault(key) ?? [])
            .Where(edge => feeding.Contains(edge.Edge.To))
            .Distinct()
            .ToList();
        var steps = lines.Where(edge => _marked.ContainsKey(edge.Edge.To))
            .SelectMany(edge => Steps(edge, lines))
            .OrderBy(step => step.Time ?? DateTimeOffset.MaxValue)
            .ThenBy(step => step.Transaction, StringComparer.Ordinal)
            .ToList();
        if (steps.Count == 0)
        {
            return null;
        }

        var arriving = lines.Where(edge => edge.Edge.To == end.Key).ToList();
        return new ReportTrail(Starts(end.Key, feeding, lines), Name(end.Key), EndOf(end), steps, Traced(arriving))
        {
            Value = Value(arriving),
        };
    }

    /// <summary>
    /// Names a trail's starts: the marked start points and the marked actors no line of the trail comes into, or the
    /// first marked actor when the money only circles.
    /// </summary>
    /// <param name="end">The end.</param>
    /// <param name="feeding">The nodes the money reaches the end from.</param>
    /// <param name="lines">The trail's lines.</param>
    /// <returns>The names, joined.</returns>
    private string Starts(string end, HashSet<string> feeding, IReadOnlyList<EdgeView> lines)
    {
        var candidates = actors.Where(node => node.Key != end && feeding.Contains(node.Key)).ToList();
        var starts = candidates
            .Where(node => node.IsStartPoint || lines.All(edge => edge.Edge.To != node.Key))
            .ToList();
        var named = starts.Count > 0 ? starts : candidates.Take(1).ToList();
        return named.Count > 0 ? string.Join(", ", named.Select(node => Name(node.Key))) : Name(end);
    }

    /// <summary>Makes the steps of a line into a marked actor: one per transfer, named from its senders.</summary>
    /// <param name="edge">The line.</param>
    /// <param name="lines">The trail's lines, through which a transaction node's senders are found.</param>
    /// <returns>The steps.</returns>
    private IEnumerable<ReportStep> Steps(EdgeView edge, IReadOnlyList<EdgeView> lines)
    {
        var sender = string.Join(" + ", Senders(edge.Edge.From, lines).Select(Name));
        var receiver = Name(edge.Edge.To);
        var (transfers, isTraced) = Transfers(edge);
        return transfers.Select(transfer => new ReportStep(
            sender,
            receiver,
            transfer.Transaction.Id,
            transfer.Amount,
            transfer.Time,
            transfer.Height,
            transfer.Block?.Hash.Value ?? transfer.AccountBlock?.Hash)
        {
            IsTraced = isTraced,
        });
    }

    /// <summary>Gives the marked actors that sent through a node: itself, or a transaction node's senders.</summary>
    /// <param name="key">The node.</param>
    /// <param name="lines">The trail's lines.</param>
    /// <returns>The senders, in the graph's order.</returns>
    private IReadOnlyList<string> Senders(string key, IReadOnlyList<EdgeView> lines)
    {
        if (_marked.ContainsKey(key))
        {
            return [key];
        }

        // Back through transaction nodes only: a marked actor is a sender, and the walk stops there.
        var found = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal) { key };
        var queue = new Queue<string>([key]);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var edge in lines.Where(edge => edge.Edge.To == node && seen.Add(edge.Edge.From)))
            {
                if (_marked.ContainsKey(edge.Edge.From))
                {
                    found.Add(edge.Edge.From);
                }
                else
                {
                    queue.Enqueue(edge.Edge.From);
                }
            }
        }

        return [.. actors.Select(node => node.Key).Where(found.Contains)];
    }

    /// <summary>
    /// Gives the transfers along a line that carry traced money; all of them, marked not traced, when none can be
    /// told.
    /// </summary>
    /// <param name="edge">The line.</param>
    /// <returns>The transfers and whether they are told traced.</returns>
    private (IReadOnlyList<AssetTransfer> Transfers, bool IsTraced) Transfers(EdgeView edge)
    {
        var traced = edge.Edge.Transfers
            .Where(transfer => RequestExits.IsTraced(transfer, edge.Edge, _tracedCoins, _traced))
            .ToList();
        return traced.Count > 0 ? (traced, true) : (edge.Edge.Transfers, false);
    }

    /// <summary>Sums the traced money on the lines into the end, one amount per asset.</summary>
    /// <param name="arriving">The lines into the end.</param>
    /// <returns>The amounts.</returns>
    private static IReadOnlyList<AssetAmount> Traced(IReadOnlyList<EdgeView> arriving) =>
        [.. arriving.Where(edge => edge.IsTraced)
            .GroupBy(edge => edge.Traced.Asset)
            .Select(group => group.Aggregate(AssetAmount.Zero(group.Key), (sum, edge) => sum + edge.Traced))];

    /// <summary>
    /// Values the traced money on the lines into the end at the rates of the case when it moved: each line's traced
    /// share of its traced transfers, each transfer at the rate of its own time. A missing rate, time or another
    /// currency gives no value, never a guess.
    /// </summary>
    /// <param name="arriving">The lines into the end.</param>
    /// <returns>The value as the graph writes it, for example <c>≈ 263 100 kr</c>, or <see langword="null"/>.</returns>
    private string? Value(IReadOnlyList<EdgeView> arriving)
    {
        var total = 0m;
        string? currency = null;
        foreach (var edge in arriving.Where(edge => edge.IsTraced))
        {
            var (transfers, _) = Transfers(edge);
            var sent = transfers.Aggregate(BigInteger.Zero, (sum, transfer) => sum + transfer.Amount.Units);
            if (sent.Sign <= 0 || transfers.Any(transfer => transfer.Amount.Asset != edge.Traced.Asset))
            {
                return null;
            }

            var share = Math.Min(1m, (decimal)edge.Traced.Units / (decimal)sent);
            foreach (var transfer in transfers)
            {
                if (transfer.Time is not { } time || rateAt(transfer.Amount.Asset, time) is not { } price
                    || (currency is not null && currency != price.Currency.Code()))
                {
                    return null;
                }

                currency = price.Currency.Code();
                total += price.ValueOf(transfer.Amount).Value * share;
            }
        }

        return currency is null
            ? null
            : new EdgeValue(Math.Round(total, 0, MidpointRounding.AwayFromZero), currency, []).Text;
    }

    /// <summary>Names a marked actor for the report.</summary>
    /// <param name="key">The actor.</param>
    /// <returns>The name.</returns>
    private string Name(string key) => ReportSelection.SafeName(_marked[key]);
}
