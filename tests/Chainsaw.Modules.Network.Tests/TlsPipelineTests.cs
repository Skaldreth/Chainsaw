using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network.Tests;

/// <summary>
/// https through the real pipeline: the server's certificate chain is checked without fetching anything, since the
/// operating system would fetch a missing intermediate or a revocation list straight from the computer's network.
/// </summary>
public sealed class TlsPipelineTests
{
    [Fact]
    public async Task A_missing_intermediate_is_not_fetched_around_the_exit()
    {
        // The certificate's AIA address points here: a connection to it would be a request around the exit.
        using var aia = new TcpListener(IPAddress.Loopback, 0);
        aia.Start();
        var aiaPort = ((IPEndPoint)aia.LocalEndpoint).Port;
        using var leaf = CreateLeafWithAia($"http://127.0.0.1:{aiaPort}/intermediate.cer");
        using var socks = new TcpListener(IPAddress.Loopback, 0);
        socks.Start();
        var served = Task.Run(() => ServeTlsThroughSocksAsync(socks, leaf));

        var options = new NetworkOptions { RequestTimeout = TimeSpan.FromSeconds(5) };
        var harness = new NetworkHarness(options, new SocksHandlerFactory(options));
        harness.Connector.Proxy = (IPEndPoint)socks.LocalEndpoint;
        await harness.ConnectedExitAsync();

        var exception = await Assert.ThrowsAsync<ExitException>(
            () => harness.Http.SendAsync(ExitRequest.Get(new Uri("https://chain.example/"))));
        var handshakeReachedProxy = await served.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ExitError.RequestFailed, exception.Error);
        Assert.True(handshakeReachedProxy, "The TLS handshake did not reach the proxy.");
        await Task.Delay(200);
        Assert.False(aia.Pending(), "The certificate's AIA address was fetched around the exit.");
        harness.Connection.Dispose();
    }

    /// <summary>Creates a root, an intermediate and a leaf for chain.example whose AIA names a URL.</summary>
    private static X509Certificate2 CreateLeafWithAia(string aiaUrl)
    {
        var now = DateTimeOffset.UtcNow;
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest(
            "CN=Test Root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var root = rootRequest.CreateSelfSigned(now.AddDays(-1), now.AddDays(30));

        using var intermediateKey = RSA.Create(2048);
        var intermediateRequest = new CertificateRequest(
            "CN=Test Intermediate", intermediateKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        intermediateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var intermediatePublic = intermediateRequest.Create(root, now.AddDays(-1), now.AddDays(29), [1, 2, 3, 4]);
        using var intermediate = intermediatePublic.CopyWithPrivateKey(intermediateKey);

        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest(
            "CN=chain.example", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("chain.example");
        leafRequest.CertificateExtensions.Add(names.Build());
        leafRequest.CertificateExtensions.Add(new X509AuthorityInformationAccessExtension(null, [aiaUrl]));
        using var leafPublic = leafRequest.Create(intermediate, now.AddDays(-1), now.AddDays(28), [5, 6, 7, 8]);
        using var leaf = leafPublic.CopyWithPrivateKey(leafKey);
        return X509CertificateLoader.LoadPkcs12(leaf.Export(X509ContentType.Pfx), null);
    }

    /// <summary>
    /// Accepts one SOCKS5 client with any credentials and ends TLS itself with the leaf alone, built offline so that
    /// the test server does not fetch the AIA address either.
    /// </summary>
    /// <returns><see langword="true"/> when a TLS handshake was started through the proxy.</returns>
    private static async Task<bool> ServeTlsThroughSocksAsync(TcpListener socks, X509Certificate2 leaf)
    {
        using var client = await socks.AcceptTcpClientAsync();
        var stream = client.GetStream();
        var buffer = new byte[512];
        await stream.ReadExactlyAsync(buffer.AsMemory(0, 2));
        await stream.ReadExactlyAsync(buffer.AsMemory(0, buffer[1]));
        await stream.WriteAsync(new byte[] { 5, 2 });
        await stream.ReadExactlyAsync(buffer.AsMemory(0, 2));
        await stream.ReadExactlyAsync(buffer.AsMemory(0, buffer[1]));
        await stream.ReadExactlyAsync(buffer.AsMemory(0, 1));
        await stream.ReadExactlyAsync(buffer.AsMemory(0, buffer[0]));
        await stream.WriteAsync(new byte[] { 1, 0 });
        await stream.ReadExactlyAsync(buffer.AsMemory(0, 5));
        await stream.ReadExactlyAsync(buffer.AsMemory(0, buffer[4] + 2));
        await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 0, 0, 0, 0, 0, 0 });
        await using var tls = new SslStream(stream);
        try
        {
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificateContext = SslStreamCertificateContext.Create(leaf, null, offline: true),
            });
        }
        catch (Exception exception) when (exception is IOException or AuthenticationException)
        {
            // The client refuses the chain, as it should.
        }

        return true;
    }
}
