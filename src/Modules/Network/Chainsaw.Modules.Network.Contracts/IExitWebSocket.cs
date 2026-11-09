namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// A websocket the way out chosen in the settings and nowhere else (principle 4), for a source's own news (a new
/// block) where it has it: with VPN through the connected exit, with Windows through the computer's own network, and
/// without either nothing is sent. Only <c>wss</c>, with the same neutral User-Agent as every request, no cookies and
/// no other header. The socket is closed when the exit is disconnected or the way out changes.
/// </summary>
public interface IExitWebSocket
{
    /// <summary>Opens a websocket.</summary>
    /// <param name="address">The address: absolute <c>wss</c>, not on the computer or a private network.</param>
    /// <param name="cancellationToken">Stops the attempt.</param>
    /// <returns>The open socket, which the caller disposes.</returns>
    /// <exception cref="ExitNotConnectedException">There is no way out now; nothing was sent.</exception>
    /// <exception cref="ExitException">The socket could not be opened (refused, timed out, not a websocket).</exception>
    /// <exception cref="ArgumentException">The address is not allowed.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled the attempt.</exception>
    Task<IExitSocket> ConnectAsync(Uri address, CancellationToken cancellationToken = default);
}

/// <summary>An open websocket through the way out: text messages in and out, each with a size limit.</summary>
public interface IExitSocket : IAsyncDisposable
{
    /// <summary>Sends a text message.</summary>
    /// <param name="text">The message.</param>
    /// <param name="cancellationToken">Stops the sending; the socket is then closed.</param>
    /// <returns>A task that completes when it is sent.</returns>
    /// <exception cref="ExitException">The socket was closed or the way out went.</exception>
    Task SendAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for the next text message. A message longer than the limit is read to its end and passed over, so that a
    /// source can never make Chainsaw hold more than the limit.
    /// </summary>
    /// <param name="maximumBytes">The longest message that is given.</param>
    /// <param name="cancellationToken">Stops the waiting; the socket is then closed.</param>
    /// <returns>The message, or <see langword="null"/> when the source closed the socket.</returns>
    /// <exception cref="ExitException">The socket broke or the way out went.</exception>
    Task<string?> ReceiveAsync(int maximumBytes, CancellationToken cancellationToken = default);
}
