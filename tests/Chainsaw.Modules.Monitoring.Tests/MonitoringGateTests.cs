using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring.Tests;

/// <summary>
/// The gate: never a lookup while locked, without a connected exit when VPN is chosen, or for a chain off.
/// </summary>
public sealed class MonitoringGateTests
{
    private readonly FakeVault _vault = new();
    private readonly Route _route = new();
    private readonly Connection _connection = new();
    private readonly Sources _sources = new();

    private MonitoringGate Gate => new(_vault, _route, _connection, _sources);

    [Fact]
    public void Open_through_a_connected_exit_or_through_Windows()
    {
        _connection.Status = new ExitStatus(ExitState.Connected, Guid.NewGuid(), null);
        Assert.Equal(WatchGate.Open, Gate.For(Chain.Bitcoin));

        _connection.Status = ExitStatus.NotConnected;
        _route.Current = ExitRoute.Windows;
        Assert.Equal(WatchGate.Open, Gate.For(Chain.Ethereum));
    }

    [Fact]
    public void Windows_without_the_internet_lets_nothing_out()
    {
        _route.Current = ExitRoute.Windows;
        var presence = new Presence { IsAvailable = false };
        var gate = new MonitoringGate(_vault, _route, _connection, _sources, presence);

        Assert.Equal(WatchGate.NoExit, gate.For(Chain.Bitcoin));

        presence.IsAvailable = true;
        Assert.Equal(WatchGate.Open, gate.For(Chain.Bitcoin));
    }

    [Fact]
    public void Vpn_ignores_the_presence_which_is_not_probed_then()
    {
        var presence = new Presence { IsAvailable = false };
        var gate = new MonitoringGate(_vault, _route, _connection, _sources, presence);
        _connection.Status = new ExitStatus(ExitState.Connected, Guid.NewGuid(), null);
        Assert.Equal(WatchGate.Open, gate.For(Chain.Bitcoin));

        presence.IsAvailable = true;
        _connection.Status = ExitStatus.NotConnected;
        Assert.Equal(WatchGate.NoExit, gate.For(Chain.Bitcoin));
    }

    [Fact]
    public void Closed_while_locked_without_an_exit_and_for_a_chain_off()
    {
        Assert.Equal(WatchGate.NoExit, Gate.For(Chain.Bitcoin));

        _route.Current = ExitRoute.Windows;
        _sources.Off.Add(Chain.Bitcoin);
        Assert.Equal(WatchGate.ChainOff, Gate.For(Chain.Bitcoin));
        Assert.Equal(WatchGate.ChainOff, Gate.For(Chain.Tron));

        _vault.State = VaultState.Locked;
        Assert.Equal(WatchGate.Locked, Gate.For(Chain.Ethereum));
    }

    private sealed class Presence : INetworkPresence
    {
        public event EventHandler<bool>? Changed
        {
            add { }
            remove { }
        }

        public bool IsAvailable { get; set; }
    }

    private sealed class Route : IExitRoute
    {
        public ExitRoute Current { get; set; } = ExitRoute.Vpn;

        public event EventHandler<ExitRoute>? Changed
        {
            add { }
            remove { }
        }

        public void Choose(ExitRoute route) => Current = route;
    }

    private sealed class Connection : IExitConnection
    {
        public ExitStatus Status { get; set; } = ExitStatus.NotConnected;

        public event EventHandler<ExitStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public Task<ExitStatus> ConnectAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DisconnectAsync() => throw new NotSupportedException();
    }

    private sealed class Sources : IChainSources
    {
        public event EventHandler<SourceEndpoint>? CertificateRefused
        {
            add { }
            remove { }
        }

        public Task<Chainsaw.Modules.Network.Contracts.NodeCertificate> ReadCertificateAsync(
            Chain chain, Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task PinAsync(Chain chain, Guid id, string fingerprint, string by) => Task.CompletedTask;

        public Task UnpinAsync(Chain chain, Guid id, string by) => Task.CompletedTask;

        public HashSet<Chain> Off { get; } = [];

        public IReadOnlyList<Chain> Chains => [Chain.Bitcoin, Chain.Ethereum];

        public event EventHandler<Chain>? Changed
        {
            add { }
            remove { }
        }

        public ChainSourcesView Get(Chain chain) =>
            new(chain, Off.Contains(chain) ? SourceMode.Off : SourceMode.Fastest, [], null);

        public IReadOnlyList<SourceApiInfo> Apis(Chain chain) => throw new NotSupportedException();

        public Task SetModeAsync(Chain chain, SourceMode mode) => throw new NotSupportedException();

        public (SourceEndpoint Endpoint, Task Saved) Add(
            Chain chain, Uri address, string api, string? apiKey, bool local = false) =>
            throw new NotSupportedException();

        public Task RemoveAsync(Chain chain, Guid id) => throw new NotSupportedException();

        public Task SetEnabledAsync(Chain chain, Guid id, bool enabled) => throw new NotSupportedException();

        public Task MoveAsync(Chain chain, Guid id, int steps) => throw new NotSupportedException();

        public Task<ProbeRun> ProbeAsync(Chain chain, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EndpointProbe> TryAsync(
            Chain chain, Uri address, string api, string? apiKey, bool local = false,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
