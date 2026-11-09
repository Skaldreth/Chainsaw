namespace Chainsaw.App.Graph;

/// <summary>
/// Places the nodes that come into the free surface, as after an expansion, as cleanly as can be found (the user's
/// wish 2026-11-09: <em>the networks are complex enough as they are</em>): next to the nodes they have lines to, the
/// ones that sent money behind them (to the left) and the ones that received it in front (to the right), as the rule
/// <em>höger är ut, vänster är in</em> draws the lines; ordered by the height of what they are tied to, so that their
/// lines do not cross each other; and moved up, down or a column further out where their lines cross the fewest other
/// lines and pass through the fewest nodes. The nodes already standing never move.
/// </summary>
/// <remarks>
/// It is greedy and deterministic: the same graph always gets the same places. Nodes tied only to other new nodes are
/// placed in waves outward from those already standing; nodes tied to nothing standing get the fallback place. The
/// placing is arrangement, not a fact about the owners (principle 2).
/// </remarks>
public static class ExpansionPlacement
{
    /// <summary>The distance between two rows of new nodes: as on the sorted surface, room for two amounts.</summary>
    public const double RowSpacing = TidyLayout.RowSpacing;

    /// <summary>The gap beside a node for the amounts on its lines, as between two hop columns.</summary>
    public const double ColumnGap = HopLayout.ColumnGap;

    /// <summary>The space kept free around a standing node, so that a new node never touches one.</summary>
    public const double Clearance = 16;

    /// <summary>How many half rows a group may be moved up or down from where its lines pull it.</summary>
    public const int MostHalfRows = 12;

    /// <summary>How many columns further out a group may be put than next to what it is tied to.</summary>
    public const int MostExtraColumns = 2;

    /// <summary>What a crossing costs, against a half row of moving.</summary>
    private const double CrossingCost = 6;

    /// <summary>What a line through a node costs, against a half row of moving: worse than a crossing.</summary>
    private const double ThroughNodeCost = 10;

    /// <summary>What a column further out costs, against a half row of moving.</summary>
    private const double ColumnCost = 3;

    /// <summary>What a new node on a standing one costs: so much that it is chosen only when nothing is free.</summary>
    private const double OverlapCost = 10_000;

    /// <summary>Gives the places of the new nodes.</summary>
    /// <param name="newNodes">The nodes without a place, in the graph's order (by time).</param>
    /// <param name="standing">The places of the nodes that already stand; they keep them.</param>
    /// <param name="links">The lines, from the sender to the receiver; a line to a node not given is ignored.</param>
    /// <param name="widthOf">How wide each node is drawn.</param>
    /// <param name="fallback">
    /// Where a node tied to nothing standing goes, given the places taken so far; the caller's own placing.
    /// </param>
    /// <returns>The new nodes' top-left corners.</returns>
    public static IReadOnlyDictionary<string, GraphPoint> Place(
        IReadOnlyList<string> newNodes,
        IReadOnlyDictionary<string, GraphPoint> standing,
        IEnumerable<(string From, string To)> links,
        Func<string, double> widthOf,
        Func<string, IReadOnlyDictionary<string, GraphPoint>, GraphPoint> fallback)
    {
        ArgumentNullException.ThrowIfNull(newNodes);
        ArgumentNullException.ThrowIfNull(standing);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(widthOf);
        ArgumentNullException.ThrowIfNull(fallback);
        var order = newNodes
            .Select((key, index) => (key, index))
            .ToDictionary(pair => pair.key, pair => pair.index, StringComparer.Ordinal);
        var places = new Dictionary<string, GraphPoint>(standing, StringComparer.Ordinal);
        var lines = links.Where(link => link.From != link.To
                && (places.ContainsKey(link.From) || order.ContainsKey(link.From))
                && (places.ContainsKey(link.To) || order.ContainsKey(link.To)))
            .Distinct()
            .ToList();
        var ties = new Ties(lines);
        var waiting = new HashSet<string>(newNodes.Where(key => !places.ContainsKey(key)), StringComparer.Ordinal);
        var placed = new Dictionary<string, GraphPoint>(StringComparer.Ordinal);
        while (waiting.Count > 0)
        {
            var wave = Pulls(waiting, places, ties, widthOf);
            if (wave.Count == 0)
            {
                // Nothing left is tied to anything standing: the first in time gets the caller's place, and the
                // waves go on from it.
                var first = waiting.MinBy(key => order[key])!;
                Settle(first, fallback(first, places), places, placed, waiting);
                continue;
            }

            foreach (var group in wave.GroupBy(pull => (pull.X, pull.Outward)).OrderBy(group => group.Key.X))
            {
                var members = group
                    .OrderBy(pull => pull.Y)
                    .ThenBy(pull => order[pull.Key])
                    .ToList();
                foreach (var (key, place) in Best(members, group.Key.Outward, places, lines, ties, widthOf))
                {
                    Settle(key, place, places, placed, waiting);
                }
            }
        }

        return placed;
    }

    /// <summary>Puts a node at its place: it stands from now on, and the next nodes are placed around it.</summary>
    /// <param name="key">The node.</param>
    /// <param name="place">Its place.</param>
    /// <param name="places">Every place taken.</param>
    /// <param name="placed">The new nodes' places.</param>
    /// <param name="waiting">The nodes not yet placed.</param>
    private static void Settle(
        string key,
        GraphPoint place,
        Dictionary<string, GraphPoint> places,
        Dictionary<string, GraphPoint> placed,
        HashSet<string> waiting)
    {
        places[key] = place;
        placed[key] = place;
        waiting.Remove(key);
    }

    /// <summary>
    /// Gives, for each waiting node tied to a node standing, where its lines pull it: right of every sender, left of
    /// every receiver, between them when it has both and there is room, and at the mean height of them all.
    /// </summary>
    /// <param name="waiting">The nodes not yet placed.</param>
    /// <param name="places">Every place taken.</param>
    /// <param name="ties">The lines by node.</param>
    /// <param name="widthOf">How wide each node is drawn.</param>
    /// <returns>The pulls; empty when no waiting node is tied to anything standing.</returns>
    private static List<Pull> Pulls(
        HashSet<string> waiting,
        Dictionary<string, GraphPoint> places,
        Ties ties,
        Func<string, double> widthOf)
    {
        var pulls = new List<Pull>();
        foreach (var key in waiting)
        {
            var senders = ties.Senders(key).Where(places.ContainsKey).ToList();
            var receivers = ties.Receivers(key).Where(places.ContainsKey).ToList();
            if (senders.Count == 0 && receivers.Count == 0)
            {
                continue;
            }

            var width = widthOf(key);
            var right = senders.Count == 0
                ? (double?)null
                : senders.Max(sender => places[sender].X + widthOf(sender)) + ColumnGap;
            var left = receivers.Count == 0
                ? (double?)null
                : receivers.Min(receiver => places[receiver].X) - ColumnGap - width;
            var (x, outward) = (right, left) switch
            {
                ({ } after, { } before) when after <= before => ((after + before) / 2, 0),
                ({ } after, _) => (after, 1),
                (_, { } before) => (before, -1),
                _ => (0d, 0),
            };
            var y = senders.Concat(receivers).Average(other => places[other].Y);
            pulls.Add(new Pull(key, x, y, outward));
        }

        return pulls;
    }

    /// <summary>
    /// Gives the places of a group of nodes pulled to the same column: stacked in their order around the mean of
    /// their pulls, spread over more columns outward when they are many, and moved to where their lines cost least.
    /// </summary>
    /// <param name="members">The group, in the order they stand from the top.</param>
    /// <param name="outward">
    /// Which way further columns go: 1 to the right of the senders, −1 to the left of the receivers, 0 for neither,
    /// which never moves outward.
    /// </param>
    /// <param name="places">Every place taken.</param>
    /// <param name="lines">The lines.</param>
    /// <param name="byNode">The lines by node.</param>
    /// <param name="widthOf">How wide each node is drawn.</param>
    /// <returns>Each member's place.</returns>
    private static List<(string Key, GraphPoint Place)> Best(
        List<Pull> members,
        int outward,
        Dictionary<string, GraphPoint> places,
        List<(string From, string To)> lines,
        Ties byNode,
        Func<string, double> widthOf)
    {
        var rows = HopLayout.RowsFor(members.Count);
        var widest = members.Max(member => widthOf(member.Key));
        var step = widest + ColumnGap;
        var tall = (Math.Min(rows, members.Count) - 1) * RowSpacing;
        var top = members.Average(member => member.Y) - (tall / 2);
        var x = members[0].X;
        var keys = members.Select(member => member.Key).ToHashSet(StringComparer.Ordinal);
        var ties = members
            .SelectMany(member => byNode.Senders(member.Key)
                .Where(places.ContainsKey)
                .Select(other => (From: other, To: member.Key))
                .Concat(byNode.Receivers(member.Key)
                    .Where(places.ContainsKey)
                    .Select(other => (From: member.Key, To: other))))
            .ToList();
        var extra = outward == 0 ? 0 : MostExtraColumns;

        // Only what lies where the group's lines can run is weighed, so that a large graph costs no more (principle 6).
        var reach = Math.Max(1, (members.Count + rows - 1) / rows) + extra;
        var region = new GraphRect(
                Math.Min(x, x - (reach * step)), top - (MostHalfRows * RowSpacing / 2), (2 * reach * step) + widest,
                tall + (MostHalfRows * RowSpacing) + GraphSizes.NodeHeight)
            .Union(GraphRect.Around(ties.Select(tie => places[keys.Contains(tie.From) ? tie.To : tie.From])))
            .Inflate(GraphSizes.LaneSpacing);
        var standing = places
            .Select(pair => new Standing(pair.Key, Frame(pair.Value, widthOf(pair.Key))))
            .Where(node => node.Rect.Intersects(region))
            .ToList();
        var segments = lines.Where(line => places.ContainsKey(line.From) && places.ContainsKey(line.To))
            .Select(line => Segment(places[line.From], widthOf(line.From), places[line.To]))
            .Where(segment => GraphRect.Around([segment.From, segment.To]).Intersects(region))
            .ToList();

        List<(string Key, GraphPoint Place)>? best = null;
        var lowest = double.PositiveInfinity;
        for (var column = 0; column <= extra; column++)
        {
            for (var half = 0; half <= 2 * MostHalfRows; half++)
            {
                // 0, +1, −1, +2, −2 …: the nearest first, so that a tie keeps the group where its lines pull it.
                var shift = (half + 1) / 2 * (half % 2 == 0 ? -1 : 1);
                var left = x + (outward * column * step);
                var candidate = Stack(members, left, top + (shift * RowSpacing / 2), rows, step, outward);
                var cost = (Math.Abs(shift) + (column * ColumnCost))
                    + Cost(candidate, ties, places, standing, segments, widthOf);
                if (cost < lowest)
                {
                    (best, lowest) = (candidate, cost);
                }
            }
        }

        return best!;
    }

    /// <summary>
    /// Stacks a group: down the first column in their order, then the next column outward, so that the order still
    /// reads from the top.
    /// </summary>
    /// <param name="members">The group in order.</param>
    /// <param name="x">The first column's left edge.</param>
    /// <param name="top">The first row's top.</param>
    /// <param name="rows">How many stand one under the other.</param>
    /// <param name="step">The distance between two columns.</param>
    /// <param name="outward">Which way the next columns go; 0 is taken as to the right.</param>
    /// <returns>Each member's place.</returns>
    private static List<(string Key, GraphPoint Place)> Stack(
        List<Pull> members, double x, double top, int rows, double step, int outward)
    {
        var direction = outward == 0 ? 1 : outward;
        return
        [
            .. members.Select((member, index) =>
            {
                var (sub, row) = Math.DivRem(index, rows);
                return (member.Key, new GraphPoint(x + (direction * sub * step), top + (row * RowSpacing)));
            }),
        ];
    }

    /// <summary>
    /// Gives what a placing of a group costs: its new lines' crossings with the lines standing, the nodes they pass
    /// through, and every new node on a standing one.
    /// </summary>
    /// <param name="candidate">The group's places.</param>
    /// <param name="ties">The lines between the group and the nodes standing.</param>
    /// <param name="places">Every place taken.</param>
    /// <param name="standing">The standing nodes' frames.</param>
    /// <param name="segments">The standing lines, as straight segments.</param>
    /// <param name="widthOf">How wide each node is drawn.</param>
    /// <returns>The cost.</returns>
    private static double Cost(
        List<(string Key, GraphPoint Place)> candidate,
        List<(string From, string To)> ties,
        Dictionary<string, GraphPoint> places,
        List<Standing> standing,
        List<(GraphPoint From, GraphPoint To)> segments,
        Func<string, double> widthOf)
    {
        var at = candidate.ToDictionary(pair => pair.Key, pair => pair.Place, StringComparer.Ordinal);
        var cost = 0d;
        foreach (var (key, place) in candidate)
        {
            var frame = Frame(place, widthOf(key)).Inflate(Clearance);
            cost += standing.Count(other => other.Rect.Intersects(frame)) * OverlapCost;
        }

        foreach (var (from, to) in ties)
        {
            var line = Segment(at.TryGetValue(from, out var a) ? a : places[from], widthOf(from),
                at.TryGetValue(to, out var b) ? b : places[to]);
            cost += segments.Count(other => Crosses(line, other)) * CrossingCost;
            cost += standing.Count(other => other.Key != from && other.Key != to && Through(line, other.Rect))
                * ThroughNodeCost;
        }

        return cost;
    }

    /// <summary>Gives a line as a straight segment, out of the sender's right side into the receiver's left.</summary>
    /// <param name="from">The sender's place.</param>
    /// <param name="fromWidth">The sender's width.</param>
    /// <param name="to">The receiver's place.</param>
    /// <returns>The segment.</returns>
    private static (GraphPoint From, GraphPoint To) Segment(GraphPoint from, double fromWidth, GraphPoint to) =>
        (new GraphPoint(from.X + fromWidth, from.Y + (GraphSizes.NodeHeight / 2)),
            new GraphPoint(to.X, to.Y + (GraphSizes.NodeHeight / 2)));

    /// <summary>Gives a node's frame at a place.</summary>
    /// <param name="place">Its top-left corner.</param>
    /// <param name="width">Its width.</param>
    /// <returns>The frame.</returns>
    private static GraphRect Frame(GraphPoint place, double width) => GraphSizes.NodeFrame(place, width);

    /// <summary>
    /// Says whether two segments cross; touching at an end, as two lines from one port do, is no crossing.
    /// </summary>
    /// <param name="a">One segment.</param>
    /// <param name="b">The other.</param>
    /// <returns><see langword="true"/> when they cross.</returns>
    public static bool Crosses((GraphPoint From, GraphPoint To) a, (GraphPoint From, GraphPoint To) b)
    {
        var d1 = Side(b.From, b.To, a.From);
        var d2 = Side(b.From, b.To, a.To);
        var d3 = Side(a.From, a.To, b.From);
        var d4 = Side(a.From, a.To, b.To);
        return d1 * d2 < 0 && d3 * d4 < 0;
    }

    /// <summary>Says whether a segment passes through a node's frame.</summary>
    /// <param name="line">The segment.</param>
    /// <param name="frame">The frame.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public static bool Through((GraphPoint From, GraphPoint To) line, GraphRect frame)
    {
        var inner = frame.Inflate(-2);
        if (Inside(line.From, inner) || Inside(line.To, inner))
        {
            return true;
        }

        var corners = new[]
        {
            new GraphPoint(inner.X, inner.Y), new GraphPoint(inner.Right, inner.Y),
            new GraphPoint(inner.Right, inner.Bottom), new GraphPoint(inner.X, inner.Bottom),
        };
        return Enumerable.Range(0, 4).Any(index => Crosses(line, (corners[index], corners[(index + 1) % 4])));
    }

    /// <summary>Says whether a point lies inside a frame.</summary>
    /// <param name="point">The point.</param>
    /// <param name="frame">The frame.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool Inside(GraphPoint point, GraphRect frame) =>
        point.X > frame.X && point.X < frame.Right && point.Y > frame.Y && point.Y < frame.Bottom;

    /// <summary>Gives which side of a line a point is on: the sign of the cross product, 0 on it.</summary>
    /// <param name="from">The line's start.</param>
    /// <param name="to">The line's end.</param>
    /// <param name="point">The point.</param>
    /// <returns>Negative, zero or positive.</returns>
    private static double Side(GraphPoint from, GraphPoint to, GraphPoint point)
    {
        var value = ((to.X - from.X) * (point.Y - from.Y)) - ((to.Y - from.Y) * (point.X - from.X));
        return Math.Abs(value) < 1e-9 ? 0 : value;
    }

    /// <summary>The lines by node: who sent to each node and who received from it.</summary>
    private sealed class Ties
    {
        /// <summary>The senders by receiver.</summary>
        private readonly ILookup<string, string> _senders;

        /// <summary>The receivers by sender.</summary>
        private readonly ILookup<string, string> _receivers;

        /// <summary>Collects the lines by node.</summary>
        /// <param name="lines">The lines.</param>
        public Ties(List<(string From, string To)> lines)
        {
            _senders = lines.ToLookup(line => line.To, line => line.From, StringComparer.Ordinal);
            _receivers = lines.ToLookup(line => line.From, line => line.To, StringComparer.Ordinal);
        }

        /// <summary>Gives the nodes that sent money to a node.</summary>
        /// <param name="key">The node.</param>
        /// <returns>The senders, each once.</returns>
        public IEnumerable<string> Senders(string key) => _senders[key].Distinct(StringComparer.Ordinal);

        /// <summary>Gives the nodes a node sent money to.</summary>
        /// <param name="key">The node.</param>
        /// <returns>The receivers, each once.</returns>
        public IEnumerable<string> Receivers(string key) => _receivers[key].Distinct(StringComparer.Ordinal);
    }

    /// <summary>Where a waiting node's lines pull it.</summary>
    /// <param name="Key">The node.</param>
    /// <param name="X">Its left edge.</param>
    /// <param name="Y">Its top.</param>
    /// <param name="Outward">Which way further columns go: 1 right, −1 left, 0 neither.</param>
    private sealed record Pull(string Key, double X, double Y, int Outward);

    /// <summary>A standing node and its frame.</summary>
    /// <param name="Key">The node.</param>
    /// <param name="Rect">Its frame.</param>
    private sealed record Standing(string Key, GraphRect Rect);
}
