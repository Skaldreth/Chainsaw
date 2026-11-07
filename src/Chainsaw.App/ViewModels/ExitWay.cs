using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The way lookups leave the computer as the shell shows it: the way chosen in the settings (VPN or Windows) and the
/// exit's status, which together say whether anything can be looked up now (principle 9). Changes are told on the UI
/// thread. It knows nothing of WPF.
/// </summary>
public sealed class ExitWay
{
    /// <summary>The way chosen in the settings.</summary>
    private readonly IExitRoute _route;

    /// <summary>Creates the way and follows the choice and the exit.</summary>
    /// <param name="route">The way chosen in the settings.</param>
    /// <param name="connection">The connection to the exit (VPN).</param>
    /// <param name="ui">The UI thread, where changes are told.</param>
    public ExitWay(IExitRoute route, IExitConnection connection, IUiThread ui)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(ui);
        _route = route;
        Status = connection.Status;
        Route = route.Current;
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
    }

    /// <summary>Raised on the UI thread after the way or the exit's status has changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the way chosen, as last told on the UI thread.</summary>
    public ExitRoute Route { get; private set; }

    /// <summary>Gets the exit's status, as last told on the UI thread.</summary>
    public ExitStatus Status { get; private set; }

    /// <summary>Gets whether Windows is the way out.</summary>
    public bool IsWindows => Route == ExitRoute.Windows;

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
}
