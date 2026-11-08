#if DEBUG
using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Pricing.TestData;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.TestData;

/// <summary>
/// Answers the price service's requests for the test case from made-up rates, in this process and in the real sources'
/// form (Bitstamp's candles and the Riksbank's observations), so that the test case never sends a rate lookup out and
/// its rates never pretend to be real. The rates follow a gentle made-up curve over time, so that two times give two
/// rates. A request for anything else than the made-up sources' addresses is refused, never sent.
/// </summary>
/// <param name="clock">The clock, for the response's times.</param>
public sealed class TestRatesHttp(IClock clock) : IExitHttp
{
    /// <summary>The media type of a JSON answer.</summary>
    private const string Json = "application/json";

    /// <summary>The part of a crypto request's path before the market.</summary>
    private const string CandlePart = "ohlc/";

    /// <summary>The part of a fiat request's path before the series.</summary>
    private const string ObservationsPart = "Observations/";

    /// <summary>The length of a fiat currency's code, which ends a market's symbol.</summary>
    private const int QuoteLength = 3;

    /// <summary>The query parameter that names a candle's start, in seconds since 1970.</summary>
    private const string StartParameter = "start";

    /// <summary>The dollar's code, the quote whose rate is converted from euro.</summary>
    private const string DollarCode = "USD";

    /// <summary>How the Riksbank writes a day.</summary>
    private const string DayFormat = "yyyy-MM-dd";

    /// <summary>The made-up crypto curve's period: a day, so that the hours of a day differ.</summary>
    private const long CryptoPeriod = 86_400;

    /// <summary>The made-up fiat curve's period: a week.</summary>
    private const long FiatPeriod = 7 * 86_400;

    /// <summary>The made-up rate of each currency in euro at the start of the curve.</summary>
    private static readonly Dictionary<string, decimal> InEuro = new(StringComparer.Ordinal)
    {
        ["BTC"] = 92_000m,
        ["ETH"] = 3_100m,
        ["USDT"] = 0.92m,
        ["USDC"] = 0.92m,
    };

    /// <summary>The made-up price of a euro and a dollar in kronor at the start of the curve.</summary>
    private static readonly Dictionary<string, decimal> InKronor = new(StringComparer.Ordinal)
    {
        ["EUR"] = 11.05m,
        ["USD"] = 10.20m,
    };

    /// <summary>The made-up price of a dollar in euro, for a crypto rate asked in dollars.</summary>
    private const decimal EuroPerDollar = 0.92m;

    /// <summary>How many requests have been answered.</summary>
    private int _requests;

    /// <summary>Gets how many requests have been answered, so that a test can see that the rates were asked.</summary>
    public int Requests => Volatile.Read(ref _requests);

    /// <inheritdoc/>
    public Task<ExitResponse> SendAsync(ExitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var address = request.Uri.AbsoluteUri;
        var answer = address switch
        {
            _ when Under(address, TestDataPricing.CryptoAddress) is { } path => Candle(request.Uri, path),
            _ when Under(address, TestDataPricing.FiatAddress) is { } path => Observations(path),
            _ => throw new ExitException(ExitError.RequestFailed),
        };
        Interlocked.Increment(ref _requests);
        var now = clock.UtcNow;
        var (status, body) = answer ?? (404, "[]"u8.ToArray());
        return Task.FromResult(new ExitResponse(
            request.Uri, Guid.Empty, status, Json, body, now, now, ExitRoute.Windows) { IsLocal = true });
    }

    /// <summary>Gives the path of an address under a made-up source's address.</summary>
    /// <param name="address">The requested address.</param>
    /// <param name="root">The source's address.</param>
    /// <returns>The path, or <see langword="null"/> when the address is not under it.</returns>
    private static string? Under(string address, Uri root) =>
        address.StartsWith(root.AbsoluteUri, StringComparison.Ordinal) ? address[root.AbsoluteUri.Length..] : null;

    /// <summary>Answers <c>ohlc/:market/?step=…&amp;start=…</c> with one candle, as Bitstamp does.</summary>
    /// <param name="uri">The request.</param>
    /// <param name="path">The path after the source's address.</param>
    /// <returns>The answer, or <see langword="null"/> for a market the made-up rates do not have.</returns>
    private static (int, byte[])? Candle(Uri uri, string path)
    {
        if (!path.StartsWith(CandlePart, StringComparison.Ordinal))
        {
            return null;
        }

        var symbol = path[CandlePart.Length..].Split('/')[0].ToUpperInvariant();
        var query = uri.Query.TrimStart('?').Split('&')
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
        if (symbol.Length <= QuoteLength || !query.TryGetValue(StartParameter, out var startText)
            || !long.TryParse(startText, NumberStyles.None, CultureInfo.InvariantCulture, out var start))
        {
            return null;
        }

        var (code, quote) = (symbol[..^QuoteLength], symbol[^QuoteLength..]);
        if (!InEuro.TryGetValue(code, out var euro))
        {
            return null;
        }

        var close = Curve(euro, start, CryptoPeriod) / (quote == DollarCode ? EuroPerDollar : 1m);
        var rate = decimal.Round(close, 2);
        return Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteStartObject("data");
            writer.WriteString("pair", $"{code}/{quote}");
            writer.WriteStartArray("ohlc");
            writer.WriteStartObject();
            writer.WriteString("timestamp", start.ToString(CultureInfo.InvariantCulture));
            foreach (var name in (ReadOnlySpan<string>)["open", "high", "low", "close"])
            {
                writer.WriteString(name, rate.ToString(CultureInfo.InvariantCulture));
            }

            writer.WriteString("volume", "1.0");
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        });
    }

    /// <summary>
    /// Answers <c>Observations/:series/:from/:to</c> with a rate for each weekday in the range, as the Riksbank does,
    /// which publishes none on weekends.
    /// </summary>
    /// <param name="path">The path after the source's address.</param>
    /// <returns>The answer, or <see langword="null"/> for a series the made-up rates do not have.</returns>
    private static (int, byte[])? Observations(string path)
    {
        var parts = path.Split('/');
        if (parts.Length != 4 || parts[0] + "/" != ObservationsPart || parts[1].Length < 9
            || !InKronor.TryGetValue(parts[1][3..6], out var kronor)
            || !DateOnly.TryParseExact(parts[2], DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var from)
            || !DateOnly.TryParseExact(parts[3], DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var to))
        {
            return null;
        }

        return Write(writer =>
        {
            writer.WriteStartArray();
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    continue;
                }

                var seconds = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
                writer.WriteStartObject();
                writer.WriteString("date", day.ToString(DayFormat, CultureInfo.InvariantCulture));
                writer.WriteNumber("value", decimal.Round(Curve(kronor, seconds, FiatPeriod), 4));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
    }

    /// <summary>A made-up rate at a time: a start value that rises and falls a few per cent over a period.</summary>
    /// <param name="start">The value the curve swings around.</param>
    /// <param name="unixSeconds">The time.</param>
    /// <param name="period">The curve's period in seconds.</param>
    /// <returns>The rate.</returns>
    private static decimal Curve(decimal start, long unixSeconds, long period)
    {
        var phase = (double)(unixSeconds % period) / period;
        return start * (1m + (0.03m * (decimal)Math.Sin(2 * Math.PI * phase)));
    }

    /// <summary>Gives a JSON answer written by a writer.</summary>
    /// <param name="write">Writes the JSON.</param>
    /// <returns>The answer.</returns>
    private static (int, byte[]) Write(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            write(writer);
        }

        return (200, stream.ToArray());
    }
}
#endif
