using System.Net;
using System.Security.Cryptography.X509Certificates;
using Chainsaw.Modules.Network.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Network.Tests;

/// <summary>
/// The way out chosen in the settings (etapp 19): VPN, where nothing leaves without a connected exit and Windows is
/// never asked, or Windows, through the computer's own network. Never the network: the pipelines are recording fakes.
/// </summary>
public sealed class ExitRouteTests
{
    private static readonly Uri Address = new("https://chain.example/api/tx/abc");

    private readonly NetworkHarness _harness = new();

    [Fact]
    public void The_way_out_is_vpn_unless_something_else_is_chosen()
    {
        Assert.Equal(ExitRoute.Vpn, new NetworkOptions().Route);
        Assert.Equal(ExitRoute.Vpn, _harness.Route.Current);
    }

    [Fact]
    public async Task With_vpn_and_no_exit_nothing_is_sent_and_windows_is_never_built()
    {
        await Assert.ThrowsAsync<ExitNotConnectedException>(() => _harness.Http.SendAsync(ExitRequest.Get(Address)));

        Assert.Empty(_harness.WindowsHandlers.Handlers);
        Assert.Empty(_harness.Handlers.Handlers);
    }

    [Fact]
    public async Task With_vpn_and_a_failed_exit_nothing_goes_through_windows()
    {
        await _harness.SelectedExitAsync();
        _harness.Connector.Failure = new ExitConnectException(ExitFailure.ConnectFailed, "refused");
        await _harness.Connection.ConnectAsync();

        await Assert.ThrowsAsync<ExitNotConnectedException>(() => _harness.Http.SendAsync(ExitRequest.Get(Address)));

        Assert.Empty(_harness.WindowsHandlers.Handlers);
    }

    [Fact]
    public async Task With_windows_a_request_leaves_through_windows_and_says_so()
    {
        _harness.Route.Choose(ExitRoute.Windows);

        var response = await _harness.Http.SendAsync(ExitRequest.Get(Address));

        Assert.Equal(ExitRoute.Windows, response.Route);
        Assert.Equal(Guid.Empty, response.ExitId);
        Assert.Null(response.ExitIdText);
        Assert.Equal("windows", response.RouteText);
        var sent = Assert.Single(Assert.Single(_harness.WindowsHandlers.Handlers).Requests);
        Assert.Equal(Address, sent.RequestUri);
        Assert.Equal(HttpVersion.Version11, sent.Version);
        Assert.Equal(NetworkOptions.UserAgent, string.Join(" ", sent.Headers.GetValues("User-Agent")));
        Assert.False(sent.Headers.Contains("Cookie"));
        Assert.Null(sent.Headers.Referrer);
    }

    [Fact]
    public async Task With_windows_a_connected_exit_is_left_alone_and_unused()
    {
        await _harness.ConnectedExitAsync();
        _harness.Route.Choose(ExitRoute.Windows);

        await _harness.Http.SendAsync(ExitRequest.Get(Address));

        Assert.Equal(ExitState.Connected, _harness.Connection.Status.State);
        Assert.Empty(Assert.Single(_harness.Handlers.Handlers).Requests);
        Assert.Single(Assert.Single(_harness.WindowsHandlers.Handlers).Requests);
    }

    [Fact]
    public async Task With_vpn_and_a_connected_exit_the_response_names_the_exit_and_the_way_vpn()
    {
        var exit = await _harness.ConnectedExitAsync();

        var response = await _harness.Http.SendAsync(ExitRequest.Get(Address));

        Assert.Equal(ExitRoute.Vpn, response.Route);
        Assert.Equal(exit.Id.ToString("D"), response.ExitIdText);
        Assert.Equal("vpn", response.RouteText);
        Assert.Empty(_harness.WindowsHandlers.Handlers);
    }

    [Fact]
    public async Task Back_to_vpn_without_an_exit_nothing_is_sent_and_the_windows_pipeline_is_freed()
    {
        _harness.Route.Choose(ExitRoute.Windows);
        await _harness.Http.SendAsync(ExitRequest.Get(Address));
        _harness.Route.Choose(ExitRoute.Vpn);

        await Assert.ThrowsAsync<ExitNotConnectedException>(() => _harness.Http.SendAsync(ExitRequest.Get(Address)));

        var windows = Assert.Single(_harness.WindowsHandlers.Handlers);
        Assert.Single(windows.Requests);
        Assert.True(windows.Disposed);
    }

    [Fact]
    public async Task Leaving_windows_stops_a_request_in_flight_through_it()
    {
        var started = new TaskCompletionSource();
        _harness.WindowsHandlers.Answer = async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Never answered.");
        };
        _harness.Route.Choose(ExitRoute.Windows);
        var request = _harness.Http.SendAsync(ExitRequest.Get(Address));
        await started.Task;

        _harness.Route.Choose(ExitRoute.Vpn);

        var exception = await Assert.ThrowsAsync<ExitException>(() => request);
        Assert.Equal(ExitError.ConnectionLost, exception.Error);
    }

    [Fact]
    public async Task Windows_chosen_again_builds_a_new_pipeline()
    {
        _harness.Route.Choose(ExitRoute.Windows);
        await _harness.Http.SendAsync(ExitRequest.Get(Address));
        _harness.Route.Choose(ExitRoute.Vpn);
        _harness.Route.Choose(ExitRoute.Windows);

        await _harness.Http.SendAsync(ExitRequest.Get(Address));

        Assert.Equal(2, _harness.WindowsHandlers.Handlers.Count);
        Assert.Single(_harness.WindowsHandlers.Handlers[1].Requests);
    }

    [Theory]
    [InlineData("http://chain.example/api")]
    [InlineData("https://127.0.0.1/api")]
    [InlineData("https://192.168.1.10/api")]
    [InlineData("https://localhost/api")]
    [InlineData("https://user:secret@chain.example/api")]
    public async Task With_windows_only_plain_https_to_a_global_address_is_sent(string address)
    {
        _harness.Route.Choose(ExitRoute.Windows);

        await Assert.ThrowsAsync<ArgumentException>(() => _harness.Http.SendAsync(ExitRequest.Get(new Uri(address))));

        Assert.Empty(_harness.WindowsHandlers.Handlers);
    }

    [Fact]
    public void A_change_is_told_once_and_choosing_the_same_way_again_tells_nothing()
    {
        var told = new List<ExitRoute>();
        _harness.Route.Changed += (_, route) => throw new InvalidOperationException("A listener that fails.");
        _harness.Route.Changed += (_, route) => told.Add(route);

        _harness.Route.Choose(ExitRoute.Windows);
        _harness.Route.Choose(ExitRoute.Windows);
        _harness.Route.Choose(ExitRoute.Vpn);

        Assert.Equal([ExitRoute.Windows, ExitRoute.Vpn], told);
        Assert.Equal(ExitRoute.Vpn, _harness.Route.Current);
    }

    [Fact]
    public void An_unknown_way_is_refused_and_changes_nothing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _harness.Route.Choose((ExitRoute)7));

        Assert.Equal(ExitRoute.Vpn, _harness.Route.Current);
        Assert.Throws<ArgumentOutOfRangeException>(() => ((ExitRoute)7).Code());
    }

    [Fact]
    public async Task The_way_chosen_at_the_start_comes_from_the_options()
    {
        var harness = new NetworkHarness(new NetworkOptions { Route = ExitRoute.Windows });

        var response = await harness.Http.SendAsync(ExitRequest.Get(Address));

        Assert.Equal(ExitRoute.Windows, response.Route);
    }

    [Fact]
    public async Task After_the_module_ends_nothing_leaves_through_windows()
    {
        _harness.Route.Choose(ExitRoute.Windows);
        await _harness.Http.SendAsync(ExitRequest.Get(Address));

        _harness.Route.Dispose();

        await Assert.ThrowsAsync<ExitNotConnectedException>(() => _harness.Http.SendAsync(ExitRequest.Get(Address)));
        Assert.True(Assert.Single(_harness.WindowsHandlers.Handlers).Disposed);
    }

    [Fact]
    public void Choosing_a_way_after_the_module_ends_is_harmless()
    {
        _harness.Route.Choose(ExitRoute.Windows);
        _harness.Route.Dispose();

        _harness.Route.Choose(ExitRoute.Vpn);

        Assert.Equal(ExitRoute.Vpn, _harness.Route.Current);
    }

    [Fact]
    public async Task A_windows_pipeline_that_cannot_be_built_fails_as_the_exit_does()
    {
        var harness = new NetworkHarness(new NetworkOptions { Route = ExitRoute.Windows }, failingWindows: true);

        var exception = await Assert.ThrowsAsync<ExitException>(() => harness.Http.SendAsync(ExitRequest.Get(Address)));

        Assert.Equal(ExitError.RequestFailed, exception.Error);
    }

    [Fact]
    public async Task Vpn_chosen_while_the_windows_pipeline_is_built_sends_nothing_through_windows()
    {
        _harness.Route.Choose(ExitRoute.Windows);
        _harness.WindowsHandlers.Building = () => _harness.Route.Choose(ExitRoute.Vpn);

        await Assert.ThrowsAsync<ExitNotConnectedException>(() => _harness.Http.SendAsync(ExitRequest.Get(Address)));

        var built = Assert.Single(_harness.WindowsHandlers.Handlers);
        Assert.True(built.Disposed);
        Assert.Empty(built.Requests);
    }

    [Fact]
    public void The_container_gives_one_choice_and_refuses_an_unknown_way_at_the_start()
    {
        using var provider = new ServiceCollection()
            .AddNetworkModule(options => options.Route = ExitRoute.Windows)
            .BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<ExitRouteChoice>(), provider.GetRequiredService<IExitRoute>());
        Assert.Equal(ExitRoute.Windows, provider.GetRequiredService<IExitRoute>().Current);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ServiceCollection().AddNetworkModule(options => options.Route = (ExitRoute)7));
    }

    [Fact]
    public void The_windows_handler_uses_the_systems_proxy_and_sends_no_cookies_credentials_or_downloads()
    {
        using var handler = (SocketsHttpHandler)new WindowsHandlerFactory(new NetworkOptions()).Create();

        Assert.True(handler.UseProxy);
        Assert.Null(handler.Proxy);
        Assert.Null(handler.DefaultProxyCredentials);
        Assert.Null(handler.Credentials);
        Assert.False(handler.PreAuthenticate);
        Assert.False(handler.UseCookies);
        Assert.False(handler.AllowAutoRedirect);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.NotNull(handler.ConnectCallback);
        Assert.Null(handler.ActivityHeadersPropagator);
        Assert.False(handler.SslOptions.AllowTlsResume);
        Assert.True(handler.SslOptions.CertificateChainPolicy!.DisableCertificateDownloads);
        Assert.Equal(X509RevocationMode.NoCheck, handler.SslOptions.CertificateChainPolicy.RevocationMode);
        Assert.Equal(X509ChainTrustMode.CustomRootTrust, handler.SslOptions.CertificateChainPolicy.TrustMode);
    }

    [Theory]
    [InlineData("chain.example", 443, "https://chain.example/api", true)]
    [InlineData("CHAIN.example", 443, "https://chain.example/api", true)]
    [InlineData("proxy.corp", 8080, "https://chain.example/api", false)]
    [InlineData("chain.example", 8443, "https://chain.example/api", false)]
    [InlineData("2001:4860::1", 443, "https://[2001:4860::1]/api", true)]
    public void A_connection_to_the_source_itself_is_told_from_one_to_a_proxy(
        string host, int port, string address, bool isSource)
    {
        Assert.Equal(isSource, WindowsHandlerFactory.IsSource(new DnsEndPoint(host, port), new Uri(address)));
        Assert.False(WindowsHandlerFactory.IsSource(new DnsEndPoint(host, port), null));
    }

    [Fact]
    public void A_name_that_points_only_into_the_computer_or_a_private_network_is_refused()
    {
        var global = IPAddress.Parse("2001:4860:4860::8888");

        Assert.Throws<HttpRequestException>(() => WindowsHandlerFactory.GlobalOnly(
            [IPAddress.Loopback, IPAddress.Parse("10.0.0.1"), IPAddress.Parse("fe80::1")]));
        Assert.Equal([global], WindowsHandlerFactory.GlobalOnly([IPAddress.Parse("192.168.0.1"), global]));
    }

    [Fact]
    public async Task A_direct_connection_to_a_name_on_the_computer_is_refused_before_a_socket_opens()
    {
        await Assert.ThrowsAsync<HttpRequestException>(async () => await WindowsHandlerFactory.ConnectAsync(
            new DnsEndPoint("localhost", 443), new Uri("https://localhost/"), CancellationToken.None));
    }
}
