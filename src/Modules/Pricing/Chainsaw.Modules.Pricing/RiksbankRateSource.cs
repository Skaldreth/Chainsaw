using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Pricing.Contracts;

namespace Chainsaw.Modules.Pricing;

/// <summary>
/// The Riksbank's open API for exchange rates (SWEA), free and open without a key: the series
/// <c>SEKUSDPMI</c> and <c>SEKEURPMI</c> give how many kronor one dollar or one euro costs, one value per banking day,
/// from <c>GET /swea/v1/Observations/{series}/{from}/{to}</c> as a JSON list of <c>{"date", "value"}</c>.
/// </summary>
/// <param name="baseUri">The API's base address, for example <c>https://api.riksbank.se/swea/v1/</c>.</param>
internal sealed class RiksbankRateSource(Uri baseUri) : IFiatRateSource
{
    /// <summary>The source's name in the case.</summary>
    public const string SourceName = "riksbank";

    /// <summary>The checked base address.</summary>
    private readonly Uri _baseUri = BaseAddress.Check(baseUri, nameof(baseUri));

    /// <inheritdoc/>
    public string Name => SourceName;

    /// <inheritdoc/>
    public FiatCurrency Base => FiatCurrency.Sek;

    /// <inheritdoc/>
    public TimeZoneInfo Calendar { get; } = Stockholm();

    /// <inheritdoc/>
    public bool Supports(FiatCurrency currency) => currency is FiatCurrency.Usd or FiatCurrency.Eur;

    /// <inheritdoc/>
    public string Series(FiatCurrency currency) => Supports(currency)
        ? $"SEK{currency.Code()}PMI"
        : throw new ArgumentOutOfRangeException(nameof(currency), currency, "The Riksbank source has no such series.");

    /// <inheritdoc/>
    public Uri RequestUri(FiatCurrency currency, DateOnly from, DateOnly to)
    {
        var first = from.ToString(RateResponseStore.DayFormat, CultureInfo.InvariantCulture);
        var last = to.ToString(RateResponseStore.DayFormat, CultureInfo.InvariantCulture);
        return new Uri(_baseUri, $"Observations/{Series(currency)}/{first}/{last}");
    }

    /// <inheritdoc/>
    public IReadOnlyList<FiatObservation> ReadObservations(
        ReadOnlySpan<byte> body, FiatCurrency currency, DateOnly from, DateOnly to)
    {
        // With no rate in the days the API answers 204 No Content, with an empty body.
        if (body.Trim(" \t\r\n"u8).IsEmpty)
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new RateFormatException("The response is not a list.");
            }

            var observations = new List<FiatObservation>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var date = DateOnly.ParseExact(
                    item.GetProperty("date").GetString() ?? string.Empty, RateResponseStore.DayFormat,
                    CultureInfo.InvariantCulture);
                if (date < from || date > to || (observations.Count > 0 && date <= observations[^1].Date))
                {
                    throw new RateFormatException("The days are outside the range or out of order.");
                }

                var value = item.GetProperty("value");
                if (value.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                var perUnit = value.GetDecimal();
                if (perUnit <= 0)
                {
                    throw new RateFormatException("A rate is not positive.");
                }

                observations.Add(new FiatObservation(date, perUnit));
            }

            return observations;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or FormatException or KeyNotFoundException)
        {
            throw new RateFormatException("The Riksbank's response could not be read.", exception);
        }
    }

    /// <summary>Finds Swedish time, by its IANA name or, on an older Windows, by its Windows name.</summary>
    /// <returns>The time zone.</returns>
    private static TimeZoneInfo Stockholm()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        }
    }
}
