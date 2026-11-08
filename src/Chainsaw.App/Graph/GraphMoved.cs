namespace Chainsaw.App.Graph;

/// <summary>
/// What a drag moved on the surface: the nodes that got a new place. The surface draws these again in their own layer
/// while the nodes that stand still keep their drawing; anything else that changes is a full change of the scene
/// (principle 6).
/// </summary>
/// <param name="Nodes">The nodes that moved.</param>
public sealed record GraphMoved(IReadOnlySet<string> Nodes)
{
    /// <summary>Gets whether nothing moved.</summary>
    public bool IsEmpty => Nodes.Count == 0;

    /// <summary>Gives the nodes that moved between two arrangements; a node that came or went counts as moved.</summary>
    /// <param name="before">The nodes' places before.</param>
    /// <param name="after">The nodes' places after.</param>
    /// <returns>What moved.</returns>
    public static GraphMoved Between(
        IReadOnlyDictionary<string, GraphPoint> before, IReadOnlyDictionary<string, GraphPoint> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var moved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, place) in after)
        {
            if (!before.TryGetValue(key, out var old) || old != place)
            {
                moved.Add(key);
            }
        }

        moved.UnionWith(before.Keys.Where(key => !after.ContainsKey(key)));
        return new GraphMoved(moved);
    }

    /// <summary>Gives what moved together with more nodes.</summary>
    /// <param name="nodes">The nodes to add.</param>
    /// <returns>What moved and the nodes.</returns>
    public GraphMoved With(IEnumerable<string> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var all = new HashSet<string>(Nodes, StringComparer.Ordinal);
        all.UnionWith(nodes);
        return new GraphMoved(all);
    }

    /// <summary>
    /// Gives the lines' new curves, with each curve that did not change kept as the same instance as before, so that
    /// what was made for it (its drawn geometry) is used again.
    /// </summary>
    /// <param name="before">The curves before.</param>
    /// <param name="after">The curves just worked out.</param>
    /// <returns>The curves after, sharing the unchanged ones with before.</returns>
    public static IReadOnlyDictionary<string, EdgeCurve> KeepSameCurves(
        IReadOnlyDictionary<string, EdgeCurve> before, IReadOnlyDictionary<string, EdgeCurve> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var kept = new Dictionary<string, EdgeCurve>(after.Count, StringComparer.Ordinal);
        foreach (var (key, curve) in after)
        {
            kept[key] = before.TryGetValue(key, out var old) && old == curve ? old : curve;
        }

        return kept;
    }
}
