namespace Chainsaw.App.Navigation;

/// <summary>A view in the rail: its name, its icon and the shortcut that opens it.</summary>
/// <param name="View">The view.</param>
/// <param name="Name">What the view is called, shown when the pointer rests on its icon, e.g. <c>Graf</c>.</param>
/// <param name="Icon">The key of the view's icon in <c>Themes/Icons.xaml</c>, e.g. <c>Icon.Graph</c>.</param>
/// <param name="Gesture">The shortcut as it is shown, e.g. <c>Ctrl+1</c>.</param>
/// <param name="Group">Where the view stands in the rail.</param>
/// <param name="IsBuilt">Whether the view is built; one that is not is toned down in the rail and says so.</param>
/// <param name="NeedsCase">
/// Whether the view needs an open case; without one its icon folds away, since it could not be used (principle 7).
/// </param>
/// <param name="IconNeedsCase">
/// Whether the view's icon folds away without an open case even though the view itself can be shown: the graph is the
/// start page without a case, but there is no graph to go into until a case is open (the user's decision 2026-11-08).
/// </param>
public sealed record ViewEntry(
    AppView View, string Name, string Icon, string Gesture, RailGroup Group, bool IsBuilt = true, bool NeedsCase = false,
    bool IconNeedsCase = false)
{
    /// <summary>What the tooltip of a view that is not built adds.</summary>
    public const string NotBuiltText = "Inte byggt än";

    /// <summary>
    /// Gets the tooltip of the view's icon: its name and its shortcut, e.g. <c>Graf · Ctrl+1</c>, and that it is not
    /// built when it is not.
    /// </summary>
    public string ToolTip => IsBuilt ? Name + " · " + Gesture : Name + " · " + NotBuiltText;

    /// <summary>Gets whether the view's icon stands in the rail when no case is open.</summary>
    public bool IsShownWithoutCase => !NeedsCase && !IconNeedsCase;
}
