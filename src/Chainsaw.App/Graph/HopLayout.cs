using System.Globalization;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>A hop column (sketch GR4), drawn as a faint band with its name at the top.</summary>
/// <param name="X">The column's left edge.</param>
/// <param name="Name">The column's name: <c>Start</c>, <c>+1</c>, <c>−1</c> or <c>Utan hopp</c>.</param>
public sealed record HopLane(double X, string Name);

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

        var lanes = new List<HopLane>();
        for (var column = 0; column <= lastColumn; column++)
        {
            lanes.Add(new HopLane(ColumnX(column), NameOf(column + first)));
        }

        if (hasUnordered)
        {
            lanes.Add(new HopLane(ColumnX(noHopColumn), NoHopName));
        }

        var rows = new Dictionary<int, int>();
        var places = new Dictionary<string, GraphPoint>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var column = node.Hop is { } hop ? hop - first : noHopColumn;
            var row = rows.GetValueOrDefault(column);
            rows[column] = row + 1;
            places[node.Key] = new GraphPoint(ColumnX(column), GraphSizes.Top + (row * GraphSizes.RowSpacing));
        }

        return (lanes, places);
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
