using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Pricing;

/// <summary>
/// Bitstamp's public price history (<c>GET /api/v2/ohlc/{market}/</c>), open without an API key, with candles of a
/// minute, an hour or a day back to 2011 for BTC/USD and BTC/EUR, and for ETH, TRX, LTC, BNB, USDT and USDC (on
/// Ethereum, USDT on TRON, both on BSC and both on Solana) in dollars and euro (markets read 2026-11-09: <c>ltceur</c>,
/// <c>bnbeur</c> and <c>soleur</c> among them; <c>soleur</c> answers from 2022 on, nothing for May 2022 and a candle
/// for September 2022). One request asks for one candle: <c>step</c> is the length in seconds,
/// <c>limit=1</c> and <c>start</c> the candle's start as Unix time.
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

    /// <summary>
    /// The tokens Bitstamp has markets for, recognised by chain and contract (never by symbol), with Bitstamp's code
    /// for each: USDT and USDC on Ethereum and USDT on TRON, the stablecoins it lists against dollars and euro
    /// (<c>usdtusd</c>, <c>usdteur</c>, <c>usdcusd</c>, <c>usdceur</c>). Bitstamp's USDT is Tether's token whatever
    /// chain it is issued on, so the same market prices it on Ethereum and on TRON. On BSC, USDT and USDC are
    /// Binance's bridged tokens, each backed one to one by the token it bridges; Bitstamp has no market of their own,
    /// so they are priced by the token they stand for, which the rate's source says (principle 2: a rate is a
    /// judgement with its source). On Solana, USDT and USDC are Tether's and Circle's own tokens (their mints'
    /// authorities are the issuers'), priced by the same markets.
    /// </summary>
    private static readonly Dictionary<Asset, string> Tokens = new()
    {
        [KnownTokens.EthereumUsdt] = "USDT",
        [KnownTokens.EthereumUsdc] = "USDC",
        [KnownTokens.TronUsdt] = "USDT",
        [KnownTokens.BscUsdt] = "USDT",
        [KnownTokens.BscUsdc] = "USDC",
        [KnownTokens.SolanaUsdt] = "USDT",
        [KnownTokens.SolanaUsdc] = "USDC",
    };

    /// <inheritdoc/>
    public bool Supports(Chain chain, FiatCurrency quote) =>
        chain is Chain.Bitcoin or Chain.Ethereum or Chain.Tron or Chain.Litecoin or Chain.Bsc or Chain.Solana
        && HasQuote(quote);


    /// <inheritdoc/>
    public bool Supports(Asset asset, FiatCurrency quote)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return asset.Kind == AssetKind.Native
            ? Supports(asset.Chain, quote)
            : Tokens.ContainsKey(asset) && HasQuote(quote);
    }

    /// <inheritdoc/>
    public Uri RequestUri(Chain chain, FiatCurrency quote, PriceResolution resolution, DateTimeOffset periodStart) =>
        Ask(chain.Code(), quote, resolution, periodStart);

    /// <inheritdoc/>
    public Uri RequestUri(Asset asset, FiatCurrency quote, PriceResolution resolution, DateTimeOffset periodStart) =>
        Ask(CodeOf(asset), quote, resolution, periodStart);

    /// <inheritdoc/>
    public RateCandle? ReadCandle(
        ReadOnlySpan<byte> body, Chain chain, FiatCurrency quote, PriceResolution resolution,
        DateTimeOffset periodStart) =>
        Read(body, chain.Code(), quote, periodStart);

    /// <inheritdoc/>
    public RateCandle? ReadCandle(
        ReadOnlySpan<byte> body, Asset asset, FiatCurrency quote, PriceResolution resolution,
        DateTimeOffset periodStart) =>
        Read(body, CodeOf(asset), quote, periodStart);

    /// <inheritdoc/>
    /// <remarks>
    /// Bitstamp answers <c>ohlc</c> with the periods from <c>start</c> on; a market that began later answers with its
    /// first periods, which then all start after the period asked about. Not checked against Bitstamp's documentation
    /// (BACKLOG); without such periods the answer is never taken as final.
    /// </remarks>
    public bool HistoryStartsAfter(
        ReadOnlySpan<byte> body, Asset asset, FiatCurrency quote, PriceResolution resolution,
        DateTimeOffset periodStart)
    {
        ArgumentNullException.ThrowIfNull(asset);
        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            var data = Property(document.RootElement, "data", JsonValueKind.Object);
            if (!string.Equals(MarketOf(data), $"{CodeOf(asset)}/{quote.Code()}", StringComparison.Ordinal))
            {
                return false;
            }

            var starts = Property(data, "ohlc", JsonValueKind.Array).EnumerateArray()
                .Select(item => Number(Property(item, "timestamp")))
                .ToList();
            var end = periodStart.ToUnixTimeSeconds() + resolution.Seconds();
            return starts.Count > 0 && starts.Min() >= end;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or FormatException or OverflowException or KeyNotFoundException or RateFormatException
            or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Gets whether Bitstamp quotes its markets in a currency: dollars and euro.</summary>
    /// <param name="quote">The currency.</param>
    /// <returns><see langword="true"/> for USD and EUR.</returns>
    private static bool HasQuote(FiatCurrency quote) => quote is FiatCurrency.Usd or FiatCurrency.Eur;

    /// <summary>Gives Bitstamp's code for an asset: the chain's code for its coin, its own code for a token.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>For example <c>BTC</c> or <c>USDT</c>.</returns>
    /// <exception cref="ArgumentNullException">The asset is null.</exception>
    /// <exception cref="NotSupportedException">Bitstamp has no market for the token.</exception>
    private static string CodeOf(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Kind == AssetKind.Native)
        {
            return asset.Chain.Code();
        }

        return Tokens.TryGetValue(asset, out var code)
            ? code
            : throw new NotSupportedException($"Bitstamp has no market for {asset}.");
    }

    /// <summary>Gives the address that asks for one candle of a market.</summary>
    /// <param name="code">Bitstamp's code for the asset, for example <c>BTC</c>.</param>
    /// <param name="quote">The currency.</param>
    /// <param name="resolution">The length of the period.</param>
    /// <param name="periodStart">The period's start.</param>
    /// <returns>For example <c>…/ohlc/btcusd/?step=3600&amp;limit=1&amp;start=1699999200</c>.</returns>
    private Uri Ask(string code, FiatCurrency quote, PriceResolution resolution, DateTimeOffset periodStart)
    {
        var market = Uri.EscapeDataString(Symbol(code, quote));
        var step = resolution.Seconds().ToString(CultureInfo.InvariantCulture);
        var start = periodStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return new Uri(_baseUri, $"ohlc/{market}/?step={step}&limit=1&start={start}");
    }

    /// <summary>Reads the candle that starts a period, after checking that the response is for the market.</summary>
    /// <param name="body">The response's raw bytes.</param>
    /// <param name="code">Bitstamp's code for the asset, for example <c>USDT</c>.</param>
    /// <param name="quote">The currency.</param>
    /// <param name="periodStart">The period.</param>
    /// <returns>The candle, or <see langword="null"/> when the response does not have it.</returns>
    /// <exception cref="RateFormatException">The response cannot be read or does not add up.</exception>
    private static RateCandle? Read(
        ReadOnlySpan<byte> body, string code, FiatCurrency quote, DateTimeOffset periodStart)
    {
        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            var data = Property(document.RootElement, "data", JsonValueKind.Object);
            var market = MarketOf(data);
            var expected = $"{code}/{quote.Code()}";
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
    /// <param name="code">Bitstamp's code for the asset, for example <c>BTC</c>.</param>
    /// <param name="quote">The currency.</param>
    /// <returns>For example <c>btcusd</c> or <c>usdtusd</c>.</returns>
    private static string Symbol(string code, FiatCurrency quote) =>
        (code + quote.Code()).ToLowerInvariant();

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
