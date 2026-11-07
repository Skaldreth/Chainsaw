using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>An endpoint to ask, with the sort of API it speaks.</summary>
/// <param name="Endpoint">The endpoint.</param>
/// <param name="Api">Its sort of API.</param>
internal sealed record PlannedEndpoint(SourceEndpoint Endpoint, ISourceApi Api);

/// <summary>Which endpoints a lookup asks, in order; none when the chain is off.</summary>
/// <param name="Off">Whether the chain is <see cref="SourceMode.Off"/>.</param>
/// <param name="Endpoints">The endpoints to ask, first first; the next is asked when one fails.</param>
internal sealed record SourcePlan(bool Off, IReadOnlyList<PlannedEndpoint> Endpoints);

/// <summary>
/// The chains' endpoints, their modes and their probes (sketch KL1a), kept in <c>sources.json</c> in the program's
/// data. Every probe goes through <see cref="IExitHttp"/> and asks only for the chain's height (and, in the form that
/// adds an endpoint, the hash of the block at that height); nothing is probed while the vault is locked, and a round
/// in which the exit refused is not kept. The lookups ask the endpoints in the order <see cref="Plan"/> gives.
/// </summary>
internal sealed class ChainSources : IChainSources
{
    /// <summary>How long an endpoint has to answer a probe; a slower one is not the fastest anyway.</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The sorts of API, by code.</summary>
    private readonly IReadOnlyDictionary<string, ISourceApi> _apis;

    /// <summary>The only way out.</summary>
    private readonly IExitHttp _http;

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
    public ChainSources(
        IEnumerable<ISourceApi> apis, IExitHttp http, IApiKeyStore? keys, IVault? vault, IClock clock,
        ChainSourcesOptions options)
    {
        ArgumentNullException.ThrowIfNull(apis);
        ArgumentNullException.ThrowIfNull(options);
        _apis = apis.ToDictionary(api => api.Info.Code, StringComparer.Ordinal);
        _http = http;
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

    /// <inheritdoc/>
    public IReadOnlyList<SourceApiInfo> Apis(Chain chain) =>
        [.. _apis.Values.Where(api => api.Info.Chain == chain).Select(api => api.Info)];

    /// <inheritdoc/>
    public ChainSourcesView Get(Chain chain)
    {
        lock (_gate)
        {
            var state = State(chain);
            var first = Order(state).FirstOrDefault()?.Id;
            var endpoints = state.Endpoints.Select(endpoint => new EndpointView(
                endpoint,
                state.Probes.GetValueOrDefault(endpoint.Id),
                state.FailedSince.Contains(endpoint.Id),
                endpoint.Id == first)).ToList();
            return new ChainSourcesView(chain, state.Mode, endpoints, state.ProbedAt);
        }
    }

    /// <summary>Gives the endpoints a lookup on the chain asks, in order.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>The plan; off, or the endpoints that are on.</returns>
    public SourcePlan Plan(Chain chain)
    {
        lock (_gate)
        {
            var state = State(chain);
            return state.Mode == SourceMode.Off
                ? new SourcePlan(true, [])
                : new SourcePlan(false, [.. Order(state).Select(e => new PlannedEndpoint(e, _apis[e.Api]))]);
        }
    }

    /// <summary>
    /// Notes that a lookup through an endpoint failed or did not answer in time, so that the others are asked before
    /// it until it is tried again or answers.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="id">The endpoint.</param>
    public void Failed(Chain chain, Guid id) => Note(chain, id, failed: true);

    /// <summary>Notes that a lookup through an endpoint was answered, so that an earlier failure is forgotten.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="id">The endpoint.</param>
    public void Answered(Chain chain, Guid id) => Note(chain, id, failed: false);

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
    public (SourceEndpoint Endpoint, Task Saved) Add(Chain chain, Uri address, string api, string? apiKey)
    {
        var (checkedAddress, sort) = Check(chain, address, api);
        var key = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        var name = SourceAddress.NameOf(checkedAddress);
        var endpoint = new SourceEndpoint(
            Guid.NewGuid(), chain, name[..Math.Min(name.Length, SourceAddress.MaximumNameLength)], checkedAddress,
            sort.Info.Code, key, Enabled: true);
        return (endpoint, Change(chain, state => state.Endpoints.Add(endpoint)));
    }

    /// <inheritdoc/>
    public Task RemoveAsync(Chain chain, Guid id) => Change(chain, state =>
    {
        state.Endpoints.RemoveAll(endpoint => endpoint.Id == id);
        state.Probes.Remove(id);
        state.FailedSince.Remove(id);
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
            round = ++state.Round;
        }

        if (endpoints.Count == 0)
        {
            return ProbeRun.NoneOn;
        }

        if (IsLocked)
        {
            return ProbeRun.NotSent;
        }

        EndpointProbe[] probes;
        try
        {
            probes = await Task.WhenAll(endpoints.Select(planned => ProbeOneAsync(
                planned.Endpoint.Id, planned.Endpoint.Address, planned.Api, planned.Endpoint.ApiKey, withHash: false,
                cancellationToken))).ConfigureAwait(false);
        }
        catch (ExitNotConnectedException)
        {
            // The exit did not allow lookups (or stopped allowing them): nothing more is sent, and the round says
            // nothing about the endpoints, so the earlier results stand.
            return ProbeRun.NotSent;
        }

        cancellationToken.ThrowIfCancellationRequested();
        Keep(chain, round, Ranked(probes));
        return ProbeRun.Probed;
    }

    /// <inheritdoc/>
    public async Task<EndpointProbe> TryAsync(
        Chain chain, Uri address, string api, string? apiKey, CancellationToken cancellationToken = default)
    {
        var (checkedAddress, sort) = Check(chain, address, api);
        if (IsLocked)
        {
            return NotSent(Guid.Empty, "Valvet är låst. Inget skickades.");
        }

        try
        {
            var key = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
            return await ProbeOneAsync(Guid.Empty, checkedAddress, sort, key, withHash: true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ExitNotConnectedException exception)
        {
            return NotSent(Guid.Empty, exception.Message);
        }
    }

    /// <summary>Gets whether the vault is locked, so that nothing may be asked (CLAUDE.md, Säkerhet).</summary>
    private bool IsLocked => _vault is not null && _vault.State != VaultState.Unlocked;

    /// <summary>
    /// Orders a chain's endpoints that are on for a lookup. In Fastest, those that answered right come first, the
    /// fastest first, then those not tried, then those that failed; one that lags behind in height is left out. In
    /// Priority, the user's order. Either way an endpoint that failed a lookup since it was tried comes last.
    /// </summary>
    /// <param name="state">The chain's state, under the lock.</param>
    /// <returns>The endpoints to ask, first first.</returns>
    private static IEnumerable<SourceEndpoint> Order(ChainState state)
    {
        if (state.Mode == SourceMode.Off)
        {
            return [];
        }

        var on = state.Endpoints.Where(endpoint => endpoint.Enabled);
        if (state.Mode == SourceMode.Fastest)
        {
            on = on
                .Where(endpoint => state.Probes.GetValueOrDefault(endpoint.Id)?.Outcome != ProbeOutcome.Behind)
                .OrderBy(endpoint => Rank(state.Probes.GetValueOrDefault(endpoint.Id)))
                .ThenBy(endpoint => state.Probes.GetValueOrDefault(endpoint.Id)?.Duration ?? TimeSpan.MaxValue);
        }

        return on.OrderBy(endpoint => state.FailedSince.Contains(endpoint.Id)).ToList();
    }

    /// <summary>Ranks a probe for Fastest: answered, not tried, failed.</summary>
    /// <param name="probe">The probe, if any.</param>
    /// <returns>The rank, lowest first.</returns>
    private static int Rank(EndpointProbe? probe) => probe?.Outcome switch
    {
        ProbeOutcome.Answered => 0,
        ProbeOutcome.Failed => 2,
        _ => 1,
    };

    /// <summary>
    /// Marks every answer lower than the highest as behind: such an endpoint has not seen the newest blocks, and a
    /// lookup through it could miss transactions the others know.
    /// </summary>
    /// <param name="probes">The round's probes.</param>
    /// <returns>The probes with those behind marked.</returns>
    private static EndpointProbe[] Ranked(EndpointProbe[] probes)
    {
        var highest = probes.Where(p => p.Outcome == ProbeOutcome.Answered).Max(p => p.Height);
        return [.. probes.Select(probe => probe.Outcome == ProbeOutcome.Answered && probe.Height < highest
            ? probe with
            {
                Outcome = ProbeOutcome.Behind,
                BlocksBehind = highest - probe.Height,
                Problem = string.Create(CultureInfo.InvariantCulture,
                    $"Höjd {probe.Height:N0} mot {highest:N0}: {highest - probe.Height} block efter. Används inte.")
                    .Replace(',', ' '),
            }
            : probe)];
    }

    /// <summary>
    /// Tries one endpoint: the chain's height, timed, and when asked the hash of the block at that height. The exit's
    /// refusal is thrown on, so that a round can be dropped.
    /// </summary>
    /// <param name="id">The endpoint's id, or <see cref="Guid.Empty"/>.</param>
    /// <param name="address">Its address.</param>
    /// <param name="api">Its sort of API.</param>
    /// <param name="apiKey">The vault's service for its key, or <see langword="null"/>.</param>
    /// <param name="withHash">Whether to ask for the block's hash too.</param>
    /// <param name="cancellationToken">Stops the probe.</param>
    /// <returns>What it answered.</returns>
    /// <exception cref="ExitNotConnectedException">The exit does not allow lookups; nothing was sent.</exception>
    private async Task<EndpointProbe> ProbeOneAsync(
        Guid id, Uri address, ISourceApi api, string? apiKey, bool withHash, CancellationToken cancellationToken)
    {
        var at = _clock.UtcNow;
        if (WithKey(apiKey, api, api.HeightRequest(address), out var problem) is not { } heightRequest)
        {
            return NotSent(id, problem!, at);
        }

        var (height, heightProblem) = await AskAsync(heightRequest, api.ReadHeight, cancellationToken)
            .ConfigureAwait(false);
        if (height is not { } answer)
        {
            return new EndpointProbe(id, ProbeOutcome.Failed, null, null, null, at, heightProblem);
        }

        string? hash = null;
        if (withHash)
        {
            var hashRequest = WithKey(apiKey, api, api.HashRequest(address, answer.Value), out _)!;
            var (read, hashProblem) = await AskAsync(hashRequest, api.ReadHash, cancellationToken)
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

    /// <summary>Sends one request of a probe through the exit and reads its answer.</summary>
    /// <typeparam name="T">What is read.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="read">Reads a successful body; throws <see cref="FormatException"/> when it cannot.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The answer and how long it took, or why there is none, in Swedish.</returns>
    /// <exception cref="ExitNotConnectedException">The exit does not allow lookups; nothing was sent.</exception>
    private async Task<((T Value, TimeSpan Duration)? Answer, string? Problem)> AskAsync<T>(
        ExitRequest request, ReadBody<T> read, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(ProbeTimeout);
        ExitResponse response;
        try
        {
            response = await _http.SendAsync(request, limit.Token).ConfigureAwait(false);
        }
        catch (ExitNotConnectedException)
        {
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
    /// <returns>The address ending in <c>/</c> and the sort.</returns>
    /// <exception cref="ArgumentException">Either cannot be used.</exception>
    private (Uri Address, ISourceApi Api) Check(Chain chain, Uri address, string api)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!SourceAddress.TryRead(address.OriginalString, out var checkedAddress, out var problem))
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

            state.Probes.Clear();
            foreach (var probe in probes)
            {
                state.Probes[probe.EndpointId] = probe;
            }

            state.FailedSince.Clear();
            state.ProbedAt = _clock.UtcNow;
        }

        Tell(chain);
    }

    /// <summary>Notes a lookup's outcome through an endpoint, telling only a change.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="id">The endpoint.</param>
    /// <param name="failed">Whether it failed.</param>
    private void Note(Chain chain, Guid id, bool failed)
    {
        bool changed;
        lock (_gate)
        {
            var set = State(chain).FailedSince;
            changed = failed ? set.Add(id) : set.Remove(id);
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

        /// <summary>Gets the endpoints a lookup failed through since they were tried.</summary>
        public HashSet<Guid> FailedSince { get; } = [];

        /// <summary>Gets or sets when the endpoints were last tried.</summary>
        public DateTimeOffset? ProbedAt { get; set; }

        /// <summary>Gets or sets the number of the latest round, so that an older round's results are dropped.</summary>
        public int Round { get; set; }
    }
}
