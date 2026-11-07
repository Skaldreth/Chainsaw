namespace Chainsaw.Modules.Pricing.Contracts;

/// <summary>Whether a rate may be asked for from its source.</summary>
public enum PriceLookupMode
{
    /// <summary>Take the rate from the case when it is there, otherwise ask the source. The default.</summary>
    AsNeeded,

    /// <summary>
    /// Take the rate only from the case and never ask, for example when a graph is shown again; a rate the case does
    /// not have comes as <see cref="PriceLookupStatus.NotInCase"/>.
    /// </summary>
    CaseOnly,
}
