namespace Chainsaw.Modules.Pricing.Contracts;

/// <summary>
/// The settings for rates: the currency to count in, how fine the crypto rate is and the sources' addresses.
/// </summary>
public sealed record PricingOptions
{
    /// <summary>Gets the currency to count in when a request names none; Swedish kronor by default.</summary>
    public FiatCurrency Currency { get; init; } = FiatCurrency.Sek;

    /// <summary>Gets the resolution of the crypto rate when a request names none; one hour by default.</summary>
    public PriceResolution Resolution { get; init; } = PriceResolution.Hour;

    /// <summary>
    /// Gets the currency the crypto rate is taken in when counting in kronor, which no exchange in the sources quotes
    /// directly; the rate is then converted with the Riksbank's rate for this currency. US dollars by default, the
    /// market with the most trading.
    /// </summary>
    public FiatCurrency KronaVia { get; init; } = FiatCurrency.Usd;

    /// <summary>Gets the base address of Bitstamp's public API.</summary>
    public Uri BitstampBaseUri { get; init; } = new("https://www.bitstamp.net/api/v2/");

    /// <summary>Gets the base address of the Riksbank's open API for interest and exchange rates (SWEA).</summary>
    public Uri RiksbankBaseUri { get; init; } = new("https://api.riksbank.se/swea/v1/");
}
