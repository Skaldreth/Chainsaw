namespace Chainsaw.Modules.Pricing.Tests;

/// <summary>
/// The synthetic responses in <c>Responses/</c>: the sources' formats with made-up prices and rates, so that no test
/// depends on the network or on real market data.
/// </summary>
internal static class Fixtures
{
    public const string Bitstamp = "https://bitstamp.test/api/v2/";

    public const string Riksbank = "https://riksbank.test/swea/v1/";

    /// <summary>2023-11-14 22:00 UTC, the start of the hour the tests price in.</summary>
    public const long HourStart = 1_699_999_200;

    /// <summary>A time within that hour: 2023-11-14 22:30 UTC, which is 23:30 in Stockholm the same day.</summary>
    public static readonly DateTimeOffset At = DateTimeOffset.FromUnixTimeSeconds(HourStart + 1_800);

    public static readonly string BitstampUsdHour = $"{Bitstamp}ohlc/btcusd/?step=3600&limit=1&start={HourStart}";

    public static readonly string BitstampEurHour = $"{Bitstamp}ohlc/btceur/?step=3600&limit=1&start={HourStart}";

    public static readonly string BitstampUsdtHour = $"{Bitstamp}ohlc/usdtusd/?step=3600&limit=1&start={HourStart}";

    public static readonly string BitstampUsdcHour = $"{Bitstamp}ohlc/usdcusd/?step=3600&limit=1&start={HourStart}";

    public static readonly string BitstampEthHour = $"{Bitstamp}ohlc/ethusd/?step=3600&limit=1&start={HourStart}";

    /// <summary>The Riksbank request for 2023-11-14: 14 days before and 31 days after.</summary>
    public static readonly string RiksbankUsdNovember = $"{Riksbank}Observations/SEKUSDPMI/2023-10-31/2023-12-15";

    public static byte[] Bytes(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Responses", name));
}
