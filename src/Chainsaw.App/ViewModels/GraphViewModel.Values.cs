using Chainsaw.App.Graph;
using Chainsaw.App.Guide;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The lines' values (sketch KU1a): a click on a line's amount opens <em>Varför?</em> with the steps in the Guide's
/// panel, and hovering a missing value says which rate is missing.
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>
    /// Gives what hovering a line's amount says when its value is missing (<c>– kr</c>): which rate and why, or
    /// <see langword="null"/> when the point is not on such an amount.
    /// </summary>
    /// <param name="point">The point on the surface.</param>
    /// <returns>The tooltip, or <see langword="null"/>.</returns>
    public string? AmountTipAt(GraphPoint point) =>
        AmountAt(point) is { Value: null, ValueGap: { } gap } ? gap.Tip : null;

    /// <summary>
    /// Opens <em>Varför?</em> for the value of the line whose amount is under a point, after choosing the line, so
    /// that the Guide's panel shows the amount, the rates with their hashes and the calculation (sketch KU1a).
    /// </summary>
    /// <param name="point">The point on the surface.</param>
    /// <returns>Whether a value was under the point.</returns>
    public bool AskValueAt(GraphPoint point)
    {
        if (AmountAt(point) is not { Value: { } value } edge
            || GuideValuation.Of(value, TimeZoneInfo.Local) is not { } valuation)
        {
            return false;
        }

        Choose(null, edge.Key);
        GuideWanted?.Invoke(this, GuideRequest.ForValue(edge.Key, valuation));
        return true;
    }

    /// <summary>Gives the line whose written amount covers a point, or <see langword="null"/>.</summary>
    /// <param name="point">The point on the surface.</param>
    /// <returns>The line.</returns>
    private EdgeView? AmountAt(GraphPoint point)
    {
        if (Scene is not { } scene)
        {
            return null;
        }

        foreach (var (key, spot) in Amounts)
        {
            if (spot.Box.Contains(point) && scene.Edge(key) is { HasValueLine: true } edge && IsWritten(edge))
            {
                return edge;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets whether a line's amount is written on screen, as the canvas decides it: always while it is hovered,
    /// otherwise by the gallring when nothing is chosen, or when the line itself is chosen.
    /// </summary>
    /// <param name="edge">The line.</param>
    /// <returns><see langword="true"/> when the amount can be seen.</returns>
    private bool IsWritten(EdgeView edge) =>
        HoveredEdge == edge.Key || SelectedEdge == edge.Key
        || (!IsFocused && Labels.Shows(edge.IsTraced, false, false, false));
}
