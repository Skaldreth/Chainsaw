using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>
/// Where every node stands in a layout (sketch GR4): on the free surface where the user put it, else at its place in
/// the hop columns; in the hop columns always in its hop's column, at the height the user dragged it to. Each layout
/// remembers its own places.
/// </summary>
public static class GraphPlacement
{
    /// <summary>The highest a node may be dragged in the hop columns, under the columns' names.</summary>
    public const double HighestInColumns = 28;

    /// <summary>Gives every node's place in a layout.</summary>
    /// <param name="nodes">The nodes.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="columns">Each node's place in the hop columns, from <see cref="HopLayout"/>.</param>
    /// <param name="moved">Places the user has dragged nodes to and that the case may not hold yet.</param>
    /// <returns>Each node's top-left corner.</returns>
    public static IReadOnlyDictionary<string, GraphPoint> Place(
        IReadOnlyList<GraphNode> nodes,
        GraphLayout layout,
        IReadOnlyDictionary<string, GraphPoint> columns,
        IReadOnlyDictionary<(string Key, GraphLayout Layout), GraphPoint> moved)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(moved);
        var places = new Dictionary<string, GraphPoint>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var column = columns[node.Key];
            var chosen = moved.TryGetValue((node.Key, layout), out var dragged)
                ? dragged
                : Saved(node, layout);
            places[node.Key] = layout == GraphLayout.HopColumns
                ? new GraphPoint(column.X, chosen?.Y ?? column.Y)
                : chosen ?? column;
        }

        return places;
    }

    /// <summary>
    /// Gives where a dragged node goes: anywhere on the free surface, and only up and down in its hop column.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="start">Where the node stood when the drag began.</param>
    /// <param name="wanted">Where the pointer wants it.</param>
    /// <returns>The new place.</returns>
    public static GraphPoint Drag(GraphLayout layout, GraphPoint start, GraphPoint wanted) =>
        layout == GraphLayout.HopColumns
            ? new GraphPoint(start.X, Math.Max(HighestInColumns, wanted.Y))
            : wanted;

    /// <summary>Gives the place the case holds for a node in a layout.</summary>
    /// <param name="node">The node.</param>
    /// <param name="layout">The layout.</param>
    /// <returns>The place, or <see langword="null"/> when the user has not placed it.</returns>
    private static GraphPoint? Saved(GraphNode node, GraphLayout layout) =>
        node.Positions.TryGetValue(layout, out var position) ? new GraphPoint(position.X, position.Y) : null;
}
