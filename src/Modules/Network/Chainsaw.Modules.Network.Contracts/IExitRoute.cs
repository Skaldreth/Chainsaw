namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// The way out chosen in the settings: <see cref="ExitRoute.Vpn"/> (the default) or <see cref="ExitRoute.Windows"/>.
/// <see cref="IExitHttp"/> reads it for every request, so a change applies to the next request; requests in flight
/// through Windows are stopped when Windows is left.
/// </summary>
public interface IExitRoute
{
    /// <summary>Gets the way out chosen now.</summary>
    ExitRoute Current { get; }

    /// <summary>
    /// Raised after <see cref="Current"/> has changed, on the thread that changed it. A handler that throws does not
    /// affect the choice.
    /// </summary>
    event EventHandler<ExitRoute>? Changed;

    /// <summary>
    /// Chooses the way out. Leaving <see cref="ExitRoute.Windows"/> stops every request still in flight through it
    /// before this returns, so that nothing leaves around the exit once VPN is chosen. Choosing the same way again is
    /// harmless. A connected exit (VPN) stays connected either way.
    /// </summary>
    /// <param name="route">The way out.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a known way out.</exception>
    void Choose(ExitRoute route);
}
