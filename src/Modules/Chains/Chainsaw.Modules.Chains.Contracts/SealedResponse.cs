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
/// What the chains' lookups found in a sealed response where it was first sealed: the chain's height it shows and the
/// transactions in it with their blocks. It is read from the response by the source that sealed it and holds nothing
/// the response does not; with it, a response sealed again in another case answers lookups there as it did where it was
/// fetched (see <see cref="IResponseTransfer.Seal(ICaseTransaction, SealedResponse, SealedResponseIndex)"/>).
/// </summary>
/// <param name="Height">The chain's height the response shows, or <see langword="null"/>.</param>
/// <param name="Transactions">The transactions in it, with their block's height when confirmed.</param>
public sealed record SealedResponseIndex(long? Height, IReadOnlyList<SealedResponseTransaction> Transactions)
{
    /// <summary>
    /// Gets whether the lookup took the response as the source not knowing what was asked (a <c>404</c> it believed),
    /// so that it is taken so in the other case too; such a response is never an answer.
    /// </summary>
    public bool NotFound { get; init; }
}

/// <summary>A transaction a sealed response holds.</summary>
/// <param name="Txid">The transaction's id as the chain writes it.</param>
/// <param name="BlockHeight">Its block's height, or <see langword="null"/> while unconfirmed.</param>
public sealed record SealedResponseTransaction(string Txid, long? BlockHeight)
{
    /// <summary>Gets its block's hash, or <see langword="null"/> while unconfirmed or when it is not known.</summary>
    public string? BlockHash { get; init; }
}

/// <summary>
/// Moves sealed raw responses from one case to another: reads one with all its values, and seals it again unchanged in
/// another case, after checking its SHA-256 (principle 1). A response sealed again with <see cref="Seal(ICaseTransaction,
/// SealedResponse)"/> is evidence of what was fetched and is not used as an answer (the background watching); one
/// sealed again with its index answers the lookups as a cached answer would, so that what cannot have changed is not
/// fetched again (Nytt ärende's lookups in advance).
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

    /// <summary>
    /// Reads what the lookups found in a sealed response, so that it can answer them in another case too.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="sequence">The response's number.</param>
    /// <returns>
    /// The index, or <see langword="null"/> when the response is not an answer the lookups use (an error status or a
    /// body that did not add up, or one sealed again as evidence only).
    /// </returns>
    SealedResponseIndex? ReadIndex(ICaseReader reader, long sequence);

    /// <summary>
    /// Seals a response in a case, unchanged, as the next record of its raw responses, and lets the lookups use it as
    /// an answer there: an answer that cannot have changed (a transaction deep in the chain, a block by its hash) is
    /// then taken from the case without asking, and everything else is asked for again as usual.
    /// </summary>
    /// <param name="transaction">The case's write.</param>
    /// <param name="response">The response.</param>
    /// <param name="index">What the lookups found in it where it was first sealed (<see cref="ReadIndex"/>).</param>
    /// <returns>Where it now lies in the case.</returns>
    /// <exception cref="ArgumentException">
    /// Its body does not match its SHA-256, a value is missing or not one the case can hold, or a transaction in the
    /// index is not a transaction id.
    /// </exception>
    ResponseOrigin Seal(ICaseTransaction transaction, SealedResponse response, SealedResponseIndex index);
}
