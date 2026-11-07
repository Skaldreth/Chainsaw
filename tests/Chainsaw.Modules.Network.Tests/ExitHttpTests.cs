using System.Net;
using System.Text;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network.Tests;

public sealed class ExitHttpTests
{
    private static readonly Uri Address = new("https://chain.example/api/tx/abc");

    private readonly NetworkHarness _harness = new();

    [Fact]
    public async Task Without_an_exit_the_request_is_refused_and_nothing_is_sent()
    {
        var exception = await Assert.ThrowsAsync<ExitNotConnectedException>(
            () => _harness.Http.SendAsync(ExitRequest.Get(Address)));

        Assert.Equal(ExitError.NotConnected, exception.Error);
        Assert.Contains("Ingen utgång är ansluten", exception.Message, StringComparison.Ordinal);
        Assert.Empty(_harness.Handlers.Handlers);
    }

    [Fact]
    public async Task A_failed_exit_refuses_requests()
    {
        await _harness.SelectedExitAsync();
        _harness.Connector.Failure = new ExitConnectException(ExitFailure.ConnectFailed, "refused");
        await _harness.Connection.ConnectAsync();

        await Assert.ThrowsAsync<ExitNotConnectedException>(() => _harness.Http.SendAsync(ExitRequest.Get(Address)));

        Assert.Empty(_harness.Handlers.Handlers);
    }

    [Fact]
    public async Task After_a_disconnect_requests_are_refused_and_reach_no_pipeline()
    {
        await _harness.ConnectedExitAsync();
        await _harness.Connection.DisconnectAsync();

        await Assert.ThrowsAsync<ExitNotConnectedException>(() => _harness.Http.SendAsync(ExitRequest.Get(Address)));

        Assert.Empty(_harness.Handlers.Handlers[0].Requests);
    }

    [Fact]
    public async Task After_the_tunnel_drops_requests_are_refused()
    {
        await _harness.ConnectedExitAsync();
        _harness.Connector.Tunnels[0].Drop();

        await Assert.ThrowsAsync<ExitNotConnectedException>(() => _harness.Http.SendAsync(ExitRequest.Get(Address)));

        Assert.Empty(_harness.Handlers.Handlers[0].Requests);
    }

    [Fact]
    public async Task A_connected_exit_sends_through_its_pipeline_and_gives_the_raw_response()
    {
        var exit = await _harness.ConnectedExitAsync();
        var raw = new byte[] { 0x7B, 0x00, 0xFF, 0x7D };
        _harness.Handlers.Answer = (_, _) =>
        {
            _harness.Clock.UtcNow += TimeSpan.FromMilliseconds(250);
            var content = new ByteArrayContent(raw);
            content.Headers.ContentType = new("application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        };
        var sentAt = _harness.Clock.UtcNow;

        var response = await _harness.Http.SendAsync(ExitRequest.Get(Address));

        Assert.Equal(raw, response.Body.ToArray());
        Assert.Equal(200, response.StatusCode);
        Assert.True(response.IsSuccess);
        Assert.Equal("application/json", response.ContentType);
        Assert.Equal(exit.Id, response.ExitId);
        Assert.Equal(Address, response.Uri);
        Assert.Equal(sentAt, response.RequestedAt);
        Assert.Equal(TimeSpan.FromMilliseconds(250), response.Duration);
        Assert.Equal(Address, Assert.Single(_harness.Handlers.Handlers[0].Requests).RequestUri);
    }

    [Fact]
    public async Task A_status_that_is_not_a_success_is_still_a_response()
    {
        await _harness.ConnectedExitAsync();
        _harness.Handlers.Answer = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("Transaction not found"),
        });

        var response = await _harness.Http.SendAsync(ExitRequest.Get(Address));

        Assert.Equal(404, response.StatusCode);
        Assert.False(response.IsSuccess);
        Assert.Equal("Transaction not found", Encoding.UTF8.GetString(response.Body.Span));
    }

    [Fact]
    public async Task Only_a_neutral_user_agent_is_sent_without_cookies_or_referer()
    {
        await _harness.ConnectedExitAsync();

        await _harness.Http.SendAsync(ExitRequest.Get(Address));

        var request = Assert.Single(_harness.Handlers.Handlers[0].Requests);
        var header = Assert.Single(request.Headers.NonValidated);
        Assert.Equal("User-Agent", header.Key);
        Assert.Equal(NetworkOptions.UserAgent, header.Value.ToString());
        Assert.Null(request.Headers.Referrer);
        Assert.Null(request.Content);
        Assert.Equal(HttpMethod.Get, request.Method);
    }

    [Fact]
    public async Task A_post_sends_its_body_and_media_type_and_accept_when_asked()
    {
        await _harness.ConnectedExitAsync();
        var body = Encoding.UTF8.GetBytes("""{"method":"eth_blockNumber"}""");

        await _harness.Http.SendAsync(new ExitRequest(ExitMethod.Post, Address)
        {
            Body = body,
            ContentType = "application/json",
            Accept = "application/json",
        });

        var handler = _harness.Handlers.Handlers[0];
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(body, handler.Bodies[0]);
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("application/json", Assert.Single(request.Headers.Accept).MediaType);
        Assert.Equal(["Accept", "User-Agent"], request.Headers.NonValidated.Select(h => h.Key).Order());
    }

    [Theory]
    [InlineData("http://chain.example/tx")]
    [InlineData("ftp://chain.example/tx")]
    [InlineData("https://user:password@chain.example/tx")]
    public async Task An_address_that_is_not_plain_https_is_refused_before_anything_is_sent(string address)
    {
        await _harness.ConnectedExitAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Http.SendAsync(ExitRequest.Get(new Uri(address))));

        Assert.Empty(_harness.Handlers.Handlers[0].Requests);
    }

    [Theory]
    [InlineData("https://localhost/tx")]
    [InlineData("https://api.localhost/tx")]
    [InlineData("https://127.0.0.1/tx")]
    [InlineData("https://127.8.9.10/tx")]
    [InlineData("https://0.0.0.0/tx")]
    [InlineData("https://10.1.2.3/tx")]
    [InlineData("https://100.64.0.1/tx")]
    [InlineData("https://169.254.1.1/tx")]
    [InlineData("https://172.16.0.1/tx")]
    [InlineData("https://172.31.255.255/tx")]
    [InlineData("https://192.168.1.1/tx")]
    [InlineData("https://[::1]/tx")]
    [InlineData("https://[::]/tx")]
    [InlineData("https://[fe80::1]/tx")]
    [InlineData("https://[fd00::1]/tx")]
    [InlineData("https://[::ffff:192.168.1.1]/tx")]
    public async Task An_address_on_the_computer_or_a_private_network_is_refused(string address)
    {
        await _harness.ConnectedExitAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Http.SendAsync(ExitRequest.Get(new Uri(address))));

        Assert.Empty(_harness.Handlers.Handlers[0].Requests);
    }

    [Theory]
    [InlineData("https://172.32.0.1/tx")]
    [InlineData("https://8.8.8.8/tx")]
    [InlineData("https://[2001:db8::1]/tx")]
    [InlineData("https://localhost.example/tx")]
    public async Task A_public_address_is_allowed(string address)
    {
        await _harness.ConnectedExitAsync();

        await _harness.Http.SendAsync(ExitRequest.Get(new Uri(address)));

        Assert.Single(_harness.Handlers.Handlers[0].Requests);
    }

    [Fact]
    public async Task Requests_that_are_not_well_formed_are_refused()
    {
        await _harness.ConnectedExitAsync();
        var http = _harness.Http;

        await Assert.ThrowsAsync<ArgumentException>(
            () => http.SendAsync(ExitRequest.Get(new Uri("/tx", UriKind.Relative))));
        await Assert.ThrowsAsync<ArgumentException>(
            () => http.SendAsync(ExitRequest.Get(Address) with { Body = new byte[] { 1 }, ContentType = "text/plain" }));
        await Assert.ThrowsAsync<ArgumentException>(
            () => http.SendAsync(new ExitRequest(ExitMethod.Post, Address) { Body = new byte[] { 1 } }));
        await Assert.ThrowsAsync<ArgumentException>(
            () => http.SendAsync(ExitRequest.Get(Address) with { Accept = "not a media type" }));
        await Assert.ThrowsAsync<ArgumentException>(() => http.SendAsync(new ExitRequest((ExitMethod)9, Address)));
        await Assert.ThrowsAsync<ArgumentNullException>(() => http.SendAsync(null!));
        Assert.Empty(_harness.Handlers.Handlers[0].Requests);
    }

    [Fact]
    public async Task A_response_that_says_it_is_too_large_is_refused()
    {
        var harness = new NetworkHarness(new NetworkOptions { MaximumResponseBytes = 8 });
        await harness.ConnectedExitAsync();
        harness.Handlers.Answer = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[9]),
        });

        var exception = await Assert.ThrowsAsync<ExitException>(() => harness.Http.SendAsync(ExitRequest.Get(Address)));

        Assert.Equal(ExitError.ResponseTooLarge, exception.Error);
    }

    [Fact]
    public async Task A_response_that_grows_too_large_while_it_is_read_is_refused()
    {
        var harness = new NetworkHarness(new NetworkOptions { MaximumResponseBytes = 8 });
        await harness.ConnectedExitAsync();
        harness.Handlers.Answer = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            // A stream without a length, as a chunked response has.
            Content = new StreamContent(new UnknownLengthStream(new byte[9])),
        });

        var exception = await Assert.ThrowsAsync<ExitException>(() => harness.Http.SendAsync(ExitRequest.Get(Address)));

        Assert.Equal(ExitError.ResponseTooLarge, exception.Error);
    }

    [Fact]
    public async Task A_response_of_exactly_the_limit_is_accepted()
    {
        var harness = new NetworkHarness(new NetworkOptions { MaximumResponseBytes = 8 });
        await harness.ConnectedExitAsync();
        harness.Handlers.Answer = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new UnknownLengthStream(new byte[8])),
        });

        var response = await harness.Http.SendAsync(ExitRequest.Get(Address));

        Assert.Equal(8, response.Body.Length);
    }

    [Fact]
    public async Task A_response_that_does_not_come_in_time_is_a_timeout()
    {
        var harness = new NetworkHarness(new NetworkOptions { RequestTimeout = TimeSpan.FromMilliseconds(100) });
        await harness.ConnectedExitAsync();
        harness.Handlers.Answer = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Never reached.");
        };

        var exception = await Assert.ThrowsAsync<ExitException>(() => harness.Http.SendAsync(ExitRequest.Get(Address)));

        Assert.Equal(ExitError.Timeout, exception.Error);
    }

    [Fact]
    public async Task The_callers_cancellation_is_a_cancellation_not_an_exit_error()
    {
        await _harness.ConnectedExitAsync();
        using var cancel = new CancellationTokenSource();
        _harness.Handlers.Answer = async (_, token) =>
        {
            await cancel.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Never reached.");
        };

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _harness.Http.SendAsync(ExitRequest.Get(Address), cancel.Token));

        Assert.IsNotAssignableFrom<ExitException>(exception);
    }

    [Fact]
    public async Task A_disconnect_while_a_request_is_in_flight_stops_it_as_connection_lost()
    {
        await _harness.ConnectedExitAsync();
        var started = new TaskCompletionSource();
        _harness.Handlers.Answer = async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Never reached.");
        };

        var request = _harness.Http.SendAsync(ExitRequest.Get(Address));
        await started.Task;
        await _harness.Connection.DisconnectAsync();

        var exception = await Assert.ThrowsAsync<ExitException>(() => request);
        Assert.Equal(ExitError.ConnectionLost, exception.Error);
    }

    [Fact]
    public async Task A_failure_on_the_way_is_a_failed_request()
    {
        await _harness.ConnectedExitAsync();
        _harness.Handlers.Answer = (_, _) => throw new HttpRequestException("proxy refused");

        var exception = await Assert.ThrowsAsync<ExitException>(() => _harness.Http.SendAsync(ExitRequest.Get(Address)));

        Assert.Equal(ExitError.RequestFailed, exception.Error);
        Assert.DoesNotContain("chain.example", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A readable stream that does not tell its length.</summary>
    private sealed class UnknownLengthStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();
    }
}
