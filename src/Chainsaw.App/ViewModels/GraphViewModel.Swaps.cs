using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The choice per swap (sketch SW1a): a click on the pill <c>₮ → Ξ</c> unfolds it in place to <em>Följer</em> and
/// <em>Följer inte</em>, and the choice is sealed in the case through Tracing, with who, when and a line in the
/// journal (principle 1); the graph is then read again, so that the trace follows the choice. Knows nothing of WPF.
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>What the toaster says when a choice could not be stored.</summary>
    public const string SwapChoiceFailedText = "Valet för swappen kunde inte sparas.";

    /// <summary>Gets the line whose swap pill is unfolded to the choice, or <see langword="null"/>.</summary>
    public string? OpenSwap { get; private set; }

    /// <summary>Gets whether a choice for a swap is being stored, so that the pill takes no second click meanwhile.</summary>
    public bool IsChoosingSwap { get; private set; }

    /// <summary>Unfolds a line's swap pill to the choice, or folds it again (a click on the pill).</summary>
    /// <param name="edgeKey">The line, or <see langword="null"/> to fold the open pill.</param>
    public void ToggleSwap(string? edgeKey)
    {
        // A swap with an absurd price has nothing to choose: its value never follows.
        OpenSwap = edgeKey is not null && OpenSwap != edgeKey
            && Scene?.Edge(edgeKey) is { Swaps.Count: > 0, IsSwapPriceAbsurd: false }
            ? edgeKey
            : null;
        Redraw?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Chooses whether the traced value follows through the swaps on a line (<em>Följer</em> or <em>Följer inte</em>):
    /// <em>Följer inte</em> seals a choice for each swap that is followed, <em>Följer</em> takes back each choice in
    /// force; choosing what already holds stores nothing. The pill folds, and the graph is read again.
    /// </summary>
    /// <param name="edgeKey">The line.</param>
    /// <param name="follows">Whether the value follows.</param>
    /// <returns>A task that completes when the choice is stored and the graph read again.</returns>
    public async Task ChooseSwapAsync(string edgeKey, bool follows)
    {
        OpenSwap = null;
        Redraw?.Invoke(this, EventArgs.Empty);
        if (IsChoosingSwap || _session.Current is not { } openCase || Scene?.Edge(edgeKey) is not { } edge)
        {
            return;
        }

        IsChoosingSwap = true;
        var changed = false;
        try
        {
            if (follows)
            {
                foreach (var stop in edge.SwapStops)
                {
                    changed |= (await _editor.FollowSwapAsync(openCase, stop, null)).Done;
                }
            }
            else
            {
                foreach (var swap in edge.Swaps.Where(swap => !edge.SwapStops.Any(stop => stop.Is(swap))))
                {
                    await _editor.StopAtSwapAsync(openCase, swap, null);
                    changed = true;
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Tell(ToastKind.Error, SwapChoiceFailedText, InspectorFormsViewModel.Failure(exception));
        }
        finally
        {
            IsChoosingSwap = false;
        }

        if (changed)
        {
            await RefreshAsync();
        }
    }

    /// <summary>Opens the Guide on the trace through a swap (the <em>?</em> in the unfolded pill).</summary>
    public void OpenSwapGuide() => GuideWanted?.Invoke(
        this, GuideRequest.ForTopic(GuideTopic.Swaps) with { Heading = SwapPill.GuideHeading, IsChoice = true });

    /// <summary>Folds the open pill when its line is no longer in the graph.</summary>
    private void KeepOpenSwap()
    {
        if (OpenSwap is { } open && Scene?.Edge(open) is not { Swaps.Count: > 0 })
        {
            OpenSwap = null;
        }
    }
}
