namespace Chainsaw.App.Navigation;

/// <summary>A view in the rail: its name, its icon and the shortcut that opens it.</summary>
/// <param name="View">The view.</param>
/// <param name="Name">What the view is called, shown when the pointer rests on its icon, e.g. <c>Graf</c>.</param>
/// <param name="Icon">The key of the view's icon in <c>Themes/Icons.xaml</c>, e.g. <c>Icon.Graph</c>.</param>
/// <param name="Gesture">The shortcut as it is shown, e.g. <c>Ctrl+1</c>.</param>
/// <param name="Group">Where the view stands in the rail.</param>
public sealed record ViewEntry(AppView View, string Name, string Icon, string Gesture, RailGroup Group)
{
    /// <summary>Gets the tooltip of the view's icon: its name and its shortcut, e.g. <c>Graf · Ctrl+1</c>.</summary>
    public string ToolTip => Name + " · " + Gesture;
}
