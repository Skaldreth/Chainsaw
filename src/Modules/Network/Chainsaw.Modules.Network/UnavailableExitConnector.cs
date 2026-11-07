using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>
/// The connector until Chainsaw's own WireGuard tunnel is built: it says honestly that the tunnel is not available, so
/// connecting ends in a failed state and every request is refused.
/// </summary>
internal sealed class UnavailableExitConnector : IExitConnector
{
    /// <inheritdoc />
    public Task<IExitTunnel> ConnectAsync(ExitInfo exit, string configuration, CancellationToken cancellationToken) =>
        Task.FromException<IExitTunnel>(
            new ExitConnectException(ExitFailure.TunnelUnavailable, "The exit's tunnel is not built yet."));
}
