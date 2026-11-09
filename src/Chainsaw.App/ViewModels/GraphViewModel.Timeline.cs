using Chainsaw.App.Graph;

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
