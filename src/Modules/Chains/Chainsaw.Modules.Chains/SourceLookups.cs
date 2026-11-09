using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// How every chain's source answers a lookup, written once: from the case when the answer cannot have changed,
/// otherwise from the chain's endpoints in the order of its mode, each at its own pace and spread over them, through
/// <see cref="IExitHttp"/> only (or straight to the user's own node when the endpoint is marked local, see <see
/// cref="ChainSources.SendAsync"/>), each response sealed in the case with its endpoint before it is used (principle
/// 1), and from the case again, saying so, when no endpoint can be asked (principle 9). An endpoint that fails, does
/// not answer or asks Chainsaw to wait is left quietly for the others (decided 2026-11-07). A chain's source says what
/// to ask and how to read it (<see cref="LookupQuery{T}"/>).
/// </summary>
internal sealed class SourceLookups
{
    /// <summary>The HTTP status for something the source does not know.</summary>
    private const int NotFoundStatus = 404;

    /// <summary>What a lookup says when only endpoints that cannot be believed said that they do not know it.</summary>
    internal const string UntrustedNotFoundText =
        "Källan känner inte till det som söktes, men ingen endpoint som visat att den svarar rätt sa det. "
        + "Uppslaget räknas som misslyckat tills en endpoint som provats svarar.";

    /// <summary>What a lookup says when a response cannot be read or does not add up.</summary>
    private const string FailedText =
        "Källans svar gick inte att tolka eller gick inte ihop. Det är sparat i ärendet men används inte.";

    /// <summary>Says that the source refused the API key, in its own words, and where the key is changed.</summary>
    /// <param name="said">What the source answered.</param>
    /// <returns>The text for the user.</returns>
    internal static string KeyRefusedText(string said) =>
        $"{SourceKeyTexts.KeyRefusedLead} (den svarade: {said}).";

    /// <summary>The HTTP status of a source that is asked too fast.</summary>
    private const int TooManyRequestsStatus = 429;

    /// <summary>The HTTP status of a source that cannot answer now.</summary>
    private const int UnavailableStatus = 503;

    /// <summary>How long an endpoint that says it is asked too fast, without saying for how long, is waited
    /// for.</summary>
    public static readonly TimeSpan DefaultBusyWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The longest a lookup waits for an endpoint's turn; when every endpoint's turn is further away the lookup gives
    /// up, says so, and answers from the case.
    /// </summary>
    public static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(2);

    /// <summary>A wait shorter than this is not told: the pace alone often waits a moment.</summary>
    public static readonly TimeSpan NoticeableWait = TimeSpan.FromSeconds(1);

    /// <summary>The chain whose endpoints are asked.</summary>
    private readonly Chain _chain;

    /// <summary>Who fetches, recorded with every response.</summary>
    private readonly IOperatorProfileStore _operators;

    /// <summary>The endpoints to ask, in the order the chosen mode gives.</summary>
    private readonly ChainSources _sources;

    /// <summary>Seals and finds the raw responses.</summary>
    private readonly RawResponseStore _store;

    /// <summary>How many blocks deep an answer's block must be before it is never fetched again.</summary>
    private readonly int _safeConfirmations;

    /// <summary>Asks for the chain's height when the newest known one is old, before a shallow answer is asked
    /// again.</summary>
    private readonly Func<SourceLookups, ICase, CancellationToken, Task> _refreshHeight;

    /// <summary>What a lookup says when the chain's sources are off in the settings.</summary>
    private readonly string _sourcesOffText;

    /// <summary>What a lookup says when no endpoint of the chain is on.</summary>
    private readonly string _noneOnText;

    /// <summary>What a lookup that needs an endpoint in step says when none is known to be.</summary>
    private readonly string _noneInStepText;

    /// <summary>Why nothing was sent when only the endpoint passed over is on.</summary>
    private readonly string _noOtherText;


    /// <summary>Creates the lookups of one chain.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="chainName">The chain's name in the texts for the user, for example <c>Bitcoin</c>.</param>
    /// <param name="operators">The operator profile.</param>
    /// <param name="sources">The chains' endpoints, which also say the way to each (<see
    /// cref="ChainSources.SendAsync"/>).</param>
    /// <param name="store">The chain's sealed responses.</param>
    /// <param name="safeConfirmations">How deep an answer's block must be before it is never fetched again.</param>
    /// <param name="refreshHeight">
    /// Asks for the chain's height when the known one is old; called before an answer whose block was shallow is asked
    /// for again, so that the new answer can count as settled.
    /// </param>
    /// <exception cref="ArgumentException">The depth is less than 1.</exception>
    public SourceLookups(
        Chain chain, string chainName, IOperatorProfileStore operators, ChainSources sources,
        RawResponseStore store, int safeConfirmations,
        Func<SourceLookups, ICase, CancellationToken, Task> refreshHeight)
    {
        if (safeConfirmations < 1)
        {
            throw new ArgumentException("The depth must be at least 1.", nameof(safeConfirmations));
        }

        (_chain, _operators, _sources, _store) = (chain, operators, sources, store);
        (_safeConfirmations, _refreshHeight) = (safeConfirmations, refreshHeight);
        _sourcesOffText = $"Källorna för {chainName} är avstängda i inställningarna (Av). Inget skickades.";
        _noneOnText = $"Ingen källa för {chainName} är påslagen i inställningarna. Inget skickades.";
        _noOtherText = $"Ingen annan källa för {chainName} är påslagen att fråga. Inget skickades.";
        _noneInStepText = $"Ingen källa för {chainName} är känd i fas med kedjans högsta höjd; prova källorna. "
            + "Inget skickades.";
    }

    /// <summary>Gets the chain's sealed responses.</summary>
    public RawResponseStore Store => _store;

    /// <summary>
    /// Answers a lookup: from the case when the answer cannot have changed, otherwise from the source through the
    /// exit (or straight from the user's own node), sealed in the case before it is used, and from the case again when
    /// the source cannot be asked.
    /// </summary>
    /// <typeparam name="T">What is looked up.</typeparam>
    /// <param name="case">The open case.</param>
    /// <param name="query">The lookup.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The answer.</returns>
    public async Task<BitcoinLookup<T>> LookupAsync<T>(
        ICase @case, LookupQuery<T> query, LookupMode mode, CancellationToken cancellationToken)
        where T : class
    {
        var (stored, heightThen) = await ReadCaseAsync(@case, query, cancellationToken).ConfigureAwait(false);
        if (stored is { } found && IsSettled(query, found.Value, heightThen))
        {
            return new BitcoinLookup<T>(LookupStatus.FromCase, found.Value, [found.Response.Origin], null);
        }

        if (mode == LookupMode.CaseOnly)
        {
            return Fallback(stored, LookupStatus.NotInCase, "Läst bara ur ärendet; inget skickades.");
        }

        // An answer in a block that was not yet deep when it was fetched is fetched again, so that a block that has
        // fallen out of the chain since is noticed (a reorganisation). The height is refreshed first, so that the new
        // answer can count as settled once its block is deep enough; and it is asked of an endpoint in step with the
        // chain when one is known, since one a block behind could answer that the transaction is not in a block yet.
        var shallowAgain = stored is { } shallow && query.SettledAt(shallow.Value) is not null;
        if (shallowAgain)
        {
            await _refreshHeight(this, @case, cancellationToken).ConfigureAwait(false);
        }

        return await FetchAsync(
                @case, query, stored, cancellationToken, mode == LookupMode.InStep, preferInStep: shallowAgain)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the source, seals the response and reads it; falls back on the case's answer when it must. The endpoints
    /// are asked in the plan's order, each in its turn: the one whose turn comes first is asked, so that a lookup
    /// never waits for one endpoint's pace while another is free, and the next is asked when one fails, does not answer
    /// in time, asks Chainsaw to wait or gives an answer that cannot be used. Every response, also one that cannot be
    /// used, is sealed with the endpoint it came from (principle 1).
    /// </summary>
    /// <typeparam name="T">What is looked up.</typeparam>
    /// <param name="case">The open case.</param>
    /// <param name="query">The lookup.</param>
    /// <param name="stored">The case's newest answer, if any.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <param name="inStep">
    /// Whether only an endpoint in step with the chain may be asked (<see cref="LookupMode.InStep"/>).
    /// </param>
    /// <param name="preferInStep">
    /// Whether endpoints in step with the chain are asked when some are known to be, and the others only when none is.
    /// </param>
    /// <param name="except">An endpoint not to ask, as when its own answer is to be confirmed by another.</param>
    /// <param name="doubted">
    /// The response of an answer that is not believed (a final TRON answer without a known token's event): its
    /// endpoint, and any other at the same host and port, is not asked (<see cref="IsOther"/>); <see langword="null"/>
    /// to ask any.
    /// </param>
    /// <param name="preferNotHosts">
    /// Hosts whose endpoints are passed over while another can be asked, as when what they said is to be confirmed
    /// elsewhere; when every endpoint is on them, they are asked all the same.
    /// </param>
    /// <param name="ownNodeOnly">
    /// Whether only the user's own nodes (endpoints marked local) may be asked, as when a block is to be confirmed by
    /// the own node; nothing then goes through the exit.
    /// </param>
    /// <returns>The answer.</returns>
    public async Task<BitcoinLookup<T>> FetchAsync<T>(
        ICase @case, LookupQuery<T> query, (T Value, StoredResponse Response)? stored,
        CancellationToken cancellationToken, bool inStep = false, bool preferInStep = false, Guid? except = null,
        ResponseOrigin? doubted = null,
        IReadOnlySet<string>? preferNotHosts = null, bool ownNodeOnly = false)
        where T : class
    {
        var fetchedBy = CurrentOperator(out var noOperator);
        if (fetchedBy is null)
        {
            return Fallback(stored, LookupStatus.Failed, noOperator!);
        }

        var plan = Answering(_sources.Plan(_chain, inStep || preferInStep), query.Path, ownNodeOnly);
        if (preferInStep && !inStep && plan is { Off: false, Endpoints.Count: 0 })
        {
            plan = Answering(_sources.Plan(_chain), query.Path, ownNodeOnly);
        }

        if (except is { } passedOver)
        {
            // Another endpoint than the one whose answer is to be confirmed.
            plan = plan with { Endpoints = [.. plan.Endpoints.Where(each => each.Endpoint.Id != passedOver)] };
        }

        if (preferNotHosts is { Count: > 0 } hosts
            && plan.Endpoints.Where(each => !hosts.Contains(each.Endpoint.Address.Host)).ToList() is [_, ..] others)
        {
            plan = plan with { Endpoints = others };
        }

        if (plan.Off)
        {
            return Fallback(stored, LookupStatus.Offline, _sourcesOffText);
        }

        if (plan.Endpoints.Count == 0)
        {
            return Fallback(stored, LookupStatus.Failed, plan.NoneInStep ? _noneInStepText : _noneOnText);
        }

        if (doubted is { } notBelieved)
        {
            plan = plan with
            {
                Endpoints = [.. plan.Endpoints.Where(planned => IsOther(planned.Endpoint, notBelieved))],
            };

            if (plan.Endpoints.Count == 0)
            {
                return Fallback(stored, LookupStatus.Failed, _noOtherText);
            }
        }

        var failures = new List<ResponseOrigin>();
        var problem = _noneOnText;
        string? notFound = null;
        long? notFoundSequence = null;
        var trustedNotFound = false;
        var onlyNotFound = true;
        string? exitClosed = null;
        var remaining = plan.Endpoints.ToList();
        while (remaining.Count > 0)
        {
            var (turn, waitProblem) = await TurnAsync(remaining, cancellationToken).ConfigureAwait(false);
            if (turn is null)
            {
                problem = waitProblem!;
                onlyNotFound = false;
                break;
            }

            var planned = turn.Endpoint;
            remaining.Remove(planned);
            var endpoint = planned.Endpoint;
            var unkeyed = query.Body is { } body
                ? planned.Api.Post(endpoint.Address, query.Path, body)
                : planned.Api.Request(endpoint.Address, query.Path);
            if (_sources.WithKey(endpoint, planned.Api, unkeyed, out var keyProblem) is not { } request)
            {
                _sources.Pacer.Release(turn);
                _sources.Failed(_chain, endpoint.Id);
                problem = keyProblem!;
                onlyNotFound = false;
                continue;
            }

            ExitResponse response;
            try
            {
                response = await _sources.SendAsync(endpoint, request, cancellationToken).ConfigureAwait(false);
            }
            catch (ExitNotConnectedException exception)
            {
                // Nothing was sent, and nothing more goes through the exit; the user's own node is still asked, since
                // it is reached in the user's own network. The endpoints passed over were not asked, so a not found
                // from another can no longer count as everyone's.
                _sources.Pacer.Release(turn);
                exitClosed = exception.Message + " Inget skickades.";
                onlyNotFound = false;
                remaining.RemoveAll(other => !other.Endpoint.Local);
                continue;
            }
            catch (ExitException exception)
            {
                if (!ChainSources.IsTheWayOuts(exception))
                {
                    _sources.Failed(_chain, endpoint.Id);
                }

                problem = exception.Message;
                onlyNotFound = false;
                continue;
            }

            var (value, interpretProblem, outcome) = Interpret(query, response, planned.Api);
            var index = value is null ? null : query.Index(value);
            if (index?.Highest > ChainLimits.HighestPlausible(_store.ChainName, response.ReceivedAt))
            {
                // A block higher than the chain can have come to: the answer is not this chain's (a test network or
                // another chain added by mistake, or a source that lies). Taken, it would make every answer deep.
                (value, index, outcome) = (null, null, Outcome.Failed);
                interpretProblem = "Källan uppgav ett block högre än kedjan kan ha nått. "
                    + "Svaret är sparat i ärendet men används inte.";
            }

            var sealedAs = query.Body is { } call
                ? response with { Uri = SealedPostUri(response.Uri, call) }
                : response;
            var sealedResponse = await Task.Run(
                () => @case.Write(transaction => _store.Append(
                    transaction, query.Kind, query.Subject, sealedAs, endpoint, fetchedBy, index)),
                CancellationToken.None).ConfigureAwait(false);
            if (value is not null)
            {
                _sources.Answered(_chain, endpoint.Id);
                var change = stored is { } earlier ? query.Changed?.Invoke(earlier.Value, value) : null;
                return new BitcoinLookup<T>(LookupStatus.Fetched, value, [.. failures, sealedResponse.Origin], null)
                {
                    Notes = change is null ? [] : [change],
                };
            }

            failures.Add(sealedResponse.Origin);
            if (outcome == Outcome.NotFound)
            {
                // The source says it does not know it. From an endpoint whose probe did not fail that is an answer,
                // and no other endpoint is asked; from one that failed its probe or lags behind (a wrong path, a node
                // still syncing), the next is asked, so that one broken endpoint never says that a tx does not exist.
                notFound = interpretProblem;
                notFoundSequence = sealedResponse.Origin.Sequence;
                if (_sources.TrustsNotFound(_chain, endpoint.Id))
                {
                    trustedNotFound = true;
                    break;
                }

                continue;
            }

            if (outcome == Outcome.Wait)
            {
                // Asked too fast: it is waited for as long as it asks, and the others are asked meanwhile. It has not
                // failed, so it comes back by itself when the wait is over.
                _sources.AskedToWait(_chain, endpoint.Id, AsksToWait(response) ?? DefaultBusyWait);
            }
            else if (outcome == Outcome.CannotAnswer)
            {
                // It works, but cannot answer this kind of question (a node without the index): the others are asked,
                // and it is not paused for the questions it can answer.
                _sources.Answered(_chain, endpoint.Id);
            }
            else if (outcome == Outcome.KeyRefused)
            {
                // The key is wrong, not the source: it is said on the endpoint's row until the key is changed.
                _sources.KeyRefused(_chain, endpoint.Id, interpretProblem!);
                interpretProblem = KeyRefusedText(interpretProblem!);
            }
            else
            {
                _sources.Failed(_chain, endpoint.Id);
            }

            problem = interpretProblem!;
            onlyNotFound = false;
        }

        // Not found is said only when an endpoint that can be believed said it. When every endpoint asked said it but
        // none of them can be believed (a single endpoint whose path has changed, say), the lookup failed instead and
        // says why (etapp 25, M), so that nothing reads as gone from the chain on an untried word.
        var isNotFound = notFound is not null && trustedNotFound;
        if (!isNotFound && notFound is not null && onlyNotFound)
        {
            problem = UntrustedNotFoundText;
        }

        if (isNotFound && notFoundSequence is { } taken)
        {
            // Only an answer the lookup believed is noted, so that a lagging endpoint's 404 never reads as a
            // transaction gone from the chain.
            await Task.Run(
                () => @case.Write(transaction =>
                {
                    _store.MarkNotFound(transaction, query.Kind, query.Subject, taken);
                    return taken;
                }),
                CancellationToken.None).ConfigureAwait(false);
        }

        if (isNotFound && stored is null)
        {
            return new BitcoinLookup<T>(LookupStatus.NotFound, null, failures, notFound);
        }

        if (exitClosed is not null && !isNotFound)
        {
            // Said as the way out says it: no exit connected with VPN, or no network with Windows (principle 9).
            return Fallback(stored, LookupStatus.Offline, exitClosed, failures);
        }

        return Fallback(stored, LookupStatus.Failed, isNotFound ? notFound! : problem, failures);
    }

    /// <summary>
    /// Waits for the turn that comes first among the endpoints still to ask (<see cref="EndpointPacer.Take"/>), saying
    /// so while the wait is noticeable, and gives that turn. After the wait, an endpoint that has since asked Chainsaw
    /// to wait is passed over and its turn given back; a cancelled wait gives its turn back too.
    /// </summary>
    /// <param name="remaining">The endpoints still to ask, in the plan's order.</param>
    /// <param name="cancellationToken">Stops the wait.</param>
    /// <returns>The turn to use now, or <see langword="null"/> and why none is used, in Swedish.</returns>
    private async Task<(EndpointTurn? Turn, string? Problem)> TurnAsync(
        List<PlannedEndpoint> remaining, CancellationToken cancellationToken)
    {
        while (true)
        {
            var (turn, none) = _sources.Pacer.Take(remaining, LongestWait);
            if (turn is null)
            {
                var until = none is null ? string.Empty : $" till {LocalText(none.Earliest.ToLocalTime())}";
                return (null, none is { SourceAsked: false }
                    ? $"Chainsaws egen takt mot källorna är full{until}. Inget mer skickades; försök igen om en stund."
                    : $"Källorna ber Chainsaw vänta{until}. Inget mer skickades.");
            }

            var noticed = turn.Wait >= NoticeableWait;
            if (noticed)
            {
                _sources.Waiting(_chain, turn.StartsAt);
            }

            try
            {
                await _sources.DelayAsync(turn.Wait, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancelled before anything was sent: the turn goes back.
                _sources.Pacer.Release(turn);
                throw;
            }
            finally
            {
                if (noticed)
                {
                    _sources.DoneWaiting(_chain, turn.StartsAt);
                }
            }

            if (_sources.Pacer.WaitUntil(turn.Endpoint.Endpoint.Id) is null)
            {
                return (turn, null);
            }

            _sources.Pacer.Release(turn);
        }
    }

    /// <summary>
    /// Gives the address a POST's response (a JSON-RPC call) is sealed under: the endpoint's scheme, host and port
    /// only, since a provider may put its key in the path or the query and a key never goes into a case (it belongs in
    /// the vault); and the call that was sent as the fragment, which is never sent itself, so that the case says what
    /// was asked (the method and its parameters, for example the signature a page was asked after).
    /// </summary>
    /// <param name="sent">The address the call was sent to.</param>
    /// <param name="call">The call's body.</param>
    /// <returns>The address to seal.</returns>
    public static Uri SealedPostUri(Uri sent, string call)
    {
        ArgumentNullException.ThrowIfNull(sent);
        return new UriBuilder(sent.Scheme, sent.Host, sent.Port, "/") { Fragment = call }.Uri;
    }

    /// <summary>
    /// Tells whether a response asks Chainsaw to wait: <c>429</c> (asked too fast; at least
    /// <see cref="NoticeableWait"/>), or <c>503</c> with a <c>Retry-After</c> of at least that. A 503 that asks for no
    /// wait is a failure, so that a broken endpoint is paused instead of asked first every time.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>How long, <see cref="DefaultBusyWait"/> when a 429 does not say; <see langword="null"/> when it does
    /// not ask.</returns>
    public static TimeSpan? AsksToWait(ExitResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.StatusCode switch
        {
            TooManyRequestsStatus => TimeSpan.FromTicks(
                Math.Max((response.RetryAfter ?? DefaultBusyWait).Ticks, NoticeableWait.Ticks)),
            UnavailableStatus => response.RetryAfter is { } asked && asked >= NoticeableWait ? asked : null,
            _ => null,
        };
    }

    /// <summary>
    /// Keeps the endpoints of a plan that can answer a path: a sort that answers only some (the user's own Bitcoin
    /// Core) is passed over for the others; and only the user's own nodes when asked for.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <param name="path">The lookup's path.</param>
    /// <param name="ownNodeOnly">Whether only endpoints marked local are kept.</param>
    /// <returns>The plan with the endpoints that may be asked.</returns>
    private static SourcePlan Answering(SourcePlan plan, string path, bool ownNodeOnly) => plan with
    {
        Endpoints = [.. plan.Endpoints.Where(planned =>
            planned.Api.Answers(path) && (!ownNodeOnly || planned.Endpoint.Local))],
    };

    /// <summary>
    /// Gives a sealed body in the form the chains' parsers read: Bitcoin Core's JSON-RPC answer (<see
    /// cref="BitcoinCoreApi"/>) as the plain text Esplora gives for the same question, and every other body as it is.
    /// The raw body stays what is sealed and hashed (principle 1); this is the one place it is unwrapped, both for a
    /// fresh response and for one read back from the case.
    /// </summary>
    /// <param name="source">The sort of API the body came from (<see cref="ResponseOrigin.Source"/>).</param>
    /// <param name="body">The raw body.</param>
    /// <param name="path">
    /// The path of a fresh lookup, whose call's id a fresh JSON-RPC answer must give back; <see langword="null"/> for
    /// an answer read back from the case, which was checked so when it was fetched.
    /// </param>
    /// <returns>The body to read.</returns>
    /// <exception cref="FormatException">A JSON-RPC body is not an answer, or not the answer to the call.</exception>
    public static byte[] Readable(string source, byte[] body, string? path = null)
    {
        if (source != BitcoinCoreApi.Code)
        {
            return body;
        }

        return path is null ? BitcoinCoreApi.Result(body, expectedId: null) : BitcoinCoreApi.ResultFor(path, body);
    }

    /// <summary>Reads a fresh response, or says why it cannot be used.</summary>
    /// <typeparam name="T">What is looked up.</typeparam>
    /// <param name="query">The lookup.</param>
    /// <param name="response">The response.</param>
    /// <param name="api">The sort of API that answered, which may read more from the response than its status.</param>
    /// <returns>
    /// The value, or <see langword="null"/> and the problem in Swedish, and what the source said when there is no
    /// value: that it does not know what was asked (HTTP 404, or a body that says so, <see
    /// cref="SourceNotFoundException"/>, as the Etherscan-style APIs answer 200 with <c>status 0</c>), that it is asked
    /// too fast (<see cref="AsksToWait"/>, or a body that says so, <see cref="SourceBusyException"/>), or that it
    /// failed.
    /// </returns>
    private static (T? Value, string? Problem, Outcome Outcome) Interpret<T>(
        LookupQuery<T> query, ExitResponse response, ISourceApi api)
        where T : class
    {
        const string NotFoundText = "Källan känner inte till det som söktes.";
        const string BusyText = "Källan bad Chainsaw vänta.";
        if (api.Judge(response) is { } verdict)
        {
            return (null, verdict.Problem ?? (verdict.Outcome == Outcome.NotFound ? NotFoundText : FailedText),
                verdict.Outcome);
        }

        if (response.StatusCode == NotFoundStatus)
        {
            return (null, NotFoundText, Outcome.NotFound);
        }

        if (AsksToWait(response) is not null)
        {
            return (null, BusyText, Outcome.Wait);
        }

        if (!response.IsSuccess)
        {
            return (null, $"Källan svarade med status {response.StatusCode.ToString(CultureInfo.InvariantCulture)}.",
                Outcome.Failed);
        }

        try
        {
            return (query.Parse(Readable(api.Info.Code, response.Body.ToArray(), query.Path)), null,
                Outcome.Answered);
        }
        catch (SourceNotFoundException)
        {
            return (null, NotFoundText, Outcome.NotFound);
        }
        catch (SourceBusyException)
        {
            return (null, BusyText, Outcome.Wait);
        }
        catch (SourceKeyRefusedException refused)
        {
            return (null, refused.Said, Outcome.KeyRefused);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Whatever the source sent, the response is sealed afterwards; a body that cannot be read is never
            // allowed to stop that (principle 1) or to escape as an exception (the chains' contracts).
            return (null, FailedText, Outcome.Failed);
        }
    }

    /// <summary>
    /// Gives the case's answer, marked as possibly outdated, when there is one; otherwise says that there is no answer.
    /// </summary>
    /// <typeparam name="T">What is looked up.</typeparam>
    /// <param name="stored">The case's newest answer, if any.</param>
    /// <param name="withoutAnswer">The status when the case has no answer.</param>
    /// <param name="reason">Why the source could not answer, in Swedish.</param>
    /// <param name="failedResponses">The sealed responses that could not be used, if any.</param>
    /// <returns>The answer.</returns>
    public static BitcoinLookup<T> Fallback<T>(
        (T Value, StoredResponse Response)? stored, LookupStatus withoutAnswer, string reason,
        IReadOnlyList<ResponseOrigin>? failedResponses = null)
        where T : class
    {
        var failed = failedResponses ?? [];
        if (stored is not { } found)
        {
            return new BitcoinLookup<T>(withoutAnswer, null, failed, NoAnswer(withoutAnswer, reason));
        }

        var fetchedAt = LocalText(found.Response.Origin.ReceivedLocal);
        var problem = $"{reason} Svaret kommer ur ärendet, hämtat {fetchedAt}, och kan ha ändrats sedan.";
        return new BitcoinLookup<T>(
            LookupStatus.FromCaseMayBeOutdated, found.Value, [found.Response.Origin, .. failed], problem);
    }

    /// <summary>Says that there is no answer, and why.</summary>
    /// <param name="status">The status.</param>
    /// <param name="reason">Why the source could not answer.</param>
    /// <returns>The text for the user.</returns>
    private static string NoAnswer(LookupStatus status, string reason) =>
        status is LookupStatus.Offline or LookupStatus.NotInCase
        ? $"{reason} Ärendet har inget sparat svar."
        : reason;

    /// <summary>
    /// Says whether an endpoint that is switched on could be asked instead of the one that gave a response: one that is
    /// neither that endpoint nor at its host and port (<see cref="IsOther"/>).
    /// </summary>
    /// <param name="origin">The response.</param>
    /// <returns><see langword="true"/> when there is another.</returns>
    public bool HasOtherThan(ResponseOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        return _sources.Plan(_chain).Endpoints.Any(planned => IsOther(planned.Endpoint, origin));
    }

    /// <summary>
    /// Says whether an endpoint is another than the one that gave a response: not the same endpoint, and not at the
    /// same host and port, since the same server added twice (or under another path) would give the same answer.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="origin">The response.</param>
    /// <returns><see langword="true"/> when it is another.</returns>
    public static bool IsOther(SourceEndpoint endpoint, ResponseOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(origin);
        return endpoint.Id != origin.EndpointId
            && !(string.Equals(endpoint.Address.Host, origin.Url.Host, StringComparison.OrdinalIgnoreCase)
                && endpoint.Address.Port == origin.Url.Port);
    }

    /// <summary>An answer refused before anything was asked.</summary>

    /// <typeparam name="T">What is looked up.</typeparam>
    /// <param name="problem">Why, in Swedish.</param>
    /// <returns>The answer.</returns>
    public static BitcoinLookup<T> Refused<T>(string problem)
        where T : class =>
        new(LookupStatus.Failed, null, [], problem);

    /// <summary>
    /// Reads the case's newest answer and the highest block the case knew of when that answer was sealed, off the
    /// calling thread. The depth counts from then, not from now: an answer whose block was shallow when it was fetched
    /// may have left the chain since, which only asking again shows.
    /// </summary>
    /// <typeparam name="T">What is looked up.</typeparam>
    /// <param name="case">The open case.</param>
    /// <param name="query">The lookup.</param>
    /// <param name="cancellationToken">Stops the lookup before it starts.</param>
    /// <returns>The answer, if any, and the height then.</returns>
    public Task<((T Value, StoredResponse Response)? Stored, long? HeightThen)> ReadCaseAsync<T>(
        ICase @case, LookupQuery<T> query, CancellationToken cancellationToken)
        where T : class =>
        Task.Run(
            () => @case.Read(reader =>
            {
                var stored = query.FindStored is { } find
                    ? find(reader)
                    : StoredAs(_store.Latest(reader, query.Kind, query.Subject), query.Parse);
                var counts = stored is not null && !query.NeverChanges;
                return (stored, counts ? _store.KnownHeight(reader, stored!.Value.Response.Origin.Sequence) : null);
            }),
            cancellationToken);

    /// <summary>
    /// Reads a stored response with a parser, when there is one. A response that this build can no longer read (or
    /// an index that points at the wrong response) counts as no answer, so the source is asked again.
    /// </summary>
    /// <typeparam name="T">What is read.</typeparam>
    /// <param name="response">The response, or <see langword="null"/>.</param>
    /// <param name="parse">The parser; it may give <see langword="null"/> when the response lacks the value.</param>
    /// <returns>The value and the response, or <see langword="null"/>.</returns>
    public static (T Value, StoredResponse Response)? StoredAs<T>(StoredResponse? response, Func<byte[], T?> parse)
        where T : class
    {
        if (response is null)
        {
            return null;
        }

        try
        {
            return parse(Readable(response.Origin.Source, response.Body)) is { } value ? (value, response) : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Decides whether an answer can no longer change.</summary>
    /// <typeparam name="T">What is looked up.</typeparam>
    /// <param name="query">The lookup.</param>
    /// <param name="value">The answer.</param>
    /// <param name="knownHeight">The highest block the case knows of.</param>
    /// <returns><see langword="true"/> when it is never fetched again.</returns>
    private bool IsSettled<T>(LookupQuery<T> query, T value, long? knownHeight)
        where T : class
    {
        if (query.NeverChanges)
        {
            return true;
        }

        if (query.SettledAt(value) is not { } height)
        {
            return false;
        }

        return Confirmations(height, knownHeight) >= _safeConfirmations;
    }

    /// <summary>Counts a block's confirmations from the highest block the case knows of.</summary>
    /// <param name="height">The block's height.</param>
    /// <param name="knownHeight">The highest known block; never lower than the block itself.</param>
    /// <returns>The number of confirmations, at least 1.</returns>
    public static long Confirmations(long height, long? knownHeight) =>
        Math.Max(knownHeight ?? height, height) - height + 1;

    /// <summary>
    /// Says when something is done next, as the user reads it: <c>kl. 14:05</c> on the same local day, <c>den
    /// 2026-11-10 kl. 00:05</c> on another, and <c>vid nästa uppslag</c> when the time has already passed (nothing was
    /// asked, for example when the case was only read).
    /// </summary>
    /// <param name="at">When.</param>
    /// <param name="now">The time now.</param>
    /// <param name="zone">The time zone the user reads times in.</param>
    /// <returns>The text.</returns>
    public static string WhenText(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (at <= now)
        {
            return "vid nästa uppslag";
        }

        var local = TimeZoneInfo.ConvertTime(at, zone);
        var clock = "kl. " + local.ToString("HH:mm", CultureInfo.InvariantCulture);
        return local.Date == TimeZoneInfo.ConvertTime(now, zone).Date
            ? clock
            : $"den {local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} {clock}";
    }

    /// <summary>Gives sealed responses once each, in the order they were sealed.</summary>
    /// <param name="origins">The responses.</param>
    /// <returns>The responses.</returns>
    public static List<ResponseOrigin> Ordered(IEnumerable<ResponseOrigin> origins) =>
        [.. origins.DistinctBy(origin => origin.Sequence).OrderBy(origin => origin.Sequence)];


    /// <summary>Writes a local time for the user.</summary>
    /// <param name="time">The time.</param>
    /// <returns>For example <c>2026-11-07 02:02</c>.</returns>
    public static string LocalText(DateTimeOffset time) =>
        time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Gets the operator who fetches, or says why there is none.</summary>
    /// <param name="problem">Why there is no operator, in Swedish.</param>
    /// <returns>The operator, or <see langword="null"/>.</returns>
    private OperatorProfile? CurrentOperator(out string? problem)
    {
        problem = null;
        try
        {
            var profile = _operators.Get();
            if (profile is null)
            {
                problem =
                    "Ingen operatörsprofil är sparad. Chainsaw hämtar inget utan att kunna säga vem som hämtade.";
            }

            return profile;
        }
        catch (VaultException)
        {
            problem = "Valvet är låst. Inget skickades.";
            return null;
        }
    }
}

/// <summary>What a response from an endpoint said, when it was read.</summary>
internal enum Outcome
{
    /// <summary>It answered, and the answer can be used.</summary>
    Answered,

    /// <summary>It does not know what was asked.</summary>
    NotFound,

    /// <summary>It asked Chainsaw to wait: it is asked too fast.</summary>
    Wait,

    /// <summary>
    /// It works, but cannot answer this kind of question as it is set up (Bitcoin Core without <c>txindex=1</c>): the
    /// others are asked, and it is neither paused nor taken as saying that the chain does not have it.
    /// </summary>
    CannotAnswer,

    /// <summary>It failed, or its answer cannot be used.</summary>
    Failed,

    /// <summary>It refused the API key: the problem is the source's own words.</summary>
    KeyRefused,
}

/// <summary>
/// Thrown by a parser when the source's body says that it is asked too fast (the Etherscan-style APIs answer a rate
/// limit with 200 and <c>status 0</c>), which the lookups treat as a 429: wait for it and ask the others meanwhile.
/// </summary>
internal sealed class SourceBusyException : FormatException
{
    /// <summary>Creates the exception.</summary>
    public SourceBusyException()
        : base("The source says it is asked too fast.")
    {
    }
}

/// <summary>
/// Thrown by a parser when the source's body says that the API key is missing or refused (Etherscan answers that with
/// 200 and <c>status 0</c>), so that the user is told so instead of that the answer could not be read.
/// </summary>
/// <param name="said">What the source answered.</param>
internal sealed class SourceKeyRefusedException(string said) : FormatException("The source refused the API key.")
{
    /// <summary>Gets what the source answered.</summary>
    public string Said { get; } = said;
}

/// <summary>
/// Thrown by a parser when the source's body says that it does not know what was asked (an unknown transaction or
/// token), which the lookups treat as a 404: an answer, not a broken source.
/// </summary>
internal sealed class SourceNotFoundException : FormatException
{
    /// <summary>Creates the exception.</summary>
    public SourceNotFoundException()
        : base("The source does not know what was asked.")
    {
    }
}
