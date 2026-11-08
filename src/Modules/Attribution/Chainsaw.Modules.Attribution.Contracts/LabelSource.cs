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
    /// Another case, through the label library on this computer. Everything follows: who with title and unit, when,
    /// why, the note and the case it was set in (the user's decision 2026-11-07, <em>Allt följer med</em>).
    /// </summary>
    OtherCase,

    /// <summary>
    /// A supplier of attribution that Chainsaw asks about an address (for example Chainabuse), with the name, the
    /// sealed response the label rests on and when it was fetched. Its reports are judgements, never facts.
    /// </summary>
    Provider,
}
