using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// How every chain's source answers a lookup, written once: from the case when the answer cannot have changed,
/// otherwise from the chain's endpoints in the order of its mode, through <see cref="IExitHttp"/> only, each response
/// sealed in the case with its endpoint before it is used (principle 1), and from the case again, saying so, when no
/// endpoint can be asked (principle 9). A chain's source says what to ask and how to read it (<see
/// cref="LookupQuery{T}"/>).
/// </summary>
internal sealed class SourceLookups
{
    /// <summary>The HTTP status for something the source does not know.</summary>
    private const int NotFoundStatus = 404;

    /// <summary>The chain whose endpoints are asked.</summary>
    private readonly Chain _chain;

    /// <summary>The only way out.</summary>
    private readonly IExitHttp _http;

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

    /// <summary>Creates the lookups of one chain.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="chainName">The chain's name in the texts for the user, for example <c>Bitcoin</c>.</param>
    /// <param name="http">The only way out.</param>
    /// <param name="operators">The operator profile.</param>
    /// <param name="sources">The chains' endpoints.</param>
    /// <param name="store">The chain's sealed responses.</param>
    /// <param name="safeConfirmations">How deep an answer's block must be before it is never fetched again.</param>
    /// <param name="refreshHeight">
    /// Asks for the chain's height when the known one is old; called before an answer whose block was shallow is asked
    /// for again, so that the new answer can count as settled.
    /// </param>
    /// <exception cref="ArgumentException">The depth is less than 1.</exception>
    public SourceLookups(
        Chain chain, string chainName, IExitHttp http, IOperatorProfileStore operators, ChainSources sources,
        RawResponseStore store, int safeConfirmations,
        Func<SourceLookups, ICase, CancellationToken, Task> refreshHeight)
    {
        if (safeConfirmations < 1)
        {
            throw new ArgumentException("The depth must be at least 1.", nameof(safeConfirmations));
        }

        (_chain, _http, _operators, _sources, _store) = (chain, http, operators, sources, store);
        (_safeConfirmations, _refreshHeight) = (safeConfirmations, refreshHeight);
        _sourcesOffText = $"Källorna för {chainName} är avstängda i inställningarna (Av). Inget skickades.";
        _noneOnText = $"Ingen källa för {chainName} är påslagen i inställningarna. Inget skickades.";
    }

    /// <summary>Gets the chain's sealed responses.</summary>
    public RawResponseStore Store => _store;

    /// <summary>
    /// Answers a lookup: from the case when the answer cannot have changed, otherwise from the source through the
    /// exit, sealed in the case before it is used, and from the case again when the source cannot be asked.
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
        // answer can count as settled once its block is deep enough.
        if (stored is { } shallow && query.SettledAt(shallow.Value) is not null)
        {
            await _refreshHeight(this, @case, cancellationToken).ConfigureAwait(false);
        }

        return await FetchAsync(@case, query, stored, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the source, seals the response and reads it; falls back on the case's answer when it must.
    /// </summary>
    /// <typeparam name="T">What is looked up.</typeparam>
    /// <param name="case">The open case.</param>
    /// <param name="query">The lookup.</param>
    /// <param name="stored">The case's newest answer, if any.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The answer.</returns>
    public async Task<BitcoinLookup<T>> FetchAsync<T>(
        ICase @case, LookupQuery<T> query, (T Value, StoredResponse Response)? stored,
        CancellationToken cancellationToken)
        where T : class
    {
        var fetchedBy = CurrentOperator(out var noOperator);
        if (fetchedBy is null)
        {
            return Fallback(stored, LookupStatus.Failed, noOperator!);
        }

        var plan = _sources.Plan(_chain);
        if (plan.Off)
        {
            return Fallback(stored, LookupStatus.Offline, _sourcesOffText);
        }

        if (plan.Endpoints.Count == 0)
        {
            return Fallback(stored, LookupStatus.Failed, _noneOnText);
        }

        // The endpoints are asked in the plan's order; the next is asked when one fails, does not answer in time or
        // gives an answer that cannot be used. Every response is sealed with the endpoint it came from (principle 1).
        var failures = new List<ResponseOrigin>();
        var problem = _noneOnText;
        string? notFound = null;
        var trustedNotFound = false;
        var onlyNotFound = true;
        foreach (var planned in plan.Endpoints)
        {
            var endpoint = planned.Endpoint;
            var unkeyed = planned.Api.Request(endpoint.Address, query.Path);
            if (_sources.WithKey(endpoint, planned.Api, unkeyed, out var keyProblem) is not { } request)
            {
                _sources.Failed(_chain, endpoint.Id);
                problem = keyProblem!;
                onlyNotFound = false;
                continue;
            }

            ExitResponse response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (ExitNotConnectedException)
            {
                return Fallback(stored, LookupStatus.Offline, "Ingen utgång är ansluten, och inget skickades.",
                    failures);
            }
            catch (ExitException exception)
            {
                _sources.Failed(_chain, endpoint.Id);
                problem = exception.Message;
                onlyNotFound = false;
                continue;
            }

            var (value, interpretProblem, saysNotFound) = Interpret(query, response);
            var sealedResponse = await Task.Run(
                () => @case.Write(transaction => _store.Append(
                    transaction, query.Kind, query.Subject, response, endpoint, fetchedBy,
                    value is null ? null : query.Index(value))),
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

            if (saysNotFound)
            {
                // The source says it does not know it. From an endpoint whose probe did not fail that is an answer,
                // and no other endpoint is asked; from one that failed its probe or lags behind (a wrong path, a node
                // still syncing), the next is asked, so that one broken endpoint never says that a tx does not exist.
                failures.Add(sealedResponse.Origin);
                notFound = interpretProblem;
                if (_sources.TrustsNotFound(_chain, endpoint.Id))
                {
                    trustedNotFound = true;
                    break;
                }

                continue;
            }

            _sources.Failed(_chain, endpoint.Id);
            failures.Add(sealedResponse.Origin);
            problem = interpretProblem!;
            onlyNotFound = false;
        }

        // Not found is said only when an endpoint that can be believed said it, or every endpoint asked said it.
        var isNotFound = notFound is not null && (trustedNotFound || onlyNotFound);
        if (isNotFound && stored is null)
        {
            return new BitcoinLookup<T>(LookupStatus.NotFound, null, failures, notFound);
        }

        return Fallback(stored, LookupStatus.Failed, isNotFound ? notFound! : problem, failures);
    }

    /// <summary>Reads a fresh response, or says why it cannot be used.</summary>
    /// <typeparam name="T">What is looked up.</typeparam>
    /// <param name="query">The lookup.</param>
    /// <param name="response">The response.</param>
    /// <returns>
    /// The value, or <see langword="null"/> and the problem in Swedish, and whether the source said it does not know
    /// what was asked: HTTP 404, or a body that says so (<see cref="SourceNotFoundException"/>, as the
    /// Etherscan-style APIs answer 200 with <c>status 0</c>).
    /// </returns>
    private static (T? Value, string? Problem, bool NotFound) Interpret<T>(
        LookupQuery<T> query, ExitResponse response)
        where T : class
    {
        const string NotFoundText = "Källan känner inte till det som söktes.";
        if (response.StatusCode == NotFoundStatus)
        {
            return (null, NotFoundText, true);
        }

        if (!response.IsSuccess)
        {
            return (null, $"Källan svarade med status {response.StatusCode.ToString(CultureInfo.InvariantCulture)}.",
                false);
        }

        try
        {
            return (query.Parse(response.Body.ToArray()), null, false);
        }
        catch (SourceNotFoundException)
        {
            return (null, NotFoundText, true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Whatever the source sent, the response is sealed afterwards; a body that cannot be read is never
            // allowed to stop that (principle 1) or to escape as an exception (the chains' contracts).
            return (null,
                "Källans svar gick inte att tolka eller gick inte ihop. Det är sparat i ärendet men används inte.",
                false);
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
                var stored = query.FindStored(reader);
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
            return parse(response.Body) is { } value ? (value, response) : null;
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
