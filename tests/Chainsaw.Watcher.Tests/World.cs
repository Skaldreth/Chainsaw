using System.Globalization;
using System.Text;
using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering;
using Chainsaw.Modules.Monitoring;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Monitoring.Tests;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Watcher.Tests;

/// <summary>
/// The open Chainsaw's side (a real case with a watch, the endpoints' file, the plan) and the background process's side
/// (the real modules behind a fake exit with synthetic Esplora pages), sharing one application folder. Never the
/// network, never DPAPI (a stand-in protects the files).
/// </summary>
internal sealed class World : IDisposable
{
    public const string Base = "https://esplora.test/api/";

    public const string Watched = "bc1qxvenxvenxvenxvenxvenxvenxvenxven2ymjt8";

    public const string Spent = "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1";

    public const string Coinbase = "a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3a3";

    private readonly ServiceProvider _app;

    private ServiceProvider? _watcher;

    public World()
    {
        Http = new FakeExitHttp(Clock);
        var services = new ServiceCollection();
        services.AddSingleton<IVault, FakeVault>();
        services.AddSingleton<IPurposeKeys, FakePurposeKeys>();
        services.AddSingleton<IOperatorProfileStore>(new FakeProfiles());
        services.AddSingleton<IVaultSecrets>(new TestVaultSecrets());
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<IExitHttp>(new FakeExitHttp(Clock));
        services.AddSingleton<IWatchGate>(new FakeGate());
        services.AddSingleton<IUserDataProtector>(Protector);
        services.AddCasesModule(options =>
        {
            options.Directory = Path.Combine(AppData, "Ärenden");
            options.AnchorDirectory = Path.Combine(AppData, "anchors");
        });
        services.AddChainsModule(sources: options => options.Directory = AppData);
        services.AddClusteringModule();
        services.AddMonitoringModule();
        services.AddBackgroundWatchFiles(Path.Combine(AppData, BackgroundNames.Folder));
        _app = services.BuildServiceProvider();
        Case = _app.GetRequiredService<ICaseStore>().Create("2026-0270");
        Http.Answer(Base + $"address/{Watched}/txs", Recorded("address-page-first.json"));
        Http.Answer(Base + $"address/{Watched}/txs/chain/{Spent}", Recorded("address-page-after.json"));
    }

    public string AppData { get; } =
        Path.Combine(Path.GetTempPath(), "chainsaw-watcher-" + Guid.NewGuid().ToString("N"));

    public FakeClock Clock { get; } = new();

    public FakeExitHttp Http { get; }

    public FakeProtector Protector { get; } = new();

    public RecordingAlerts Alerts { get; } = new();

    public ICase Case { get; }

    public T App<T>()
        where T : notnull => _app.GetRequiredService<T>();

    public T Background<T>()
        where T : notnull => Watcher.GetRequiredService<T>();

    /// <summary>The background process's services, made when first asked, as the process makes them.</summary>
    public ServiceProvider Watcher => _watcher ??= WatcherServices.Build(AppData, services =>
    {
        services.AddSingleton<IUserDataProtector>(Protector);
        services.AddSingleton<IExitHttp>(Http);
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<IAlerts>(Alerts);
        services.AddSingleton<IExitWebSocket>(new NoSockets());
        Configure?.Invoke(services);
    });

    /// <summary>Gets or sets what a test puts in place in the background process, before it is first made.</summary>
    public Action<IServiceCollection>? Configure { get; set; }

    /// <summary>Bitcoin's endpoints in Chainsaw's settings: only the synthetic Esplora.</summary>
    public async Task UseTestEndpointAsync()
    {
        var sources = App<IChainSources>();
        foreach (var endpoint in sources.Get(Chain.Bitcoin).Endpoints.ToList())
        {
            await sources.RemoveAsync(Chain.Bitcoin, endpoint.Endpoint.Id);
        }

        await sources.Add(Chain.Bitcoin, new Uri(Base), "esplora", null).Saved;
    }

    /// <summary>Adds a watch on the synthetic address and starts it at a block, as the open Chainsaw does.</summary>
    public async Task<Watch> WatchAsync(long start = 799_999)
    {
        var service = App<IMonitoringService>();
        var watch = service.Add(
            Case,
            new WatchSubject(WatchSubjectKind.Address, new ChainAddress(Chain.Bitcoin, Watched), "bc1q…jt8"),
            new WatchFilter(Chain.Bitcoin, WatchDirection.Both, [], [], null, null));
        await service.CheckAsync(Case, Chain.Bitcoin, start);
        return watch;
    }

    /// <summary>Writes the plan from the case, as the open Chainsaw does.</summary>
    public BackgroundPlan WritePlan(ExitRoute route = ExitRoute.Windows, WatchAlerts alerts = WatchAlerts.Sound)
    {
        var described = App<IBackgroundWatches>().Describe(Case, Clock.UtcNow);
        return App<IBackgroundPlan>().Update(plan => plan.With(described) with
        {
            Enabled = true,
            Route = route,
            Alerts = alerts,
        });
    }

    public void Height(long height) => Http.Answer(
        Base + "blocks/tip/height",
        Encoding.ASCII.GetBytes(height.ToString(CultureInfo.InvariantCulture)),
        "text/plain");

    public void Dispose()
    {
        _watcher?.Dispose();
        Case.Dispose();
        _app.Dispose();
        if (Directory.Exists(AppData))
        {
            Directory.Delete(AppData, recursive: true);
        }
    }

    private static byte[] Recorded(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Recorded", name));
}

/// <summary>Records each time a watch's hit is said, and the ways.</summary>
internal sealed class RecordingAlerts : IAlerts
{
    public List<WatchAlerts> Raised { get; } = [];

    public void Raise(WatchAlerts ways) => Raised.Add(ways);
}

/// <summary>No websockets: the tests never reach the network, and the watching asks as before.</summary>
internal sealed class NoSockets : IExitWebSocket
{
    public Task<IExitSocket> ConnectAsync(Uri address, CancellationToken cancellationToken = default) =>
        throw new ExitNotConnectedException();
}
