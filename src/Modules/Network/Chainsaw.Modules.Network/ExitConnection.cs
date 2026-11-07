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
    /// <summary>How long the application's exit waits for a tunnel to stop before it leaves it to itself.</summary>
    private static readonly TimeSpan DisposeStopLimit = TimeSpan.FromSeconds(5);

    /// <summary>The exits in the vault.</summary>
    private readonly ExitRecords _records;

    /// <summary>Starts the tunnels.</summary>
    private readonly IExitConnector _connector;

    /// <summary>Builds the pipeline through a tunnel's proxy.</summary>
    private readonly IExitHandlerFactory _handlers;

    /// <summary>Lets one operation (connect, disconnect, a change to the exits) run at a time.</summary>
    private readonly SemaphoreSlim _operation = new(1, 1);

    /// <summary>Guards the state fields below; held only briefly, never across an await or a handler.</summary>
    private readonly Lock _gate = new();

    /// <summary>Guards who raises <see cref="StatusChanged"/>; never held while a handler runs.</summary>
    private readonly Lock _eventGate = new();

    /// <summary>The connected exit, or <see langword="null"/> when none is connected.</summary>
    private ActiveExit? _active;

    /// <summary>The current status.</summary>
    private ExitStatus _status = ExitStatus.NotConnected;

    /// <summary>Counts the changes of <see cref="_status"/>, so that events are raised in order and never stale.</summary>
    private long _version;

    /// <summary>The version last raised as an event; read and written by the thread that raises.</summary>
    private long _raisedVersion;

    /// <summary>Whether a thread is raising events now (guarded by <see cref="_eventGate"/>).</summary>
    private bool _raising;

    /// <summary>Whether a change came while a thread was raising (guarded by <see cref="_eventGate"/>).</summary>
    private bool _raisePending;

    /// <summary>Cancels the attempt to connect that is running, so that a disconnect never waits behind it.</summary>
    private CancellationTokenSource? _attempt;

    /// <summary>Whether the connection is disposed; a tunnel that is started afterwards is stopped at once.</summary>
    private bool _disposed;

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
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed(), this);
            var selected = _records.FindSelected() ?? throw new InvalidOperationException("No exit is selected.");
            await DisconnectCoreAsync().ConfigureAwait(false);
            lock (_gate)
            {
                _attempt = attempt;
            }

            var exitId = selected.Exit.Id;
            SetStatus(new ExitStatus(ExitState.Connecting, exitId, null));
            var tunnel = await StartTunnelAsync(selected, attempt.Token).ConfigureAwait(false);
            return tunnel is null ? Status : await PublishAsync(exitId, tunnel, attempt.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _attempt = null;
            }

            _operation.Release();
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        CancelAttempt();
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
    /// Changes the stored exits as one operation: an attempt to connect is cancelled, and the connected exit is
    /// disconnected first unless it may stay, so a request can never leave through an exit that is no longer
    /// selected.
    /// </summary>
    /// <typeparam name="T">What the change gives.</typeparam>
    /// <param name="leaveConnected">Tells whether the connected exit, by id, may stay connected.</param>
    /// <param name="change">The change to the vault.</param>
    /// <returns>What the change gave.</returns>
    public async Task<T> ChangeExitsAsync<T>(Func<Guid, bool> leaveConnected, Func<T> change)
    {
        CancelAttempt();
        await _operation.WaitAsync().ConfigureAwait(false);
        try
        {
            var status = Status;
            if (status.ExitId is { } id && status.State != ExitState.NotConnected && !leaveConnected(id))
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

    /// <summary>
    /// Stops the exit when the application ends. An attempt to connect is cancelled, and a tunnel that it starts
    /// anyway is stopped as soon as it is handed over. The wait for the tunnel to stop is bounded.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        CancelAttempt();
        var active = TakeActive(ExitStatus.NotConnected);
        if (active is not null)
        {
            active.Stop();

            // Off the caller's thread, so that a tunnel that awaits on a captured context cannot deadlock.
            Task.Run(active.StopTunnelAsync).Wait(DisposeStopLimit);
        }

        // The semaphore is not disposed: an operation in flight still releases it when it ends.
    }

    /// <summary>Starts the tunnel; a failure ends in <see cref="ExitState.Failed"/>, a cancellation in not connected.</summary>
    /// <param name="selected">The exit and its configuration.</param>
    /// <param name="cancellationToken">Stops the attempt.</param>
    /// <returns>The tunnel, or <see langword="null"/> when it failed.</returns>
    private async Task<IExitTunnel?> StartTunnelAsync(
        ExitWithConfiguration selected, CancellationToken cancellationToken)
    {
        var exitId = selected.Exit.Id;
        try
        {
            return await _connector.ConnectAsync(selected.Exit, selected.Configuration, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus(ExitStatus.NotConnected);
            throw;
        }
        catch (ExitConnectException exception)
        {
            SetStatus(new ExitStatus(ExitState.Failed, exitId, exception.Failure));
            return null;
        }
#pragma warning disable CA1031 // The connector is a plug-in; any failure of it is a failed connection.
        catch (Exception)
#pragma warning restore CA1031
        {
            SetStatus(new ExitStatus(ExitState.Failed, exitId, ExitFailure.ConnectFailed));
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
        if (cancellationToken.IsCancellationRequested || IsDisposed())
        {
            await ActiveExit.StopQuietlyAsync(tunnel).ConfigureAwait(false);
            SetStatus(ExitStatus.NotConnected);
            cancellationToken.ThrowIfCancellationRequested();
            throw new ObjectDisposedException(nameof(ExitConnection));
        }

        var invoker = CreatePipeline(tunnel);
        if (invoker is null)
        {
            await ActiveExit.StopQuietlyAsync(tunnel).ConfigureAwait(false);
            SetStatus(new ExitStatus(ExitState.Failed, exitId, ExitFailure.ConnectFailed));
            return Status;
        }

        var active = new ActiveExit(exitId, tunnel, invoker);
        bool published;
        lock (_gate)
        {
            published = !_disposed;
            if (published)
            {
                _active = active;
                _status = new ExitStatus(ExitState.Connected, exitId, null);
                _version++;
            }
        }

        if (!published)
        {
            active.Stop();
            await active.StopTunnelAsync().ConfigureAwait(false);
            throw new ObjectDisposedException(nameof(ExitConnection));
        }

        // Watched after it is published, so that a tunnel that has already stopped is noticed at once.
        active.Watch(() => OnTunnelStopped(active));
        RaiseLatest();
        return Status;
    }

    /// <summary>
    /// Builds the pipeline through a tunnel's proxy, which must be on the loopback address. The endpoint is copied, so
    /// that the tunnel cannot change it after it has been checked.
    /// </summary>
    /// <param name="tunnel">The started tunnel.</param>
    /// <returns>The pipeline, or <see langword="null"/> when the proxy is refused or the pipeline cannot be built.</returns>
    private HttpMessageInvoker? CreatePipeline(IExitTunnel tunnel)
    {
        try
        {
            var given = tunnel.Socks5Proxy;
            var credentials = tunnel.Socks5Credentials;
            if (given is null || credentials is null)
            {
                return null;
            }

            var proxy = new IPEndPoint(CopyOf(given.Address), given.Port);
            var copied = new NetworkCredential(credentials.UserName, credentials.Password);
            var acceptable = IPAddress.IsLoopback(proxy.Address)
                && copied.UserName.Length > 0
                && copied.Password.Length > 0;
            return acceptable ? new HttpMessageInvoker(_handlers.Create(proxy, copied), disposeHandler: true) : null;
        }
#pragma warning disable CA1031 // A tunnel whose proxy cannot be read or used is a failed connection.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>Copies an address, so that a change to the original cannot reach the copy.</summary>
    /// <param name="address">The address.</param>
    /// <returns>An equal, separate address.</returns>
    private static IPAddress CopyOf(IPAddress address) =>
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? new IPAddress(address.GetAddressBytes(), address.ScopeId)
            : new IPAddress(address.GetAddressBytes());

    /// <summary>Stops the exit when its tunnel stops on its own; the state becomes failed with the connection lost.</summary>
    /// <param name="active">The exit whose tunnel stopped.</param>
    private void OnTunnelStopped(ActiveExit active)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_active, active))
            {
                return;
            }

            _active = null;
            _status = new ExitStatus(ExitState.Failed, active.ExitId, ExitFailure.ConnectionLost);
            _version++;
        }

        active.Stop();
        _ = active.StopTunnelAsync();
        RaiseLatest();
    }

    /// <summary>Disconnects the connected exit, if any; the caller holds the operation gate.</summary>
    /// <returns>A task that completes when the tunnel has stopped.</returns>
    private async Task DisconnectCoreAsync()
    {
        var active = TakeActive(ExitStatus.NotConnected);
        try
        {
            if (active is not null)
            {
                active.Stop();
                await active.StopTunnelAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            RaiseLatest();
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
            if (!Equals(_status, status))
            {
                _status = status;
                _version++;
            }

            return active;
        }
    }

    /// <summary>Sets the status when no exit is connected and raises the event.</summary>
    /// <param name="status">The new status.</param>
    private void SetStatus(ExitStatus status)
    {
        lock (_gate)
        {
            if (Equals(_status, status))
            {
                return;
            }

            _status = status;
            _version++;
        }

        RaiseLatest();
    }

    /// <summary>
    /// Raises <see cref="StatusChanged"/> with the latest status, one thread at a time and in order: a status that is
    /// already replaced is never raised after its replacement, and the last change is always raised. A handler that
    /// throws does not affect the connection; its exception is dropped, since the state is already changed.
    /// </summary>
    private void RaiseLatest()
    {
        lock (_eventGate)
        {
            if (_raising)
            {
                _raisePending = true;
                return;
            }

            _raising = true;
        }

        try
        {
            while (true)
            {
                ExitStatus status;
                long version;
                lock (_gate)
                {
                    status = _status;
                    version = _version;
                }

                if (version > _raisedVersion)
                {
                    _raisedVersion = version;
                    InvokeHandlers(status);
                }

                lock (_eventGate)
                {
                    if (!_raisePending)
                    {
                        _raising = false;
                        return;
                    }

                    _raisePending = false;
                }
            }
        }
        catch
        {
            lock (_eventGate)
            {
                _raising = false;
            }

            throw;
        }
    }

    /// <summary>Calls each handler of <see cref="StatusChanged"/>, keeping one handler's failure from the others.</summary>
    /// <param name="status">The status to report.</param>
    private void InvokeHandlers(ExitStatus status)
    {
        foreach (var handler in StatusChanged?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler<ExitStatus>)handler).Invoke(this, status);
            }
#pragma warning disable CA1031 // A display that fails must not change or stop the connection (see RaiseLatest).
            catch (Exception)
#pragma warning restore CA1031
            {
                // Dropped; see the summary of RaiseLatest.
            }
        }
    }

    /// <summary>Cancels the attempt to connect that is running, if any.</summary>
    private void CancelAttempt()
    {
        CancellationTokenSource? attempt;
        lock (_gate)
        {
            attempt = _attempt;
        }

        try
        {
            attempt?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The attempt has just ended.
        }
    }

    /// <summary>Tells whether the connection is disposed.</summary>
    /// <returns><see langword="true"/> after <see cref="Dispose"/>.</returns>
    private bool IsDisposed()
    {
        lock (_gate)
        {
            return _disposed;
        }
    }
}
