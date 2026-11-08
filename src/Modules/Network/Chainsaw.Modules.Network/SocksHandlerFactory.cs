using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Builds the pipeline for a connected exit: a <see cref="SocketsHttpHandler"/> whose only proxy is the tunnel's
/// SOCKS5 proxy and whose only socket can go to that proxy. Even if the proxy setting were somehow bypassed, the
/// handler cannot open a connection to anything else, so there is no way out around the exit.
/// </summary>
/// <param name="options">The limits on requests.</param>
internal sealed class SocksHandlerFactory(NetworkOptions options) : IExitHandlerFactory
{
    /// <summary>The size of the nonce sent to the helper before SOCKS5.</summary>
    internal const int ProofNonceSize = 32;

    /// <summary>The size of the helper's proof.</summary>
    private const int ProofSize = 32;

    /// <summary>The domain in front of the nonce in the proof, the same as the helper's.</summary>
    private static ReadOnlySpan<byte> ProofDomain => "chainsaw/tunnel/proof/v1\n"u8;

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
            ConnectCallback = (context, cancellationToken) => ConnectOnlyToProxyAsync(
                socks5Proxy, context.DnsEndPoint, new TunnelCheck(proxyIsTunnel, credentials), cancellationToken),
        };
    }

    /// <summary>
    /// Builds the server certificate's chain without fetching anything: a missing intermediate (from the certificate's
    /// AIA address) or a revocation list would otherwise be fetched by the operating system straight from the
    /// computer's network, around the exit. The intermediates the computer already has (Windows keeps those it has met
    /// in its store of intermediate authorities) are offered, so that a server that leaves out an intermediate the
    /// computer knows is still reached; one that leaves out an intermediate the computer has never seen fails.
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
        var disallowed = DistrustedThumbprints();
        policy.CustomTrustStore.AddRange(Allowed(StoreName.Root, disallowed));

        // Only offered to build a chain with: an intermediate is never trusted by itself, it must lead to a root.
        policy.ExtraStore.AddRange(Allowed(StoreName.CertificateAuthority, disallowed));
        return policy;
    }

    /// <summary>
    /// Reads a store for the user and the computer, without the certificates Windows distrusts.
    /// </summary>
    /// <param name="name">The store: the trusted roots, or the intermediate authorities.</param>
    /// <param name="disallowed">The thumbprints of the distrusted certificates.</param>
    /// <returns>The certificates.</returns>
    /// <remarks>
    /// Every certificate read and not kept is disposed at once: the stores are read for each new pipeline, also for
    /// each probe of the internet, so their handles must not wait for the garbage collector.
    /// </remarks>
    private static X509Certificate2Collection Allowed(StoreName name, IReadOnlySet<string> disallowed)
    {
        var kept = new X509Certificate2Collection();
        foreach (var certificate in ReadStores(name))
        {
            if (disallowed.Contains(certificate.Thumbprint))
            {
                certificate.Dispose();
            }
            else
            {
                kept.Add(certificate);
            }
        }

        return kept;
    }

    /// <summary>Reads the thumbprints of the certificates Windows distrusts, and lets go of the certificates.</summary>
    /// <returns>The thumbprints.</returns>
    private static HashSet<string> DistrustedThumbprints()
    {
        var distrusted = ReadStores(StoreName.Disallowed);
        var thumbprints = distrusted
            .Select(certificate => certificate.Thumbprint)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dispose(distrusted);
        return thumbprints;
    }

    /// <summary>Disposes every certificate of a collection.</summary>
    /// <param name="certificates">The certificates.</param>
    private static void Dispose(X509Certificate2Collection certificates)
    {
        foreach (var certificate in certificates)
        {
            certificate.Dispose();
        }
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
    /// made, and the proxy is reached by its address, so no name is resolved on the computer. Before the stream is
    /// handed to SOCKS5, the helper must prove that it knows the tunnel's password (<see cref="ProveAsync"/>).
    /// </summary>
    /// <param name="socks5Proxy">The tunnel's proxy.</param>
    /// <param name="requested">The endpoint the handler asks for.</param>
    /// <param name="check">Tells whether the port still belongs to the tunnel, and its credentials for the proof.</param>
    /// <param name="cancellationToken">Stops the connection.</param>
    /// <returns>The stream to the proxy.</returns>
    /// <exception cref="HttpRequestException">
    /// The endpoint is not the tunnel's proxy, the tunnel no longer holds the port, or what answers on it cannot prove
    /// that it is the helper.
    /// </exception>
    internal static async ValueTask<Stream> ConnectOnlyToProxyAsync(
        IPEndPoint socks5Proxy, DnsEndPoint requested, TunnelCheck check, CancellationToken cancellationToken)
    {
        if (!IsProxy(socks5Proxy, requested))
        {
            throw new HttpRequestException("The exit's pipeline only connects to the tunnel's proxy.");
        }

        var socket = new Socket(socks5Proxy.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(socks5Proxy, cancellationToken).ConfigureAwait(false);

            // Asked after the connection, before anything is sent: the helper holds its port exclusively and never
            // frees it before it ends, so a helper that still lives now has held the port all along. The moment a
            // crashing helper's handles close before Windows marks it exited is closed by the proof below.
            if (!check.ProxyIsTunnel())
            {
                throw new HttpRequestException("The tunnel no longer holds its proxy's port.");
            }

            var stream = new NetworkStream(socket, ownsSocket: true);
            await ProveAsync(stream, check.Credentials, cancellationToken).ConfigureAwait(false);
            return stream;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Asks what answers on the proxy's port to prove that it is the helper: a fresh random nonce goes out, and the
    /// answer must be HMAC-SHA256 under the tunnel's password over a fixed domain and the nonce (see <c>prove</c> in
    /// <c>tools/Chainsaw.Tunnel/socks.go</c>). Nothing of SOCKS5, the credentials least of all, is sent before it
    /// holds, so a program that has taken the port after the helper ended learns nothing.
    /// </summary>
    /// <param name="stream">The connection to the proxy.</param>
    /// <param name="credentials">The tunnel's credentials.</param>
    /// <param name="cancellationToken">Stops the proof.</param>
    /// <exception cref="HttpRequestException">The proof does not hold or does not come.</exception>
    internal static async Task ProveAsync(
        Stream stream, NetworkCredential credentials, CancellationToken cancellationToken)
    {
        var nonce = RandomNumberGenerator.GetBytes(ProofNonceSize);
        var answer = new byte[ProofSize];
        try
        {
            await stream.WriteAsync(nonce, cancellationToken).ConfigureAwait(false);
            await stream.ReadExactlyAsync(answer, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException)
        {
            throw new HttpRequestException("The tunnel's proxy did not prove that it is the helper.", exception);
        }

        if (!CryptographicOperations.FixedTimeEquals(answer, ProofFor(credentials, nonce)))
        {
            throw new HttpRequestException("The tunnel's proxy did not prove that it is the helper.");
        }
    }

    /// <summary>Computes the helper's proof of a nonce under the tunnel's password.</summary>
    /// <param name="credentials">The tunnel's credentials.</param>
    /// <param name="nonce">The nonce.</param>
    /// <returns>The proof.</returns>
    internal static byte[] ProofFor(NetworkCredential credentials, ReadOnlySpan<byte> nonce)
    {
        var password = Encoding.UTF8.GetBytes(credentials.Password);
        try
        {
            using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, password);
            hmac.AppendData(ProofDomain);
            hmac.AppendData(nonce);
            return hmac.GetHashAndReset();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
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
