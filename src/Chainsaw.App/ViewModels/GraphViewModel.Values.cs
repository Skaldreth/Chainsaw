using Chainsaw.App.Graph;
using Chainsaw.App.Guide;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The lines' values (sketch KU1a): the graph is shown at once with the case's rates, and the rates it lacks are
/// fetched in the background and filled in as they come; a click on a line's amount opens <em>Varför?</em> with the
/// steps in the Guide's panel, and hovering a missing value says which rate is missing.
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>Where each amount was drawn last, on the surface, so that a click hits what is seen.</summary>
    private readonly Dictionary<string, GraphRect> _drawnAmounts = new(StringComparer.Ordinal);

    /// <summary>Counts the background fetches of rates, so that only the latest one shows its answer.</summary>
    private int _rateFills;

    /// <summary>Forgets where the amounts were drawn, as the canvas starts drawing them again.</summary>
    public void BeginAmounts() => _drawnAmounts.Clear();

    /// <summary>Remembers where a line's amount was drawn, on the surface.</summary>
    /// <param name="edgeKey">The line.</param>
    /// <param name="box">The dark box the amount and its value stand in.</param>
    public void AmountDrawn(string edgeKey, GraphRect box) => _drawnAmounts[edgeKey] = box;

    /// <summary>
    /// Fetches the rates the scene's lines lack in the background, through the exit, a batch at a time
    /// (<see cref="GraphSource.MostRateLookups"/>), and fills them in as they come, so that showing or expanding the
    /// graph never waits for a source (principle 6). Offline, nothing is asked and the lines say so (principle 9).
    /// </summary>
    /// <returns>A task that completes when nothing more can be fetched now.</returns>
    public async Task FillRatesAsync()
    {
        var fill = ++_rateFills;
        if (_session.Current is not { } openCase || Scene is not { PendingRates: > 0 } scene)
        {
            return;
        }

        if (IsOffline)
        {
            if (scene.Snapshot.ValueGaps.Values.Any(gap => gap is { IsPending: true, IsOffline: false }))
            {
                ShowRates(scene.Rates with { Gaps = EdgeValues.Offline(scene.Snapshot.ValueGaps) });
            }

            return;
        }

        var version = _version;
        var token = _work.Token;
        var pending = scene.PendingRates;
        while (pending > 0)
        {
            GraphRates rates;
            try
            {
                rates = await _source.FillRatesAsync(openCase, Scene ?? scene, token);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A failed or cancelled fetch leaves the lines as they are; the next reading asks again.
                return;
            }

            if (fill != _rateFills || version != _version)
            {
                return;
            }

            ShowRates(rates);
            if (rates.Pending >= pending)
            {
                // No progress (the source refused, or the exit went away): stop, and say it honestly per line.
                return;
            }

            pending = rates.Pending;
        }
    }

    /// <summary>
    /// Follows the exit: offline the lines waiting for a rate say so at once (principle 9), and when the exit is back
    /// they are fetched.
    /// </summary>
    /// <param name="value">Whether lookups cannot go now.</param>
    partial void OnIsOfflineChanged(bool value) => _ = FillRatesAsync();

    /// <summary>Shows the lines' values without reading the graph again, keeping what is chosen.</summary>
    /// <param name="rates">The values.</param>
    private void ShowRates(GraphRates rates)
    {
        if (Scene is not { } scene)
        {
            return;
        }

        Scene = scene.WithRates(rates);
        Arrange();
        Inspect();
        Redraw?.Invoke(this, EventArgs.Empty);
    }

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

    /// <summary>
    /// Gives the line whose amount, as last drawn, covers a point, or <see langword="null"/>; an amount the gallring
    /// hides was not drawn and is never hit.
    /// </summary>
    /// <param name="point">The point on the surface.</param>
    /// <returns>The line.</returns>
    private EdgeView? AmountAt(GraphPoint point)
    {
        if (Scene is not { } scene)
        {
            return null;
        }

        foreach (var (key, box) in _drawnAmounts)
        {
            if (box.Contains(point) && scene.Edge(key) is { HasValueLine: true } edge)
            {
                return edge;
            }
        }

        return null;
    }
}
