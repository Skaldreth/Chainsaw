using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// Runs the watches in the background while Chainsaw is open and unlocked (CLAUDE.md, <em>Bevaka</em>; the process
/// that runs when Chainsaw is closed is <c>Chainsaw.Watcher</c>). Every few seconds it looks at the open case's active
/// watches; for each chain whose gate is open it asks the chain's height at most once a minute (at once for a watch
/// without a start), and the Monitoring module checks the watches that are due: a new block has come and each watch's
/// own pace allows. A new block heard of by websocket (<see cref="IBlockNews"/>, where a source has it) makes it ask at
/// once instead of waiting for the minute. Locked, without an exit or with the chain off, nothing is asked (the
/// module's gate) and nothing is listened to. First in each round it brings in what the background process saw while
/// Chainsaw was closed. It knows nothing of WPF.
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

    /// <summary>Brings what the background process saw into the open case before the checks, or nothing.</summary>
    private readonly Func<ICase, Task>? _import;

    /// <summary>The chains' news of new blocks, or <see langword="null"/>.</summary>
    private readonly IBlockNews? _news;

    /// <summary>The case the states belong to, so that another case starts afresh.</summary>
    private ICase? _case;

    /// <summary>Creates the runner; <see cref="Start"/> starts it.</summary>
    /// <param name="board">The watches.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="delay">
    /// Waits between rounds; <see cref="Task.Delay(TimeSpan, CancellationToken)"/> when none.
    /// </param>
    /// <param name="import">
    /// Brings what the background process saw into the open case (<see cref="BackgroundLink.ImportAsync"/>), first in
    /// each round, so that it and the checks never write the case's watches at once; none when not given.
    /// </param>
    /// <param name="news">The chains' news of new blocks, or <see langword="null"/> to ask once a minute only.</param>
    public WatchRunner(
        WatchBoard board,
        IClock clock,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<ICase, Task>? import = null,
        IBlockNews? news = null)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(clock);
        _board = board;
        _clock = clock;
        _delay = delay ?? Task.Delay;
        _import = import;
        _news = news;
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
            // No case open (closed or locked): nothing is listened to either.
            foreach (var chain in _chains.Keys)
            {
                _news?.Release(chain);
            }

            return;
        }

        if (!ReferenceEquals(openCase, _case))
        {
            _case = openCase;
            _chains.Clear();
        }

        if (_import is not null)
        {
            try
            {
                await _import(openCase).ConfigureAwait(false);
            }
            catch (CaseException)
            {
                // The case was closed or locked meanwhile; the next round looks at the case open then.
                return;
            }
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
        if (source.Gate(chain) != WatchGate.Open)
        {
            _news?.Release(chain);
            return;
        }

        // A new block heard of asks at once, and so does a new watch, unless the last ask for the height failed: then the
        // minute holds whatever is heard, so that a source that fails cannot be asked faster by news.
        _news?.Keep(chain);
        var heard = _news?.Heard(chain) ?? 0;
        var nudged = heard != state.Heard;
        state.Heard = heard;
        var waits = state.HeightFailed || (!unstarted && !nudged);
        if (waits && now < state.NextHeight)
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

        /// <summary>Gets or sets how many new blocks had been heard of at the last look.</summary>
        public long Heard { get; set; }
    }
}
