namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// Which way Chainsaw's lookups leave the computer, chosen in the settings (decided 2026-11-07). Only https in both.
/// </summary>
public enum ExitRoute
{
    /// <summary>
    /// Only through a connected exit (VPN) in Chainsaw; without one no lookup is made (principle 4). The default, since
    /// it is the safest.
    /// </summary>
    Vpn,

    /// <summary>
    /// Through the computer's own network, with the proxy and protection installed for the whole computer. Still only
    /// https, without cookies, referer or fetches of certificates around the request.
    /// </summary>
    Windows,
}

/// <summary>The ways out as they are written where they are kept.</summary>
public static class ExitRoutes
{
    /// <summary>Gives a way out's code, written with every sealed response and in the settings' file.</summary>
    /// <param name="route">The way out.</param>
    /// <returns><c>vpn</c> or <c>windows</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a known way out.</exception>
    public static string Code(this ExitRoute route) => route switch
    {
        ExitRoute.Vpn => "vpn",
        ExitRoute.Windows => "windows",
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Unknown way out."),
    };
}
