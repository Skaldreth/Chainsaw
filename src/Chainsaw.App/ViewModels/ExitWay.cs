using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The way lookups leave the computer as the shell shows it: the way chosen in the settings (VPN or Windows) and the
/// exit's status, which together say whether anything can be looked up now (principle 9), and the chains whose sources
/// are off in the settings (sketch KL1a). Changes are told on the UI thread. It knows nothing of WPF.
/// </summary>
public sealed class ExitWay
{
    /// <summary>The way chosen in the settings.</summary>
    private readonly IExitRoute _route;

    /// <summary>The chains whose sources are off.</summary>
    private readonly HashSet<Chain> _off = [];

    /// <summary>Creates the way and follows the choice and the exit.</summary>
    /// <param name="route">The way chosen in the settings.</param>
    /// <param name="connection">The connection to the exit (VPN).</param>
    /// <param name="ui">The UI thread, where changes are told.</param>
    /// <param name="sources">The chains' endpoints, whose mode Av is followed; none when <see langword="null"/>.</param>
    public ExitWay(IExitRoute route, IExitConnection connection, IUiThread ui, IChainSources? sources = null)
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
    }

    /// <summary>Raised on the UI thread after the way or the exit's status has changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the way chosen, as last told on the UI thread.</summary>
    public ExitRoute Route { get; private set; }

    /// <summary>Gets the exit's status, as last told on the UI thread.</summary>
    public ExitStatus Status { get; private set; }

    /// <summary>Gets whether Windows is the way out.</summary>
    public bool IsWindows => Route == ExitRoute.Windows;

    /// <summary>Gets the chains whose sources are off (<see cref="SourceMode.Off"/>), as last told on the UI thread.</summary>
    public IReadOnlySet<Chain> ChainsOff => _off;

    /// <summary>Gets whether lookups can go now: through Windows, or through a connected exit.</summary>
    public bool IsLive => IsWindows || Status.State == ExitState.Connected;

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

    /// <summary>Reads whether a chain's sources are off.</summary>
    /// <param name="sources">The chains' endpoints.</param>
    /// <param name="chain">The chain.</param>
    /// <returns><see langword="true"/> when that changed.</returns>
    private bool Follow(IChainSources sources, Chain chain) =>
        sources.Get(chain).Mode == SourceMode.Off ? _off.Add(chain) : _off.Remove(chain);
}
