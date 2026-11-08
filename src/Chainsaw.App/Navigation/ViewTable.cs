namespace Chainsaw.App.Navigation;

/// <summary>
/// The views of the rail in their order (sketch SH1b), from which the rail and the window's shortcuts are made. No WPF,
/// so that it is tested in Chainsaw.App.Tests.
/// </summary>
public static class ViewTable
{
    /// <summary>The shortcut that puts the cursor in the command bar at the bottom.</summary>
    public const string CommandGesture = "Ctrl+K";

    /// <summary>The second shortcut to the command bar: search is done there and nowhere else (2026-11-07).</summary>
    public const string SearchGesture = "Ctrl+F";

    /// <summary>The shortcut that locks Chainsaw at once: the vault, the case and everything shown.</summary>
    public const string LockGesture = "Ctrl+L";

    /// <summary>Gets every view in the order of the rail.</summary>
    public static IReadOnlyList<ViewEntry> All { get; } =
    [
        new(AppView.New, "Nytt", "Icon.Plus", "Ctrl+N", RailGroup.Top),
        new(AppView.Graph, "Graf", "Icon.Graph", "Ctrl+1", RailGroup.Work, IconNeedsCase: true),
        new(AppView.Timeline, "Tidslinje", "Icon.Timeline", "Ctrl+2", RailGroup.Work, NeedsCase: true),
        new(AppView.Flow, "Flöde", "Icon.Flow", "Ctrl+3", RailGroup.Work, NeedsCase: true),
        new(AppView.Exposure, "Exponering", "Icon.Exposure", "Ctrl+4", RailGroup.Work, NeedsCase: true),
        new(AppView.Requests, "Begäran", "Icon.Request", "Ctrl+5", RailGroup.Work, NeedsCase: true),
        new(AppView.Monitoring, "Bevakning", "Icon.Eye", "Ctrl+6", RailGroup.Work, NeedsCase: true),
        new(AppView.Labels, "Etiketter", "Icon.Tag", "Ctrl+7", RailGroup.Work, NeedsCase: true),
        new(AppView.Report, "Rapport", "Icon.Report", "Ctrl+8", RailGroup.Work, NeedsCase: true),
        new(AppView.Guide, "Guide", "Icon.Guide", "F1", RailGroup.Bottom),
        new(AppView.Settings, "Inställningar", "Icon.Settings", "Ctrl+,", RailGroup.Bottom),
    ];

    /// <summary>Gets the view that opens first.</summary>
    public static AppView Start => AppView.Graph;

    /// <summary>Gets the entry of a view.</summary>
    /// <param name="view">The view.</param>
    /// <returns>The view's entry; every view has one.</returns>
    public static ViewEntry For(AppView view) => All.First(e => e.View == view);

    /// <summary>Gets the views of one group of the rail, in their order.</summary>
    /// <param name="group">The group.</param>
    /// <returns>The group's views.</returns>
    public static IReadOnlyList<ViewEntry> In(RailGroup group) => All.Where(e => e.Group == group).ToList();
}
