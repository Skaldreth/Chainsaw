using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases.Tests;

/// <summary>
/// A case file that was changed or replaced on disk while its case is open (Explorer, a sync tool, a colleague) is
/// never written over by a save: the file stays as it is, and the session's work stays in the working copy, which is
/// offered as a case of its own (sketch ÄR1b), by a save by hand, by the case's own save and by the closing alike.
/// </summary>
public sealed class ReplacedFileTests : IDisposable
{
    private readonly CaseFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private string CasePath => _fixture.CaseFile("A1");

    [Fact]
    public void A_file_left_alone_is_saved_as_usual()
    {
        using var opened = Open();
        var states = Listen(opened);
        var before = File.ReadAllBytes(CasePath);
        opened.AppendJournal(new JournalDraft("test.note", "Arbetet"));

        opened.Save();
        opened.AppendJournal(new JournalDraft("test.note", "Mer arbete"));
        opened.Save();

        Assert.NotEqual(before, File.ReadAllBytes(CasePath));
        Assert.All(states, state => Assert.False(state.FileReplaced));
        Assert.False(File.Exists(CasePath + ".tmp"));
    }

    [Fact]
    public void A_file_only_touched_with_the_same_content_is_saved_as_usual()
    {
        using var opened = Open();
        opened.AppendJournal(new JournalDraft("test.note", "Arbetet"));
        opened.Save();
        File.WriteAllBytes(CasePath, File.ReadAllBytes(CasePath));
        File.SetLastWriteTimeUtc(CasePath, DateTime.UtcNow.AddMinutes(1));

        opened.AppendJournal(new JournalDraft("test.note", "Mer arbete"));
        opened.Save();
        opened.Close();

        Assert.Null(_fixture.CreateStore().FindConflict("A1"));
    }

    [Fact]
    public void A_file_changed_since_the_last_save_is_left_alone_and_the_work_becomes_a_case_of_its_own()
    {
        var store = _fixture.CreateStore();
        var opened = Open(store);
        var states = Listen(opened);
        opened.AppendJournal(new JournalDraft("test.note", "Före"));
        opened.Save();

        // Another program writes the file right after the save, with the same size, as a sync tool may.
        var theirs = File.ReadAllBytes(CasePath);
        theirs[^1] ^= 0xFF;
        File.WriteAllBytes(CasePath, theirs);
        opened.AppendJournal(new JournalDraft("test.note", "Sessionens arbete"));

        Assert.Equal(CaseError.Conflict, Assert.Throws<CaseException>(opened.Save).Error);
        Assert.Equal(theirs, File.ReadAllBytes(CasePath));
        Assert.False(File.Exists(CasePath + ".tmp"));
        Assert.True(Assert.Single(states, state => !state.IsSaved).FileReplaced);

        // Every later save refuses at once and says nothing more; the closing too, and the working copy stays.
        Assert.Equal(CaseError.Conflict, Assert.Throws<CaseException>(opened.Save).Error);
        Assert.Equal(CaseError.Conflict, Assert.Throws<CaseException>(opened.Close).Error);
        Assert.Single(states, state => state.FileReplaced);
        Assert.Equal(theirs, File.ReadAllBytes(CasePath));
        Assert.True(opened.IsClosed);

        var conflict = store.FindConflict("A1");
        Assert.NotNull(conflict);
        using var branch = store.SaveWorkingCopyAsCase("A1", conflict.SuggestedBranchNumber);
        Assert.Equal("A1-B", branch.Info.CaseNumber);
        Assert.Contains(branch.ReadJournal(), entry => entry.Summary == "Sessionens arbete");
        Assert.Equal(theirs, File.ReadAllBytes(CasePath));
    }

    [Fact]
    public void A_file_replaced_since_the_opening_is_left_alone_at_the_first_save()
    {
        var store = _fixture.CreateStore();
        using (var created = store.Create("A1"))
        {
            created.AppendJournal(new JournalDraft("test.note", "Den äldre"));
        }

        var older = File.ReadAllBytes(CasePath);
        using (var reopened = store.Open("A1"))
        {
            reopened.AppendJournal(new JournalDraft("test.note", "Den nyare"));
        }

        var opened = store.Open("A1");
        File.WriteAllBytes(CasePath, older);
        opened.AppendJournal(new JournalDraft("test.note", "Sessionens arbete"));

        Assert.Equal(CaseError.Conflict, Assert.Throws<CaseException>(opened.Save).Error);
        opened.Dispose();

        Assert.Equal(older, File.ReadAllBytes(CasePath));
        Assert.Equal(CaseError.Conflict, Assert.Throws<CaseException>(() => store.Open("A1")).Error);
        using var branch = store.SaveWorkingCopyAsCase("A1", "A1-B");
        Assert.Contains(branch.ReadJournal(), entry => entry.Summary == "Sessionens arbete");
    }

    [Fact]
    public void The_cases_own_save_leaves_a_replaced_file_alone_says_so_once_and_does_not_try_again()
    {
        var store = _fixture.CreateStore();
        var opened = Open(store);
        var states = Listen(opened);
        var theirs = Replace();
        opened.AppendJournal(new JournalDraft("test.note", "Sessionens arbete"));

        _fixture.Time.Advance(CasesOptions.DefaultAutosaveQuietPeriod);
        _fixture.Time.Advance(TimeSpan.FromHours(1));
        opened.AppendJournal(new JournalDraft("test.note", "Mer arbete"));
        _fixture.Time.Advance(TimeSpan.FromHours(1));

        var state = Assert.Single(states);
        Assert.True(state.FileReplaced);
        Assert.False(state.IsSaved);
        Assert.Equal(theirs, File.ReadAllBytes(CasePath));
        opened.Dispose();
        Assert.True(opened.IsClosed);
        Assert.Equal(theirs, File.ReadAllBytes(CasePath));
        Assert.NotNull(store.FindConflict("A1"));
    }

    [Fact]
    public void Closing_a_case_whose_file_was_replaced_keeps_the_work_beside_the_file()
    {
        var store = _fixture.CreateStore();
        var opened = Open(store);
        var theirs = Replace();
        opened.AppendJournal(new JournalDraft("test.note", "Sessionens arbete"));

        Assert.Equal(CaseError.Conflict, Assert.Throws<CaseException>(opened.Close).Error);

        Assert.True(opened.IsClosed);
        Assert.Equal(theirs, File.ReadAllBytes(CasePath));
        Assert.True(File.Exists(CasePath + ".work"));
        Assert.False(File.Exists(CasePath + ".lock") && IsLocked(CasePath + ".lock"));
        Assert.NotNull(store.FindConflict("A1"));
        using var branch = store.SaveWorkingCopyAsCase("A1", "A1-B");
        Assert.Contains(branch.ReadJournal(), entry => entry.Summary == "Sessionens arbete");
    }

    /// <summary>Creates case A1, saved and closed, and opens it again.</summary>
    /// <param name="store">The store, or a new one.</param>
    /// <returns>The open case.</returns>
    private Case Open(CaseStore? store = null)
    {
        store ??= _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        return (Case)store.Open("A1");
    }

    /// <summary>Writes the case file over with other bytes of the same size, as another program would.</summary>
    /// <returns>What the file holds now.</returns>
    private byte[] Replace()
    {
        var theirs = File.ReadAllBytes(CasePath);
        theirs[^1] ^= 0xFF;
        File.WriteAllBytes(CasePath, theirs);
        return theirs;
    }

    /// <summary>Gathers what the case says about its saves.</summary>
    /// <param name="opened">The case.</param>
    /// <returns>The states, as they come.</returns>
    private static List<CaseSaveState> Listen(ICase opened)
    {
        var states = new List<CaseSaveState>();
        opened.SaveStateChanged += (_, state) => states.Add(state);
        return states;
    }

    /// <summary>Tells whether a file is held open by someone.</summary>
    /// <param name="path">The file.</param>
    /// <returns><see langword="true"/> when it cannot be opened alone.</returns>
    private static bool IsLocked(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }
}
