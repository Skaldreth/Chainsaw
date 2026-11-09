using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Pricing.Contracts;

/// <summary>
/// Reads the prices a case already holds, synchronously and for many times in one read of the case, for an analysis
/// that runs synchronously over the whole graph (for example the check of round amounts in fiat). It never asks a
/// source and never sends anything (principles 4 and 9): a rate the case does not have is answered with
/// <see cref="PriceLookupStatus.NotInCase"/>, never guessed. A price read here is the same as the one
/// <see cref="IPriceService.GetPriceAsync"/> gives from the case with <see cref="PriceLookupMode.CaseOnly"/>.
/// </summary>
public interface ICaseRates
{
    /// <summary>
    /// Gives the price the case holds for each request, in the requests' order. Every answer is
    /// <see cref="PriceLookupStatus.FromCase"/> with its price, <see cref="PriceLookupStatus.NotInCase"/>,
    /// <see cref="PriceLookupStatus.NotAvailable"/> (the case holds the source's sealed answer that it has no rate for
    /// the time) or <see cref="PriceLookupStatus.Unsupported"/>; never <see cref="PriceLookupStatus.Fetched"/>.
    /// </summary>
    /// <param name="case">The open case the rates are kept in.</param>
    /// <param name="requests">What to price, when and in which currency.</param>
    /// <returns>One answer per request.</returns>
    /// <exception cref="ArgumentNullException">The case or the requests are null, or a request is null.</exception>
    /// <exception cref="CaseException"><see cref="CaseError.Closed"/>.</exception>
    IReadOnlyList<PriceLookup<Price>> ReadStored(ICase @case, IReadOnlyList<PriceRequest> requests);
}
