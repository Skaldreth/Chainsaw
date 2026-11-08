using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Connects the exit again when its tunnel falls (<see cref="ExitFailure.ConnectionLost"/>), for example after the
/// computer has slept: it waits <see cref="Waits"/> between the attempts, the last of them over and over, and tries at
/// once when the network comes back. A failure that will not pass by waiting (the configuration refused, the tunnel
/// not available), a cancelled attempt and any connection or disconnection by the user end it.
/// </summary>
/// <remarks>
/// The connection's own events decide only when to begin and when the user has taken over. Whether to go on after an
/// attempt is read from what the attempt gave, and whether the exit is still lost from the connection's status before
/// each attempt, so that an event that comes late can never start an attempt nobody wants.
/// </remarks>
internal sealed class ExitReconnector : IExitReconnect, IDisposable
{
    /// <summary>
    /// The waits before each attempt: short at first, since a tunnel that fell often comes back as soon as the network
    /// has woken, and then no more often than once a minute.
    /// </summary>
    internal static readonly IReadOnlyList<TimeSpan> Waits =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
    ];

    /// <summary>The connection to the exit.</summary>
    private readonly IExitConnection _connection;

    /// <summary>
    /// Whether the computer reaches the internet, whose return wakes the wait; none when <see langword="null"/>.
    /// </summary>
    private readonly INetworkPresence? _presence;

    /// <summary>Times the waits; the tests move it by hand.</summary>
    private readonly TimeProvider _time;

    /// <summary>Guards the fields below; never held across an await or while <see cref="Changed"/> is raised.</summary>
    private readonly Lock _gate = new();

    /// <summary>Stops the reconnecting under way, or <see langword="null"/> while none runs.</summary>
    private CancellationTokenSource? _run;

    /// <summary>The exit being connected again.</summary>
    private Guid _exitId;

    /// <summary>Whether an attempt runs now, while the connection's own events are its and not the user's.</summary>
    private bool _attempting;

    /// <summary>When the next attempt is made, or <see langword="null"/>.</summary>
    private DateTimeOffset? _next;

    /// <summary>How many attempts have been made since the connection was lost.</summary>
    private int _attempts;

    /// <summary>Completed when the network comes back, so that the wait ends at once.</summary>
    private TaskCompletionSource _wake = NewWake();

    /// <summary>Whether it has been disposed, after which nothing begins.</summary>
    private bool _disposed;

    /// <summary>Creates the reconnecting and follows the connection and the network.</summary>
    /// <param name="connection">The connection to the exit.</param>
    /// <param name="presence">Whether the computer reaches the internet; none when <see langword="null"/>.</param>
    /// <param name="time">Times the waits.</param>
    internal ExitReconnector(IExitConnection connection, INetworkPresence? presence, TimeProvider time)
    {
        _connection = connection;
        _presence = presence;
        _time = time;
        connection.StatusChanged += OnStatusChanged;
        if (presence is not null)
        {
            presence.Changed += OnNetworkChanged;
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public DateTimeOffset? NextAttempt
    {
        get
        {
            lock (_gate)
            {
                return _next;
            }
        }
    }

    /// <inheritdoc />
    public int Attempts
    {
        get
        {
            lock (_gate)
            {
                return _attempts;
            }
        }
    }

    /// <summary>Stops the reconnecting and follows nothing more.</summary>
    public void Dispose()
    {
        _connection.StatusChanged -= OnStatusChanged;
        if (_presence is not null)
        {
            _presence.Changed -= OnNetworkChanged;
        }

        CancellationTokenSource? run;
        lock (_gate)
        {
            _disposed = true;
            run = _run;
        }

        Cancel(run);
    }

    /// <summary>Gives the wait before an attempt.</summary>
    /// <param name="round">
    /// The attempt's number since the connection was lost, or since the network came back, from 0.
    /// </param>
    /// <returns>The wait; the last of <see cref="Waits"/> once they run out.</returns>
    internal static TimeSpan WaitBefore(int round) => Waits[Math.Clamp(round, 0, Waits.Count - 1)];

    /// <summary>Says whether a failure can pass by waiting, so that it is worth another attempt.</summary>
    /// <param name="failure">Why the exit failed.</param>
    /// <returns><see langword="true"/> for a lost connection, no answer and a failed start.</returns>
    internal static bool CanPass(ExitFailure? failure) =>
        failure is ExitFailure.ConnectionLost or ExitFailure.NoAnswer or ExitFailure.ConnectFailed;

    /// <summary>Gives a new, uncompleted wake-up.</summary>
    /// <returns>The wake-up.</returns>
    private static TaskCompletionSource NewWake() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Cancels a run, which may just have ended.</summary>
    /// <param name="run">The run, or <see langword="null"/>.</param>
    private static void Cancel(CancellationTokenSource? run)
    {
        try
        {
            run?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run has just ended.
        }
    }

    /// <summary>
    /// Begins when a connected tunnel falls, and ends when the user connects or disconnects meanwhile. The events of
    /// an attempt of its own are passed over.
    /// </summary>
    /// <param name="sender">The connection.</param>
    /// <param name="status">The new status.</param>
    private void OnStatusChanged(object? sender, ExitStatus status)
    {
        CancellationTokenSource? stop = null;
        lock (_gate)
        {
            if (_disposed || _attempting)
            {
                return;
            }

            if (_run is null)
            {
                if (status is { State: ExitState.Failed, Failure: ExitFailure.ConnectionLost, ExitId: { } id })
                {
                    Begin(id);
                }

                return;
            }

            if (status.State != ExitState.Failed || status.ExitId != _exitId)
            {
                stop = _run;
            }
        }

        Cancel(stop);
    }

    /// <summary>Ends the wait at once when the network comes back.</summary>
    /// <param name="sender">The presence.</param>
    /// <param name="available">Whether the computer reaches the internet now.</param>
    private void OnNetworkChanged(object? sender, bool available)
    {
        if (!available)
        {
            return;
        }

        lock (_gate)
        {
            _wake.TrySetResult();
        }
    }

    /// <summary>Begins reconnecting an exit; the caller holds <see cref="_gate"/> and none runs.</summary>
    /// <param name="exitId">The exit whose tunnel fell.</param>
    private void Begin(Guid exitId)
    {
        var run = new CancellationTokenSource();
        _run = run;
        _exitId = exitId;
        _attempts = 0;
        _ = Task.Run(() => RunAsync(exitId, run));
    }

    /// <summary>
    /// Waits and tries again until the exit is connected, the failure will not pass, or the user takes over.
    /// </summary>
    /// <param name="exitId">The exit whose tunnel fell.</param>
    /// <param name="run">The run's cancellation, disposed when it ends.</param>
    /// <returns>A task that completes when the reconnecting ends.</returns>
    private async Task RunAsync(Guid exitId, CancellationTokenSource run)
    {
        try
        {
            var round = 0;
            while (true)
            {
                var woken = await WaitAsync(WaitBefore(round), run.Token).ConfigureAwait(false);
                if (!IsStillLost(exitId) || !BeginAttempt(run))
                {
                    return;
                }

                var status = await AttemptAsync(run.Token).ConfigureAwait(false);
                EndAttempt();
                if (status is null || status.State == ExitState.Connected || !CanPass(status.Failure))
                {
                    return;
                }

                round = woken ? 0 : round + 1;
            }
        }
        catch (OperationCanceledException)
        {
            // The user took over, or Chainsaw is closing.
        }
        finally
        {
            End(run);
        }
    }

    /// <summary>Waits before an attempt, and says when the attempt will be made.</summary>
    /// <param name="wait">How long.</param>
    /// <param name="cancellationToken">Stops the wait.</param>
    /// <returns><see langword="true"/> when the network came back before the wait was over.</returns>
    private async Task<bool> WaitAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        // The delay is started before the time is told, so that the time told is never later than the delay's.
        Task woken;
        Task delay;
        lock (_gate)
        {
            _wake = NewWake();
            woken = _wake.Task;
            delay = Task.Delay(wait, _time, cancellationToken);
            _next = _time.GetUtcNow() + wait;
        }

        RaiseChanged();
        var first = await Task.WhenAny(delay, woken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return first == woken;
    }

    /// <summary>
    /// Says whether the exit is still lost, by its status now, so that the user's own choice is never undone.
    /// </summary>
    /// <param name="exitId">The exit.</param>
    /// <returns><see langword="true"/> when it has failed in a way that can pass.</returns>
    private bool IsStillLost(Guid exitId)
    {
        var status = _connection.Status;
        return status.State == ExitState.Failed && status.ExitId == exitId && CanPass(status.Failure);
    }

    /// <summary>Marks that an attempt runs, unless the run was stopped meanwhile.</summary>
    /// <param name="run">The run.</param>
    /// <returns><see langword="true"/> when the attempt may go.</returns>
    private bool BeginAttempt(CancellationTokenSource run)
    {
        lock (_gate)
        {
            if (run.IsCancellationRequested)
            {
                return false;
            }

            _attempting = true;
            _next = null;
            _attempts++;
        }

        RaiseChanged();
        return true;
    }

    /// <summary>Marks that the attempt has ended.</summary>
    private void EndAttempt()
    {
        lock (_gate)
        {
            _attempting = false;
        }
    }

    /// <summary>Connects the selected exit once.</summary>
    /// <param name="cancellationToken">Stops the attempt.</param>
    /// <returns>
    /// The status the attempt gave, or <see langword="null"/> when it was cancelled (the user disconnected or changed
    /// the exit) or no exit is selected any more.
    /// </returns>
    private async Task<ExitStatus?> AttemptAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
#pragma warning disable CA1031 // The vault may be locked meanwhile; the exit is still lost and is tried again later.
        catch (Exception)
#pragma warning restore CA1031
        {
            return _connection.Status;
        }
    }

    /// <summary>Ends the run and says that no attempt waits any more.</summary>
    /// <param name="run">The run.</param>
    private void End(CancellationTokenSource run)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_run, run))
            {
                _run = null;
            }

            _attempting = false;
            _next = null;
            _attempts = 0;
        }

        run.Dispose();
        RaiseChanged();
    }

    /// <summary>Raises <see cref="Changed"/>, keeping one handler's failure from the others.</summary>
    private void RaiseChanged()
    {
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler)handler).Invoke(this, EventArgs.Empty);
            }
#pragma warning disable CA1031 // A display that fails must not stop the reconnecting.
            catch (Exception)
#pragma warning restore CA1031
            {
                // Dropped.
            }
        }
    }
}
