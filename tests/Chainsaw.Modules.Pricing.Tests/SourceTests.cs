using System.Text;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Pricing.Tests;

public sealed class BitstampSourceTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeSeconds(Fixtures.HourStart);

    private readonly BitstampRateSource _source = new(new Uri(Fixtures.Bitstamp));

    [Fact]
    public void Asks_for_one_candle_by_market_length_and_start()
    {
        var uri = _source.RequestUri(Chain.Bitcoin, FiatCurrency.Usd, PriceResolution.Hour, Start);

        Assert.Equal(Fixtures.BitstampUsdHour, uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(PriceResolution.Minute, "step=60")]
    [InlineData(PriceResolution.Day, "step=86400")]
    public void Asks_with_the_length_of_the_resolution(PriceResolution resolution, string step)
    {
        var uri = _source.RequestUri(Chain.Bitcoin, FiatCurrency.Eur, resolution, Start);

        Assert.Contains("ohlc/btceur/", uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains(step + "&", uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Chain.Bitcoin, FiatCurrency.Usd, true)]
    [InlineData(Chain.Bitcoin, FiatCurrency.Eur, true)]
    [InlineData(Chain.Bitcoin, FiatCurrency.Sek, false)]
    [InlineData(Chain.Ethereum, FiatCurrency.Usd, true)]
    [InlineData(Chain.Ethereum, FiatCurrency.Sek, false)]
    [InlineData(Chain.Tron, FiatCurrency.Usd, true)]
    [InlineData(Chain.Tron, FiatCurrency.Eur, true)]
    [InlineData(Chain.Tron, FiatCurrency.Sek, false)]
    [InlineData(Chain.Litecoin, FiatCurrency.Eur, true)]
    [InlineData(Chain.Litecoin, FiatCurrency.Sek, false)]
    [InlineData(Chain.Bsc, FiatCurrency.Eur, true)]
    [InlineData(Chain.Bsc, FiatCurrency.Sek, false)]
    public void Has_bitcoin_and_ether_in_dollars_and_euro_only(Chain chain, FiatCurrency quote, bool supported)
    {
        Assert.Equal(supported, _source.Supports(chain, quote));
    }

    [Theory]
    [InlineData(Chain.Litecoin, "ohlc/ltceur/")]
    [InlineData(Chain.Bsc, "ohlc/bnbeur/")]
    public void Asks_litecoin_and_bnb_in_their_own_markets(Chain chain, string market)
    {
        var uri = _source.RequestUri(chain, FiatCurrency.Eur, PriceResolution.Day, Start);

        Assert.Contains(market, uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void Reads_the_candle_that_starts_the_period_and_ignores_the_others()
    {
        var candle = Read(Fixtures.Bytes("bitstamp-btcusd-hour.json"));

        Assert.NotNull(candle);
        Assert.Equal("BTC/USD", candle.Market);
        Assert.Equal(30050.00m, candle.Open);
        Assert.Equal(30200.00m, candle.High);
        Assert.Equal(30000.00m, candle.Low);
        Assert.Equal(30123.45m, candle.Close);
        Assert.Equal(20.25m, candle.Volume);
    }

    [Fact]
    public void Gives_nothing_when_the_period_is_missing()
    {
        Assert.Null(Read(Encoding.UTF8.GetBytes(Body(Candle(Fixtures.HourStart - 3_600)))));
        Assert.Null(Read(Encoding.UTF8.GetBytes(Body())));
    }

    [Fact]
    public void Reads_the_market_from_market_and_plain_numbers()
    {
        var body = $$$"""
            {"data": {"market": "BTC/USD", "ohlc": [{"timestamp": {{{Fixtures.HourStart}}}, "open": 1.5, "high": 2,
            "low": 1, "close": 1.75, "volume": 0}]}}
            """;

        var candle = Read(Encoding.UTF8.GetBytes(body));

        Assert.Equal(1.75m, candle!.Close);
        Assert.Equal(0m, candle.Volume);
    }

    [Fact]
    public void Refuses_another_market()
    {
        var body = Body(Candle(Fixtures.HourStart)).Replace("BTC/USD", "BTC/EUR", StringComparison.Ordinal);

        Assert.Throws<RateFormatException>(() => Read(Encoding.UTF8.GetBytes(body)));
    }

    [Theory]
    [InlineData("10", "12", "9", "13", "1")]
    [InlineData("10", "12", "11", "11", "1")]
    [InlineData("10", "9", "8", "8.5", "1")]
    [InlineData("0", "0", "0", "0", "1")]
    [InlineData("10", "12", "9", "11", "-1")]
    [InlineData("1e3", "12", "9", "11", "1")]
    public void Refuses_prices_that_do_not_go_together(
        string open, string high, string low, string close, string volume)
    {
        var item = $$$"""
            {"timestamp": "{{{Fixtures.HourStart}}}", "open": "{{{open}}}", "high": "{{{high}}}", "low": "{{{low}}}",
            "close": "{{{close}}}", "volume": "{{{volume}}}"}
            """;

        Assert.Throws<RateFormatException>(() => Read(Encoding.UTF8.GetBytes(Body(item))));
    }

    [Fact]
    public void Refuses_the_period_twice()
    {
        var body = Body(Candle(Fixtures.HourStart), Candle(Fixtures.HourStart));

        Assert.Throws<RateFormatException>(() => Read(Encoding.UTF8.GetBytes(body)));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"code": "validation-error"}""")]
    [InlineData("""{"data": {"pair": "BTC/USD"}}""")]
    [InlineData("""{"data": {"ohlc": []}}""")]
    [InlineData("""{"data": {"pair": "BTC/USD", "ohlc": [{"timestamp": "x"}]}}""")]
    public void Refuses_what_cannot_be_read(string body)
    {
        Assert.Throws<RateFormatException>(() => Read(Encoding.UTF8.GetBytes(body)));
    }

    [Theory]
    [InlineData("http://bitstamp.test/api/")]
    [InlineData("https://user@bitstamp.test/api/")]
    [InlineData("https://bitstamp.test/api/?x=1")]
    public void Refuses_a_base_address_that_is_not_https_of_its_own(string address)
    {
        Assert.Throws<ArgumentException>(() => new BitstampRateSource(new Uri(address)));
    }

    [Fact]
    public void Adds_the_slash_to_a_base_address()
    {
        var source = new BitstampRateSource(new Uri("https://bitstamp.test/api/v2"));

        var uri = source.RequestUri(Chain.Bitcoin, FiatCurrency.Usd, PriceResolution.Hour, Start);

        Assert.StartsWith("https://bitstamp.test/api/v2/ohlc/", uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void Reads_ether_from_its_own_market_only()
    {
        var body = Encoding.UTF8.GetBytes(Body(Candle(Fixtures.HourStart)).Replace("BTC/USD", "ETH/USD",
            StringComparison.Ordinal));

        var candle = _source.ReadCandle(body, Chain.Ethereum, FiatCurrency.Usd, PriceResolution.Hour, Start);

        Assert.Equal("ETH/USD", candle!.Market);
        Assert.Throws<RateFormatException>(() => Read(body));
        Assert.Contains("ohlc/ethusd/", _source.RequestUri(Chain.Ethereum, FiatCurrency.Usd, PriceResolution.Hour,
            Start).AbsoluteUri, StringComparison.Ordinal);
    }

    private RateCandle? Read(byte[] body) =>
        _source.ReadCandle(body, Chain.Bitcoin, FiatCurrency.Usd, PriceResolution.Hour, Start);

    private static string Candle(long timestamp) =>
        $$$"""
        {"timestamp": "{{{timestamp}}}", "open": "10.00", "high": "12.00", "low": "9.00", "close": "11.00",
        "volume": "1.00000000"}
        """;

    private static string Body(params string[] candles) =>
        $$$"""{"data": {"pair": "BTC/USD", "ohlc": [{{{string.Join(", ", candles)}}}]}}""";
}

public sealed class RiksbankSourceTests
{
    private static readonly DateOnly From = new(2023, 10, 31);

    private static readonly DateOnly To = new(2023, 12, 15);

    private readonly RiksbankRateSource _source = new(new Uri(Fixtures.Riksbank));

    [Fact]
    public void Asks_for_the_series_over_the_days()
    {
        var uri = _source.RequestUri(FiatCurrency.Usd, From, To);

        Assert.Equal(Fixtures.RiksbankUsdNovember, uri.AbsoluteUri);
    }

    [Fact]
    public void Names_the_series_for_dollars_and_euro_in_kronor()
    {
        Assert.Equal("SEKUSDPMI", _source.Series(FiatCurrency.Usd));
        Assert.Equal("SEKEURPMI", _source.Series(FiatCurrency.Eur));
        Assert.Equal(FiatCurrency.Sek, _source.Base);
        Assert.False(_source.Supports(FiatCurrency.Sek));
        Assert.Throws<ArgumentOutOfRangeException>(() => _source.Series(FiatCurrency.Sek));
    }

    [Fact]
    public void Follows_the_swedish_calendar()
    {
        var evening = new DateTimeOffset(2023, 11, 14, 23, 30, 0, TimeSpan.Zero);

        var local = TimeZoneInfo.ConvertTime(evening, _source.Calendar);

        Assert.Equal(new DateTime(2023, 11, 15, 0, 30, 0), local.DateTime);
    }

    [Fact]
    public void Reads_the_banking_days_oldest_first()
    {
        var observations = Read(Fixtures.Bytes("riksbank-sekusdpmi.json"));

        Assert.Equal(34, observations.Count);
        Assert.Equal(new FiatObservation(new DateOnly(2023, 10, 31), 10.5m), observations[0]);
        Assert.Equal(new FiatObservation(new DateOnly(2023, 11, 14), 10.513m), observations[10]);
        Assert.DoesNotContain(observations, observation => observation.Date.DayOfWeek == DayOfWeek.Saturday);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n")]
    [InlineData("[]")]
    public void Reads_no_content_as_no_rates(string body)
    {
        Assert.Empty(Read(Encoding.UTF8.GetBytes(body)));
    }

    [Fact]
    public void Skips_a_day_without_a_value()
    {
        var observations = Read(Encoding.UTF8.GetBytes(
            """[{"date":"2023-11-01","value":null},{"date":"2023-11-02","value":10.25}]"""));

        Assert.Equal([new FiatObservation(new DateOnly(2023, 11, 2), 10.25m)], observations);
    }

    [Theory]
    [InlineData("""[{"date":"2023-10-30","value":10.1}]""")]
    [InlineData("""[{"date":"2023-12-16","value":10.1}]""")]
    [InlineData("""[{"date":"2023-11-02","value":10.1},{"date":"2023-11-01","value":10.1}]""")]
    [InlineData("""[{"date":"2023-11-02","value":10.1},{"date":"2023-11-02","value":10.1}]""")]
    [InlineData("""[{"date":"2023-11-02","value":0}]""")]
    [InlineData("""[{"date":"2023-11-02","value":-1}]""")]
    [InlineData("""[{"date":"2023-11-02","value":"10,1"}]""")]
    [InlineData("""[{"date":"2/11/2023","value":10.1}]""")]
    [InlineData("""[{"value":10.1}]""")]
    [InlineData("""{"date":"2023-11-02","value":10.1}""")]
    [InlineData("not json")]
    public void Refuses_what_does_not_add_up(string body)
    {
        Assert.Throws<RateFormatException>(() => Read(Encoding.UTF8.GetBytes(body)));
    }

    private IReadOnlyList<FiatObservation> Read(byte[] body) =>
        _source.ReadObservations(body, FiatCurrency.Usd, From, To);
}
