using Chainsaw.Modules.Cases.Contracts;

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
    }

    public string Directory { get; }

    public string AnchorDirectory => Path.Combine(Directory, "anchors");

    public FakeVault Vault { get; } = new();

    public FakePurposeKeys PurposeKeys { get; }

    public FakeProfiles Profiles { get; }

    public FakeClock Clock { get; } = new();

    /// <summary>The recovery key the stores get; none unless the test sets one.</summary>
    public RecoveryPublicKey? RecoveryKey { get; set; }

    public static TimeZoneInfo TimeZone { get; } =
        TimeZoneInfo.CreateCustomTimeZone("Chainsaw/Test", TimeSpan.FromHours(1), "Test", "Test");

    public CaseStore CreateStore(params ICaseSchemaContributor[] contributors)
    {
        var options = new CasesOptions
        {
            Directory = Directory,
            AnchorDirectory = AnchorDirectory,
            SharingIterations = 1_000,
            TimeZone = TimeZone,
            RecoveryKey = RecoveryKey,
        };
        var store = new CaseStore(
            Vault, PurposeKeys, Profiles, Clock, options, [new CasesSchema(), .. contributors]);
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
