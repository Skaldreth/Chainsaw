using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// A real, encrypted case in a temporary folder with the Cases, Clustering and Tracing modules, a synthetic chain
/// behind a fake source, and hand-made labels.
/// </summary>
internal sealed class CaseHarness : IDisposable
{
    public const string CaseNumber = "2026-0077";

    private readonly ServiceProvider _provider;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "chainsaw-tracing-" + Guid.NewGuid().ToString("N"));

    public CaseHarness(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IVault, FakeVault>();
        services.AddSingleton<IPurposeKeys, FakePurposeKeys>();
        services.AddSingleton<IOperatorProfileStore>(Profiles);
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<IBitcoinSource>(Source);
        services.AddSingleton<IEthereumSource>(Ethereum);
        services.AddCasesModule(options =>
        {
            options.Directory = _directory;
            options.AnchorDirectory = Path.Combine(_directory, "anchors");
            options.TimeZone = TimeZone;
        });
        services.AddClusteringModule();
        services.AddTracingModule();
        services.AddSingleton<ITrailEndClassifier>(Labels);
        configure?.Invoke(services);
        _provider = services.BuildServiceProvider();
        Store = _provider.GetRequiredService<ICaseStore>();
        Case = Store.Create(CaseNumber);
        Service = _provider.GetRequiredService<ITracingService>();
    }

    /// <summary>
    /// The journal's time zone, fixed so that the facit does not depend on the computer's zone (Windows in Sweden
    /// wrote +01:00 where Linux in UTC wrote +00:00, and the zone's name differs between the systems).
    /// </summary>
    public static TimeZoneInfo TimeZone { get; } =
        TimeZoneInfo.CreateCustomTimeZone("Chainsaw/Test", TimeSpan.Zero, "Test", "Test");

    public FakeProfiles Profiles { get; } = new();

    public FakeClock Clock { get; } = new();

    public FakeSource Source { get; } = new();

    public FakeEthereumSource Ethereum { get; } = new();

    public FakeLabels Labels { get; } = new();

    public ICaseStore Store { get; }

    public ICase Case { get; private set; }

    public ITracingService Service { get; }

    public IGraphCommentService Comments => _provider.GetRequiredService<IGraphCommentService>();

    public Task<TraceGraph> Load() => Service.LoadAsync(Case);

    public async Task<ExpansionResult> Expand(BitcoinTransaction transaction) =>
        await Service.ExpandAsync(Case, ExpansionRequest.ForTransaction(transaction.Txid));

    public async Task<ExpansionResult> Expand(BitcoinAddress address, int maximum = 50) =>
        await Service.ExpandAsync(Case, ExpansionRequest.ForAddress(address, maximum));

    public CaseVerification Verify() => _provider.GetRequiredService<ICaseVerifier>().Verify(Case);

    public void Reopen()
    {
        Case.Close();
        Case = Store.Open(CaseNumber);
    }

    public void Dispose()
    {
        Case.Dispose();
        _provider.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
