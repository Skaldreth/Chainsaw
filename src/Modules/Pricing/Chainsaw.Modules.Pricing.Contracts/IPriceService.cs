using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Pricing.Contracts;

/// <summary>
/// The price of a chain's coin or a token at a time, in the chosen currency. Every rate is a fact from a named
/// source, sealed in the case with its raw response the first time it is fetched and never fetched again
/// (principle 1); the conversion between them is a calculation that shows its steps (principle 2). Requests go only
/// through the exit
/// and carry only the market and the time, never anything about the case (principle 4). Without an exit the case
/// answers, or the answer honestly says <see cref="PriceLookupStatus.Offline"/> (principle 9).
/// </summary>
/// <remarks>
/// The methods never throw for a failed lookup; the answer says what went wrong. Only a case that cannot be read
/// throws.
/// </remarks>
public interface IPriceService
{
    /// <summary>
    /// Gives the price of one unit of a chain's own coin, or of a token on it (<see cref="PriceRequest.Asset"/>), at
    /// a time. A token no source prices is answered with <see cref="PriceLookupStatus.Unsupported"/>; a stablecoin is
    /// never assumed to be worth its peg.
    /// </summary>
    /// <param name="case">The open case the rates are kept in.</param>
    /// <param name="request">What to price, when and in which currency.</param>
    /// <param name="mode">Whether the sources may be asked.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The price with its rates and their origins, or why there is none.</returns>
    /// <exception cref="ArgumentNullException">The case or the request is null.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.Closed"/>, or <see cref="CaseError.Corrupt"/> when a stored response does not match its
    /// SHA-256.
    /// </exception>
    Task<PriceLookup<Price>> GetPriceAsync(
        ICase @case, PriceRequest request, PriceLookupMode mode = PriceLookupMode.AsNeeded,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts what the case keeps from the sources: the sealed answers that hold a crypto rate, and those that hold a
    /// central bank's rates, so that the settings can say honestly how many there are. Nothing is sent.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <returns>The counts.</returns>
    /// <exception cref="ArgumentNullException">The case is null.</exception>
    /// <exception cref="CaseException"><see cref="CaseError.Closed"/>.</exception>
    RateSummary Summarize(ICase @case);
}

/// <summary>How many sealed answers from the rate sources a case keeps.</summary>
/// <param name="CryptoRates">The answers that hold a crypto rate for a period (Bitstamp).</param>
/// <param name="FiatResponses">The answers that hold a central bank's rates over some days (the Riksbank).</param>
public sealed record RateSummary(int CryptoRates, int FiatResponses)
{
    /// <summary>Gets every answer kept.</summary>
    public int Total => CryptoRates + FiatResponses;
}
