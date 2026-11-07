namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// The only way out of the computer for Chainsaw's modules (principle 4). Every request leaves through the program's
/// connected exit (VPN); when no exit is connected the request is refused before anything is sent. There is no
/// fallback to the computer's own network.
/// </summary>
public interface IExitHttp
{
    /// <summary>Sends a request through the connected exit and reads the whole response.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The response with its raw body, status and times.</returns>
    /// <exception cref="ExitNotConnectedException">No exit is connected; nothing was sent.</exception>
    /// <exception cref="ExitException">
    /// The request timed out, the response was too large, the request failed on the way or the exit was disconnected
    /// while it was in flight.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The request is not allowed (not https, user information, a GET body).
    /// </exception>
    /// <exception cref="OperationCanceledException">The caller cancelled the request.</exception>
    Task<ExitResponse> SendAsync(ExitRequest request, CancellationToken cancellationToken = default);
}
