using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// Where a fetched fact comes from: one raw response sealed in the case (principle 1). The response's bytes, their
/// SHA-256 and who fetched it are in the case's sealed table <c>chains.raw_responses</c> under
/// <see cref="Sequence"/>.
/// </summary>
/// <param name="Sequence">The response's number in the sealed table.</param>
/// <param name="Source">The source's name, for example <c>esplora</c>.</param>
/// <param name="Url">The address that was asked.</param>
/// <param name="StatusCode">The HTTP status of the response.</param>
/// <param name="ReceivedUtc">When the response arrived, in UTC.</param>
/// <param name="ReceivedLocal">When the response arrived, in the local time of the computer that fetched it.</param>
/// <param name="FetchedBy">Who fetched it, from the operator profile at the time.</param>
/// <param name="Sha256">The SHA-256 of the raw response's bytes, as lower-case hex.</param>
public sealed record ResponseOrigin(
    long Sequence,
    string Source,
    Uri Url,
    int StatusCode,
    DateTimeOffset ReceivedUtc,
    DateTimeOffset ReceivedLocal,
    OperatorProfile FetchedBy,
    string Sha256);
