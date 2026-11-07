namespace Chainsaw.App.Navigation;

/// <summary>What a row of a submenu needs to be chosen; without it the row is toned down and says why.</summary>
public enum SubmenuNeed
{
    /// <summary>Nothing: the row can always be chosen.</summary>
    Nothing,

    /// <summary>An open case: without one the row is toned down.</summary>
    OpenCase,

    /// <summary>
    /// The row's function is not built yet: it is always toned down, and its tooltip says so honestly.
    /// </summary>
    NotBuilt,
}
