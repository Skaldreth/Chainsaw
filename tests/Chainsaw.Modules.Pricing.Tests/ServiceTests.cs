using System.Security.Cryptography;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Pricing.Tests;

public sealed class ServiceTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Prices_in_kronor_by_default_through_dollars_and_the_riksbank()
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
            $"{Fixtures.Riksbank}Observations/SEKEURPMI/2023-10-31/2023-12-15", """[{"date":"2023-11-14","value":11.5}]""");

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
    [InlineData(Chain.Ethereum)]
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
        Assert.Contains("inte slut", lookup.Problem, StringComparison.Ordinal);
        Assert.Empty(_harness.Http.Sent);
    }

    [Fact]
    public async Task Never_keeps_a_period_the_source_has_not_ended_as_final()
    {
        _harness.AnswerAll();
        // The computer's clock says the hour has ended, but the response arrived before it did.
        _harness.Clock.UtcNow = DateTimeOffset.FromUnixTimeSeconds(Fixtures.HourStart + 3_600);
        _harness.Http.Lag = TimeSpan.FromSeconds(-1);

        var lookup = await _harness.PriceAsync(Fixtures.At, FiatCurrency.Usd);

        Assert.Equal(PriceLookupStatus.NotAvailable, lookup.Status);
        Assert.Single(_harness.Http.Sent);
        Assert.Equal(0, _harness.Count("pricing_lookup"));
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
        var later = await _harness.PriceAsync(Fixtures.At);

        Assert.Equal(PriceLookupStatus.Failed, failed.Status);
        Assert.Contains("500", failed.Problem, StringComparison.Ordinal);
        Assert.Equal(500, failed.Origins.Single().StatusCode);
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
        _harness.Http.AnswerText(riksbank, """[{"date":"2026-11-05","value":9.5},{"date":"2026-11-06","value":9.6}]""");
        var published = await _harness.PriceAsync(at);
        var again = await _harness.PriceAsync(at);

        Assert.Equal(PriceLookupStatus.NotAvailable, waiting.Status);
        Assert.Contains("inte publicerad", waiting.Problem, StringComparison.Ordinal);
        Assert.Equal(9.6m, published.Value!.FiatRate!.PerUnit);
        Assert.Equal(PriceLookupStatus.Fetched, again.Status);
        Assert.Equal(0, _harness.Count("pricing_fiat_ranges"));
        Assert.Equal(
            [riksbank, riksbank, riksbank],
            _harness.Http.Sent.Select(request => request.Uri.AbsoluteUri).Where(url => url.Contains("riksbank")));
    }

    [Fact]
    public async Task Reaches_only_to_yesterday_for_a_recent_day()
    {
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 11, 6, 12, 0, 1, TimeSpan.Zero);
        var at = new DateTimeOffset(2026, 11, 3, 10, 30, 0, TimeSpan.Zero);
        _harness.AnswerCandle(at, "1");
        _harness.Http.AnswerText(
            $"{Fixtures.Riksbank}Observations/SEKUSDPMI/2026-10-20/2026-11-05", """[{"date":"2026-11-03","value":9.5}]""");

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

    private void Execute(string sql) => _harness.Case.Write(transaction =>
    {
        using var command = transaction.CreateCommand(sql);
        return command.ExecuteNonQuery();
    });
}
