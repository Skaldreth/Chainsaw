using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Whether the internet answers through the way out through Windows: the computer has a network
/// (<see cref="INetworkSignals"/>) <em>and</em> the last probe (<see cref="IInternetProbe"/>) reached the internet. A
/// network alone is not enough, since a Wi-Fi with a login page is a network that reaches nothing (CLAUDE.md,
/// <em>Windows som utgång</em>: green only while the computer reaches the internet).
/// </summary>
/// <remarks>
/// The probe runs in the background: at the start, at once when the network comes, goes or changes address, every
/// <see cref="SettledInterval"/> while it reaches the internet and every <see cref="RetryInterval"/> while it does not,
/// so that Chainsaw turns green soon after the login. Without a network no probe is sent. Until the first probe has
/// answered, the network alone counts, so that the start is not red for nothing.
/// <para>
/// The probe leaves outside any tunnel, so it runs only while <see cref="ExitRoute.Windows"/> is the way chosen (decided
/// 2026-11-08): with VPN chosen nothing is sent, a probe in flight is cancelled when Windows is left, and the last
/// answer is forgotten, so only Windows' own signal is told (all that is known then). Choosing Windows probes at once.
/// </para>
/// </remarks>
internal sealed class NetworkPresence : INetworkPresence, IDisposable
{
    /// <summary>How long to wait between probes while the internet answers.</summary>
    internal static readonly TimeSpan SettledInterval = TimeSpan.FromSeconds(60);

    /// <summary>How long to wait between probes while the network has not reached the internet.</summary>
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(10);

    /// <summary>Guards the state, so that a change and a read never cross.</summary>
    private readonly Lock _gate = new();

    /// <summary>Where the network's signals come from.</summary>
    private readonly INetworkSignals _signals;

    /// <summary>Asks whether the internet answers.</summary>
    private readonly IInternetProbe _probe;

    /// <summary>The way out chosen, which must be Windows for a probe to be sent.</summary>
    private readonly IExitRoute _route;

    /// <summary>Times the waits between probes.</summary>
    private readonly TimeProvider _time;

    /// <summary>Told each wait the loop starts, with its length (<see langword="null"/>: until the network comes).</summary>
    private readonly Action<TimeSpan?>? _waiting;

    /// <summary>Stops the loop; the loop disposes it when it ends.</summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>Whether the computer has a network, as last told.</summary>
    private bool _hasNetwork;

    /// <summary>Whether the last probe reached the internet; <see langword="null"/> before the first has answered.</summary>
    private bool? _reached;

    /// <summary>Whether the presence has been disposed, after which no change is told.</summary>
    private bool _disposed;

    /// <summary>Completed when the network's signals change, so that the loop probes at once.</summary>
    private TaskCompletionSource _wake = NewWake();

    /// <summary>Stops the probe in flight when Windows is left; <see langword="null"/> while none is.</summary>
    private CancellationTokenSource? _probing;

    /// <summary>Creates the presence and starts following the network and probing.</summary>
    /// <param name="signals">Where the network's signals come from.</param>
    /// <param name="probe">Asks whether the internet answers.</param>
    /// <param name="route">The way out chosen; the probe is sent only while it is Windows.</param>
    /// <param name="time">Times the waits; the tests move it by hand.</param>
    /// <param name="waiting">Told each wait the loop starts; only the tests listen.</param>
    internal NetworkPresence(
        INetworkSignals signals,
        IInternetProbe probe,
        IExitRoute route,
        TimeProvider time,
        Action<TimeSpan?>? waiting = null)
    {
        _signals = signals;
        _probe = probe;
        _route = route;
        _time = time;
        _waiting = waiting;

        // Followed first and read after, so that a change in between is never lost.
        signals.Changed += OnSignalsChanged;
        route.Changed += OnRouteChanged;
        _hasNetwork = signals.HasNetwork;
        _ = Task.Run(() => RunAsync(_stop.Token));
    }

    /// <inheritdoc />
    public event EventHandler<bool>? Changed;

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                return AvailableLocked();
            }
        }
    }

    /// <summary>Stops following the network and probing.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _signals.Changed -= OnSignalsChanged;
        _route.Changed -= OnRouteChanged;
        _stop.Cancel();
    }

    /// <summary>Creates a wake that continues its waiter off the signalling thread.</summary>
    /// <returns>The wake.</returns>
    private static TaskCompletionSource NewWake() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Reads the network again, tells a change and wakes the loop to probe at once.</summary>
    /// <param name="sender">Not used.</param>
    /// <param name="e">Not used.</param>
    private void OnSignalsChanged(object? sender, EventArgs e)
    {
        var hasNetwork = _signals.HasNetwork;
        Update(() => _hasNetwork = hasNetwork);
        lock (_gate)
        {
            _wake.TrySetResult();
        }
    }

    /// <summary>
    /// Stops the probe in flight and forgets its answer when Windows is left, and wakes the loop, which probes at once
    /// when Windows is chosen.
    /// </summary>
    /// <param name="sender">Not used.</param>
    /// <param name="route">The way chosen now.</param>
    private void OnRouteChanged(object? sender, ExitRoute route)
    {
        if (route != ExitRoute.Windows)
        {
            CancellationTokenSource? probing;
            lock (_gate)
            {
                probing = _probing;
            }

            // Cancelled outside the gate, since the probe's own callbacks run on it.
            try
            {
                probing?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The probe has just ended; its answer is not kept, since Windows is no longer chosen.
            }

            Update(() => _reached = null);
        }

        lock (_gate)
        {
            _wake.TrySetResult();
        }
    }

    /// <summary>Probes while the network is there, and waits as long as the last answer says.</summary>
    /// <param name="token">Stops the loop.</param>
    /// <returns>A task that ends when the presence is disposed.</returns>
    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var wake = ArmWake();
                var wait = await ProbeOnceAsync(token).ConfigureAwait(false);
                await WaitAsync(wait, wake, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Disposed.
        }
        finally
        {
            _stop.Dispose();
        }
    }

    /// <summary>
    /// Replaces the wake before probing, so that a signal during the probe makes the loop probe again at once.
    /// </summary>
    /// <returns>The task the coming wait also ends on.</returns>
    private Task ArmWake()
    {
        lock (_gate)
        {
            _wake = NewWake();
            return _wake.Task;
        }
    }

    /// <summary>Probes once when the computer has a network and Windows is the way chosen, and keeps the answer.</summary>
    /// <param name="token">Stops the probe.</param>
    /// <returns>
    /// How long to wait before the next probe, or <see langword="null"/> for until the network or the way changes.
    /// </returns>
    private async Task<TimeSpan?> ProbeOnceAsync(CancellationToken token)
    {
        // The way is read and the probe registered under the gate that a change of way takes to cancel it, so that a
        // probe begun just before VPN was chosen is always stopped.
        CancellationTokenSource probing;
        lock (_gate)
        {
            if (!_hasNetwork || _route.Current != ExitRoute.Windows)
            {
                return null;
            }

            probing = CancellationTokenSource.CreateLinkedTokenSource(token);
            _probing = probing;
        }

        bool? reached;
        try
        {
            reached = await ReachesAsync(probing.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _probing = null;
            }

            probing.Dispose();
        }

        token.ThrowIfCancellationRequested();
        if (reached is not { } answer)
        {
            // Windows was left meanwhile: nothing is kept, and the loop waits for the way or the network.
            return null;
        }

        // Kept only while Windows is still chosen: an answer that comes as VPN is chosen is forgotten.
        Update(() => _reached = _route.Current == ExitRoute.Windows ? answer : null);
        return answer ? SettledInterval : RetryInterval;
    }

    /// <summary>Asks the probe, counting a probe that throws as not reached.</summary>
    /// <param name="token">Stops the probe.</param>
    /// <returns>Whether the internet answered, or <see langword="null"/> when the probe was stopped.</returns>
    private async Task<bool?> ReachesAsync(CancellationToken token)
    {
        try
        {
            return await _probe.ReachesInternetAsync(token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return token.IsCancellationRequested ? null : false;
        }
    }

    /// <summary>Waits the time given, or until the network's signals change.</summary>
    /// <param name="wait">How long, or <see langword="null"/> for until the signals change.</param>
    /// <param name="wake">Ends the wait when the signals change.</param>
    /// <param name="token">Stops the wait.</param>
    /// <returns>A task that ends when the next probe is due.</returns>
    private async Task WaitAsync(TimeSpan? wait, Task wake, CancellationToken token)
    {
        using var delayStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var delay = Task.Delay(wait ?? Timeout.InfiniteTimeSpan, _time, delayStop.Token);
        _waiting?.Invoke(wait);
        await Task.WhenAny(delay, wake).ConfigureAwait(false);
        await delayStop.CancelAsync().ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
    }

    /// <summary>Tells whether lookups may go, while the gate is held.</summary>
    /// <returns>A network, and the internet reached or not yet probed.</returns>
    private bool AvailableLocked() => _hasNetwork && (_reached ?? true);

    /// <summary>Changes the state and tells the new value when it differs, once per real change.</summary>
    /// <param name="change">Changes the state, under the gate.</param>
    private void Update(Action change)
    {
        bool after;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var before = AvailableLocked();
            change();
            after = AvailableLocked();
            if (before == after)
            {
                return;
            }
        }

        foreach (var handler in Changed?.GetInvocationList().Cast<EventHandler<bool>>() ?? [])
        {
            try
            {
                handler(this, after);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // One listener's failure must not keep the change from the others.
            }
        }
    }
}
