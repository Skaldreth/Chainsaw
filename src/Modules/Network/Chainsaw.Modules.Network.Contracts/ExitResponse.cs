namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// The response to a request through the exit, with the body exactly as it came, so that the caller can hash and keep
/// the raw response (principle 1). Any status is a response, also 404 or 500; only a failure to get one throws.
/// </summary>
/// <param name="Uri">The address that was asked.</param>
/// <param name="ExitId">
/// The exit (VPN) the request left through, or <see cref="Guid.Empty"/> when it left through Windows.
/// </param>
/// <param name="StatusCode">The HTTP status.</param>
/// <param name="ContentType">The response's media type, or <see langword="null"/> when it had none.</param>
/// <param name="Body">The body's bytes, not decompressed or decoded.</param>
/// <param name="RequestedAt">When the request was sent, in UTC.</param>
/// <param name="ReceivedAt">When the whole body had arrived, in UTC.</param>
/// <param name="Route">
/// Which sort of way out the request took (principle 1: every sealed response says it), VPN or Windows.
/// </param>
public sealed record ExitResponse(
    Uri Uri,
    Guid ExitId,
    int StatusCode,
    string? ContentType,
    ReadOnlyMemory<byte> Body,
    DateTimeOffset RequestedAt,
    DateTimeOffset ReceivedAt,
    ExitRoute Route)
{
    /// <summary>
    /// Gets whether the request went straight to the user's own node on the computer or a private network
    /// (<see cref="ILocalHttp"/>) and never through a way out; <see cref="Route"/> then says nothing.
    /// </summary>
    public bool IsLocal { get; init; }

    /// <summary>
    /// Gets how long the source asked Chainsaw to wait before asking again (the <c>Retry-After</c> header, as a delay
    /// or a date), or <see langword="null"/> when it did not say.
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>
    /// Gets the source's own time for the response (the <c>Date</c> header), or <see langword="null"/> when it gave
    /// none; the source's clock, not Chainsaw's, for an origin that wants it.
    /// </summary>
    public DateTimeOffset? ServerDate { get; init; }

    /// <summary>
    /// Gets the source's tag for the version of what it answered (the <c>ETag</c> header, as sent), or
    /// <see langword="null"/> when it gave none.
    /// </summary>
    public string? ETag { get; init; }

    /// <summary>Gets how long the request took.</summary>
    public TimeSpan Duration => ReceivedAt - RequestedAt;

    /// <summary>
    /// Gets the exit's id as it is sealed with a response (<c>D</c> form), or <see langword="null"/> through Windows
    /// or to the user's own node, which are no exits of Chainsaw's.
    /// </summary>
    public string? ExitIdText => Route == ExitRoute.Vpn && !IsLocal ? ExitId.ToString("D") : null;

    /// <summary>
    /// Gets the sort of way out as it is sealed with a response: <c>vpn</c>, <c>windows</c>, or <c>local</c> for the
    /// user's own node on the computer or a private network.
    /// </summary>
    public string RouteText => IsLocal ? LocalRouteCode : Route.Code();

    /// <summary>The code sealed with a response from the user's own node, which took no way out.</summary>
    public const string LocalRouteCode = "local";

    /// <summary>Gets whether the status is a success (200–299).</summary>
    public bool IsSuccess => StatusCode is >= 200 and <= 299;
}
