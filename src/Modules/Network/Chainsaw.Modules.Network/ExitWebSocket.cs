using System.Net.WebSockets;
using System.Text;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Opens websockets the way out chosen and nowhere else, on the same pipeline as <see cref="ExitHttp"/>: through the
/// connected exit with VPN, through the computer's own network with Windows, never falling back from one to the other.
/// The socket is aborted when the exit is disconnected or the way out is left.
/// </summary>
/// <param name="connection">The connection that holds the pipeline through the exit (VPN).</param>
/// <param name="route">The way out chosen, which holds the pipeline through Windows.</param>
/// <param name="presence">Whether the computer has a network, which the way through Windows needs.</param>
/// <param name="options">The limits.</param>
internal sealed class ExitWebSocket(
    ExitConnection connection, ExitRouteChoice route, INetworkPresence presence, NetworkOptions options)
    : IExitWebSocket
{
    /// <summary>How often the socket says it is alive, so that a quiet connection is not dropped on the way.</summary>
    internal static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public async Task<IExitSocket> ConnectAsync(Uri address, CancellationToken cancellationToken = default)
    {
        Validate(address, options);
        cancellationToken.ThrowIfCancellationRequested();
        var leased = route.TryLeaseWindows(out var lease)
            || (route.Current == ExitRoute.Vpn && connection.TryLease(out lease));
        if (!leased)
        {
            throw new ExitNotConnectedException();
        }

        if (lease.Route == ExitRoute.Windows && !presence.IsAvailable)
        {
            throw ExitNotConnectedException.Offline();
        }

        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("User-Agent", NetworkOptions.UserAgent);
        socket.Options.KeepAliveInterval = KeepAlive;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.Stopping);
        limit.CancelAfter(options.RequestTimeout);
        try
        {
            await socket.ConnectAsync(address, lease.Invoker, limit.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not ExitException)
        {
            socket.Dispose();
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            throw Translate(exception, lease);
        }

        return new Socket(socket, lease);
    }

    /// <summary>
    /// Refuses an address that would leave the encrypted channel or reach the computer or a private network: only an
    /// absolute <c>wss</c> address without user information (plain <c>ws</c> only when the tests allow plain http).
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="options">The limits, with what the tests allow.</param>
    /// <exception cref="ArgumentException">The address is not allowed.</exception>
    internal static void Validate(Uri address, NetworkOptions options)
    {
        ArgumentNullException.ThrowIfNull(address);
        var schemeAllowed = address.IsAbsoluteUri
            && (address.Scheme == Uri.UriSchemeWss || (options.AllowPlainHttp && address.Scheme == Uri.UriSchemeWs));
        if (!schemeAllowed)
        {
            throw new ArgumentException("The address must be an absolute wss address.", nameof(address));
        }

        if (!options.AllowLocalTargets && LocalAddresses.IsLocal(address))
        {
            throw new ArgumentException(
                "The address must not be on the computer or a private network.", nameof(address));
        }

        if (address.UserInfo.Length > 0)
        {
            throw new ArgumentException("The address must not carry user information.", nameof(address));
        }
    }

    /// <summary>Gives a failure on the socket as the way out's error.</summary>
    /// <param name="exception">The failure.</param>
    /// <param name="lease">The way out it went through.</param>
    /// <returns>The error.</returns>
    private static ExitException Translate(Exception exception, ExitLease lease)
    {
        if (lease.Stopping.IsCancellationRequested || exception is ObjectDisposedException)
        {
            return new ExitException(ExitError.ConnectionLost, lease.Route, exception);
        }

        return exception is OperationCanceledException
            ? new ExitException(ExitError.Timeout, lease.Route, exception)
            : new ExitException(ExitError.RequestFailed, lease.Route, exception);
    }

    /// <summary>An open socket through the way out, aborted when the way out goes.</summary>
    private sealed class Socket : IExitSocket
    {
        /// <summary>The socket.</summary>
        private readonly ClientWebSocket _socket;

        /// <summary>The way out it went through.</summary>
        private readonly ExitLease _lease;

        /// <summary>Aborts the socket when the way out goes.</summary>
        private readonly CancellationTokenRegistration _stopping;

        /// <summary>Wraps an open socket.</summary>
        /// <param name="socket">The socket.</param>
        /// <param name="lease">The way out it went through.</param>
        public Socket(ClientWebSocket socket, ExitLease lease)
        {
            (_socket, _lease) = (socket, lease);
            _stopping = lease.Stopping.Register(socket.Abort);
        }

        /// <inheritdoc />
        public async Task SendAsync(string text, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(text);
            try
            {
                await _socket.SendAsync(
                    Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException
                or IOException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                throw Translate(exception, _lease);
            }
        }

        /// <inheritdoc />
        public async Task<string?> ReceiveAsync(int maximumBytes, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumBytes, 0);
            var buffer = new byte[Math.Min(maximumBytes, 64 * 1024)];
            using var message = new MemoryStream();
            var tooLong = false;
            try
            {
                while (true)
                {
                    var part = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (part.MessageType == WebSocketMessageType.Close)
                    {
                        return null;
                    }

                    tooLong |= message.Length + part.Count > maximumBytes;
                    if (!tooLong)
                    {
                        message.Write(buffer, 0, part.Count);
                    }

                    if (!part.EndOfMessage)
                    {
                        continue;
                    }

                    if (tooLong || part.MessageType != WebSocketMessageType.Text)
                    {
                        // Passed over: too long, or not text.
                        (tooLong, message.Position) = (false, 0);
                        message.SetLength(0);
                        continue;
                    }

                    return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                }
            }
            catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException
                or IOException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                throw Translate(exception, _lease);
            }
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            _stopping.Dispose();
            _socket.Abort();
            _socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
