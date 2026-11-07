using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Pricing.Tests;

public sealed class PriceTests
{
    private static readonly RateOrigin Origin =
        new(1, "bitstamp", new Uri("https://bitstamp.test/"), 200, default, default,
            new OperatorProfile("Testa Testsson", "Utredare", "Enheten"), "00");

    private static readonly CryptoRate Usd = new(
        Chain.Bitcoin, FiatCurrency.Usd, PriceResolution.Hour,
        DateTimeOffset.FromUnixTimeSeconds(Fixtures.HourStart),
        DateTimeOffset.FromUnixTimeSeconds(Fixtures.HourStart + 3_600),
        "BTC/USD", 30050m, 30200m, 30000m, 30123.45m, 20.25m, Origin) { SourceName = "Bitstamp" };

    private static readonly FiatRate Krona = new(
        FiatCurrency.Usd, FiatCurrency.Sek, 10.513m, new DateOnly(2023, 11, 14), new DateOnly(2023, 11, 14),
        "SEKUSDPMI", Origin with { Sequence = 2, Source = "riksbank" }) { SourceName = "Riksbanken" };

    [Fact]
    public void Multiplies_the_rates_exactly()
    {
        var price = new Price(Fixtures.At, FiatCurrency.Sek, Usd, Krona);

        Assert.Equal(316687.82985m, price.UnitPrice);
        Assert.Equal([Usd.Origin, Krona.Origin], price.Origins);
    }

    [Fact]
    public void Shows_each_rate_with_its_source_and_the_calculation()
    {
        var steps = new Price(Fixtures.At, FiatCurrency.Sek, Usd, Krona).Steps;

        Assert.Equal(
            [
                "1 BTC = 30\u00a0123,45 USD: Bitstamp, BTC/USD, sista priset för 2023-11-14 22:00–23:00 (UTC).",
                "1 USD = 10,513 SEK: Riksbanken, serien SEKUSDPMI, kursen för 2023-11-14.",
                "1 BTC = 30\u00a0123,45 × 10,513 = 316\u00a0687,82985 SEK.",
            ],
            steps);
    }

    [Fact]
    public void Says_when_the_rate_comes_from_an_earlier_day()
    {
        var friday = Krona with { AskedDate = new DateOnly(2023, 11, 18), Date = new DateOnly(2023, 11, 17) };

        var steps = new Price(Fixtures.At, FiatCurrency.Sek, Usd, friday).Steps;

        Assert.Contains("kursen för 2023-11-17 (2023-11-18 saknar kurs, senaste dagen före används).", steps[1],
            StringComparison.Ordinal);
    }

    [Fact]
    public void Says_when_no_trade_was_made_in_the_period()
    {
        var steps = new Price(Fixtures.At, FiatCurrency.Usd, Usd with { Volume = 0 }, null).Steps;

        Assert.EndsWith("(UTC). Ingen handel under perioden.", steps[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Names_a_source_without_a_name_for_the_user_by_its_stable_name()
    {
        var steps = new Price(Fixtures.At, FiatCurrency.Usd, Usd with { SourceName = null }, null).Steps;

        Assert.Contains("USD: bitstamp, BTC/USD", steps[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Has_one_step_when_the_rate_is_already_in_the_currency()
    {
        var price = new Price(Fixtures.At, FiatCurrency.Usd, Usd, null);

        Assert.Equal(30123.45m, price.UnitPrice);
        Assert.Single(price.Steps);
        Assert.Equal([Usd.Origin], price.Origins);
    }

    [Fact]
    public void Writes_a_day_period_with_both_dates()
    {
        var day = Usd with
        {
            Resolution = PriceResolution.Day,
            PeriodStart = new DateTimeOffset(2023, 11, 14, 0, 0, 0, TimeSpan.Zero),
            PeriodEnd = new DateTimeOffset(2023, 11, 15, 0, 0, 0, TimeSpan.Zero),
        };

        var steps = new Price(Fixtures.At, FiatCurrency.Usd, day, null).Steps;

        Assert.Contains("2023-11-14 00:00–2023-11-15 00:00", steps[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_rates_that_do_not_lead_to_the_currency()
    {
        Assert.Throws<ArgumentException>(() => new Price(Fixtures.At, FiatCurrency.Sek, Usd, null));
        Assert.Throws<ArgumentException>(() => new Price(Fixtures.At, FiatCurrency.Eur, Usd, null));
        Assert.Throws<ArgumentException>(() => new Price(
            Fixtures.At, FiatCurrency.Sek, Usd, Krona with { Currency = FiatCurrency.Eur }));
        Assert.Throws<ArgumentException>(() => new Price(Fixtures.At, FiatCurrency.Usd, Usd, Krona));
    }

    [Fact]
    public void Values_an_amount_exactly_and_rounds_only_what_is_shown()
    {
        var price = new Price(Fixtures.At, FiatCurrency.Sek, Usd, Krona);

        var valuation = price.ValueOf(new Satoshi(51_200_000));

        Assert.Equal(0.512m, valuation.Amount);
        Assert.Equal(162144.1688832m, valuation.Value);
        Assert.Equal(162144.17m, valuation.Rounded);
        Assert.Equal(4, valuation.Steps.Count);
        Assert.Equal(
            "0,512 BTC × 316\u00a0687,82985 = 162\u00a0144,1688832 SEK, avrundat 162\u00a0144,17 SEK.",
            valuation.Steps[^1]);
    }

    [Fact]
    public void Values_ether_from_wei_exactly()
    {
        var price = new Price(Fixtures.At, FiatCurrency.Usd, Usd with { Chain = Chain.Ethereum }, null);

        var valuation = price.ValueOf(new AssetAmount(Asset.Ether, 5_820_000_000_000_000_000));

        Assert.Equal(5.82m, valuation.Amount);
        Assert.Equal(5.82m * price.UnitPrice, valuation.Value);
        Assert.StartsWith("5,82 ETH × ", valuation.Steps[^1], StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => price.ValueOf(new Satoshi(1)));
        Assert.Throws<InvalidOperationException>(() => price.ValueOf(new AssetAmount(
            Asset.Token(Chain.Ethereum, "0xdac17f958d2ee523a2206206994597c13d831ec7", "USDT", 6), 1)));
    }

    [Fact]
    public void Values_nothing_as_nothing()
    {
        var valuation = new Price(Fixtures.At, FiatCurrency.Usd, Usd, null).ValueOf(Satoshi.Zero);

        Assert.Equal(0m, valuation.Rounded);
    }

    [Fact]
    public void Values_only_satoshi_with_a_bitcoin_price()
    {
        var price = new Price(Fixtures.At, FiatCurrency.Usd, Usd with { Chain = Chain.Ethereum }, null);

        Assert.Throws<InvalidOperationException>(() => price.ValueOf(new Satoshi(1)));
    }

    [Theory]
    [InlineData(PriceResolution.Minute, "2023-11-14T22:30:00Z")]
    [InlineData(PriceResolution.Hour, "2023-11-14T22:00:00Z")]
    [InlineData(PriceResolution.Day, "2023-11-14T00:00:00Z")]
    public void Finds_the_period_that_holds_a_time(PriceResolution resolution, string start)
    {
        var at = Fixtures.At.AddSeconds(42);

        Assert.Equal(DateTimeOffset.Parse(start, System.Globalization.CultureInfo.InvariantCulture),
            resolution.PeriodStart(at));
    }

    [Fact]
    public void Finds_the_period_for_a_time_given_with_an_offset_and_before_1970()
    {
        var stockholm = new DateTimeOffset(2023, 11, 14, 23, 30, 0, TimeSpan.FromHours(1));
        var old = new DateTimeOffset(1969, 12, 31, 23, 30, 0, TimeSpan.Zero);

        Assert.Equal(Fixtures.HourStart, PriceResolution.Hour.PeriodStart(stockholm).ToUnixTimeSeconds());
        Assert.Equal(-3_600, PriceResolution.Hour.PeriodStart(old).ToUnixTimeSeconds());
    }

    [Fact]
    public void Gives_the_codes()
    {
        Assert.Equal("SEK", FiatCurrency.Sek.Code());
        Assert.Equal("USD", FiatCurrency.Usd.Code());
        Assert.Equal("EUR", FiatCurrency.Eur.Code());
        Assert.Equal("BTC", Chain.Bitcoin.Code());
        Assert.Equal("ETH", Chain.Ethereum.Code());
        Assert.Equal("TRX", Chain.Tron.Code());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((FiatCurrency)99).Code());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((PriceResolution)99).Seconds());
    }
}
