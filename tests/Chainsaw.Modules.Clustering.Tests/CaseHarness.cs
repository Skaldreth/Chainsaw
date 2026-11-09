using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Clustering.Tests;

/// <summary>A real, encrypted case in a temporary folder with the Clustering module's tables.</summary>
internal sealed class CaseHarness : IDisposable
{
    public const string CaseNumber = "2026-0066";

    private readonly ServiceProvider _provider;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "chainsaw-clustering-" + Guid.NewGuid().ToString("N"));

    public CaseHarness(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IVault, FakeVault>();
        services.AddSingleton<IPurposeKeys, FakePurposeKeys>();
        services.AddSingleton<IOperatorProfileStore>(Profiles);
        services.AddSingleton<IVaultSecrets>(new TestVaultSecrets());
        services.AddSingleton<IClock>(new FakeClock());
        services.AddCasesModule(options =>
        {
            options.Directory = _directory;
            options.AnchorDirectory = Path.Combine(_directory, "anchors");
        });
        services.AddClusteringModule();
        configure?.Invoke(services);
        _provider = services.BuildServiceProvider();
        Store = _provider.GetRequiredService<ICaseStore>();
        Case = Store.Create(CaseNumber);
        Service = _provider.GetRequiredService<IClusteringService>();
        Accounts = _provider.GetRequiredService<IAccountClustering>();
    }

    public IAccountClustering Accounts { get; }

    public FakeProfiles Profiles { get; } = new();

    public ICaseStore Store { get; }

    public ICase Case { get; private set; }

    public IClusteringService Service { get; }

    public ClusteringRun Analyse(params BitcoinTransaction[] transactions) => Service.Analyse(Case, transactions);

    public Cluster ClusterOf(BitcoinAddress address) => Service.GetCluster(Case, address);

    public CaseVerification Verify() => _provider.GetRequiredService<ICaseVerifier>().Verify(Case);

    public void Reopen()
    {
        Case.Close();
        Case = Store.Open(CaseNumber);
    }

    public long Count(string table) => Case.Read(reader =>
    {
        using var command = reader.CreateCommand($"SELECT COUNT(*) FROM {table}");
        return (long)command.ExecuteScalar()!;
    });

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
