using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// Runs the watches in the background while Chainsaw is open and unlocked (CLAUDE.md, <em>Bevaka</em>; the process
/// that runs when Chainsaw is closed is stage 27). Every few seconds it looks at the open case's active watches; for
/// each chain whose gate is open it asks the chain's height at most once a minute (at once for a watch without a
/// start), and the Monitoring module checks the watches that are due: a new block has come and each watch's own pace
/// allows. Locked, without an exit or with the chain off, nothing is asked (the module's gate). It knows nothing of
/// WPF.
/// </summary>
public sealed class WatchRunner : IDisposable
{
    /// <summary>How often the runner looks.</summary>
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);

    /// <summary>How often a chain's height is asked, at most.</summary>
    public static readonly TimeSpan HeightPoll = TimeSpan.FromMinutes(1);

    /// <summary>The watches.</summary>
    private readonly WatchBoard _board;

    /// <summary>The clock.</summary>
    private readonly IClock _clock;

    /// <summary>Waits between rounds; the test's waits pass at once.</summary>
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>Stops the loop.</summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>What the runner knows per chain.</summary>
    private readonly Dictionary<Chain, ChainState> _chains = [];

    /// <summary>The case the states belong to, so that another case starts afresh.</summary>
    private ICase? _case;

    /// <summary>Creates the runner; <see cref="Start"/> starts it.</summary>
    /// <param name="board">The watches.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="delay">
    /// Waits between rounds; <see cref="Task.Delay(TimeSpan, CancellationToken)"/> when none.
    /// </param>
    public WatchRunner(
        WatchBoard board, IClock clock, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(clock);
        _board = board;
        _clock = clock;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>Starts the loop off the UI thread; it runs until <see cref="Dispose"/>.</summary>
    public void Start() => _ = Task.Run(() => LoopAsync(_stop.Token));

    /// <summary>Stops the loop.</summary>
    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    /// <summary>One round: checks every chain whose watches are due. Never throws for a closed case.</summary>
    /// <param name="cancellationToken">Stops the round.</param>
    /// <returns>A task that completes when the round is done.</returns>
    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (_board.Case is not { } openCase)
        {
            return;
        }

        if (!ReferenceEquals(openCase, _case))
        {
            _case = openCase;
            _chains.Clear();
        }

        var active = _board.Snapshot.Watches.Where(watch => watch.State == WatchState.Active).ToList();
        foreach (var chain in active.Select(watch => watch.Chain).Distinct())
        {
            var unstarted = active.Any(watch => watch.Chain == chain && watch.StartHeight is null);
            try
            {
                await RunChainAsync(openCase, chain, unstarted, cancellationToken).ConfigureAwait(false);
            }
            catch (CaseException)
            {
                // The case was closed or locked meanwhile; the next round looks at the case open then.
                return;
            }
        }
    }

    /// <summary>Asks one chain's height when it is time, and lets the module check the watches that are due.</summary>
    /// <param name="openCase">The open case.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="unstarted">Whether a watch on it has no start yet.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>A task that completes when the chain is done.</returns>
    private async Task RunChainAsync(ICase openCase, Chain chain, bool unstarted, CancellationToken cancellationToken)
    {
        var source = _board.Source;
        var state = _chains.TryGetValue(chain, out var known) ? known : _chains[chain] = new ChainState();
        var now = _clock.UtcNow;
        // A new watch starts at once, unless the last ask for the height failed: then the minute holds too.
        var waits = !unstarted || state.HeightFailed;
        if (source.Gate(chain) != WatchGate.Open || (waits && now < state.NextHeight))
        {
            return;
        }

        state.NextHeight = now + HeightPoll;
        var height = await source.GetHeightAsync(openCase, chain, cancellationToken).ConfigureAwait(false);
        state.HeightFailed = height is null;
        _board.HeightAsked(openCase, chain, height is null);
        if (height is null)
        {
            return;
        }

        var run = await source.CheckAsync(openCase, chain, height.Value, cancellationToken).ConfigureAwait(false);
        _board.Checked(openCase, [.. run.Hits]);
    }

    /// <summary>Runs rounds until stopped; a round that fails waits for the next.</summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when stopped.</returns>
    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A round must never take the loop down: a source or the case may fail now and work at the next.
            }

            try
            {
                await _delay(Tick, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>What the runner knows about one chain.</summary>
    private sealed class ChainState
    {
        /// <summary>Gets or sets whether the last ask for the height gave nothing.</summary>
        public bool HeightFailed { get; set; }

        /// <summary>Gets or sets when the height may be asked next.</summary>
        public DateTimeOffset NextHeight { get; set; } = DateTimeOffset.MinValue;
    }
}
