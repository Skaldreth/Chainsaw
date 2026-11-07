using System.Net;
using System.Net.Sockets;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network.Tests;

/// <summary>
/// The real pipeline (<see cref="SocksHandlerFactory"/>) against a SOCKS5 proxy on the loopback address: what it
/// sends, and that it reaches nothing but the proxy.
/// </summary>
public sealed class SocksPipelineTests : IAsyncLifetime
{
    private static readonly Uri Address = new("http://chain.example/api/tx/abc");

    private readonly FakeSocksServer _proxy = new();
    private NetworkHarness _harness = null!;

    public async Task InitializeAsync()
    {
        var options = new NetworkOptions { AllowPlainHttp = true, RequestTimeout = TimeSpan.FromSeconds(10) };
        _harness = new NetworkHarness(options, new SocksHandlerFactory(options));
        _harness.Connector.Proxy = _proxy.EndPoint;
        await _harness.ConnectedExitAsync();
    }

    public async Task DisposeAsync()
    {
        _harness.Connection.Dispose();
        await _proxy.DisposeAsync();
    }

    [Fact]
    public async Task The_request_goes_through_the_proxy_with_the_name_resolved_at_the_exit()
    {
        var response = await _harness.Http.SendAsync(ExitRequest.Get(Address));

        Assert.Equal("{}", System.Text.Encoding.ASCII.GetString(response.Body.Span));
        var target = Assert.Single(_proxy.Targets);

        // Address type 3 is a name: the computer did not look the name up in its own DNS.
        Assert.Equal((3, "chain.example", 80), target);
    }

    [Fact]
    public async Task Only_the_address_host_and_a_neutral_user_agent_are_sent()
    {
        await _harness.Http.SendAsync(ExitRequest.Get(Address));

        var lines = Assert.Single(_proxy.Requests).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("GET /api/tx/abc HTTP/1.1", lines[0]);
        Assert.Equal(["Host: chain.example", "User-Agent: Mozilla/5.0"], lines[1..].Order());
    }

    [Fact]
    public async Task A_cookie_from_a_response_is_never_sent_back()
    {
        _proxy.Response = _ => "HTTP/1.1 200 OK\r\nSet-Cookie: tracking=abc123; Path=/\r\n"
            + "Content-Length: 0\r\nConnection: close\r\n\r\n";

        await _harness.Http.SendAsync(ExitRequest.Get(Address));
        await _harness.Http.SendAsync(ExitRequest.Get(Address));

        Assert.Equal(2, _proxy.Requests.Count);
        Assert.DoesNotContain("Cookie", _proxy.Requests[1], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Referer", _proxy.Requests[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_redirect_is_returned_and_not_followed()
    {
        _proxy.Response = _ => "HTTP/1.1 302 Found\r\nLocation: http://elsewhere.example/\r\n"
            + "Content-Length: 0\r\nConnection: close\r\n\r\n";

        var response = await _harness.Http.SendAsync(ExitRequest.Get(Address));

        Assert.Equal(302, response.StatusCode);
        Assert.Single(_proxy.Targets);
    }

    [Fact]
    public async Task A_compressed_body_is_kept_as_it_came()
    {
        _proxy.Response = _ => "HTTP/1.1 200 OK\r\nContent-Encoding: gzip\r\nContent-Length: 3\r\n"
            + "Connection: close\r\n\r\nabc";

        var response = await _harness.Http.SendAsync(ExitRequest.Get(Address));

        Assert.Equal("abc"u8.ToArray(), response.Body.ToArray());
        Assert.DoesNotContain("Accept-Encoding", _proxy.Requests[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task When_the_proxy_is_gone_the_request_fails_and_does_not_go_around_it()
    {
        // A local server that the request names directly: if the pipeline went around the dead proxy, it would land here.
        using var direct = new TcpListener(IPAddress.Loopback, 0);
        direct.Start();
        var directPort = ((IPEndPoint)direct.LocalEndpoint).Port;
        await _proxy.DisposeAsync();

        var exception = await Assert.ThrowsAsync<ExitException>(() =>
            _harness.Http.SendAsync(ExitRequest.Get(new Uri($"http://127.0.0.1:{directPort}/"))));

        Assert.Equal(ExitError.RequestFailed, exception.Error);
        Assert.False(direct.Pending());
    }

    [Fact]
    public async Task A_loopback_address_is_also_sent_to_the_proxy()
    {
        await _harness.Http.SendAsync(ExitRequest.Get(new Uri("http://127.0.0.1:8080/x")));

        Assert.Equal((1, "127.0.0.1", 8080), Assert.Single(_proxy.Targets));
    }

    [Theory]
    [InlineData("127.0.0.1", 1080, true)]
    [InlineData("[::1]", 1080, false)]
    [InlineData("127.0.0.1", 1081, false)]
    [InlineData("chain.example", 1080, false)]
    [InlineData("localhost", 1080, false)]
    [InlineData("10.0.0.1", 1080, false)]
    public void The_pipeline_opens_sockets_only_to_the_proxy(string host, int port, bool allowed)
    {
        var proxy = new IPEndPoint(IPAddress.Loopback, 1080);

        Assert.Equal(allowed, SocksHandlerFactory.IsProxy(proxy, new DnsEndPoint(host, port)));
    }

    [Fact]
    public async Task A_socket_to_anything_but_the_proxy_is_refused_before_it_is_made()
    {
        var proxy = new IPEndPoint(IPAddress.Loopback, 1080);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await SocksHandlerFactory.ConnectOnlyToProxyAsync(
                proxy, new DnsEndPoint("chain.example", 443), CancellationToken.None));
    }

    [Fact]
    public void The_proxy_setting_never_bypasses_and_takes_no_credentials()
    {
        var proxy = new ExitProxy(new IPEndPoint(IPAddress.Loopback, 1080));

        Assert.False(proxy.IsBypassed(new Uri("http://localhost/")));
        Assert.False(proxy.IsBypassed(new Uri("http://127.0.0.1/")));
        Assert.Equal(new Uri("socks5://127.0.0.1:1080"), proxy.GetProxy(new Uri("https://chain.example/")));
        Assert.Null(proxy.Credentials);
        Assert.Throws<NotSupportedException>(() => proxy.Credentials = CredentialCache.DefaultCredentials);
    }

    [Fact]
    public void The_handler_keeps_no_cookies_follows_no_redirects_and_uses_only_the_proxy()
    {
        var options = new NetworkOptions();
        using var handler = (SocketsHttpHandler)new SocksHandlerFactory(options)
            .Create(new IPEndPoint(IPAddress.Loopback, 1080));

        Assert.True(handler.UseProxy);
        Assert.IsType<ExitProxy>(handler.Proxy);
        Assert.False(handler.UseCookies);
        Assert.False(handler.AllowAutoRedirect);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.Null(handler.Credentials);
        Assert.NotNull(handler.ConnectCallback);
    }
}
