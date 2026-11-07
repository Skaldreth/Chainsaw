using System.Net;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>
/// The program's connection to its selected exit, and the only holder of the pipeline that requests leave through.
/// Connecting, disconnecting and changing the exits run one at a time; reading the state and taking a lease for a
/// request never wait for them.
/// </summary>
internal sealed class ExitConnection : IExitConnection, IDisposable
{
    /// <summary>The exits in the vault.</summary>
    private readonly ExitRecords _records;

    /// <summary>Starts the tunnels.</summary>
    private readonly IExitConnector _connector;

    /// <summary>Builds the pipeline through a tunnel's proxy.</summary>
    private readonly IExitHandlerFactory _handlers;

    /// <summary>Lets one operation (connect, disconnect, a change to the exits) run at a time.</summary>
    private readonly SemaphoreSlim _operation = new(1, 1);

    /// <summary>Guards <see cref="_active"/> and <see cref="_status"/>; held only briefly, never across an await.</summary>
    private readonly Lock _gate = new();

    /// <summary>The connected exit, or <see langword="null"/> when none is connected.</summary>
    private ActiveExit? _active;

    /// <summary>The current status.</summary>
    private ExitStatus _status = ExitStatus.NotConnected;

    /// <summary>Creates the connection, not connected.</summary>
    /// <param name="records">The exits in the vault.</param>
    /// <param name="connector">Starts the tunnels.</param>
    /// <param name="handlers">Builds the pipeline through a tunnel's proxy.</param>
    public ExitConnection(ExitRecords records, IExitConnector connector, IExitHandlerFactory handlers)
    {
        _records = records;
        _connector = connector;
        _handlers = handlers;
    }

    /// <inheritdoc />
    public event EventHandler<ExitStatus>? StatusChanged;

    /// <inheritdoc />
    public ExitStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    /// <inheritdoc />
    public async Task<ExitStatus> ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (exit, configuration) = _records.FindSelected()
                ?? throw new InvalidOperationException("No exit is selected.");
            await DisconnectCoreAsync().ConfigureAwait(false);
            SetStatus(new ExitStatus(ExitState.Connecting, exit.Id, null));
            var tunnel = await StartTunnelAsync(exit, configuration, cancellationToken).ConfigureAwait(false);
            return tunnel is null ? Status : await PublishAsync(exit.Id, tunnel, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operation.Release();
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        await _operation.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _operation.Release();
        }
    }

    /// <summary>
    /// Changes the stored exits as one operation: the connected exit is disconnected first unless it may stay, so a
    /// request can never leave through an exit that is no longer selected.
    /// </summary>
    /// <typeparam name="T">What the change gives.</typeparam>
    /// <param name="leaveConnected">Tells whether the connected exit, by id, may stay connected.</param>
    /// <param name="change">The change to the vault.</param>
    /// <returns>What the change gave.</returns>
    public async Task<T> ChangeExitsAsync<T>(Func<Guid, bool> leaveConnected, Func<T> change)
    {
        await _operation.WaitAsync().ConfigureAwait(false);
        try
        {
            var connectedId = Status.ExitId;
            if (connectedId is { } id && Status.State != ExitState.NotConnected && !leaveConnected(id))
            {
                await DisconnectCoreAsync().ConfigureAwait(false);
            }

            return change();
        }
        finally
        {
            _operation.Release();
        }
    }

    /// <summary>Takes what a request needs from the connected exit.</summary>
    /// <param name="lease">The exit's id, pipeline and stopping token when connected.</param>
    /// <returns><see langword="true"/> when an exit is connected.</returns>
    public bool TryLease(out ExitLease lease)
    {
        lock (_gate)
        {
            if (_active is { } active && _status.State == ExitState.Connected)
            {
                lease = new ExitLease(active.ExitId, active.Invoker, active.Stopping);
                return true;
            }
        }

        lease = default;
        return false;
    }

    /// <summary>Stops the exit when the application ends.</summary>
    public void Dispose()
    {
        var active = TakeActive(ExitStatus.NotConnected);
        if (active is not null)
        {
            active.Stop();

            // Off the caller's thread, so that a tunnel that awaits on a captured context cannot deadlock.
            Task.Run(active.StopTunnelAsync).GetAwaiter().GetResult();
        }

        _operation.Dispose();
    }

    /// <summary>Starts the tunnel; a failure ends in <see cref="ExitState.Failed"/>, a cancellation in not connected.</summary>
    /// <param name="exit">The exit.</param>
    /// <param name="configuration">Its configuration.</param>
    /// <param name="cancellationToken">Stops the attempt.</param>
    /// <returns>The tunnel, or <see langword="null"/> when it failed.</returns>
    private async Task<IExitTunnel?> StartTunnelAsync(
        ExitInfo exit, string configuration, CancellationToken cancellationToken)
    {
        try
        {
            return await _connector.ConnectAsync(exit, configuration, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus(ExitStatus.NotConnected);
            throw;
        }
        catch (ExitConnectException exception)
        {
            SetStatus(new ExitStatus(ExitState.Failed, exit.Id, exception.Failure));
            return null;
        }
#pragma warning disable CA1031 // The connector is a plug-in; any failure of it is a failed connection.
        catch (Exception)
#pragma warning restore CA1031
        {
            SetStatus(new ExitStatus(ExitState.Failed, exit.Id, ExitFailure.ConnectFailed));
            return null;
        }
    }

    /// <summary>
    /// Checks the tunnel and makes it the connected exit with a pipeline through its proxy. A proxy that is not on the
    /// loopback address is refused, so that the proxy itself is never reached over the computer's network.
    /// </summary>
    /// <param name="exitId">The exit.</param>
    /// <param name="tunnel">The started tunnel.</param>
    /// <param name="cancellationToken">The attempt's token; a cancellation now stops the tunnel again.</param>
    /// <returns>The status after the attempt.</returns>
    private async Task<ExitStatus> PublishAsync(Guid exitId, IExitTunnel tunnel, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            await ActiveExit.StopQuietlyAsync(tunnel).ConfigureAwait(false);
            SetStatus(ExitStatus.NotConnected);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var invoker = CreatePipeline(tunnel);
        if (invoker is null)
        {
            await ActiveExit.StopQuietlyAsync(tunnel).ConfigureAwait(false);
            SetStatus(new ExitStatus(ExitState.Failed, exitId, ExitFailure.ConnectFailed));
            return Status;
        }

        var active = new ActiveExit(exitId, tunnel, invoker);
        var connected = new ExitStatus(ExitState.Connected, exitId, null);
        lock (_gate)
        {
            _active = active;
            _status = connected;
        }

        // Watched after it is published, so that a tunnel that has already stopped is noticed at once.
        active.Watch(() => OnTunnelStopped(active));
        if (ReferenceEquals(Status, connected))
        {
            StatusChanged?.Invoke(this, connected);
        }

        return Status;
    }

    /// <summary>Builds the pipeline through a tunnel's proxy, which must be on the loopback address.</summary>
    /// <param name="tunnel">The started tunnel.</param>
    /// <returns>The pipeline, or <see langword="null"/> when the proxy is refused or the pipeline cannot be built.</returns>
    private HttpMessageInvoker? CreatePipeline(IExitTunnel tunnel)
    {
        try
        {
            var proxy = tunnel.Socks5Proxy;
            return proxy is not null && IPAddress.IsLoopback(proxy.Address)
                ? new HttpMessageInvoker(_handlers.Create(proxy), disposeHandler: true)
                : null;
        }
#pragma warning disable CA1031 // A tunnel whose proxy cannot be read or used is a failed connection.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>Stops the exit when its tunnel stops on its own; the state becomes failed with the connection lost.</summary>
    /// <param name="active">The exit whose tunnel stopped.</param>
    private void OnTunnelStopped(ActiveExit active)
    {
        var failed = new ExitStatus(ExitState.Failed, active.ExitId, ExitFailure.ConnectionLost);
        lock (_gate)
        {
            if (!ReferenceEquals(_active, active))
            {
                return;
            }

            _active = null;
            _status = failed;
        }

        active.Stop();
        _ = active.StopTunnelAsync();
        StatusChanged?.Invoke(this, failed);
    }

    /// <summary>Disconnects the connected exit, if any; the caller holds the operation gate.</summary>
    /// <returns>A task that completes when the tunnel has stopped.</returns>
    private async Task DisconnectCoreAsync()
    {
        var wasConnectedOrFailed = Status.State != ExitState.NotConnected;
        var active = TakeActive(ExitStatus.NotConnected);
        active?.Stop();
        if (wasConnectedOrFailed)
        {
            StatusChanged?.Invoke(this, ExitStatus.NotConnected);
        }

        if (active is not null)
        {
            await active.StopTunnelAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Takes the connected exit out of the state and sets a new status, in one step.</summary>
    /// <param name="status">The new status.</param>
    /// <returns>The exit that was connected, or <see langword="null"/>.</returns>
    private ActiveExit? TakeActive(ExitStatus status)
    {
        lock (_gate)
        {
            var active = _active;
            _active = null;
            _status = status;
            return active;
        }
    }

    /// <summary>Sets the status when no exit is connected and raises the event.</summary>
    /// <param name="status">The new status.</param>
    private void SetStatus(ExitStatus status)
    {
        lock (_gate)
        {
            _status = status;
        }

        StatusChanged?.Invoke(this, status);
    }
}
