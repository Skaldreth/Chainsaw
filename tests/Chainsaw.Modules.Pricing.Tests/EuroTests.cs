using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Pricing.Contracts;

namespace Chainsaw.Modules.Pricing.Tests;

/// <summary>
/// The krona follows the Riksbank (the user's decision 2026-11-08, sketch KU1a): Bitstamp has no SEK, so the rate in
/// euro is converted with the Riksbank's day rate for EUR/SEK, and both answers are sealed with their way out.
/// </summary>
public sealed class EuroTests : IDisposable
{
    /// <summary>The Riksbank request for 2023-11-14 in euro: 14 days before and 31 days after.</summary>
    private static readonly string RiksbankEurNovember =
        $"{Fixtures.Riksbank}Observations/SEKEURPMI/2023-10-31/2023-12-15";

    /// <summary>A case with the module's own defaults, but the test sources' addresses.</summary>
    private readonly Harness _harness = new(new PricingOptions
    {
        BitstampBaseUri = new Uri(Fixtures.Bitstamp),
        RiksbankBaseUri = new Uri(Fixtures.Riksbank),
        RiksbankInterval = TimeSpan.Zero,
    });

    /// <inheritdoc/>
    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Prices_in_kronor_by_default_through_euro_and_the_riksbank()
    {
        AnswerEuro();

        var lookup = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.Fetched, lookup.Status);
        var price = lookup.Value!;
        Assert.Equal(FiatCurrency.Sek, price.Currency);
        Assert.Equal(FiatCurrency.Eur, price.CryptoRate.Quote);
        Assert.Equal("BTC/EUR", price.CryptoRate.Market);
        Assert.Equal(28010.50m, price.CryptoRate.Close);
        Assert.Equal("SEKEURPMI", price.FiatRate!.Series);
        Assert.Equal(11.451m, price.FiatRate.PerUnit);
        Assert.Equal(28010.50m * 11.451m, price.UnitPrice);
        Assert.Equal(
            [Fixtures.BitstampEurHour, RiksbankEurNovember],
            _harness.Http.Sent.Select(request => request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task Sends_only_the_market_and_the_days()
    {
        AnswerEuro();

        await _harness.PriceAsync(Fixtures.At);

        Assert.All(_harness.Http.Sent, request => Assert.DoesNotContain(Harness.CaseNumber, request.Uri.AbsoluteUri));
        Assert.All(_harness.Http.Sent, request => Assert.Equal("https", request.Uri.Scheme));
    }

    [Fact]
    public async Task Each_rate_carries_its_hash_and_way_out_also_from_the_case()
    {
        AnswerEuro();
        _harness.Http.Route = ExitRoute.Windows;
        var fetched = (await _harness.PriceAsync(Fixtures.At)).Value!;

        var read = await _harness.PriceAsync(Fixtures.At, mode: PriceLookupMode.CaseOnly);

        Assert.Equal(PriceLookupStatus.FromCase, read.Status);
        var price = read.Value!;
        Assert.Equal("windows", fetched.CryptoRate.Origin.Route);
        Assert.Equal("windows", price.CryptoRate.Origin.Route);
        Assert.Equal("windows", price.FiatRate!.Origin.Route);
        Assert.Equal(fetched.CryptoRate.Origin.Sha256, price.CryptoRate.Origin.Sha256);
        Assert.Equal(64, price.FiatRate.Origin.Sha256.Length);
        Assert.Equal(2, _harness.Http.Sent.Count);
    }

    [Fact]
    public async Task Is_offline_and_sends_nothing_without_an_exit()
    {
        _harness.Http.Connected = false;

        var lookup = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.Offline, lookup.Status);
        Assert.Null(lookup.Value);
        Assert.Empty(_harness.Http.Sent);
    }

    [Fact]
    public void An_empty_case_keeps_no_rates()
    {
        Assert.Equal(new RateSummary(0, 0), _harness.Prices.Summarize(_harness.Case));
    }

    [Fact]
    public async Task Counts_the_sealed_rates_in_the_case()
    {
        AnswerEuro();
        await _harness.PriceAsync(Fixtures.At);
        await _harness.PriceAsync(Fixtures.At);

        var summary = _harness.Prices.Summarize(_harness.Case);

        Assert.Equal(new RateSummary(1, 1), summary);
        Assert.Equal(2, summary.Total);
    }

    [Fact]
    public async Task A_saturday_takes_the_riksbank_s_friday_rate_for_the_euro()
    {
        // Saturday 2023-11-18 12:30 UTC: the Riksbank has no rate that day, so Friday's is used and the steps say so.
        var saturday = new DateTimeOffset(2023, 11, 18, 12, 30, 0, TimeSpan.Zero);
        var start = PriceResolution.Hour.PeriodStart(saturday).ToUnixTimeSeconds();
        _harness.Http.AnswerText(
            $"{Fixtures.Bitstamp}ohlc/btceur/?step=3600&limit=1&start={start}",
            $$$"""
            {"data": {"pair": "BTC/EUR", "ohlc": [{"timestamp": "{{{start}}}", "open": "34000", "high": "34000",
            "low": "34000", "close": "34000", "volume": "1"}]}}
            """);
        _harness.Http.AnswerText(
            $"{Fixtures.Riksbank}Observations/SEKEURPMI/2023-11-04/2023-12-19",
            """
            [{"date":"2023-11-16","value":11.5},{"date":"2023-11-17","value":11.52},
            {"date":"2023-11-20","value":11.6}]
            """);

        var price = (await _harness.PriceAsync(saturday)).Value!;

        Assert.Equal(new DateOnly(2023, 11, 17), price.FiatRate!.Date);
        Assert.Equal(new DateOnly(2023, 11, 18), price.FiatRate.AskedDate);
        Assert.Equal(11.52m, price.FiatRate.PerUnit);
        Assert.Contains(price.Steps, step => step.Contains("2023-11-18 saknar kurs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_source_that_asks_to_wait_is_not_asked_again_until_the_cooldown_has_passed()
    {
        _harness.Http.AnswerText(Fixtures.BitstampEurHour, "Too many requests", 429);

        var refused = await _harness.PriceAsync(Fixtures.At);
        var again = await _harness.PriceAsync(Fixtures.At);
        AnswerEuro();
        _harness.Clock.UtcNow += new PricingOptions().FailureCooldown;
        var later = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.Failed, refused.Status);
        Assert.True(refused.IsThrottled);
        Assert.Equal(PriceLookupStatus.Failed, again.Status);
        Assert.True(again.IsThrottled);
        Assert.Empty(again.Origins);
        Assert.Equal(PriceLookupStatus.Fetched, later.Status);
        Assert.Equal(3, _harness.Http.Sent.Count);
    }

    [Fact]
    public async Task A_server_error_is_not_a_request_to_wait_but_is_not_asked_again_at_once()
    {
        _harness.Http.AnswerText(Fixtures.BitstampEurHour, "oops", 503);

        var failed = await _harness.PriceAsync(Fixtures.At);
        var again = await _harness.PriceAsync(Fixtures.At);

        Assert.False(failed.IsThrottled);
        Assert.Equal(PriceLookupStatus.Failed, again.Status);
        Assert.Single(_harness.Http.Sent);
    }

    /// <summary>Answers Bitstamp in euro and the Riksbank's EUR/SEK from the fixtures.</summary>
    private void AnswerEuro()
    {
        _harness.Http.Answer(Fixtures.BitstampEurHour, "bitstamp-btceur-hour.json");
        _harness.Http.Answer(RiksbankEurNovember, "riksbank-sekeurpmi.json");
    }
}
