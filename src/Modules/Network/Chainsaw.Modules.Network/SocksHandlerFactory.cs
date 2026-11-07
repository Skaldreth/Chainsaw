using System.Net;
using System.Net.Sockets;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Builds the pipeline for a connected exit: a <see cref="SocketsHttpHandler"/> whose only proxy is the tunnel's
/// SOCKS5 proxy and whose only socket can go to that proxy. Even if the proxy setting were somehow bypassed, the
/// handler cannot open a connection to anything else, so there is no way out around the exit.
/// </summary>
/// <param name="options">The limits on requests.</param>
internal sealed class SocksHandlerFactory(NetworkOptions options) : IExitHandlerFactory
{
    /// <inheritdoc />
    public HttpMessageHandler Create(IPEndPoint socks5Proxy)
    {
        ArgumentNullException.ThrowIfNull(socks5Proxy);
        return new SocketsHttpHandler
        {
            UseProxy = true,
            Proxy = new ExitProxy(socks5Proxy),
            DefaultProxyCredentials = null,
            Credentials = null,
            PreAuthenticate = false,
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = options.ConnectTimeout,
            ConnectCallback = (context, cancellationToken) =>
                ConnectOnlyToProxyAsync(socks5Proxy, context.DnsEndPoint, cancellationToken),
        };
    }

    /// <summary>
    /// Opens the handler's socket, but only to the tunnel's proxy. Any other endpoint is refused before a socket is
    /// made, and the proxy is reached by its address, so no name is resolved on the computer.
    /// </summary>
    /// <param name="socks5Proxy">The tunnel's proxy.</param>
    /// <param name="requested">The endpoint the handler asks for.</param>
    /// <param name="cancellationToken">Stops the connection.</param>
    /// <returns>The stream to the proxy.</returns>
    /// <exception cref="HttpRequestException">The endpoint is not the tunnel's proxy.</exception>
    internal static async ValueTask<Stream> ConnectOnlyToProxyAsync(
        IPEndPoint socks5Proxy, DnsEndPoint requested, CancellationToken cancellationToken)
    {
        if (!IsProxy(socks5Proxy, requested))
        {
            throw new HttpRequestException("The exit's pipeline only connects to the tunnel's proxy.");
        }

        var socket = new Socket(socks5Proxy.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(socks5Proxy, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Tells whether an endpoint the handler asks for is the tunnel's proxy.</summary>
    /// <param name="socks5Proxy">The tunnel's proxy.</param>
    /// <param name="requested">The endpoint asked for.</param>
    /// <returns><see langword="true"/> when the host is the proxy's address and the port its port.</returns>
    internal static bool IsProxy(IPEndPoint socks5Proxy, DnsEndPoint requested) =>
        requested.Port == socks5Proxy.Port
        && IPAddress.TryParse(requested.Host.Trim('[', ']'), out var address)
        && address.Equals(socks5Proxy.Address);
}
