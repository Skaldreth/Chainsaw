using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Pricing.Contracts;

/// <summary>
/// A source of a crypto currency's price history: the contract for one kind of source, so that the source can be
/// replaced without anything else changing (principle 8). The source only says how to ask and how to read the
/// answer; asking through the exit and sealing the answer in the case is done once, outside the source.
/// </summary>
public interface ICryptoRateSource
{
    /// <summary>Gets the source's stable name, stored with every response, for example <c>bitstamp</c>.</summary>
    string Name { get; }

    /// <summary>Gets the source's name for the user, for example <c>Bitstamp</c>.</summary>
    string DisplayName { get; }

    /// <summary>Gets whether the source has a market for a chain's currency in a currency.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="quote">The currency.</param>
    /// <returns><see langword="true"/> when it can be asked.</returns>
    bool Supports(Chain chain, FiatCurrency quote);

    /// <summary>
    /// Gives the address that asks for one period. It names only the market, the length and the time.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="quote">The currency.</param>
    /// <param name="resolution">The length of the period.</param>
    /// <param name="periodStart">The period's start, aligned to the resolution.</param>
    /// <returns>The absolute <c>https</c> address.</returns>
    Uri RequestUri(Chain chain, FiatCurrency quote, PriceResolution resolution, DateTimeOffset periodStart);

    /// <summary>Reads the period from a response.</summary>
    /// <param name="body">The response's raw bytes.</param>
    /// <param name="chain">The chain asked about.</param>
    /// <param name="quote">The currency asked about.</param>
    /// <param name="resolution">The length asked about.</param>
    /// <param name="periodStart">The period asked about.</param>
    /// <returns>The period, or <see langword="null"/> when the response does not have it.</returns>
    /// <exception cref="RateFormatException">The response cannot be read or does not add up.</exception>
    RateCandle? ReadCandle(
        ReadOnlySpan<byte> body, Chain chain, FiatCurrency quote, PriceResolution resolution,
        DateTimeOffset periodStart);

    /// <summary>
    /// Gets whether the source has a market for an asset in a currency. A token is recognised by its chain and
    /// contract (<see cref="Asset"/> equality), never by its symbol. By default only a chain's own coin is, as
    /// <see cref="Supports(Chain, FiatCurrency)"/> says.
    /// </summary>
    /// <param name="asset">The asset: a chain's own coin or a token.</param>
    /// <param name="quote">The currency.</param>
    /// <returns><see langword="true"/> when it can be asked.</returns>
    /// <exception cref="ArgumentNullException">The asset is null.</exception>
    bool Supports(Asset asset, FiatCurrency quote)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return asset.Kind == AssetKind.Native && Supports(asset.Chain, quote);
    }

    /// <summary>
    /// Gives the address that asks for one period of an asset's market. It names only the market, the length and the
    /// time. By default only a chain's own coin can be asked for.
    /// </summary>
    /// <param name="asset">The asset, one the source supports.</param>
    /// <param name="quote">The currency.</param>
    /// <param name="resolution">The length of the period.</param>
    /// <param name="periodStart">The period's start, aligned to the resolution.</param>
    /// <returns>The absolute <c>https</c> address.</returns>
    /// <exception cref="ArgumentNullException">The asset is null.</exception>
    /// <exception cref="NotSupportedException">The source has no market for the asset.</exception>
    Uri RequestUri(Asset asset, FiatCurrency quote, PriceResolution resolution, DateTimeOffset periodStart)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return asset.Kind == AssetKind.Native
            ? RequestUri(asset.Chain, quote, resolution, periodStart)
            : throw new NotSupportedException($"The source has no market for {asset}.");
    }

    /// <summary>
    /// Reads the period of an asset's market from a response. By default only a chain's own coin can be read.
    /// </summary>
    /// <param name="body">The response's raw bytes.</param>
    /// <param name="asset">The asset asked about.</param>
    /// <param name="quote">The currency asked about.</param>
    /// <param name="resolution">The length asked about.</param>
    /// <param name="periodStart">The period asked about.</param>
    /// <returns>The period, or <see langword="null"/> when the response does not have it.</returns>
    /// <exception cref="ArgumentNullException">The asset is null.</exception>
    /// <exception cref="NotSupportedException">The source has no market for the asset.</exception>
    /// <exception cref="RateFormatException">The response cannot be read or does not add up.</exception>
    RateCandle? ReadCandle(
        ReadOnlySpan<byte> body, Asset asset, FiatCurrency quote, PriceResolution resolution,
        DateTimeOffset periodStart)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return asset.Kind == AssetKind.Native
            ? ReadCandle(body, asset.Chain, quote, resolution, periodStart)
            : throw new NotSupportedException($"The source has no market for {asset}.");
    }
}

/// <summary>One period of a price history as a source gives it.</summary>
/// <param name="Market">The market as the source names it, for example <c>BTC/USD</c>.</param>
/// <param name="Open">The first price.</param>
/// <param name="High">The highest price.</param>
/// <param name="Low">The lowest price.</param>
/// <param name="Close">The last price.</param>
/// <param name="Volume">How much was traded, in the asset priced.</param>
public sealed record RateCandle(string Market, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

/// <summary>
/// A source of a central bank's exchange rates against one currency, per banking day: the contract for one kind of
/// source (principle 8). Like <see cref="ICryptoRateSource"/>, it only says how to ask and how to read.
/// </summary>
public interface IFiatRateSource
{
    /// <summary>Gets the source's stable name, stored with every response, for example <c>riksbank</c>.</summary>
    string Name { get; }

    /// <summary>Gets the source's name for the user, for example <c>Riksbanken</c>.</summary>
    string DisplayName { get; }

    /// <summary>Gets the currency the rates are given in, for the Riksbank Swedish kronor.</summary>
    FiatCurrency Base { get; }

    /// <summary>Gets the time zone whose calendar the source's days follow.</summary>
    TimeZoneInfo Calendar { get; }

    /// <summary>Gets whether the source has rates for a currency.</summary>
    /// <param name="currency">The foreign currency.</param>
    /// <returns><see langword="true"/> when it can be asked.</returns>
    bool Supports(FiatCurrency currency);

    /// <summary>Gives the source's name for a currency's series.</summary>
    /// <param name="currency">The foreign currency.</param>
    /// <returns>For example <c>SEKUSDPMI</c>.</returns>
    string Series(FiatCurrency currency);

    /// <summary>
    /// Gives the address that asks for the rates over some days. It names only the series and the days.
    /// </summary>
    /// <param name="currency">The foreign currency.</param>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <returns>The absolute <c>https</c> address.</returns>
    Uri RequestUri(FiatCurrency currency, DateOnly from, DateOnly to);

    /// <summary>Reads the rates from a response, oldest first; days without a rate are missing.</summary>
    /// <param name="body">The response's raw bytes.</param>
    /// <param name="currency">The currency asked about.</param>
    /// <param name="from">The first day asked about.</param>
    /// <param name="to">The last day asked about.</param>
    /// <returns>The rates.</returns>
    /// <exception cref="RateFormatException">The response cannot be read or does not add up.</exception>
    IReadOnlyList<FiatObservation> ReadObservations(
        ReadOnlySpan<byte> body, FiatCurrency currency, DateOnly from, DateOnly to);
}

/// <summary>A central bank's rate on one banking day.</summary>
/// <param name="Date">The day.</param>
/// <param name="PerUnit">How much of the base currency one unit of the foreign currency costs.</param>
public sealed record FiatObservation(DateOnly Date, decimal PerUnit);

/// <summary>A source's response cannot be read or does not add up.</summary>
public sealed class RateFormatException : Exception
{
    /// <summary>Creates the exception.</summary>
    public RateFormatException()
    {
    }

    /// <summary>Creates the exception with a message for diagnostics.</summary>
    /// <param name="message">What is wrong.</param>
    public RateFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the failure that caused it.</summary>
    /// <param name="message">What is wrong.</param>
    /// <param name="innerException">The cause.</param>
    public RateFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
