using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring;

/// <summary>
/// Says whether the watches may look anything up (principles 4 and 9): never while Chainsaw is locked, never when VPN
/// is the way out and no exit is connected or Windows is the way out and the computer does not reach the internet
/// (both <see cref="WatchGate.NoExit"/>: nothing can leave), and never for a chain whose sources are off.
/// </summary>
/// <param name="vault">The vault, whose lock stops every lookup.</param>
/// <param name="route">The way out chosen in the settings.</param>
/// <param name="connection">The connection to the exit.</param>
/// <param name="sources">The chains' sources, whose mode can be off.</param>
/// <param name="presence">
/// Whether the computer reaches the internet, which Windows as the way out needs; always when <see langword="null"/>.
/// </param>
internal sealed class MonitoringGate(
    IVault vault,
    IExitRoute route,
    IExitConnection connection,
    IChainSources sources,
    INetworkPresence? presence = null) : IWatchGate
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

        if (route.Current == ExitRoute.Windows && presence is { IsAvailable: false })
        {
            return WatchGate.NoExit;
        }

        return !sources.Chains.Contains(chain) || sources.Get(chain).Mode == SourceMode.Off
            ? WatchGate.ChainOff
            : WatchGate.Open;
    }
}
