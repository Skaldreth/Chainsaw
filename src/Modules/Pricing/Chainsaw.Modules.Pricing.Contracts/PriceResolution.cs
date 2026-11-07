namespace Chainsaw.Modules.Pricing.Contracts;

/// <summary>
/// How fine the crypto rate is: the length of the period (a candle in the exchange's price history) whose closing
/// price is used. The period is always shown with the rate, so that the distance to the time asked about is visible.
/// </summary>
public enum PriceResolution
{
    /// <summary>One minute.</summary>
    Minute,

    /// <summary>One hour. The default, since a block's time is itself only roughly right.</summary>
    Hour,

    /// <summary>One day, from midnight UTC.</summary>
    Day,
}

/// <summary>The resolutions' lengths.</summary>
public static class PriceResolutions
{
    /// <summary>Gives the length of a period.</summary>
    /// <param name="resolution">The resolution.</param>
    /// <returns>The length in seconds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The resolution is unknown.</exception>
    public static long Seconds(this PriceResolution resolution) => resolution switch
    {
        PriceResolution.Minute => 60,
        PriceResolution.Hour => 3_600,
        PriceResolution.Day => 86_400,
        _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown resolution."),
    };

    /// <summary>
    /// Gives the start of the period, aligned to whole periods since 1970-01-01 UTC, that holds a time.
    /// </summary>
    /// <param name="resolution">The resolution.</param>
    /// <param name="time">The time.</param>
    /// <returns>The period's start, in UTC.</returns>
    public static DateTimeOffset PeriodStart(this PriceResolution resolution, DateTimeOffset time)
    {
        var seconds = resolution.Seconds();
        var unix = time.ToUnixTimeSeconds();
        var start = unix - (((unix % seconds) + seconds) % seconds);
        return DateTimeOffset.FromUnixTimeSeconds(start);
    }
}
