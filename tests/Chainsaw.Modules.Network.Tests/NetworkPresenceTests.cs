using Chainsaw.Modules.Network.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Network.Tests;

/// <summary>
/// Windows as the way out without a network (CLAUDE.md, <em>Windows som utgång</em>): nothing leaves and the refusal
/// says offline honestly, VPN is unaffected, and lookups go again when the network is back. The presence itself is
/// tested in <see cref="PresenceProbingTests"/>.
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
        Assert.Contains("når inte internet", refusal.Message, StringComparison.Ordinal);
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
    public async Task The_module_sends_no_probe_while_vpn_is_the_way_and_probes_once_windows_is_chosen()
    {
        var probe = new ScriptedProbe();
        using var services = new ServiceCollection()
            .AddSingleton<INetworkSignals>(new FakeSignals())
            .AddSingleton<IInternetProbe>(probe)
            .AddNetworkModule()
            .BuildServiceProvider();

        var presence = services.GetRequiredService<INetworkPresence>();
        await Task.Delay(100);
        Assert.Equal(0, probe.Calls);
        Assert.True(presence.IsAvailable);

        services.GetRequiredService<IExitRoute>().Choose(ExitRoute.Windows);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (probe.Calls == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "No probe with Windows chosen.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public void The_module_registers_the_presence_and_keeps_one_already_registered()
    {
        var fake = new FakePresence();
        using var withFake = new ServiceCollection()
            .AddSingleton<INetworkPresence>(fake)
            .AddNetworkModule()
            .BuildServiceProvider();
        // Without a network, so that the real presence sends no probe from the tests.
        using var plain = new ServiceCollection()
            .AddSingleton<INetworkSignals>(new FakeSignals { HasNetwork = false })
            .AddNetworkModule()
            .BuildServiceProvider();

        Assert.Same(fake, withFake.GetRequiredService<INetworkPresence>());
        Assert.IsType<NetworkPresence>(plain.GetRequiredService<INetworkPresence>());
    }
}
