using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Pricing.Contracts;

/// <summary>
/// The price of a chain's currency at a time, in the chosen currency. Every rate is a fact from a named source, sealed
/// in the case with its raw response the first time it is fetched and never fetched again (principle 1); the
/// conversion between them is a calculation that shows its steps (principle 2). Requests go only through the exit
/// and carry only the market and the time, never anything about the case (principle 4). Without an exit the case
/// answers, or the answer honestly says <see cref="PriceLookupStatus.Offline"/> (principle 9).
/// </summary>
/// <remarks>The methods never throw for a failed lookup; the answer says what went wrong.</remarks>
public interface IPriceService
{
    /// <summary>Gives the price of one unit of a chain's currency at a time.</summary>
    /// <param name="case">The open case the rates are kept in.</param>
    /// <param name="request">What to price, when and in which currency.</param>
    /// <param name="mode">Whether the sources may be asked.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The price with its rates and their origins, or why there is none.</returns>
    /// <exception cref="ArgumentNullException">The case or the request is null.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
    Task<PriceLookup<Price>> GetPriceAsync(
        ICase @case, PriceRequest request, PriceLookupMode mode = PriceLookupMode.AsNeeded,
        CancellationToken cancellationToken = default);
}
