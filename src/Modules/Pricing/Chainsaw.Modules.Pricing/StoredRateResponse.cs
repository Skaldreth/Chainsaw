using Chainsaw.Modules.Pricing.Contracts;

namespace Chainsaw.Modules.Pricing;

/// <summary>A raw response read back from the case's sealed table, or just sealed.</summary>
/// <param name="Kind">What was asked (<see cref="RateKinds"/>).</param>
/// <param name="Subject">What it was asked about.</param>
/// <param name="Origin">Where and when it came from, and who fetched it.</param>
/// <param name="Body">The raw bytes, checked against their SHA-256.</param>
internal sealed record StoredRateResponse(string Kind, string Subject, RateOrigin Origin, byte[] Body);

/// <summary>A sealed response from a fiat source and the days it covers, as the index says.</summary>
/// <param name="Response">The response.</param>
/// <param name="From">The first day asked.</param>
/// <param name="To">The last day asked.</param>
internal sealed record StoredFiatRange(StoredRateResponse Response, DateOnly From, DateOnly To);

/// <summary>The kinds of request, stored with each response so that it can be found again.</summary>
internal static class RateKinds
{
    /// <summary>One period of a crypto price history.</summary>
    public const string CryptoCandle = "crypto.candle";

    /// <summary>A central bank's rates over some days.</summary>
    public const string FiatObservations = "fiat.observations";
}
