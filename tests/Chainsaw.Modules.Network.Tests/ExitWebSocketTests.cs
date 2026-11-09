using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network.Tests;

/// <summary>
/// The websocket for a source's own news: only <c>wss</c>, never the computer or a private network, and nothing at all
/// without a way out (principle 4). Never the network.
/// </summary>
public sealed class ExitWebSocketTests
{
    private readonly NetworkHarness _harness = new();

    private ExitWebSocket Socket => new(_harness.Connection, _harness.Route, _harness.Presence, _harness.Options);

    [Theory]
    [InlineData("https://mempool.example/api/v1/ws")]
    [InlineData("ws://mempool.example/api/v1/ws")]
    [InlineData("wss://127.0.0.1/api/v1/ws")]
    [InlineData("wss://192.168.1.20/api/v1/ws")]
    [InlineData("wss://user@mempool.example/api/v1/ws")]
    public async Task An_address_that_is_not_plain_wss_out_is_refused(string address)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Socket.ConnectAsync(new Uri(address)));
    }

    [Fact]
    public async Task With_VPN_and_no_exit_connected_nothing_is_sent()
    {
        var error = await Assert.ThrowsAsync<ExitNotConnectedException>(
            () => Socket.ConnectAsync(new Uri("wss://mempool.example/api/v1/ws")));

        Assert.Equal(ExitError.NotConnected, error.Error);
        Assert.Empty(_harness.Handlers.Handlers);
    }

    [Fact]
    public async Task A_source_that_answers_without_a_websocket_fails_through_the_connected_exit()
    {
        var exit = _harness.Store.Add("Exit A", "Oslo", ExitKind.WireGuard, NetworkHarness.Configuration);
        await _harness.Store.SelectAsync(exit.Id);
        await _harness.Connection.ConnectAsync();

        var error = await Assert.ThrowsAsync<ExitException>(
            () => Socket.ConnectAsync(new Uri("wss://blockstream.example/api/v1/ws")));

        Assert.Equal(ExitError.RequestFailed, error.Error);
        Assert.Single(_harness.Handlers.Handlers);
    }

    [Fact]
    public async Task Through_Windows_without_a_network_nothing_is_sent()
    {
        _harness.Route.Choose(ExitRoute.Windows);
        _harness.Presence.IsAvailable = false;

        var error = await Assert.ThrowsAsync<ExitNotConnectedException>(
            () => Socket.ConnectAsync(new Uri("wss://mempool.example/api/v1/ws")));

        Assert.Equal(ExitError.Offline, error.Error);
    }
}
