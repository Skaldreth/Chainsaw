using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>
/// The way out chosen in the settings, and the pipeline through Windows. A request takes the way and, through Windows,
/// a token that is cancelled when Windows is left, in one step under the same lock that changes the way, so a request
/// can never leave through Windows after VPN has been chosen.
/// </summary>
internal sealed class ExitRouteChoice : IExitRoute, IDisposable
{
    /// <summary>Guards the way, the pipeline and the token.</summary>
    private readonly Lock _gate = new();

    /// <summary>Builds the pipeline through Windows when it is first needed.</summary>
    private readonly IWindowsHandlerFactory _handlers;

    /// <summary>The way chosen now.</summary>
    private ExitRoute _route;

    /// <summary>The pipeline through Windows, built when Windows is first used; <see langword="null"/> before.</summary>
    private HttpMessageInvoker? _windows;

    /// <summary>Cancelled when Windows is left, which stops the requests in flight through it.</summary>
    private CancellationTokenSource _stopping = new();

    /// <summary>Whether the module has been disposed, after which nothing leaves through Windows.</summary>
    private bool _disposed;

    /// <summary>Creates the choice with the way chosen when Chainsaw started.</summary>
    /// <param name="options">The settings, with the way out read from the application's settings.</param>
    /// <param name="handlers">Builds the pipeline through Windows.</param>
    public ExitRouteChoice(NetworkOptions options, IWindowsHandlerFactory handlers)
    {
        _route = options.Route;
        _handlers = handlers;
    }

    /// <inheritdoc />
    public event EventHandler<ExitRoute>? Changed;

    /// <inheritdoc />
    public ExitRoute Current
    {
        get
        {
            lock (_gate)
            {
                return _route;
            }
        }
    }

    /// <inheritdoc />
    public void Choose(ExitRoute route)
    {
        if (!Enum.IsDefined(route))
        {
            throw new ArgumentOutOfRangeException(nameof(route), route, "Unknown way out.");
        }

        CancellationTokenSource? left = null;
        HttpMessageInvoker? pipeline = null;
        lock (_gate)
        {
            if (_route == route)
            {
                return;
            }

            if (_route == ExitRoute.Windows)
            {
                left = _stopping;
                _stopping = new CancellationTokenSource();

                // The pipeline goes too, with its idle connections; Windows builds a new one when chosen again.
                pipeline = _windows;
                _windows = null;
            }

            _route = route;
        }

        // Cancelled outside the lock: the requests' own callbacks run on it.
        left?.Cancel();
        left?.Dispose();
        pipeline?.Dispose();
        Raise(route);
    }

    /// <summary>Takes what one request through Windows needs, when Windows is the way chosen now.</summary>
    /// <param name="lease">The pipeline and the token that stops it, when Windows is chosen.</param>
    /// <returns><see langword="true"/> when Windows is the way chosen.</returns>
    public bool TryLeaseWindows(out ExitLease lease)
    {
        lock (_gate)
        {
            if (_route == ExitRoute.Windows && !_disposed)
            {
                _windows ??= new HttpMessageInvoker(_handlers.Create(), disposeHandler: true);
                lease = new ExitLease(Guid.Empty, _windows, _stopping.Token, ExitRoute.Windows);
                return true;
            }
        }

        lease = default;
        return false;
    }

    /// <summary>Stops the requests through Windows and frees the pipeline when the application ends.</summary>
    public void Dispose()
    {
        HttpMessageInvoker? windows;
        CancellationTokenSource stopping;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            windows = _windows;
            _windows = null;
            stopping = _stopping;
        }

        stopping.Cancel();
        stopping.Dispose();
        windows?.Dispose();
    }

    /// <summary>Tells the listeners, one at a time; a listener that throws is passed over.</summary>
    /// <param name="route">The way chosen.</param>
    private void Raise(ExitRoute route)
    {
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler<ExitRoute>)handler)(this, route);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A listener's failure never changes the choice.
            }
        }
    }
}
