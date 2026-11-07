namespace Chainsaw.Modules.Pricing.Contracts;

/// <summary>
/// The currency and resolution used when a request names none, chosen in the settings (page Kurser). They start as
/// <see cref="PricingOptions.Currency"/> and <see cref="PricingOptions.Resolution"/>, and a change applies to the next
/// request.
/// </summary>
public interface IPriceDefaults
{
    /// <summary>Gets the currency to count in when a request names none.</summary>
    FiatCurrency Currency { get; }

    /// <summary>Gets the crypto rate's resolution when a request names none.</summary>
    PriceResolution Resolution { get; }

    /// <summary>Chooses the currency and the resolution.</summary>
    /// <param name="currency">The currency to count in.</param>
    /// <param name="resolution">The crypto rate's resolution.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value is not known.</exception>
    void Choose(FiatCurrency currency, PriceResolution resolution);
}
