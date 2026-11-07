using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The way lookups leave the computer as the shell shows it: the way chosen in the settings (VPN or Windows), the
/// exit's status and whether the computer has a network, which together say whether anything can be looked up now
/// (principle 9), and the chains whose sources are off in the settings (sketch KL1a). Changes are told on the UI thread. It knows nothing of WPF.
/// </summary>
public sealed class ExitWay
{
    /// <summary>What the status line says while a lookup waits for a source; the chains follow.</summary>
    public const string WaitingText = "Väntar på källan";

    /// <summary>The way chosen in the settings.</summary>
    private readonly IExitRoute _route;

    /// <summary>The chains whose sources are off.</summary>
    private readonly HashSet<Chain> _off = [];

    /// <summary>The chains whose lookups wait for a source now.</summary>
    private readonly HashSet<Chain> _waiting = [];

    /// <summary>Creates the way and follows the choice and the exit.</summary>
    /// <param name="route">The way chosen in the settings.</param>
    /// <param name="connection">The connection to the exit (VPN).</param>
    /// <param name="ui">The UI thread, where changes are told.</param>
    /// <param name="sources">
    /// The chains' endpoints, whose mode Av and whose waits for a source are followed; none when
    /// <see langword="null"/>.
    /// </param>
    /// <param name="network">
    /// Whether the computer has a network, which Windows as the way out needs; always online when
    /// <see langword="null"/>.
    /// </param>
    public ExitWay(
        IExitRoute route,
        IExitConnection connection,
        IUiThread ui,
        IChainSources? sources = null,
        INetworkPresence? network = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(ui);
        _route = route;

        // Followed first and read after, so that a change in between is never lost.
        connection.StatusChanged += (_, status) => ui.Post(() =>
        {
            Status = status;
            Changed?.Invoke(this, EventArgs.Empty);
        });
        route.Changed += (_, _) => ui.Post(() =>
        {
            Route = _route.Current;
            Changed?.Invoke(this, EventArgs.Empty);
        });
        if (network is not null)
        {
            network.Changed += (_, available) => ui.Post(() =>
            {
                if (IsOnline != available)
                {
                    IsOnline = available;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            });
        }

        if (sources is not null)
        {
            sources.Changed += (_, chain) => ui.Post(() =>
            {
                if (Follow(sources, chain))
                {
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            });
            foreach (var chain in sources.Chains)
            {
                Follow(sources, chain);
            }
        }

        Status = connection.Status;
        Route = route.Current;
        IsOnline = network?.IsAvailable ?? true;
    }

    /// <summary>Raised on the UI thread after the way or the exit's status has changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the way chosen, as last told on the UI thread.</summary>
    public ExitRoute Route { get; private set; }

    /// <summary>Gets the exit's status, as last told on the UI thread.</summary>
    public ExitStatus Status { get; private set; }

    /// <summary>Gets whether Windows is the way out.</summary>
    public bool IsWindows => Route == ExitRoute.Windows;

    /// <summary>Gets whether the computer has a network, as last told on the UI thread.</summary>
    public bool IsOnline { get; private set; }

    /// <summary>
    /// Gets whether Windows is the way out and the computer has no network, so that it is shown as <em>Offline</em>.
    /// </summary>
    public bool IsWindowsOffline => IsWindows && !IsOnline;

    /// <summary>Gets the chains whose sources are off (<see cref="SourceMode.Off"/>), as last told.</summary>
    public IReadOnlySet<Chain> ChainsOff => _off;

    /// <summary>
    /// Gets the chains whose lookups wait for a source now (its pace, or a wait it asked for), as last told, so that
    /// the wait is shown honestly while the UI goes on.
    /// </summary>
    public IReadOnlySet<Chain> ChainsWaiting => _waiting;

    /// <summary>
    /// Gets what the status line says while a lookup waits for a source, such as <c>Väntar på källan · Bitcoin</c>, or
    /// empty when none waits.
    /// </summary>
    public string WaitText => _waiting.Count == 0
        ? string.Empty
        : WaitingText + " · " + string.Join(", ", _waiting.Order().Select(ChainName));

    /// <summary>
    /// Gets whether lookups can go now: through Windows while the computer has a network, or through a connected exit.
    /// </summary>
    public bool IsLive => IsWindows ? IsOnline : Status.State == ExitState.Connected;

    /// <summary>
    /// Chooses the way out in Network at once, so that the next lookup takes it, and tells the change. Leaving Windows
    /// stops the lookups in flight through it.
    /// </summary>
    /// <param name="route">The way out.</param>
    public void Choose(ExitRoute route)
    {
        _route.Choose(route);
        if (Route != route)
        {
            Route = route;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gives a chain's name as the status line writes it.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>For example <c>Bitcoin</c> or <c>TRON</c>.</returns>
    private static string ChainName(Chain chain) => chain == Chain.Tron ? "TRON" : chain.ToString();

    /// <summary>Reads whether a chain's sources are off, and whether its lookups wait for a source.</summary>
    /// <param name="sources">The chains' endpoints.</param>
    /// <param name="chain">The chain.</param>
    /// <returns><see langword="true"/> when either changed.</returns>
    private bool Follow(IChainSources sources, Chain chain)
    {
        var view = sources.Get(chain);
        var off = view.Mode == SourceMode.Off ? _off.Add(chain) : _off.Remove(chain);
        var waiting = view.WaitingUntil is not null ? _waiting.Add(chain) : _waiting.Remove(chain);
        return off || waiting;
    }
}
