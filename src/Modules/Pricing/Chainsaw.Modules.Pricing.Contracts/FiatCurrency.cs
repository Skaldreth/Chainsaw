namespace Chainsaw.Modules.Pricing.Contracts;

/// <summary>
/// The currencies Chainsaw can count in. Swedish kronor are the default (see <see cref="PricingOptions"/>).
/// </summary>
public enum FiatCurrency
{
    /// <summary>Swedish kronor, SEK.</summary>
    Sek,

    /// <summary>US dollars, USD.</summary>
    Usd,

    /// <summary>Euro, EUR.</summary>
    Eur,
}

/// <summary>The currencies' codes.</summary>
public static class FiatCurrencies
{
    /// <summary>Gives a currency's ISO 4217 code.</summary>
    /// <param name="currency">The currency.</param>
    /// <returns>For example <c>SEK</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The currency is unknown.</exception>
    public static string Code(this FiatCurrency currency) => currency switch
    {
        FiatCurrency.Sek => "SEK",
        FiatCurrency.Usd => "USD",
        FiatCurrency.Eur => "EUR",
        _ => throw new ArgumentOutOfRangeException(nameof(currency), currency, "Unknown currency."),
    };
}
