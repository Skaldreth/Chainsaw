using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Watcher;

/// <summary>
/// One round of the background watching (CLAUDE.md, <em>Bevaka</em>): it reads the plan Chainsaw left, leaves every
/// case open in Chainsaw to it, and for each chain asks the height at most once a minute through the way out; the
/// watches that are due (<see cref="WatchSchedule.IsDue"/>) are checked with the same check and feeds as in the open
/// Chainsaw (<see cref="LookupMode.InStep"/>, the endpoints' pace and fallback), against a case in memory whose raw
/// responses go, with the matches, into the protected queue. With VPN as the way out nothing is looked up: the exits
/// are in the vault, which the background never opens.
/// </summary>
internal sealed class WatcherLoop
{
    /// <summary>How often a chain's height is asked, at most.</summary>
    public static readonly TimeSpan HeightPoll = TimeSpan.FromMinutes(1);

    /// <summary>How long before the endpoints are tried again when none is in step.</summary>
    public static readonly TimeSpan ProbeAgain = TimeSpan.FromMinutes(10);

    /// <summary>How many hits per watch it remembers having told of.</summary>
    public const int AlertedKept = 500;

    /// <summary>The services the round uses.</summary>
    private readonly WatcherParts _parts;

    /// <summary>The way out, set from the plan.</summary>
    private readonly IExitRoute _route;

    /// <summary>The gate, which follows the plan's way out.</summary>
    private readonly WatcherGate _gate;

    /// <summary>Makes the case in memory of a round.</summary>
    private readonly Func<IMemoryCase> _scratch;

    /// <summary>What it knows per chain.</summary>
    private readonly Dictionary<Chain, ChainState> _chains = [];

    /// <summary>Its progress per watch.</summary>
    private Dictionary<(Guid, string), BackgroundProgress> _progress;

    /// <summary>Whether the progress changed since it was last written.</summary>
    private bool _progressChanged;

    /// <summary>The plan read last.</summary>
    private BackgroundPlan _plan = BackgroundPlan.Empty;

    /// <summary>How many rounds without a readable plan before it is said as failing.</summary>
    private const int UnreadRoundsSaid = 4;

    /// <summary>How many rounds have gone without a readable plan.</summary>
    private int _unreadRounds;

    /// <summary>Whether a plan has been read at all.</summary>
    private bool _planRead;

    /// <summary>When the plan read last was written.</summary>
    private DateTimeOffset? _planWritten;

    /// <summary>The status said last.</summary>
    private (BackgroundState, string?)? _said;

    /// <summary>Creates the loop.</summary>
    /// <param name="parts">The services.</param>
    /// <param name="route">The way out.</param>
    /// <param name="gate">The gate.</param>
    /// <param name="scratch">Makes a case in memory.</param>
    public WatcherLoop(WatcherParts parts, IExitRoute route, WatcherGate gate, Func<IMemoryCase> scratch)
    {
        ArgumentNullException.ThrowIfNull(parts);
        (_parts, _route, _gate, _scratch) = (parts, route, gate, scratch);
        _progress = parts.Progress.Read().ToDictionary(each => (each.CaseId, each.WatchId));
    }

    /// <summary>Runs one round.</summary>
    /// <param name="cancellationToken">Stops the round.</param>
    /// <returns><see langword="false"/> when there is nothing to watch, so that the process ends.</returns>
    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        ReadPlan();
        if (!_planRead)
        {
            // The first plan could not be read just now (a scanner holds it at sign-in): wait a round, never end, and
            // say that it fails once it has gone on for a while.
            if (++_unreadRounds >= UnreadRoundsSaid)
            {
                Say(BackgroundState.Failing, null);
            }

            return true;
        }

        if (!_plan.HasWork)
        {
            Say(BackgroundState.Stopped, null);
            return false;
        }

        _gate.Route = _plan.Route;
        _route.Choose(_plan.Route);
        if (_plan.Route != ExitRoute.Windows)
        {
            // VPN needs the exit's WireGuard file, which is in the vault: nothing is looked up, and that is said.
            Say(BackgroundState.WaitingForExit, null);
            return true;
        }

        var work = _plan.Cases.Where(@case => !_parts.Holds.IsHeld(@case.CaseId))
            .SelectMany(@case => @case.Watches.Select(watch => (Case: @case.CaseId, Watch: watch)))
            .ToList();
        var round = new Round();
        using (var scratch = _scratch())
        {
            foreach (var chain in work.Select(each => each.Watch.Filter.Chain).Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await RunChainAsync(scratch, chain, [.. work.Where(each => each.Watch.Filter.Chain == chain)],
                        round, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
                {
                    // A source or the disk may fail now and work at the next round; the round goes on.
                    round.Failing = true;
                }
            }
        }

        KeepProgress();
        Say(StateOf(round, work.Count), round.InStepWith);
        return true;
    }

    /// <summary>Gives what to say after a round.</summary>
    /// <param name="round">The round.</param>
    /// <param name="watches">How many watches it had.</param>
    /// <returns>The state.</returns>
    private static BackgroundState StateOf(Round round, int watches)
    {
        if (round.Failing)
        {
            return BackgroundState.Failing;
        }

        return watches > 0 && round.ChainsOff == round.Chains ? BackgroundState.SourcesOff : BackgroundState.Running;
    }

    /// <summary>Checks one chain's watches that are due.</summary>
    /// <param name="scratch">The round's case in memory.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="work">Its watches, with their cases.</param>
    /// <param name="round">What the round has seen.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>A task that completes when the chain is done.</returns>
    private async Task RunChainAsync(
        IMemoryCase scratch,
        Chain chain,
        IReadOnlyList<(Guid Case, BackgroundWatch Watch)> work,
        Round round,
        CancellationToken cancellationToken)
    {
        round.Chains++;
        if (_gate.For(chain) != WatchGate.Open || _parts.Feeds.For(scratch, chain) is not { } feed)
        {
            round.ChainsOff++;
            return;
        }

        // Every chain's endpoints are tried, also when another chain has already named one in step.
        var inStep = await InStepAsync(chain, cancellationToken).ConfigureAwait(false);
        round.InStepWith ??= inStep;
        var state = _chains.TryGetValue(chain, out var known) ? known : _chains[chain] = new ChainState();
        var now = _parts.Clock.UtcNow;
        var unstarted = work.Any(each => StartOf(each.Case, each.Watch) is null);
        var waits = !unstarted || state.HeightFailed;
        if (waits && now < state.NextHeight)
        {
            return;
        }

        state.NextHeight = now + HeightPoll;
        var mark = scratch.Mark();
        var height = await WatchSchedule.HeightAsync(_parts.Bitcoin, _parts.Ethereum, scratch, chain, cancellationToken)
            .ConfigureAwait(false);
        state.HeightFailed = height is null;
        if (height is not { } tip)
        {
            round.Failing = true;
            return;
        }

        var tipResponses = scratch.SealedSince(SealedResponses.Chain, mark);
        var given = new HashSet<Guid>();
        foreach (var (caseId, watch) in work)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var withTip = given.Contains(caseId) ? [] : tipResponses;
            if (await CheckAsync(scratch, feed, caseId, watch, tip, withTip, round, cancellationToken)
                .ConfigureAwait(false))
            {
                // The height's responses go with the case's first entry that is queued, once.
                given.Add(caseId);
            }
        }
    }

    /// <summary>Checks one watch when it is due, or sets its start when it has none, and queues what it saw.</summary>
    /// <param name="scratch">The round's case in memory.</param>
    /// <param name="feed">The chain's feed.</param>
    /// <param name="caseId">The case.</param>
    /// <param name="watch">The watch.</param>
    /// <param name="height">The chain's height.</param>
    /// <param name="tipResponses">The height's responses, for the case's first entry of the round.</param>
    /// <param name="round">What the round has seen.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <returns><see langword="true"/> when an entry was queued.</returns>
    private async Task<bool> CheckAsync(
        IMemoryCase scratch,
        IWatchFeed feed,
        Guid caseId,
        BackgroundWatch watch,
        long height,
        IReadOnlyList<long> tipResponses,
        Round round,
        CancellationToken cancellationToken)
    {
        var progress = ProgressOf(caseId, watch.WatchId);
        var now = _parts.Clock.UtcNow;
        if (StartOf(caseId, watch) is not { } start)
        {
            _parts.Queue.Add(new QueuedCheck(
                Guid.NewGuid(), caseId, watch.WatchId, QueuedKind.Started, watch.Filter, height, height, null, [], [],
                Responses(scratch, tipResponses), now));
            _progress[(caseId, watch.WatchId)] = progress with { StartHeight = height };
            _progressChanged = true;

            // Kept at once: a stop or crash must never let a later run set another start.
            KeepProgress();
            return true;
        }

        var last = Later(watch.Checked, progress.Checked);
        if (!WatchSchedule.IsDue(watch.Filter.Pace, last, height, now))
        {
            return false;
        }

        var mark = scratch.Mark();
        var downTo = WatchSchedule.DownTo(start, last);
        var result = await _parts.Check
            .CheckAsync(watch.Addresses, watch.Filter, feed, start, downTo, cancellationToken).ConfigureAwait(false);
        var checkedNow = WatchSchedule.After(last?.Height ?? start, result, result.Problems, height, now);

        // What the check fetched now, and what it rests on that an earlier check of the round already had in memory
        // (a settled page, a block's hash), so that every hit's origins come into the case with it (principle 1).
        var fetched = tipResponses.Concat(scratch.SealedSince(SealedResponses.Chain, mark))
            .Concat(result.Origins)
            .ToList();

        // Only a hit not known to the case and not told before is said: one in the look-back the open Chainsaw already
        // sealed is never told again, and one an endpoint indexed late is told when it is first seen. One below the
        // look-back (a history page reaches further back) was read by an earlier check, which sealed or told it.
        var alerted = progress.Alerted.ToList();
        var fresh = false;
        foreach (var match in result.Matches.Where(match => match.Block.Height > downTo))
        {
            var key = WatchSchedule.Key(watch.WatchId, match);
            if (!alerted.Contains(key, StringComparer.Ordinal) && !watch.Known.Contains(key, StringComparer.Ordinal))
            {
                alerted.Add(key);
                fresh = true;
            }
        }

        _parts.Queue.Add(new QueuedCheck(
            Guid.NewGuid(), caseId, watch.WatchId, QueuedKind.Checked, watch.Filter, start, height, checkedNow,
            result.Matches, result.Origins, Responses(scratch, fetched), now)
        {
            HasNewHits = fresh,
        });
        if (fresh && !round.Alerted)
        {
            // Said at once, once a round, before the progress that remembers it is written: a stop later in the round
            // never swallows it.
            round.Alerted = true;
            _parts.Alerts.Raise(_plan.Alerts);
        }

        round.Failing |= result.Status == WatchFeedStatus.Failed;
        _progressChanged = true;
        _progress[(caseId, watch.WatchId)] = progress with
        {
            StartHeight = progress.StartHeight ?? start,
            Checked = checkedNow,
            Alerted = alerted.Count > AlertedKept ? alerted[^AlertedKept..] : alerted,
        };
        KeepProgress();
        return true;
    }

    /// <summary>Reads the responses sealed in the case in memory, to go into the queue.</summary>
    /// <param name="scratch">The case in memory.</param>
    /// <param name="sequences">Their numbers.</param>
    /// <returns>The responses.</returns>
    private IReadOnlyList<SealedResponse> Responses(IMemoryCase scratch, IReadOnlyList<long> sequences) =>
        sequences.Count == 0
            ? []
            : scratch.Read(reader => sequences.Distinct()
                .Select(sequence => _parts.Transfer.Read(reader, sequence))
                .OfType<SealedResponse>()
                .ToList());

    /// <summary>
    /// Tries the chain's endpoints when they have not been tried since the process started, or again after a while
    /// when none is in step, and gives the one in step that is asked first.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="cancellationToken">Stops the probe.</param>
    /// <returns>Its name, or <see langword="null"/>.</returns>
    private async Task<string?> InStepAsync(Chain chain, CancellationToken cancellationToken)
    {
        var state = _chains.TryGetValue(chain, out var known) ? known : _chains[chain] = new ChainState();
        var now = _parts.Clock.UtcNow;
        var found = InStep(_parts.Sources.Get(chain));
        if (state.ProbedAt is null || (found is null && now - state.ProbedAt >= ProbeAgain))
        {
            state.ProbedAt = now;
            await _parts.Sources.ProbeAsync(chain, cancellationToken).ConfigureAwait(false);
            found = InStep(_parts.Sources.Get(chain));
        }

        return found;
    }

    /// <summary>Gives the first endpoint in step with the chain, as the lookups in step choose them.</summary>
    /// <param name="view">The chain's sources.</param>
    /// <returns>Its name, or <see langword="null"/>.</returns>
    private static string? InStep(ChainSourcesView view) => view.Endpoints
        .FirstOrDefault(each => each.Endpoint.Enabled && !each.FailedSince
            && each.Probe is { Outcome: ProbeOutcome.Answered } probe && (probe.BlocksBehind ?? 0) == 0)
        ?.Endpoint.Name;

    /// <summary>Reads the plan again when Chainsaw has written it since.</summary>
    private void ReadPlan()
    {
        var written = _parts.Plan.WrittenAt();
        if (written == _planWritten && _planWritten is not null)
        {
            return;
        }

        // A plan that cannot be read just now (held by Chainsaw's write or a scanner) keeps the last one, and is read
        // again at the next round; only a missing or foreign plan means nothing to watch.
        if (_parts.Plan.TryRead(out var plan))
        {
            _planWritten = written;
            _plan = plan;
            _planRead = true;
        }
    }

    /// <summary>Gives a watch's start: the case's, or the one the background set.</summary>
    /// <param name="caseId">The case.</param>
    /// <param name="watch">The watch.</param>
    /// <returns>The start, or <see langword="null"/>.</returns>
    private long? StartOf(Guid caseId, BackgroundWatch watch) =>
        watch.StartHeight ?? _progress.GetValueOrDefault((caseId, watch.WatchId))?.StartHeight;

    /// <summary>Gives a watch's progress, new when there is none.</summary>
    /// <param name="caseId">The case.</param>
    /// <param name="watchId">The watch.</param>
    /// <returns>The progress.</returns>
    private BackgroundProgress ProgressOf(Guid caseId, string watchId) =>
        _progress.GetValueOrDefault((caseId, watchId)) ?? new BackgroundProgress(caseId, watchId, null, null, []);

    /// <summary>Gives the later of two checks: the case's (in the plan) or the background's own.</summary>
    /// <param name="plan">The case's.</param>
    /// <param name="own">The background's.</param>
    /// <returns>The later, or <see langword="null"/>.</returns>
    private static WatchChecked? Later(WatchChecked? plan, WatchChecked? own)
    {
        if (plan is null || own is null)
        {
            return plan ?? own;
        }

        return own.CheckedUtc > plan.CheckedUtc ? own : plan;
    }

    /// <summary>
    /// Writes the progress when it changed, keeping only the watches still in the plan, so that a quiet round writes
    /// nothing to the disk.
    /// </summary>
    private void KeepProgress()
    {
        var inPlan = _plan.Cases
            .SelectMany(@case => @case.Watches.Select(watch => (@case.CaseId, watch.WatchId)))
            .ToHashSet();
        var kept = _progress.Where(each => inPlan.Contains(each.Key)).ToDictionary();
        if (!_progressChanged && kept.Count == _progress.Count)
        {
            return;
        }

        _progress = kept;
        try
        {
            _parts.Progress.Write([.. _progress.Values]);
            _progressChanged = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.Security.Cryptography.CryptographicException)
        {
            // A file held for a moment (a virus scanner): written again at the next change or round.
            _progressChanged = true;
        }
    }

    /// <summary>Writes the status when it has changed.</summary>
    /// <param name="state">What it does.</param>
    /// <param name="inStepWith">The endpoint in step, if any.</param>
    private void Say(BackgroundState state, string? inStepWith)
    {
        if (_said == (state, inStepWith))
        {
            return;
        }

        _said = (state, inStepWith);
        try
        {
            _parts.Status.Write(new BackgroundStatus(state, inStepWith, _parts.Clock.UtcNow, Environment.ProcessId));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The status is only a word to Chainsaw; it is written again at the next change.
            _said = null;
        }
    }

    /// <summary>What a round has seen.</summary>
    private sealed class Round
    {
        /// <summary>Gets or sets whether a lookup failed.</summary>
        public bool Failing { get; set; }

        /// <summary>Gets or sets whether the round has already said that a watch has hit.</summary>
        public bool Alerted { get; set; }

        /// <summary>Gets or sets how many chains it had.</summary>
        public int Chains { get; set; }

        /// <summary>Gets or sets how many of them were off.</summary>
        public int ChainsOff { get; set; }

        /// <summary>Gets or sets the endpoint in step asked first.</summary>
        public string? InStepWith { get; set; }
    }

    /// <summary>What the loop knows about one chain.</summary>
    private sealed class ChainState
    {
        /// <summary>Gets or sets whether the last ask for the height gave nothing.</summary>
        public bool HeightFailed { get; set; }

        /// <summary>Gets or sets when the height may be asked next.</summary>
        public DateTimeOffset NextHeight { get; set; } = DateTimeOffset.MinValue;

        /// <summary>Gets or sets when the endpoints were last tried.</summary>
        public DateTimeOffset? ProbedAt { get; set; }
    }
}

/// <summary>The services one round uses, gathered so that the loop's constructor stays readable.</summary>
/// <param name="Plan">The plan Chainsaw writes.</param>
/// <param name="Progress">The background's own progress.</param>
/// <param name="Queue">The queue.</param>
/// <param name="Holds">Which cases are open in Chainsaw.</param>
/// <param name="Status">The status file.</param>
/// <param name="Check">The check, the same as in the open Chainsaw.</param>
/// <param name="Feeds">The feeds, the same as in the open Chainsaw.</param>
/// <param name="Sources">The chains' endpoints.</param>
/// <param name="Bitcoin">Bitcoin's source.</param>
/// <param name="Ethereum">Ethereum's source.</param>
/// <param name="Transfer">Reads the responses out of the case in memory.</param>
/// <param name="Alerts">Says that a watch has hit.</param>
/// <param name="Clock">The clock.</param>
internal sealed record WatcherParts(
    IBackgroundPlan Plan,
    IBackgroundProgressFile Progress,
    IWatchQueue Queue,
    ICaseHolds Holds,
    IBackgroundStatusFile Status,
    IWatchCheck Check,
    IWatchFeeds Feeds,
    IChainSources Sources,
    IBitcoinSource Bitcoin,
    IEthereumSource Ethereum,
    IResponseTransfer Transfer,
    IAlerts Alerts,
    IClock Clock);
