using System.Net;
using System.Net.Http.Headers;
using Chainsaw.Modules.Network.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Network.Tests;

/// <summary>
/// The way to the user's own node (etapp 25) and a source's <c>Retry-After</c>: only https, only addresses on the
/// computer or a private network, never through the exit, and the response says <c>local</c>. Through the exit a
/// private address is still refused. Never the network: the pipelines are recording fakes.
/// </summary>
public sealed class LocalHttpTests
{
    private readonly NetworkOptions _options = new() { RequestTimeout = TimeSpan.FromSeconds(5) };

    private readonly RecordingLocalHandlerFactory _handlers = new();

    private readonly FakeClock _clock = new();

    [Theory]
    [InlineData("https://192.168.1.20/api/blocks/tip/height")]
    [InlineData("https://esplora.lan/api/blocks/tip/height")]
    [InlineData("https://[fd00::20]:3002/api/blocks/tip/height")]
    [InlineData("https://localhost:3002/blocks/tip/height")]
    public async Task A_request_to_the_own_node_goes_straight_and_says_local(string address)
    {
        using var http = new LocalHttp(_handlers, _options, _clock);

        var response = await http.SendAsync(ExitRequest.Get(new Uri(address)));

        Assert.True(response.IsLocal);
        Assert.Equal("local", response.RouteText);
        Assert.Equal(ExitResponse.LocalRouteCode, response.RouteText);
        Assert.Null(response.ExitIdText);
        var sent = Assert.Single(Assert.Single(_handlers.Handlers).Requests);
        Assert.Equal(new Uri(address), sent.RequestUri);
        Assert.Equal(NetworkOptions.UserAgent, string.Join(" ", sent.Headers.GetValues("User-Agent")));
        Assert.False(sent.Headers.Contains("Cookie"));
    }

    [Theory]
    [InlineData("https://8.8.8.8/api/")]
    [InlineData("https://[2606:4700::1111]/")]
    public async Task A_global_address_is_refused_before_anything_is_built(string address)
    {
        using var http = new LocalHttp(_handlers, _options, _clock);

        await Assert.ThrowsAsync<ArgumentException>(() => http.SendAsync(ExitRequest.Get(new Uri(address))));

        Assert.Empty(_handlers.Handlers);
    }

    [Fact]
    public async Task Plain_http_is_refused_also_where_the_tests_allow_it_through_the_exit()
    {
        var options = new NetworkOptions { AllowPlainHttp = true };
        using var http = new LocalHttp(_handlers, options, _clock);

        await Assert.ThrowsAsync<ArgumentException>(
            () => http.SendAsync(ExitRequest.Get(new Uri("http://192.168.1.20/"))));

        Assert.Empty(_handlers.Handlers);
    }

    [Fact]
    public void A_name_is_connected_only_to_its_addresses_that_are_not_global()
    {
        var kept = LocalHandlerFactory.LocalOnly(
            [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("192.168.1.20"), IPAddress.Parse("fd00::20")]);

        Assert.Equal([IPAddress.Parse("192.168.1.20"), IPAddress.Parse("fd00::20")], kept);
        var refused = Assert.Throws<ExitException>(() => LocalHandlerFactory.LocalOnly(
            [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("2606:4700::1111")]));
        Assert.Equal(ExitError.NotLocal, refused.Error);
    }

    [Fact]
    public async Task A_name_that_points_to_the_internet_is_refused_as_not_local()
    {
        _handlers.Answer = (_, _) => throw new HttpRequestException(
            "connect failed", new ExitException(ExitError.NotLocal));
        using var http = new LocalHttp(_handlers, _options, _clock);

        var refused = await Assert.ThrowsAsync<ExitException>(
            () => http.SendAsync(ExitRequest.Get(new Uri("https://esplora.lan/"))));

        Assert.Equal(ExitError.NotLocal, refused.Error);
    }

    [Fact]
    public async Task The_real_pipeline_never_connects_to_a_global_address()
    {
        // An address on the internet is refused before any connection is tried, so nothing is sent.
        var stream = LocalHandlerFactory.ConnectAsync(new DnsEndPoint("93.184.216.34", 443), default);

        var refused = await Assert.ThrowsAsync<ExitException>(async () => await stream);
        Assert.Equal(ExitError.NotLocal, refused.Error);
    }

    [Fact]
    public async Task A_private_address_is_still_refused_through_the_exit()
    {
        var harness = new NetworkHarness();
        await harness.ConnectedExitAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => harness.Http.SendAsync(ExitRequest.Get(new Uri("https://192.168.1.20/api/"))));
        harness.Route.Choose(ExitRoute.Windows);
        await Assert.ThrowsAsync<ArgumentException>(
            () => harness.Http.SendAsync(ExitRequest.Get(new Uri("https://127.0.0.1/api/"))));

        Assert.Empty(Assert.Single(harness.Handlers.Handlers).Requests);
        Assert.Empty(harness.WindowsHandlers.Handlers);
    }

    [Fact]
    public async Task Retry_After_comes_with_the_response_as_a_delay()
    {
        var harness = new NetworkHarness();
        await harness.ConnectedExitAsync();
        harness.Handlers.Answer = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new ByteArrayContent([]),
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(42));
            return Task.FromResult(response);
        };

        var answer = await harness.Http.SendAsync(ExitRequest.Get(new Uri("https://chain.example/api/")));

        Assert.Equal(429, answer.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(42), answer.RetryAfter);
        Assert.False(answer.IsLocal);
    }

    [Fact]
    public void Retry_After_as_a_date_counts_from_the_arrival_never_below_zero_and_at_most_an_hour()
    {
        var arrived = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        static HttpResponseMessage With(RetryConditionHeaderValue? value)
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = value;
            return response;
        }

        Assert.Equal(TimeSpan.FromSeconds(90),
            ExitHttp.RetryAfterOf(With(new RetryConditionHeaderValue(arrived.AddSeconds(90))), arrived));
        Assert.Equal(TimeSpan.Zero,
            ExitHttp.RetryAfterOf(With(new RetryConditionHeaderValue(arrived.AddSeconds(-90))), arrived));
        Assert.Equal(ExitHttp.MaximumRetryAfter,
            ExitHttp.RetryAfterOf(With(new RetryConditionHeaderValue(TimeSpan.FromDays(3))), arrived));
        Assert.Null(ExitHttp.RetryAfterOf(With(null), arrived));
    }

    [Fact]
    public void The_module_offers_the_way_to_the_own_node()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Chainsaw.SharedKernel.IClock>(_clock);
        services.AddSingleton<Chainsaw.Modules.Security.Contracts.IVaultSecrets>(new FakeSecrets());
        services.AddNetworkModule();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<LocalHttp>(provider.GetRequiredService<ILocalHttp>());
    }
}
