namespace Chainsaw.App.Links;

/// <summary>Where the link <c>chainsaw:</c> points for the user.</summary>
public enum LinkState
{
    /// <summary>The link is not registered.</summary>
    None,

    /// <summary>The link opens this program.</summary>
    ThisProgram,

    /// <summary>The link opens another program, for example an older copy of Chainsaw elsewhere.</summary>
    OtherProgram,
}
