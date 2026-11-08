using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>A chain's part of the surface: the room its actors take, under a heading with the chain's name.</summary>
/// <param name="Chain">The chain.</param>
/// <param name="Bounds">The room its actors' frames take.</param>
public sealed record ChainSection(Chain Chain, GraphRect Bounds)
{
    /// <summary>Gets where the heading's text starts: at the part's left edge, above its highest actor.</summary>
    public GraphPoint HeadingAt => new(Bounds.X, Bounds.Y - ChainSections.HeadingLift);
}

/// <summary>
/// The chains' parts of the surface (sketch TK1a): when a graph holds actors on more than one chain, each chain's
/// actors stand under a quiet heading with its name in capitals (<c>TRON</c>, <c>ETHEREUM</c>), so that it is seen
/// where the money continues on another chain. A graph on one chain has no headings, since one would only be noise
/// (principle 7).
/// </summary>
public static class ChainSections
{
    /// <summary>How far above a part's highest actor its heading stands, as in sketch TK1a.</summary>
    public const double HeadingLift = 34;

    /// <summary>Gives the chains' parts, the highest first; empty when the graph's actors are on one chain.</summary>
    /// <param name="scene">The graph.</param>
    /// <param name="places">Each node's top-left corner.</param>
    /// <returns>The parts.</returns>
    public static IReadOnlyList<ChainSection> Of(GraphScene scene, IReadOnlyDictionary<string, GraphPoint> places)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(places);
        var parts = scene.Nodes
            .Where(node => places.ContainsKey(node.Key))
            .GroupBy(node => ChainOf(node.Node))
            .Select(group => new ChainSection(
                group.Key,
                group.Select(node => scene.FrameOf(node.Key, places[node.Key]))
                    .Aggregate((all, one) => all.Union(one))))
            .ToList();
        return parts.Count < 2 ? [] : [.. parts.OrderBy(part => part.Bounds.Y).ThenBy(part => part.Bounds.X)];
    }

    /// <summary>Gives the chain an actor is on: the other chain's for a transaction there, else its own.</summary>
    /// <param name="node">The actor.</param>
    /// <returns>The chain.</returns>
    public static Chain ChainOf(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.OtherChain?.Chain ?? node.Chain;
    }
}
