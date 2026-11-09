using Chainsaw.Modules.Cases.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace Chainsaw.Modules.Cases.Tests;

/// <summary>
/// One operator's computer for the tests: a fake vault, purpose keys, profile and clock, and a temporary cases
/// folder that is removed afterwards.
/// </summary>
internal sealed class CaseFixture : IDisposable
{
    public const string Password = "ett långt delningslösenord";

    private readonly List<CaseStore> _stores = [];

    public CaseFixture(string vaultName = "vault-a", string? directory = null)
    {
        Directory = directory ?? Path.Combine(Path.GetTempPath(), "chainsaw-cases-" + Guid.NewGuid().ToString("N"));
        PurposeKeys = new FakePurposeKeys(Vault, vaultName);
        Profiles = new FakeProfiles(Vault);
        Secrets = new FakeSecrets(Vault);
    }

    public FakeSecrets Secrets { get; }

    public string Directory { get; }

    public string AnchorDirectory => Path.Combine(Directory, "anchors");

    public FakeVault Vault { get; } = new();

    public FakePurposeKeys PurposeKeys { get; }

    public FakeProfiles Profiles { get; }

    public FakeClock Clock { get; } = new();

    /// <summary>The time the cases save themselves by; it only moves when a test moves it.</summary>
    public FakeTimeProvider Time { get; } = new();

    /// <summary>The recovery key the stores get; none unless the test sets one.</summary>
    public RecoveryPublicKey? RecoveryKey { get; set; }

    /// <summary>The revoked recovery keys the stores get; none unless the test sets them.</summary>
    public IReadOnlySet<string> Revoked { get; set; } = new HashSet<string>();

    public static TimeZoneInfo TimeZone { get; } =
        TimeZoneInfo.CreateCustomTimeZone("Chainsaw/Test", TimeSpan.FromHours(1), "Test", "Test");

    /// <summary>Gets or sets the iterations of a new sharing envelope; below 1 000 it is weaker than accepted.</summary>
    public int SharingIterations { get; set; } = 1_000;

    public CaseStore CreateStore(params ICaseSchemaContributor[] contributors)
    {
        var options = new CasesOptions
        {
            Directory = Directory,
            AnchorDirectory = AnchorDirectory,
            SharingIterations = SharingIterations,
            MinimumSharingIterations = 1_000,
            TimeZone = TimeZone,
            Time = Time,
            RecoveryKey = RecoveryKey,
            RevokedRecoveryKeys = Revoked,
        };
        var store = new CaseStore(
            Vault, PurposeKeys, Profiles, Secrets, Clock, options, [new CasesSchema(), .. contributors]);
        _stores.Add(store);
        return store;
    }

    public string CaseFile(string caseNumber) => Path.Combine(Directory, caseNumber + ".cut");

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
