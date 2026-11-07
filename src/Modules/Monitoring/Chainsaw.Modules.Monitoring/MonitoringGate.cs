using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring;

/// <summary>
/// Says whether the watches may look anything up (principles 4 and 9): never while Chainsaw is locked, never when VPN
/// is the way out and no exit is connected, and never for a chain whose sources are off.
/// </summary>
/// <param name="vault">The vault, whose lock stops every lookup.</param>
/// <param name="route">The way out chosen in the settings.</param>
/// <param name="connection">The connection to the exit.</param>
/// <param name="sources">The chains' sources, whose mode can be off.</param>
internal sealed class MonitoringGate(
    IVault vault, IExitRoute route, IExitConnection connection, IChainSources sources) : IWatchGate
{
    /// <inheritdoc/>
    public WatchGate For(Chain chain)
    {
        if (vault.State != VaultState.Unlocked)
        {
            return WatchGate.Locked;
        }

        if (route.Current == ExitRoute.Vpn && connection.Status.State != ExitState.Connected)
        {
            return WatchGate.NoExit;
        }

        return !sources.Chains.Contains(chain) || sources.Get(chain).Mode == SourceMode.Off
            ? WatchGate.ChainOff
            : WatchGate.Open;
    }
}
