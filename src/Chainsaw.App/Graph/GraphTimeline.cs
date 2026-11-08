using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>A transaction in the strip under the graph (sketch TL1a): one stroke, with what moved in it.</summary>
/// <param name="Time">The block's time: a fact from the chain.</param>
/// <param name="Text">What moved, for example <c>bc1q…x0wl → Börs A · 0,60 BTC</c>.</param>
public sealed record TimelineTick(DateTimeOffset Time, string Text);

/// <summary>
/// The graph's times, read from a scene without WPF (sketch TL1a): when the money came to each actor, when each line
/// first carried money, and one stroke per confirmed transaction for the strip. Every time is a block's time, a fact
/// from the chain; an unconfirmed transaction has none and is left out of the strip.
/// </summary>
public sealed class GraphTimeline
{
    /// <summary>How many more lines of a transaction the stroke's text names after the first, as <c>+2</c>.</summary>
    private const string MoreLinesFormat = " · +{0}";

    /// <summary>When the money came to each actor, by key; an actor without a confirmed movement is missing.</summary>
    private readonly Dictionary<string, DateTimeOffset> _nodes;

    /// <summary>When each line first carried money, by key; a line with no confirmed transfer is missing.</summary>
    private readonly Dictionary<string, DateTimeOffset> _edges;

    /// <summary>Creates the times.</summary>
    /// <param name="nodes">When the money came to each actor.</param>
    /// <param name="edges">When each line first carried money.</param>
    /// <param name="ticks">The strip's strokes, in time order.</param>
    private GraphTimeline(
        Dictionary<string, DateTimeOffset> nodes, Dictionary<string, DateTimeOffset> edges, IReadOnlyList<TimelineTick> ticks)
    {
        _nodes = nodes;
        _edges = edges;
        Ticks = ticks;
    }

    /// <summary>Gets the times of no graph.</summary>
    public static GraphTimeline Empty { get; } = new([], [], []);

    /// <summary>Gets one stroke per confirmed transaction, in time order.</summary>
    public IReadOnlyList<TimelineTick> Ticks { get; }

    /// <summary>Gets when the money came to each actor that has a time, by key.</summary>
    public IReadOnlyDictionary<string, DateTimeOffset> NodeTimes => _nodes;

    /// <summary>Reads the times of a scene.</summary>
    /// <param name="scene">The scene, or <see langword="null"/> for none.</param>
    /// <returns>The times.</returns>
    public static GraphTimeline Of(GraphScene? scene)
    {
        if (scene is null)
        {
            return Empty;
        }

        var edges = scene.Edges
            .Where(edge => edge.Edge.FirstTime is not null)
            .ToDictionary(edge => edge.Key, edge => edge.Edge.FirstTime!.Value, StringComparer.Ordinal);
        var nodes = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var node in scene.LayoutNodes)
        {
            if (ArrivalOf(node, scene.Edges) is { } time)
            {
                nodes[node.Key] = time;
            }
        }

        return new GraphTimeline(nodes, edges, TicksOf(scene));
    }

    /// <summary>
    /// Says whether an actor came later than a time: the money reached it after, or it has no confirmed movement at
    /// all and so belongs to now.
    /// </summary>
    /// <param name="nodeKey">The actor.</param>
    /// <param name="at">The time.</param>
    /// <returns><see langword="true"/> when it came later.</returns>
    public bool IsLaterNode(string nodeKey, DateTimeOffset at) => !_nodes.TryGetValue(nodeKey, out var time) || time > at;

    /// <summary>Says whether a line first carried money later than a time, or has no confirmed transfer at all.</summary>
    /// <param name="edgeKey">The line.</param>
    /// <param name="at">The time.</param>
    /// <returns><see langword="true"/> when it came later.</returns>
    public bool IsLaterEdge(string edgeKey, DateTimeOffset at) => !_edges.TryGetValue(edgeKey, out var time) || time > at;

    /// <summary>
    /// Gives when the money came to an actor: the first money in, as the graph knows it; an actor nothing came to in
    /// the graph, such as a start point that only sends, stands at its first money out.
    /// </summary>
    /// <param name="node">The actor.</param>
    /// <param name="edges">The graph's lines.</param>
    /// <returns>The time, or <see langword="null"/> when it has no confirmed movement in the graph.</returns>
    private static DateTimeOffset? ArrivalOf(GraphNode node, IReadOnlyList<EdgeView> edges) =>
        node.FirstReceived
        ?? First(edges.Where(edge => edge.Edge.To == node.Key))
        ?? First(edges.Where(edge => edge.Edge.From == node.Key));

    /// <summary>Gives the earliest time among lines, or <see langword="null"/> when none has one.</summary>
    /// <param name="edges">The lines.</param>
    /// <returns>The time.</returns>
    private static DateTimeOffset? First(IEnumerable<EdgeView> edges) =>
        edges.Select(edge => edge.Edge.FirstTime).Where(time => time is not null).Min();

    /// <summary>Gives one stroke per confirmed transaction, with the first line it moved money on.</summary>
    /// <param name="scene">The scene.</param>
    /// <returns>The strokes, in time order.</returns>
    private static IReadOnlyList<TimelineTick> TicksOf(GraphScene scene)
    {
        var moves = scene.Edges
            .SelectMany(edge => edge.Edge.Transfers
                .Where(transfer => transfer.Time is not null)
                .Select(transfer => (Edge: edge, Transfer: transfer)))
            .GroupBy(move => move.Transfer.Transaction);
        return [.. moves
            .Select(transaction => Tick(scene, [.. transaction]))
            .OrderBy(tick => tick.Time)
            .ThenBy(tick => tick.Text, StringComparer.Ordinal)];
    }

    /// <summary>Gives a transaction's stroke: its time, the first line's two actors and what it moved on it.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="moves">What the transaction moved, per line.</param>
    /// <returns>The stroke.</returns>
    private static TimelineTick Tick(GraphScene scene, IReadOnlyList<(EdgeView Edge, AssetTransfer Transfer)> moves)
    {
        var lines = moves.GroupBy(move => move.Edge.Key, StringComparer.Ordinal).ToList();
        var first = lines[0].First().Edge;
        var amount = lines[0].Select(move => move.Transfer.Amount).Aggregate((sum, next) => sum + next);
        var text = $"{NameOf(scene, first.Edge.From)} → {NameOf(scene, first.Edge.To)} · {AmountText.WithUnit(amount)}";
        if (lines.Count > 1)
        {
            text += string.Format(System.Globalization.CultureInfo.InvariantCulture, MoreLinesFormat, lines.Count - 1);
        }

        return new TimelineTick(moves[0].Transfer.Time!.Value, text);
    }

    /// <summary>Gives an actor's name as the graph writes it, or its key when the scene does not draw it.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="key">The actor's key.</param>
    /// <returns>The name.</returns>
    private static string NameOf(GraphScene scene, string key) => scene.Node(key)?.Name ?? key;
}
