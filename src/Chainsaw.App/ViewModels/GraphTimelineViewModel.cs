using Chainsaw.App.Graph;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The time line under the graph (sketch TL1a), without WPF: the graph's times, the time the user chose in the strip
/// and the time under the pointer. A chosen time shows the graph as it was then: what came later is drawn faint, and
/// <em>Till nu</em> (Esc) goes back. Choosing a time only changes what is seen; nothing is written in the case.
/// </summary>
public sealed partial class GraphTimelineViewModel : ObservableObject
{
    /// <summary>What the graph's head says while a time is chosen; the time is put in.</summary>
    public const string HeadFormat = "Grafen vid {0} · det som kom senare är blekt";

    /// <summary>The time zone the days are counted in.</summary>
    private readonly TimeZoneInfo _zone;

    /// <summary>Creates the time line, counting days in a time zone.</summary>
    /// <param name="zone">The time zone; the user's own when none is given.</param>
    public GraphTimelineViewModel(TimeZoneInfo? zone = null)
    {
        _zone = zone ?? TimeZoneInfo.Local;
        Axis = new TimeAxis(DateTimeOffset.UnixEpoch, _zone);
    }

    /// <summary>Raised when what the graph draws changes: new times, a chosen time or the time under the pointer.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the graph's times.</summary>
    public GraphTimeline Times { get; private set; } = GraphTimeline.Empty;

    /// <summary>Gets the time axis, which starts on the first day with a transaction.</summary>
    public TimeAxis Axis { get; private set; }

    /// <summary>Gets whether the time line is shown: only when the graph has a confirmed transaction (principle 7).</summary>
    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    /// <summary>Gets the time the user chose in the strip, or <see langword="null"/> for now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChosen), nameof(HeadText))]
    [NotifyCanExecuteChangedFor(nameof(ToNowCommand))]
    public partial DateTimeOffset? Chosen { get; private set; }

    /// <summary>Gets the time under the pointer in the strip, or <see langword="null"/>.</summary>
    public DateTimeOffset? Hovered { get; private set; }

    /// <summary>Gets whether a time is chosen, so that the graph is shown as it was then.</summary>
    public bool IsChosen => Chosen is not null;

    /// <summary>Gets what the graph's head says while a time is chosen, or an empty text.</summary>
    public string HeadText => Chosen is { } at ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
        HeadFormat, Axis.TimeName(at)) : string.Empty;

    /// <summary>Reads the times of a new graph; with no graph, the chosen time goes back to now.</summary>
    /// <param name="scene">The graph, or <see langword="null"/>.</param>
    public void Show(GraphScene? scene)
    {
        Times = GraphTimeline.Of(scene);
        if (Times.Ticks.Count > 0)
        {
            Axis = new TimeAxis(Times.Ticks[0].Time, _zone);
        }

        IsShown = Times.Ticks.Count > 0;
        if (!IsShown)
        {
            Chosen = null;
            Hovered = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows the graph as it was at a time: what came later is drawn faint.</summary>
    /// <param name="time">The time.</param>
    public void Choose(DateTimeOffset time)
    {
        if (!IsShown)
        {
            return;
        }

        Chosen = time;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Goes back to the graph as it is now (<em>Till nu</em>, Esc).</summary>
    [RelayCommand(CanExecute = nameof(IsChosen))]
    public void ToNow()
    {
        if (Chosen is null)
        {
            return;
        }

        Chosen = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Says which time is under the pointer in the strip, so that a faint front shows it in the graph.</summary>
    /// <param name="time">The time, or <see langword="null"/> when the pointer left the strip.</param>
    public void Hover(DateTimeOffset? time)
    {
        if (Hovered == time)
        {
            return;
        }

        Hovered = time;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Says whether an actor is drawn faint, because it came after the chosen time.</summary>
    /// <param name="nodeKey">The actor.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool IsLaterNode(string nodeKey) => Chosen is { } at && Times.IsLaterNode(nodeKey, at);

    /// <summary>Says whether a line is drawn faint, because it first carried money after the chosen time.</summary>
    /// <param name="edgeKey">The line.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool IsLaterEdge(string edgeKey) => Chosen is { } at && Times.IsLaterEdge(edgeKey, at);
}
