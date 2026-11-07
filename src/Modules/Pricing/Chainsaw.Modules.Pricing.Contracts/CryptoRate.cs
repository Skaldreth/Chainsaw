using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Pricing.Contracts;

/// <summary>
/// What an exchange's price history says about one period (a candle): a fact from a source, not something Chainsaw
/// works out (principle 2). The rate used is <see cref="Close"/>, the last price traded in the period that holds the
/// time asked about; the period is always given, so that the distance to that time is visible.
/// </summary>
/// <param name="Chain">The chain the priced asset lives on, for example Bitcoin (BTC).</param>
/// <param name="Quote">The currency the price is in.</param>
/// <param name="Resolution">The length of the period.</param>
/// <param name="PeriodStart">When the period begins, in UTC.</param>
/// <param name="PeriodEnd">When the period ends, in UTC; the period does not include this instant.</param>
/// <param name="Market">The market as the source names it, for example <c>BTC/USD</c>.</param>
/// <param name="Open">The first price in the period.</param>
/// <param name="High">The highest price in the period.</param>
/// <param name="Low">The lowest price in the period.</param>
/// <param name="Close">The last price in the period: the rate used.</param>
/// <param name="Volume">
/// How much was traded in the period, in the priced asset; zero means no trade in the period, so the prices do
/// not come from a trade in it.
/// </param>
/// <param name="Origin">The sealed response it comes from.</param>
public sealed record CryptoRate(
    Chain Chain,
    FiatCurrency Quote,
    PriceResolution Resolution,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string Market,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    RateOrigin Origin)
{
    /// <summary>The token priced, or <see langword="null"/> for the chain's own coin.</summary>
    private readonly Asset? _asset;

    /// <summary>
    /// Gets the asset the rate is for: the chain's own coin unless an asset is given, for example USDT on Ethereum.
    /// Left out, it follows <see cref="Chain"/>, so that a rate for a chain's coin is the same as before tokens could
    /// be priced.
    /// </summary>
    /// <exception cref="ArgumentNullException">The asset given is null.</exception>
    public Asset Asset
    {
        get => _asset ?? Chain.NativeAsset();
        init => _asset = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets the source's name for the user, for example <c>Bitstamp</c>, or <see langword="null"/> for the stable
    /// name in <see cref="RateOrigin.Source"/>.
    /// </summary>
    public string? SourceName { get; init; }
}
