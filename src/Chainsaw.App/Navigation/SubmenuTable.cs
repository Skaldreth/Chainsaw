namespace Chainsaw.App.Navigation;

/// <summary>
/// The submenus of the pill (sketch PN1, after Gawk's B9): the pill is the main menu, and a button in it can have a
/// submenu that folds out of the button. The rows come from here, so that another button gets a submenu by a line in
/// this table and no new code in the view. No WPF, so that it is tested in Chainsaw.App.Tests.
/// </summary>
public static class SubmenuTable
{
    /// <summary>What a toned-down row says while no case is open.</summary>
    public const string NeedsCaseText = "Kräver ett öppet ärende";

    /// <summary>Gets the submenu of Nytt: what can be made in a case, a sanction list, and a case.</summary>
    public static IReadOnlyList<SubmenuRow> New { get; } =
    [
        new(SubmenuAction.StartPoint, "Startpunkt", "S", SubmenuNeed.Nothing),
        new(SubmenuAction.Label, "Etikett", "E", SubmenuNeed.NotBuilt,
            "Inte byggt än: en etikett sätts på en nod i grafen (L)"),
        new(SubmenuAction.Watch, "Bevakning", "B", SubmenuNeed.OpenCase),
        new(SubmenuAction.Request, "Begäran", "R", SubmenuNeed.NotBuilt,
            "Inte byggt än: begäran till en börs kommer senare"),
        SubmenuRow.Line,
        new(SubmenuAction.ImportSanctionList, "Importera sanktionslista …", "I", SubmenuNeed.Nothing),
        SubmenuRow.Line,
        new(SubmenuAction.NewCase, "Ärende", ViewTable.For(AppView.New).Gesture, SubmenuNeed.Nothing),
    ];

    /// <summary>Gets the submenu of a view's button in the pill.</summary>
    /// <param name="view">The view.</param>
    /// <returns>Its rows, or <see langword="null"/> when the button opens its view directly.</returns>
    public static IReadOnlyList<SubmenuRow>? For(AppView view) => view switch
    {
        AppView.New => New,
        _ => null,
    };
}
