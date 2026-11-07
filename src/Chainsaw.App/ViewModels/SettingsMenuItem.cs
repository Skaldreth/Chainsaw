using Chainsaw.App.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>A page in the settings dialog's menu, marked while it is shown.</summary>
/// <param name="entry">The page's entry.</param>
public sealed partial class SettingsMenuItem(SettingsEntry entry) : ObservableObject
{
    /// <summary>Gets the page's entry.</summary>
    public SettingsEntry Entry { get; } = entry;

    /// <summary>Gets whether the page is shown.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>A group of the settings dialog's menu, with its pages.</summary>
/// <param name="Name">The group's name, or empty.</param>
/// <param name="Items">Its pages.</param>
public sealed record SettingsMenuGroup(string Name, IReadOnlyList<SettingsMenuItem> Items)
{
    /// <summary>Gets whether the group has a name to show.</summary>
    public bool HasName => Name.Length > 0;
}
