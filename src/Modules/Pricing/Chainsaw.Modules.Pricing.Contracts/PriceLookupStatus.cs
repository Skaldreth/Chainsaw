namespace Chainsaw.Modules.Pricing.Contracts;

/// <summary>How a rate was answered; never silent about what is missing (principle 9).</summary>
public enum PriceLookupStatus
{
    /// <summary>At least one rate was fetched now and sealed in the case; the rest came from the case.</summary>
    Fetched,

    /// <summary>
    /// Every rate came from the case without asking. A rate in the case is never fetched again, since it is for a
    /// period that has ended.
    /// </summary>
    FromCase,

    /// <summary>No exit is connected and the case does not have the rate; nothing was sent.</summary>
    Offline,

    /// <summary>The rate was to come from the case only, and the case does not have it; nothing was sent.</summary>
    NotInCase,

    /// <summary>
    /// The source has no rate for the time: before its history begins, or not set yet (a period that has not ended,
    /// or a day the Riksbank has not published yet).
    /// </summary>
    NotAvailable,

    /// <summary>
    /// The request cannot be answered by the sources: a chain or currency they do not have, or a time in the future;
    /// nothing was sent.
    /// </summary>
    Unsupported,

    /// <summary>The lookup failed (the source answered with an error, or the exit failed).</summary>
    Failed,
}
