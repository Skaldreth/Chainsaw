using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>An endpoint to ask, with the sort of API it speaks.</summary>
/// <param name="Endpoint">The endpoint.</param>
/// <param name="Api">Its sort of API.</param>
internal sealed record PlannedEndpoint(SourceEndpoint Endpoint, ISourceApi Api)
{
    /// <summary>
    /// Gets its group in the plan, best first: the lookups are spread only over the endpoints of the best group whose
    /// turn comes soon (<see cref="EndpointPacer.Patience"/>). In Snabbast the groups are those that answered right,
    /// those not tried, those behind, those whose probe failed, and each of them again while a failed lookup's pause
    /// lasts; in Prioriterad every endpoint is a group of its own, so that the user's order holds.
    /// </summary>
    public int Tier { get; init; }
}

/// <summary>Which endpoints a lookup asks, in order; none when the chain is off.</summary>
/// <param name="Off">Whether the chain is <see cref="SourceMode.Off"/>.</param>
/// <param name="Endpoints">The endpoints to ask, first first; the next is asked when one fails.</param>
internal sealed record SourcePlan(bool Off, IReadOnlyList<PlannedEndpoint> Endpoints)
{
    /// <summary>
    /// Gets whether endpoints in step with the chain were asked for and some are on, but none is known to be in step.
    /// </summary>
    public bool NoneInStep { get; init; }
}

/// <summary> The chains' endpoints, their modes and their probes (sketch KL1a), kept in <c>sources.json</c> in the
/// program's data. Every probe goes the endpoint's way (<see cref="SendAsync"/>) and asks only for the chain's height
/// (and, in the form that adds an endpoint, the hash of the block at that height); nothing is probed while the vault is
/// locked, and a round in which the exit refused is not kept. The lookups ask the endpoints in the order <see
/// cref="Plan"/> gives, each at its own pace (<see cref="Pacer"/>). An endpoint marked as the user's own node is asked
/// straight in the user's own network through <see cref="ILocalHttp"/>, and every other through <see cref="IExitHttp"/>
/// (<see cref="SendAsync"/>); nothing else chooses the way.
/// </summary>
internal sealed class ChainSources : IChainSources
{
    /// <summary>How long an endpoint has to answer a probe; a slower one is not the fastest anyway.</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How many blocks below the highest an endpoint may be and still count as answering right: one block behind is
    /// not behind for an ordinary lookup, since the information comes in step by step (decided 2026-11-07).
    /// </summary>
    public const int HeightMargin = 1;

    /// <summary>How long an endpoint that failed a lookup is passed over first; each new failure doubles it.</summary>
    public static readonly TimeSpan FirstPause = TimeSpan.FromMinutes(1);

    /// <summary>The longest an endpoint that keeps failing is passed over before it is tried in its place
    /// again.</summary>
    public static readonly TimeSpan LongestPause = TimeSpan.FromMinutes(30);

    /// <summary>What a probe says when the vault is locked.</summary>
    private const string LockedText = "Valvet är låst. Inget skickades.";

    /// <summary>What a probe says when Chainsaw's own pace for the endpoint left no room in time.</summary>
    private const string PacedText = "Chainsaws egen takt för endpointen gav ingen plats i tid. Inget skickades.";

    /// <summary>How a probe's words begin when the endpoint asked Chainsaw to wait.</summary>
    private const string AskedText = "Bad Chainsaw vänta";

    /// <summary>The sorts of API, by code.</summary>
    private readonly IReadOnlyDictionary<string, ISourceApi> _apis;

    /// <summary>The only way out.</summary>
    private readonly IExitHttp _http;

    /// <summary>
    /// The way to the user's own node, or <see langword="null"/> when there is none (an endpoint marked local then
    /// fails, and nothing goes another way).
    /// </summary>
    private readonly ILocalHttp? _local;

    /// <summary>Waits, so that the tests can move the time instead.</summary>
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>The API keys, or <see langword="null"/> when there is no vault (a key is then never found).</summary>
    private readonly IApiKeyStore? _keys;

    /// <summary>The vault, whose lock stops every probe; <see langword="null"/> counts as unlocked.</summary>
    private readonly IVault? _vault;

    /// <summary>The time of a probe.</summary>
    private readonly IClock _clock;

    /// <summary>The file in the program's data.</summary>
    private readonly SourcesFile _file;

    /// <summary>Guards <see cref="_chains"/>.</summary>
    private readonly Lock _gate = new();

    /// <summary>Each chain's state.</summary>
    private readonly Dictionary<Chain, ChainState> _chains = [];

    /// <summary>Creates the sources from the file, with each chain's defaults where the file says nothing.</summary>
    /// <param name="apis">The sorts of API Chainsaw speaks.</param>
    /// <param name="http">The only way out.</param>
    /// <param name="keys">The API keys in the vault, if any.</param>
    /// <param name="vault">The vault, if any.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">Where the file is.</param>
    /// <param name="local">The way to the user's own node, if any.</param>
    /// <param name="delay">Waits for an endpoint's turn; <see cref="Task.Delay(TimeSpan, CancellationToken)"/> when
    /// <see langword="null"/>.</param>
    public ChainSources(
        IEnumerable<ISourceApi> apis, IExitHttp http, IApiKeyStore? keys, IVault? vault, IClock clock,
        ChainSourcesOptions options, ILocalHttp? local = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(apis);
        ArgumentNullException.ThrowIfNull(options);
        _apis = apis.ToDictionary(api => api.Info.Code, StringComparer.Ordinal);
        _http = http;
        _local = local;
        _delay = delay ?? Task.Delay;
        Pacer = new EndpointPacer(clock);
        _keys = keys;
        _vault = vault;
        _clock = clock;
        _file = new SourcesFile(options.Directory);
        Chains = [.. _apis.Values.Select(api => api.Info.Chain).Distinct().Order()];
        var saved = _file.Load(_apis);
        foreach (var chain in Chains)
        {
            var state = saved.TryGetValue(chain, out var found)
                ? new ChainState(found.Mode, [.. found.Endpoints])
                : new ChainState(SourceMode.Fastest, [.. _apis.Values.Where(a => a.Info.Chain == chain)
                    .SelectMany(api => api.Defaults)]);
            _chains[chain] = state;
        }
    }

    /// <inheritdoc/>
    public event EventHandler<Chain>? Changed;

    /// <inheritdoc/>
    public IReadOnlyList<Chain> Chains { get; }

    /// <summary>Gets each endpoint's pace and its requests to wait, shared by the lookups and the probes.</summary>
    public EndpointPacer Pacer { get; }

    /// <inheritdoc/>
    public IReadOnlyList<SourceApiInfo> Apis(Chain chain) =>
        [.. _apis.Values.Where(api => api.Info.Chain == chain).Select(api => api.Info)];

    /// <inheritdoc/>
    public ChainSourcesView Get(Chain chain)
    {
        lock (_gate)
        {
            var state = State(chain);
            var next = Order(state).FirstOrDefault();
            var first = next is not null && Usable(state, next) ? next.Id : (Guid?)null;
            var endpoints = state.Endpoints.Select(endpoint => new EndpointView(
                endpoint,
                state.Probes.GetValueOrDefault(endpoint.Id),
                state.FailedSince.ContainsKey(endpoint.Id),
                endpoint.Id == first)
            {
                WaitUntil = Pacer.WaitUntil(endpoint.Id),
            }).ToList();
            var now = _clock.UtcNow;
            var waiting = state.Waits.Where(until => until > now).DefaultIfEmpty().Max();
            return new ChainSourcesView(chain, state.Mode, endpoints, state.ProbedAt)
            {
                WaitingUntil = waiting == default ? null : waiting,
            };
        }
    }

    /// <summary>Gives the endpoints a lookup on the chain asks, in order.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="inStep">
    /// Whether only endpoints in step with the chain may be asked (<see cref="LookupMode.InStep"/>, for the
    /// monitoring): those whose latest probe answered with the round's highest height, not even one block behind.
    /// </param>
    /// <returns>The plan; off, or the endpoints that are on.</returns>
    public SourcePlan Plan(Chain chain, bool inStep = false)
    {
        lock (_gate)
        {
            var state = State(chain);
            if (state.Mode == SourceMode.Off)
            {
                return new SourcePlan(true, []);
            }

            var order = Order(state);
            var chosen = inStep ? [.. order.Where(endpoint => InStep(state, endpoint))] : order;
            var now = _clock.UtcNow;
            return new SourcePlan(false, [.. chosen.Select((e, index) => new PlannedEndpoint(e, _apis[e.Api])
            {
                Tier = state.Mode == SourceMode.Priority ? index : Tier(state, e, now),
            })])
            {
                NoneInStep = inStep && chosen.Count == 0 && order.Count > 0,
            };
        }
    }

    /// <summary>
    /// Sends a request to an endpoint the only way it may go: straight to the user's own node when the endpoint is
    /// marked local, and through the exit for every other. A local endpoint without a way to the own node fails; it
    /// never goes through the exit, nor another endpoint around it.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="request">The request under its address.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ExitNotConnectedException">Through the exit, and it does not allow lookups now.</exception>
    /// <exception cref="ExitException">The request failed.</exception>
    public Task<ExitResponse> SendAsync(
        SourceEndpoint endpoint, ExitRequest request, CancellationToken cancellationToken) =>
        endpoint.Local
            ? _local?.SendAsync(request, cancellationToken)
                ?? Task.FromException<ExitResponse>(new ExitException(ExitError.RequestFailed))
            : _http.SendAsync(request, cancellationToken);

    /// <summary>Waits for an endpoint's turn.</summary>
    /// <param name="wait">How long.</param>
    /// <param name="cancellationToken">Stops the wait.</param>
    /// <returns>A task that completes when the turn has come.</returns>
    public Task DelayAsync(TimeSpan wait, CancellationToken cancellationToken) =>
        wait <= TimeSpan.Zero ? Task.CompletedTask : _delay(wait, cancellationToken);

    /// <summary>
    /// Notes that a lookup on the chain waits for an endpoint until a time, so that the settings and the status line
    /// can say so honestly, and tells the change.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="until">Until when, in UTC.</param>
    public void Waiting(Chain chain, DateTimeOffset until)
    {
        lock (_gate)
        {
            State(chain).Waits.Add(until);
        }

        Tell(chain);
    }

    /// <summary>Notes that a lookup's wait noted by <see cref="Waiting"/> is over, and tells the change.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="until">The time it was noted with.</param>
    public void DoneWaiting(Chain chain, DateTimeOffset until)
    {
        lock (_gate)
        {
            State(chain).Waits.Remove(until);
        }

        Tell(chain);
    }

    /// <summary>
    /// Notes that an endpoint asked Chainsaw to wait (<c>429</c> or <c>Retry-After</c>): the others are asked until
    /// then, without counting it as failed, and it comes back by itself.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="id">The endpoint.</param>
    /// <param name="wait">How long it asked for.</param>
    /// <returns>Until when it is waited for, in UTC.</returns>
    public DateTimeOffset AskedToWait(Chain chain, Guid id, TimeSpan wait)
    {
        var until = Pacer.AskedToWait(id, wait);
        Tell(chain);
        return until;
    }

    /// <summary>
    /// Notes that a lookup through an endpoint failed or did not answer in time, so that the others are asked before
    /// it until it is tried again or answers, or until its pause is over (<see cref="FirstPause"/>, doubled for each
    /// failure in a row up to <see cref="LongestPause"/>): then it is asked in its place again, so that it comes back
    /// when it answers again.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="id">The endpoint.</param>
    public void Failed(Chain chain, Guid id) => Note(chain, id, failed: true);

    /// <summary>Notes that a lookup through an endpoint was answered, so that an earlier failure is
    /// forgotten.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="id">The endpoint.</param>
    public void Answered(Chain chain, Guid id) => Note(chain, id, failed: false);

    /// <summary>
    /// Gets whether an endpoint's <em>not found</em> can be believed: it has not failed a lookup since it was tried,
    /// and its latest probe did not fail or show it behind. One not tried yet is believed only before the chain's
    /// first round, so that a txid nobody knows is not sent to every endpoint; one added after a round (perhaps with
    /// a wrong path) is believed once a round has shown that it answers.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="id">The endpoint.</param>
    /// <returns><see langword="true"/> when it can.</returns>
    public bool TrustsNotFound(Chain chain, Guid id)
    {
        lock (_gate)
        {
            var state = State(chain);
            var probe = state.Probes.GetValueOrDefault(id);
            return !state.FailedSince.ContainsKey(id)
                && (probe?.Outcome == ProbeOutcome.Answered || (probe is null && state.ProbedAt is null));
        }
    }

    /// <summary>Gives the header an endpoint's key is sent in, from the vault.</summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="api">Its sort of API.</param>
    /// <param name="request">The request without a key.</param>
    /// <param name="problem">Why the key cannot be sent, in Swedish, or <see langword="null"/>.</param>
    /// <returns>The request with its key, or <see langword="null"/> when nothing may be sent.</returns>
    public ExitRequest? WithKey(SourceEndpoint endpoint, ISourceApi api, ExitRequest request, out string? problem) =>
        WithKey(endpoint.ApiKey, api, request, out problem);

    /// <inheritdoc/>
    public Task SetModeAsync(Chain chain, SourceMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The mode is not known.");
        }

        return Change(chain, state => state.Mode = mode);
    }

    /// <inheritdoc/>
    public (SourceEndpoint Endpoint, Task Saved) Add(
        Chain chain, Uri address, string api, string? apiKey, bool local = false)
    {
        var (checkedAddress, sort) = Check(chain, address, api, local);
        var key = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        var name = SourceAddress.NameOf(checkedAddress);
        var endpoint = new SourceEndpoint(
            Guid.NewGuid(), chain, name[..Math.Min(name.Length, SourceAddress.MaximumNameLength)], checkedAddress,
            sort.Info.Code, key, Enabled: true)
        {
            Local = local,
            Interval = local ? SourcePace.Local : SourcePace.Public,
        };
        return (endpoint, Change(chain, state => state.Endpoints.Add(endpoint)));
    }

    /// <inheritdoc/>
    public Task RemoveAsync(Chain chain, Guid id) => Change(chain, state =>
    {
        state.Endpoints.RemoveAll(endpoint => endpoint.Id == id);
        state.Probes.Remove(id);
        state.FailedSince.Remove(id);
        state.OnChain.Remove(id);
        Pacer.Forget(id);
    });

    /// <inheritdoc/>
    public Task SetEnabledAsync(Chain chain, Guid id, bool enabled) => Change(chain, state =>
    {
        var index = state.Endpoints.FindIndex(endpoint => endpoint.Id == id);
        if (index >= 0)
        {
            state.Endpoints[index] = state.Endpoints[index] with { Enabled = enabled };
        }
    });

    /// <inheritdoc/>
    public Task MoveAsync(Chain chain, Guid id, int steps) => Change(chain, state =>
    {
        var index = state.Endpoints.FindIndex(endpoint => endpoint.Id == id);
        if (index < 0)
        {
            return;
        }

        var endpoint = state.Endpoints[index];
        state.Endpoints.RemoveAt(index);
        state.Endpoints.Insert(Math.Clamp(index + steps, 0, state.Endpoints.Count), endpoint);
    });

    /// <inheritdoc/>
    public async Task<ProbeRun> ProbeAsync(Chain chain, CancellationToken cancellationToken = default)
    {
        List<PlannedEndpoint> endpoints;
        int round;
        lock (_gate)
        {
            var state = State(chain);
            if (state.Mode == SourceMode.Off)
            {
                return ProbeRun.Off;
            }

            endpoints = [.. state.Endpoints.Where(e => e.Enabled).Select(e => new PlannedEndpoint(e, _apis[e.Api]))];
            if (endpoints.Count == 0)
            {
                return ProbeRun.NoneOn;
            }

            if (IsLocked)
            {
                return ProbeRun.NotSent;
            }

            // Counted only for a round that is sent, so that one that sends nothing never drops a running one.
            round = ++state.Round;
        }

        EndpointProbe[] probes;
        try
        {
            probes = await Task.WhenAll(endpoints.Select(planned => ProbeOneAsync(
                planned.Endpoint, planned.Api, withHash: false, cancellationToken))).ConfigureAwait(false);
        }
        catch (ExitException exception) when (IsTheWayOuts(exception))
        {
            // The exit did not allow lookups (or stopped allowing them): nothing more is sent, and the round says
            // nothing about the endpoints, so the earlier results stand.
            return ProbeRun.NotSent;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (IsLocked || probes.Any(probe => probe.Problem == LockedText))
        {
            // The vault locked during the round: what was not sent says nothing about the endpoints.
            return ProbeRun.NotSent;
        }

        Keep(chain, round, probes);
        return ProbeRun.Probed;
    }

    /// <inheritdoc/>
    public async Task<EndpointProbe> TryAsync(
        Chain chain, Uri address, string api, string? apiKey, bool local = false,
        CancellationToken cancellationToken = default)
    {
        var (checkedAddress, sort) = Check(chain, address, api, local);
        if (IsLocked)
        {
            return NotSent(Guid.Empty, LockedText);
        }

        try
        {
            var key = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
            var candidate = new SourceEndpoint(
                Guid.Empty, chain, SourceAddress.NameOf(checkedAddress), checkedAddress, sort.Info.Code, key, true)
            {
                Local = local,
            };
            return await ProbeOneAsync(candidate, sort, withHash: true, cancellationToken).ConfigureAwait(false);
        }
        catch (ExitException exception) when (IsTheWayOuts(exception))
        {
            return NotSent(Guid.Empty, exception.Message);
        }
    }

    /// <summary>Gets whether the vault is locked, so that nothing may be asked (CLAUDE.md, Säkerhet).</summary>
    private bool IsLocked => _vault is not null && _vault.State != VaultState.Unlocked;

    /// <summary>
    /// Orders a chain's endpoints that are on for a lookup. In Fastest, those that answered right come first, the
    /// fastest first, then those not tried, then one that lags behind in height by more than
    /// <see cref="HeightMargin"/> (only asked when no other answers, so that switching off the one that was highest
    /// never leaves the chain without a source), then those that failed their probe. In Priority, the user's order,
    /// with those that failed their probe after the others. Either way an endpoint that failed a lookup since it was
    /// tried comes last while its pause lasts, and in its place again after it. An endpoint whose latest probe showed
    /// it is not on the chain (<see cref="ChainCheckAsync"/>) is never asked.
    /// </summary>
    /// <param name="state">The chain's state, under the lock.</param>
    /// <returns>The endpoints to ask, first first.</returns>
    private List<SourceEndpoint> Order(ChainState state)
    {
        if (state.Mode == SourceMode.Off)
        {
            return [];
        }

        var now = _clock.UtcNow;
        var on = state.Endpoints.Where(endpoint =>
            endpoint.Enabled && state.OnChain.GetValueOrDefault(endpoint.Id, true));
        on = state.Mode == SourceMode.Fastest
            ? on.OrderBy(endpoint => Rank(state.Probes.GetValueOrDefault(endpoint.Id)))
                .ThenBy(endpoint => state.Probes.GetValueOrDefault(endpoint.Id)?.Duration ?? TimeSpan.MaxValue)
            : on.OrderBy(endpoint => state.Probes.GetValueOrDefault(endpoint.Id)?.Outcome == ProbeOutcome.Failed);
        return [.. on.OrderBy(endpoint =>
            state.FailedSince.TryGetValue(endpoint.Id, out var failure) && failure.PausedUntil > now)];
    }

    /// <summary>
    /// Gives an endpoint's group in Snabbast: its probe's rank, and worse while a failed lookup's pause lasts.
    /// </summary>
    /// <param name="state">The chain's state, under the lock.</param>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="now">The time now.</param>
    /// <returns>The group, lowest best.</returns>
    private static int Tier(ChainState state, SourceEndpoint endpoint, DateTimeOffset now)
    {
        var paused = state.FailedSince.TryGetValue(endpoint.Id, out var failure) && failure.PausedUntil > now;
        return Rank(state.Probes.GetValueOrDefault(endpoint.Id)) + (paused ? 10 : 0);
    }

    /// <summary>
    /// Gets whether an endpoint is known to be in step with the chain: its latest probe answered with the highest
    /// height of its round, and no lookup through it has failed since.
    /// </summary>
    /// <param name="state">The chain's state, under the lock.</param>
    /// <param name="endpoint">The endpoint.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool InStep(ChainState state, SourceEndpoint endpoint) =>
        !state.FailedSince.ContainsKey(endpoint.Id)
        && state.Probes.GetValueOrDefault(endpoint.Id) is { Outcome: ProbeOutcome.Answered } probe
        && (probe.BlocksBehind ?? 0) == 0;

    /// <summary>
    /// Gets whether an endpoint can be shown as the one used (★): it did not fail its probe or a lookup since. An
    /// endpoint not tried yet counts, since the next lookup asks it.
    /// </summary>
    /// <param name="state">The chain's state, under the lock.</param>
    /// <param name="endpoint">The endpoint.</param>
    /// <returns><see langword="true"/> when it is expected to answer.</returns>
    private static bool Usable(ChainState state, SourceEndpoint endpoint) =>
        !state.FailedSince.ContainsKey(endpoint.Id)
        && state.Probes.GetValueOrDefault(endpoint.Id)?.Outcome is not (ProbeOutcome.Failed or ProbeOutcome.NotSent);

    /// <summary>Ranks a probe for Fastest: answered, not tried, behind, failed.</summary>
    /// <param name="probe">The probe, if any.</param>
    /// <returns>The rank, lowest first.</returns>
    private static int Rank(EndpointProbe? probe) => probe?.Outcome switch
    {
        ProbeOutcome.Answered => 0,
        ProbeOutcome.Behind => 2,
        ProbeOutcome.Failed => 3,
        _ => 1,
    };

    /// <summary>
    /// Marks every answer more than <see cref="HeightMargin"/> blocks lower than the highest as behind: such an
    /// endpoint has not seen the newest blocks, and a lookup through it could miss transactions the others know. One
    /// block lower still answers right, with how far behind it was noted, so that only the monitoring passes it over
    /// (<see cref="LookupMode.InStep"/>).
    /// </summary>
    /// <param name="probes">The round's probes.</param>
    /// <returns>The probes with those behind marked.</returns>
    internal static EndpointProbe[] Ranked(EndpointProbe[] probes)
    {
        var highest = probes.Where(p => p.Outcome == ProbeOutcome.Answered).Max(p => p.Height);
        return [.. probes.Select(probe => probe.Outcome == ProbeOutcome.Answered && probe.Height < highest
            ? highest - probe.Height > HeightMargin
                ? probe with
                {
                    Outcome = ProbeOutcome.Behind,
                    BlocksBehind = highest - probe.Height,
                    Problem = string.Create(CultureInfo.InvariantCulture,
                            $"Höjd {probe.Height:N0} mot {highest:N0}: {highest - probe.Height} block efter. ")
                        .Replace(',', ' ') + "Används bara när ingen annan svarar.",
                }
                : probe with { BlocksBehind = highest - probe.Height }
            : probe)];
    }

    /// <summary>
    /// Tries one endpoint: the chain's height, timed, and when asked the hash of the block at that height. An endpoint
    /// that has been added waits for its turn (<see cref="Pacer"/>); one that asks Chainsaw to wait is not counted as
    /// failed. The exit's refusal is thrown on, so that a round can be dropped.
    /// </summary>
    /// <param name="endpoint">The endpoint, with <see cref="Guid.Empty"/> as id when it is not added yet.</param>
    /// <param name="api">Its sort of API.</param>
    /// <param name="withHash">Whether to ask for the block's hash too.</param>
    /// <param name="cancellationToken">Stops the probe.</param>
    /// <returns>What it answered.</returns>
    /// <exception cref="ExitNotConnectedException">The exit does not allow lookups; nothing was sent.</exception>
    private async Task<EndpointProbe> ProbeOneAsync(
        SourceEndpoint endpoint, ISourceApi api, bool withHash, CancellationToken cancellationToken)
    {
        var (id, address, apiKey) = (endpoint.Id, endpoint.Address, endpoint.ApiKey);
        var at = _clock.UtcNow;
        if (IsLocked)
        {
            return NotSent(id, LockedText, at);
        }

        if (WithKey(apiKey, api, api.HeightRequest(address), out var problem) is not { } heightRequest)
        {
            return NotSent(id, problem!, at);
        }

        var (mayAsk, turn) = await TurnAsync(endpoint, api, cancellationToken).ConfigureAwait(false);
        if (!mayAsk)
        {
            return NotSent(id, Pacer.WaitUntil(id) is { } until ? WaitText(until) : PacedText, at);
        }

        var (height, heightProblem) = await AskAsync(endpoint, heightRequest, api.ReadHeight, turn, cancellationToken)
            .ConfigureAwait(false);
        if (Pacer.WaitUntil(id) is { } busy && height is null)
        {
            return NotSent(id, WaitText(busy), at);
        }

        if (height is not { } answer)
        {
            return new EndpointProbe(id, ProbeOutcome.Failed, null, null, null, at, heightProblem);
        }

        if (await ChainCheckAsync(endpoint, api, answer.Value, at, cancellationToken).ConfigureAwait(false)
            is { } offChain)
        {
            return new EndpointProbe(id, ProbeOutcome.Failed, answer.Value, null, answer.Duration, at, offChain);
        }

        string? hash = null;
        if (withHash)
        {
            if (IsLocked)
            {
                return NotSent(id, LockedText, at);
            }

            if (WithKey(apiKey, api, api.HashRequest(address, answer.Value), out var hashKeyProblem)
                is not { } hashRequest)
            {
                return NotSent(id, hashKeyProblem!, at);
            }

            var (read, hashProblem) = await AskAsync(endpoint, hashRequest, api.ReadHash, null, cancellationToken)
                .ConfigureAwait(false);
            if (read is null)
            {
                return new EndpointProbe(id, ProbeOutcome.Failed, answer.Value, null, answer.Duration, at,
                    "Höjden kom, men inte blockets hash: " + hashProblem);
            }

            hash = read.Value.Value;
        }

        return new EndpointProbe(id, ProbeOutcome.Answered, answer.Value, hash, answer.Duration, at, null);
    }

    /// <summary>
    /// Checks that an endpoint answers for its chain and network, so that a test network or another chain added by
    /// mistake, or an endpoint that lies about the height, never becomes the height every answer's depth is counted
    /// from, nor the endpoint asked first. A height above what the chain can have come to by now is refused; the hash
    /// of the first block (height 0) is then asked for, once per endpoint, and must be the chain's own. An endpoint
    /// shown not to be on the chain is never asked by a lookup (<see cref="Order"/>) until a probe shows that it is;
    /// a first block that cannot be had shows nothing either way, and is asked for again in the next round.
    /// </summary>
    /// <param name="endpoint">The endpoint, with <see cref="Guid.Empty"/> as id when it is not added yet.</param>
    /// <param name="api">Its sort of API.</param>
    /// <param name="height">The height it answered.</param>
    /// <param name="at">When it was tried.</param>
    /// <param name="cancellationToken">Stops the probe.</param>
    /// <returns>Why it is not on the chain, in Swedish, or <see langword="null"/> when nothing shows that.</returns>
    /// <exception cref="ExitNotConnectedException">The exit does not allow lookups; nothing was sent.</exception>
    private async Task<string?> ChainCheckAsync(
        SourceEndpoint endpoint, ISourceApi api, long height, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (ChainLimits.HighestPlausible(api.ChainName, at) is { } most && height > most)
        {
            NoteChain(endpoint, onChain: false);
            var shown = height.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');
            return $"Höjden {shown} är högre än kedjan kan ha nått, så endpointen svarar inte för kedjan. "
                + "Den används inte.";
        }

        if (ChainLimits.Genesis(api.ChainName) is not { } genesis || IsOnChain(endpoint) || IsLocked
            || WithKey(endpoint.ApiKey, api, api.HashRequest(endpoint.Address, 0), out _) is not { } request)
        {
            return null;
        }

        var (mayAsk, turn) = await TurnAsync(endpoint, api, cancellationToken).ConfigureAwait(false);
        if (!mayAsk)
        {
            return null;
        }

        var asked = await AskAsync(endpoint, request, api.ReadHash, turn, cancellationToken).ConfigureAwait(false);
        if (asked.Answer is not { } first)
        {
            return null;
        }

        var same = string.Equals(first.Value, genesis, StringComparison.OrdinalIgnoreCase);
        NoteChain(endpoint, same);
        return same
            ? null
            : $"Kedjans första block har hashen {first.Value} hos endpointen, inte {genesis}: den svarar för en "
                + "annan kedja eller ett annat nät. Den används inte.";
    }

    /// <summary>Gets whether a probe has shown an added endpoint to be on its chain.</summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <returns><see langword="true"/> when it has.</returns>
    private bool IsOnChain(SourceEndpoint endpoint)
    {
        lock (_gate)
        {
            return _chains.TryGetValue(endpoint.Chain, out var state)
                && state.OnChain.GetValueOrDefault(endpoint.Id);
        }
    }

    /// <summary>Notes whether an added endpoint is on its chain; an address not yet added is not noted.</summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="onChain">Whether it is.</param>
    private void NoteChain(SourceEndpoint endpoint, bool onChain)
    {
        lock (_gate)
        {
            if (endpoint.Id != Guid.Empty && _chains.TryGetValue(endpoint.Chain, out var state))
            {
                state.OnChain[endpoint.Id] = onChain;
            }
        }
    }

    /// <summary>
    /// Waits for an added endpoint's turn before a probe asks it, unless its turn is further away than a probe waits
    /// or the endpoint asked Chainsaw to wait meanwhile; a turn not used is given back.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="api">Its sort of API.</param>
    /// <param name="cancellationToken">Stops the wait.</param>
    /// <returns>
    /// Whether it may be asked now, and the turn taken (<see langword="null"/> for an address not yet added).
    /// </returns>
    private async Task<(bool MayAsk, EndpointTurn? Turn)> TurnAsync(
        SourceEndpoint endpoint, ISourceApi api, CancellationToken cancellationToken)
    {
        if (endpoint.Id == Guid.Empty)
        {
            return (true, null);
        }

        var (turn, _) = Pacer.Take([new PlannedEndpoint(endpoint, api)], ProbeTimeout);
        if (turn is null)
        {
            return (false, null);
        }

        try
        {
            await DelayAsync(turn.Wait, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Pacer.Release(turn);
            throw;
        }

        if (Pacer.WaitUntil(endpoint.Id) is not null)
        {
            Pacer.Release(turn);
            return (false, null);
        }

        return (true, turn);
    }

    /// <summary>Says that an endpoint asked Chainsaw to wait.</summary>
    /// <param name="until">Until when, or <see langword="null"/>.</param>
    /// <returns>The words.</returns>
    private static string WaitText(DateTimeOffset? until) =>
        until is { } time
            ? $"{AskedText} till {time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)}."
            : AskedText + ".";

    /// <summary>
    /// Tells whether a failure is the way out's, not the endpoint's: nothing was sent (no exit connected, or Windows
    /// without a network), or the exit was disconnected or the way out changed while the request was in flight. Such a
    /// failure says nothing about the endpoint, so it never marks it as failing.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns><see langword="true"/> when it is the way out's.</returns>
    internal static bool IsTheWayOuts(ExitException exception) =>
        exception is ExitNotConnectedException || exception.Error == ExitError.ConnectionLost;

    /// <summary>
    /// Tells whether a probe sent nothing only because of a wait (the endpoint's or Chainsaw's own pace): it says
    /// nothing about the endpoint, so its earlier probe is kept.
    /// </summary>
    /// <param name="probe">The probe.</param>
    /// <returns><see langword="true"/> when it was a wait.</returns>
    private static bool IsWait(EndpointProbe probe) =>
        probe.Outcome == ProbeOutcome.NotSent
        && (probe.Problem == PacedText || probe.Problem?.StartsWith(AskedText, StringComparison.Ordinal) == true);

    /// <summary>
    /// Sends one request of a probe the endpoint's way (<see cref="SendAsync"/>) and reads its answer; a source that
    /// asks Chainsaw to wait is noted (<see cref="EndpointPacer.AskedToWait"/>). When the way out fails (nothing was
    /// sent, or what was sent was cut), the turn goes back before the failure is thrown on.
    /// </summary>
    /// <typeparam name="T">What is read.</typeparam>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="request">The request.</param>
    /// <param name="read">Reads a successful body; throws <see cref="FormatException"/> when it cannot.</param>
    /// <param name="turn">The turn the request uses, or <see langword="null"/> when it took none.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The answer and how long it took, or why there is none, in Swedish.</returns>
    /// <exception cref="ExitNotConnectedException">The exit does not allow lookups; nothing was sent.</exception>
    private async Task<((T Value, TimeSpan Duration)? Answer, string? Problem)> AskAsync<T>(
        SourceEndpoint endpoint, ExitRequest request, ReadBody<T> read, EndpointTurn? turn,
        CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(ProbeTimeout);
        ExitResponse response;
        try
        {
            response = await SendAsync(endpoint, request, limit.Token).ConfigureAwait(false);
        }
        catch (ExitException exception) when (IsTheWayOuts(exception))
        {
            if (turn is not null)
            {
                Pacer.Release(turn);
            }

            throw;
        }
        catch (ExitException exception)
        {
            return (null, exception.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "Svarade inte i tid.");
        }

        if (SourceLookups.AsksToWait(response) is { } wait)
        {
            if (endpoint.Id != Guid.Empty)
            {
                Pacer.AskedToWait(endpoint.Id, wait);
            }

            return (null, AskedText + ".");
        }

        if (!response.IsSuccess)
        {
            return (null, $"Svarade med status {response.StatusCode.ToString(CultureInfo.InvariantCulture)}.");
        }

        try
        {
            return ((read(response.Body.Span), response.Duration), null);
        }
        catch (FormatException)
        {
            return (null, "Svaret gick inte att läsa som kedjans svar.");
        }
    }

    /// <summary>Adds the key named by an endpoint to a request, from the vault.</summary>
    /// <param name="apiKey">The vault's service for the key, or <see langword="null"/> for none.</param>
    /// <param name="api">The sort of API, which says how the key is sent.</param>
    /// <param name="request">The request without a key.</param>
    /// <param name="problem">Why nothing may be sent, in Swedish, or <see langword="null"/>.</param>
    /// <returns>The request, or <see langword="null"/> when the key is missing or the vault is locked.</returns>
    private ExitRequest? WithKey(string? apiKey, ISourceApi api, ExitRequest request, out string? problem)
    {
        problem = null;
        if (apiKey is null)
        {
            return request;
        }

        ApiKey? key;
        try
        {
            key = _keys?.Find(apiKey);
        }
        catch (VaultException)
        {
            problem = "Valvet är låst, så API-nyckeln kan inte läsas. Inget skickades.";
            return null;
        }

        var credential = key is null ? null : api.Credential(key.Secret);
        if (credential is not { IsWellFormed: true })
        {
            problem = $"API-nyckeln {apiKey} finns inte i valvet eller kan inte skickas. Inget skickades.";
            return null;
        }

        return request with { Credential = credential };
    }

    /// <summary>A probe that sent nothing.</summary>
    /// <param name="id">The endpoint.</param>
    /// <param name="problem">Why, in Swedish.</param>
    /// <param name="at">When, or now.</param>
    /// <returns>The probe.</returns>
    private EndpointProbe NotSent(Guid id, string problem, DateTimeOffset? at = null) =>
        new(id, ProbeOutcome.NotSent, null, null, null, at ?? _clock.UtcNow, problem);

    /// <summary>Checks an address and a sort of API for a chain.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="address">The address.</param>
    /// <param name="api">The sort's code.</param>
    /// <param name="local">Whether the endpoint is marked as the user's own node.</param>
    /// <returns>The address ending in <c>/</c> and the sort.</returns>
    /// <exception cref="ArgumentException">Either cannot be used.</exception>
    private (Uri Address, ISourceApi Api) Check(Chain chain, Uri address, string api, bool local)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!SourceAddress.TryRead(address.OriginalString, local, out var checkedAddress, out var problem))
        {
            throw new ArgumentException(problem, nameof(address));
        }

        if (api is null || !_apis.TryGetValue(api, out var sort) || sort.Info.Chain != chain)
        {
            throw new ArgumentException("The sort of API is not one Chainsaw speaks for the chain.", nameof(api));
        }

        return (checkedAddress!, sort);
    }

    /// <summary>Gives a chain's state, under the lock.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>The state.</returns>
    /// <exception cref="ArgumentException">The chain is not built yet.</exception>
    private ChainState State(Chain chain) =>
        _chains.TryGetValue(chain, out var state)
            ? state
            : throw new ArgumentException("Chainsaw has no sources for the chain yet.", nameof(chain));

    /// <summary>Changes a chain under the lock, tells it and writes the file.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="change">The change.</param>
    /// <returns>A task that completes when the file is written.</returns>
    private Task Change(Chain chain, Action<ChainState> change)
    {
        lock (_gate)
        {
            change(State(chain));
        }

        Tell(chain);
        return _file.SaveAsync(Snapshot);
    }

    /// <summary>Keeps a round's probes unless a newer round has started since.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="round">The round.</param>
    /// <param name="probes">Its probes.</param>
    private void Keep(Chain chain, int round, EndpointProbe[] probes)
    {
        lock (_gate)
        {
            var state = State(chain);
            if (state.Round != round)
            {
                return;
            }

            // A probe that only waited says nothing about its endpoint: its earlier probe stands, with its pause if a
            // lookup failed through it. Every probe is then measured against the highest height of them all, kept ones
            // too, so that neither a kept one nor a new one counts as in step when another is known to be higher.
            // A kept probe never comes out better than it was: the height its round was measured against stays a
            // reference (a stand-in answer that is not kept).
            var earlier = new Dictionary<Guid, EndpointProbe>(state.Probes);
            var kept = probes.Where(IsWait).Select(probe => earlier.GetValueOrDefault(probe.EndpointId))
                .OfType<EndpointProbe>().ToList();
            var merged = probes.Select(probe => kept.Find(k => k.EndpointId == probe.EndpointId) is { } found
                ? Unranked(found)
                : probe);
            var references = kept
                .Where(probe => probe is { Height: not null, BlocksBehind: > 0 })
                .Select(probe => new EndpointProbe(Guid.Empty, ProbeOutcome.Answered,
                    probe.Height + probe.BlocksBehind, null, null, probe.At, null));
            state.Probes.Clear();
            foreach (var probe in Ranked([.. merged, .. references]).Where(probe => probe.EndpointId != Guid.Empty))
            {
                state.Probes[probe.EndpointId] = probe;
            }

            foreach (var probe in probes.Where(probe => !IsWait(probe)))
            {
                state.FailedSince.Remove(probe.EndpointId);
            }

            state.ProbedAt = _clock.UtcNow;
        }

        Tell(chain);
    }

    /// <summary>
    /// Gives a probe kept from an earlier round as it answered, before it was measured against that round, so that it
    /// can be measured against the new one.
    /// </summary>
    /// <param name="kept">The earlier probe.</param>
    /// <returns>
    /// An answer as <see cref="ProbeOutcome.Answered"/> without how far behind it was; any other as it is.
    /// </returns>
    private static EndpointProbe Unranked(EndpointProbe kept) =>
        kept.Outcome is ProbeOutcome.Answered or ProbeOutcome.Behind
            ? kept with { Outcome = ProbeOutcome.Answered, BlocksBehind = null, Problem = null }
            : kept;

    /// <summary>
    /// Notes a lookup's outcome through an endpoint, telling only a change. A failure starts or doubles the pause
    /// in which the endpoint is passed over.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="id">The endpoint.</param>
    /// <param name="failed">Whether it failed.</param>
    private void Note(Chain chain, Guid id, bool failed)
    {
        bool changed;
        lock (_gate)
        {
            var failures = State(chain).FailedSince;
            if (failed)
            {
                var count = failures.TryGetValue(id, out var earlier) ? earlier.Count + 1 : 1;
                var pause = TimeSpan.FromTicks(Math.Min(
                    FirstPause.Ticks * (1L << Math.Min(count - 1, 10)), LongestPause.Ticks));
                failures[id] = new Failure(count, _clock.UtcNow + pause);
                changed = count == 1;
            }
            else
            {
                changed = failures.Remove(id);
            }
        }

        if (changed)
        {
            Tell(chain);
        }
    }

    /// <summary>Tells a change; a handler that throws does not affect it.</summary>
    /// <param name="chain">The chain.</param>
    private void Tell(Chain chain)
    {
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler<Chain>)handler)(this, chain);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The change stands; a listener's failure is its own.
            }
        }
    }

    /// <summary>Gives every chain as it is now, for the file.</summary>
    /// <returns>The chains.</returns>
    private IReadOnlyList<SavedChain> Snapshot()
    {
        lock (_gate)
        {
            return [.. _chains.Select(pair => new SavedChain(pair.Key, pair.Value.Mode, [.. pair.Value.Endpoints]))];
        }
    }

    /// <summary>Reads a successful body.</summary>
    /// <typeparam name="T">What is read.</typeparam>
    /// <param name="body">The body.</param>
    /// <returns>What it says.</returns>
    private delegate T ReadBody<out T>(ReadOnlySpan<byte> body);

    /// <summary>The failures in a row of a lookup through an endpoint, and until when it is passed over.</summary>
    /// <param name="Count">How many lookups in a row have failed through it.</param>
    /// <param name="PausedUntil">Until when the others are asked before it, in UTC.</param>
    private sealed record Failure(int Count, DateTimeOffset PausedUntil);

    /// <summary>A chain's endpoints, mode and probes.</summary>
    /// <param name="mode">The mode.</param>
    /// <param name="endpoints">The endpoints in the user's order.</param>
    private sealed class ChainState(SourceMode mode, List<SourceEndpoint> endpoints)
    {
        /// <summary>Gets or sets the mode.</summary>
        public SourceMode Mode { get; set; } = mode;

        /// <summary>Gets the endpoints in the user's order.</summary>
        public List<SourceEndpoint> Endpoints { get; } = endpoints;

        /// <summary>Gets the latest probe of each endpoint.</summary>
        public Dictionary<Guid, EndpointProbe> Probes { get; } = [];

        /// <summary>
        /// Gets whether a probe showed each endpoint to be on the chain (its first block is the chain's) or not
        /// (another first block, or a height the chain cannot have come to); one not here has not been shown either.
        /// </summary>
        public Dictionary<Guid, bool> OnChain { get; } = [];

        /// <summary>Gets the endpoints a lookup failed through since they were tried, with their pause.</summary>
        public Dictionary<Guid, Failure> FailedSince { get; } = [];

        /// <summary>Gets until when each lookup that waits for an endpoint now waits, in UTC.</summary>
        public List<DateTimeOffset> Waits { get; } = [];

        /// <summary>Gets or sets when the endpoints were last tried.</summary>
        public DateTimeOffset? ProbedAt { get; set; }

        /// <summary>Gets or sets the number of the latest round, so that an older round's results are
        /// dropped.</summary>
        public int Round { get; set; }
    }
}
