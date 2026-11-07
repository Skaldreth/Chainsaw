using Chainsaw.Modules.Network.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Network.Tests;

/// <summary>
/// Windows as the way out without a network (CLAUDE.md, <em>Windows som utgång</em>): nothing leaves and the refusal
/// says offline honestly, VPN is unaffected, and lookups go again when the network is back. The presence itself tells
/// each real change once, and a listener that throws never keeps it from the others.
/// </summary>
public sealed class NetworkPresenceTests
{
    private static readonly Uri Address = new("https://chain.example/api/tx/abc");

    private readonly NetworkHarness _harness = new();

    [Fact]
    public async Task With_windows_and_no_network_nothing_is_sent_and_offline_is_said()
    {
        _harness.Route.Choose(ExitRoute.Windows);
        _harness.Presence.IsAvailable = false;

        var refusal = await Assert.ThrowsAsync<ExitNotConnectedException>(
            () => _harness.Http.SendAsync(ExitRequest.Get(Address)));

        Assert.Equal(ExitError.Offline, refusal.Error);
        Assert.Contains("inget nät", refusal.Message, StringComparison.Ordinal);
        Assert.All(_harness.WindowsHandlers.Handlers, handler => Assert.Empty(handler.Requests));
    }

    [Fact]
    public async Task With_windows_lookups_go_again_when_the_network_is_back()
    {
        _harness.Route.Choose(ExitRoute.Windows);
        _harness.Presence.IsAvailable = false;
        await Assert.ThrowsAsync<ExitNotConnectedException>(() => _harness.Http.SendAsync(ExitRequest.Get(Address)));

        _harness.Presence.IsAvailable = true;
        var response = await _harness.Http.SendAsync(ExitRequest.Get(Address));

        Assert.Equal(ExitRoute.Windows, response.Route);
        Assert.Single(Assert.Single(_harness.WindowsHandlers.Handlers).Requests);
    }

    [Fact]
    public async Task With_vpn_and_a_connected_exit_the_presence_is_not_asked()
    {
        await _harness.ConnectedExitAsync();
        _harness.Presence.IsAvailable = false;

        var response = await _harness.Http.SendAsync(ExitRequest.Get(Address));

        Assert.Equal(ExitRoute.Vpn, response.Route);
    }

    [Fact]
    public async Task With_vpn_and_no_exit_the_refusal_is_still_not_connected()
    {
        _harness.Presence.IsAvailable = false;

        var refusal = await Assert.ThrowsAsync<ExitNotConnectedException>(
            () => _harness.Http.SendAsync(ExitRequest.Get(Address)));

        Assert.Equal(ExitError.NotConnected, refusal.Error);
    }

    [Fact]
    public void The_presence_tells_each_real_change_once()
    {
        using var presence = new NetworkPresence();
        var told = new List<bool>();
        presence.Changed += (_, available) => told.Add(available);
        var start = presence.IsAvailable;

        presence.Set(start);
        presence.Set(!start);
        presence.Set(!start);
        presence.Set(start);

        Assert.Equal([!start, start], told);
        Assert.Equal(start, presence.IsAvailable);
    }

    [Fact]
    public void A_listener_that_throws_never_keeps_the_change_from_the_others()
    {
        using var presence = new NetworkPresence();
        var told = false;
        presence.Changed += (_, _) => throw new InvalidOperationException("listener");
        presence.Changed += (_, _) => told = true;

        presence.Set(!presence.IsAvailable);

        Assert.True(told);
    }

    [Fact]
    public void After_dispose_no_change_is_followed()
    {
        var presence = new NetworkPresence();
        var start = presence.IsAvailable;
        var told = 0;
        presence.Changed += (_, _) => told++;

        presence.Dispose();
        presence.Set(!start);

        Assert.Equal(0, told);
        Assert.Equal(start, presence.IsAvailable);
    }

    [Fact]
    public void The_module_registers_the_presence_and_keeps_one_already_registered()
    {
        var fake = new FakePresence();
        using var withFake = new ServiceCollection()
            .AddSingleton<INetworkPresence>(fake)
            .AddNetworkModule()
            .BuildServiceProvider();
        using var plain = new ServiceCollection().AddNetworkModule().BuildServiceProvider();

        Assert.Same(fake, withFake.GetRequiredService<INetworkPresence>());
        Assert.IsType<NetworkPresence>(plain.GetRequiredService<INetworkPresence>());
    }
}
