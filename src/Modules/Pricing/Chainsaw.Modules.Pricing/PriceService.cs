using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
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

    /// <summary>
    /// How many requests the Riksbank allows from one IP address a day without a key. Counted on this computer across
    /// restarts when <see cref="PricingOptions.StateDirectory"/> is set; computers that share an exit share the limit
    /// without knowing it.
    /// </summary>
    private const int FiatDailyLimit = 1_000;

    /// <summary>The folder under <see cref="PricingOptions.StateDirectory"/> where the daily count is kept.</summary>
    private const string StateFolder = "Kurser";

    /// <summary>How long an answer without a final rate is given again instead of sending the same request.</summary>
    private static readonly TimeSpan PendingReuse = TimeSpan.FromMinutes(15);

    /// <summary>How long after midnight in the fiat source's calendar a day counts as passed.</summary>
    private static readonly TimeSpan DayPassMargin = TimeSpan.FromHours(6);

    /// <summary>The HTTP status for too many requests.</summary>
    private const int TooManyRequestsStatus = 429;

    /// <summary>
    /// How long after a period's end the source is asked, and a response must have arrived, before its candle is
    /// final: the computer's clock may be ahead, and the exchange may add the last trades a little late.
    /// </summary>
    private static readonly TimeSpan SettleMargin = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The crypto periods being fetched now, per open case by subject, so that lookups share one request.
    /// </summary>
    private readonly ConditionalWeakTable<ICase, ConcurrentDictionary<string, CryptoFlight>> _cryptoFlights = new();

    /// <summary>Lets one request at a time go to the fiat source.</summary>
    private readonly SemaphoreSlim _fiatGate = new(1, 1);

    /// <summary>
    /// When the last request to the fiat source was answered or failed on the way, as
    /// <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>.
    /// </summary>
    private long? _lastFiatRequest;

    /// <summary>The requests to the fiat source today, in its calendar; only used inside the fiat gate.</summary>
    private readonly DailyRequestCount _fiatRequests;

    /// <summary>
    /// Answers from the fiat source without a final rate, and failed requests to it, by case and request, with when
    /// they came; only used inside <see cref="_fiatGate"/>.
    /// </summary>
    private readonly Dictionary<string, (PriceLookup<FiatRate> Lookup, DateTimeOffset At)> _pendingFiat = [];

    /// <summary>
    /// The crypto periods whose request failed lately, or whose response had no final rate (a period before the
    /// market's history), by subject, with when, the status and whether the source asked to wait; such a period is
    /// not asked for again until <see cref="PricingOptions.FailureCooldown"/> has passed.
    /// </summary>
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, PriceLookupStatus Status, bool Throttled)>
        _failedCrypto = new();

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

    /// <summary>The currency and resolution chosen in the settings, for a request that names none.</summary>
    private readonly PriceDefaults _defaults;

    /// <summary>Seals and finds the raw responses.</summary>
    private readonly RateResponseStore _store;

    /// <summary>Creates the service.</summary>
    /// <param name="crypto">The source of crypto rates.</param>
    /// <param name="fiat">The source of fiat rates.</param>
    /// <param name="http">The only way out.</param>
    /// <param name="operators">The operator profile.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">The settings.</param>
    /// <param name="defaults">The currency and resolution chosen in the settings.</param>
    /// <param name="timeZone">The time zone local times are recorded in.</param>
    public PriceService(
        ICryptoRateSource crypto, IFiatRateSource fiat, IExitHttp http, IOperatorProfileStore operators,
        IClock clock, PricingOptions options, PriceDefaults defaults, TimeZoneInfo timeZone)
    {
        _defaults = defaults;
        _crypto = crypto;
        _fiat = fiat;
        _http = http;
        _operators = operators;
        _clock = clock;
        _options = options;
        _fiatRequests = new DailyRequestCount(options.StateDirectory is { } state
            ? Path.Combine(state, StateFolder, fiat.Name + "-requests.json")
            : null);
        _store = new RateResponseStore(timeZone);
    }

    /// <inheritdoc/>
    public async Task<PriceLookup<Price>> GetPriceAsync(
        ICase @case, PriceRequest request, PriceLookupMode mode = PriceLookupMode.AsNeeded,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(request);
        var chosen = _defaults.Both;
        var currency = request.Currency ?? chosen.Currency;
        var resolution = request.Resolution ?? chosen.Resolution;
        if (Refusal(request, currency, resolution, out var quote) is { } refusal)
        {
            return new PriceLookup<Price>(PriceLookupStatus.Unsupported, null, [], refusal);
        }

        var asset = AssetOf(request);
        var crypto = await CryptoRateAsync(@case, asset, quote, resolution, request.At, mode, cancellationToken)
            .ConfigureAwait(false);
        if (crypto.Value is not { } cryptoRate)
        {
            return new PriceLookup<Price>(crypto.Status, null, crypto.Origins, crypto.Problem)
            {
                IsThrottled = crypto.IsThrottled,
            };
        }

        if (currency == quote)
        {
            return new PriceLookup<Price>(
                crypto.Status, new Price(request.At, currency, cryptoRate, null), crypto.Origins, null);
        }

        var day = DayOf(request.At);
        var fiat = await FiatRateAsync(@case, quote, day, mode, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<RateOrigin> origins = [.. crypto.Origins, .. fiat.Origins];
        if (fiat.Value is not { } fiatRate)
        {
            return new PriceLookup<Price>(fiat.Status, null, origins, fiat.Problem)
            {
                IsThrottled = fiat.IsThrottled || Throttled(fiat.Origins),
            };
        }

        var status = crypto.Status == PriceLookupStatus.Fetched || fiat.Status == PriceLookupStatus.Fetched
            ? PriceLookupStatus.Fetched
            : PriceLookupStatus.FromCase;
        return new PriceLookup<Price>(status, new Price(request.At, currency, cryptoRate, fiatRate), origins, null);
    }

    /// <inheritdoc/>
    public RateSummary Summarize(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(RateResponseStore.Summarize);
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

        if (request.Asset is { } given && given.Chain != request.Chain)
        {
            return $"{given.Symbol} finns inte på kedjan {request.Chain}. Inget skickades.";
        }

        if (request.At > _clock.UtcNow)
        {
            return "Tidpunkten ligger i framtiden, och ingen kurs är satt för den. Inget skickades.";
        }

        if (currency != quote && (quote == _fiat.Base || !_fiat.Supports(quote)))
        {
            return $"Det finns ingen källa som räknar om {quote.Code()} till {currency.Code()}. Inget skickades.";
        }

        var asset = AssetOf(request);
        if (_crypto.Supports(asset, quote))
        {
            return null;
        }

        if (asset.Kind == AssetKind.Native)
        {
            return $"Källan har ingen kurs för {request.Chain.Code()} i {quote.Code()}. Inget skickades.";
        }

        // A token is never taken to be worth its peg: without a market for it there is no price at all.
        return Enum.GetValues<FiatCurrency>().Any(other => _crypto.Supports(asset, other))
            ? $"Källan har ingen kurs för {asset.Symbol} i {quote.Code()}. Inget skickades."
            : $"Ingen kurs: ingen källa anger kursen för {asset.Symbol} ({asset.Contract}). Inget skickades.";
    }

    /// <summary>
    /// Gives the asset a request prices: the chain's own coin when it names none, and a token Chainsaw knows with its
    /// own symbol and decimals (by contract), so that a source's spelling never shows in the steps.
    /// </summary>
    /// <param name="request">The request, with a known chain.</param>
    /// <returns>The asset.</returns>
    private static Asset AssetOf(PriceRequest request) =>
        request.Asset is { } given ? KnownTokens.Canonical(given) : request.Chain.NativeAsset();

    /// <summary>
    /// Gives the crypto rate for the period that holds a time: from the case, or from the source. Lookups of the same
    /// period at once share one request.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="asset">The asset: a chain's own coin or a token.</param>
    /// <param name="quote">The currency.</param>
    /// <param name="resolution">The length of the period.</param>
    /// <param name="at">The time.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="cancellationToken">Stops the wait; a request that is already shared runs to its end.</param>
    /// <returns>The rate, or why there is none.</returns>
    private async Task<PriceLookup<CryptoRate>> CryptoRateAsync(
        ICase @case, Asset asset, FiatCurrency quote, PriceResolution resolution, DateTimeOffset at,
        PriceLookupMode mode, CancellationToken cancellationToken)
    {
        var query = new CandleQuery(asset, quote, resolution, resolution.PeriodStart(at));
        if (await StoredCryptoRateAsync(@case, query, cancellationToken).ConfigureAwait(false) is { } kept)
        {
            return FromCase(kept, NoCryptoHistory(query));
        }

        if (mode == PriceLookupMode.CaseOnly)
        {
            return Missing<CryptoRate>(PriceLookupStatus.NotInCase, "Läst bara ur ärendet; inget skickades.");
        }

        if (query.SettlesAt > _clock.UtcNow)
        {
            return Missing<CryptoRate>(PriceLookupStatus.NotAvailable,
                $"Perioden {query.Period} är inte avslutad hos källan än, så kursen är inte satt. " +
                "Inget skickades.");
        }

        if (RecentFailure(query) is { } failure)
        {
            return failure;
        }

        // Per open case: a response is sealed in the case that asked, and a case opened again is a new one.
        var flights = _cryptoFlights.GetValue(@case, _ => new ConcurrentDictionary<string, CryptoFlight>());
        CryptoFlight? mine = null;
        mine = new CryptoFlight(() => SharedCryptoFetchAsync(@case, query, flights, mine!));
        var flight = flights.GetOrAdd(query.Subject, mine);
        return await flight.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches a period once for every lookup waiting for it: reads the case again first (an earlier request may just
    /// have sealed it), and lets the next lookup start afresh when done. It runs to its end even if a caller stops
    /// waiting, so that a response that was sent for is always sealed.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="query">The period.</param>
    /// <param name="flights">The case's fetches now, by subject.</param>
    /// <param name="self">This fetch, which removes only itself when done.</param>
    /// <returns>The rate, or why there is none.</returns>
    private async Task<PriceLookup<CryptoRate>> SharedCryptoFetchAsync(
        ICase @case, CandleQuery query, ConcurrentDictionary<string, CryptoFlight> flights, CryptoFlight self)
    {
        try
        {
            if (await StoredCryptoRateAsync(@case, query, CancellationToken.None).ConfigureAwait(false) is { } kept)
            {
                return FromCase(kept, NoCryptoHistory(query));
            }

            var fetched = await FetchCryptoRateAsync(@case, query).ConfigureAwait(false);
            RememberFailure(query, fetched);
            return fetched;
        }
        finally
        {
            flights.TryRemove(KeyValuePair.Create(query.Subject, self));
        }
    }

    /// <summary>
    /// Answers a period whose request failed lately, or whose response had no final rate, without asking again,
    /// until the cooldown has passed.
    /// </summary>
    /// <param name="query">The period.</param>
    /// <returns>The answer, or <see langword="null"/> when the period may be asked for.</returns>
    private PriceLookup<CryptoRate>? RecentFailure(CandleQuery query)
    {
        if (!_failedCrypto.TryGetValue(query.Subject, out var failed)
            || _clock.UtcNow - failed.At >= _options.FailureCooldown)
        {
            return null;
        }

        var problem = failed.Status == PriceLookupStatus.Failed
            ? "Kursen kunde inte hämtas nyss och frågas igen om en stund. Inget skickades."
            : "Källan hade ingen avslutad kurs för perioden nyss och frågas igen om en stund. Inget skickades.";
        return new PriceLookup<CryptoRate>(failed.Status, null, [], problem)
        {
            IsThrottled = failed.Throttled,
        };
    }

    /// <summary>
    /// Remembers a failed request for a period, or a response without a final rate for it, or forgets an earlier
    /// one when it answered.
    /// </summary>
    /// <param name="query">The period.</param>
    /// <param name="lookup">The answer.</param>
    private void RememberFailure(CandleQuery query, PriceLookup<CryptoRate> lookup)
    {
        // A response that came after the period ended and still had no rate for it (before the market's history) is
        // sealed each time it is asked, so it is not asked again at once either.
        if (lookup.Status == PriceLookupStatus.Failed
            || (lookup.Status == PriceLookupStatus.NotAvailable
                && lookup.Origins.Any(origin => origin.ReceivedUtc >= query.SettlesAt)))
        {
            _failedCrypto[query.Subject] = (_clock.UtcNow, lookup.Status, lookup.IsThrottled);
        }
        else
        {
            _failedCrypto.TryRemove(query.Subject, out _);
        }
    }

    /// <summary>Gets whether a source answered that it takes no more requests now.</summary>
    /// <param name="origins">The sealed responses.</param>
    /// <returns><see langword="true"/> when one of them is 429.</returns>
    private static bool Throttled(IEnumerable<RateOrigin> origins) =>
        origins.Any(origin => origin.StatusCode == TooManyRequestsStatus);

    /// <summary>
    /// Finds a final answer for a period in the case: its rate, or a sealed response that came after the period ended
    /// and had no rate for it (a period before the source's history), which is final too and is not asked for again
    /// (the review of etapp 14, M6 and M-3).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="query">The period.</param>
    /// <param name="cancellationToken">Stops the lookup before it starts.</param>
    /// <returns>The answer, or <see langword="null"/> when the case has none.</returns>
    private Task<Kept<CryptoRate>?> StoredCryptoRateAsync(
        ICase @case, CandleQuery query, CancellationToken cancellationToken)
    {
        // The index is not sealed: a stored answer counts only when its own sealed time shows it was final.
        Kept<CryptoRate>? Read(StoredRateResponse response)
        {
            if (response.Origin.ReceivedUtc < query.SettlesAt || !Succeeded(response.Origin))
            {
                return null;
            }

            var candle = _crypto.ReadCandle(response.Body, query.Asset, query.Quote, query.Resolution, query.Start);
            return new Kept<CryptoRate>(candle is null ? null : Rate(query, candle, response.Origin), response.Origin);
        }

        return Task.Run(
            () => @case.Read(reader =>
                _store.Latest(reader, _crypto.Name, RateKinds.CryptoCandle, query.Subject) is { } stored
                    ? Quietly(() => Read(stored))
                    : null),
            cancellationToken);
    }

    /// <summary>Asks the crypto source for a period, seals the response and reads the candle.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="query">The period.</param>
    /// <returns>The rate, or why there is none.</returns>
    private async Task<PriceLookup<CryptoRate>> FetchCryptoRateAsync(ICase @case, CandleQuery query)
    {
        var sent = await SendAsync(
            () => _crypto.RequestUri(query.Asset, query.Quote, query.Resolution, query.Start),
            null, CancellationToken.None).ConfigureAwait(false);
        if (sent.Response is not { } response)
        {
            return Missing<CryptoRate>(sent.Status, sent.Problem!);
        }

        RateCandle? candle = null;
        var noHistory = false;
        var problem = Unusable(response);
        var failed = problem is not null;
        if (problem is null)
        {
            try
            {
                candle = _crypto.ReadCandle(
                    response.Body.Span, query.Asset, query.Quote, query.Resolution, query.Start);
                if (candle is null || query.SettlesAt > response.ReceivedAt)
                {
                    // A period that may not have ended at the source can still change, so it is never kept; one
                    // that had ended and still has no rate lies before the source's history, which is final.
                    noHistory = candle is null && query.SettlesAt <= response.ReceivedAt;
                    candle = null;
                    problem = noHistory
                        ? NoCryptoHistory(query)
                        : $"Källan har ingen avslutad kurs för perioden {query.Period}.";
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Whatever the source's reader throws, the response is sealed afterwards (principle 1).
                (problem, failed) = (UnreadableProblem, true);
            }
        }

        var sealedResponse = await SealAsync(@case, _crypto.Name, RateKinds.CryptoCandle, query.Subject, sent,
            (transaction, saved) =>
            {
                if (candle is not null || noHistory)
                {
                    RateResponseStore.AddLookup(transaction, saved);
                }
            }).ConfigureAwait(false);
        var origin = sealedResponse.Origin;
        return candle is null
            ? new PriceLookup<CryptoRate>(StatusOf(failed), null, [origin], problem)
            {
                IsThrottled = origin.StatusCode == TooManyRequestsStatus,
            }
            : new PriceLookup<CryptoRate>(PriceLookupStatus.Fetched, Rate(query, candle, origin), [origin], null);
    }

    /// <summary>
    /// A final answer kept in the case: a rate, or none when the sealed response shows the source has no rate for the
    /// time.
    /// </summary>
    /// <typeparam name="T">The rate.</typeparam>
    /// <param name="Rate">The rate, or <see langword="null"/>.</param>
    /// <param name="Origin">The sealed response.</param>
    private sealed record Kept<T>(T? Rate, RateOrigin Origin)
        where T : class;

    /// <summary>A fetch of one crypto period that lookups at once share.</summary>
    /// <param name="fetch">Starts the fetch, once.</param>
    private sealed class CryptoFlight(Func<Task<PriceLookup<CryptoRate>>> fetch)
        : Lazy<Task<PriceLookup<CryptoRate>>>(fetch);

    /// <summary>One period of a crypto price history to look up.</summary>
    /// <param name="Asset">The asset: a chain's own coin or a token.</param>
    /// <param name="Quote">The currency.</param>
    /// <param name="Resolution">The length of the period.</param>
    /// <param name="Start">The period's start.</param>
    private sealed record CandleQuery(Asset Asset, FiatCurrency Quote, PriceResolution Resolution, DateTimeOffset Start)
    {
        /// <summary>Gets the chain the asset lives on.</summary>
        public Chain Chain => Asset.Chain;

        /// <summary>Gets the period's end.</summary>
        public DateTimeOffset End => Start.AddSeconds(Resolution.Seconds());

        /// <summary>Gets when the period's candle is final: <see cref="SettleMargin"/> after its end.</summary>
        public DateTimeOffset SettlesAt => End + SettleMargin;

        /// <summary>
        /// Gets the subject the response is stored under, for example <c>BTC-USD/3600/1699999200</c> for a chain's
        /// coin, as before tokens could be priced, and
        /// <c>Ethereum:0xdac17f958d2ee523a2206206994597c13d831ec7-USD/3600/1699999200</c> for a token, named by its
        /// chain and contract so that it can never be mistaken for the coin or for a token with the same symbol.
        /// </summary>
        public string Subject =>
            $"{AssetKey}-{Quote.Code()}/{Resolution.Seconds().ToString(CultureInfo.InvariantCulture)}/" +
            Start.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        /// <summary>Gets the asset as the subject names it: the chain's code, or the chain and the contract.</summary>
        private string AssetKey => Asset.Kind == AssetKind.Native ? Chain.Code() : $"{Chain}:{Asset.Contract}";

        /// <summary>Gets the period for the user, for example <c>2023-11-14 22:00–2023-11-14 23:00 UTC</c>.</summary>
        public string Period => $"{Time(Start)}–{Time(End)} UTC";
    }

    /// <summary>
    /// Gives the fiat rate for a day: the source's rate on that day, or on the latest banking day before it. Requests
    /// to the source go one at a time, at least <see cref="PricingOptions.RiksbankInterval"/> apart, and the case is
    /// read again before each, so that many lookups at once share one response (the Riksbank allows few anonymous
    /// requests).
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
        if (await StoredFiatRateAsync(@case, currency, series, day, cancellationToken).ConfigureAwait(false)
            is { } kept)
        {
            return FromCase(kept, NoFiatHistory(series, day));
        }

        if (mode == PriceLookupMode.CaseOnly)
        {
            return Missing<FiatRate>(PriceLookupStatus.NotInCase, "Läst bara ur ärendet; inget skickades.");
        }

        await _fiatGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await StoredFiatRateAsync(@case, currency, series, day, cancellationToken).ConfigureAwait(false)
                is { } meanwhile)
            {
                return FromCase(meanwhile, NoFiatHistory(series, day));
            }

            var (from, to) = FiatRange(day);
            // The day that has passed is part of the key: once it moves on, the same request may now be final.
            var passed = PassedBefore(_clock.UtcNow);
            var key = $"{@case.Path}|{RateResponseStore.FiatSubject(series, from, to)}|" +
                passed.ToString(RateResponseStore.DayFormat, CultureInfo.InvariantCulture);
            if (_pendingFiat.TryGetValue(key, out var pending))
            {
                if (pending.Lookup.Status == PriceLookupStatus.NotAvailable
                    && _clock.UtcNow - pending.At < PendingReuse)
                {
                    // The same request was answered a moment ago without a final rate; asking again cannot help yet.
                    return pending.Lookup;
                }

                if (pending.Lookup.Status == PriceLookupStatus.Failed
                    && _clock.UtcNow - pending.At < _options.FailureCooldown)
                {
                    // The same request failed a moment ago: a source that is down is not asked over and over.
                    return new PriceLookup<FiatRate>(
                        PriceLookupStatus.Failed, null, [],
                        "Kursen kunde inte hämtas nyss och frågas igen om en stund. Inget skickades.")
                    {
                        IsThrottled = Throttled(pending.Lookup.Origins),
                    };
                }
            }

            if (DailyLimitReached())
            {
                return Missing<FiatRate>(PriceLookupStatus.Failed,
                    "Chainsaw har gjort Riksbankens 1 000 uppslag för i dag från den här datorn. " +
                    "Inget skickades; försök igen i morgon.");
            }

            await SpaceFiatRequestAsync(cancellationToken).ConfigureAwait(false);
            var lookup = await FetchFiatRateAsync(@case, currency, series, day, from, to).ConfigureAwait(false);
            RememberPending(key, lookup);
            return lookup;
        }
        finally
        {
            _fiatGate.Release();
        }
    }

    /// <summary>
    /// Finds a final answer for a day in the case: the day's rate, or a sealed response for the days before it that
    /// had passed and had no rate (a day before the series' history), which is final too and is not asked for again.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="currency">The foreign currency.</param>
    /// <param name="series">The series.</param>
    /// <param name="day">The day asked about.</param>
    /// <param name="cancellationToken">Stops the lookup before it starts.</param>
    /// <returns>The answer, or <see langword="null"/> when the case has none.</returns>
    private Task<Kept<FiatRate>?> StoredFiatRateAsync(
        ICase @case, FiatCurrency currency, string series, DateOnly day, CancellationToken cancellationToken) =>
        Task.Run(
            () => @case.Read(reader => _store.FirstFiatRange(reader, _fiat.Name, series, day, range => Quietly(() =>
            {
                var observations = _fiat.ReadObservations(range.Response.Body, currency, range.From, range.AskedTo);
                if (!FinalFor(range.Response.Origin, range.From, range.AskedTo, observations, day))
                {
                    return null;
                }

                var origin = range.Response.Origin;
                if (!Succeeded(origin))
                {
                    return null;
                }

                return Pick(observations, day) is { } found
                    ? new Kept<FiatRate>(FiatRateOf(currency, series, day, found, origin), origin)
                    : new Kept<FiatRate>(null, origin);
            }))),
            cancellationToken);

    /// <summary>
    /// Gives the days to ask the fiat source for: <see cref="LookbackDays"/> before the day, and up to
    /// <see cref="AheadDays"/> after it as long as they have passed (see <see cref="PassedBefore"/>), so that one
    /// response serves a month.
    /// </summary>
    /// <param name="day">The day asked about.</param>
    /// <returns>The first and last day.</returns>
    private (DateOnly From, DateOnly To) FiatRange(DateOnly day)
    {
        var passed = PassedBefore(_clock.UtcNow);
        var to = day < passed ? Min(day.AddDays(AheadDays), passed.AddDays(-1)) : day;
        return (day.AddDays(-LookbackDays), to);
    }

    /// <summary>
    /// Gives the first day that has not surely passed at a time: a day counts as passed only
    /// <see cref="DayPassMargin"/> after its midnight, since the computer's clock may be ahead and the source may
    /// publish a day's rate late.
    /// </summary>
    /// <param name="time">The time.</param>
    /// <returns>Every day before this one has passed.</returns>
    private DateOnly PassedBefore(DateTimeOffset time) => DayOf(time - DayPassMargin);

    /// <summary>
    /// Remembers an answer without a final rate, or a failed request, so that the same request is not sent again at
    /// once (<see cref="PendingReuse"/> and <see cref="PricingOptions.FailureCooldown"/>).
    /// </summary>
    /// <param name="key">The case, the request's subject and the last day that had passed.</param>
    /// <param name="lookup">The answer.</param>
    private void RememberPending(string key, PriceLookup<FiatRate> lookup)
    {
        var now = _clock.UtcNow;
        foreach (var old in _pendingFiat.Where(entry => now - entry.Value.At >= PendingReuse)
            .Select(entry => entry.Key).ToList())
        {
            _pendingFiat.Remove(old);
        }

        // A failure is remembered only when its response was sealed, so that a source that answers with errors does
        // not fill the case with them; a failure on the way seals nothing and is held back by the spacing alone.
        if (lookup.Status == PriceLookupStatus.NotAvailable
            || (lookup.Status == PriceLookupStatus.Failed && lookup.Origins.Count > 0))
        {
            _pendingFiat[key] = (lookup, now);
        }
        else
        {
            _pendingFiat.Remove(key);
        }
    }

    /// <summary>Counts a request to the fiat source towards the spacing and the day's limit.</summary>
    private void CountFiatRequest()
    {
        _lastFiatRequest = System.Diagnostics.Stopwatch.GetTimestamp();
        _fiatRequests.Add(Today());
    }

    /// <summary>Gets whether this computer has made the fiat source's daily number of requests today.</summary>
    /// <returns><see langword="true"/> when no more may be sent today.</returns>
    private bool DailyLimitReached() => _fiatRequests.On(Today()) >= FiatDailyLimit;

    /// <summary>Waits until the last request to the fiat source is far enough behind.</summary>
    /// <param name="cancellationToken">Stops the wait.</param>
    /// <returns>A task that completes when a request may be sent.</returns>
    private async Task SpaceFiatRequestAsync(CancellationToken cancellationToken)
    {
        if (_lastFiatRequest is not { } last)
        {
            return;
        }

        var wait = _options.RiksbankInterval - System.Diagnostics.Stopwatch.GetElapsedTime(last);
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Asks the fiat source for the days around a day, seals the response and reads the day's rate.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="currency">The foreign currency.</param>
    /// <param name="series">The series.</param>
    /// <param name="day">The day asked about.</param>
    /// <param name="from">The first day to ask for.</param>
    /// <param name="to">The last day to ask for.</param>
    /// <returns>The rate, or why there is none.</returns>
    private async Task<PriceLookup<FiatRate>> FetchFiatRateAsync(
        ICase @case, FiatCurrency currency, string series, DateOnly day, DateOnly from, DateOnly to)
    {
        // Once the request may leave, it runs to its end and is sealed: a response that was sent for is never lost.
        var sent = await SendAsync(() => _fiat.RequestUri(currency, from, to), CountFiatRequest, CancellationToken.None)
            .ConfigureAwait(false);
        if (sent.Response is not { } response)
        {
            return Missing<FiatRate>(sent.Status, sent.Problem!);
        }

        FiatObservation? picked = null;
        DateOnly? finalTo = null;
        var noHistory = false;
        var problem = Unusable(response);
        var failed = problem is not null;
        if (problem is null)
        {
            try
            {
                var observations = _fiat.ReadObservations(response.Body.Span, currency, from, to);
                picked = Pick(observations, day);
                var passed = PassedBefore(response.ReceivedAt);
                var final = day < passed || observations.Any(observation => observation.Date == day);
                finalTo = to < passed ? to : observations.LastOrDefault()?.Date;
                noHistory = picked is null && final;
                problem = noHistory ? NoFiatHistory(series, day) : FiatProblem(series, day, from, picked, final);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Whatever the source's reader throws, the response is sealed afterwards (principle 1).
                (problem, failed) = (UnreadableProblem, true);
            }
        }

        var subject = RateResponseStore.FiatSubject(series, from, to);
        var sealedResponse = await SealAsync(@case, _fiat.Name, RateKinds.FiatObservations, subject, sent,
            (transaction, saved) =>
            {
                if ((problem is null || noHistory) && finalTo is { } covered && covered >= day)
                {
                    RateResponseStore.AddFiatRange(transaction, series, new StoredFiatRange(saved, from, to), covered);
                }
            }).ConfigureAwait(false);
        return problem is null
            ? new PriceLookup<FiatRate>(PriceLookupStatus.Fetched,
                FiatRateOf(currency, series, day, picked!, sealedResponse.Origin), [sealedResponse.Origin], null)
            : new PriceLookup<FiatRate>(StatusOf(failed), null, [sealedResponse.Origin], problem);
    }

    /// <summary>
    /// Decides from a sealed response alone whether its rate for a day is final: the day must be one that was asked
    /// for, and either have passed (see <see cref="PassedBefore"/>) when the response arrived or have its own rate (a
    /// published rate does not change).
    /// </summary>
    /// <param name="origin">The sealed response's origin.</param>
    /// <param name="from">The first day asked for.</param>
    /// <param name="to">The last day asked for.</param>
    /// <param name="observations">The rates in the response.</param>
    /// <param name="day">The day.</param>
    /// <returns><see langword="true"/> when the day's rate can no longer change.</returns>
    private bool FinalFor(
        RateOrigin origin, DateOnly from, DateOnly to, IReadOnlyList<FiatObservation> observations, DateOnly day) =>
        day >= from && day <= to
        && (day < PassedBefore(origin.ReceivedUtc) || observations.Any(observation => observation.Date == day));

    /// <summary>Says why fiat rates that were read give no rate for the day, if they do not.</summary>
    /// <param name="series">The series.</param>
    /// <param name="day">The day asked about.</param>
    /// <param name="from">The first day asked for.</param>
    /// <param name="picked">The rate picked for the day, if any.</param>
    /// <param name="final">Whether the day's rate can no longer change.</param>
    /// <returns>Why, in Swedish, or <see langword="null"/> when there is a rate.</returns>
    private static string? FiatProblem(
        string series, DateOnly day, DateOnly from, FiatObservation? picked, bool final)
    {
        if (picked is null)
        {
            return $"Källan har ingen kurs i serien {series} mellan {Day(from)} och {Day(day)}.";
        }

        // Today's rate is set during the day: until it is, the latest earlier rate may not be the day's.
        return final
            ? null
            : $"Kursen i serien {series} för {Day(day)} är inte publicerad än, eller så är dagen ingen bankdag; " +
              $"då gäller kursen för {Day(picked.Date)} när dagen har passerat. Försök igen senare.";
    }

    /// <summary>
    /// Says whether a sealed response is a success, so that an index that points at an error (the index is not
    /// sealed) is never read as a final answer without a rate.
    /// </summary>
    /// <param name="origin">The sealed response's origin.</param>
    /// <returns><see langword="true"/> for a 2xx status.</returns>
    private static bool Succeeded(RateOrigin origin) => origin.StatusCode is >= 200 and <= 299;

    /// <summary>Says that a crypto period lies before the source's history, as the sealed answer shows.</summary>
    /// <param name="query">The period.</param>
    /// <returns>Why there is no rate, in Swedish.</returns>
    private static string NoCryptoHistory(CandleQuery query) =>
        $"Källan har ingen kurs för perioden {query.Period}, som var avslutad när den frågades: perioden ligger " +
        "före källans historik. Svaret är sparat i ärendet och frågas inte igen.";

    /// <summary>Says that a day lies before a fiat series' history, which the case's sealed answer shows.</summary>
    /// <param name="series">The series.</param>
    /// <param name="day">The day.</param>
    /// <returns>Why there is no rate, in Swedish.</returns>
    private static string NoFiatHistory(string series, DateOnly day) =>
        $"Källan har ingen kurs i serien {series} för {Day(day)} eller dagarna före: dagen ligger före seriens " +
        "historik. Svaret är sparat i ärendet och frågas inte igen.";

    /// <summary>
    /// Answers from what the case holds: its rate, or that the source has no rate for the time, with the sealed
    /// response that shows it.
    /// </summary>
    /// <typeparam name="T">The rate.</typeparam>
    /// <param name="kept">The answer in the case.</param>
    /// <param name="noRate">Why there is no rate, when there is none.</param>
    /// <returns>The lookup.</returns>
    private static PriceLookup<T> FromCase<T>(Kept<T> kept, string noRate)
        where T : class =>
        kept.Rate is { } rate
            ? new PriceLookup<T>(PriceLookupStatus.FromCase, rate, [kept.Origin], null)
            : new PriceLookup<T>(PriceLookupStatus.NotAvailable, null, [kept.Origin], noRate);

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
        new(currency, _fiat.Base, observation.PerUnit, observation.Date, day, series, origin)
        {
            SourceName = _fiat.DisplayName,
        };

    /// <summary>Makes a crypto rate from what the source said.</summary>
    /// <param name="query">The period.</param>
    /// <param name="candle">The period as the source gave it.</param>
    /// <param name="origin">The sealed response.</param>
    /// <returns>The rate.</returns>
    private CryptoRate Rate(CandleQuery query, RateCandle candle, RateOrigin origin)
    {
        var rate = new CryptoRate(query.Chain, query.Quote, query.Resolution, query.Start, query.End, candle.Market,
            candle.Open, candle.High, candle.Low, candle.Close, candle.Volume, origin)
        {
            SourceName = _crypto.DisplayName,
        };

        // A chain's coin is the rate's default asset, so its rate stays exactly as before tokens could be priced.
        return query.Asset.Kind == AssetKind.Native ? rate : rate with { Asset = query.Asset };
    }

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

    /// <summary>Sends a request through the exit, when an operator can be named; never throws for a failure.</summary>
    /// <param name="uri">
    /// Gives the address, which never names the case; a source's failure to give one is answered.
    /// </param>
    /// <param name="sent">
    /// Called when the request may have left the computer (answered or failed on the way), or <see langword="null"/>;
    /// not when the exit refused it before sending.
    /// </param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The response, or why there is none.</returns>
    private async Task<Sent> SendAsync(Func<Uri> uri, Action? sent, CancellationToken cancellationToken)
    {
        var fetchedBy = CurrentOperator(out var noOperator);
        if (fetchedBy is null)
        {
            return new Sent(null, null, PriceLookupStatus.Failed, noOperator);
        }

        try
        {
            var request = ExitRequest.Get(uri()) with { Accept = "application/json" };
            var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            sent?.Invoke();
            return new Sent(response, fetchedBy, PriceLookupStatus.Fetched, null);
        }
        catch (ExitNotConnectedException exception)
        {
            // Said as the way out says it: no exit connected with VPN, or no network with Windows.
            return new Sent(null, null, PriceLookupStatus.Offline,
                $"Offline: {exception.Message} Ärendet har inte kursen. Inget skickades.");
        }
        catch (ExitException exception)
        {
            sent?.Invoke();
            return new Sent(null, null, PriceLookupStatus.Failed, exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A replaced source that gives no address, or one the exit refuses (not https).
            return new Sent(null, null, PriceLookupStatus.Failed,
                "Källans adress gick inte att bygga eller är inte tillåten. Inget skickades.");
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

    /// <summary>
    /// Reads a stored rate; a response this build (or a replaced source) can no longer read counts as no answer.
    /// </summary>
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
        catch (Exception exception) when (exception is not OperationCanceledException and not CaseException)
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
    private DateOnly Today() => DayOf(_clock.UtcNow);

    /// <summary>Gives the date of a time in the fiat source's calendar.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The date.</returns>
    private DateOnly DayOf(DateTimeOffset time) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, _fiat.Calendar).DateTime);

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
