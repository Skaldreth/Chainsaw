using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution;

/// <summary>Asks the suppliers of attribution about an address (see <see cref="IAttributionProviders"/>).</summary>
/// <param name="providers">The suppliers, in the order they are asked.</param>
/// <param name="http">The exit, the only way out (principle 4).</param>
/// <param name="keys">The vault's API keys.</param>
/// <param name="profiles">The operator profile, who fetched.</param>
/// <param name="timeZone">The time zone the local time is sealed in.</param>
internal sealed class AttributionProviders(
    IEnumerable<IAttributionProvider> providers,
    IExitHttp http,
    IApiKeyStore keys,
    IOperatorProfileStore profiles,
    TimeZoneInfo timeZone) : IAttributionProviders
{
    /// <summary>The ordered suppliers.</summary>
    private readonly IReadOnlyList<IAttributionProvider> _providers = [.. providers];

    /// <summary>
    /// When each supplier that asked Chainsaw to wait (429) may be asked again, so that its limit is always kept and
    /// nothing is sent before then (CLAUDE.md, <em>Rate limits respekteras alltid</em>).
    /// </summary>
    private readonly Dictionary<string, DateTimeOffset> _waitUntil = new(StringComparer.Ordinal);

    /// <summary>How long Chainsaw waits after a 429 that does not say how long.</summary>
    private static readonly TimeSpan DefaultWait = TimeSpan.FromMinutes(15);

    /// <summary>Creates the service with the computer's own time zone.</summary>
    /// <param name="providers">The suppliers.</param>
    /// <param name="http">The exit.</param>
    /// <param name="keys">The vault's API keys.</param>
    /// <param name="profiles">The operator profile.</param>
    public AttributionProviders(
        IEnumerable<IAttributionProvider> providers, IExitHttp http, IApiKeyStore keys, IOperatorProfileStore profiles)
        : this(providers, http, keys, profiles, TimeZoneInfo.Local)
    {
    }

    /// <inheritdoc/>
    public IReadOnlyList<AttributionProviderInfo> Providers => [.. _providers.Select(provider => provider.Info)];

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProviderLookup>> LookupAsync(
        ICase @case, ChainAddress address, IReadOnlyCollection<string> enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(enabled);
        var results = new List<ProviderLookup>();
        foreach (var provider in _providers.Where(provider => enabled.Contains(provider.Info.Name)))
        {
            results.Add(await AskAsync(@case, provider, address, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    /// <summary>Asks one supplier, seals its response and labels what it reports.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="provider">The supplier.</param>
    /// <param name="address">The address.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>What it came to.</returns>
    private async Task<ProviderLookup> AskAsync(
        ICase @case, IAttributionProvider provider, ChainAddress address, CancellationToken cancellationToken)
    {
        var name = provider.Info.Name;
        if (!provider.Info.Chains.Contains(address.Chain) || !CanLabel(address))
        {
            return Missing(name, ProviderLookupStatus.NotSupported,
                $"{provider.Info.DisplayName} frågas inte om adresser på den här kedjan. Inget skickades.");
        }

        if (Waiting(name) is { } until)
        {
            return Missing(name, ProviderLookupStatus.RateLimited, string.Format(
                CultureInfo.InvariantCulture,
                "{0} bad Chainsaw vänta till {1:HH:mm} UTC. Inget skickades.",
                provider.Info.DisplayName,
                until.UtcDateTime));
        }

        var (key, fetchedBy, problem) = Credentials(provider);
        if (key is null || fetchedBy is null)
        {
            return Missing(name, ProviderLookupStatus.NoKey, problem!);
        }

        ExitResponse response;
        try
        {
            response = await http.SendAsync(provider.Request(address, key), cancellationToken).ConfigureAwait(false);
        }
        catch (ExitNotConnectedException)
        {
            return Missing(name, ProviderLookupStatus.Offline, "Offline: ingen utgång är ansluten. Inget skickades.");
        }
        catch (ExitException exception)
        {
            return Missing(name, ProviderLookupStatus.Failed, exception.Message);
        }

        // Every response is sealed, also a refusal, so that what was asked and answered can always be shown.
        var (status, answer, readProblem) = Interpret(provider, response, address);
        if (status == ProviderLookupStatus.RateLimited)
        {
            Wait(name, response);
        }

        return await Task.Run(
            () => @case.Write(transaction =>
            {
                var sealedResponse = SealResponse(transaction, name, address, response, fetchedBy);
                var added = answer is null
                    ? []
                    : Label(transaction, provider, address, answer, sealedResponse);
                return new ProviderLookup(
                    name, status, added, answer?.Reports ?? 0, sealedResponse.Sequence, readProblem);
            }),
            CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Gives when a supplier that asked Chainsaw to wait may be asked again, if it is not yet.</summary>
    /// <param name="provider">The supplier's name.</param>
    /// <returns>The time, or <see langword="null"/> when it may be asked now.</returns>
    private DateTimeOffset? Waiting(string provider)
    {
        lock (_waitUntil)
        {
            return _waitUntil.TryGetValue(provider, out var until) && until > DateTimeOffset.UtcNow ? until : null;
        }
    }

    /// <summary>Remembers how long a supplier asked Chainsaw to wait.</summary>
    /// <param name="provider">The supplier's name.</param>
    /// <param name="response">The 429 response, with its <c>Retry-After</c> when it gave one.</param>
    private void Wait(string provider, ExitResponse response)
    {
        lock (_waitUntil)
        {
            _waitUntil[provider] = DateTimeOffset.UtcNow + (response.RetryAfter ?? DefaultWait);
        }
    }

    /// <summary>Gets whether an address can carry labels: Bitcoin and Ethereum so far.</summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> when a label can be set on it.</returns>
    private static bool CanLabel(ChainAddress address)
    {
        try
        {
            _ = LabelSubject.ForAccount(address);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Gets the supplier's key and who fetches, or says why there is none.</summary>
    /// <param name="provider">The supplier.</param>
    /// <returns>The key's secret, the operator and the problem, in Swedish.</returns>
    private (string? Key, OperatorProfile? FetchedBy, string? Problem) Credentials(IAttributionProvider provider)
    {
        try
        {
            var key = keys.Find(provider.Info.KeyService);
            if (key is null || string.IsNullOrEmpty(key.Secret))
            {
                return (null, null,
                    $"Ingen nyckel för {provider.Info.DisplayName} finns i valvet. Inget skickades.");
            }

            var profile = profiles.Get();
            return profile is null
                ? (null, null,
                    "Ingen operatörsprofil är sparad. Chainsaw hämtar inget utan att kunna säga vem som hämtade.")
                : (key.Secret, profile, null);
        }
        catch (VaultException)
        {
            return (null, null, "Valvet är låst. Inget skickades.");
        }
    }

    /// <summary>Reads what a response means.</summary>
    /// <param name="provider">The supplier.</param>
    /// <param name="response">The response.</param>
    /// <param name="address">The address that was asked about.</param>
    /// <returns>The status, the answer when there is one, and the problem in Swedish when there is not.</returns>
    private static (ProviderLookupStatus Status, ProviderAnswer? Answer, string? Problem) Interpret(
        IAttributionProvider provider, ExitResponse response, ChainAddress address)
    {
        var display = provider.Info.DisplayName;
        switch (response.StatusCode)
        {
            case 401 or 403:
                return (ProviderLookupStatus.Refused, null, $"{display} godtog inte nyckeln i valvet.");
            case 429:
                var wait = response.RetryAfter is { } delay
                    ? $" Försök igen om {Math.Ceiling(delay.TotalMinutes).ToString(CultureInfo.InvariantCulture)}"
                        + " min."
                    : string.Empty;
                return (ProviderLookupStatus.RateLimited, null,
                    $"{display} ber Chainsaw vänta: nyckelns anrop är slut för stunden.{wait}");
            case < 200 or > 299:
                return (ProviderLookupStatus.Failed, null,
                    $"{display} svarade {response.StatusCode.ToString(CultureInfo.InvariantCulture)}.");
        }

        try
        {
            return (ProviderLookupStatus.Answered, provider.Read(response.Body.Span, address), null);
        }
        catch (FormatException)
        {
            return (ProviderLookupStatus.Failed, null, $"{display}s svar gick inte att läsa.");
        }
    }

    /// <summary>Seals a supplier's raw response in the caller's transaction.</summary>
    /// <param name="transaction">The case's write transaction.</param>
    /// <param name="provider">The supplier's name.</param>
    /// <param name="address">The address that was asked about.</param>
    /// <param name="response">The response.</param>
    /// <param name="fetchedBy">Who fetched it.</param>
    /// <returns>The sealed record and the response's SHA-256.</returns>
    private SealedResponse SealResponse(
        ICaseTransaction transaction, string provider, ChainAddress address, ExitResponse response,
        OperatorProfile fetchedBy)
    {
        var body = response.Body.ToArray();
        var sha256 = SHA256.HashData(body);
        var local = TimeZoneInfo.ConvertTime(response.ReceivedAt, timeZone);
        var record = transaction.AppendSealed(
            AttributionSchema.ResponsesChain,
            [
                provider, response.Uri.AbsoluteUri, address.Chain.ToString(), address.Value, (long)response.StatusCode,
                response.ContentType, Text(response.RequestedAt), Text(response.ReceivedAt), Text(local), timeZone.Id,
                response.ExitIdText, response.RouteText, JsonSerializer.Serialize(fetchedBy), sha256, body,
            ]);
        return new SealedResponse(record.Sequence, Convert.ToHexStringLower(sha256), response.ReceivedAt);
    }

    /// <summary>
    /// Labels what a supplier reports, with one line in the journal for the lookup. A finding already labelled in the
    /// case from the same supplier, also one taken back, is not labelled again.
    /// </summary>
    /// <param name="transaction">The case's write transaction.</param>
    /// <param name="provider">The supplier.</param>
    /// <param name="address">The address.</param>
    /// <param name="answer">What it reports.</param>
    /// <param name="response">The sealed response the labels rest on.</param>
    /// <returns>The new labels.</returns>
    private static IReadOnlyList<LabelEntry> Label(
        ICaseTransaction transaction, IAttributionProvider provider, ChainAddress address, ProviderAnswer answer,
        SealedResponse response)
    {
        var info = provider.Info;
        var subject = LabelSubject.ForAccount(address);
        var stored = LabelStore.Read(transaction);
        var fresh = answer.Findings
            .Select(finding => (Finding: finding, Origin: $"{info.Name}/{finding.Key}"))
            // Any way in counts: a copy from another case through the library is the same finding, never a second one.
            .Where(found => !stored.Has(LabelStore.Key(found.Origin, subject, found.Finding.Kind, found.Finding.Name)))
            .ToList();
        var journal = transaction.AppendJournal(new JournalDraft(
            AttributionJournalKinds.ProviderAsked,
            string.Format(
                CultureInfo.InvariantCulture,
                "{0} om {1}: {2} rapporter, {3} nya etiketter",
                info.DisplayName,
                subject,
                answer.Reports,
                fresh.Count),
            JsonSerializer.Serialize(new
            {
                provider = info.Name,
                chain = address.Chain.ToString(),
                address = address.Value,
                response = response.Sequence,
                sha256 = response.Sha256,
                reports = answer.Reports,
                labels = fresh.Select(found => found.Finding.Name),
            })));
        var version = string.Format(
            CultureInfo.InvariantCulture,
            "svar {0} {1:yyyy-MM-dd HH:mm} UTC, SHA-256 {2}",
            response.Sequence,
            response.ReceivedUtc.UtcDateTime,
            response.Sha256[..12]);
        return
        [
            .. fresh.Select(found => LabelStore.Seal(
                transaction,
                new LabelEntry(
                    0, subject, found.Finding.Kind, found.Finding.Name,
                    new LabelOrigin(
                        found.Origin, LabelSource.Provider, LabelSource.Provider, info.DisplayName, version,
                        journal.Operator, journal.Utc, found.Finding.Reason, null),
                    false, 0),
                journal)),
        ];
    }

    /// <summary>Writes a time as it is sealed.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The round-trip text.</returns>
    private static string Text(DateTimeOffset time) => time.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>A lookup that sent nothing and sealed nothing.</summary>
    /// <param name="provider">The supplier's name.</param>
    /// <param name="status">Why.</param>
    /// <param name="problem">Why, in Swedish.</param>
    /// <returns>The lookup.</returns>
    private static ProviderLookup Missing(string provider, ProviderLookupStatus status, string problem) =>
        new(provider, status, [], 0, null, problem);

    /// <summary>A sealed response.</summary>
    /// <param name="Sequence">Its sequence in the chain.</param>
    /// <param name="Sha256">The SHA-256 of its bytes, in lower-case hex.</param>
    /// <param name="ReceivedUtc">When it arrived.</param>
    private sealed record SealedResponse(long Sequence, string Sha256, DateTimeOffset ReceivedUtc);
}
