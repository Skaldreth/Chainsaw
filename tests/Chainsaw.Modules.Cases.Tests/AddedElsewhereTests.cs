using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases.Tests;

/// <summary>
/// The records a colleague added in a copy that came back are marked as added on another computer (decision 19):
/// what lay beyond this computer's anchors when the case was opened, kept with the anchors so that the mark outlives
/// the next save.
/// </summary>
public sealed class AddedElsewhereTests : IDisposable
{
    private readonly CaseFixture _owner = new();

    private readonly CaseFixture _colleague;

    public AddedElsewhereTests()
    {
        _colleague = new CaseFixture("vault-b");
        _colleague.Profiles.Profile = new("Kollega Kollegasson", "Analytiker", "Andra enheten");
    }

    public void Dispose()
    {
        _owner.Dispose();
        _colleague.Dispose();
    }

    [Fact]
    public void Lines_a_colleague_added_are_marked_and_the_owners_are_not()
    {
        var (path, own) = CreateShared();
        var theirs = AddAsColleague(path);

        using var opened = _owner.CreateStore().Open("A1");
        var mine = opened.AppendJournal(new JournalDraft("test.note", "Ägarens egen rad efter öppningen"));

        Assert.All(theirs, sequence => Assert.True(Elsewhere(opened, sequence)));
        Assert.All(Enumerable.Range(1, (int)own), sequence => Assert.False(Elsewhere(opened, sequence)));
        Assert.False(Elsewhere(opened, mine.Sequence));
        Assert.False(opened.WasAddedElsewhere("another.chain", theirs[0]));
    }

    [Fact]
    public void The_marks_outlive_saves_and_reopening()
    {
        var (path, _) = CreateShared();
        var theirs = AddAsColleague(path);
        long mine;
        using (var opened = _owner.CreateStore().Open("A1"))
        {
            mine = opened.AppendJournal(new JournalDraft("test.note", "Ägarens egen rad")).Sequence;
            opened.Save();
        }

        using var reopened = _owner.CreateStore().Open("A1");

        Assert.All(theirs, sequence => Assert.True(Elsewhere(reopened, sequence)));
        Assert.False(Elsewhere(reopened, mine));
    }

    [Fact]
    public void A_second_round_from_the_colleague_is_marked_beside_the_first()
    {
        var (path, _) = CreateShared();
        var first = AddAsColleague(path);
        long mine;
        using (var opened = _owner.CreateStore().Open("A1"))
        {
            mine = opened.AppendJournal(new JournalDraft("test.note", "Ägarens egen rad")).Sequence;
        }

        var second = AddAsColleague(path);
        using var reopened = _owner.CreateStore().Open("A1");

        Assert.All(first.Concat(second), sequence => Assert.True(Elsewhere(reopened, sequence)));
        Assert.False(Elsewhere(reopened, mine));
    }

    [Fact]
    public void A_case_opened_for_the_first_time_on_a_computer_has_nothing_marked()
    {
        var (path, own) = CreateShared();

        using var opened = _colleague.CreateStore().OpenWithPassword(path, CaseFixture.Password);

        Assert.All(
            Enumerable.Range(1, (int)opened.ReadJournal()[^1].Sequence),
            sequence => Assert.False(Elsewhere(opened, sequence)));
        Assert.True(own > 0);
    }

    [Fact]
    public void A_recovered_working_copy_keeps_the_marks_and_its_own_lines_stay_unmarked()
    {
        var (path, _) = CreateShared();
        var theirs = AddAsColleague(path);
        var store = _owner.CreateStore();
        var crashed = (Case)store.Open("A1");
        var mine = crashed.AppendJournal(new JournalDraft("test.note", "Bara i arbetskopian")).Sequence;
        crashed.Abandon();

        using var recovered = store.Open("A1");

        Assert.Equal(CaseJournalKinds.Recovered, recovered.ReadJournal()[^1].Kind);
        Assert.All(theirs, sequence => Assert.True(Elsewhere(recovered, sequence)));
        Assert.False(Elsewhere(recovered, mine));
    }

    /// <summary>Gives whether a journal line is marked as added on another computer.</summary>
    private static bool Elsewhere(ICase opened, long sequence) =>
        opened.WasAddedElsewhere(CaseChains.Journal, sequence);

    /// <summary>Creates the owner's case with a sharing password; returns its path and its last journal line.</summary>
    private (string Path, long Last) CreateShared()
    {
        using var opened = _owner.CreateStore().Create("A1");
        opened.SetSharingPassword(CaseFixture.Password);
        return (opened.Path, opened.ReadJournal()[^1].Sequence);
    }

    /// <summary>
    /// Lets the colleague open the file with the password and add a line; returns every line of this round (the
    /// opening's own and the note).
    /// </summary>
    private long[] AddAsColleague(string path)
    {
        using var opened = _colleague.CreateStore().OpenWithPassword(path, CaseFixture.Password);
        var start = opened.ReadJournal()[^1];
        Assert.Equal(CaseJournalKinds.OpenedWithPassword, start.Kind);
        var note = opened.AppendJournal(new JournalDraft("test.note", "Kollegans rad, påstår sig vara ägarens"));
        return [start.Sequence, note.Sequence];
    }
}
