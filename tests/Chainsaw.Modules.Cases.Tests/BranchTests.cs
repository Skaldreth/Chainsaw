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
    public void Choosing_the_working_copy_saves_it_as_a_case_of_its_own_with_the_same_envelopes()
    {
        var store = _fixture.CreateStore();
        MakeConflict(store);
        var file = File.ReadAllBytes(CasePath);
        var envelopes = store.DescribeEnvelopes(CasePath);

        using (var branch = store.SaveWorkingCopyAsCase("A1", "A1-B"))
        {
            Assert.Equal(_fixture.CaseFile("A1-B"), branch.Path);
            var journal = branch.ReadJournal();
            Assert.Contains(journal, item => item.Summary == "Före kraschen");
            Assert.Contains(journal, item => item.Summary == "Efter sparningen");
            Assert.Contains(journal, item => item.Kind == CaseJournalKinds.Recovered);
            var saved = journal.Single(item => item.Kind == CaseJournalKinds.BranchSaved);
            using var details = JsonDocument.Parse(saved.Details!);
            Assert.Equal("A1", details.RootElement.GetProperty("from").GetString());
            Assert.Equal("A1-B", details.RootElement.GetProperty("caseNumber").GetString());
            Assert.Equal(envelopes, branch.DescribeEnvelopes());
            Assert.True(new CaseVerifier(_fixture.Clock).Verify(branch).IsIntact);
        }

        // The file is left as it is, without its working copy, and both open.
        Assert.Equal(file, File.ReadAllBytes(CasePath));
        Assert.False(File.Exists(CasePath + ".work"));
        using (var original = store.Open("A1"))
        {
            Assert.DoesNotContain(original.ReadJournal(), item => item.Summary == "Efter sparningen");
        }

        using var reopened = store.Open("A1-B");
        Assert.Contains(reopened.ReadJournal(), item => item.Kind == CaseJournalKinds.BranchSaved);
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
        Assert.False(File.Exists(_fixture.CaseFile("A1-B")));
        Assert.False(File.Exists(_fixture.CaseFile("A1-B") + ".work"));
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

        File.WriteAllBytes(CasePath + ".work-journal", RandomNumberGenerator.GetBytes(4096));

        using var opened = store.Open("A1");

        Assert.True(new CaseVerifier(_fixture.Clock).Verify(opened).IsIntact);
    }

    /// <summary>
    /// Leaves case A1 with a working copy from a crash that went on after the last save, and the file replaced by an
    /// older copy, as when the file came back from a backup or another computer.
    /// </summary>
    private void MakeConflict(CaseStore store)
    {
        using (store.Create("A1"))
        {
        }

        var older = File.ReadAllBytes(CasePath);
        var crashed = (Case)store.Open("A1");
        crashed.AppendJournal(new JournalDraft("test.note", "Före kraschen"));
        crashed.Save();
        crashed.AppendJournal(new JournalDraft("test.note", "Efter sparningen"));
        crashed.Abandon();
        File.WriteAllBytes(CasePath, older);
        Assert.Equal(CaseError.Conflict, Assert.Throws<CaseException>(() => store.Open("A1")).Error);
    }
}
