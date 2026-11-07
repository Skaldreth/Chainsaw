namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// The way to the user's own node on the computer or a private network (decided 2026-11-07: an endpoint the user has
/// added and marked <em>lokal</em>, for example <c>https://esplora.lan</c>). Such a request goes straight to the node
/// in the user's own network and never through the exit or Windows' proxy, and it can go nowhere else: an IP address
/// outside the user's own network (<see cref="LocalAddresses.IsOwnNetwork(System.Net.IPAddress)"/>) is refused before
/// anything is built, and a name is resolved by the computer and connected only to its addresses in the user's own
/// network, so that no lookup to an external source can leave the computer directly through it.
/// Only https, without cookies, referer or redirects, and with the certificate checked against the roots installed on
/// the computer without fetching anything.
/// </summary>
public interface ILocalHttp
{
    /// <summary>Sends a request to the user's own node and reads the whole response.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The response, with <see cref="ExitResponse.IsLocal"/> set and no exit.</returns>
    /// <exception cref="ExitException">
    /// <see cref="ExitError.NotLocal"/>: the name points only outside the user's own network, so nothing was sent; or
    /// the request timed out, the response was too large or the request failed on the way.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The request is not allowed: not https, user information, a GET body, or an IP address outside the user's own
    /// network.
    /// </exception>
    /// <exception cref="OperationCanceledException">The caller cancelled the request.</exception>
    Task<ExitResponse> SendAsync(ExitRequest request, CancellationToken cancellationToken = default);
}
