using System.Text;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Network.Tests;

public sealed class NetworkModuleTests : IDisposable
{
    private const string MasterKey = "correct horse battery";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "chainsaw-network-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void AddNetworkModule_returns_the_same_container()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddNetworkModule());
    }

    [Fact]
    public void AddNetworkModule_refuses_a_missing_container()
    {
        Assert.Throws<ArgumentNullException>(() => NetworkModule.AddNetworkModule(null!));
    }

    [Fact]
    public void The_default_limits_are_thirty_seconds_and_32_MiB_and_https_only()
    {
        var options = new NetworkOptions();

        Assert.Equal(TimeSpan.FromSeconds(30), options.RequestTimeout);
        Assert.Equal(32 * 1024 * 1024, options.MaximumResponseBytes);
        Assert.False(options.AllowPlainHttp);
        Assert.False(options.AllowLocalTargets);
    }

    [Fact]
    public void Limits_that_would_break_every_request_are_refused()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => services.AddNetworkModule(options => options.RequestTimeout = TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => services.AddNetworkModule(options => options.RequestTimeout = Timeout.InfiniteTimeSpan));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => services.AddNetworkModule(options => options.ConnectTimeout = TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => services.AddNetworkModule(options => options.MaximumResponseBytes = 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => services.AddNetworkModule(options => options.RequestTimeout = TimeSpan.FromDays(60)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => services.AddNetworkModule(options => options.ConnectTimeout = TimeSpan.FromDays(30)));
    }

    [Fact]
    public async Task Without_the_helper_next_to_the_program_the_container_connects_nothing_and_refuses_every_request()
    {
        using var provider = BuildProvider();
        provider.GetRequiredService<IVault>().Create(MasterKey);
        var store = provider.GetRequiredService<IExitStore>();
        var configuration = "[Interface]\nPrivateKey = not-real\nDNS = 10.64.0.1\n";
        var exit = store.Add("Exit A", "Stockholm", ExitKind.WireGuard, configuration);
        await store.SelectAsync(exit.Id);

        var status = await provider.GetRequiredService<IExitConnection>().ConnectAsync();

        Assert.Equal(new ExitStatus(ExitState.Failed, exit.Id, ExitFailure.TunnelUnavailable), status);
        await Assert.ThrowsAsync<ExitNotConnectedException>(() => provider.GetRequiredService<IExitHttp>()
            .SendAsync(ExitRequest.Get(new Uri("https://chain.example/"))));
    }

    [Fact]
    public void A_registered_tunnel_replaces_the_wireguard_helper()
    {
        var connector = new FakeConnector();
        using var provider = new ServiceCollection()
            .AddSingleton<IExitConnector>(connector)
            .AddNetworkModule()
            .BuildServiceProvider();

        Assert.Same(connector, provider.GetRequiredService<IExitConnector>());
    }

    [Fact]
    public void The_container_connects_with_the_wireguard_helper()
    {
        using var provider = new ServiceCollection().AddNetworkModule().BuildServiceProvider();

        Assert.IsType<WireGuardConnector>(provider.GetRequiredService<IExitConnector>());
        Assert.IsType<HelperProcessStarter>(provider.GetRequiredService<ITunnelProcessStarter>());
    }

    [Fact]
    public async Task The_exits_are_kept_encrypted_in_the_vault_and_come_back_after_a_restart()
    {
        const string marker = "PrivateKey = configuration-marker-1234567890";
        Guid id;
        using (var provider = BuildProvider())
        {
            provider.GetRequiredService<IVault>().Create(MasterKey);
            var store = provider.GetRequiredService<IExitStore>();
            var configuration = "[Interface]\nDNS = 10.64.0.1\n" + marker;
            id = store.Add("Exit-name-marker", "Place-marker", ExitKind.WireGuard, configuration).Id;
            await store.SelectAsync(id);
        }

        var file = File.ReadAllBytes(Path.Combine(_directory, "vault.json"));
        foreach (var plain in new[] { marker, "Exit-name-marker", "Place-marker", id.ToString("N"), "network/" })
        {
            Assert.DoesNotContain(plain, Encoding.UTF8.GetString(file), StringComparison.Ordinal);
        }

        using (var provider = BuildProvider())
        {
            var store = provider.GetRequiredService<IExitStore>();
            var locked = Assert.Throws<VaultException>(store.List);
            Assert.Equal(VaultError.Locked, locked.Error);

            provider.GetRequiredService<IVault>().Unlock(MasterKey);
            Assert.Equal(new ExitInfo(id, "Exit-name-marker", "Place-marker", ExitKind.WireGuard), store.FindSelected());
            Assert.Equal(
                [ExitRecords.ExitPrefix + id.ToString("N")],
                provider.GetRequiredService<IVaultSecrets>().List(ExitRecords.ExitPrefix));
        }
    }

    private ServiceProvider BuildProvider() =>
        new ServiceCollection()
            .AddSingleton<IClock, SystemClock>()
            .AddSecurityModule(options => options.Directory = _directory)
            .AddNetworkModule()
            .BuildServiceProvider();
}
