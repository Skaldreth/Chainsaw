using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// A raw response exactly as it was sealed (principle 1): every value of its record, so that it can be sealed again
/// in another case unchanged. The background watching fetches its responses outside the case and brings them in this
/// way when the case is opened (CLAUDE.md, <em>Bevaka</em>). The times and who fetched are kept as the text that was
/// sealed, so that nothing is rewritten on the way.
/// </summary>
/// <param name="Sequence">The response's number where it was sealed; a new one is given when it is sealed again.</param>
/// <param name="Chain">The chain's name in the record, for example <c>bitcoin-mainnet</c>.</param>
/// <param name="Source">The sort of API, for example <c>esplora</c>.</param>
/// <param name="EndpointId">The endpoint's id.</param>
/// <param name="EndpointName">The endpoint's name then.</param>
/// <param name="Url">The address that was asked.</param>
/// <param name="Kind">What was looked up.</param>
/// <param name="Subject">About what.</param>
/// <param name="StatusCode">The HTTP status.</param>
/// <param name="MediaType">The media type, if any.</param>
/// <param name="RequestedUtc">When it was asked, as sealed (round-trip text, UTC).</param>
/// <param name="ReceivedUtc">When it arrived, as sealed (UTC).</param>
/// <param name="ReceivedLocal">When it arrived in the local time of the computer that fetched it, as sealed.</param>
/// <param name="TimeZone">That computer's time zone.</param>
/// <param name="ExitId">The exit it came through, if any.</param>
/// <param name="ExitRoute">The sort of way out, for example <c>windows</c>.</param>
/// <param name="FetchedBy">Who fetched it, as the sealed JSON.</param>
/// <param name="Sha256">The SHA-256 of the body, in lower-case hex.</param>
/// <param name="Body">The raw body.</param>
public sealed record SealedResponse(
    long Sequence,
    string Chain,
    string Source,
    Guid EndpointId,
    string EndpointName,
    string Url,
    string Kind,
    string Subject,
    int StatusCode,
    string? MediaType,
    string RequestedUtc,
    string ReceivedUtc,
    string ReceivedLocal,
    string TimeZone,
    string? ExitId,
    string ExitRoute,
    string FetchedBy,
    string Sha256,
    byte[] Body);

/// <summary>The sealed chain the raw responses are in, in every case.</summary>
public static class SealedResponses
{
    /// <summary>The chain's name.</summary>
    public const string Chain = "chains.raw_responses";
}

/// <summary>
/// Moves sealed raw responses from one case to another: reads one with all its values, and seals it again unchanged in
/// another case, after checking its SHA-256 (principle 1). A response sealed again this way is evidence of what was
/// fetched; it is not used as a cached answer.
/// </summary>
public interface IResponseTransfer
{
    /// <summary>Reads a sealed raw response by its number.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="sequence">The response's number.</param>
    /// <returns>The response, or <see langword="null"/> when there is none with that number.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.Corrupt"/> when its body no longer matches its SHA-256.
    /// </exception>
    SealedResponse? Read(ICaseReader reader, long sequence);

    /// <summary>Seals a response in a case, unchanged, as the next record of its raw responses.</summary>
    /// <param name="transaction">The case's write.</param>
    /// <param name="response">The response.</param>
    /// <returns>Where it now lies in the case.</returns>
    /// <exception cref="ArgumentException">
    /// Its body does not match its SHA-256, or a value is missing or not one the case can hold.
    /// </exception>
    ResponseOrigin Seal(ICaseTransaction transaction, SealedResponse response);
}
