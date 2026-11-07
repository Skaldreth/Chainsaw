namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// Starts the tunnel for an exit. The tunnel itself (Chainsaw's own copy of the WireGuard helper) plugs in here: it
/// takes the configuration and exposes a SOCKS5 proxy on the computer's loopback address, through which
/// <see cref="IExitHttp"/> sends every request. Until a tunnel is registered, connecting fails with
/// <see cref="ExitFailure.TunnelUnavailable"/>.
/// </summary>
public interface IExitConnector
{
    /// <summary>Starts the tunnel for an exit.</summary>
    /// <param name="exit">The exit.</param>
    /// <param name="configuration">The exit's configuration from the vault; it must never be logged.</param>
    /// <param name="cancellationToken">Stops the attempt.</param>
    /// <returns>The running tunnel, which the caller disposes to stop it.</returns>
    /// <exception cref="ExitConnectException">The tunnel could not be started.</exception>
    Task<IExitTunnel> ConnectAsync(ExitInfo exit, string configuration, CancellationToken cancellationToken);
}
