namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>Where a label comes from. The names are stored in the case, so they never change.</summary>
public enum LabelSource
{
    /// <summary>The user set it, with a reason.</summary>
    Own,

    /// <summary>A sanction list that the user imported (for example OFAC SDN), with the list's hash and date.</summary>
    SanctionList,

    /// <summary>An open source of labels, with its name and version.</summary>
    OpenSource,

    /// <summary>An analysis of behaviour on the chain, with its name and version.</summary>
    Behaviour,

    /// <summary>
    /// Another case, through the label library on this computer. Only who, when and the label came along; never
    /// the other case's number, the note or why it was set there.
    /// </summary>
    OtherCase,
}
