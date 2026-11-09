using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.Modules.Tracing.Contracts;

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

    /// <summary>
    /// Gets the swaps' price checks from before the trace was calculated again with newly fetched rates, so that the
    /// next reading says what changed; <see langword="null"/> when no such reading is waiting.
    /// </summary>
    private IReadOnlyList<SwapPriceCheck>? _swapChecksBefore;

    /// <summary>
    /// Gets whether the last reading for the swaps' rates tested no more prices than before, so that the graph is not
    /// read again for them until a reading of its own: a rate that keeps arriving but tests nothing can never loop.
    /// </summary>
    private bool _swapRereadStalled;

    /// <summary>What the toaster says when the trace through a swap changed after the swaps' rates arrived.</summary>
    public const string SwapsRecalculatedText = "Spåret genom en swap räknades om efter kurserna.";

    /// <summary>
    /// Says which swaps the trace goes through differently now that their price is tested: one whose price became
    /// absurd (the value no longer follows), where less came out than went in (part stays in the pool), or where more
    /// came out (only the share that went in follows).
    /// </summary>
    /// <param name="before">The checks before the rates arrived.</param>
    /// <param name="after">The checks now.</param>
    /// <returns>
    /// A line per changed swap, for example <c>₮ → Ξ i tx 0x12…ab: orimligt pris …</c>; empty when none.
    /// </returns>
    public static IReadOnlyList<string> RecalculatedSwaps(
        IReadOnlyList<SwapPriceCheck> before, IReadOnlyList<SwapPriceCheck> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        return
        [
            .. after.Where(check => check.ChangesAmounts)
                .Where(check => before.FirstOrDefault(old => old.Swap.Equals(check.Swap)) is { } old
                    && (old.Verdict != check.Verdict || old.KeptShare != check.KeptShare
                        || old.FollowShare != check.FollowShare))
                .Select(check => $"{SwapMarks.Of(check.Swap).Text} i tx "
                    + $"{GraphScene.Shorten(check.Swap.Transaction.Value)}: {check.Summary}"),
        ];
    }

    /// <summary>Counts the swaps whose price is not tested.</summary>
    /// <param name="checks">The swaps' price checks.</param>
    /// <returns>How many.</returns>
    private static int Untested(IReadOnlyList<SwapPriceCheck> checks) =>
        checks.Count(check => check.Verdict == SwapPriceVerdict.NotTested);

    /// <summary>
    /// Says once, after the trace was calculated again with the swaps' rates, which swaps it now goes through
    /// differently (M-1: never a silent change).
    /// </summary>
    /// <param name="scene">The scene just read.</param>
    private void TellRecalculatedSwaps(GraphScene scene)
    {
        if (_swapChecksBefore is not { } before)
        {
            _swapRereadStalled = false;
            return;
        }

        _swapChecksBefore = null;
        var after = scene.Snapshot.AccountTrace?.PriceChecks ?? [];
        _swapRereadStalled = Untested(after) >= Untested(before);
        var changed = RecalculatedSwaps(before, after);
        if (changed.Count > 0)
        {
            Tell(ToastKind.Information, SwapsRecalculatedText, string.Join(" · ", changed));
        }
    }

    /// <summary>Forgets where the amounts were drawn, as the canvas starts drawing them again.</summary>
    public void BeginAmounts() => _drawnAmounts.Clear();

    /// <summary>Remembers where a line's amount was drawn, on the surface.</summary>
    /// <param name="edgeKey">The line.</param>
    /// <param name="box">The dark box the amount and its value stand in.</param>
    public void AmountDrawn(string edgeKey, GraphRect box) => _drawnAmounts[edgeKey] = box;

    /// <summary>
    /// Fetches the rates the scene's lines lack in the background, through the exit, a batch at a time
    /// (<see cref="GraphSource.MostRateLookups"/>), and fills them in as they come, so that showing or expanding the
    /// graph never waits for a source (principle 6). It goes on only while a batch brings at least one new rate, and
    /// stops at once when a source asks to wait. Offline, nothing is asked and the lines say so (principle 9).
    /// </summary>
    /// <returns>A task that completes when nothing more can be fetched now.</returns>
    public async Task FillRatesAsync()
    {
        var fill = ++_rateFills;
        if (_session.Current is not { } openCase || Scene is not { } scene
            || (scene.PendingRates == 0 && !scene.HasUntestedSwapPrices))
        {
            return;
        }

        if (IsOffline)
        {
            if (scene.Snapshot.ValueGaps.Values.Any(gap => gap is { IsPending: true, IsOffline: false }))
            {
                await ShowRatesAsync(scene.Rates with { Gaps = EdgeValues.Offline(scene.Snapshot.ValueGaps) }, fill);
            }

            return;
        }

        var version = _version;
        var token = _work.Token;
        var valued = scene.Snapshot.Values.Count;
        while (true)
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

            if (fill != _rateFills || version != _version || !await ShowRatesAsync(rates, fill))
            {
                return;
            }

            if (rates.SwapRatesFetched && !_swapRereadStalled)
            {
                // A swap's price can now be tested, which may change where the traced money goes: the trace is
                // calculated again, and the reading says once what changed (TellRecalculatedSwaps).
                _swapChecksBefore = scene.Snapshot.AccountTrace?.PriceChecks ?? [];
                await RefreshAsync();

                // A reading that was replaced or waits for an expansion never compares with these checks.
                _swapChecksBefore = null;
                return;
            }

            if (rates.IsThrottled || rates.Pending == 0 || rates.Values.Count <= valued)
            {
                // The source asked to wait, everything is in, or the batch brought no new rate: stop, and let each
                // line say why; Pricing waits a while before it asks again for a rate that just failed.
                return;
            }

            valued = rates.Values.Count;
        }
    }

    /// <summary>
    /// Follows the exit: offline the lines waiting for a rate say so at once (principle 9), and when the exit is back
    /// they are fetched.
    /// </summary>
    /// <param name="value">Whether lookups cannot go now.</param>
    partial void OnIsOfflineChanged(bool value) => _ = FillRatesAsync();

    /// <summary>
    /// Shows the lines' values without reading the graph again, keeping what is chosen: the scene is built off the UI
    /// thread (principle 6), and shown only when no newer fetch or reading came meanwhile; then it is drawn the way
    /// the graph is read now.
    /// </summary>
    /// <param name="rates">The values.</param>
    /// <param name="fill">The fetch they come from.</param>
    /// <returns>Whether they were shown.</returns>
    private async Task<bool> ShowRatesAsync(GraphRates rates, int fill)
    {
        if (Scene is not { } scene)
        {
            return false;
        }

        var version = _version;
        var built = await Task.Run(() => scene.WithRates(rates));
        if (fill != _rateFills || version != _version || !ReferenceEquals(Scene, scene))
        {
            return false;
        }

        Scene = built;
        Arrange();
        Inspect();
        Redraw?.Invoke(this, EventArgs.Empty);
        _ = FollowReadingAsync();
        return true;
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
