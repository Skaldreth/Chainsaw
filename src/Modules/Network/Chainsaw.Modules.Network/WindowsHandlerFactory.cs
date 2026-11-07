using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Builds the pipeline for the way out through Windows (decided 2026-11-07): the computer's own network, through the
/// proxy Windows is set up with, so that the protection installed for the whole computer sees and carries the lookup.
/// Everything else is as through an exit: no cookies, no redirects, no credentials, no trace headers, no resumed TLS
/// sessions, HTTP/1.1, and a certificate chain built without fetching anything around the request.
/// </summary>
/// <param name="options">The limits on requests.</param>
internal sealed class WindowsHandlerFactory(NetworkOptions options) : IWindowsHandlerFactory
{
    /// <inheritdoc />
    public HttpMessageHandler Create() =>
        new SocketsHttpHandler
        {
            // No proxy of its own: HttpClient.DefaultProxy, which on Windows is the system's proxy (also from a PAC
            // file or automatic detection), or none. Its credentials are never sent on their own (see DEV-LOG, etapp
            // 19): a proxy that asks for them answers 407, which is shown honestly.
            UseProxy = true,
            Proxy = null,
            DefaultProxyCredentials = null,
            Credentials = null,
            PreAuthenticate = false,
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = options.ConnectTimeout,

            // No trace headers (traceparent, baggage), even when diagnostics listen: they could link lookups.
            ActivityHeadersPropagator = null,
            SslOptions = new SslClientAuthenticationOptions
            {
                AllowTlsResume = false,
                CertificateChainPolicy = SocksHandlerFactory.OfflineChainPolicy(),
            },
            ConnectCallback = (context, cancellationToken) => ConnectAsync(
                context.DnsEndPoint, context.InitialRequestMessage.RequestUri, cancellationToken),
        };

    /// <summary>
    /// Opens a socket. A connection to the source itself (no proxy) resolves the name here and refuses every address
    /// that is not global, as the tunnel does, so that a name that points into the computer or the local network is
    /// never reached. A connection to the system's proxy, which may well be on the computer or the local network, is
    /// made as Windows says.
    /// </summary>
    /// <param name="endpoint">Where the handler connects: the source, or the proxy.</param>
    /// <param name="requested">The address the request is for.</param>
    /// <param name="cancellationToken">Stops the connection.</param>
    /// <returns>The stream.</returns>
    /// <exception cref="HttpRequestException">
    /// The source's name points only to addresses that are not global.
    /// </exception>
    internal static async ValueTask<Stream> ConnectAsync(
        DnsEndPoint endpoint, Uri? requested, CancellationToken cancellationToken)
    {
        var addresses = IsSource(endpoint, requested)
            ? GlobalOnly(await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken).ConfigureAwait(false))
            : null;
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            if (addresses is null)
            {
                await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await socket.ConnectAsync(addresses, endpoint.Port, cancellationToken).ConfigureAwait(false);
            }

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Tells whether the handler connects to the source itself rather than to a proxy.</summary>
    /// <param name="endpoint">Where the handler connects.</param>
    /// <param name="requested">The address the request is for.</param>
    /// <returns><see langword="true"/> when the host and port are the request's own.</returns>
    internal static bool IsSource(DnsEndPoint endpoint, Uri? requested) =>
        requested is not null
        && endpoint.Port == requested.Port
        && string.Equals(
            endpoint.Host.Trim('[', ']'), requested.IdnHost.Trim('[', ']'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Keeps the global addresses of a name.</summary>
    /// <param name="addresses">What the name resolved to.</param>
    /// <returns>The global addresses.</returns>
    /// <exception cref="HttpRequestException">None is global.</exception>
    internal static IPAddress[] GlobalOnly(IPAddress[] addresses)
    {
        var global = addresses.Where(address => !LocalTargets.IsLocal(address)).ToArray();
        return global.Length > 0
            ? global
            : throw new HttpRequestException("The source's name points only to the computer or a private network.");
    }
}
