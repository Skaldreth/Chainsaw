using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Pricing.Contracts;

/// <summary>A request for the price of a chain's own coin, or of a token on it, at a time.</summary>
/// <param name="Chain">The chain whose coin or token is priced, for example Bitcoin.</param>
/// <param name="At">The time, for example a transaction's block time.</param>
public sealed record PriceRequest(Chain Chain, DateTimeOffset At)
{
    /// <summary>
    /// Gets the asset to price, or <see langword="null"/> for the chain's own coin (bitcoin, ether), as before tokens
    /// could be priced. A token is known by its chain and contract, never by its symbol; one that no source prices is
    /// answered with <see cref="PriceLookupStatus.Unsupported"/>, never with a price assumed from its name.
    /// </summary>
    public Asset? Asset { get; init; }

    /// <summary>Gets the currency to count in, or <see langword="null"/> for the setting (SEK by default).</summary>
    public FiatCurrency? Currency { get; init; }

    /// <summary>
    /// Gets the crypto rate's resolution, or <see langword="null"/> for the setting (an hour by default).
    /// </summary>
    public PriceResolution? Resolution { get; init; }
}
