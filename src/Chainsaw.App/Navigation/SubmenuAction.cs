namespace Chainsaw.App.Navigation;

/// <summary>What a row of a submenu in the pill does when it is chosen (sketch PN1).</summary>
public enum SubmenuAction
{
    /// <summary>Nothing: the row is a line between groups of rows.</summary>
    None,

    /// <summary>
    /// Adds a start point: the cursor goes to the command bar, or Nytt ärende opens when no case is open.
    /// </summary>
    StartPoint,

    /// <summary>Sets a label on an address or a cluster.</summary>
    Label,

    /// <summary>Watches an address or a cluster.</summary>
    Watch,

    /// <summary>Writes a request for information to an exchange.</summary>
    Request,

    /// <summary>Imports a sanction list from a file the user saved (Attribution).</summary>
    ImportSanctionList,

    /// <summary>Opens Nytt ärende (sketch NC1a).</summary>
    NewCase,
}
