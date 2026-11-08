#if DEBUG
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Pricing.TestData;

/// <summary>
/// Makes the made-up rates of the test case (<c>/testdata</c>), only in a Debug build: the real price service, with its
/// sealing and its steps (the crypto rate in euro, converted with the day's fiat rate), but asking made-up sources on
/// this computer in Bitstamp's and the Riksbank's form instead of the real ones through the exit. Every sealed response
/// names <see cref="CryptoSourceName"/> or <see cref="FiatSourceName"/>, so that the case never says that a made-up
/// rate came from Bitstamp or the Riksbank (principle 1).
/// </summary>
public static class TestDataPricing
{
    /// <summary>The made-up crypto source's name in every sealed response.</summary>
    public const string CryptoSourceName = "testdata-kurser";

    /// <summary>The made-up fiat source's name in every sealed response.</summary>
    public const string FiatSourceName = "testdata-valutor";

    /// <summary>
    /// The made-up crypto source's address, in Bitstamp's form. The top-level name <c>.invalid</c> never resolves
    /// (RFC 2606), so the address could reach nothing even if it were sent; it is not, since the requests go to the
    /// made-up answers given to <see cref="Create"/>.
    /// </summary>
    public static readonly Uri CryptoAddress = new("https://testdata.invalid/kurser/");

    /// <summary>The made-up fiat source's address, in the Riksbank's form; it never resolves either.</summary>
    public static readonly Uri FiatAddress = new("https://testdata.invalid/valutor/");

    /// <summary>Creates the price service over made-up rates.</summary>
    /// <param name="rates">Answers the requests from made-up rates; it must never go out on the net.</param>
    /// <param name="operators">Who fetched, as in every sealed response.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="defaults">The currency and resolution chosen in the settings, shared with the real service.</param>
    /// <returns>The service.</returns>
    public static IPriceService Create(
        IExitHttp rates, IOperatorProfileStore operators, IClock clock, IPriceDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(rates);
        ArgumentNullException.ThrowIfNull(operators);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(defaults);

        // Nothing leaves the computer, so the Riksbank's spacing between requests is not needed.
        var options = new PricingOptions
        {
            Currency = defaults.Currency,
            Resolution = defaults.Resolution,
            RiksbankInterval = TimeSpan.Zero,
        };
        var shared = defaults as PriceDefaults ?? new PriceDefaults(options);
        return new PriceService(
            new MadeUpCrypto(new BitstampRateSource(CryptoAddress)),
            new MadeUpFiat(new RiksbankRateSource(FiatAddress)),
            rates,
            operators,
            clock,
            options,
            shared,
            TimeZoneInfo.Local);
    }

    /// <summary>Bitstamp's form under the made-up source's own name.</summary>
    /// <param name="form">The form the requests and answers follow.</param>
    private sealed class MadeUpCrypto(ICryptoRateSource form) : ICryptoRateSource
    {
        /// <inheritdoc/>
        public string Name => CryptoSourceName;

        /// <inheritdoc/>
        public string DisplayName => "Påhittade kurser (testdata)";

        /// <inheritdoc/>
        public bool Supports(Chain chain, FiatCurrency quote) => form.Supports(chain, quote);

        /// <inheritdoc/>
        public bool Supports(Asset asset, FiatCurrency quote) => form.Supports(asset, quote);

        /// <inheritdoc/>
        public Uri RequestUri(Chain chain, FiatCurrency quote, PriceResolution resolution, DateTimeOffset periodStart) =>
            form.RequestUri(chain, quote, resolution, periodStart);

        /// <inheritdoc/>
        public Uri RequestUri(Asset asset, FiatCurrency quote, PriceResolution resolution, DateTimeOffset periodStart) =>
            form.RequestUri(asset, quote, resolution, periodStart);

        /// <inheritdoc/>
        public RateCandle? ReadCandle(
            ReadOnlySpan<byte> body, Chain chain, FiatCurrency quote, PriceResolution resolution,
            DateTimeOffset periodStart) =>
            form.ReadCandle(body, chain, quote, resolution, periodStart);

        /// <inheritdoc/>
        public RateCandle? ReadCandle(
            ReadOnlySpan<byte> body, Asset asset, FiatCurrency quote, PriceResolution resolution,
            DateTimeOffset periodStart) =>
            form.ReadCandle(body, asset, quote, resolution, periodStart);
    }

    /// <summary>The Riksbank's form under the made-up source's own name.</summary>
    /// <param name="form">The form the requests and answers follow.</param>
    private sealed class MadeUpFiat(IFiatRateSource form) : IFiatRateSource
    {
        /// <inheritdoc/>
        public string Name => FiatSourceName;

        /// <inheritdoc/>
        public string DisplayName => "Påhittade växelkurser (testdata)";

        /// <inheritdoc/>
        public FiatCurrency Base => form.Base;

        /// <inheritdoc/>
        public TimeZoneInfo Calendar => form.Calendar;

        /// <inheritdoc/>
        public bool Supports(FiatCurrency currency) => form.Supports(currency);

        /// <inheritdoc/>
        public string Series(FiatCurrency currency) => form.Series(currency);

        /// <inheritdoc/>
        public Uri RequestUri(FiatCurrency currency, DateOnly from, DateOnly to) =>
            form.RequestUri(currency, from, to);

        /// <inheritdoc/>
        public IReadOnlyList<FiatObservation> ReadObservations(
            ReadOnlySpan<byte> body, FiatCurrency currency, DateOnly from, DateOnly to) =>
            form.ReadObservations(body, currency, from, to);
    }
}
#endif
