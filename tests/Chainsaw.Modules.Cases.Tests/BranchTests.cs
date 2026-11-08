using System.Security.Cryptography;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases.Tests;

/// <summary>
/// A working copy left by a crash that does not belong to the case file any more (sketch ÄR1b, <em>gick åt två
/// håll</em>): the user chooses the file, and the working copy is set aside, or the working copy, saved as a case of
/// its own. Nothing is ever removed.
/// </summary>
public sealed class BranchTests : IDisposable
{
    private readonly CaseFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private string CasePath => _fixture.CaseFile("A1");

    private string RecoveredFolder => Path.Combine(_fixture.Directory, "Återställda");

    [Fact]
    public void A_case_without_a_working_copy_has_no_conflict()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        Assert.Null(store.FindConflict("A1"));
    }

    [Fact]
    public void A_working_copy_that_belongs_to_the_file_is_no_conflict()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        ((Case)store.Open("A1")).Abandon();

        Assert.Null(store.FindConflict("A1"));
    }

    [Fact]
    public void A_conflict_says_when_each_branch_was_written_and_suggests_a_free_number()
    {
        var store = _fixture.CreateStore();
        MakeConflict(store);
        using (store.Create("A1-B"))
        {
        }

        var conflict = store.FindConflict("A1");

        Assert.NotNull(conflict);
        Assert.Equal("A1", conflict.CaseNumber);
        Assert.Equal("A1-C", conflict.SuggestedBranchNumber);
        Assert.Equal(File.GetLastWriteTimeUtc(CasePath), conflict.CaseFileWrittenUtc.UtcDateTime);
        Assert.Equal(File.GetLastWriteTimeUtc(CasePath + ".work"), conflict.WorkingCopyWrittenUtc.UtcDateTime);
    }

    [Fact]
    public void Choosing_the_file_moves_the_working_copy_aside_unchanged_and_journals_it()
    {
        var store = _fixture.CreateStore();
        MakeConflict(store);
        var workingCopy = File.ReadAllBytes(CasePath + ".work");
        var workingBase = File.ReadAllBytes(CasePath + ".work.base");

        using (var opened = store.OpenSettingAsideWorkingCopy("A1"))
        {
            var folder = Assert.Single(Directory.GetDirectories(RecoveredFolder));
            Assert.StartsWith("A1 20261107-010203Z", Path.GetFileName(folder), StringComparison.Ordinal);
            Assert.Equal(workingCopy, File.ReadAllBytes(Path.Combine(folder, "A1.cut.work")));
            Assert.Equal(workingBase, File.ReadAllBytes(Path.Combine(folder, "A1.cut.work.base")));

            var entry = opened.ReadJournal().Single(item => item.Kind == CaseJournalKinds.WorkingCopySetAside);
            using var details = JsonDocument.Parse(entry.Details!);
            Assert.Equal(
                "Återställda/" + Path.GetFileName(folder),
                details.RootElement.GetProperty("folder").GetString());
            Assert.Equal(
                Convert.ToHexStringLower(SHA256.HashData(workingCopy)),
                details.RootElement.GetProperty("workingCopySha256").GetString());

            // The file as it was saved: the notes of the crashed session are not in it.
            Assert.DoesNotContain(opened.ReadJournal(), item => item.Summary == "Efter sparningen");
        }

        using var again = store.Open("A1");
        Assert.Null(store.FindConflict("A1"));
    }

    [Fact]
    public void Choosing_the_file_twice_never_writes_over_what_was_set_aside()
    {
        var store = _fixture.CreateStore();
        MakeConflict(store);
        store.OpenSettingAsideWorkingCopy("A1").Dispose();
        var crashed = (Case)store.Open("A1");
        crashed.AppendJournal(new JournalDraft("test.note", "Ännu en krasch"));
        crashed.Abandon();
        File.WriteAllBytes(CasePath + ".work.base", new byte[32]);

        store.OpenSettingAsideWorkingCopy("A1").Dispose();

        Assert.Equal(2, Directory.GetDirectories(RecoveredFolder).Length);
    }

    [Fact]
    public void A_working_copy_that_belongs_to_the_file_is_recovered_rather_than_set_aside()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        var crashed = (Case)store.Open("A1");
        crashed.AppendJournal(new JournalDraft("test.note", "Arbetet"));
        crashed.Abandon();

        using var opened = store.OpenSettingAsideWorkingCopy("A1");

        Assert.False(Directory.Exists(RecoveredFolder));
        Assert.Contains(opened.ReadJournal(), item => item.Kind == CaseJournalKinds.Recovered);
        Assert.Contains(opened.ReadJournal(), item => item.Summary == "Arbetet");
    }

    [Fact]
    public void Choosing_the_file_with_a_locked_vault_moves_nothing()
    {
        var store = _fixture.CreateStore();
        MakeConflict(store);
        _fixture.Vault.Lock();

        var error = Assert.Throws<CaseException>(() => store.OpenSettingAsideWorkingCopy("A1"));

        Assert.Equal(CaseError.VaultLocked, error.Error);
        Assert.True(File.Exists(CasePath + ".work"));
        Assert.False(Directory.Exists(RecoveredFolder));
    }

    [Fact]
    public void Choosing_the_file_in_another_vault_moves_nothing()
    {
        var store = _fixture.CreateStore();
        MakeConflict(store);
        using var other = new CaseFixture("vault-b", _fixture.Directory);

        var error = Assert.Throws<CaseException>(() => other.CreateStore().OpenSettingAsideWorkingCopy("A1"));

        Assert.Equal(CaseError.NoAccess, error.Error);
        Assert.True(File.Exists(CasePath + ".work"));
        Assert.False(Directory.Exists(RecoveredFolder));
    }

    [Fact]
    public void Choosing_the_working_copy_makes_a_case_of_its_own_with_a_new_id_and_number()
    {
        var store = _fixture.CreateStore();
        var original = MakeConflict(store);
        var file = File.ReadAllBytes(CasePath);
        var workingCopy = File.ReadAllBytes(CasePath + ".work");

        using (var branch = store.SaveWorkingCopyAsCase("A1", "A1-B"))
        {
            Assert.Equal(_fixture.CaseFile("A1-B"), branch.Path);
            Assert.Equal("A1-B", branch.Info.CaseNumber);
            Assert.NotEqual(original.Id, branch.Info.Id);
            Assert.Equal(original.CreatedUtc, branch.Info.CreatedUtc);
            var journal = branch.ReadJournal();
            Assert.Equal(CaseJournalKinds.Created, journal[0].Kind);
            Assert.Contains(journal, item => item.Summary == "Före kraschen");
            Assert.Contains(journal, item => item.Summary == "Efter sparningen");
            var saved = journal.Single(item => item.Kind == CaseJournalKinds.BranchSaved);
            using (var details = JsonDocument.Parse(saved.Details!))
            {
                Assert.Equal("A1", details.RootElement.GetProperty("from").GetString());
                Assert.Equal(original.Id.ToString("D"), details.RootElement.GetProperty("fromCaseId").GetString());
                Assert.Equal(branch.Info.Id.ToString("D"), details.RootElement.GetProperty("caseId").GetString());
                Assert.Equal("A1-B", details.RootElement.GetProperty("caseNumber").GetString());
            }

            Assert.Equal(CaseJournalKinds.WorkingCopySetAside, journal[^1].Kind);
            Assert.Equal([CaseEnvelopeKind.Yours], branch.DescribeEnvelopes().Select(item => item.Kind));
            Assert.True(new CaseVerifier(_fixture.Clock).Verify(branch).IsIntact);
        }

        // The file is left as it is, and its working copy is kept, unchanged, in Återställda.
        Assert.Equal(file, File.ReadAllBytes(CasePath));
        Assert.False(File.Exists(CasePath + ".work"));
        var folder = Assert.Single(Directory.GetDirectories(RecoveredFolder));
        Assert.Equal(workingCopy, File.ReadAllBytes(Path.Combine(folder, "A1.cut.work")));
        Assert.Null(store.FindConflict("A1"));
    }

    [Fact]
    public void The_two_branches_open_by_turns_without_replacing_each_others_anchors()
    {
        var store = _fixture.CreateStore();
        MakeConflictWithoutRollback(store);
        store.SaveWorkingCopyAsCase("A1", "A1-B").Dispose();

        for (var round = 0; round < 3; round++)
        {
            foreach (var number in new[] { "A1", "A1-B" })
            {
                using var opened = store.Open(number);
                opened.AppendJournal(new JournalDraft("test.note", $"Varv {round} i {number}"));
                Assert.True(new CaseVerifier(_fixture.Clock).Verify(opened).IsIntact, number);
                Assert.Equal(number, opened.Info.CaseNumber);
            }
        }

        using var branch = store.Open("A1-B");
        using var original = store.Open("A1");
        Assert.DoesNotContain(branch.ReadJournal(), item => item.Kind == CaseJournalKinds.AnchorsReplaced);
        Assert.DoesNotContain(original.ReadJournal(), item => item.Kind == CaseJournalKinds.AnchorsReplaced);
        Assert.NotEqual(original.Info.Id, branch.Info.Id);
    }

    [Fact]
    public void A_branch_names_itself_and_has_an_id_of_its_own_for_exports_watches_and_the_label_library()
    {
        // Exports write Info.CaseNumber; the background watching and the label library key on Info.Id.
        var store = _fixture.CreateStore();
        var original = MakeConflict(store);

        using var branch = store.SaveWorkingCopyAsCase("A1", "A1-B");
        branch.Close();
        using var reopened = store.Open("A1-B");

        Assert.Equal("A1-B", reopened.Info.CaseNumber);
        Assert.NotEqual(original.Id, reopened.Info.Id);
        using var file = File.OpenRead(reopened.Path);
        Assert.Equal(reopened.Info.Id, CaseContainer.ReadHeader(file).CaseId);
    }

    [Fact]
    public void A_branch_of_a_branch_still_verifies()
    {
        var store = _fixture.CreateStore();
        MakeConflict(store);
        store.SaveWorkingCopyAsCase("A1", "A1-B").Dispose();
        var path = _fixture.CaseFile("A1-B");
        var older = File.ReadAllBytes(path);
        var crashed = (Case)store.Open("A1-B");
        crashed.AppendJournal(new JournalDraft("test.note", "I grenen"));
        crashed.Save();
        crashed.Abandon();
        File.WriteAllBytes(path, older);

        using var second = store.SaveWorkingCopyAsCase("A1-B", "A1-C");

        Assert.Equal("A1-C", second.Info.CaseNumber);
        Assert.Equal(2, second.ReadJournal().Count(item => item.Kind == CaseJournalKinds.BranchSaved));
        Assert.True(new CaseVerifier(_fixture.Clock).Verify(second).IsIntact);
    }

    [Fact]
    public void A_changed_case_number_inside_a_branch_is_found_by_the_verification()
    {
        var store = _fixture.CreateStore();
        MakeConflict(store);
        store.SaveWorkingCopyAsCase("A1", "A1-B").Dispose();

        // Someone with the key gives the branch the original's number back, outside Chainsaw.
        CaseFileSurgery.Change(_fixture, "A1-B", connection =>
        {
            CaseFileSurgery.Execute(connection, "DROP TRIGGER seal_case_info_no_update");
            CaseFileSurgery.Execute(connection, "UPDATE case_info SET case_number = 'A1'");
            CaseFileSurgery.Execute(connection, CasesSchema.CaseInfoNoUpdateTrigger);
        });

        using var opened = store.Open("A1-B");
        Assert.False(new CaseVerifier(_fixture.Clock).Verify(opened).IsIntact);
    }

    [Fact]
    public void Envelopes_that_cannot_follow_the_branch_are_named_in_its_journal()
    {
        var store = _fixture.CreateStore();
        using (var created = store.Create("A1"))
        {
            created.SetSharingPassword(CaseFixture.Password);
        }

        var crashed = (Case)store.Open("A1");
        crashed.AppendJournal(new JournalDraft("test.note", "Efter sparningen"));
        crashed.Abandon();
        File.WriteAllBytes(CasePath + ".work.base", new byte[32]);

        using var branch = store.SaveWorkingCopyAsCase("A1", "A1-B");

        var saved = branch.ReadJournal().Single(item => item.Kind == CaseJournalKinds.BranchSaved);
        Assert.Contains("ärendelösenordet", saved.Summary, StringComparison.Ordinal);
        Assert.False(branch.HasSharingPassword);
    }

    [Fact]
    public void A_branch_gets_an_envelope_for_the_built_in_recovery_key()
    {
        using var key = System.Security.Cryptography.ECDiffieHellman.Create(
            System.Security.Cryptography.ECCurve.NamedCurves.nistP384);
        _fixture.RecoveryKey = RecoveryPublicKey.FromPrivateKey(key);
        var store = _fixture.CreateStore();
        MakeConflict(store);

        using var branch = store.SaveWorkingCopyAsCase("A1", "A1-B");

        Assert.Equal([_fixture.RecoveryKey.Fingerprint], branch.RecoveryFingerprints);
        var path = branch.Path;
        branch.Close();
        using var administrator = _fixture.CreateStore().OpenWithRecoveryKey(path, key, "Admin Adminsson");
        Assert.Equal("A1-B", administrator.Info.CaseNumber);
    }

    [Fact]
    public void The_working_copy_is_never_saved_over_a_case_that_exists()
    {
        var store = _fixture.CreateStore();
        MakeConflict(store);
        using (store.Create("A2"))
        {
        }

        var other = File.ReadAllBytes(_fixture.CaseFile("A2"));
        var workingCopy = File.ReadAllBytes(CasePath + ".work");

        var error = Assert.Throws<CaseException>(() => store.SaveWorkingCopyAsCase("A1", "A2"));

        Assert.Equal(CaseError.AlreadyExists, error.Error);
        Assert.Equal(other, File.ReadAllBytes(_fixture.CaseFile("A2")));
        Assert.Equal(workingCopy, File.ReadAllBytes(CasePath + ".work"));
        Assert.Equal(
            CaseError.AlreadyExists,
            Assert.Throws<CaseException>(() => store.SaveWorkingCopyAsCase("A1", "a1")).Error);
    }

    [Fact]
    public void A_working_copy_that_cannot_be_opened_goes_back_where_it_was()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        var garbage = RandomNumberGenerator.GetBytes(8192);
        File.WriteAllBytes(CasePath + ".work", garbage);
        File.WriteAllBytes(CasePath + ".work.base", new byte[32]);

        var error = Assert.Throws<CaseException>(() => store.SaveWorkingCopyAsCase("A1", "A1-B"));

        Assert.Equal(CaseError.Conflict, error.Error);
        Assert.Equal(garbage, File.ReadAllBytes(CasePath + ".work"));
        Assert.Equal(new byte[32], File.ReadAllBytes(CasePath + ".work.base"));
        Assert.Empty(Directory.GetFiles(_fixture.Directory, "A1-B*"));
        Assert.False(Directory.Exists(RecoveredFolder));
        Assert.NotNull(store.FindConflict("A1"));
    }

    [Fact]
    public void Without_a_working_copy_in_conflict_nothing_is_saved_as_a_case()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        var error = Assert.Throws<CaseException>(() => store.SaveWorkingCopyAsCase("A1", "A1-B"));

        Assert.Equal(CaseError.NotFound, error.Error);
        Assert.False(File.Exists(_fixture.CaseFile("A1-B")));
    }

    [Fact]
    public void A_rollback_journal_without_its_working_copy_is_not_rolled_into_a_new_one()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        File.WriteAllBytes(CasePath + ".work-journal", HotJournalOfAnotherCase(store));

        using var opened = store.Open("A1");

        Assert.True(new CaseVerifier(_fixture.Clock).Verify(opened).IsIntact);
        Assert.False(File.Exists(CasePath + ".work-journal"));
    }

    [Fact]
    public void A_set_aside_that_cannot_open_the_file_puts_the_working_copy_back()
    {
        var store = _fixture.CreateStore();
        MakeConflict(store);
        var workingCopy = File.ReadAllBytes(CasePath + ".work");
        var bytes = File.ReadAllBytes(CasePath);
        bytes.AsSpan(bytes.Length - 2048, 1024).Fill(0x5A);
        File.WriteAllBytes(CasePath, bytes);

        var error = Assert.Throws<CaseException>(() => store.OpenSettingAsideWorkingCopy("A1"));

        Assert.Equal(CaseError.Corrupt, error.Error);
        Assert.Equal(workingCopy, File.ReadAllBytes(CasePath + ".work"));
        Assert.Empty(Directory.Exists(RecoveredFolder) ? Directory.GetFileSystemEntries(RecoveredFolder) : []);
        Assert.NotNull(store.FindConflict("A1"));
    }

    [Fact]
    public void A_branch_number_with_a_half_written_file_is_not_free()
    {
        var store = _fixture.CreateStore();
        MakeConflict(store);
        File.WriteAllBytes(_fixture.CaseFile("A1-B") + ".tmp", [1, 2, 3]);

        var error = Assert.Throws<CaseException>(() => store.SaveWorkingCopyAsCase("A1", "A1-B"));

        Assert.Equal(CaseError.AlreadyExists, error.Error);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(_fixture.CaseFile("A1-B") + ".tmp"));
        Assert.Equal("A1-C", store.FindConflict("A1")!.SuggestedBranchNumber);
    }

    [Fact]
    public void A_branch_in_a_locked_vault_writes_nothing()
    {
        var store = _fixture.CreateStore();
        MakeConflict(store);
        _fixture.Vault.Lock();

        Assert.Equal(
            CaseError.VaultLocked,
            Assert.Throws<CaseException>(() => store.SaveWorkingCopyAsCase("A1", "A1-B")).Error);
        Assert.Empty(Directory.GetFiles(_fixture.Directory, "A1-B*"));
        Assert.True(File.Exists(CasePath + ".work"));
    }

    /// <summary>
    /// Takes a real rollback journal from another case in the middle of a write: the journal SQLite writes before it
    /// changes a page, encrypted with that case's key.
    /// </summary>
    private byte[] HotJournalOfAnotherCase(CaseStore store)
    {
        using (store.Create("A9"))
        {
        }

        var other = _fixture.CaseFile("A9");
        using var opened = (Case)store.Open("A9");
        byte[]? journal = null;
        opened.Write(transaction =>
        {
            for (var index = 0; index < 50; index++)
            {
                transaction.AppendJournal(new JournalDraft("test.note", new string('x', 500)));
            }

            journal = File.ReadAllBytes(other + ".work-journal");
            return 0;
        });
        Assert.NotNull(journal);
        Assert.True(journal.Length > 512);
        return journal;
    }

    /// <summary>
    /// Leaves case A1 with a working copy from a crash that went on after the last save and a file that is as it was
    /// saved, but whose record of the working copy's file no longer matches, as when the file was saved elsewhere:
    /// a conflict without a rollback of the file.
    /// </summary>
    [Fact]
    public void A_conflict_says_where_the_branches_went_apart_and_touches_neither()
    {
        var store = _fixture.CreateStore();
        MakeConflict(store);
        var file = File.ReadAllBytes(CasePath);
        var workingCopy = File.ReadAllBytes(CasePath + ".work");

        var conflict = store.FindConflict("A1")!;

        var divergence = Assert.IsType<CaseDivergence>(conflict.Divergence);
        Assert.Equal(0, divergence.FileEntries);
        Assert.Equal(2, divergence.CopyEntries);
        Assert.True(divergence.Sequence > 0);
        Assert.Equal(64, divergence.Hash.Length);
        Assert.Equal(file, File.ReadAllBytes(CasePath));
        Assert.Equal(workingCopy, File.ReadAllBytes(CasePath + ".work"));
        Assert.Empty(Directory.GetFiles(_fixture.Directory, "*.diverge*"));

        // The file as it is now ends at the shared entry: opened as it is, its journal's last entry is that one.
        using var opened = store.OpenSettingAsideWorkingCopy("A1");
        var shared = opened.ReadJournal().Single(entry => entry.Sequence == divergence.Sequence);
        Assert.Equal(divergence.Hash, shared.Hash);
    }

    [Fact]
    public void Without_the_vault_a_conflict_has_no_divergence()
    {
        var store = _fixture.CreateStore();
        MakeConflict(store);
        _fixture.Vault.Lock();

        Assert.Null(store.FindConflict("A1")!.Divergence);
    }

    [Fact]
    public void Two_journals_are_compared_entry_by_entry()
    {
        byte[] Hash(byte value) => Enumerable.Repeat(value, 32).ToArray();

        var divergence = BranchDivergence.Compare(
            [(1, Hash(1)), (2, Hash(2)), (3, Hash(3))],
            [(1, Hash(1)), (2, Hash(2)), (3, Hash(9)), (4, Hash(4))]);

        Assert.Equal(new CaseDivergence(2, Convert.ToHexStringLower(Hash(2)), 1, 2), divergence);
        Assert.Equal(
            new CaseDivergence(0, string.Empty, 1, 1), BranchDivergence.Compare([(1, Hash(1))], [(1, Hash(2))]));
    }

    private void MakeConflictWithoutRollback(CaseStore store)
    {
        using (store.Create("A1"))
        {
        }

        var crashed = (Case)store.Open("A1");
        crashed.AppendJournal(new JournalDraft("test.note", "Före kraschen"));
        crashed.Save();
        crashed.AppendJournal(new JournalDraft("test.note", "Efter sparningen"));
        crashed.Abandon();
        File.WriteAllBytes(CasePath + ".work.base", new byte[32]);
        Assert.Equal(CaseError.Conflict, Assert.Throws<CaseException>(() => store.Open("A1")).Error);
    }

    /// <summary>
    /// Leaves case A1 with a working copy from a crash that went on after the last save, and the file replaced by an
    /// older copy, as when the file came back from a backup or another computer.
    /// </summary>
    private CaseInfo MakeConflict(CaseStore store)
    {
        CaseInfo info;
        using (var created = store.Create("A1"))
        {
            info = created.Info;
        }

        var older = File.ReadAllBytes(CasePath);
        var crashed = (Case)store.Open("A1");
        crashed.AppendJournal(new JournalDraft("test.note", "Före kraschen"));
        crashed.Save();
        crashed.AppendJournal(new JournalDraft("test.note", "Efter sparningen"));
        crashed.Abandon();
        File.WriteAllBytes(CasePath, older);
        Assert.Equal(CaseError.Conflict, Assert.Throws<CaseException>(() => store.Open("A1")).Error);
        return info;
    }
}
