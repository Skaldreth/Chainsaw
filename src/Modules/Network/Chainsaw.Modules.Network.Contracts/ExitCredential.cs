namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// A source's API key, sent as one header with a request through the exit. It is never part of the address, so it is
/// never sealed with a response in a case; it comes from the vault for each request and is masked when written out.
/// </summary>
/// <param name="Header">
/// The header the key is sent in; one of <see cref="AllowedHeaders"/>, so that a key can never set a header that
/// says something about the user or the computer (cookies, referer, the host).
/// </param>
/// <param name="Value">
/// The header's value, for example <c>Bearer</c> and the key; printable ASCII only, so no line breaks.
/// </param>
public sealed record ExitCredential(string Header, string Value)
{
    /// <summary>The headers a key may be sent in.</summary>
    public static readonly IReadOnlyList<string> AllowedHeaders = ["Authorization", "X-Api-Key"];

    /// <summary>Gets whether the header is allowed and the value can be sent as it is.</summary>
    public bool IsWellFormed =>
        AllowedHeaders.Contains(Header, StringComparer.Ordinal)
        && Value is { Length: > 0 }
        && Value.All(c => c is >= ' ' and <= '~');

    /// <summary>Gives the credential without its value, so that it can be logged or shown safely.</summary>
    /// <returns>The header with the value masked.</returns>
    public override string ToString() => $"ExitCredential {{ Header = {Header}, Value = *** }}";
}
