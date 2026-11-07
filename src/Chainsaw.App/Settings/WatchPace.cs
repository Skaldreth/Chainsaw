namespace Chainsaw.App.Settings;

/// <summary>
/// How often the watches are checked while Chainsaw is open and unlocked (setting <em>Hur ofta</em>).
/// </summary>
public enum WatchPace
{
    /// <summary>At every new block (the default): the chain's height is asked once a minute.</summary>
    EveryBlock,

    /// <summary>At a new block, at most every ten minutes.</summary>
    TenMinutes,

    /// <summary>At a new block, at most every hour.</summary>
    Hour,
}

/// <summary>What the paces mean.</summary>
public static class WatchPaces
{
    /// <summary>Gives the least time between two checks of a chain.</summary>
    /// <param name="pace">The pace.</param>
    /// <returns>Zero for every block.</returns>
    public static TimeSpan Interval(this WatchPace pace) => pace switch
    {
        WatchPace.TenMinutes => TimeSpan.FromMinutes(10),
        WatchPace.Hour => TimeSpan.FromHours(1),
        _ => TimeSpan.Zero,
    };

    /// <summary>Gives the pace's name in the settings and the form.</summary>
    /// <param name="pace">The pace.</param>
    /// <returns>For example <c>Varje block</c>.</returns>
    public static string Label(this WatchPace pace) => pace switch
    {
        WatchPace.TenMinutes => "Var 10:e min",
        WatchPace.Hour => "Varje timme",
        _ => "Varje block",
    };
}
