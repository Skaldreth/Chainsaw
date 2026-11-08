using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Requests.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Requests.Tests;

/// <summary>
/// A real, encrypted case in a temporary folder with the Requests module's table, and a folder for the exported
/// files. Nothing goes on the network.
/// </summary>
internal sealed class Harness : IDisposable
{
    public const string CaseNumber = "2026-0117";

    private readonly ServiceProvider _provider;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "chainsaw-requests-" + Guid.NewGuid().ToString("N"));

    public Harness()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IVault, FakeVault>();
        services.AddSingleton<IPurposeKeys, FakePurposeKeys>();
        services.AddSingleton<IOperatorProfileStore>(Profiles);
        services.AddSingleton<IClock>(Clock);
        services.AddCasesModule(options =>
        {
            options.Directory = Path.Combine(_directory, "cases");
            options.AnchorDirectory = Path.Combine(_directory, "anchors");
        });
        services.AddRequestsModule();
        _provider = services.BuildServiceProvider();
        Store = _provider.GetRequiredService<ICaseStore>();
        Case = Store.Create(CaseNumber);
        Service = _provider.GetRequiredService<IRequestService>();
        Reports = _provider.GetRequiredService<IReportService>();
        Exports = Path.Combine(_directory, "export");
        Directory.CreateDirectory(Exports);
    }

    public FakeProfiles Profiles { get; } = new();

    public FakeClock Clock { get; } = new();

    public ICaseStore Store { get; }

    public ICase Case { get; private set; }

    public IRequestService Service { get; }

    public IReportService Reports { get; }

    /// <summary>The folder the tests export to.</summary>
    public string Exports { get; }

    public CaseVerification Verify() => _provider.GetRequiredService<ICaseVerifier>().Verify(Case);

    public void Reopen()
    {
        Case.Close();
        Case = Store.Open(CaseNumber);
    }

    public IReadOnlyList<JournalEntry> Journal(string kind) =>
        [.. Case.ReadJournal().Where(entry => entry.Kind == kind)];

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

internal sealed class FakeVault : IVault
{
    public VaultState State { get; set; } = VaultState.Unlocked;

    public event EventHandler<VaultState>? StateChanged
    {
        add { }
        remove { }
    }

    public void Create(ReadOnlySpan<char> masterKey) => throw new NotSupportedException();

    public void Unlock(ReadOnlySpan<char> masterKey) => throw new NotSupportedException();

    public void Lock() => throw new NotSupportedException();

    public void ChangeMasterKey(ReadOnlySpan<char> currentMasterKey, ReadOnlySpan<char> newMasterKey) =>
        throw new NotSupportedException();
}

internal sealed class FakePurposeKeys : IPurposeKeys
{
    public void DeriveKey(string purpose, Span<byte> destination)
    {
        var secret = SHA256.HashData(Encoding.UTF8.GetBytes("requests-tests"));
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, destination, [], Encoding.ASCII.GetBytes(purpose));
    }
}

internal sealed class FakeProfiles : IOperatorProfileStore
{
    public OperatorProfile? Profile { get; set; } = new("Testa Testsson", "Utredare", "Enheten");

    public bool IsLocked { get; set; }

    public OperatorProfile? Get() => IsLocked ? throw new VaultException(VaultError.Locked) : Profile;

    public void Save(OperatorProfile profile) => Profile = profile;
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 7, 13, 20, 0, TimeSpan.Zero);
}

/// <summary>Synthetic recipients and deposits; no real address, transaction or exchange.</summary>
internal static class Samples
{
    public static readonly ChainAddress Deposit = new(Chain.Bitcoin, "bc1qtestdeposit0000000000000000000000aaaa");

    public static readonly ChainAddress Holder = new(Chain.Ethereum, "0x1111111111111111111111111111111111111111");

    public static readonly Asset Usdt =
        Asset.Token(Chain.Ethereum, "0x2222222222222222222222222222222222222222", "USDT", 6);

    /// <summary>A zone with a fixed offset of one hour, so that the local times do not depend on the computer.</summary>
    public static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");

    public static RequestTarget Exchange(string name = "Börs A") =>
        new(name, RequestTargetKind.Exchange, Chain.Bitcoin, [Deposit]);

    public static RequestDeposit Bitcoin(string tx, long sats, int day, bool traced = true, long? height = 921_140) =>
        new(tx, Deposit, new AssetAmount(Asset.Bitcoin, sats), new DateTimeOffset(2026, 10, day, 20, 14, 5, TimeSpan.Zero),
            height, height is null ? null : new string('b', 64), traced);

    public static RequestDraft Information(
        RequestLanguage language = RequestLanguage.Swedish,
        RequestTrail? trail = null,
        string name = "Börs A",
        IReadOnlyList<RequestDeposit>? deposits = null,
        RequestKind kind = RequestKind.Information) =>
        new(
            kind,
            language,
            Exchange(name),
            deposits ?? [Bitcoin(new string('9', 64), 60_000_000, 28), Bitcoin(new string('a', 64), 21_500_000, 30)],
            trail,
            Zone);

    public static RequestTrail Trail() => new(["bc1q…a8f3", "bc1q…4tz9", "bc1q…x0wl", "Börs A"], "FIFO v1");
}
