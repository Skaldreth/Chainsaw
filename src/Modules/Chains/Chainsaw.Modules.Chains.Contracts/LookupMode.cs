namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>Whether a lookup may ask the source.</summary>
public enum LookupMode
{
    /// <summary>
    /// Ask the source when the answer may have changed or is missing; answer from the case when it cannot have
    /// changed. The default.
    /// </summary>
    AsNeeded,

    /// <summary>
    /// Answer only from the case and never ask the source, for example when a graph is shown again. What may have
    /// changed comes as <see cref="LookupStatus.FromCaseMayBeOutdated"/>, and what is missing as
    /// <see cref="LookupStatus.NotInCase"/>; nothing is sent.
    /// </summary>
    CaseOnly,
}
