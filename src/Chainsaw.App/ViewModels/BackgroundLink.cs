using Chainsaw.App.Settings;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.ViewModels;

/// <summary>What the background watching does, as the head of the view Bevakning says it (sketch BV1a).</summary>
public enum BackgroundMode
{
    /// <summary>No watch is active in any case, so it does not run.</summary>
    Idle,

    /// <summary>The user has turned it off.</summary>
    Off,

    /// <summary>VPN is the way out, which the background cannot connect without the vault: nothing is asked.</summary>
    NeedsWindows,

    /// <summary>It should run but does not (it could not be started).</summary>
    NotRunning,

    /// <summary>It runs, through Windows.</summary>
    Running,

    /// <summary>It runs, but its lookups fail; it tries again.</summary>
    Failing,

    /// <summary>It runs, but the watched chains' sources are off, so it looks nothing up.</summary>
    SourcesOff,
}

/// <summary>
/// The open Chainsaw's side of the background watching (CLAUDE.md, <em>Bevaka</em>): it holds the open case so that the
/// background leaves it to Chainsaw, writes the plan (the addresses, filters and case ids, under DPAPI) when the
/// watches or the settings change, starts the background process and sets it to start with Windows while a watch is
/// active and it is on (and the way out is Windows), brings what the background saw into the open case, and says
/// honestly what the background does. It knows nothing of WPF.
/// </summary>
public sealed class BackgroundLink : IDisposable
{
    /// <summary>How often it looks at the background.</summary>
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(10);

    /// <summary>How long between two tries to start a background process that should run.</summary>
    public static readonly TimeSpan StartAgain = TimeSpan.FromMinutes(1);

    /// <summary>What it needs.</summary>
    private readonly BackgroundParts _parts;

    /// <summary>The open case.</summary>
    private readonly CaseSession _session;

    /// <summary>The open case's watches.</summary>
    private readonly WatchBoard _board;

    /// <summary>The UI thread.</summary>
    private readonly IUiThread _ui;

    /// <summary>Lets one write of the plan at a time.</summary>
    private readonly SemaphoreSlim _writing = new(1, 1);

    /// <summary>Stops the loop.</summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>Waits between rounds; the test's waits pass at once.</summary>
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>The settings in force.</summary>
    private AppSettings _settings;

    /// <summary>The plan written last.</summary>
    private BackgroundPlan _plan = BackgroundPlan.Empty;

    /// <summary>The hold on the open case.</summary>
    private IDisposable? _hold;

    /// <summary>The case held, or <see langword="null"/>.</summary>
    private Guid? _held;

    /// <summary>When the background was last started.</summary>
    private DateTimeOffset _started = DateTimeOffset.MinValue;

    /// <summary>Creates the link; <see cref="Start"/> starts it.</summary>
    /// <param name="parts">What it needs.</param>
    /// <param name="session">The open case.</param>
    /// <param name="board">The open case's watches.</param>
    /// <param name="ui">The UI thread.</param>
    /// <param name="settings">The settings at the start.</param>
    /// <param name="delay">
    /// Waits between rounds; <see cref="Task.Delay(TimeSpan, CancellationToken)"/> when none.
    /// </param>
    public BackgroundLink(
        BackgroundParts parts,
        CaseSession session,
        WatchBoard board,
        IUiThread ui,
        AppSettings settings,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(settings);
        (_parts, _session, _board, _ui, _settings) = (parts, session, board, ui, settings);
        _delay = delay ?? Task.Delay;
    }

    /// <summary>Raised on the UI thread when what is said about the background may have changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets what the background does.</summary>
    public BackgroundMode Mode { get; private set; } = BackgroundMode.Idle;

    /// <summary>Gets the endpoint in step the background asks, when it says so.</summary>
    public string? InStepWith { get; private set; }

    /// <summary>Gets whether the background saw a hit that is not yet in its case (for the lock screen).</summary>
    public bool HasQueuedHits { get; private set; }

    /// <summary>Starts following the open case and the watches, and the loop that looks at the background.</summary>
    public void Start()
    {
        _session.Changed += (_, _) => HoldOpenCase();
        _session.Closing += (_, closing) => WriteClosing(closing);
        _board.Changed += (_, _) => WriteCaseSoon();
        HoldOpenCase();
        _ = WriteAsync(_session.Current);
        _ = Task.Run(() => LoopAsync(_stop.Token));
    }

    /// <summary>Takes changed settings: the plan gets them, and the background is started or stopped.</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>A task that completes when the plan is written.</returns>
    public Task ApplyAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        return WriteAsync(null);
    }

    /// <summary>Writes the open case's active watches into the plan now, off the UI thread.</summary>
    /// <returns>A task that completes when the plan is written.</returns>
    public Task WriteCaseAsync() => _board.Case is { } openCase ? WriteAsync(openCase) : WriteAsync(null);

    /// <summary>
    /// Brings what the background saw into the open case, off the UI thread; the window reads the case again. Called by
    /// the runner before its checks, so that the two never write the case's watches at once.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <returns>A task that completes when it is done.</returns>
    /// <exception cref="CaseException">The case was closed meanwhile.</exception>
    public async Task ImportAsync(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        BackgroundImport result;
        try
        {
            if (_parts.Queue.List(@case.Info.Id).Count == 0)
            {
                return;
            }

            result = await Task.Run(() => _parts.Watches.Import(@case)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The queue could not be read just now; the runner's own checks go on, and the next round tries again.
            return;
        }

        // The background's hits come in as tips, never told as hits: one becomes a hit, and is told, only when the
        // runner's own lookup gives it.
        if (result.Entries > 0)
        {
            _board.Checked(@case, []);
        }

        if (result.Problems.Count > 0)
        {
            _board.Report(result.Problems[0]);
        }
    }

    /// <summary>Looks at the background once: whether it runs, what it says, and whether a hit waits.</summary>
    /// <returns>A task that completes when it is done.</returns>
    public async Task RefreshAsync()
    {
        var plan = _plan;
        var needed = Needed(plan);
        var running = _parts.Process.IsRunning();
        var now = _parts.Clock.UtcNow;
        if (needed && !running && now - _started >= StartAgain)
        {
            // It ended or could not start: started again at most once a minute.
            _started = now;
            running = _parts.Process.Start();
        }

        // A status left by a process that has ended is not believed; the new one says its own after its first round.
        var said = _parts.Status.Read();
        var status = said is not null && _parts.Process.IsProcess(said.ProcessId) ? said : null;
        var hits = _parts.Queue.HasHits();
        var mode = ModeOf(plan, running, status);
        var inStep = mode == BackgroundMode.Running ? status?.InStepWith : null;
        await PostAsync(() =>
        {
            if (_session.Current is not null && _hold is null)
            {
                HoldOpenCase();
            }

            if (mode == Mode && inStep == InStepWith && hits == HasQueuedHits)
            {
                return;
            }

            (Mode, InStepWith, HasQueuedHits) = (mode, inStep, hits);
            Changed?.Invoke(this, EventArgs.Empty);
        }).ConfigureAwait(false);
    }

    /// <summary>Stops the loop and lets go of the open case.</summary>
    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _hold?.Dispose();
        _hold = null;
    }

    /// <summary>Gives what the background does, for a plan.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="running">Whether the process runs.</param>
    /// <param name="status">What the process says it does, if anything.</param>
    /// <returns>The mode.</returns>
    private BackgroundMode ModeOf(BackgroundPlan plan, bool running, BackgroundStatus? status)
    {
        if (!_settings.WatchInBackground)
        {
            return BackgroundMode.Off;
        }

        if (!plan.HasWork)
        {
            return BackgroundMode.Idle;
        }

        if (plan.Route != ExitRoute.Windows)
        {
            return BackgroundMode.NeedsWindows;
        }

        if (!running)
        {
            return BackgroundMode.NotRunning;
        }

        // What the running process says itself: its lookups fail, or the chains are off.
        return status?.State switch
        {
            BackgroundState.Failing => BackgroundMode.Failing,
            BackgroundState.SourcesOff => BackgroundMode.SourcesOff,
            _ => BackgroundMode.Running,
        };
    }

    /// <summary>Says whether the background should run: on, a watch active, and Windows as the way out.</summary>
    /// <param name="plan">The plan.</param>
    /// <returns><see langword="true"/> when it should.</returns>
    private static bool Needed(BackgroundPlan plan) => plan.HasWork && plan.Route == ExitRoute.Windows;

    /// <summary>Holds the open case, and lets go of the one held before (on the UI thread).</summary>
    private void HoldOpenCase()
    {
        var id = _session.Current?.Info.Id;
        if (id == _held && (_hold is not null || id is null))
        {
            return;
        }

        _hold?.Dispose();
        _hold = id is { } caseId ? _parts.Holds.Hold(caseId) : null;
        _held = id;
    }

    /// <summary>
    /// Writes a case that is being let go (closed, forgotten at a lock, or replaced) into the plan while it can still be
    /// read, since the background takes it over as soon as the hold goes: a watch taken back, paused or changed, or a
    /// hit sealed, just before is then never watched or told on the plan from before. It runs on the UI thread, as the
    /// case is closed right after; starting or stopping the background follows off it.
    /// </summary>
    /// <param name="closing">The case being let go.</param>
    private void WriteClosing(ICase closing)
    {
        _writing.Wait();
        try
        {
            var described = _parts.Watches.Describe(closing, _parts.Clock.UtcNow);
            _plan = _parts.Plan.Update(plan => plan.With(described));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The plan keeps what it had for the case; letting the case go must never fail for it.
        }
        finally
        {
            _writing.Release();
        }

        _ = WriteAsync(null);
    }

    /// <summary>Writes the open case's watches into the plan soon, off the UI thread.</summary>
    private void WriteCaseSoon()
    {
        if (_board.Case is not null)
        {
            _ = WriteCaseAsync();
        }
    }

    /// <summary>
    /// Writes the plan with the settings and, when a case is given, its active watches; then sets the background to
    /// start with Windows, or not, and starts or stops it. A case closed meanwhile keeps what the plan had.
    /// </summary>
    /// <param name="case">The open case, or <see langword="null"/>.</param>
    /// <returns>A task that completes when it is written.</returns>
    private async Task WriteAsync(ICase? @case)
    {
        await _writing.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                BackgroundCase? described = null;
                if (@case is not null)
                {
                    try
                    {
                        described = _parts.Watches.Describe(@case, _parts.Clock.UtcNow);
                    }
                    catch (Exception exception) when (exception is CaseException or InvalidDataException)
                    {
                        // The case was closed or locked: the plan keeps what it had for it.
                    }
                }

                var settings = _settings;
                BackgroundPlan Settled(BackgroundPlan plan) => plan with
                {
                    Enabled = settings.WatchInBackground,
                    Route = settings.ExitRoute,
                    Alerts = settings.Alerts,
                };

                try
                {
                    _plan = _parts.Plan.Update(plan => Settled(described is null ? plan : plan.With(described)));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                    or System.Security.Cryptography.CryptographicException or PlatformNotSupportedException)
                {
                    // The plan could not be written; the next change tries again, and the head says what runs. The
                    // settings hold all the same: a background that may no longer go out through Windows is stopped
                    // now, never left to look up on the old plan (principle 4).
                    _plan = Settled(_plan);
                }

                StartOrStop(_plan);
            }).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.Security.Cryptography.CryptographicException or PlatformNotSupportedException)
        {
            // Starting or stopping failed; the loop starts it again when it should run, and the head says what runs.
        }
        finally
        {
            _writing.Release();
        }

        await RefreshAsync().ConfigureAwait(false);
    }

    /// <summary>Sets the background to start with Windows, or not, and starts or stops it now.</summary>
    /// <param name="plan">The plan written.</param>
    private void StartOrStop(BackgroundPlan plan)
    {
        var needed = Needed(plan);
        try
        {
            _parts.Autostart.Set(needed && _parts.Process.Exists ? _parts.Process.Command : null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            // Without the Run key it still runs until sign-out; the head says whether it runs.
        }

        var running = _parts.Process.IsRunning();
        if (needed && !running)
        {
            _started = _parts.Clock.UtcNow;
            _parts.Process.Start();
        }
        else if (!needed && running)
        {
            _parts.Process.Stop();
        }
    }

    /// <summary>Runs an action on the UI thread and waits for it.</summary>
    /// <param name="action">The action.</param>
    /// <returns>A task that completes when it has run.</returns>
    private Task PostAsync(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(() =>
        {
            try
            {
                action();
            }
            finally
            {
                done.SetResult();
            }
        });
        return done.Task;
    }

    /// <summary>Looks at the background every <see cref="Tick"/> until stopped.</summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when stopped.</returns>
    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync().ConfigureAwait(false);
                await _delay(Tick, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Looking must never take the window down; the next round looks again.
            }
        }
    }
}

/// <summary>What the link needs from the modules and Windows, gathered so that its constructor stays readable.</summary>
/// <param name="Watches">The case's side: describing and bringing in.</param>
/// <param name="Plan">The plan.</param>
/// <param name="Holds">The holds.</param>
/// <param name="Queue">The queue.</param>
/// <param name="Status">The background's status.</param>
/// <param name="Process">The background process.</param>
/// <param name="Autostart">Starting it with Windows.</param>
/// <param name="Clock">The clock.</param>
public sealed record BackgroundParts(
    IBackgroundWatches Watches,
    IBackgroundPlan Plan,
    ICaseHolds Holds,
    IWatchQueue Queue,
    IBackgroundStatusFile Status,
    IBackgroundProcess Process,
    IAutostart Autostart,
    IClock Clock);
