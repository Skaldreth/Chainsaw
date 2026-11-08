using System.Security.Cryptography;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases.Tests;

/// <summary>Replacing a case's file key (sketch VT1a): who keeps access, what is journalled and what a crash leaves.</summary>
public sealed class FileKeyTests : IDisposable
{
    private const string NewPassword = "ett helt nytt ärendelösenord";

    private readonly CaseFixture _owner = new();

    private readonly CaseFixture _colleague = new("vault-b");

    private readonly ECDiffieHellman _recoveryKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);

    private readonly List<CaseFixture> _pictures = [];

    public FileKeyTests()
    {
        _owner.RecoveryKey = RecoveryPublicKey.FromPrivateKey(_recoveryKey);
        _colleague.Profiles.Profile = new("Maria Lind", "Analytiker", "Andra enheten");
    }

    public void Dispose()
    {
        _owner.Dispose();
        _colleague.Dispose();
        _recoveryKey.Dispose();
        foreach (var picture in _pictures)
        {
            picture.Dispose();
        }
    }

    [Fact]
    public void The_colleague_and_the_case_password_lose_access_and_the_owner_and_the_organisation_keep_it()
    {
        var path = CreateSharedWithColleague();

        FileKeyReplacement replaced;
        using (var opened = _owner.CreateStore().Open("A1"))
        {
            replaced = opened.ReplaceFileKey(FakeVault.MasterKey, default);
            Assert.False(opened.HasSharingPassword);
            var kinds = opened.DescribeEnvelopes().Select(envelope => envelope.Kind).ToList();
            Assert.Equal([CaseEnvelopeKind.Yours, CaseEnvelopeKind.Recovery], kinds);
        }

        Assert.Contains("Maria Lind", replaced.Dropped);
        Assert.Contains("ärendelösenordet", replaced.Dropped);
        Assert.False(replaced.SharingPasswordCreated);

        var byPassword = Assert.Throws<CaseException>(
            () => _colleague.CreateStore().OpenWithPassword(path, CaseFixture.Password));
        Assert.Equal(CaseError.NoAccess, byPassword.Error);

        using (var mine = _owner.CreateStore().Open("A1"))
        {
            var line = mine.ReadJournal().Single(entry => entry.Kind == CaseJournalKinds.FileKeyReplaced);
            Assert.Equal(replaced.Sequence, line.Sequence);
            Assert.Contains("Kopior av filen som redan lämnats ut påverkas inte", line.Summary);
            Assert.True(new CaseVerifier(_owner.Clock).Verify(mine).IsIntact);
        }

        using var administrator = new CaseFixture("vault-admin");
        using var recovered = administrator.CreateStore().OpenWithRecoveryKey(path, _recoveryKey, "Admin Adminsson");
        Assert.Contains(recovered.ReadJournal(), entry => entry.Kind == CaseJournalKinds.FileKeyReplaced);
    }

    [Fact]
    public void The_colleagues_own_vault_no_longer_opens_the_file()
    {
        var path = CreateSharedWithColleague();
        File.Copy(path, Path.Combine(Directory.CreateDirectory(_colleague.Directory).FullName, "A1.cut"));
        using (var check = _colleague.CreateStore().Open("A1"))
        {
            Assert.Equal("A1", check.Info.CaseNumber);
        }

        File.Delete(Path.Combine(_colleague.Directory, "A1.cut"));
        using (var opened = _owner.CreateStore().Open("A1"))
        {
            opened.ReplaceFileKey(FakeVault.MasterKey, default);
        }

        File.Copy(path, Path.Combine(_colleague.Directory, "A1.cut"));
        var error = Assert.Throws<CaseException>(() => _colleague.CreateStore().Open("A1"));
        Assert.Equal(CaseError.NoAccess, error.Error);
    }

    [Fact]
    public void A_copy_handed_out_before_still_opens_with_the_old_password()
    {
        var path = CreateSharedWithColleague();
        var handedOut = Path.Combine(Directory.CreateDirectory(_colleague.Directory).FullName, "utlamnad.cut");
        File.Copy(path, handedOut);
        using (var opened = _owner.CreateStore().Open("A1"))
        {
            opened.ReplaceFileKey(FakeVault.MasterKey, default);
        }

        using var old = _colleague.CreateStore().OpenWithPassword(handedOut, CaseFixture.Password);
        Assert.DoesNotContain(old.ReadJournal(), entry => entry.Kind == CaseJournalKinds.FileKeyReplaced);
    }

    [Fact]
    public void A_new_case_password_made_in_the_same_step_opens_the_file_and_the_old_does_not()
    {
        var path = CreateSharedWithColleague();
        using (var opened = _owner.CreateStore().Open("A1"))
        {
            var replaced = opened.ReplaceFileKey(FakeVault.MasterKey, NewPassword);
            Assert.True(replaced.SharingPasswordCreated);
            Assert.True(opened.HasSharingPassword);
        }

        Assert.Equal(
            CaseError.NoAccess,
            Assert.Throws<CaseException>(() => _colleague.CreateStore().OpenWithPassword(path, CaseFixture.Password))
                .Error);
        using var colleagues = _colleague.CreateStore().OpenWithPassword(path, NewPassword);
        Assert.Equal("A1", colleagues.Info.CaseNumber);
    }

    [Fact]
    public void A_wrong_master_key_changes_nothing()
    {
        var path = CreateSharedWithColleague();
        var before = File.ReadAllBytes(path);
        using (var opened = _owner.CreateStore().Open("A1"))
        {
            var error = Assert.Throws<CaseException>(() => opened.ReplaceFileKey("fel huvudnyckel, men lång", default));
            Assert.Equal(CaseError.WrongMasterKey, error.Error);
            Assert.True(opened.HasSharingPassword);
        }

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_vault_that_cannot_be_read_is_the_cases_own_error_and_changes_nothing()
    {
        var path = CreateSharedWithColleague();
        var before = File.ReadAllBytes(path);
        using (var opened = _owner.CreateStore().Open("A1"))
        {
            _owner.Vault.VerifyFailure = new Security.Contracts.VaultException(
                Security.Contracts.VaultError.Corrupt);

            var error = Assert.Throws<CaseException>(() => opened.ReplaceFileKey(FakeVault.MasterKey, default));

            Assert.Equal(CaseError.VaultFailed, error.Error);
        }

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_short_new_password_is_refused_before_anything_else()
    {
        CreateSharedWithColleague();
        using var opened = _owner.CreateStore().Open("A1");

        var error = Assert.Throws<CaseException>(() => opened.ReplaceFileKey(FakeVault.MasterKey, "kort"));

        Assert.Equal(CaseError.PasswordTooShort, error.Error);
        Assert.True(opened.HasSharingPassword);
    }

    [Fact]
    public void A_case_opened_with_the_password_alone_cannot_replace_the_key()
    {
        var path = CreateSharedWithColleague(addColleaguesEnvelope: false);
        using var opened = _colleague.CreateStore().OpenWithPassword(path, CaseFixture.Password);

        var error = Assert.Throws<CaseException>(() => opened.ReplaceFileKey(FakeVault.MasterKey, default));

        Assert.Equal(CaseError.WouldLoseAccess, error.Error);
    }

    [Fact]
    public void The_case_goes_on_under_the_new_key_and_its_later_saves_hold()
    {
        CreateSharedWithColleague();
        using (var opened = _owner.CreateStore().Open("A1"))
        {
            opened.ReplaceFileKey(FakeVault.MasterKey, default);
            opened.SetTitle("Efter bytet");
            opened.Save();
            Assert.True(new CaseVerifier(_owner.Clock).Verify(opened).IsIntact);
        }

        using var reopened = _owner.CreateStore().Open("A1");
        Assert.Equal("Efter bytet", reopened.Title);
        var verification = new CaseVerifier(_owner.Clock).Verify(reopened);
        Assert.True(verification.IsIntact);
        Assert.DoesNotContain(reopened.ReadJournal(), entry => entry.Kind == CaseJournalKinds.AnchorsReplaced);
        Assert.False(File.Exists(reopened.Path + ".work.rekey"));
        Assert.False(File.Exists(reopened.Path + ".work.old-key"));
    }

    [Fact]
    public void The_seal_follows_the_new_key()
    {
        var path = CreateSharedWithColleague();
        using (var opened = _owner.CreateStore().Open("A1"))
        {
            opened.ReplaceFileKey(FakeVault.MasterKey, default);
        }

        // A byte changed in the database part, after the header, makes the seal refuse the file.
        var bytes = File.ReadAllBytes(path);
        bytes[bytes.Length - CaseFileSeal.TrailerLength - 100] ^= 0x01;
        File.WriteAllBytes(path, bytes);
        var error = Assert.Throws<CaseException>(() => _owner.CreateStore().Open("A1"));
        Assert.Equal(CaseError.Corrupt, error.Error);
    }

    [Theory]
    [InlineData(Case.RekeyPrepared, false)]
    [InlineData(Case.RekeyWritten, false)]
    [InlineData(Case.RekeySetAside, false)]
    [InlineData(Case.RekeyReplaced, true)]
    public void A_crash_at_any_step_leaves_the_old_file_or_the_new_one_whole(string step, bool replacedByThen)
    {
        var path = CreateSharedWithColleague();
        CaseFixture? picture = null;
        using (var opened = (Case)_owner.CreateStore().Open("A1"))
        {
            opened.FileKeyStep = name =>
            {
                if (name == step)
                {
                    picture = Picture(_owner);
                }
            };
            opened.ReplaceFileKey(FakeVault.MasterKey, default);
        }

        Assert.NotNull(picture);
        using (var afterCrash = picture.CreateStore().Open("A1"))
        {
            var replaced = afterCrash.ReadJournal().Any(entry => entry.Kind == CaseJournalKinds.FileKeyReplaced);
            Assert.Equal(replacedByThen, replaced);
            Assert.Equal(!replacedByThen, afterCrash.HasSharingPassword);
            Assert.True(new CaseVerifier(picture.Clock).Verify(afterCrash).Chains.All(chain => chain.Break is null));

            // An interrupted replacement is said, once, when the old file is what opened.
            Assert.Equal(!replacedByThen, afterCrash.FileKeyReplacementInterrupted);
            Assert.Equal(
                replacedByThen ? 0 : 1,
                afterCrash.ReadJournal().Count(entry => entry.Kind == CaseJournalKinds.FileKeyReplacementInterrupted));
        }

        Assert.False(File.Exists(Path.Combine(picture.Directory, "A1.cut.tmp")));
        using (var again = picture.CreateStore().Open("A1"))
        {
            Assert.False(again.FileKeyReplacementInterrupted);
        }

        Assert.False(File.Exists(Path.Combine(picture.Directory, "A1.cut.work.rekey")));
        Assert.False(File.Exists(Path.Combine(picture.Directory, "A1.cut.work.old-key")));
        var crashed = Path.Combine(picture.Directory, "A1.cut");
        if (replacedByThen)
        {
            Assert.Throws<CaseException>(() => _colleague.CreateStore().OpenWithPassword(crashed, CaseFixture.Password));
        }
        else
        {
            using var old = _colleague.CreateStore().OpenWithPassword(crashed, CaseFixture.Password);
            Assert.DoesNotContain(old.ReadJournal(), entry => entry.Kind == CaseJournalKinds.FileKeyReplaced);
        }

        Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData(Case.RekeyPrepared)]
    [InlineData(Case.RekeyWritten)]
    [InlineData(Case.RekeySetAside)]
    public void A_failure_before_the_move_leaves_the_case_open_under_the_old_key(string step)
    {
        var path = CreateSharedWithColleague();
        var before = File.ReadAllBytes(path);
        using (var opened = (Case)_owner.CreateStore().Open("A1"))
        {
            opened.FileKeyStep = name =>
            {
                if (name == step)
                {
                    throw new IOException("Avbrutet i testet.");
                }
            };

            Assert.Throws<IOException>(() => opened.ReplaceFileKey(FakeVault.MasterKey, default));
            Assert.False(opened.IsClosed);
            Assert.True(opened.HasSharingPassword);
            Assert.DoesNotContain(opened.ReadJournal(), entry => entry.Kind == CaseJournalKinds.FileKeyReplaced);
            opened.SetTitle("Efter avbrottet");
        }

        Assert.NotEqual(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".work.rekey"));
        Assert.False(File.Exists(path + ".work.old-key"));
        Assert.False(File.Exists(path + ".tmp"));
        using var colleagues = _colleague.CreateStore().OpenWithPassword(path, CaseFixture.Password);
        Assert.Equal("Efter avbrottet", colleagues.Title);
    }

    [Fact]
    public void A_failure_after_the_move_closes_the_case_and_it_opens_from_the_new_file()
    {
        var path = CreateSharedWithColleague();
        using (var opened = (Case)_owner.CreateStore().Open("A1"))
        {
            opened.FileKeyStep = name =>
            {
                if (name == Case.RekeyReplaced)
                {
                    throw new IOException("Avbrutet i testet.");
                }
            };

            Assert.Throws<IOException>(() => opened.ReplaceFileKey(FakeVault.MasterKey, default));
            Assert.True(opened.IsClosed);
        }

        using (var reopened = _owner.CreateStore().Open("A1"))
        {
            Assert.Contains(reopened.ReadJournal(), entry => entry.Kind == CaseJournalKinds.FileKeyReplaced);
            Assert.False(reopened.HasSharingPassword);
            Assert.True(new CaseVerifier(_owner.Clock).Verify(reopened).Chains.All(chain => chain.Break is null));
        }

        Assert.Equal(
            CaseError.NoAccess,
            Assert.Throws<CaseException>(() => _colleague.CreateStore().OpenWithPassword(path, CaseFixture.Password))
                .Error);
    }

    [Fact]
    public void The_replacement_reads_back_from_the_journal()
    {
        CreateSharedWithColleague();
        using var opened = _owner.CreateStore().Open("A1");
        var replaced = opened.ReplaceFileKey(FakeVault.MasterKey, NewPassword);

        var line = opened.ReadJournal().Single(entry => entry.Kind == CaseJournalKinds.FileKeyReplaced);
        var read = FileKeyReplacement.FromJournal(line);

        Assert.NotNull(read);
        Assert.Equal(replaced.Dropped, read.Dropped);
        Assert.True(read.SharingPasswordCreated);
        Assert.Null(FileKeyReplacement.FromJournal(opened.ReadJournal()[0]));
    }

    [Fact]
    public void Only_this_vault_and_the_built_in_recovery_key_are_kept()
    {
        Assert.True(FileKeyReplacement.Keeps(new CaseEnvelope(CaseEnvelopeKind.Yours)));
        Assert.True(FileKeyReplacement.Keeps(
            new CaseEnvelope(CaseEnvelopeKind.Recovery, "ab", RecoveryEnvelopeKey.BuiltIn)));
        Assert.False(FileKeyReplacement.Keeps(
            new CaseEnvelope(CaseEnvelopeKind.Recovery, "ab", RecoveryEnvelopeKey.Other)));
        Assert.False(FileKeyReplacement.Keeps(new CaseEnvelope(CaseEnvelopeKind.OtherVault)));
        Assert.False(FileKeyReplacement.Keeps(new CaseEnvelope(CaseEnvelopeKind.SharingPassword)));
        Assert.False(FileKeyReplacement.Keeps(new CaseEnvelope(CaseEnvelopeKind.Unknown)));
    }

    /// <summary>
    /// Creates the owner's case with a case password and, unless told not to, an envelope for the colleague's vault
    /// (the colleague opens it with the password and adds one).
    /// </summary>
    /// <param name="addColleaguesEnvelope">Whether the colleague adds an envelope of their own.</param>
    /// <returns>The case file.</returns>
    private string CreateSharedWithColleague(bool addColleaguesEnvelope = true)
    {
        string path;
        using (var opened = _owner.CreateStore().Create("A1"))
        {
            opened.SetSharingPassword(CaseFixture.Password);
            path = opened.Path;
        }

        if (addColleaguesEnvelope)
        {
            using var colleagues = _colleague.CreateStore().OpenWithPassword(path, CaseFixture.Password);
            colleagues.AddOperatorAccess();
        }

        return path;
    }

    /// <summary>
    /// Takes a picture of the owner's folder as a crash would leave it right now: every file but the lock, in a fixture
    /// of its own for the same vault.
    /// </summary>
    /// <param name="fixture">The fixture whose folder is copied.</param>
    /// <returns>A fixture over the copy.</returns>
    private CaseFixture Picture(CaseFixture fixture)
    {
        var target = Path.Combine(Path.GetTempPath(), "chainsaw-crash-" + Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.GetFiles(fixture.Directory, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".lock", StringComparison.Ordinal))
            {
                continue;
            }

            var copy = Path.Combine(target, Path.GetRelativePath(fixture.Directory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        var picture = new CaseFixture("vault-a", target) { RecoveryKey = fixture.RecoveryKey };
        _pictures.Add(picture);
        return picture;
    }
}
