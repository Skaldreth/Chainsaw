using Chainsaw.App.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// An icon in the rail: the view it opens or the submenu that folds out of it (sketch PN1), whether it is shown, and a
/// dot when something waits there.
/// </summary>
public sealed partial class RailItemViewModel : ObservableObject
{
    /// <summary>Creates the icon for a view.</summary>
    /// <param name="entry">The view's entry in <see cref="ViewTable"/>.</param>
    public RailItemViewModel(ViewEntry entry)
    {
        Entry = entry;
        Submenu = SubmenuTable.For(entry.View) is { } rows ? new SubmenuViewModel(rows) : null;
    }

    /// <summary>Gets the view's entry: name, icon and shortcut.</summary>
    public ViewEntry Entry { get; }

    /// <summary>
    /// Gets the submenu that folds out of the icon, or <see langword="null"/> when the icon opens its view directly.
    /// </summary>
    public SubmenuViewModel? Submenu { get; }

    /// <summary>Gets or sets whether this view is the one shown.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>
    /// Gets or sets how many things wait in the view, for example watches that have fired. Shown as a dot, not a number
    /// (the visual language); zero shows nothing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWaiting))]
    public partial int Waiting { get; set; }

    /// <summary>
    /// Gets or sets whether the icon stands in the rail: one whose view needs a case folds away without one.
    /// </summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; } = true;

    /// <summary>Gets whether the dot is shown.</summary>
    public bool HasWaiting => Waiting > 0;
}
