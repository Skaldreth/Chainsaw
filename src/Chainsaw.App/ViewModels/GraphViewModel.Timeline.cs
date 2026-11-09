using Chainsaw.App.Graph;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The time line under the graph (sketch TL1a): the bands between the days over the surface and the strip with one
/// stroke per transaction, which follow the graph shown.
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>The time line, made the first time it is asked for.</summary>
    private GraphTimelineViewModel? _timeline;

    /// <summary>Gets the time line under the graph; a new time, chosen or hovered, draws the graph again.</summary>
    public GraphTimelineViewModel Timeline => _timeline ??= CreateTimeline();

    /// <summary>
    /// Gives whether a journal line of the open case, by its sequence, was added on another computer
    /// (<see cref="ICase.WasAddedElsewhere"/>); an in-memory answer, so it can be asked on the UI thread.
    /// </summary>
    /// <param name="journal">The journal line's sequence.</param>
    /// <returns><see langword="true"/> when it was; never without an open case.</returns>
    public bool WasAddedElsewhere(long journal) =>
        _session.Current is { } openCase && openCase.WasAddedElsewhere(CaseChains.Journal, journal);

    /// <summary>Gives the time line the times of each new graph.</summary>
    /// <param name="value">The graph, or <see langword="null"/>.</param>
    partial void OnSceneChanged(GraphScene? value)
    {
        Timeline.Show(value);
        KeepOpenSwap();
        UpdateObfuscation();
        _ = ReadHeldBackAsync();
    }

    /// <summary>Makes the time line, which draws the graph again when its times change.</summary>
    /// <returns>The time line.</returns>
    private GraphTimelineViewModel CreateTimeline()
    {
        var timeline = new GraphTimelineViewModel();
        timeline.Changed += (_, _) => Redraw?.Invoke(this, EventArgs.Empty);
        return timeline;
    }
}
