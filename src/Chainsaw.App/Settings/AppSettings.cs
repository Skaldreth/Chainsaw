using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Pricing.Contracts;

namespace Chainsaw.App.Settings;

/// <summary>
/// The program's own settings, which hold nothing secret and nothing about a case, so they live in a plain file next
/// to the vault (<c>settings.json</c>). They are changed in the settings dialog (sketch ST1a) and saved at once.
/// </summary>
public sealed record AppSettings
{
    /// <summary>The automatic lock's time unless the user chooses another (decided 2026-11-07).</summary>
    public const int DefaultAutoLockMinutes = 15;

    /// <summary>The shortest time that can be chosen.</summary>
    public const int MinimumAutoLockMinutes = 1;

    /// <summary>The longest time that can be chosen: a working day.</summary>
    public const int MaximumAutoLockMinutes = 480;

    /// <summary>Gets how many minutes without activity before Chainsaw locks itself.</summary>
    public int AutoLockMinutes { get; init; } = DefaultAutoLockMinutes;

    /// <summary>Gets whether Chainsaw locks when Windows is locked; on by default.</summary>
    public bool LockWithWindows { get; init; } = true;

    /// <summary>
    /// Gets the way lookups leave the computer: VPN (the default, the safest) or Windows (decided 2026-11-07).
    /// </summary>
    public ExitRoute ExitRoute { get; init; } = ExitRoute.Vpn;

    /// <summary>Gets the currency amounts are counted in; Swedish kronor by default.</summary>
    public FiatCurrency Currency { get; init; } = FiatCurrency.Sek;

    /// <summary>
    /// Gets the crypto rate's resolution; an hour by default, since a block's time is only roughly right.
    /// </summary>
    public PriceResolution Resolution { get; init; } = PriceResolution.Hour;

    /// <summary>
    /// Gets whether the watching goes on when Chainsaw is closed, in the background process (decided 2026-11-07); on by
    /// default, and the user can turn it off.
    /// </summary>
    public bool WatchInBackground { get; init; } = true;

    /// <summary>Gets whether a hit is said with a notification in Windows; on by default.</summary>
    public bool AlertInWindows { get; init; } = true;

    /// <summary>Gets whether a hit is said with the coin swoosh; on by default.</summary>
    public bool AlertWithSound { get; init; } = true;

    /// <summary>
    /// Gets whether a hit is said with a message in the middle of the screen (sketch BV4a), which stays on top until it
    /// is closed; off by default, since it covers the user's work.
    /// </summary>
    public bool AlertOnScreen { get; init; }

    /// <summary>
    /// Gets whether an exit is checked once each time it connects (sketch UT3b): the trace is asked through it, and a
    /// country other than its place says is told in the toaster; on by default.
    /// </summary>
    public bool CheckExitOnConnect { get; init; } = true;

    /// <summary>
    /// Gets the names of the suppliers of attribution that are on, such as <c>chainabuse</c> (decided 2026-11-08, each
    /// with its own switch on the page Källor); none by default.
    /// </summary>
    public IReadOnlyList<string> AttributionProviders { get; init; } = [];

    /// <summary>Gets the ways a hit is said, as the background process reads them.</summary>
    public WatchAlerts Alerts =>
        (AlertInWindows ? WatchAlerts.Windows : WatchAlerts.None)
        | (AlertWithSound ? WatchAlerts.Sound : WatchAlerts.None)
        | (AlertOnScreen ? WatchAlerts.Screen : WatchAlerts.None);

    /// <summary>Gets the automatic lock's time, kept within the allowed range.</summary>
    public TimeSpan AutoLockTimeout =>
        TimeSpan.FromMinutes(Math.Clamp(AutoLockMinutes, MinimumAutoLockMinutes, MaximumAutoLockMinutes));

    /// <summary>
    /// Gives the settings with every value Chainsaw does not know replaced by its default, as a file changed by hand
    /// may hold: an unknown way out becomes VPN, never Windows.
    /// </summary>
    /// <returns>The settings, safe to use.</returns>
    public AppSettings Known()
    {
        var defaults = new AppSettings();
        return this with
        {
            ExitRoute = Enum.IsDefined(ExitRoute) ? ExitRoute : defaults.ExitRoute,
            Currency = Enum.IsDefined(Currency) ? Currency : defaults.Currency,
            Resolution = Enum.IsDefined(Resolution) ? Resolution : defaults.Resolution,
            AttributionProviders = AttributionProviders ?? defaults.AttributionProviders,
        };
    }
}
