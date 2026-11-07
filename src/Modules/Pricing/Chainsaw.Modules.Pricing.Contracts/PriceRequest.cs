using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Pricing.Contracts;

/// <summary>A request for the price of a chain's currency at a time.</summary>
/// <param name="Chain">The chain whose currency is priced, for example Bitcoin.</param>
/// <param name="At">The time, for example a transaction's block time.</param>
public sealed record PriceRequest(Chain Chain, DateTimeOffset At)
{
    /// <summary>Gets the currency to count in, or <see langword="null"/> for the setting (SEK by default).</summary>
    public FiatCurrency? Currency { get; init; }

    /// <summary>
    /// Gets the crypto rate's resolution, or <see langword="null"/> for the setting (an hour by default).
    /// </summary>
    public PriceResolution? Resolution { get; init; }
}
