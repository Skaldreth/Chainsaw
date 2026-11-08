using System.Net;
using System.Net.Sockets;

namespace Chainsaw.Modules.Network.Tests;

/// <summary>Names and addresses that the system's proxy would carry into the computer or a private network.</summary>
public sealed class ProxiedNameGuardTests
{
    private static readonly Uri Proxy = new("http://proxy.corp:8080");

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.1", "93.184.216.34")]
    [InlineData("fd00::1")]
    public async Task A_name_with_a_local_address_is_refused_through_the_proxy(params string[] resolved)
    {
        var sent = new List<HttpRequestMessage>();
        using var guard = Guard(sent, Proxy, [.. resolved.Select(IPAddress.Parse)]);
        using var invoker = new HttpMessageInvoker(guard);

        await Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://intranet.corp/api"), CancellationToken.None));

        Assert.Empty(sent);
    }

    [Fact]
    public async Task A_local_address_written_in_the_request_is_refused_through_the_proxy()
    {
        var sent = new List<HttpRequestMessage>();
        using var guard = Guard(sent, Proxy, []);
        using var invoker = new HttpMessageInvoker(guard);

        await Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://[fe80::1]/api"), CancellationToken.None));

        Assert.Empty(sent);
    }

    [Fact]
    public async Task A_global_name_goes_through_the_proxy()
    {
        var sent = new List<HttpRequestMessage>();
        using var guard = Guard(sent, Proxy, [IPAddress.Parse("93.184.216.34")]);
        using var invoker = new HttpMessageInvoker(guard);

        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://chain.example/api"), CancellationToken.None);

        Assert.Single(sent);
    }

    [Fact]
    public async Task A_name_the_computer_cannot_resolve_is_left_to_the_proxy()
    {
        var sent = new List<HttpRequestMessage>();
        using var guard = new ProxiedNameGuard(
            new Recording(sent), _ => Proxy, (_, _) => throw new SocketException((int)SocketError.HostNotFound));
        using var invoker = new HttpMessageInvoker(guard);

        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://chain.example/api"), CancellationToken.None);

        Assert.Single(sent);
    }

    [Fact]
    public async Task Without_a_proxy_nothing_is_resolved_here()
    {
        var sent = new List<HttpRequestMessage>();
        var resolved = 0;
        using var guard = new ProxiedNameGuard(new Recording(sent), _ => null, (_, _) =>
        {
            resolved++;
            return Task.FromResult(new[] { IPAddress.Loopback });
        });
        using var invoker = new HttpMessageInvoker(guard);

        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://chain.example/api"), CancellationToken.None);

        Assert.Equal(0, resolved);
        Assert.Single(sent);
    }

    private static ProxiedNameGuard Guard(List<HttpRequestMessage> sent, Uri? proxy, IPAddress[] addresses) =>
        new(new Recording(sent), _ => proxy, (_, _) => Task.FromResult(addresses));

    /// <summary>Records what reaches the pipeline and answers 200.</summary>
    private sealed class Recording(List<HttpRequestMessage> sent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            sent.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
