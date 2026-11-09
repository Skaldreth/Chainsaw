using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network.Tests;

/// <summary>
/// The own node's pinned certificate (sketch KL3b): with a pin the node must show exactly that certificate, a
/// self-signed one included, and any other is refused before a byte of the request is sent; without a pin the roots on
/// the computer decide as before; the certificate can be read for the user without sending anything; and a pin is
/// never taken through the way out. Against a real TLS node on loopback, never the network.
/// </summary>
public sealed class CertificatePinTests
{
    private readonly NetworkOptions _options = new() { RequestTimeout = TimeSpan.FromSeconds(10) };

    [Fact]
    public async Task The_pinned_self_signed_certificate_is_accepted()
    {
        using var node = new TlsNode();
        using var http = new LocalHttp(new LocalHandlerFactory(_options), _options, new FakeClock());

        var response = await http.SendAsync(
            ExitRequest.Get(node.Url("blocks/tip/height")) with { CertificatePin = node.Fingerprint });

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("ok", Encoding.ASCII.GetString(response.Body.Span));
        Assert.True(response.IsLocal);
    }

    [Fact]
    public async Task Another_certificate_than_the_pinned_one_is_refused_and_nothing_is_sent()
    {
        using var node = new TlsNode();
        using var other = TlsNode.CreateCertificate();
        using var http = new LocalHttp(new LocalHandlerFactory(_options), _options, new FakeClock());

        var refused = await Assert.ThrowsAsync<ExitException>(() => http.SendAsync(
            ExitRequest.Get(node.Url("blocks/tip/height")) with { CertificatePin = CertificatePin.Of(other.RawData) }));

        Assert.Equal(ExitError.CertificateChanged, refused.Error);
        await Task.Delay(200);
        Assert.Empty(node.Requests);
    }

    [Fact]
    public async Task Without_a_pin_a_certificate_Windows_does_not_trust_is_refused_as_before()
    {
        using var node = new TlsNode();
        using var http = new LocalHttp(new LocalHandlerFactory(_options), _options, new FakeClock());

        var refused = await Assert.ThrowsAsync<ExitException>(
            () => http.SendAsync(ExitRequest.Get(node.Url("blocks/tip/height"))));

        Assert.Equal(ExitError.RequestFailed, refused.Error);
        Assert.Empty(node.Requests);
    }

    [Fact]
    public async Task A_pin_never_loosens_the_check_of_a_request_without_one()
    {
        using var node = new TlsNode();
        using var http = new LocalHttp(new LocalHandlerFactory(_options), _options, new FakeClock());
        await http.SendAsync(ExitRequest.Get(node.Url("a")) with { CertificatePin = node.Fingerprint });

        var refused = await Assert.ThrowsAsync<ExitException>(() => http.SendAsync(ExitRequest.Get(node.Url("b"))));

        Assert.Equal(ExitError.RequestFailed, refused.Error);
    }

    [Fact]
    public async Task The_certificate_is_read_for_the_user_without_sending_a_request()
    {
        using var node = new TlsNode();
        using var http = new LocalHttp(new LocalHandlerFactory(_options), _options, new FakeClock());

        var shown = await http.ReadCertificateAsync(node.Url(string.Empty));

        Assert.Equal(node.Fingerprint, shown.Fingerprint);
        Assert.False(shown.TrustedByWindows);
        Assert.True(shown.SelfSigned);
        Assert.Equal("CN=esplora.lan", shown.Subject);
        Assert.True(shown.NotAfter > shown.NotBefore);
        await Task.Delay(200);
        Assert.Empty(node.Requests);
    }

    [Fact]
    public async Task Reading_the_certificate_keeps_to_the_own_network()
    {
        using var http = new LocalHttp(new RecordingLocalHandlerFactory(), _options, new FakeClock());

        await Assert.ThrowsAsync<ArgumentException>(() => http.ReadCertificateAsync(new Uri("https://8.8.8.8/")));
        await Assert.ThrowsAsync<ArgumentException>(() => http.ReadCertificateAsync(new Uri("http://192.168.1.5/")));
        var refused = await Assert.ThrowsAsync<ExitException>(() => new LocalHandlerFactory(_options)
            .ReadCertificateAsync(new DnsEndPoint("93.184.216.34", 443), default));
        Assert.Equal(ExitError.NotLocal, refused.Error);
    }

    [Fact]
    public async Task Each_pin_has_its_own_pipeline()
    {
        var handlers = new RecordingLocalHandlerFactory();
        using var http = new LocalHttp(handlers, _options, new FakeClock());
        var pin = new string('A', CertificatePin.Length);

        await http.SendAsync(ExitRequest.Get(new Uri("https://192.168.1.20/a")));
        await http.SendAsync(ExitRequest.Get(new Uri("https://192.168.1.20/b")) with { CertificatePin = pin });
        await http.SendAsync(ExitRequest.Get(new Uri("https://192.168.1.20/c")) with { CertificatePin = pin });

        Assert.Equal([null, pin], handlers.Pins);
    }

    [Fact]
    public async Task A_pin_through_the_way_out_or_one_that_is_not_a_fingerprint_is_refused()
    {
        var harness = new NetworkHarness();
        await harness.ConnectedExitAsync();
        var pin = new string('A', CertificatePin.Length);
        using var http = new LocalHttp(new RecordingLocalHandlerFactory(), _options, new FakeClock());

        await Assert.ThrowsAsync<ArgumentException>(() => harness.Http.SendAsync(
            ExitRequest.Get(new Uri("https://chain.example/")) with { CertificatePin = pin }));
        await Assert.ThrowsAsync<ArgumentException>(() => http.SendAsync(
            ExitRequest.Get(new Uri("https://192.168.1.20/")) with { CertificatePin = "abc" }));
        await Assert.ThrowsAsync<ArgumentException>(() => http.SendAsync(
            ExitRequest.Get(new Uri("https://192.168.1.20/")) with { CertificatePin = pin.ToLowerInvariant() }));
    }

    [Fact]
    public void The_fingerprint_is_the_SHA_256_of_the_whole_certificate_in_upper_case_hex()
    {
        using var certificate = TlsNode.CreateCertificate();

        var fingerprint = CertificatePin.Of(certificate.RawData);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(certificate.RawData)), fingerprint);
        Assert.True(CertificatePin.IsValid(fingerprint));
        Assert.Equal(32, CertificatePin.Bytes(fingerprint).Length);
        Assert.Equal(95, CertificatePin.Grouped(fingerprint).Length);
        Assert.Equal($"{fingerprint[..2]}:{fingerprint[2..4]} … {fingerprint[60..62]}:{fingerprint[62..]}",
            CertificatePin.Short(fingerprint));
        Assert.False(CertificatePin.IsValid(fingerprint[1..]));
        Assert.Throws<ArgumentException>(() => CertificatePin.Bytes("00"));
    }

    /// <summary>
    /// A node on loopback that ends TLS with a self-signed certificate for esplora.lan and answers <c>ok</c>, recording
    /// every request it reads after the handshake.
    /// </summary>
    private sealed class TlsNode : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        private readonly CancellationTokenSource _stop = new();

        private readonly X509Certificate2 _certificate = CreateCertificate();

        public TlsNode()
        {
            _listener.Start();
            Fingerprint = CertificatePin.Of(_certificate.RawData);
            _ = ServeAsync();
        }

        public string Fingerprint { get; }

        public List<string> Requests { get; } = [];

        public static X509Certificate2 CreateCertificate()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=esplora.lan", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("esplora.lan");
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            var now = DateTimeOffset.UtcNow;
            using var created = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(30));
            return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
        }

        public Uri Url(string path) =>
            new($"https://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/{path}");

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _certificate.Dispose();
            _stop.Dispose();
        }

        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _ = ServeOneAsync(client);
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException
                                              or ObjectDisposedException)
            {
                // Stopped.
            }
        }

        private async Task ServeOneAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    await using var tls = new SslStream(client.GetStream());
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificateContext = SslStreamCertificateContext.Create(
                            _certificate, null, offline: true),
                    }, _stop.Token);
                    var buffer = new byte[4096];
                    var read = await tls.ReadAsync(buffer, _stop.Token);
                    if (read == 0)
                    {
                        return;
                    }

                    lock (Requests)
                    {
                        Requests.Add(Encoding.ASCII.GetString(buffer, 0, read));
                    }

                    await tls.WriteAsync(Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"), _stop.Token);
                }
                catch (Exception exception) when (exception is IOException or AuthenticationException
                                                  or OperationCanceledException or ObjectDisposedException)
                {
                    // The client refused the certificate or went away, as it may.
                }
            }
        }
    }
}
