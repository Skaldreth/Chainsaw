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
    /// <summary>How long an operation or the application's exit waits for a tunnel to stop before it leaves it.</summary>
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

    /// <summary>
    /// Counts the changes of <see cref="_status"/>, so that events are raised in order and never stale.
    /// </summary>
    private long _version;

    /// <summary>The version last raised as an event; read and written by the thread that raises.</summary>
    private long _raisedVersion;

    /// <summary>Whether a thread is raising events now (guarded by <see cref="_eventGate"/>).</summary>
    private bool _raising;

    /// <summary>Whether a change came while a thread was raising (guarded by <see cref="_eventGate"/>).</summary>
    private bool _raisePending;

    /// <summary>
    /// The attempts to connect that have begun, also those still waiting for their turn, so that a disconnect, a
    /// change of exit or <see cref="Dispose"/> cancels every one of them and never waits behind one (guarded by
    /// <see cref="_gate"/>).
    /// </summary>
    private readonly HashSet<CancellationTokenSource> _attempts = [];

    /// <summary>The tunnel stops still running on the thread pool (guarded by <see cref="_gate"/>).</summary>
    private readonly HashSet<Task> _pendingStops = [];

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
        // Registered before the wait for the turn, so that a disconnect that comes at any later point cancels it.
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _attempts.Add(attempt);
        }

        try
        {
            await _operation.WaitAsync(attempt.Token).ConfigureAwait(false);
            try
            {
                return await ConnectInTurnAsync(attempt.Token).ConfigureAwait(false);
            }
            finally
            {
                _operation.Release();
            }
        }
        finally
        {
            lock (_gate)
            {
                _attempts.Remove(attempt);
            }
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
        // An attempt connects the selected exit, so it is cancelled only when that exit may not stay.
        if (_records.SelectedId() is not { } selected || !leaveConnected(selected))
        {
            CancelAttempt();
        }

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
                lease = new ExitLease(active.ExitId, active.Invoker, active.Stopping, ExitRoute.Vpn);
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
            try
            {
                active.Stop();
            }
            finally
            {
                // Off the caller's thread, so that a tunnel that awaits on a captured context cannot deadlock.
                _ = StopTracked(active.StopTunnelAsync);
            }
        }

        // Also the stops begun when a tunnel fell on its own, so that no helper is cut off when the application ends.
        Task[] stops;
        lock (_gate)
        {
            stops = [.. _pendingStops];
        }

        Task.WaitAll(stops, DisposeStopLimit);
        RaiseLatest();

        // The semaphore is not disposed: an operation in flight still releases it when it ends.
    }

    /// <summary>Connects the selected exit; the caller holds the operation gate.</summary>
    /// <param name="cancellationToken">The attempt's token.</param>
    /// <returns>The status when the attempt ended.</returns>
    private async Task<ExitStatus> ConnectInTurnAsync(CancellationToken cancellationToken)
    {
        var selected = _records.FindSelected() ?? throw new InvalidOperationException("No exit is selected.");
        await DisconnectCoreAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var exitId = selected.Exit.Id;
        SetStatus(new ExitStatus(ExitState.Connecting, exitId, null));
        var tunnel = await StartTunnelAsync(selected, cancellationToken).ConfigureAwait(false);
        return tunnel is null ? Status : await PublishAsync(exitId, tunnel, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the tunnel; a failure ends in <see cref="ExitState.Failed"/>, a cancellation in not connected.
    /// </summary>
    /// <param name="selected">The exit and its configuration.</param>
    /// <param name="cancellationToken">Stops the attempt.</param>
    /// <returns>The tunnel, or <see langword="null"/> when it failed.</returns>
    private async Task<IExitTunnel?> StartTunnelAsync(
        ExitWithConfiguration selected, CancellationToken cancellationToken)
    {
        var exitId = selected.Exit.Id;
        try
        {
            var tunnel = await _connector.ConnectAsync(selected.Exit, selected.Configuration, cancellationToken)
                .ConfigureAwait(false);
            if (tunnel is null)
            {
                SetStatus(new ExitStatus(ExitState.Failed, exitId, ExitFailure.ConnectFailed));
            }

            return tunnel;
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
        var pipeline = CreatePipeline(tunnel);
        if (pipeline is null)
        {
            await StopBoundedAsync(() => ActiveExit.StopQuietlyAsync(tunnel)).ConfigureAwait(false);
            SetStatus(new ExitStatus(ExitState.Failed, exitId, ExitFailure.ConnectFailed));
            return Status;
        }

        var (invoker, stopped) = pipeline.Value;
        var active = new ActiveExit(exitId, tunnel, invoker);
        bool disposed;
        bool publish;
        lock (_gate)
        {
            // Decided once, under the same lock as the publication: a disconnect or a dispose that cancelled the
            // attempt before this point stops it; one that comes after takes the published exit down as usual.
            disposed = _disposed;
            publish = !disposed && !cancellationToken.IsCancellationRequested;
            if (publish)
            {
                _active = active;
                _status = new ExitStatus(ExitState.Connected, exitId, null);
                _version++;
            }
            else
            {
                _status = ExitStatus.NotConnected;
                _version++;
            }
        }

        if (!publish)
        {
            active.Stop();
            RaiseLatest();
            await StopBoundedAsync(active.StopTunnelAsync).ConfigureAwait(false);
            ObjectDisposedException.ThrowIf(disposed, this);
            throw new OperationCanceledException(cancellationToken);
        }

        // Watched after it is published, so that a tunnel that has already stopped is noticed at once.
        active.Watch(stopped, () => OnTunnelStopped(active));
        RaiseLatest();
        return Status;
    }

    /// <summary>
    /// Builds the pipeline through a tunnel's proxy, which must be on the loopback address. The endpoint is copied, so
    /// that the tunnel cannot change it after it has been checked.
    /// </summary>
    /// <param name="tunnel">The started tunnel.</param>
    /// <returns>
    /// The pipeline and the tunnel's stop token, or <see langword="null"/> when the proxy is refused or the pipeline
    /// cannot be built.
    /// </returns>
    private (HttpMessageInvoker Invoker, CancellationToken Stopped)? CreatePipeline(IExitTunnel tunnel)
    {
        try
        {
            var stopped = tunnel.Stopped;
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
            if (!acceptable)
            {
                return null;
            }

            var handler = _handlers.Create(proxy, copied, () => IsRunning(tunnel));
            return (new HttpMessageInvoker(handler, disposeHandler: true), stopped);
        }
#pragma warning disable CA1031 // A tunnel whose proxy cannot be read or used is a failed connection.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>Asks a tunnel whether it still holds its proxy's port; a tunnel that cannot say does not.</summary>
    /// <param name="tunnel">The tunnel.</param>
    /// <returns><see langword="true"/> when the tunnel says it is running.</returns>
    private static bool IsRunning(IExitTunnel tunnel)
    {
        try
        {
            return tunnel.IsRunning;
        }
#pragma warning disable CA1031 // A tunnel that fails to answer is treated as one that has stopped.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    /// <summary>Copies an address, so that a change to the original cannot reach the copy.</summary>
    /// <param name="address">The address.</param>
    /// <returns>An equal, separate address; IPv4 for an IPv4-mapped one.</returns>
    private static IPAddress CopyOf(IPAddress address)
    {
        // An IPv4 address written as IPv6 becomes plain IPv4, since an IPv6 socket on Windows cannot reach it.
        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4();
        }

        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? new IPAddress(address.GetAddressBytes(), address.ScopeId)
            : new IPAddress(address.GetAddressBytes());
    }

    /// <summary>
    /// Stops the exit when its tunnel stops on its own; the state becomes failed with the connection lost. Runs inside
    /// the tunnel's cancellation callback, so only the state and the pipeline are changed there; stopping the tunnel
    /// and raising the event run on the thread pool, so that a tunnel that holds its own lock while it cancels cannot
    /// deadlock.
    /// </summary>
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

        // The event first, so that the display shows the lost connection while the tunnel is still stopping.
        TrackStop(Task.Run(async () =>
        {
            RaiseLatest();
            await active.StopTunnelAsync().ConfigureAwait(false);
        }));
    }

    /// <summary>Disconnects the connected exit, if any; the caller holds the operation gate.</summary>
    /// <returns>A task that completes when the tunnel has stopped.</returns>
    private async Task DisconnectCoreAsync()
    {
        var active = TakeActive(ExitStatus.NotConnected);
        try
        {
            active?.Stop();
        }
        finally
        {
            // The event first, so that the display follows the state while the tunnel is still stopping.
            RaiseLatest();
            if (active is not null)
            {
                await StopBoundedAsync(active.StopTunnelAsync).ConfigureAwait(false);
            }
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

    /// <summary>
    /// Calls each handler of <see cref="StatusChanged"/>, keeping one handler's failure from the others.
    /// </summary>
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

    /// <summary>
    /// Stops a tunnel on the thread pool as a tracked stop, so that disposal waits for it whoever began it. Every
    /// tunnel stop goes through here or through <see cref="TrackStop"/>.
    /// </summary>
    /// <param name="stop">Stops the tunnel; it never throws.</param>
    /// <returns>The tracked stop.</returns>
    private Task StopTracked(Func<Task> stop)
    {
        var task = Task.Run(stop);
        TrackStop(task);
        return task;
    }

    /// <summary>
    /// Stops a tunnel as a tracked stop and waits for it at most <see cref="DisposeStopLimit"/>. A tunnel that hangs
    /// while it stops is left to itself (its pipeline is already gone) and cannot hold the operations for ever.
    /// </summary>
    /// <param name="stop">Stops the tunnel; it never throws.</param>
    /// <returns>A task that completes when the tunnel has stopped or the limit has passed.</returns>
    private async Task StopBoundedAsync(Func<Task> stop)
    {
        var stopping = StopTracked(stop);
        await Task.WhenAny(stopping, Task.Delay(DisposeStopLimit)).ConfigureAwait(false);
    }

    /// <summary>Keeps a tunnel stop running on the thread pool until it ends, so that disposal can wait for it.</summary>
    /// <param name="stop">The stop.</param>
    private void TrackStop(Task stop)
    {
        lock (_gate)
        {
            _pendingStops.Add(stop);
        }

        stop.ContinueWith(
            ended =>
            {
                lock (_gate)
                {
                    _pendingStops.Remove(ended);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Cancels every attempt to connect that has begun, also those still waiting for their turn.</summary>
    private void CancelAttempt()
    {
        CancellationTokenSource[] attempts;
        lock (_gate)
        {
            attempts = [.. _attempts];
        }

        foreach (var attempt in attempts)
        {
            try
            {
                attempt.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The attempt has just ended.
            }
        }
    }
}
