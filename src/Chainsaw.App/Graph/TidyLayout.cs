using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>
/// Sorts a messy free surface neatly (<em>Sortera grafen</em>, the user's wish 2026-11-08): the money left to right in
/// the order of the hops, as in sketch GR4, each hop's actors ordered so that lines cross as little as is cheap to find,
/// and every column centred on the same middle so that the graph reads as one flow. It is airy on purpose, never
/// compact: the rows stand further apart than in the hop columns, so that the amounts of a fan of lines fit between
/// them, and a hop with many counterparties is spread over the surface rather than one long column (CLAUDE.md,
/// <em>Grafen använder ytan</em>).
/// </summary>
/// <remarks>
/// The order within a hop is found by the barycentre heuristic: each actor is moved toward the mean height of the actors
/// it has lines to, a few sweeps back and forth over the columns. It is deterministic: the same graph is always sorted
/// the same way. The sorting is arrangement, not a fact about the owners (principle 2).
/// </remarks>
public static class TidyLayout
{
    /// <summary>
    /// The distance from one row to the next: a node and a gap a little wider than in the hop columns, so that two
    /// amounts of a fan of lines stand between two rows with air around them.
    /// </summary>
    public const double RowSpacing = GraphSizes.RowSpacing + 32;

    /// <summary>The distance from one column to the next: a node and room for the longest amount between them.</summary>
    public const double ColumnSpacing = GraphSizes.LaneSpacing;

    /// <summary>How many times the actors are ordered back and forth over the columns.</summary>
    public const int Sweeps = 4;

    /// <summary>Gives every node's place on the sorted surface.</summary>
    /// <param name="nodes">The nodes in the graph's order.</param>
    /// <param name="links">The lines between them, by the nodes' keys; a line to a node not given is ignored.</param>
    /// <param name="widthOf">
    /// How wide each node is drawn (sketch GR5), so that a column with a wide node moves the next one along and the gap
    /// for the amounts stays; <see langword="null"/> for the graph's width.
    /// </param>
    /// <returns>Each node's top-left corner.</returns>
    public static IReadOnlyDictionary<string, GraphPoint> Arrange(
        IReadOnlyList<GraphNode> nodes,
        IEnumerable<(string From, string To)> links,
        Func<string, double>? widthOf = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(links);
        widthOf ??= _ => GraphSizes.NodeWidth;
        var columns = Columns(nodes);
        var neighbours = Neighbours(nodes, links);
        var heights = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            Measure(column, heights);
        }

        for (var sweep = 0; sweep < Sweeps; sweep++)
        {
            // Forth and back, so that both the money's sources and its receivers pull on each hop.
            var order = sweep % 2 == 0 ? columns : Enumerable.Reverse(columns);
            foreach (var column in order)
            {
                Reorder(column, neighbours, heights);
            }
        }

        return Places(columns, heights, widthOf);
    }

    /// <summary>
    /// Groups the nodes in columns by hop, left to right from the earliest hop, with the actors no start point reaches
    /// in a column of their own last; each column in the graph's order (by time) to begin with.
    /// </summary>
    /// <param name="nodes">The nodes.</param>
    /// <returns>The columns.</returns>
    private static List<List<string>> Columns(IReadOnlyList<GraphNode> nodes)
    {
        var hops = nodes.Select(node => node.Hop).OfType<int>().Distinct().Order().ToList();
        var noHop = hops.Count == 0 ? 0 : hops[^1] + 1;
        return nodes
            .GroupBy(node => node.Hop ?? noHop)
            .OrderBy(group => group.Key)
            .Select(group => group.Select(node => node.Key).ToList())
            .ToList();
    }

    /// <summary>Gives every node's neighbours: the other ends of its lines, each once, in a fixed order.</summary>
    /// <param name="nodes">The nodes.</param>
    /// <param name="links">The lines.</param>
    /// <returns>The neighbours by node.</returns>
    private static Dictionary<string, List<string>> Neighbours(
        IReadOnlyList<GraphNode> nodes, IEnumerable<(string From, string To)> links)
    {
        var neighbours = nodes.ToDictionary(node => node.Key, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var (from, to) in links)
        {
            if (from == to || !neighbours.TryGetValue(from, out var outgoing)
                || !neighbours.TryGetValue(to, out var incoming))
            {
                continue;
            }

            if (!outgoing.Contains(to))
            {
                outgoing.Add(to);
                incoming.Add(from);
            }
        }

        return neighbours;
    }

    /// <summary>
    /// Orders a column by the mean height of each actor's neighbours, keeping an actor without neighbours where it is,
    /// and measures it again. Ties keep the column's order, so that the result is the same every time.
    /// </summary>
    /// <param name="column">The column's actors, ordered in place.</param>
    /// <param name="neighbours">The neighbours by node.</param>
    /// <param name="heights">Every actor's height, updated for the column.</param>
    private static void Reorder(
        List<string> column, IReadOnlyDictionary<string, List<string>> neighbours, Dictionary<string, double> heights)
    {
        var wanted = column
            .Select((key, index) => (Key: key, Index: index, Height: Barycentre(key, neighbours[key], heights)))
            .OrderBy(entry => entry.Height)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Key)
            .ToList();
        column.Clear();
        column.AddRange(wanted);
        Measure(column, heights);
    }

    /// <summary>Gives the mean height of a node's neighbours, or its own height when it has none.</summary>
    /// <param name="key">The node.</param>
    /// <param name="neighbours">Its neighbours.</param>
    /// <param name="heights">Every actor's height.</param>
    /// <returns>The height it is drawn toward.</returns>
    private static double Barycentre(string key, List<string> neighbours, IReadOnlyDictionary<string, double> heights) =>
        neighbours.Count == 0 ? heights[key] : neighbours.Average(neighbour => heights[neighbour]);

    /// <summary>
    /// Gives each actor of a column its height from the column's middle: row by row, the rows centred on the middle,
    /// so that columns of different lengths stand balanced around the same line.
    /// </summary>
    /// <param name="column">The column's actors in order.</param>
    /// <param name="heights">Where the heights are written.</param>
    private static void Measure(IReadOnlyList<string> column, Dictionary<string, double> heights)
    {
        var (rows, spread) = Shape(column.Count);
        var middle = (rows - 1) / 2.0;
        for (var index = 0; index < column.Count; index++)
        {
            heights[column[index]] = ((index / spread) - middle) * RowSpacing;
        }
    }

    /// <summary>
    /// Gives a column's shape: how many rows, and how many actors stand side by side in a row. A hop with many actors is
    /// spread side by side (<see cref="HopLayout.RowsFor"/>), filled row by row, so that actors pulled to the same height
    /// stand next to each other rather than far apart in different sub-columns.
    /// </summary>
    /// <param name="count">The column's actors.</param>
    /// <returns>The rows and the actors per row.</returns>
    private static (int Rows, int Spread) Shape(int count)
    {
        var tallest = HopLayout.RowsFor(count);
        var spread = Math.Max(1, (count + tallest - 1) / tallest);
        var rows = Math.Max(1, (count + spread - 1) / spread);
        return (rows, spread);
    }

    /// <summary>
    /// Turns the columns and heights into places on the surface: the columns left to right, each as wide as its
    /// actors side by side, and the whole moved so that its top stands under the columns' names. A column with a wide
    /// node (sketch GR5) steps by that node's width, as in the hop columns, so that the amounts keep their gap.
    /// </summary>
    /// <param name="columns">The columns in order.</param>
    /// <param name="heights">Every actor's height from the middle.</param>
    /// <param name="widthOf">How wide each node is drawn.</param>
    /// <returns>Each node's top-left corner.</returns>
    private static Dictionary<string, GraphPoint> Places(
        IReadOnlyList<List<string>> columns,
        IReadOnlyDictionary<string, double> heights,
        Func<string, double> widthOf)
    {
        var places = new Dictionary<string, GraphPoint>(StringComparer.Ordinal);
        if (heights.Count == 0)
        {
            return places;
        }

        var top = heights.Values.Min();
        var left = GraphSizes.Left;
        foreach (var column in columns)
        {
            var (_, spread) = Shape(column.Count);
            var step = StepOf(column, widthOf);
            for (var index = 0; index < column.Count; index++)
            {
                var x = left + (index % spread * step);
                places[column[index]] = new GraphPoint(x, GraphSizes.Top + heights[column[index]] - top);
            }

            left += spread * step;
        }

        return places;
    }

    /// <summary>
    /// Gives the step from one column to the next beside a column: <see cref="ColumnSpacing"/> for nodes of the
    /// graph's width, and as much more as the column's widest node is wider.
    /// </summary>
    /// <param name="column">The column's actors.</param>
    /// <param name="widthOf">How wide each node is drawn.</param>
    /// <returns>The step.</returns>
    private static double StepOf(IReadOnlyList<string> column, Func<string, double> widthOf)
    {
        var widest = column.Count == 0 ? GraphSizes.NodeWidth : column.Max(widthOf);
        return ColumnSpacing + Math.Max(0, widest - GraphSizes.NodeWidth);
    }
}
