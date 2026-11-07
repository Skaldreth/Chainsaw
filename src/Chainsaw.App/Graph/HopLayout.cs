using System.Globalization;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>A hop column (sketch GR4), drawn as a faint band with its name at the top.</summary>
/// <param name="X">The column's left edge.</param>
/// <param name="Name">The column's name: <c>Start</c>, <c>+1</c>, <c>−1</c> or <c>Utan hopp</c>.</param>
public sealed record HopLane(double X, string Name)
{
    /// <summary>Gets the band's width: one node, or more when the hop's actors are spread over several columns.</summary>
    public double Width { get; init; } = GraphSizes.NodeWidth;
}

/// <summary>
/// Places the actors in hop columns (sketch GR4): one column per hop from the start, left to right along the money,
/// and the actors in a column in the graph's order (by time). The free surface starts from the same places, so that
/// the actors first lie roughly in the order of the hops.
/// </summary>
/// <remarks>The hops are arrangement, not a fact about the owners (principle 2).</remarks>
public static class HopLayout
{
    /// <summary>The name of the column for actors that no start point reaches.</summary>
    public const string NoHopName = "Utan hopp";

    /// <summary>The name of the start's column.</summary>
    public const string StartName = "Start";

    /// <summary>
    /// The most actors one hop stands in, one under the other, before the hop is spread over more columns side by side,
    /// so that the graph uses the surface instead of one long column (CLAUDE.md, <em>Grafen använder ytan</em>).
    /// </summary>
    public const int MostRows = 8;

    /// <summary>Gives the columns and every node's place in them.</summary>
    /// <param name="nodes">The nodes in the graph's order.</param>
    /// <returns>The columns from left to right, and each node's top-left corner.</returns>
    public static (IReadOnlyList<HopLane> Lanes, IReadOnlyDictionary<string, GraphPoint> Places) Arrange(
        IReadOnlyList<GraphNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var hops = nodes.Select(node => node.Hop).OfType<int>().Distinct().Order().ToList();
        var first = hops.Count == 0 ? 0 : hops[0];
        var lastColumn = hops.Count == 0 ? -1 : hops[^1] - first;
        var hasUnordered = nodes.Any(node => node.Hop is null);
        var noHopColumn = lastColumn + 1;

        var byColumn = nodes
            .GroupBy(node => node.Hop is { } hop ? hop - first : noHopColumn)
            .ToDictionary(group => group.Key, group => group.ToList());
        var lanes = new List<HopLane>();
        var places = new Dictionary<string, GraphPoint>(StringComparer.Ordinal);
        var left = GraphSizes.Left;
        var last = hasUnordered ? noHopColumn : lastColumn;
        for (var column = 0; column <= last; column++)
        {
            var actors = byColumn.GetValueOrDefault(column) ?? [];
            var rows = RowsFor(actors.Count);
            var spread = Math.Max(1, (actors.Count + rows - 1) / rows);
            var name = column == noHopColumn && hasUnordered ? NoHopName : NameOf(column + first);
            lanes.Add(new HopLane(left, name) { Width = ((spread - 1) * GraphSizes.LaneSpacing) + GraphSizes.NodeWidth });
            for (var index = 0; index < actors.Count; index++)
            {
                // Down each column first, so that the order by time still reads from the top.
                var (sub, row) = Math.DivRem(index, rows);
                places[actors[index].Key] = new GraphPoint(
                    left + (sub * GraphSizes.LaneSpacing), GraphSizes.Top + (row * GraphSizes.RowSpacing));
            }

            left += spread * GraphSizes.LaneSpacing;
        }

        return (lanes, places);
    }

    /// <summary>
    /// Gives how many actors stand one under the other in a hop: all of them up to <see cref="MostRows"/>, and beyond
    /// that as many as make the hop's block about as tall as it is wide, so that hundreds of counterparties fill a
    /// square of the surface rather than a long column or a long row.
    /// </summary>
    /// <param name="count">How many actors the hop has.</param>
    /// <returns>The rows, at least one.</returns>
    public static int RowsFor(int count)
    {
        if (count <= MostRows)
        {
            return Math.Max(1, count);
        }

        var square = Math.Sqrt(count * GraphSizes.LaneSpacing / GraphSizes.RowSpacing);
        return Math.Max(MostRows, (int)Math.Ceiling(square));
    }

    /// <summary>Gives a column's left edge.</summary>
    /// <param name="column">The column, from 0 at the left.</param>
    /// <returns>The left edge.</returns>
    public static double ColumnX(int column) => GraphSizes.Left + (column * GraphSizes.LaneSpacing);

    /// <summary>Names a hop's column: <c>Start</c> for the start, <c>+2</c> after it and <c>−1</c> before it.</summary>
    /// <param name="hop">The hop.</param>
    /// <returns>The name.</returns>
    public static string NameOf(int hop) => hop switch
    {
        0 => StartName,
        > 0 => "+" + hop.ToString(CultureInfo.InvariantCulture),
        _ => "−" + (-hop).ToString(CultureInfo.InvariantCulture),
    };
}
