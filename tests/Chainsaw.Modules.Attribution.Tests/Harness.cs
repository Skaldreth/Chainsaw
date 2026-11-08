using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Clustering;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Attribution.Tests;

/// <summary>
/// Real, encrypted cases in a temporary folder with the Cases, Clustering and Attribution modules; the vault, the
/// operator and the clock are fakes the test controls.
/// </summary>
internal sealed class Harness : IDisposable
{
    public const string CaseNumber = "2026-0101";

    private readonly ServiceProvider _provider;

    public Harness()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IVault, FakeVault>();
        services.AddSingleton<IPurposeKeys>(Keys);
        services.AddSingleton<IOperatorProfileStore>(Profiles);
        services.AddSingleton<IVaultSecrets>(new TestVaultSecrets());
        services.AddSingleton<IClock>(Clock);
        Http = new FakeExitHttp(Clock);
        services.AddSingleton<IExitHttp>(Http);
        services.AddSingleton<IApiKeyStore>(ApiKeys);
        services.AddCasesModule(options =>
        {
            options.Directory = System.IO.Path.Combine(Directory, "cases");
            options.AnchorDirectory = System.IO.Path.Combine(Directory, "anchors");
        });
        services.AddClusteringModule();
        services.AddAttributionModule(options => options.Directory = Directory);
        _provider = services.BuildServiceProvider();
        Store = _provider.GetRequiredService<ICaseStore>();
        Case = Store.Create(CaseNumber);
    }

    public string Directory { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chainsaw-attribution-" + Guid.NewGuid().ToString("N"));

    public FakeProfiles Profiles { get; } = new();

    public FakeClock Clock { get; } = new();

    public FakePurposeKeys Keys { get; } = new();

    public FakeExitHttp Http { get; }

    public FakeApiKeys ApiKeys { get; } = new();

    public IAttributionProviders Providers => _provider.GetRequiredService<IAttributionProviders>();

    public ICaseStore Store { get; }

    public ICase Case { get; private set; }

    public IAttributionService Labels => _provider.GetRequiredService<IAttributionService>();

    public ISanctionLists Sanctions => _provider.GetRequiredService<ISanctionLists>();

    public IKnownContractLabels KnownContracts => _provider.GetRequiredService<IKnownContractLabels>();

    public ILabelLibrary Library => _provider.GetRequiredService<ILabelLibrary>();

    public IClusteringService Clustering => _provider.GetRequiredService<IClusteringService>();

    public ITrailEndClassifier Classifier =>
        _provider.GetServices<ITrailEndClassifier>().Single(classifier => classifier is LabelTrailEnds);

    public CaseVerification Verify(ICase? @case = null) =>
        _provider.GetRequiredService<ICaseVerifier>().Verify(@case ?? Case);

    public ICase Create(string caseNumber) => Store.Create(caseNumber);

    public void Reopen()
    {
        Case.Close();
        Case = Store.Open(CaseNumber);
    }

    public LabelEntry Own(
        LabelSubject subject,
        LabelKind kind,
        string? name = null,
        string reason = "Insättningsadress enligt börsens svar",
        string? note = null,
        bool doNotSpread = false,
        ICase? @case = null) =>
        Labels.Add(@case ?? Case, new LabelDraft(subject, kind, name, reason, note, doNotSpread));

    public void Dispose()
    {
        Case.Dispose();
        _provider.Dispose();
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
