namespace Chainsaw.Modules.Pricing.Contracts;

/// <summary>
/// The settings for rates: the currency to count in, how fine the crypto rate is and the sources' addresses.
/// </summary>
public sealed record PricingOptions
{
    /// <summary>
    /// Gets the currency to count in when a request names none, at the start (see <see cref="IPriceDefaults"/>);
    /// Swedish kronor by default.
    /// </summary>
    public FiatCurrency Currency { get; init; } = FiatCurrency.Sek;

    /// <summary>
    /// Gets the resolution of the crypto rate when a request names none, at the start (see
    /// <see cref="IPriceDefaults"/>); one hour by default.
    /// </summary>
    public PriceResolution Resolution { get; init; } = PriceResolution.Hour;

    /// <summary>
    /// Gets the currency the crypto rate is taken in when counting in kronor, which no exchange in the sources quotes
    /// directly; the rate is then converted with the Riksbank's rate for this currency. US dollars by default, the
    /// market with the most trading.
    /// </summary>
    public FiatCurrency KronaVia { get; init; } = FiatCurrency.Usd;

    /// <summary>
    /// Gets the shortest time between two requests to the Riksbank, which allows 5 anonymous requests a minute from one
    /// IP address; 12 seconds by default. It guards only that limit: the limit of 1 000 a day is counted separately,
    /// since Chainsaw started, and computers that share an exit share both limits without knowing it.
    /// </summary>
    public TimeSpan RiksbankInterval { get; init; } = TimeSpan.FromSeconds(12);

    /// <summary>Gets the base address of Bitstamp's public API.</summary>
    public Uri BitstampBaseUri { get; init; } = new("https://www.bitstamp.net/api/v2/");

    /// <summary>Gets the base address of the Riksbank's open API for interest and exchange rates (SWEA).</summary>
    public Uri RiksbankBaseUri { get; init; } = new("https://api.riksbank.se/swea/v1/");
}
