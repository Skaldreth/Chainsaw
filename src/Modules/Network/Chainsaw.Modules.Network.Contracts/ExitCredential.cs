namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// A source's API key, sent with a request through the exit: as one header, or, for a source that takes its key only
/// in the address (Etherscan's <c>apikey</c>), as one query parameter that the exit adds to the message it sends and
/// never to the request's address. Either way the key is never part of the address a response is sealed under in a
/// case; it comes from the vault for each request and is masked when written out.
/// </summary>
/// <param name="Header">
/// The header the key is sent in; one of <see cref="AllowedHeaders"/>, so that a key can never set a header that
/// says something about the user or the computer (cookies, referer, the host). For a key sent in the address, the
/// query parameter instead; one of <see cref="AllowedQueryParameters"/>.
/// </param>
/// <param name="Value">
/// The header's value, for example <c>Bearer</c> and the key; printable ASCII only, so no line breaks.
/// </param>
public sealed record ExitCredential(string Header, string Value)
{
    /// <summary>The headers a key may be sent in.</summary>
    public static readonly IReadOnlyList<string> AllowedHeaders = ["Authorization", "X-Api-Key"];

    /// <summary>The query parameters a key may be sent in.</summary>
    public static readonly IReadOnlyList<string> AllowedQueryParameters = ["apikey"];

    /// <summary>
    /// Gets whether the key goes in the address as the query parameter <see cref="Header"/> names, added by the exit
    /// to the message it sends, rather than as a header.
    /// </summary>
    public bool InQuery { get; init; }

    /// <summary>
    /// Gets whether the header or parameter is allowed and the value can be sent as it is: printable ASCII in a
    /// header, and only letters, digits, <c>-</c>, <c>_</c>, <c>.</c> and <c>~</c> in the address, so that a key never
    /// needs escaping and can never add another parameter.
    /// </summary>
    public bool IsWellFormed => InQuery
        ? AllowedQueryParameters.Contains(Header, StringComparer.Ordinal)
            && Value is { Length: > 0 } && Value.All(IsUnreserved)
        : AllowedHeaders.Contains(Header, StringComparer.Ordinal)
            && Value is { Length: > 0 } && Value.All(c => c is >= ' ' and <= '~');

    /// <summary>Makes a key that is sent as a query parameter in the address.</summary>
    /// <param name="parameter">The parameter, one of <see cref="AllowedQueryParameters"/>.</param>
    /// <param name="value">The key.</param>
    /// <returns>The credential.</returns>
    public static ExitCredential Query(string parameter, string value) => new(parameter, value) { InQuery = true };

    /// <summary>Tells whether a character is unreserved in an address (RFC 3986) and so is sent as it is.</summary>
    /// <param name="c">The character.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool IsUnreserved(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~';

    /// <summary>Gives the credential without its value, so that it can be logged or shown safely.</summary>
    /// <returns>The header with the value masked.</returns>
    public override string ToString() =>
        $"ExitCredential {{ Header = {Header}, InQuery = {InQuery}, Value = *** }}";
}
