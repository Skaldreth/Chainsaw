using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Pricing.Tests;

/// <summary>
/// The synchronous reader of the prices a case already holds (<see cref="ICaseRates"/>): it gives the same price the
/// lookup gives from the case, says what the case does not hold, and never sends anything.
/// </summary>
public sealed class CaseRatesTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static PriceRequest Request(
        DateTimeOffset at, FiatCurrency currency, PriceResolution resolution = PriceResolution.Hour) =>
        new(Chain.Bitcoin, at) { Currency = currency, Resolution = resolution };

    [Fact]
    public async Task Reads_the_same_price_the_lookup_sealed_and_sends_nothing()
    {
        _harness.AnswerAll();
        var fetched = (await _harness.PriceAsync(Fixtures.At, FiatCurrency.Sek)).Value!;
        var sent = _harness.Http.Sent.Count;

        var answers = _harness.Stored.ReadStored(
            _harness.Case,
            [
                Request(Fixtures.At, FiatCurrency.Sek),
                Request(Fixtures.At, FiatCurrency.Usd),
                Request(Fixtures.At.AddDays(3), FiatCurrency.Sek),
                Request(Fixtures.At, FiatCurrency.Sek, PriceResolution.Minute),
            ]);

        Assert.Equal(sent, _harness.Http.Sent.Count);
        Assert.Equal(4, answers.Count);
        Assert.Equal(PriceLookupStatus.FromCase, answers[0].Status);
        Assert.Equal(fetched.UnitPrice, answers[0].Value!.UnitPrice);
        Assert.Equal(fetched.Origins, answers[0].Origins);
        Assert.Equal(fetched.Steps, answers[0].Value!.Steps);
        Assert.Equal(PriceLookupStatus.FromCase, answers[1].Status);
        Assert.Equal(fetched.CryptoRate.Close, answers[1].Value!.UnitPrice);
        Assert.All(answers.Skip(2), answer =>
        {
            Assert.Equal(PriceLookupStatus.NotInCase, answer.Status);
            Assert.Null(answer.Value);
            Assert.Contains("inget skickades", answer.Problem, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_crypto_rate_without_the_day_rate_is_not_in_the_case()
    {
        _harness.AnswerAll();
        await _harness.PriceAsync(Fixtures.At, FiatCurrency.Usd);

        var answer = _harness.Stored.ReadStored(_harness.Case, [Request(Fixtures.At, FiatCurrency.Sek)])[0];

        Assert.Equal(PriceLookupStatus.NotInCase, answer.Status);
        Assert.Null(answer.Value);
        Assert.Single(answer.Origins);
        Assert.Equal(_harness.Http.Sent.Count, _harness.RawRows().Count);
    }

    [Fact]
    public void An_empty_case_answers_without_sending_and_a_future_time_is_unsupported()
    {
        var answers = _harness.Stored.ReadStored(
            _harness.Case,
            [Request(Fixtures.At, FiatCurrency.Eur), Request(_harness.Clock.UtcNow.AddDays(1), FiatCurrency.Eur)]);

        Assert.Equal(PriceLookupStatus.NotInCase, answers[0].Status);
        Assert.Equal(PriceLookupStatus.Unsupported, answers[1].Status);
        Assert.Empty(_harness.Http.Sent);
        Assert.Empty(_harness.Stored.ReadStored(_harness.Case, []));
    }

    [Fact]
    public void Null_requests_are_refused()
    {
        Assert.Throws<ArgumentNullException>(() => _harness.Stored.ReadStored(_harness.Case, null!));
        Assert.Throws<ArgumentNullException>(() => _harness.Stored.ReadStored(_harness.Case, [null!]));
        Assert.Throws<ArgumentNullException>(() => _harness.Stored.ReadStored(null!, []));
    }

    [Fact]
    public async Task Many_requests_in_the_same_hour_give_the_same_price()
    {
        _harness.AnswerAll();
        await _harness.PriceAsync(Fixtures.At, FiatCurrency.Sek);

        var answers = _harness.Stored.ReadStored(
            _harness.Case,
            [.. Enumerable.Range(0, 50).Select(minute => Request(Fixtures.At.AddSeconds(minute), FiatCurrency.Sek))]);

        Assert.All(answers, answer => Assert.Equal(PriceLookupStatus.FromCase, answer.Status));
        Assert.Single(answers.Select(answer => answer.Value!.UnitPrice).Distinct());
    }
}
