namespace Chainsaw.App.Graph;

/// <summary>
/// What a transaction the source no longer knows leaves uncertain in the graph (sketch GR9a): its own lines, drawn
/// grey and dashed, and the lines and actors after it, drawn dashed and faint, since the money they show rests on a
/// transaction that is not in the chain any more (principle 2). The chain's answers stay in the case; only the
/// drawing changes.
/// </summary>
/// <param name="GoneLines">The lines that carry a gone transaction.</param>
/// <param name="Lines">The lines after them, out of the uncertain actors.</param>
/// <param name="Nodes">The actors after them, that only money through a gone transaction reaches.</param>
public sealed record GoneTrail(
    IReadOnlySet<string> GoneLines, IReadOnlySet<string> Lines, IReadOnlySet<string> Nodes)
{
    /// <summary>Gets a trail with nothing in it: no transaction is gone.</summary>
    public static GoneTrail Empty { get; } = new(
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal));

    /// <summary>
    /// Follows the money on from the lines whose transaction is gone, as far as the graph goes, and keeps the actors
    /// that nothing else reaches: an actor that money also reaches by a line from outside the trail, or a start point,
    /// stays as it is, since what it holds does not rest only on the gone transaction. Money that circles back within
    /// the trail does not keep an actor. The lines out of an uncertain actor are uncertain.
    /// </summary>
    /// <param name="scene">The graph, or <see langword="null"/>.</param>
    /// <param name="marks">The block marks by line.</param>
    /// <returns>The trail; <see cref="Empty"/> when no line carries a gone transaction.</returns>
    public static GoneTrail Of(GraphScene? scene, IReadOnlyDictionary<string, BlockMark> marks)
    {
        ArgumentNullException.ThrowIfNull(marks);
        var gone = marks.Where(pair => pair.Value.IsGone).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        if (scene is null || gone.Count == 0)
        {
            return Empty;
        }

        var nodes = Reached(scene, gone);
        var into = scene.Edges.ToLookup(edge => edge.Edge.To, StringComparer.Ordinal);
        bool removed;
        do
        {
            // An actor reached from outside the trail, by a line that still stands, is not uncertain; dropping it may
            // let others be reached from outside in turn.
            var kept = nodes
                .Where(key => scene.Node(key) is { IsStartPoint: false }
                    && into[key].All(edge => gone.Contains(edge.Key) || nodes.Contains(edge.Edge.From)))
                .ToHashSet(StringComparer.Ordinal);
            removed = kept.Count < nodes.Count;
            nodes = kept;
        }
        while (removed);

        var lines = scene.Edges
            .Where(edge => nodes.Contains(edge.Edge.From) && !gone.Contains(edge.Key))
            .Select(edge => edge.Key)
            .ToHashSet(StringComparer.Ordinal);
        return new GoneTrail(gone, lines, nodes);
    }

    /// <summary>Gives the actors the money reaches after the gone lines, following the lines forward.</summary>
    /// <param name="scene">The graph.</param>
    /// <param name="gone">The gone lines.</param>
    /// <returns>The actors reached.</returns>
    private static HashSet<string> Reached(GraphScene scene, IReadOnlySet<string> gone)
    {
        var outOf = scene.Edges.ToLookup(edge => edge.Edge.From, StringComparer.Ordinal);
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var waiting = new Queue<string>(
            scene.Edges.Where(edge => gone.Contains(edge.Key)).Select(edge => edge.Edge.To));
        while (waiting.TryDequeue(out var key))
        {
            if (!reached.Add(key))
            {
                continue;
            }

            foreach (var edge in outOf[key])
            {
                waiting.Enqueue(edge.Edge.To);
            }
        }

        return reached;
    }
}
