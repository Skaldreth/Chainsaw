using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Pricing;

/// <summary>
/// The price of a chain's currency at a time: the crypto rate from an <see cref="ICryptoRateSource"/> and, when
/// counting in kronor, the Riksbank's rate from an <see cref="IFiatRateSource"/>. Every request goes only through
/// <see cref="IExitHttp"/>; every response is sealed in the case before it is used, and a rate for a period that has
/// ended is never fetched again. Without an exit the case answers, or the answer says <i>Offline</i>.
/// </summary>
internal sealed class PriceService : IPriceService
{
    /// <summary>
    /// How many days before the day asked the fiat request starts, to find a rate over weekends and holidays.
    /// </summary>
    private const int LookbackDays = 14;

    /// <summary>
    /// How many days after the day asked the fiat request reaches, when they have passed, so that one request serves
    /// the rates of a whole month of transactions (the Riksbank allows few anonymous requests a minute).
    /// </summary>
    private const int AheadDays = 31;

    /// <summary>The HTTP status for too many requests.</summary>
    private const int TooManyRequestsStatus = 429;

    /// <summary>The source of crypto rates.</summary>
    private readonly ICryptoRateSource _crypto;

    /// <summary>The source of fiat rates.</summary>
    private readonly IFiatRateSource _fiat;

    /// <summary>The only way out.</summary>
    private readonly IExitHttp _http;

    /// <summary>Who fetches, recorded with every response.</summary>
    private readonly IOperatorProfileStore _operators;

    /// <summary>The time now.</summary>
    private readonly IClock _clock;

    /// <summary>The settings.</summary>
    private readonly PricingOptions _options;

    /// <summary>Seals and finds the raw responses.</summary>
    private readonly RateResponseStore _store;

    /// <summary>Creates the service.</summary>
    /// <param name="crypto">The source of crypto rates.</param>
    /// <param name="fiat">The source of fiat rates.</param>
    /// <param name="http">The only way out.</param>
    /// <param name="operators">The operator profile.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">The settings.</param>
    /// <param name="timeZone">The time zone local times are recorded in.</param>
    public PriceService(
        ICryptoRateSource crypto, IFiatRateSource fiat, IExitHttp http, IOperatorProfileStore operators,
        IClock clock, PricingOptions options, TimeZoneInfo timeZone)
    {
        _crypto = crypto;
        _fiat = fiat;
        _http = http;
        _operators = operators;
        _clock = clock;
        _options = options;
        _store = new RateResponseStore(timeZone);
    }

    /// <inheritdoc/>
    public async Task<PriceLookup<Price>> GetPriceAsync(
        ICase @case, PriceRequest request, PriceLookupMode mode = PriceLookupMode.AsNeeded,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(request);
        var currency = request.Currency ?? _options.Currency;
        var resolution = request.Resolution ?? _options.Resolution;
        if (Refusal(request, currency, resolution, out var quote) is { } refusal)
        {
            return new PriceLookup<Price>(PriceLookupStatus.Unsupported, null, [], refusal);
        }

        var crypto = await CryptoRateAsync(@case, request.Chain, quote, resolution, request.At, mode, cancellationToken)
            .ConfigureAwait(false);
        if (crypto.Value is not { } cryptoRate)
        {
            return new PriceLookup<Price>(crypto.Status, null, crypto.Origins, crypto.Problem);
        }

        if (currency == quote)
        {
            return new PriceLookup<Price>(
                crypto.Status, new Price(request.At, currency, cryptoRate, null), crypto.Origins, null);
        }

        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(request.At, _fiat.Calendar).DateTime);
        var fiat = await FiatRateAsync(@case, quote, day, mode, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<RateOrigin> origins = [.. crypto.Origins, .. fiat.Origins];
        if (fiat.Value is not { } fiatRate)
        {
            return new PriceLookup<Price>(fiat.Status, null, origins, fiat.Problem);
        }

        var status = crypto.Status == PriceLookupStatus.Fetched || fiat.Status == PriceLookupStatus.Fetched
            ? PriceLookupStatus.Fetched
            : PriceLookupStatus.FromCase;
        return new PriceLookup<Price>(status, new Price(request.At, currency, cryptoRate, fiatRate), origins, null);
    }

    /// <summary>Says why a request cannot be answered by the sources, before anything is read or sent.</summary>
    /// <param name="request">The request.</param>
    /// <param name="currency">The currency counted in.</param>
    /// <param name="resolution">The crypto rate's resolution.</param>
    /// <param name="quote">The currency the crypto rate is taken in.</param>
    /// <returns>Why, in Swedish, or <see langword="null"/> when it can be answered.</returns>
    private string? Refusal(
        PriceRequest request, FiatCurrency currency, PriceResolution resolution, out FiatCurrency quote)
    {
        quote = currency == _fiat.Base ? _options.KronaVia : currency;
        if (!Enum.IsDefined(request.Chain) || !Enum.IsDefined(currency) || !Enum.IsDefined(resolution)
            || !Enum.IsDefined(quote))
        {
            return "Kedjan, valutan eller upplösningen är okänd. Inget skickades.";
        }

        if (request.At > _clock.UtcNow)
        {
            return "Tidpunkten ligger i framtiden, och ingen kurs är satt för den. Inget skickades.";
        }

        if (currency != quote && (quote == _fiat.Base || !_fiat.Supports(quote)))
        {
            return $"Det finns ingen källa som räknar om {quote.Code()} till {currency.Code()}. Inget skickades.";
        }

        return _crypto.Supports(request.Chain, quote)
            ? null
            : $"Källan har ingen kurs för {request.Chain.Code()} i {quote.Code()}. Inget skickades.";
    }

    /// <summary>Gives the crypto rate for the period that holds a time: from the case, or from the source.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="quote">The currency.</param>
    /// <param name="resolution">The length of the period.</param>
    /// <param name="at">The time.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The rate, or why there is none.</returns>
    private async Task<PriceLookup<CryptoRate>> CryptoRateAsync(
        ICase @case, Chain chain, FiatCurrency quote, PriceResolution resolution, DateTimeOffset at,
        PriceLookupMode mode, CancellationToken cancellationToken)
    {
        var start = resolution.PeriodStart(at);
        var end = start.AddSeconds(resolution.Seconds());
        var subject = $"{chain.Code()}-{quote.Code()}/{resolution.Seconds().ToString(CultureInfo.InvariantCulture)}/" +
            start.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        CryptoRate? Read(StoredRateResponse response) =>
            _crypto.ReadCandle(response.Body, chain, quote, resolution, start) is { } candle
                ? Rate(chain, quote, resolution, start, end, candle, response.Origin)
                : null;

        var stored = await Task.Run(
            () => @case.Read(reader => _store.Latest(reader, _crypto.Name, RateKinds.CryptoCandle, subject)),
            cancellationToken).ConfigureAwait(false);
        if (stored is not null && Quietly(() => Read(stored)) is { } kept)
        {
            return new PriceLookup<CryptoRate>(PriceLookupStatus.FromCase, kept, [kept.Origin], null);
        }

        if (mode == PriceLookupMode.CaseOnly)
        {
            return Missing<CryptoRate>(PriceLookupStatus.NotInCase, "Läst bara ur ärendet; inget skickades.");
        }

        var period = $"{Time(start)}–{Time(end)} UTC";
        if (end > _clock.UtcNow)
        {
            return Missing<CryptoRate>(PriceLookupStatus.NotAvailable,
                $"Perioden {period} är inte slut än, så kursen är inte satt. Inget skickades.");
        }

        var sent = await SendAsync(_crypto.RequestUri(chain, quote, resolution, start), cancellationToken)
            .ConfigureAwait(false);
        if (sent.Response is not { } response)
        {
            return Missing<CryptoRate>(sent.Status, sent.Problem!);
        }

        RateCandle? candle = null;
        var problem = Unusable(response);
        var failed = problem is not null;
        if (problem is null)
        {
            try
            {
                candle = _crypto.ReadCandle(response.Body.Span, chain, quote, resolution, start);
                if (candle is null || end > response.ReceivedAt)
                {
                    // A period that has not ended at the source can still change, so it is never kept as final.
                    candle = null;
                    problem = $"Källan har ingen avslutad kurs för perioden {period}.";
                }
            }
            catch (RateFormatException)
            {
                (problem, failed) = (UnreadableProblem, true);
            }
        }

        var sealedResponse = await SealAsync(@case, _crypto.Name, RateKinds.CryptoCandle, subject, sent,
            (transaction, saved) =>
            {
                if (candle is not null)
                {
                    RateResponseStore.AddLookup(transaction, saved);
                }
            }).ConfigureAwait(false);
        var origin = sealedResponse.Origin;
        return candle is null
            ? new PriceLookup<CryptoRate>(StatusOf(failed), null, [origin], problem)
            : new PriceLookup<CryptoRate>(
                PriceLookupStatus.Fetched, Rate(chain, quote, resolution, start, end, candle, origin), [origin], null);
    }

    /// <summary>
    /// Gives the fiat rate for a day: the source's rate on that day, or on the latest banking day before it.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="currency">The foreign currency.</param>
    /// <param name="day">The day asked about, in the source's calendar.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The rate, or why there is none.</returns>
    private async Task<PriceLookup<FiatRate>> FiatRateAsync(
        ICase @case, FiatCurrency currency, DateOnly day, PriceLookupMode mode, CancellationToken cancellationToken)
    {
        var series = _fiat.Series(currency);
        var ranges = await Task.Run(
            () => @case.Read(reader => _store.FiatRangesCovering(reader, _fiat.Name, series, day)),
            cancellationToken).ConfigureAwait(false);
        foreach (var range in ranges)
        {
            var kept = Quietly(() => Pick(
                _fiat.ReadObservations(range.Response.Body, currency, range.From, range.To), day) is { } found
                ? FiatRateOf(currency, series, day, found, range.Response.Origin)
                : null);
            if (kept is not null)
            {
                return new PriceLookup<FiatRate>(PriceLookupStatus.FromCase, kept, [kept.Origin], null);
            }
        }

        if (mode == PriceLookupMode.CaseOnly)
        {
            return Missing<FiatRate>(PriceLookupStatus.NotInCase, "Läst bara ur ärendet; inget skickades.");
        }

        var today = Today();
        var from = day.AddDays(-LookbackDays);
        var to = day < today ? Min(day.AddDays(AheadDays), today.AddDays(-1)) : day;
        var sent = await SendAsync(_fiat.RequestUri(currency, from, to), cancellationToken).ConfigureAwait(false);
        if (sent.Response is not { } response)
        {
            return Missing<FiatRate>(sent.Status, sent.Problem!);
        }

        FiatObservation? picked = null;
        var settled = false;
        var problem = Unusable(response);
        var failed = problem is not null;
        if (problem is null)
        {
            try
            {
                picked = Pick(_fiat.ReadObservations(response.Body.Span, currency, from, to), day);
                settled = to < DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(response.ReceivedAt, _fiat.Calendar)
                    .DateTime);
                problem = FiatProblem(series, day, from, picked, settled);
            }
            catch (RateFormatException)
            {
                (problem, failed) = (UnreadableProblem, true);
            }
        }

        var subject = RateResponseStore.FiatSubject(series, from, to);
        var sealedResponse = await SealAsync(@case, _fiat.Name, RateKinds.FiatObservations, subject, sent,
            (transaction, saved) =>
            {
                if (problem is null && settled)
                {
                    RateResponseStore.AddFiatRange(transaction, series, new StoredFiatRange(saved, from, to));
                }
            }).ConfigureAwait(false);
        return problem is null
            ? new PriceLookup<FiatRate>(PriceLookupStatus.Fetched,
                FiatRateOf(currency, series, day, picked!, sealedResponse.Origin), [sealedResponse.Origin], null)
            : new PriceLookup<FiatRate>(StatusOf(failed), null, [sealedResponse.Origin], problem);
    }

    /// <summary>Says why fiat rates that were read give no rate for the day, if they do not.</summary>
    /// <param name="series">The series.</param>
    /// <param name="day">The day asked about.</param>
    /// <param name="from">The first day asked for.</param>
    /// <param name="picked">The rate picked for the day, if any.</param>
    /// <param name="settled">Whether every day asked for has passed, so that the rates are final.</param>
    /// <returns>Why, in Swedish, or <see langword="null"/> when there is a rate.</returns>
    private static string? FiatProblem(
        string series, DateOnly day, DateOnly from, FiatObservation? picked, bool settled)
    {
        if (picked is null)
        {
            return $"Källan har ingen kurs i serien {series} mellan {Day(from)} och {Day(day)}.";
        }

        // Today's rate is set during the day: until it is, the latest earlier rate may not be the day's.
        return !settled && picked.Date != day
            ? $"Kursen i serien {series} för {Day(day)} är inte publicerad än. Försök igen senare."
            : null;
    }

    /// <summary>Picks the rate for a day: the day's own, or the latest before it.</summary>
    /// <param name="observations">The rates, oldest first.</param>
    /// <param name="day">The day.</param>
    /// <returns>The rate, or <see langword="null"/> when there is none on or before the day.</returns>
    private static FiatObservation? Pick(IReadOnlyList<FiatObservation> observations, DateOnly day) =>
        observations.LastOrDefault(observation => observation.Date <= day);

    /// <summary>Makes a fiat rate from what the source said.</summary>
    /// <param name="currency">The foreign currency.</param>
    /// <param name="series">The series.</param>
    /// <param name="day">The day asked about.</param>
    /// <param name="observation">The rate picked.</param>
    /// <param name="origin">The sealed response.</param>
    /// <returns>The rate.</returns>
    private FiatRate FiatRateOf(
        FiatCurrency currency, string series, DateOnly day, FiatObservation observation, RateOrigin origin) =>
        new(currency, _fiat.Base, observation.PerUnit, observation.Date, day, series, origin);

    /// <summary>Makes a crypto rate from what the source said.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="quote">The currency.</param>
    /// <param name="resolution">The length of the period.</param>
    /// <param name="start">The period's start.</param>
    /// <param name="end">The period's end.</param>
    /// <param name="candle">The period as the source gave it.</param>
    /// <param name="origin">The sealed response.</param>
    /// <returns>The rate.</returns>
    private static CryptoRate Rate(
        Chain chain, FiatCurrency quote, PriceResolution resolution, DateTimeOffset start, DateTimeOffset end,
        RateCandle candle, RateOrigin origin) =>
        new(chain, quote, resolution, start, end, candle.Market, candle.Open, candle.High, candle.Low, candle.Close,
            candle.Volume, origin);

    /// <summary>The text for a response that cannot be read.</summary>
    private const string UnreadableProblem =
        "Källans svar gick inte att tolka eller gick inte ihop. Det är sparat i ärendet men används inte.";

    /// <summary>Says why a response's status makes it unusable, if it does.</summary>
    /// <param name="response">The response.</param>
    /// <returns>Why, in Swedish, or <see langword="null"/> for a success.</returns>
    private static string? Unusable(ExitResponse response)
    {
        if (response.IsSuccess)
        {
            return null;
        }

        return response.StatusCode == TooManyRequestsStatus
            ? "Källan tar inte emot fler uppslag just nu. Försök igen om en stund."
            : $"Källan svarade med status {response.StatusCode.ToString(CultureInfo.InvariantCulture)}.";
    }

    /// <summary>Gives the status of an answer without a rate.</summary>
    /// <param name="failed">
    /// Whether the response was an error or could not be read, rather than a readable answer without the rate.
    /// </param>
    /// <returns><see cref="PriceLookupStatus.Failed"/> or <see cref="PriceLookupStatus.NotAvailable"/>.</returns>
    private static PriceLookupStatus StatusOf(bool failed) =>
        failed ? PriceLookupStatus.Failed : PriceLookupStatus.NotAvailable;

    /// <summary>The outcome of sending a request: the response, or why there is none.</summary>
    /// <param name="Response">The response, or <see langword="null"/>.</param>
    /// <param name="FetchedBy">Who fetched it.</param>
    /// <param name="Status">The status when there is no response.</param>
    /// <param name="Problem">Why there is no response, in Swedish.</param>
    private sealed record Sent(
        ExitResponse? Response, OperatorProfile? FetchedBy, PriceLookupStatus Status, string? Problem);

    /// <summary>Sends a request through the exit, when an operator can be named.</summary>
    /// <param name="uri">The address, which never names the case.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The response, or why there is none.</returns>
    private async Task<Sent> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        var fetchedBy = CurrentOperator(out var noOperator);
        if (fetchedBy is null)
        {
            return new Sent(null, null, PriceLookupStatus.Failed, noOperator);
        }

        try
        {
            var request = ExitRequest.Get(uri) with { Accept = "application/json" };
            var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return new Sent(response, fetchedBy, PriceLookupStatus.Fetched, null);
        }
        catch (ExitNotConnectedException)
        {
            return new Sent(null, null, PriceLookupStatus.Offline,
                "Offline: ingen utgång är ansluten, och ärendet har inte kursen. Inget skickades.");
        }
        catch (ExitException exception)
        {
            return new Sent(null, null, PriceLookupStatus.Failed, exception.Message);
        }
    }

    /// <summary>Seals a response in the case, with what it adds to the indexes, in one transaction.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="source">The source's name.</param>
    /// <param name="kind">What was asked.</param>
    /// <param name="subject">What it was asked about.</param>
    /// <param name="sent">The response and who fetched it.</param>
    /// <param name="index">Adds the sealed response to the indexes when it is a final answer.</param>
    /// <returns>The sealed response.</returns>
    private Task<StoredRateResponse> SealAsync(
        ICase @case, string source, string kind, string subject, Sent sent,
        Action<ICaseTransaction, StoredRateResponse> index) =>
        Task.Run(
            () => @case.Write(transaction =>
            {
                var saved = _store.Append(transaction, source, kind, subject, sent.Response!, sent.FetchedBy!);
                index(transaction, saved);
                return saved;
            }),
            CancellationToken.None);

    /// <summary>Reads a stored rate; a response this build can no longer read counts as no answer.</summary>
    /// <typeparam name="T">What is read.</typeparam>
    /// <param name="read">The reading.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    private static T? Quietly<T>(Func<T?> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (RateFormatException)
        {
            return null;
        }
    }

    /// <summary>An answer without a value, and nothing sealed.</summary>
    /// <typeparam name="T">What is looked up.</typeparam>
    /// <param name="status">The status.</param>
    /// <param name="problem">Why, in Swedish.</param>
    /// <returns>The answer.</returns>
    private static PriceLookup<T> Missing<T>(PriceLookupStatus status, string problem)
        where T : class =>
        new(status, null, [], problem);

    /// <summary>Gets the operator who fetches, or says why there is none.</summary>
    /// <param name="problem">Why there is no operator, in Swedish.</param>
    /// <returns>The operator, or <see langword="null"/>.</returns>
    private OperatorProfile? CurrentOperator(out string? problem)
    {
        problem = null;
        try
        {
            var profile = _operators.Get();
            if (profile is null)
            {
                problem =
                    "Ingen operatörsprofil är sparad. Chainsaw hämtar inget utan att kunna säga vem som hämtade.";
            }

            return profile;
        }
        catch (VaultException)
        {
            problem = "Valvet är låst. Inget skickades.";
            return null;
        }
    }

    /// <summary>Gives today's date in the fiat source's calendar.</summary>
    /// <returns>The date.</returns>
    private DateOnly Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.UtcNow, _fiat.Calendar).DateTime);

    /// <summary>Gives the earlier of two days.</summary>
    /// <param name="first">The first day.</param>
    /// <param name="second">The second day.</param>
    /// <returns>The earlier.</returns>
    private static DateOnly Min(DateOnly first, DateOnly second) => first < second ? first : second;

    /// <summary>Writes a day for the user.</summary>
    /// <param name="day">The day.</param>
    /// <returns>For example <c>2023-11-14</c>.</returns>
    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Writes a time in UTC for the user.</summary>
    /// <param name="time">The time.</param>
    /// <returns>For example <c>2023-11-14 22:00</c>.</returns>
    private static string Time(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
