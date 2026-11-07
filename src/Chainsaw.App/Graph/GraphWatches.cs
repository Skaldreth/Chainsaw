using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>What a mark of a watch on a line is (sketch BV2a).</summary>
public enum WatchLineKind
{
    /// <summary>
    /// A thin dotted watch line to a chosen counterparty no money has gone to yet, with an eye on it.
    /// </summary>
    Guard,

    /// <summary>A soft glow on a line that already goes in the watched direction.</summary>
    Glow,

    /// <summary>A hit: a real line with the amount, its rings moving until the hit is seen.</summary>
    Hit,
}

/// <summary>The eye in a watched node's corner.</summary>
/// <param name="NodeKey">The node.</param>
/// <param name="Tip">What it watches, for the tooltip.</param>
/// <param name="Idle">Whether lookups cannot be made now (grey and hollow).</param>
/// <param name="HasNew">Whether the watch has hits the user has not seen.</param>
public sealed record WatchMark(string NodeKey, string Tip, bool Idle, bool HasNew);

/// <summary>A watch's mark between two nodes, in the money's direction.</summary>
/// <param name="From">The node the money leaves.</param>
/// <param name="To">The node it reaches.</param>
/// <param name="Kind">A watch line, a glow or a hit.</param>
/// <param name="Idle">Whether lookups cannot be made now (grey).</param>
/// <param name="Tip">What the watch looks for.</param>
/// <param name="Label">A hit's amount, or <see langword="null"/>.</param>
/// <param name="IsNew">Whether a hit is not seen yet, so that its rings move.</param>
/// <param name="WatchId">The watch.</param>
public sealed record WatchLine(
    string From, string To, WatchLineKind Kind, bool Idle, string Tip, string? Label, bool IsNew, string WatchId);

/// <summary>Short dotted rays at a watched node's port, for a watch without counterparties.</summary>
/// <param name="NodeKey">The node.</param>
/// <param name="IsOut">Whether they stand at the out port (right); otherwise the in port (left).</param>
/// <param name="Idle">Whether lookups cannot be made now (grey).</param>
/// <param name="Tip">What the watch looks for.</param>
public sealed record WatchRay(string NodeKey, bool IsOut, bool Idle, string Tip);

/// <summary>Everything the graph draws for the watches (sketch BV2a).</summary>
/// <param name="Marks">The eyes in the nodes' corners.</param>
/// <param name="Lines">The watch lines, glows and hits.</param>
/// <param name="Rays">The rays of watches without counterparties.</param>
public sealed record GraphWatchOverlay(
    IReadOnlyList<WatchMark> Marks, IReadOnlyList<WatchLine> Lines, IReadOnlyList<WatchRay> Rays)
{
    /// <summary>Gets an overlay with nothing.</summary>
    public static GraphWatchOverlay Empty { get; } = new([], [], []);

    /// <summary>Gets whether any hit is unseen, so that the rings move.</summary>
    public bool Moves => Lines.Any(line => line.IsNew) || Marks.Any(mark => mark.HasNew);

    /// <summary>Gives the eye of a node, or <see langword="null"/>.</summary>
    /// <param name="nodeKey">The node.</param>
    /// <returns>The eye.</returns>
    public WatchMark? MarkOf(string nodeKey) => Marks.FirstOrDefault(mark => mark.NodeKey == nodeKey);
}

/// <summary>
/// Works out what the graph draws for the watches (sketch BV2a): an eye on each watched node, a dotted watch line to
/// each chosen counterparty in the graph (out from the node for receivers, in to it for senders), a glow on a line
/// that already goes that way, rays at the port when every counterparty counts, and a hit's line with its amount. A
/// paused watch keeps its eye, grey; everything is grey when lookups cannot be made. No WPF.
/// </summary>
public static class GraphWatches
{
    /// <summary>Builds the overlay.</summary>
    /// <param name="scene">The graph.</param>
    /// <param name="snapshot">The case's watches.</param>
    /// <param name="gate">Whether a chain's watches may be checked now.</param>
    /// <returns>What to draw.</returns>
    public static GraphWatchOverlay Build(GraphScene? scene, WatchSnapshot snapshot, Func<Chain, WatchGate> gate)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(gate);
        if (scene is null || snapshot.Watches.Count == 0)
        {
            return GraphWatchOverlay.Empty;
        }

        var nodeOf = NodesByAddress(scene);
        var marks = new List<WatchMark>();
        var lines = new List<WatchLine>();
        var rays = new List<WatchRay>();
        foreach (var watch in snapshot.Watches)
        {
            if (!nodeOf.TryGetValue(watch.Subject.Anchor, out var node))
            {
                continue;
            }

            var idle = watch.State == WatchState.Paused || gate(watch.Chain) != WatchGate.Open;
            var tip = TipOf(watch, scene, nodeOf);
            var hits = snapshot.HitsOf(watch.Id);
            marks.Add(new WatchMark(node, tip, idle, hits.Any(snapshot.IsNew)));
            if (watch.State == WatchState.Paused)
            {
                continue;
            }

            var hitLines = HitLines(watch, node, hits, snapshot, nodeOf, tip, idle);
            lines.AddRange(hitLines);
            lines.AddRange(GuardLines(watch, node, scene, nodeOf, tip, idle, hitLines));
            if (watch.Filter.HasNoCounterparties)
            {
                if (watch.Filter.WatchesOut)
                {
                    rays.Add(new WatchRay(node, true, idle, tip));
                }

                if (watch.Filter.WatchesIn)
                {
                    rays.Add(new WatchRay(node, false, idle, tip));
                }
            }
        }

        return new GraphWatchOverlay(marks, lines, rays);
    }

    /// <summary>Gives each address in the graph the node it belongs to.</summary>
    /// <param name="scene">The graph.</param>
    /// <returns>The nodes by address.</returns>
    private static Dictionary<ChainAddress, string> NodesByAddress(GraphScene scene)
    {
        var nodes = new Dictionary<ChainAddress, string>();
        foreach (var node in scene.Nodes)
        {
            foreach (var address in node.Node.Addresses)
            {
                nodes.TryAdd(address, node.Key);
            }
        }

        return nodes;
    }

    /// <summary>Gives a hit line per counterparty in the graph, with the newest hit's amount.</summary>
    /// <param name="watch">The watch.</param>
    /// <param name="node">Its node.</param>
    /// <param name="hits">Its hits, newest first.</param>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="nodeOf">The nodes by address.</param>
    /// <param name="tip">The watch's tooltip.</param>
    /// <param name="idle">Whether lookups cannot be made now.</param>
    /// <returns>The lines.</returns>
    private static List<WatchLine> HitLines(
        Watch watch, string node, IReadOnlyList<WatchHit> hits, WatchSnapshot snapshot,
        Dictionary<ChainAddress, string> nodeOf, string tip, bool idle)
    {
        var lines = new List<WatchLine>();
        foreach (var hit in hits)
        {
            var isOut = hit.Match.Direction == WatchDirection.Out;
            foreach (var party in hit.Match.Counterparties)
            {
                if (!nodeOf.TryGetValue(party, out var other) || other == node)
                {
                    continue;
                }

                var (from, to) = isOut ? (node, other) : (other, node);
                if (lines.Any(line => line.From == from && line.To == to))
                {
                    continue;
                }

                var isNew = hits.Any(each => snapshot.IsNew(each) && each.Match.Direction == hit.Match.Direction
                    && each.Match.Counterparties.Contains(party));
                lines.Add(new WatchLine(
                    from, to, WatchLineKind.Hit, idle, tip, AmountText.WithUnit(hit.Match.Amount), isNew, watch.Id));
            }
        }

        return lines;
    }

    /// <summary>Gives a watch line or a glow per chosen counterparty in the graph without a hit line.</summary>
    /// <param name="watch">The watch.</param>
    /// <param name="node">Its node.</param>
    /// <param name="scene">The graph.</param>
    /// <param name="nodeOf">The nodes by address.</param>
    /// <param name="tip">The watch's tooltip.</param>
    /// <param name="idle">Whether lookups cannot be made now.</param>
    /// <param name="hitLines">The hit lines already given.</param>
    /// <returns>The lines.</returns>
    private static IEnumerable<WatchLine> GuardLines(
        Watch watch, string node, GraphScene scene, Dictionary<ChainAddress, string> nodeOf, string tip, bool idle,
        IReadOnlyList<WatchLine> hitLines)
    {
        var pairs = watch.Filter.Receivers.Select(party => (Party: party, IsOut: true))
            .Concat(watch.Filter.Senders.Select(party => (Party: party, IsOut: false)));
        foreach (var (party, isOut) in pairs)
        {
            if (!nodeOf.TryGetValue(party, out var other) || other == node)
            {
                continue;
            }

            var (from, to) = isOut ? (node, other) : (other, node);
            if (hitLines.Any(line => line.From == from && line.To == to))
            {
                continue;
            }

            var exists = scene.Edges.Any(edge => edge.Edge.From == from && edge.Edge.To == to);
            yield return new WatchLine(
                from, to, exists ? WatchLineKind.Glow : WatchLineKind.Guard, idle, tip, null, false, watch.Id);
        }
    }

    /// <summary>Writes what a watch looks for, as the eye's tooltip (sketch BV2a).</summary>
    /// <param name="watch">The watch.</param>
    /// <param name="scene">The graph, whose names name the counterparties.</param>
    /// <param name="nodeOf">The nodes by address.</param>
    /// <returns>For example <c>Bevakad · ut · till Börs A eller bc1q…9fd2</c>.</returns>
    private static string TipOf(Watch watch, GraphScene scene, Dictionary<ChainAddress, string> nodeOf)
    {
        string Name(ChainAddress address) =>
            nodeOf.TryGetValue(address, out var key) && scene.Node(key) is { } node
                ? node.Name
                : WatchWords.Short(address);

        var parts = new List<string> { watch.State == WatchState.Paused ? "Bevakad, pausad" : "Bevakad" };
        parts.Add(watch.Filter.Direction switch
        {
            WatchDirection.In => "in",
            WatchDirection.Out => "ut",
            _ => "in och ut",
        });
        if (watch.Filter.Receivers.Count > 0)
        {
            parts.Add("till " + string.Join(" eller ", watch.Filter.Receivers.Select(Name)));
        }

        if (watch.Filter.Senders.Count > 0)
        {
            parts.Add("från " + string.Join(" eller ", watch.Filter.Senders.Select(Name)));
        }

        if (watch.Filter.HasNoCounterparties)
        {
            parts.Add("alla motparter");
        }

        return string.Join(" · ", parts);
    }
}
