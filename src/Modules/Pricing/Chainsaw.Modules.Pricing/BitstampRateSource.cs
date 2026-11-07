using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Pricing;

/// <summary>
/// Bitstamp's public price history (<c>GET /api/v2/ohlc/{market}/</c>), open without an API key, with candles of a
/// minute, an hour or a day back to 2011 for BTC/USD and BTC/EUR. One request asks for one candle:
/// <c>step</c> is the length in seconds, <c>limit=1</c> and <c>start</c> the candle's start as Unix time.
/// </summary>
/// <param name="baseUri">The API's base address, for example <c>https://www.bitstamp.net/api/v2/</c>.</param>
internal sealed class BitstampRateSource(Uri baseUri) : ICryptoRateSource
{
    /// <summary>The source's name in the case.</summary>
    public const string SourceName = "bitstamp";

    /// <summary>The checked base address.</summary>
    private readonly Uri _baseUri = BaseAddress.Check(baseUri, nameof(baseUri));

    /// <inheritdoc/>
    public string Name => SourceName;

    /// <inheritdoc/>
    public string DisplayName => "Bitstamp";

    /// <inheritdoc/>
    public bool Supports(Chain chain, FiatCurrency quote) =>
        chain == Chain.Bitcoin && quote is FiatCurrency.Usd or FiatCurrency.Eur;

    /// <inheritdoc/>
    public Uri RequestUri(Chain chain, FiatCurrency quote, PriceResolution resolution, DateTimeOffset periodStart)
    {
        var market = Uri.EscapeDataString(Symbol(chain, quote));
        var step = resolution.Seconds().ToString(CultureInfo.InvariantCulture);
        var start = periodStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return new Uri(_baseUri, $"ohlc/{market}/?step={step}&limit=1&start={start}");
    }

    /// <inheritdoc/>
    public RateCandle? ReadCandle(
        ReadOnlySpan<byte> body, Chain chain, FiatCurrency quote, PriceResolution resolution,
        DateTimeOffset periodStart)
    {
        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            var data = Property(document.RootElement, "data", JsonValueKind.Object);
            var market = MarketOf(data);
            var expected = $"{chain.Code()}/{quote.Code()}";
            if (!string.Equals(market, expected, StringComparison.Ordinal))
            {
                throw new RateFormatException($"The market is {market}, not {expected}.");
            }

            var wanted = periodStart.ToUnixTimeSeconds();
            RateCandle? found = null;
            foreach (var item in Property(data, "ohlc", JsonValueKind.Array).EnumerateArray())
            {
                if (Number(Property(item, "timestamp")) != wanted)
                {
                    continue;
                }

                if (found is not null)
                {
                    throw new RateFormatException("The period is in the response twice.");
                }

                found = Candle(item, market);
            }

            return found;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or FormatException or OverflowException or KeyNotFoundException)
        {
            throw new RateFormatException("Bitstamp's response could not be read.", exception);
        }
    }

    /// <summary>Gives Bitstamp's symbol for a market.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="quote">The currency.</param>
    /// <returns>For example <c>btcusd</c>.</returns>
    private static string Symbol(Chain chain, FiatCurrency quote) =>
        (chain.Code() + quote.Code()).ToLowerInvariant();

    /// <summary>Reads the market's name; the API documents both <c>pair</c> and <c>market</c>.</summary>
    /// <param name="data">The response's <c>data</c>.</param>
    /// <returns>For example <c>BTC/USD</c>.</returns>
    private static string MarketOf(JsonElement data)
    {
        foreach (var name in (ReadOnlySpan<string>)["pair", "market"])
        {
            if (data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString()!;
            }
        }

        throw new RateFormatException("The response does not name its market.");
    }

    /// <summary>Reads one candle and checks that its prices go together.</summary>
    /// <param name="item">The candle.</param>
    /// <param name="market">The market.</param>
    /// <returns>The candle.</returns>
    private static RateCandle Candle(JsonElement item, string market)
    {
        var open = Number(Property(item, "open"));
        var high = Number(Property(item, "high"));
        var low = Number(Property(item, "low"));
        var close = Number(Property(item, "close"));
        var volume = Number(Property(item, "volume"));
        if (low <= 0 || volume < 0 || low > high || open < low || open > high || close < low || close > high)
        {
            throw new RateFormatException("The candle's prices do not go together.");
        }

        return new RateCandle(market, open, high, low, close, volume);
    }

    /// <summary>Gets a property, of a kind when one is given.</summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The property's name.</param>
    /// <param name="kind">The kind it must have, or <see langword="null"/> for any.</param>
    /// <returns>The property.</returns>
    private static JsonElement Property(JsonElement element, string name, JsonValueKind? kind = null)
    {
        var value = element.GetProperty(name);
        if (kind is { } wanted && value.ValueKind != wanted)
        {
            throw new RateFormatException($"{name} is not {wanted}.");
        }

        return value;
    }

    /// <summary>Reads a number that Bitstamp writes as a string (or a plain JSON number), exactly.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The number.</returns>
    private static decimal Number(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => decimal.Parse(
            value.GetString()!, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
        JsonValueKind.Number => value.GetDecimal(),
        _ => throw new RateFormatException("A number is missing."),
    };
}
