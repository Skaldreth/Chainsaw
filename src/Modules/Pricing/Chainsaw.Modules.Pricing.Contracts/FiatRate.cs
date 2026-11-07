namespace Chainsaw.Modules.Pricing.Contracts;

/// <summary>
/// A central bank's rate for a currency on one banking day: a fact from a source (principle 2).
/// </summary>
/// <param name="Currency">The foreign currency, for example US dollars.</param>
/// <param name="Base">The currency it is given in, for the Riksbank Swedish kronor.</param>
/// <param name="PerUnit">How much of <paramref name="Base"/> one unit of <paramref name="Currency"/> costs.</param>
/// <param name="Date">The banking day the rate is for.</param>
/// <param name="AskedDate">
/// The day that was asked about, in Swedish time. It is later than <paramref name="Date"/> when that day had no rate
/// (a weekend or holiday): the latest earlier rate is used then.
/// </param>
/// <param name="Series">The source's name for the series, for example <c>SEKUSDPMI</c>.</param>
/// <param name="Origin">The sealed response it comes from.</param>
public sealed record FiatRate(
    FiatCurrency Currency,
    FiatCurrency Base,
    decimal PerUnit,
    DateOnly Date,
    DateOnly AskedDate,
    string Series,
    RateOrigin Origin);
