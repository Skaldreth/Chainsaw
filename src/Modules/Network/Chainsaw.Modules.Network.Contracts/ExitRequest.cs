namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// A request through the exit. It carries only what the lookup needs: the address, and for a POST the body. Chainsaw
/// adds no cookies, no referer and no headers about the case, the user or the organisation.
/// </summary>
/// <param name="Method">The method.</param>
/// <param name="Uri">The absolute <c>https</c> address, without user information.</param>
public sealed record ExitRequest(ExitMethod Method, Uri Uri)
{
    /// <summary>Gets the body of a POST; must be empty for a GET.</summary>
    public ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>Gets the body's media type, for example <c>application/json</c>; required when there is a body.</summary>
    public string? ContentType { get; init; }

    /// <summary>Gets the media type asked for (the <c>Accept</c> header), or <see langword="null"/> for none.</summary>
    public string? Accept { get; init; }

    /// <summary>Creates a GET request.</summary>
    /// <param name="uri">The address.</param>
    /// <returns>The request.</returns>
    public static ExitRequest Get(Uri uri) => new(ExitMethod.Get, uri);
}
