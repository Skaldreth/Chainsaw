using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases.Tests;

/// <summary>
/// The case's envelopes as sketch ÄR1b shows them, and a colleague's file taken in with the case password.
/// </summary>
public sealed class EnvelopeTests : IDisposable
{
    private readonly CaseFixture _owner = new();

    private readonly CaseFixture _colleague;

    private readonly ECDiffieHellman _recoveryKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);

    private readonly ECDiffieHellman _otherRecoveryKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);

    public EnvelopeTests()
    {
        _owner.RecoveryKey = RecoveryPublicKey.FromPrivateKey(_recoveryKey);
        _colleague = new CaseFixture("vault-b");
        _colleague.Profiles.Profile = new("Kollega Kollegasson", "Analytiker", "Andra enheten");
        _colleague.RecoveryKey = _owner.RecoveryKey;
    }

    public void Dispose()
    {
        _owner.Dispose();
        _colleague.Dispose();
        _recoveryKey.Dispose();
        _otherRecoveryKey.Dispose();
    }

    [Fact]
    public void A_new_case_has_the_operators_envelope_and_the_organisations()
    {
        using var created = _owner.CreateStore().Create("A1");

        var envelopes = created.DescribeEnvelopes();

        Assert.Equal([CaseEnvelopeKind.Yours, CaseEnvelopeKind.Recovery], envelopes.Select(item => item.Kind));
        var recovery = envelopes[1];
        Assert.Equal(_owner.RecoveryKey!.Fingerprint, recovery.Fingerprint);
        Assert.Equal(RecoveryEnvelopeKey.BuiltIn, recovery.Recovery);
    }

    [Fact]
    public void Another_vaults_envelope_is_not_described_as_yours()
    {
        var path = CreateShared();

        var seen = _colleague.CreateStore().DescribeEnvelopes(path);

        Assert.Equal(
            [CaseEnvelopeKind.OtherVault, CaseEnvelopeKind.Recovery, CaseEnvelopeKind.SharingPassword],
            seen.Select(item => item.Kind));
        Assert.Equal(
            [CaseEnvelopeKind.Yours, CaseEnvelopeKind.Recovery, CaseEnvelopeKind.SharingPassword],
            _owner.CreateStore().DescribeEnvelopes(path).Select(item => item.Kind));
    }

    [Fact]
    public void Describing_the_envelopes_of_a_file_needs_an_unlocked_vault()
    {
        var path = CreateShared();
        var store = _colleague.CreateStore();
        _colleague.Vault.Lock();

        var error = Assert.Throws<CaseException>(() => store.DescribeEnvelopes(path));

        Assert.Equal(CaseError.VaultLocked, error.Error);
    }

    [Fact]
    public void A_colleagues_file_is_taken_into_the_folder_and_opened_with_the_case_password()
    {
        var source = CreateShared();
        var before = File.ReadAllBytes(source);

        using (var opened = _colleague.CreateStore().ImportWithPassword(source, CaseFixture.Password))
        {
            Assert.Equal(_colleague.CaseFile("A1"), opened.Path);
            var last = opened.ReadJournal()[^1];
            Assert.Equal(CaseJournalKinds.OpenedWithPassword, last.Kind);
            Assert.Equal("Kollega Kollegasson", last.Operator.Name);
            Assert.DoesNotContain(opened.DescribeEnvelopes(), item => item.Kind == CaseEnvelopeKind.Yours);
        }

        Assert.Equal(before, File.ReadAllBytes(source));
        Assert.True(File.Exists(_colleague.CaseFile("A1")));
        Assert.Equal(
            CaseError.NoAccess,
            Assert.Throws<CaseException>(() => _colleague.CreateStore().Open("A1")).Error);
    }

    [Fact]
    public void An_own_envelope_opens_the_file_with_the_master_key_also_after_the_password_is_changed()
    {
        var source = CreateShared();
        using (var opened = _colleague.CreateStore().ImportWithPassword(source, CaseFixture.Password))
        {
            opened.AddOperatorAccess();
            Assert.Equal(CaseJournalKinds.OperatorAccessAdded, opened.ReadJournal()[^1].Kind);
            opened.SetSharingPassword("ett helt annat långt lösenord");
        }

        using var again = _colleague.CreateStore().Open("A1");

        var kinds = again.DescribeEnvelopes().Select(item => item.Kind).ToList();
        Assert.Equal(
            [
                CaseEnvelopeKind.OtherVault, CaseEnvelopeKind.Recovery, CaseEnvelopeKind.Yours,
                CaseEnvelopeKind.SharingPassword,
            ],
            kinds);
        Assert.Contains(again.ReadJournal(), entry => entry.Kind == CaseJournalKinds.SharingPasswordSet
            && entry.Summary == "Delningslösenordet byttes");
    }

    [Fact]
    public void Taking_in_a_file_journals_its_name_and_the_SHA256_of_what_was_copied()
    {
        var source = CreateShared();
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source)));

        using var opened = _colleague.CreateStore().ImportWithPassword(source, CaseFixture.Password);

        var entry = opened.ReadJournal().Last(item => item.Kind == CaseJournalKinds.OpenedWithPassword);
        using var details = System.Text.Json.JsonDocument.Parse(entry.Details!);
        Assert.Equal("A1.cut", details.RootElement.GetProperty("importedFrom").GetString());
        Assert.Equal(sha256, details.RootElement.GetProperty("importedSha256").GetString());
        Assert.False(File.Exists(_colleague.CaseFile("A1") + ".tmp"));
    }

    [Fact]
    public void A_case_already_in_the_folder_under_another_name_is_not_taken_in_again()
    {
        var source = CreateShared();
        Directory.CreateDirectory(_colleague.Directory);
        File.Copy(source, _colleague.CaseFile("A7"));

        var error = Assert.Throws<CaseException>(
            () => _colleague.CreateStore().ImportWithPassword(source, CaseFixture.Password));

        Assert.Equal(CaseError.AlreadyExists, error.Error);
        Assert.False(File.Exists(_colleague.CaseFile("A1")));
    }

    [Fact]
    public void A_copy_whose_database_is_damaged_is_removed_again()
    {
        var source = CreateShared();
        var bytes = File.ReadAllBytes(source);
        bytes.AsSpan(bytes.Length - 2048, 1024).Fill(0x5A);
        var damaged = Path.Combine(_owner.Directory, "elsewhere", "A1.cut");
        Directory.CreateDirectory(Path.GetDirectoryName(damaged)!);
        File.WriteAllBytes(damaged, bytes);

        var error = Assert.Throws<CaseException>(
            () => _colleague.CreateStore().ImportWithPassword(damaged, CaseFixture.Password));

        Assert.Equal(CaseError.Corrupt, error.Error);
        Assert.Empty(Directory.Exists(_colleague.Directory) ? Directory.GetFiles(_colleague.Directory, "A1*") : []);
        Assert.Equal(bytes, File.ReadAllBytes(damaged));
    }

    [Fact]
    public void A_wrong_case_password_copies_nothing()
    {
        var source = CreateShared();

        var error = Assert.Throws<CaseException>(
            () => _colleague.CreateStore().ImportWithPassword(source, "fel lösenord, men långt nog"));

        Assert.Equal(CaseError.NoAccess, error.Error);
        Assert.False(File.Exists(_colleague.CaseFile("A1")));
        Assert.False(File.Exists(_colleague.CaseFile("A1") + ".work"));
    }

    [Fact]
    public void A_colleagues_file_is_never_copied_over_a_case_with_the_same_number()
    {
        var source = CreateShared();
        using (_colleague.CreateStore().Create("A1"))
        {
        }

        var mine = File.ReadAllBytes(_colleague.CaseFile("A1"));

        var error = Assert.Throws<CaseException>(
            () => _colleague.CreateStore().ImportWithPassword(source, CaseFixture.Password));

        Assert.Equal(CaseError.AlreadyExists, error.Error);
        Assert.Equal(mine, File.ReadAllBytes(_colleague.CaseFile("A1")));
    }

    [Fact]
    public void A_file_already_in_the_folder_is_opened_where_it_lies()
    {
        var source = CreateShared();
        Directory.CreateDirectory(_colleague.Directory);
        File.Copy(source, _colleague.CaseFile("A1"));

        using var opened = _colleague.CreateStore().ImportWithPassword(_colleague.CaseFile("A1"), CaseFixture.Password);

        Assert.Equal(_colleague.CaseFile("A1"), opened.Path);
        Assert.Single(Directory.GetFiles(_colleague.Directory, "*.cut"));
    }

    [Fact]
    public void A_file_whose_name_is_no_case_number_is_refused()
    {
        var source = CreateShared();
        var odd = Path.Combine(_owner.Directory, "CON.cut");
        File.Copy(source, odd);

        var error = Assert.Throws<CaseException>(
            () => _colleague.CreateStore().ImportWithPassword(odd, CaseFixture.Password));

        Assert.Equal(CaseError.InvalidCaseNumber, error.Error);
    }

    [Fact]
    public void Envelopes_for_other_keys_and_of_unknown_kinds_are_described_and_never_removed()
    {
        using (var created = _owner.CreateStore().Create("A1"))
        {
            created.SetSharingPassword(CaseFixture.Password);
        }

        var unknown = new JsonObject { ["kind"] = "framtida", ["data"] = "AAAA" };
        var other = new JsonObject { ["kind"] = "recovery", ["alg"] = "ett-senare-format", ["key"] = "AAAA" };
        AddEnvelopes(_owner.CaseFile("A1"), unknown, other);
        _owner.RecoveryKey = RecoveryPublicKey.FromPrivateKey(_otherRecoveryKey);

        using var opened = _owner.CreateStore().Open("A1");
        opened.RemoveSharingPassword();
        opened.SetSharingPassword("ett nytt långt delningslösenord");

        var envelopes = opened.DescribeEnvelopes();
        Assert.Equal(2, envelopes.Count(item => item.Kind == CaseEnvelopeKind.Unknown));
        var recovery = envelopes.Where(item => item.Kind == CaseEnvelopeKind.Recovery).ToList();
        Assert.Equal(2, recovery.Count);
        Assert.Contains(recovery, item => item.Recovery == RecoveryEnvelopeKey.Other
            && item.Fingerprint == RecoveryPublicKey.FromPrivateKey(_recoveryKey).Fingerprint);
        Assert.Contains(recovery, item => item.Recovery == RecoveryEnvelopeKey.BuiltIn);
        Assert.Single(envelopes, item => item.Kind == CaseEnvelopeKind.SharingPassword);
    }

    [Fact]
    public void A_revoked_recovery_key_is_described_as_revoked_before_it_is_removed()
    {
        var path = CreateShared();
        var fingerprint = _owner.RecoveryKey!.Fingerprint;
        _colleague.Revoked = new HashSet<string> { fingerprint };

        var seen = _colleague.CreateStore().DescribeEnvelopes(path);

        Assert.Contains(seen, item => item.Fingerprint == fingerprint && item.Recovery == RecoveryEnvelopeKey.Revoked);
    }

    [Fact]
    public void Every_change_of_the_envelopes_is_journalled()
    {
        var source = CreateShared();
        using var opened = _colleague.CreateStore().ImportWithPassword(source, CaseFixture.Password);

        opened.AddOperatorAccess();
        opened.SetSharingPassword("ett annat långt delningslösenord");
        opened.RemoveSharingPassword();

        var kinds = opened.ReadJournal().Select(entry => entry.Kind).ToList();
        Assert.Contains(CaseJournalKinds.OperatorAccessAdded, kinds);
        Assert.Contains(CaseJournalKinds.SharingPasswordSet, kinds);
        Assert.Equal(CaseJournalKinds.SharingPasswordRemoved, kinds[^1]);
        Assert.DoesNotContain(opened.DescribeEnvelopes(), item => item.Kind == CaseEnvelopeKind.SharingPassword);
    }

    [Fact]
    public void Describing_a_closed_case_is_refused()
    {
        var created = _owner.CreateStore().Create("A1");
        created.Close();

        Assert.Equal(CaseError.Closed, Assert.Throws<CaseException>(() => created.DescribeEnvelopes()).Error);
    }

    /// <summary>Creates the owner's case A1 with a sharing password and gives its path.</summary>
    private string CreateShared()
    {
        using (var created = _owner.CreateStore().Create("A1"))
        {
            created.SetSharingPassword(CaseFixture.Password);
        }

        return _owner.CaseFile("A1");
    }

    /// <summary>Adds envelopes to a closed case file's header, as a later or another program with the key could.</summary>
    private void AddEnvelopes(string path, params JsonObject[] envelopes) =>
        CaseFileSurgery.RewriteHeader(
            _owner, path, header => header.WithEnvelopes(header.Envelopes.Concat(envelopes)));
}
