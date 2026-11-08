using System.Net;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network.Tests;

public sealed class ExitConnectionTests
{
    private readonly NetworkHarness _harness = new();

    [Fact]
    public void A_new_connection_is_not_connected()
    {
        Assert.Equal(ExitStatus.NotConnected, _harness.Connection.Status);
        Assert.False(_harness.Connection.TryLease(out _));
    }

    [Fact]
    public async Task Connecting_without_a_selected_exit_is_refused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.Connection.ConnectAsync());

        Assert.Empty(_harness.Connector.Calls);
        Assert.Empty(_harness.Events);
    }

    [Fact]
    public async Task Connecting_goes_through_connecting_to_connected_with_the_configuration_from_the_vault()
    {
        var exit = await _harness.SelectedExitAsync();

        var status = await _harness.Connection.ConnectAsync();

        Assert.Equal(new ExitStatus(ExitState.Connected, exit.Id, null), status);
        Assert.Equal(
            [new ExitStatus(ExitState.Connecting, exit.Id, null), new ExitStatus(ExitState.Connected, exit.Id, null)],
            _harness.Events);
        Assert.Equal((exit, NetworkHarness.Configuration), Assert.Single(_harness.Connector.Calls));
        Assert.Equal(FakeConnector.LoopbackProxy, Assert.Single(_harness.Handlers.Proxies));
        Assert.True(_harness.Connection.TryLease(out var lease));
        Assert.Equal(exit.Id, lease.ExitId);
    }

    [Fact]
    public async Task The_state_is_connecting_while_the_tunnel_starts_and_requests_are_refused()
    {
        await _harness.SelectedExitAsync();
        _harness.Connector.Gate = new TaskCompletionSource();

        var connecting = _harness.Connection.ConnectAsync();

        Assert.Equal(ExitState.Connecting, _harness.Connection.Status.State);
        await Assert.ThrowsAsync<ExitNotConnectedException>(
            () => _harness.Http.SendAsync(ExitRequest.Get(new Uri("https://chain.example/tx"))));
        _harness.Connector.Gate.SetResult();
        Assert.Equal(ExitState.Connected, (await connecting).State);
    }

    [Fact]
    public async Task Without_a_tunnel_connecting_fails_honestly()
    {
        var harness = new NetworkHarness();
        var connection = new ExitConnection(
            harness.Records, new WireGuardConnector(new MissingHelper(), new NetworkOptions()), harness.Handlers);
        var exit = await harness.SelectedExitAsync();

        var status = await connection.ConnectAsync();

        Assert.Equal(new ExitStatus(ExitState.Failed, exit.Id, ExitFailure.TunnelUnavailable), status);
        Assert.False(connection.TryLease(out _));
        Assert.Empty(harness.Handlers.Proxies);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_connector_that_fails_leaves_the_exit_failed(bool knownFailure)
    {
        var exit = await _harness.SelectedExitAsync();
        _harness.Connector.Failure = knownFailure
            ? new ExitConnectException(ExitFailure.ConnectFailed, "refused")
            : new InvalidDataException("broken");

        var status = await _harness.Connection.ConnectAsync();

        Assert.Equal(new ExitStatus(ExitState.Failed, exit.Id, ExitFailure.ConnectFailed), status);
        Assert.Equal(ExitState.Failed, _harness.Events[^1].State);
        Assert.False(_harness.Connection.TryLease(out _));
    }

    [Fact]
    public async Task A_tunnel_whose_proxy_is_not_on_the_loopback_address_is_refused_and_stopped()
    {
        var exit = await _harness.SelectedExitAsync();
        _harness.Connector.Proxy = new IPEndPoint(IPAddress.Parse("192.0.2.10"), 1080);

        var status = await _harness.Connection.ConnectAsync();

        Assert.Equal(new ExitStatus(ExitState.Failed, exit.Id, ExitFailure.ConnectFailed), status);
        Assert.True(Assert.Single(_harness.Connector.Tunnels).Disposed);
        Assert.Empty(_harness.Handlers.Proxies);
        Assert.False(_harness.Connection.TryLease(out _));
    }

    [Fact]
    public async Task An_attempt_cancelled_while_its_pipeline_is_refused_ends_not_connected()
    {
        using var cancel = new CancellationTokenSource();
        var harness = new NetworkHarness(handlers: new CancellingHandlerFactory(cancel));
        await harness.SelectedExitAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Connection.ConnectAsync(cancel.Token));

        Assert.Equal(ExitStatus.NotConnected, harness.Connection.Status);
        Assert.True(Assert.Single(harness.Connector.Tunnels).Disposed);
        Assert.False(harness.Connection.TryLease(out _));
    }

    [Fact]
    public async Task A_tunnel_that_stops_on_its_own_fails_the_exit_and_stops_requests()
    {
        var exit = await _harness.ConnectedExitAsync();
        Assert.True(_harness.Connection.TryLease(out var lease));

        _harness.Connector.Tunnels[0].Drop();

        // The state and the pipeline change at once; the tunnel is stopped and the event raised on the thread pool.
        var failed = new ExitStatus(ExitState.Failed, exit.Id, ExitFailure.ConnectionLost);
        Assert.Equal(failed, _harness.Connection.Status);
        Assert.True(lease.Stopping.IsCancellationRequested);
        Assert.True(_harness.Handlers.Handlers[0].Disposed);
        Assert.False(_harness.Connection.TryLease(out _));
        await NetworkHarness.WaitUntilAsync(() => _harness.Connector.Tunnels[0].Disposed);
        await NetworkHarness.WaitUntilAsync(() => _harness.Events.Count > 0 && _harness.Events[^1] == failed);
    }

    [Fact]
    public async Task A_tunnel_that_has_already_stopped_when_it_is_handed_over_fails_at_once()
    {
        var exit = await _harness.SelectedExitAsync();
        _harness.Connector.DropAtOnce = true;

        var status = await _harness.Connection.ConnectAsync();

        Assert.Equal(new ExitStatus(ExitState.Failed, exit.Id, ExitFailure.ConnectionLost), status);
        Assert.DoesNotContain(_harness.Events, e => e.State == ExitState.Connected);
        Assert.False(_harness.Connection.TryLease(out _));
    }

    [Fact]
    public async Task Disconnecting_stops_the_tunnel_and_the_pipeline()
    {
        await _harness.ConnectedExitAsync();
        _harness.ClearEvents();

        await _harness.Connection.DisconnectAsync();

        Assert.Equal(ExitStatus.NotConnected, _harness.Connection.Status);
        Assert.Equal([ExitStatus.NotConnected], _harness.Events);
        Assert.True(_harness.Connector.Tunnels[0].Disposed);
        Assert.True(_harness.Handlers.Handlers[0].Disposed);
        Assert.False(_harness.Connection.TryLease(out _));
    }

    [Fact]
    public async Task Disconnecting_when_nothing_is_connected_is_harmless_and_silent()
    {
        await _harness.Connection.DisconnectAsync();

        Assert.Equal(ExitStatus.NotConnected, _harness.Connection.Status);
        Assert.Empty(_harness.Events);
    }

    [Fact]
    public async Task Connecting_again_stops_the_previous_tunnel_first()
    {
        await _harness.ConnectedExitAsync();

        await _harness.Connection.ConnectAsync();

        Assert.Equal(2, _harness.Connector.Tunnels.Count);
        Assert.True(_harness.Connector.Tunnels[0].Disposed);
        Assert.False(_harness.Connector.Tunnels[1].Disposed);
        Assert.True(_harness.Handlers.Handlers[0].Disposed);
    }

    [Fact]
    public async Task A_cancelled_attempt_returns_to_not_connected()
    {
        await _harness.SelectedExitAsync();
        _harness.Connector.Gate = new TaskCompletionSource();
        using var cancel = new CancellationTokenSource();

        var connecting = _harness.Connection.ConnectAsync(cancel.Token);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);
        Assert.Equal(ExitStatus.NotConnected, _harness.Connection.Status);
        Assert.False(_harness.Connection.TryLease(out _));
    }

    [Fact]
    public async Task A_locked_vault_stops_a_connection_attempt_before_anything_starts()
    {
        await _harness.SelectedExitAsync();
        _harness.Secrets.Locked = true;

        await Assert.ThrowsAsync<Chainsaw.Modules.Security.Contracts.VaultException>(
            () => _harness.Connection.ConnectAsync());

        Assert.Empty(_harness.Connector.Calls);
        Assert.Equal(ExitStatus.NotConnected, _harness.Connection.Status);
    }

    [Fact]
    public async Task Disposing_stops_the_connected_tunnel()
    {
        await _harness.ConnectedExitAsync();

        _harness.Connection.Dispose();

        Assert.True(_harness.Connector.Tunnels[0].Disposed);
        Assert.False(_harness.Connection.TryLease(out _));
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("user", "")]
    public async Task A_tunnel_without_proxy_credentials_is_refused_and_stopped(string user, string password)
    {
        var exit = await _harness.SelectedExitAsync();
        _harness.Connector.Credentials = new NetworkCredential(user, password);

        var status = await _harness.Connection.ConnectAsync();

        Assert.Equal(new ExitStatus(ExitState.Failed, exit.Id, ExitFailure.ConnectFailed), status);
        Assert.True(Assert.Single(_harness.Connector.Tunnels).Disposed);
        Assert.Empty(_harness.Handlers.Proxies);
    }

    [Fact]
    public async Task The_pipeline_gets_a_copy_of_the_proxy_that_the_tunnel_cannot_change_afterwards()
    {
        var given = new IPEndPoint(IPAddress.Loopback, 49_200);
        _harness.Connector.Proxy = given;
        await _harness.ConnectedExitAsync();

        given.Address = IPAddress.Parse("192.0.2.10");

        var used = Assert.Single(_harness.Handlers.Proxies);
        Assert.NotSame(given, used);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 49_200), used);
        Assert.Equal("tunnel-user", _harness.Handlers.Credentials[0].UserName);
    }

    [Fact]
    public async Task Disconnecting_cancels_an_attempt_that_hangs()
    {
        await _harness.SelectedExitAsync();
        _harness.Connector.Gate = new TaskCompletionSource();

        var connecting = _harness.Connection.ConnectAsync();
        await _harness.Connection.DisconnectAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);
        Assert.Equal(ExitStatus.NotConnected, _harness.Connection.Status);
        Assert.False(_harness.Connection.TryLease(out _));
    }

    [Fact]
    public async Task Selecting_another_exit_cancels_an_attempt_that_hangs()
    {
        await _harness.SelectedExitAsync("Exit A");
        var other = _harness.Store.Add("Exit B", "Oslo", ExitKind.WireGuard, NetworkHarness.Configuration);
        _harness.Connector.Gate = new TaskCompletionSource();

        var connecting = _harness.Connection.ConnectAsync();
        await _harness.Store.SelectAsync(other.Id);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);
        Assert.Equal(other, _harness.Store.FindSelected());
        Assert.Equal(ExitStatus.NotConnected, _harness.Connection.Status);
    }
}

/// <summary>Cancels the attempt while its pipeline is built, and then fails to build it.</summary>
/// <param name="cancel">The attempt's cancellation.</param>
internal sealed class CancellingHandlerFactory(CancellationTokenSource cancel) : IExitHandlerFactory
{
    public HttpMessageHandler Create(IPEndPoint socks5Proxy, NetworkCredential credentials, Func<bool> proxyIsTunnel)
    {
        cancel.Cancel();
        throw new InvalidOperationException("No pipeline.");
    }
}
