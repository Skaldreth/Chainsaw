using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Watcher;

/// <summary>
/// Who the background process says fetched what it fetches (principle 1): itself, never an operator, whose profile is
/// in the vault it never opens.
/// </summary>
internal sealed class BackgroundOperator : IOperatorProfileStore
{
    /// <inheritdoc/>
    public OperatorProfile? Get() => BackgroundNames.Fetcher;

    /// <inheritdoc/>
    public void Save(OperatorProfile profile) => throw new NotSupportedException("The background has no profile.");
}

/// <summary>
/// The vault's named secrets as the background sees them: always locked. The API keys are in the vault, so the
/// background can never use them; it never asks for the master key. The exits it may use come from their own file
/// (<c>IBackgroundExits</c>), never from here.
/// </summary>
internal sealed class LockedSecrets : IVaultSecrets
{
    /// <inheritdoc/>
    public IReadOnlyList<string> List(string prefix) => throw Locked();

    /// <inheritdoc/>
    public string? Find(string name) => throw Locked();

    /// <inheritdoc/>
    public void Save(string name, string value) => throw Locked();

    /// <inheritdoc/>
    public bool Remove(string name) => throw Locked();

    /// <summary>The error: the vault is locked.</summary>
    /// <returns>The error.</returns>
    private static VaultException Locked() => new(VaultError.Locked);
}

/// <summary>
/// Whether the background may look anything up (principles 4 and 9): through Windows when the plan says so, through VPN
/// only while the exit the user allowed in the background is connected, and never for a chain whose sources are off.
/// The way out is the plan's, set by the loop.
/// </summary>
/// <param name="sources">The chains' sources.</param>
/// <param name="exit">The connection to the exit allowed in the background.</param>
internal sealed class WatcherGate(IChainSources sources, IExitConnection exit) : IWatchGate
{
    /// <summary>Gets or sets the way out chosen in the plan; VPN until the plan is read.</summary>
    public ExitRoute Route { get; set; } = ExitRoute.Vpn;

    /// <inheritdoc/>
    public WatchGate For(Chain chain)
    {
        var through = Route == ExitRoute.Windows || exit.Status.State == ExitState.Connected;
        if (!through)
        {
            return WatchGate.NoExit;
        }

        return !sources.Chains.Contains(chain) || sources.Get(chain).Mode == SourceMode.Off
            ? WatchGate.ChainOff
            : WatchGate.Open;
    }
}
