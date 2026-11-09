using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Chains;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Monitoring.Tests;

/// <summary>
/// A real, encrypted case in a temporary folder with the Clustering and Monitoring modules' tables, and synthetic
/// sources for both chains. Nothing goes on the network.
/// </summary>
internal sealed class Harness : IDisposable
{
    public const string CaseNumber = "2026-0262";

    private readonly ServiceProvider _provider;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "chainsaw-monitoring-" + Guid.NewGuid().ToString("N"));

    public Harness()
    {
        var services = new ServiceCollection();

        // The chains' tables and the transfer of raw responses; the synthetic sources below take the sources' place.
        services.AddChainsModule();
        services.AddSingleton<IVault, FakeVault>();
        services.AddSingleton<IPurposeKeys, FakePurposeKeys>();
        services.AddSingleton<IOperatorProfileStore>(Profiles);
        services.AddSingleton<IVaultSecrets>(new TestVaultSecrets());
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<IWatchGate>(Gate);
        services.AddSingleton<IBitcoinSource>(Bitcoin);
        services.AddSingleton<IEthereumSource>(Ethereum);
        services.AddSingleton<ITronSource>(Tron);
        services.AddSingleton<ILitecoinSource>(Litecoin);
        services.AddSingleton<IZcashSource>(Zcash);
        services.AddSingleton<IBscSource>(Bsc);
        services.AddSingleton<ISolanaSource>(Solana);
        services.AddCasesModule(options =>
        {
            options.Directory = _directory;
            options.AnchorDirectory = Path.Combine(_directory, "anchors");
        });
        services.AddClusteringModule();
        services.AddMonitoringModule();
        services.AddSingleton<IUserDataProtector>(Protector);
        services.AddBackgroundWatchFiles(Path.Combine(_directory, "Bevakning"));
        _provider = services.BuildServiceProvider();
        Store = _provider.GetRequiredService<ICaseStore>();
        Case = Store.Create(CaseNumber);
        Service = _provider.GetRequiredService<IMonitoringService>();
        Clustering = _provider.GetRequiredService<IClusteringService>();
    }

    public FakeProfiles Profiles { get; } = new();

    public FakeProtector Protector { get; } = new();

    public string BackgroundDirectory => Path.Combine(_directory, "Bevakning");

    public T Get<T>()
        where T : notnull => _provider.GetRequiredService<T>();

    /// <summary>
    /// Seals an answer of the case's own lookups that shows the Bitcoin chain at a height, as far as the background's
    /// read-through height may then count.
    /// </summary>
    public void SeenByTheCase(long height, DateTimeOffset? received = null) => Case.Write(transaction =>
        Get<IResponseTransfer>().Seal(
            transaction,
            Responses.Make(1, height.ToString(System.Globalization.CultureInfo.InvariantCulture), received),
            new SealedResponseIndex(height, [])));

    public FakeClock Clock { get; } = new();

    public FakeGate Gate { get; } = new();

    public FakeBitcoinSource Bitcoin { get; } = new();

    public FakeEthereumSource Ethereum { get; } = new();

    public FakeTronSource Tron { get; } = new();

    public FakeLitecoinSource Litecoin { get; } = new();

    /// <summary>Gets Zcash's fake source.</summary>
    public FakeZcashSource Zcash { get; } = new();

    public FakeBscSource Bsc { get; } = new();

    public FakeSolanaSource Solana { get; } = new();

    public ICaseStore Store { get; }

    public ICase Case { get; private set; }

    public IMonitoringService Service { get; }

    public IClusteringService Clustering { get; }

    public CaseVerification Verify() => _provider.GetRequiredService<ICaseVerifier>().Verify(Case);

    public void Reopen()
    {
        Case.Close();
        Case = Store.Open(CaseNumber);
    }

    public IReadOnlyList<JournalEntry> Journal(string kind) =>
        [.. Case.ReadJournal().Where(entry => entry.Kind == kind)];

    /// <summary>A watch on one Bitcoin address.</summary>
    public Watch WatchBitcoin(
        BitcoinAddress address, WatchDirection direction = WatchDirection.Both,
        IReadOnlyList<ChainAddress>? receivers = null, IReadOnlyList<ChainAddress>? senders = null,
        AssetAmount? minimum = null) =>
        Service.Add(
            Case,
            new WatchSubject(WatchSubjectKind.Address, new ChainAddress(Chain.Bitcoin, address.Value), "Bevakad"),
            new WatchFilter(Chain.Bitcoin, direction, receivers ?? [], senders ?? [], null, minimum));

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
