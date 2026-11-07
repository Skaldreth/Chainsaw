using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Builds the pipeline to the user's own node (decided 2026-11-07): straight to the node in the user's own network,
/// never through a proxy, and only to addresses that are not global, so that nothing through it can leave the computer
/// for the internet. Everything else is as through an exit: no cookies, no redirects, no credentials, no trace
/// headers, no resumed TLS sessions, HTTP/1.1, and a certificate chain built from the roots installed on the computer
/// without fetching anything.
/// </summary>
/// <param name="options">The limits on requests.</param>
internal sealed class LocalHandlerFactory(NetworkOptions options) : ILocalHandlerFactory
{
    /// <inheritdoc />
    public HttpMessageHandler Create() =>
        new SocketsHttpHandler
        {
            // No proxy at all, not even the system's: the request stays in the user's own network.
            UseProxy = false,
            Proxy = null,
            DefaultProxyCredentials = null,
            Credentials = null,
            PreAuthenticate = false,
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = options.ConnectTimeout,
            ActivityHeadersPropagator = null,
            SslOptions = new SslClientAuthenticationOptions
            {
                AllowTlsResume = false,
                CertificateChainPolicy = SocksHandlerFactory.OfflineChainPolicy(),
            },
            ConnectCallback = (context, cancellationToken) => ConnectAsync(context.DnsEndPoint, cancellationToken),
        };

    /// <summary>
    /// Opens a socket to the node: the name is resolved here and only the addresses that are not global are tried, so
    /// that a name that points to the internet is never reached directly.
    /// </summary>
    /// <param name="endpoint">The node's host and port.</param>
    /// <param name="cancellationToken">Stops the connection.</param>
    /// <returns>The stream.</returns>
    /// <exception cref="ExitException"><see cref="ExitError.NotLocal"/>: the name points only to global addresses.</exception>
    internal static async ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, CancellationToken cancellationToken)
    {
        var addresses = LocalOnly(
            await Dns.GetHostAddressesAsync(endpoint.Host.Trim('[', ']'), cancellationToken).ConfigureAwait(false));
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, endpoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Keeps the addresses of a name that are on the computer or a private network.</summary>
    /// <param name="addresses">What the name resolved to.</param>
    /// <returns>The addresses that are not global.</returns>
    /// <exception cref="ExitException"><see cref="ExitError.NotLocal"/>: none is.</exception>
    internal static IPAddress[] LocalOnly(IPAddress[] addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var local = addresses.Where(LocalAddresses.IsLocal).ToArray();
        return local.Length > 0 ? local : throw new ExitException(ExitError.NotLocal);
    }
}
