using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

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
    public HttpMessageHandler Create(IPEndPoint socks5Proxy, NetworkCredential credentials, Func<bool> proxyIsTunnel)
    {
        ArgumentNullException.ThrowIfNull(socks5Proxy);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(proxyIsTunnel);
        return new SocketsHttpHandler
        {
            UseProxy = true,
            Proxy = new ExitProxy(socks5Proxy, credentials),
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
                // No resumed TLS sessions, which could show a source that two exits are the same client.
                AllowTlsResume = false,
                CertificateChainPolicy = OfflineChainPolicy(),
            },
            ConnectCallback = (context, cancellationToken) =>
                ConnectOnlyToProxyAsync(socks5Proxy, context.DnsEndPoint, proxyIsTunnel, cancellationToken),
        };
    }

    /// <summary>
    /// Builds the server certificate's chain without fetching anything: a missing intermediate (from the certificate's
    /// AIA address) or a revocation list would otherwise be fetched by the operating system straight from the
    /// computer's network, around the exit. A server that sends an incomplete chain therefore fails.
    /// </summary>
    /// <returns>The policy, new for each pipeline since a policy is mutable.</returns>
    internal static X509ChainPolicy OfflineChainPolicy()
    {
        var policy = new X509ChainPolicy
        {
            DisableCertificateDownloads = true,

            // Set explicitly: a policy's default is Online, which would fetch revocation lists around the exit.
            RevocationMode = X509RevocationMode.NoCheck,

            // Only the roots already on the computer: with the system's own trust, Windows fetches a missing root of
            // its root programme from Windows Update, straight from the computer's network.
            TrustMode = X509ChainTrustMode.CustomRootTrust,
        };
        policy.CustomTrustStore.AddRange(InstalledRoots());
        return policy;
    }

    /// <summary>
    /// Reads the trusted roots installed for the user and the computer, without the ones Windows distrusts.
    /// </summary>
    /// <returns>The roots.</returns>
    private static X509Certificate2Collection InstalledRoots()
    {
        var disallowed = ReadStores(StoreName.Disallowed)
            .Select(certificate => certificate.Thumbprint)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roots = new X509Certificate2Collection();
        roots.AddRange(ReadStores(StoreName.Root)
            .Where(root => !disallowed.Contains(root.Thumbprint))
            .ToArray());
        return roots;
    }

    /// <summary>Reads a certificate store for the user and for the computer, as it is now.</summary>
    /// <param name="name">The store.</param>
    /// <returns>The certificates; a store that cannot be opened adds none.</returns>
    private static X509Certificate2Collection ReadStores(StoreName name)
    {
        var certificates = new X509Certificate2Collection();
        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            try
            {
                using var store = new X509Store(name, location);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                certificates.AddRange(store.Certificates);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // A store that does not exist or cannot be read adds nothing; chains to missing roots then fail.
            }
        }

        return certificates;
    }

    /// <summary>
    /// Opens the handler's socket, but only to the tunnel's proxy. Any other endpoint is refused before a socket is
    /// made, and the proxy is reached by its address, so no name is resolved on the computer.
    /// </summary>
    /// <param name="socks5Proxy">The tunnel's proxy.</param>
    /// <param name="requested">The endpoint the handler asks for.</param>
    /// <param name="proxyIsTunnel">Tells, once connected, whether the port still belongs to the tunnel.</param>
    /// <param name="cancellationToken">Stops the connection.</param>
    /// <returns>The stream to the proxy.</returns>
    /// <exception cref="HttpRequestException">
    /// The endpoint is not the tunnel's proxy, or the tunnel no longer holds the port.
    /// </exception>
    internal static async ValueTask<Stream> ConnectOnlyToProxyAsync(
        IPEndPoint socks5Proxy, DnsEndPoint requested, Func<bool> proxyIsTunnel, CancellationToken cancellationToken)
    {
        if (!IsProxy(socks5Proxy, requested))
        {
            throw new HttpRequestException("The exit's pipeline only connects to the tunnel's proxy.");
        }

        var socket = new Socket(socks5Proxy.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(socks5Proxy, cancellationToken).ConfigureAwait(false);

            // Asked after the connection: a tunnel that still holds its port now has held it all along, so nothing
            // else can have answered. Nothing has been sent yet, not even the credentials.
            if (!proxyIsTunnel())
            {
                throw new HttpRequestException("The tunnel no longer holds its proxy's port.");
            }

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
