namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// The response to a request through the exit, with the body exactly as it came, so that the caller can hash and keep
/// the raw response (principle 1). Any status is a response, also 404 or 500; only a failure to get one throws.
/// </summary>
/// <param name="Uri">The address that was asked.</param>
/// <param name="ExitId">The exit the request left through.</param>
/// <param name="StatusCode">The HTTP status.</param>
/// <param name="ContentType">The response's media type, or <see langword="null"/> when it had none.</param>
/// <param name="Body">The body's bytes, not decompressed or decoded.</param>
/// <param name="RequestedAt">When the request was sent, in UTC.</param>
/// <param name="ReceivedAt">When the whole body had arrived, in UTC.</param>
public sealed record ExitResponse(
    Uri Uri,
    Guid ExitId,
    int StatusCode,
    string? ContentType,
    ReadOnlyMemory<byte> Body,
    DateTimeOffset RequestedAt,
    DateTimeOffset ReceivedAt)
{
    /// <summary>Gets how long the request took.</summary>
    public TimeSpan Duration => ReceivedAt - RequestedAt;

    /// <summary>Gets whether the status is a success (200–299).</summary>
    public bool IsSuccess => StatusCode is >= 200 and <= 299;
}
