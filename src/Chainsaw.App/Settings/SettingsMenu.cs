namespace Chainsaw.App.Settings;

/// <summary>A page of the settings dialog as its menu and head show it.</summary>
/// <param name="Page">The page.</param>
/// <param name="Name">Its name in the menu and its title.</param>
/// <param name="Lead">The line under the title; empty when there is none.</param>
public sealed record SettingsEntry(SettingsPage Page, string Name, string Lead);

/// <summary>A group of the settings dialog's menu, with its name over it; the last group has none.</summary>
/// <param name="Name">The group's name, or empty.</param>
/// <param name="Entries">Its pages, in order.</param>
public sealed record SettingsGroup(string Name, IReadOnlyList<SettingsEntry> Entries)
{
    /// <summary>Gets whether the group has a name to show.</summary>
    public bool HasName => Name.Length > 0;
}

/// <summary>
/// The settings dialog's menu (sketch ST1a, after Gawk's): the pages in groups, the computer's first and Om last on its
/// own. No WPF, so that it is tested in Chainsaw.App.Tests.
/// </summary>
public static class SettingsMenu
{
    /// <summary>The shortcut that opens the dialog, as the pill shows it.</summary>
    public const string Gesture = "Ctrl+,";

    /// <summary>Gets the groups in order.</summary>
    public static IReadOnlyList<SettingsGroup> Groups { get; } =
    [
        new("Den här datorn",
        [
            new(SettingsPage.Network, "Nätverk", "Hur Chainsaw når källorna. Bara https, alltid."),
            new(SettingsPage.Sources, "Källor", "Var Chainsaw frågar kedjorna, per kedja."),
            new(SettingsPage.Lock, "Låsning", "När Chainsaw låser sig själv."),
            new(SettingsPage.Look, "Utseende", "Låsskärmens kätting."),
            new(SettingsPage.Links, "Länkar", "Andra program kan öppna en adress i Chainsaw."),
        ]),
        new("Arbetet", [new(SettingsPage.Pricing, "Kurser", "Kursen vid en tx:s tid, med källa.")]),
        new("Du", [new(SettingsPage.Profile, "Profil", "Vem som hämtade, i ärendets journal.")]),
        new(string.Empty, [new(SettingsPage.About, "Om Chainsaw", string.Empty)]),
    ];

    /// <summary>Gets the page that opens first.</summary>
    public static SettingsPage Start => SettingsPage.Network;

    /// <summary>Gets a page's entry.</summary>
    /// <param name="page">The page.</param>
    /// <returns>Its entry; every page has one.</returns>
    public static SettingsEntry For(SettingsPage page) =>
        Groups.SelectMany(group => group.Entries).First(entry => entry.Page == page);
}
