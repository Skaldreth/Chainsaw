using System.Net;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Monitoring.Tests;
using Chainsaw.Modules.Network;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Watcher.Tests;

/// <summary>
/// VPN in the background (decided 2026-11-08): the open Chainsaw allows its selected exit, whose WireGuard file is then
/// kept under the user's protection outside the vault; the background process connects that exit through the same
/// connection (and helper) and looks up only while it is connected, and lets it go when the permission is taken back.
/// </summary>
public sealed class VpnInBackgroundTests : IDisposable
{
    private const string Configuration = "[Interface]\nPrivateKey = test-private-key-not-real\nDNS = 10.64.0.1\n";

    private readonly World _world = new();

    private readonly RecordingConnector _connector = new();

    private readonly ServiceProvider _network;

    public VpnInBackgroundTests()
    {
        // The open Chainsaw's Network module, writing the allowed exits into the shared folder.
        var services = new ServiceCollection();
        services.AddSingleton<IVaultSecrets>(new TestVaultSecrets());
        services.AddSingleton<IUserDataProtector>(_world.Protector);
        services.AddSingleton<IClock>(_world.Clock);
        services.AddNetworkModule(options => options.BackgroundExitsFile = Path.Combine(
            _world.AppData, BackgroundNames.Folder, NetworkOptions.BackgroundExitsFileName));
        _network = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _network.Dispose();
        _world.Dispose();
    }

    private WatcherLoop Loop => _world.Background<WatcherLoop>();

    [Fact]
    public async Task An_allowed_selected_exit_is_connected_and_the_watch_is_checked_through_it()
    {
        await AllowSelectedExitAsync();
        await ReadyWatchAsync();

        Assert.True(await Loop.RunOnceAsync());

        var (exit, configuration) = Assert.Single(_connector.Calls);
        Assert.Equal("Exit A", exit.Name);
        Assert.Equal(Configuration, configuration);
        Assert.Equal(ExitState.Connected, _world.Background<IExitConnection>().Status.State);
        Assert.NotEmpty(_world.Http.Sent);
        Assert.Equal(BackgroundState.Running, _world.App<IBackgroundStatusFile>().Read()!.State);
    }

    [Fact]
    public async Task Without_an_allowed_exit_nothing_is_connected_or_looked_up()
    {
        var exits = _network.GetRequiredService<IExitStore>();
        var exit = exits.Add("Exit A", "Oslo", ExitKind.WireGuard, Configuration);
        await exits.SelectAsync(exit.Id);
        await ReadyWatchAsync();

        Assert.True(await Loop.RunOnceAsync());

        Assert.Empty(_connector.Calls);
        Assert.Empty(_world.Http.Sent);
        Assert.Equal(BackgroundState.WaitingForExit, _world.App<IBackgroundStatusFile>().Read()!.State);
    }

    [Fact]
    public async Task Taking_the_permission_back_lets_the_exit_go_at_the_next_round()
    {
        var exitId = await AllowSelectedExitAsync();
        await ReadyWatchAsync();
        Assert.True(await Loop.RunOnceAsync());

        _network.GetRequiredService<IBackgroundExits>().Forbid(exitId);
        _world.Http.Sent.Clear();
        _world.Clock.UtcNow += TimeSpan.FromMinutes(2);

        Assert.True(await Loop.RunOnceAsync());

        Assert.Equal(ExitState.NotConnected, _world.Background<IExitConnection>().Status.State);
        Assert.True(_connector.Tunnels.Single().Disposed);
        Assert.Empty(_world.Http.Sent);
        Assert.Equal(BackgroundState.WaitingForExit, _world.App<IBackgroundStatusFile>().Read()!.State);
    }

    [Fact]
    public async Task A_failed_connection_is_tried_again_only_after_a_minute()
    {
        await AllowSelectedExitAsync();
        await ReadyWatchAsync();
        _connector.Fails = true;

        Assert.True(await Loop.RunOnceAsync());
        Assert.True(await Loop.RunOnceAsync());
        Assert.Single(_connector.Calls);

        _world.Clock.UtcNow += WatcherLoop.ConnectAgain;
        _connector.Fails = false;
        Assert.True(await Loop.RunOnceAsync());

        Assert.Equal(2, _connector.Calls.Count);
        Assert.Equal(ExitState.Connected, _world.Background<IExitConnection>().Status.State);
    }

    /// <summary>Adds an exit in the open Chainsaw, selects it and allows it in the background.</summary>
    /// <returns>The exit's id.</returns>
    private async Task<Guid> AllowSelectedExitAsync()
    {
        var exits = _network.GetRequiredService<IExitStore>();
        var exit = exits.Add("Exit A", "Oslo", ExitKind.WireGuard, Configuration);
        await exits.SelectAsync(exit.Id);
        _network.GetRequiredService<IBackgroundExits>().Allow(exit.Id);
        return exit.Id;
    }

    /// <summary>A watch on the synthetic address, in a plan through VPN, with the background's own connector.</summary>
    /// <returns>A task that completes when it is ready.</returns>
    private async Task ReadyWatchAsync()
    {
        _world.Configure = services => services.AddSingleton<IExitConnector>(_connector);
        await _world.UseTestEndpointAsync();
        await _world.WatchAsync();
        _world.WritePlan(ExitRoute.Vpn);
        _world.Height(800_010);
        _world.Clock.UtcNow += TimeSpan.FromMinutes(5);
    }

    /// <summary>Starts tunnels in memory and remembers what it was given.</summary>
    private sealed class RecordingConnector : IExitConnector
    {
        public List<(ExitInfo Exit, string Configuration)> Calls { get; } = [];

        public List<MemoryTunnel> Tunnels { get; } = [];

        public bool Fails { get; set; }

        public Task<IExitTunnel> ConnectAsync(ExitInfo exit, string configuration, CancellationToken cancellationToken)
        {
            Calls.Add((exit, configuration));
            if (Fails)
            {
                throw new ExitConnectException(ExitFailure.ConnectFailed, "The test refuses.");
            }

            var tunnel = new MemoryTunnel();
            Tunnels.Add(tunnel);
            return Task.FromResult<IExitTunnel>(tunnel);
        }
    }

    /// <summary>A tunnel that exists only in memory, with a proxy on the loopback address.</summary>
    private sealed class MemoryTunnel : IExitTunnel
    {
        private readonly CancellationTokenSource _stopped = new();

        public IPEndPoint Socks5Proxy { get; } = new(IPAddress.Loopback, 49_151);

        public NetworkCredential Socks5Credentials { get; } = new("tunnel-user", "tunnel-password-1");

        public CancellationToken Stopped => _stopped.Token;

        public bool IsRunning => !Disposed;

        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
