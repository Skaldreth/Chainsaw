using System.Security.Cryptography;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Pricing.Tests;

public sealed class ServiceTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Prices_in_kronor_through_dollars_and_the_riksbank_when_chosen()
    {
        _harness.AnswerAll();

        var lookup = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.Fetched, lookup.Status);
        var price = lookup.Value!;
        Assert.Equal(FiatCurrency.Sek, price.Currency);
        Assert.Equal(30123.45m, price.CryptoRate.Close);
        Assert.Equal(PriceResolution.Hour, price.CryptoRate.Resolution);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(Fixtures.HourStart), price.CryptoRate.PeriodStart);
        Assert.Equal(10.513m, price.FiatRate!.PerUnit);
        Assert.Equal(new DateOnly(2023, 11, 14), price.FiatRate.Date);
        Assert.Equal(316687.82985m, price.UnitPrice);
        Assert.Equal(price.Origins, lookup.Origins);
        Assert.Equal(
            [Fixtures.BitstampUsdHour, Fixtures.RiksbankUsdNovember],
            _harness.Http.Sent.Select(request => request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task Seals_the_way_out_with_every_response()
    {
        _harness.AnswerAll();
        _harness.Http.Route = ExitRoute.Windows;

        await _harness.PriceAsync(Fixtures.At);

        Assert.All(_harness.ExitColumns(), row => Assert.Equal(((string?)null, "windows"), row));
        Assert.Equal(2, _harness.ExitColumns().Count);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task The_currency_chosen_in_the_settings_applies_to_the_next_request()
    {
        _harness.AnswerAll();
        _harness.Defaults.Choose(FiatCurrency.Eur, PriceResolution.Hour);

        var lookup = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(FiatCurrency.Eur, lookup.Value!.Currency);
        Assert.Equal(FiatCurrency.Eur, _harness.Defaults.Currency);
        Assert.Equal([Fixtures.BitstampEurHour], _harness.Http.Sent.Select(request => request.Uri.AbsoluteUri));
    }

    [Fact]
    public void An_unknown_currency_or_resolution_is_refused()
    {
        var defaults = _harness.Defaults;
        Assert.Throws<ArgumentOutOfRangeException>(() => defaults.Choose((FiatCurrency)9, PriceResolution.Hour));
        Assert.Throws<ArgumentOutOfRangeException>(() => defaults.Choose(FiatCurrency.Sek, (PriceResolution)9));
        Assert.Equal(FiatCurrency.Sek, _harness.Defaults.Currency);
    }

    [Fact]
    public async Task Seals_every_response_with_its_source_hash_and_who_fetched_it()
    {
        _harness.AnswerAll();

        var lookup = await _harness.PriceAsync(Fixtures.At);

        var rows = _harness.RawRows();
        Assert.Equal(["bitstamp", "riksbank"], rows.Select(row => row.Source));
        Assert.Equal(["crypto.candle", "fiat.observations"], rows.Select(row => row.Kind));
        Assert.Equal(["BTC-USD/3600/1699999200", "SEKUSDPMI/2023-10-31/2023-12-15"], rows.Select(row => row.Subject));
        foreach (var row in rows)
        {
            Assert.Equal(SHA256.HashData(row.Body), row.Sha256);
            Assert.Equal(200, row.Status);
            Assert.Equal(FakeExitHttp.ExitId.ToString("D"), row.ExitId);
            Assert.Contains("Testa Testsson", row.FetchedBy, StringComparison.Ordinal);
            Assert.DoesNotContain(Harness.CaseNumber, row.Url, StringComparison.Ordinal);
        }

        Assert.Equal(Fixtures.Bytes("bitstamp-btcusd-hour.json"), rows[0].Body);
        Assert.Equal(Convert.ToHexStringLower(rows[1].Sha256), lookup.Origins[1].Sha256);
        Assert.Equal(rows.Select(row => row.Sequence), lookup.Origins.Select(origin => origin.Sequence));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task Never_fetches_a_rate_again()
    {
        _harness.AnswerAll();
        var first = await _harness.PriceAsync(Fixtures.At);

        var second = await _harness.PriceAsync(Fixtures.At.AddMinutes(10));

        Assert.Equal(PriceLookupStatus.FromCase, second.Status);
        Assert.Equal(first.Value!.UnitPrice, second.Value!.UnitPrice);
        Assert.Equal(first.Origins, second.Origins);
        Assert.Equal(2, _harness.Http.Sent.Count);
        Assert.Equal(2, _harness.RawRows().Count);
    }

    [Fact]
    public async Task Uses_one_riksbank_response_for_the_days_it_covers()
    {
        _harness.AnswerAll();
        var later = Fixtures.At.AddDays(6);
        _harness.AnswerCandle(later, "3");
        await _harness.PriceAsync(Fixtures.At);

        var lookup = await _harness.PriceAsync(later);

        Assert.Equal(PriceLookupStatus.Fetched, lookup.Status);
        Assert.Equal(new DateOnly(2023, 11, 20), lookup.Value!.FiatRate!.Date);
        Assert.Equal(lookup.Origins[1], (await _harness.PriceAsync(Fixtures.At)).Origins[1]);
        Assert.Equal(3, _harness.Http.Sent.Count);
        Assert.DoesNotContain("riksbank", _harness.Http.Sent[^1].Uri.Host, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Takes_the_latest_banking_day_for_a_weekend_from_the_case()
    {
        _harness.AnswerAll();
        await _harness.PriceAsync(Fixtures.At);
        var saturday = new DateTimeOffset(2023, 11, 18, 12, 30, 0, TimeSpan.Zero);
        _harness.AnswerCandle(saturday, "2");

        var lookup = await _harness.PriceAsync(saturday);

        Assert.Equal(PriceLookupStatus.Fetched, lookup.Status);
        Assert.Equal(new DateOnly(2023, 11, 17), lookup.Value!.FiatRate!.Date);
        Assert.Equal(new DateOnly(2023, 11, 18), lookup.Value.FiatRate.AskedDate);
        Assert.Equal(10.5169m, lookup.Value.FiatRate.PerUnit);
        Assert.Equal(21.0338m, lookup.Value.UnitPrice);
        Assert.Equal(3, _harness.Http.Sent.Count);
    }

    [Fact]
    public async Task Counts_the_day_in_swedish_time()
    {
        _harness.AnswerAll();
        var evening = new DateTimeOffset(2023, 11, 13, 23, 30, 0, TimeSpan.Zero);
        _harness.AnswerCandle(evening, "1");
        _harness.Http.Answer(
            $"{Fixtures.Riksbank}Observations/SEKUSDPMI/2023-10-31/2023-12-15", "riksbank-sekusdpmi.json");

        var lookup = await _harness.PriceAsync(evening);

        Assert.Equal(new DateOnly(2023, 11, 14), lookup.Value!.FiatRate!.AskedDate);
    }

    [Fact]
    public async Task Prices_in_dollars_or_euro_without_the_riksbank()
    {
        _harness.AnswerAll();

        var dollars = await _harness.PriceAsync(Fixtures.At, FiatCurrency.Usd);
        var euro = await _harness.PriceAsync(Fixtures.At, FiatCurrency.Eur);

        Assert.Equal(30123.45m, dollars.Value!.UnitPrice);
        Assert.Null(dollars.Value.FiatRate);
        Assert.Equal(28010.50m, euro.Value!.UnitPrice);
        Assert.Equal("BTC/EUR", euro.Value.CryptoRate.Market);
        Assert.Equal(
            [Fixtures.BitstampUsdHour, Fixtures.BitstampEurHour],
            _harness.Http.Sent.Select(request => request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task Goes_through_euro_when_so_set()
    {
        using var harness = new Harness(new PricingOptions
        {
            BitstampBaseUri = new Uri(Fixtures.Bitstamp),
            RiksbankBaseUri = new Uri(Fixtures.Riksbank),
            KronaVia = FiatCurrency.Eur,
            Resolution = PriceResolution.Day,
        });
        var dayStart = Fixtures.HourStart - (22 * 3_600);
        harness.Http.AnswerText(
            $"{Fixtures.Bitstamp}ohlc/btceur/?step=86400&limit=1&start={dayStart}",
            $$$"""
            {"data": {"pair": "BTC/EUR", "ohlc": [{"timestamp": "{{{dayStart}}}", "open": "3", "high": "4",
            "low": "2", "close": "3", "volume": "5"}]}}
            """);
        harness.Http.AnswerText(
            $"{Fixtures.Riksbank}Observations/SEKEURPMI/2023-10-31/2023-12-15",
            """[{"date":"2023-11-14","value":11.5}]""");

        var lookup = await harness.PriceAsync(Fixtures.At);

        Assert.Equal(34.5m, lookup.Value!.UnitPrice);
        Assert.Equal("SEKEURPMI", lookup.Value.FiatRate!.Series);
        Assert.Equal(PriceResolution.Day, lookup.Value.CryptoRate.Resolution);
    }

    [Fact]
    public async Task Is_offline_without_an_exit_and_sends_nothing()
    {
        _harness.AnswerAll();
        _harness.Http.Connected = false;

        var lookup = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.Offline, lookup.Status);
        Assert.Null(lookup.Value);
        Assert.StartsWith("Offline", lookup.Problem, StringComparison.Ordinal);
        Assert.Empty(_harness.Http.Sent);
        Assert.Empty(_harness.RawRows());
    }

    [Fact]
    public async Task Answers_from_the_case_offline()
    {
        _harness.AnswerAll();
        await _harness.PriceAsync(Fixtures.At);
        _harness.Http.Connected = false;

        var lookup = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.FromCase, lookup.Status);
        Assert.Equal(316687.82985m, lookup.Value!.UnitPrice);
    }

    [Fact]
    public async Task Is_offline_for_the_riksbank_rate_when_only_bitstamp_is_in_the_case()
    {
        _harness.AnswerAll();
        await _harness.PriceAsync(Fixtures.At, FiatCurrency.Usd);
        _harness.Http.Connected = false;

        var lookup = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.Offline, lookup.Status);
        Assert.Single(lookup.Origins);
    }

    [Fact]
    public async Task Reads_only_the_case_when_asked_to()
    {
        _harness.AnswerAll();

        var missing = await _harness.PriceAsync(Fixtures.At, mode: PriceLookupMode.CaseOnly);
        await _harness.PriceAsync(Fixtures.At);
        var kept = await _harness.PriceAsync(Fixtures.At, mode: PriceLookupMode.CaseOnly);

        Assert.Equal(PriceLookupStatus.NotInCase, missing.Status);
        Assert.Equal(PriceLookupStatus.FromCase, kept.Status);
        Assert.Equal(2, _harness.Http.Sent.Count);
    }

    [Fact]
    public async Task Refuses_a_time_in_the_future_without_sending()
    {
        var lookup = await _harness.PriceAsync(_harness.Clock.UtcNow.AddSeconds(1));

        Assert.Equal(PriceLookupStatus.Unsupported, lookup.Status);
        Assert.Contains("framtiden", lookup.Problem, StringComparison.Ordinal);
        Assert.Empty(_harness.Http.Sent);
    }

    [Theory]
    [InlineData(Chain.Tron)]
    [InlineData((Chain)99)]
    public async Task Refuses_a_chain_without_a_source(Chain chain)
    {
        var lookup = await _harness.Prices.GetPriceAsync(_harness.Case, new PriceRequest(chain, Fixtures.At));

        Assert.Equal(PriceLookupStatus.Unsupported, lookup.Status);
        Assert.Empty(_harness.Http.Sent);
    }

    [Fact]
    public async Task Refuses_kronor_via_kronor()
    {
        using var harness = new Harness(new PricingOptions
        {
            BitstampBaseUri = new Uri(Fixtures.Bitstamp),
            RiksbankBaseUri = new Uri(Fixtures.Riksbank),
            KronaVia = FiatCurrency.Sek,
        });

        var lookup = await harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.Unsupported, lookup.Status);
        Assert.Empty(harness.Http.Sent);
    }

    [Fact]
    public async Task Waits_for_a_period_that_has_not_ended_without_sending()
    {
        var lookup = await _harness.PriceAsync(_harness.Clock.UtcNow.AddMinutes(-1));

        Assert.Equal(PriceLookupStatus.NotAvailable, lookup.Status);
        Assert.Contains("inte avslutad", lookup.Problem, StringComparison.Ordinal);
        Assert.Empty(_harness.Http.Sent);
    }

    [Fact]
    public async Task Never_keeps_a_period_the_source_has_not_ended_as_final()
    {
        // The computer's clock says the hour ended five minutes ago, but the response arrived before that.
        _harness.AnswerAll();
        _harness.Clock.UtcNow = DateTimeOffset.FromUnixTimeSeconds(Fixtures.HourStart + 3_600 + 300);
        _harness.Http.Lag = TimeSpan.FromSeconds(-1);

        var lookup = await _harness.PriceAsync(Fixtures.At, FiatCurrency.Usd);

        Assert.Equal(PriceLookupStatus.NotAvailable, lookup.Status);
        Assert.Single(_harness.Http.Sent);
        Assert.Equal(0, _harness.Count("pricing_lookup"));
    }

    [Fact]
    public async Task Waits_five_minutes_after_the_period_before_asking()
    {
        _harness.Clock.UtcNow = DateTimeOffset.FromUnixTimeSeconds(Fixtures.HourStart + 3_600 + 299);

        var lookup = await _harness.PriceAsync(Fixtures.At, FiatCurrency.Usd);

        Assert.Equal(PriceLookupStatus.NotAvailable, lookup.Status);
        Assert.Empty(_harness.Http.Sent);
    }

    [Fact]
    public async Task A_period_the_source_has_no_rate_for_is_not_asked_again_until_the_cooldown_has_passed()
    {
        _harness.Http.AnswerText(Fixtures.BitstampUsdHour, """{"data": {"pair": "BTC/USD", "ohlc": []}}""");

        var first = await _harness.PriceAsync(Fixtures.At);
        var again = await _harness.PriceAsync(Fixtures.At);
        _harness.Clock.UtcNow += new PricingOptions().FailureCooldown;
        var later = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.NotAvailable, first.Status);
        Assert.Equal(PriceLookupStatus.NotAvailable, again.Status);
        Assert.False(again.IsThrottled);
        Assert.Empty(again.Origins);
        Assert.Equal(PriceLookupStatus.NotAvailable, later.Status);
        Assert.Equal(2, _harness.Http.Sent.Count);
        Assert.Equal(2, _harness.RawRows().Count);
    }

    [Fact]
    public async Task Says_when_the_source_has_no_rate_for_the_period()
    {
        _harness.Http.AnswerText(Fixtures.BitstampUsdHour, """{"data": {"pair": "BTC/USD", "ohlc": []}}""");

        var lookup = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.NotAvailable, lookup.Status);
        Assert.Single(lookup.Origins);
        Assert.Single(_harness.RawRows());
        Assert.Equal(0, _harness.Count("pricing_lookup"));
    }

    [Fact]
    public async Task Seals_and_reports_an_error_status_and_asks_again_later()
    {
        _harness.Http.AnswerText(Fixtures.BitstampUsdHour, "oops", 500);

        var failed = await _harness.PriceAsync(Fixtures.At);
        _harness.AnswerAll();
        var soon = await _harness.PriceAsync(Fixtures.At);
        _harness.Clock.UtcNow += new PricingOptions().FailureCooldown;
        var later = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.Failed, failed.Status);
        Assert.Contains("500", failed.Problem, StringComparison.Ordinal);
        Assert.Equal(500, failed.Origins.Single().StatusCode);
        Assert.Equal(PriceLookupStatus.Failed, soon.Status);
        Assert.Empty(soon.Origins);
        Assert.Equal(PriceLookupStatus.Fetched, later.Status);
        Assert.Equal(3, _harness.RawRows().Count);
    }

    [Fact]
    public async Task Says_when_the_source_limits_requests()
    {
        _harness.Http.Answer(Fixtures.BitstampUsdHour, "bitstamp-btcusd-hour.json");
        _harness.Http.AnswerText(Fixtures.RiksbankUsdNovember, "Rate limit", 429);

        var lookup = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.Failed, lookup.Status);
        Assert.Contains("fler uppslag", lookup.Problem, StringComparison.Ordinal);
        Assert.Equal(2, lookup.Origins.Count);
        Assert.Equal(0, _harness.Count("pricing_fiat_ranges"));
    }

    [Fact]
    public async Task Seals_a_response_that_cannot_be_read_but_never_uses_it()
    {
        _harness.Http.AnswerText(Fixtures.BitstampUsdHour, "{not json");

        var lookup = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.Failed, lookup.Status);
        Assert.Contains("gick inte att tolka", lookup.Problem, StringComparison.Ordinal);
        Assert.Single(_harness.RawRows());
        Assert.Equal(0, _harness.Count("pricing_lookup"));
    }

    [Fact]
    public async Task Says_when_the_riksbank_has_no_rate_in_the_days()
    {
        _harness.Http.Answer(Fixtures.BitstampUsdHour, "bitstamp-btcusd-hour.json");
        _harness.Http.AnswerText(Fixtures.RiksbankUsdNovember, string.Empty, 204);

        var lookup = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.NotAvailable, lookup.Status);
        Assert.Contains("SEKUSDPMI", lookup.Problem, StringComparison.Ordinal);
        Assert.Equal(0, _harness.Count("pricing_fiat_ranges"));
    }

    [Fact]
    public async Task Waits_for_todays_riksbank_rate_and_keeps_nothing_as_final()
    {
        // Friday 2026-11-06 at 13:00 in Stockholm: Bitstamp's hour has ended, the Riksbank has not published.
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 11, 6, 12, 0, 1, TimeSpan.Zero);
        var at = new DateTimeOffset(2026, 11, 6, 10, 30, 0, TimeSpan.Zero);
        _harness.AnswerCandle(at, "1");
        var riksbank = $"{Fixtures.Riksbank}Observations/SEKUSDPMI/2026-10-23/2026-11-06";
        _harness.Http.AnswerText(riksbank, """[{"date":"2026-11-05","value":9.5}]""");

        var waiting = await _harness.PriceAsync(at);
        _harness.Clock.UtcNow += TimeSpan.FromMinutes(16);
        _harness.Http.AnswerText(
            riksbank, """[{"date":"2026-11-05","value":9.5},{"date":"2026-11-06","value":9.6}]""");
        var published = await _harness.PriceAsync(at);
        var again = await _harness.PriceAsync(at);

        Assert.Equal(PriceLookupStatus.NotAvailable, waiting.Status);
        Assert.Contains("inte publicerad", waiting.Problem, StringComparison.Ordinal);
        Assert.Equal(1, _harness.Count("pricing_fiat_ranges"));
        Assert.Equal(9.6m, published.Value!.FiatRate!.PerUnit);
        Assert.Equal(PriceLookupStatus.FromCase, again.Status);
        Assert.Equal(
            [riksbank, riksbank],
            _harness.Http.Sent.Select(request => request.Uri.AbsoluteUri).Where(url => url.Contains("riksbank")));
    }

    [Fact]
    public async Task Reaches_only_to_yesterday_for_a_recent_day()
    {
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 11, 6, 12, 0, 1, TimeSpan.Zero);
        var at = new DateTimeOffset(2026, 11, 3, 10, 30, 0, TimeSpan.Zero);
        _harness.AnswerCandle(at, "1");
        _harness.Http.AnswerText(
            $"{Fixtures.Riksbank}Observations/SEKUSDPMI/2026-10-20/2026-11-05",
            """[{"date":"2026-11-03","value":9.5}]""");

        var lookup = await _harness.PriceAsync(at);

        Assert.Equal(9.5m, lookup.Value!.UnitPrice);
        Assert.Equal(1, _harness.Count("pricing_fiat_ranges"));
    }

    [Fact]
    public async Task Fetches_nothing_without_an_operator()
    {
        _harness.AnswerAll();
        _harness.Profiles.Profile = null;

        var lookup = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.Failed, lookup.Status);
        Assert.Contains("operatörsprofil", lookup.Problem, StringComparison.Ordinal);
        Assert.Empty(_harness.Http.Sent);
    }

    [Fact]
    public async Task Fetches_nothing_with_a_locked_vault()
    {
        _harness.AnswerAll();
        _harness.Profiles.Locked = true;

        var lookup = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.Failed, lookup.Status);
        Assert.Contains("låst", lookup.Problem, StringComparison.Ordinal);
        Assert.Empty(_harness.Http.Sent);
    }

    [Fact]
    public async Task Reports_a_failed_exit_and_seals_nothing()
    {
        _harness.Http.Failure = ExitError.Timeout;

        var lookup = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.Failed, lookup.Status);
        Assert.Equal(new ExitException(ExitError.Timeout).Message, lookup.Problem);
        Assert.Empty(_harness.RawRows());
    }

    [Fact]
    public async Task Asks_only_for_json_with_nothing_about_the_case()
    {
        _harness.AnswerAll();

        await _harness.PriceAsync(Fixtures.At);

        Assert.All(_harness.Http.Sent, request =>
        {
            Assert.Equal(ExitMethod.Get, request.Method);
            Assert.Equal("application/json", request.Accept);
            Assert.True(request.Body.IsEmpty);
            Assert.Equal(Uri.UriSchemeHttps, request.Uri.Scheme);
        });
    }

    [Fact]
    public async Task Refuses_a_missing_case_or_request()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _harness.Prices.GetPriceAsync(null!, new PriceRequest(Chain.Bitcoin, Fixtures.At)));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _harness.Prices.GetPriceAsync(_harness.Case, null!));
    }
}

public sealed class StoredRateTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Ignores_an_index_that_points_at_another_response()
    {
        _harness.AnswerAll();
        await _harness.PriceAsync(Fixtures.At);
        Execute("UPDATE pricing_lookup SET sequence = 2");

        var lookup = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.Fetched, lookup.Status);
        Assert.Equal(3, _harness.Http.Sent.Count);
    }

    [Fact]
    public async Task Ignores_a_fiat_index_whose_days_the_sealed_response_does_not_name()
    {
        _harness.AnswerAll();
        await _harness.PriceAsync(Fixtures.At);
        Execute("UPDATE pricing_fiat_ranges SET date_from = '2023-11-01'");

        var lookup = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.Fetched, lookup.Status);
        Assert.Equal(Fixtures.RiksbankUsdNovember, _harness.Http.Sent[^1].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Seals_an_unreadable_riksbank_response_as_failed()
    {
        _harness.Http.Answer(Fixtures.BitstampUsdHour, "bitstamp-btcusd-hour.json");
        _harness.Http.AnswerText(Fixtures.RiksbankUsdNovember, """{"error": true}""");

        var lookup = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.Failed, lookup.Status);
        Assert.Equal(2, _harness.RawRows().Count);
        Assert.Equal(0, _harness.Count("pricing_fiat_ranges"));
    }

    [Fact]
    public async Task A_failed_riksbank_request_is_not_sent_again_until_the_cooldown_has_passed()
    {
        _harness.Http.Answer(Fixtures.BitstampUsdHour, "bitstamp-btcusd-hour.json");
        _harness.Http.AnswerText(Fixtures.RiksbankUsdNovember, "oops", 503);

        var failed = await _harness.PriceAsync(Fixtures.At);
        var again = await _harness.PriceAsync(Fixtures.At);
        _harness.Clock.UtcNow += new PricingOptions().FailureCooldown;
        var later = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.Failed, failed.Status);
        Assert.Equal(PriceLookupStatus.Failed, again.Status);
        Assert.False(again.IsThrottled);
        Assert.Equal(PriceLookupStatus.Failed, later.Status);
        Assert.Equal(2, _harness.Http.Sent.Count(request => request.Uri.Host == "riksbank.test"));
        Assert.Equal(3, _harness.RawRows().Count);
    }

    [Fact]
    public async Task Keeps_the_case_intact_after_many_lookups()
    {
        _harness.AnswerAll();
        _harness.Http.AnswerText(Fixtures.BitstampEurHour, "oops", 503);

        await _harness.PriceAsync(Fixtures.At);
        await _harness.PriceAsync(Fixtures.At, FiatCurrency.Eur);
        await _harness.PriceAsync(Fixtures.At, FiatCurrency.Usd);

        Assert.True(_harness.Verify().IsIntact);
        Assert.Equal(3, _harness.RawRows().Count);
    }

    private void Execute(string sql) => _harness.Execute(sql);
}

public sealed class ReviewRegressionTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Never_trusts_an_index_for_a_candle_that_was_not_final_when_it_arrived()
    {
        _harness.AnswerAll();
        _harness.Clock.UtcNow = DateTimeOffset.FromUnixTimeSeconds(Fixtures.HourStart + 3_600 + 300);
        _harness.Http.Lag = TimeSpan.FromSeconds(-1);
        await _harness.PriceAsync(Fixtures.At, FiatCurrency.Usd);
        _harness.Execute(
            """
            INSERT INTO pricing_lookup (source, kind, subject, sequence)
            SELECT source, kind, subject, sequence FROM pricing_raw_responses
            """);
        _harness.Http.Lag = TimeSpan.FromSeconds(1);

        var lookup = await _harness.PriceAsync(Fixtures.At, FiatCurrency.Usd);

        Assert.Equal(PriceLookupStatus.Fetched, lookup.Status);
        Assert.Equal(2, _harness.Http.Sent.Count);
    }

    [Fact]
    public async Task Never_trusts_an_index_for_a_day_that_had_no_rate_yet()
    {
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 11, 6, 12, 0, 1, TimeSpan.Zero);
        var at = new DateTimeOffset(2026, 11, 6, 10, 30, 0, TimeSpan.Zero);
        _harness.AnswerCandle(at, "1");
        var riksbank = $"{Fixtures.Riksbank}Observations/SEKUSDPMI/2026-10-23/2026-11-06";
        _harness.Http.AnswerText(riksbank, """[{"date":"2026-11-05","value":9.5}]""");
        await _harness.PriceAsync(at);
        _harness.Execute(
            """
            INSERT INTO pricing_fiat_ranges (source, series, date_from, date_to, asked_to, sequence)
            SELECT source, 'SEKUSDPMI', '2026-10-23', '2026-11-06', '2026-11-06', sequence
            FROM pricing_raw_responses WHERE source = 'riksbank'
            """);
        _harness.Clock.UtcNow += TimeSpan.FromMinutes(16);

        var lookup = await _harness.PriceAsync(at);

        Assert.Equal(PriceLookupStatus.NotAvailable, lookup.Status);
        Assert.Equal(2, _harness.Http.Sent.Count(request => request.Uri.Host == "riksbank.test"));
    }

    [Fact]
    public async Task Says_that_today_may_be_no_banking_day()
    {
        // Saturday 2026-11-07: the case asks about the morning, and Friday's rate is the latest.
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        var at = new DateTimeOffset(2026, 11, 7, 9, 30, 0, TimeSpan.Zero);
        _harness.AnswerCandle(at, "1");
        _harness.Http.AnswerText(
            $"{Fixtures.Riksbank}Observations/SEKUSDPMI/2026-10-24/2026-11-07",
            """[{"date":"2026-11-06","value":9.6}]""");

        var lookup = await _harness.PriceAsync(at);

        Assert.Equal(PriceLookupStatus.NotAvailable, lookup.Status);
        Assert.Contains("ingen bankdag", lookup.Problem, StringComparison.Ordinal);
        Assert.Contains("2026-11-06", lookup.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shares_one_riksbank_request_among_lookups_at_once()
    {
        _harness.AnswerAll();
        var days = Enumerable.Range(0, 8).Select(day => Fixtures.At.AddDays(day)).ToList();
        foreach (var at in days.Skip(1))
        {
            _harness.AnswerCandle(at, "2");
        }

        // Whichever lookup goes first asks for the days around its own day; each such request has an answer.
        foreach (var at in days)
        {
            var day = DateOnly.FromDateTime(at.UtcDateTime);
            var (from, to) = (day.AddDays(-14), day.AddDays(31));
            var rates = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
                .Select(offset => from.AddDays(offset))
                .Where(date => date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                .Select(date => $$$"""{"date":"{{{date:yyyy-MM-dd}}}","value":10.5}""");
            _harness.Http.AnswerText(
                $"{Fixtures.Riksbank}Observations/SEKUSDPMI/{from:yyyy-MM-dd}/{to:yyyy-MM-dd}",
                $"[{string.Join(",", rates)}]");
        }

        var lookups = await Task.WhenAll(days.Select(at => _harness.PriceAsync(at)));

        Assert.All(lookups, lookup => Assert.NotNull(lookup.Value));
        Assert.Single(_harness.Http.Sent, request => request.Uri.Host == "riksbank.test");
    }

    [Fact]
    public async Task Spaces_the_requests_to_the_riksbank()
    {
        var interval = TimeSpan.FromMilliseconds(400);
        using var harness = new Harness(new PricingOptions
        {
            BitstampBaseUri = new Uri(Fixtures.Bitstamp),
            RiksbankBaseUri = new Uri(Fixtures.Riksbank),
            RiksbankInterval = interval,
            KronaVia = FiatCurrency.Usd,
        });
        var later = Fixtures.At.AddDays(60);
        harness.AnswerCandle(Fixtures.At, "1");
        harness.AnswerCandle(later, "1");
        harness.Http.Answer(Fixtures.RiksbankUsdNovember, "riksbank-sekusdpmi.json");
        harness.Http.AnswerText(
            $"{Fixtures.Riksbank}Observations/SEKUSDPMI/2023-12-30/2024-02-13",
            """[{"date":"2024-01-12","value":10}]""");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await harness.PriceAsync(Fixtures.At);

        var lookup = await harness.PriceAsync(later);

        Assert.Equal(10m, lookup.Value!.FiatRate!.PerUnit);
        Assert.True(watch.Elapsed >= interval, watch.Elapsed.ToString());
    }

    [Fact]
    public async Task Seals_a_response_a_replaced_source_cannot_read()
    {
        using var harness = new Harness(before: services => services.AddSingleton<ICryptoRateSource>(new Throwing()));
        harness.Http.AnswerText("https://throwing.test/", "{}");

        var lookup = await harness.PriceAsync(Fixtures.At, FiatCurrency.Usd);

        Assert.Equal(PriceLookupStatus.Failed, lookup.Status);
        Assert.Single(harness.RawRows());
        Assert.Contains("Throwing", harness.RawRows()[0].Source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shares_one_bitstamp_request_among_lookups_of_the_same_period()
    {
        _harness.AnswerAll();

        var lookups = await Task.WhenAll(Enumerable.Range(0, 8).Select(
            second => _harness.PriceAsync(Fixtures.At.AddSeconds(second), FiatCurrency.Usd)));

        Assert.All(lookups, lookup => Assert.Equal(30123.45m, lookup.Value!.UnitPrice));
        Assert.Single(_harness.Http.Sent);
        Assert.Single(_harness.RawRows());
    }

    [Fact]
    public async Task Asks_the_riksbank_once_for_a_day_without_a_final_rate()
    {
        // Saturday 2026-11-07: the rate cannot be final today, and asking again at once cannot help.
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        var times = Enumerable.Range(0, 6)
            .Select(minute => new DateTimeOffset(2026, 11, 7, 9, minute, 0, TimeSpan.Zero));
        _harness.AnswerCandle(new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero), "1");
        _harness.Http.AnswerText(
            $"{Fixtures.Riksbank}Observations/SEKUSDPMI/2026-10-24/2026-11-07",
            """[{"date":"2026-11-06","value":9.6}]""");

        var lookups = await Task.WhenAll(times.Select(at => _harness.PriceAsync(at)));

        Assert.All(lookups, lookup => Assert.Equal(PriceLookupStatus.NotAvailable, lookup.Status));
        Assert.Single(_harness.Http.Sent, request => request.Uri.Host == "riksbank.test");
    }

    [Fact]
    public async Task Counts_a_day_as_passed_only_six_hours_after_midnight()
    {
        // Sunday 2026-11-08 at 00:30 in Stockholm: Saturday may still get a late rate, so nothing is kept as final.
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 11, 7, 23, 30, 0, TimeSpan.Zero);
        var at = new DateTimeOffset(2026, 11, 7, 9, 30, 0, TimeSpan.Zero);
        _harness.AnswerCandle(at, "1");
        _harness.Http.AnswerText(
            $"{Fixtures.Riksbank}Observations/SEKUSDPMI/2026-10-24/2026-11-07",
            """[{"date":"2026-11-06","value":9.6}]""");

        var early = await _harness.PriceAsync(at);
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 11, 8, 5, 0, 1, TimeSpan.Zero);
        var later = await _harness.PriceAsync(at);

        Assert.Equal(PriceLookupStatus.NotAvailable, early.Status);
        Assert.Equal(9.6m, later.Value!.FiatRate!.PerUnit);
        Assert.Equal(1, _harness.Count("pricing_fiat_ranges"));
    }

    [Fact]
    public async Task Stops_at_the_riksbank_limit_for_a_day()
    {
        _harness.Http.Failure = Chainsaw.Modules.Network.Contracts.ExitError.Timeout;
        _harness.AnswerAll();
        await _harness.PriceAsync(Fixtures.At, FiatCurrency.Usd);
        _harness.Http.Failure = null;
        _harness.Clock.UtcNow += new PricingOptions().FailureCooldown;
        await _harness.PriceAsync(Fixtures.At, FiatCurrency.Usd);
        _harness.Http.Failure = Chainsaw.Modules.Network.Contracts.ExitError.Timeout;
        for (var request = 0; request < 1_000; request++)
        {
            await _harness.PriceAsync(Fixtures.At);
        }

        _harness.Http.Failure = null;
        var sent = _harness.Http.Sent.Count;

        var lookup = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.Failed, lookup.Status);
        Assert.Contains("1 000", lookup.Problem, StringComparison.Ordinal);
        Assert.Equal(sent, _harness.Http.Sent.Count);
    }

    [Fact]
    public async Task Spaces_the_next_request_also_after_a_timeout()
    {
        var interval = TimeSpan.FromMilliseconds(400);
        using var harness = new Harness(new PricingOptions
        {
            BitstampBaseUri = new Uri(Fixtures.Bitstamp),
            RiksbankBaseUri = new Uri(Fixtures.Riksbank),
            RiksbankInterval = interval,
            KronaVia = FiatCurrency.Usd,
        });
        harness.AnswerAll();
        await harness.PriceAsync(Fixtures.At, FiatCurrency.Usd);
        harness.Http.Failure = Chainsaw.Modules.Network.Contracts.ExitError.Timeout;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await harness.PriceAsync(Fixtures.At);
        harness.Http.Failure = null;

        await harness.PriceAsync(Fixtures.At);

        Assert.True(watch.Elapsed >= interval, watch.Elapsed.ToString());
    }

    [Fact]
    public async Task Lets_the_next_lookup_go_when_one_waiting_for_the_riksbank_is_cancelled()
    {
        using var harness = new Harness(new PricingOptions
        {
            BitstampBaseUri = new Uri(Fixtures.Bitstamp),
            RiksbankBaseUri = new Uri(Fixtures.Riksbank),
            RiksbankInterval = TimeSpan.FromMilliseconds(800),
            KronaVia = FiatCurrency.Usd,
        });
        harness.AnswerAll();
        await harness.PriceAsync(Fixtures.At, FiatCurrency.Usd);
        harness.Http.Failure = Chainsaw.Modules.Network.Contracts.ExitError.Timeout;
        await harness.PriceAsync(Fixtures.At);
        harness.Http.Failure = null;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Prices.GetPriceAsync(
            harness.Case, new PriceRequest(Chain.Bitcoin, Fixtures.At), cancellationToken: cancel.Token));
        var next = await harness.PriceAsync(Fixtures.At).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(PriceLookupStatus.Fetched, next.Status);
    }

    [Fact]
    public async Task Keeps_fetching_a_shared_period_when_the_first_lookup_stops_waiting()
    {
        _harness.AnswerAll();
        _harness.Http.Delay = TimeSpan.FromMilliseconds(300);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var first = _harness.Prices.GetPriceAsync(
            _harness.Case, new PriceRequest(Chain.Bitcoin, Fixtures.At) { Currency = FiatCurrency.Usd },
            cancellationToken: cancel.Token);
        var second = _harness.PriceAsync(Fixtures.At, FiatCurrency.Usd);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(PriceLookupStatus.Fetched, (await second).Status);
        Assert.Single(_harness.Http.Sent);
        Assert.Single(_harness.RawRows());
        _harness.Http.Delay = TimeSpan.Zero;
        Assert.Equal(PriceLookupStatus.FromCase, (await _harness.PriceAsync(Fixtures.At, FiatCurrency.Usd)).Status);
        Assert.Single(_harness.Http.Sent);
    }

    [Fact]
    public async Task Takes_fridays_rate_for_a_saturday_in_the_small_hours_of_monday()
    {
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 11, 9, 1, 0, 0, TimeSpan.Zero);
        var at = new DateTimeOffset(2026, 11, 7, 9, 30, 0, TimeSpan.Zero);
        _harness.AnswerCandle(at, "1");
        _harness.Http.AnswerText(
            $"{Fixtures.Riksbank}Observations/SEKUSDPMI/2026-10-24/2026-11-07",
            """[{"date":"2026-11-06","value":9.6}]""");

        var lookup = await _harness.PriceAsync(at);

        Assert.Equal(9.6m, lookup.Value!.FiatRate!.PerUnit);
        Assert.Equal(1, _harness.Count("pricing_fiat_ranges"));
    }

    [Fact]
    public async Task Does_not_space_a_request_the_exit_refused()
    {
        using var harness = new Harness(new PricingOptions
        {
            BitstampBaseUri = new Uri(Fixtures.Bitstamp),
            RiksbankBaseUri = new Uri(Fixtures.Riksbank),
            RiksbankInterval = TimeSpan.FromSeconds(30),
            KronaVia = FiatCurrency.Usd,
        });
        harness.AnswerAll();
        await harness.PriceAsync(Fixtures.At, FiatCurrency.Usd);
        harness.Http.Connected = false;
        await harness.PriceAsync(Fixtures.At);
        harness.Http.Connected = true;

        var lookup = await harness.PriceAsync(Fixtures.At).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(PriceLookupStatus.Fetched, lookup.Status);
    }

    [Fact]
    public async Task Answers_a_source_whose_address_the_exit_refuses()
    {
        using var harness = new Harness(
            before: services => services.AddSingleton<ICryptoRateSource>(new Throwing(insecure: true)));

        var lookup = await harness.PriceAsync(Fixtures.At, FiatCurrency.Usd);

        Assert.Equal(PriceLookupStatus.Failed, lookup.Status);
        Assert.Empty(harness.Http.Sent);
        Assert.Empty(harness.RawRows());
    }

    [Fact]
    public async Task Answers_a_source_that_gives_no_address()
    {
        using var harness = new Harness(
            before: services => services.AddSingleton<ICryptoRateSource>(new Throwing(http: true)));

        var lookup = await harness.PriceAsync(Fixtures.At, FiatCurrency.Usd);

        Assert.Equal(PriceLookupStatus.Failed, lookup.Status);
        Assert.Empty(harness.RawRows());
    }

    /// <summary>A replaced source whose reader throws something other than its contract says.</summary>
    private sealed class Throwing(bool http = false, bool insecure = false) : ICryptoRateSource
    {
        public string Name => "Throwing";

        public string DisplayName => "Throwing";

        public bool Supports(Chain chain, FiatCurrency quote) => true;

        public Uri RequestUri(
            Chain chain, FiatCurrency quote, PriceResolution resolution, DateTimeOffset periodStart) =>
            http ? throw new InvalidOperationException("No address.") : new(Address(insecure));

        private static string Address(bool insecure) =>
            insecure ? "http://throwing.test/" : "https://throwing.test/";

        public RateCandle? ReadCandle(
            ReadOnlySpan<byte> body, Chain chain, FiatCurrency quote, PriceResolution resolution,
            DateTimeOffset periodStart) =>
            throw new ArgumentException("Not what the contract says.");
    }
}
