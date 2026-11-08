using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases.Tests;

/// <summary>
/// An open case writes its working copy back into the case file by itself after writes, when it has been quiet for a
/// while; the tests move the time on themselves instead of waiting.
/// </summary>
public sealed class AutosaveTests : IDisposable
{
    private static readonly TimeSpan Quiet = CasesOptions.DefaultAutosaveQuietPeriod;

    private static readonly TimeSpan Maximum = CasesOptions.DefaultAutosaveMaximumDelay;

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(1);

    private readonly CaseFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void A_write_is_saved_once_the_case_has_been_quiet_for_the_quiet_period()
    {
        using var opened = Create();
        var before = CaseFileBytes();
        opened.AppendJournal(new JournalDraft("test.note", "Ska sparas av sig själv"));

        _fixture.Time.Advance(Quiet - Tick);
        Assert.Equal(before, CaseFileBytes());
        Assert.True(opened.HasUnsavedWrites);

        _fixture.Time.Advance(Tick);
        Assert.NotEqual(before, CaseFileBytes());
        Assert.False(opened.HasUnsavedWrites);
        Assert.False(File.Exists(CasePaths.For(_fixture.Directory, "A1").SaveFile));
    }

    [Fact]
    public void A_copy_of_the_file_taken_while_the_case_is_open_holds_the_saved_write()
    {
        using var opened = Create();
        opened.AppendJournal(new JournalDraft("test.note", "Lämnas vidare"));
        _fixture.Time.Advance(Quiet);

        // The file is handed on while the case is still open here, as a colleague would get it.
        using var colleague = new CaseFixture("vault-a");
        System.IO.Directory.CreateDirectory(colleague.Directory);
        File.Copy(_fixture.CaseFile("A1"), colleague.CaseFile("A1"));
        using var copy = colleague.CreateStore().Open("A1");

        Assert.Contains(copy.ReadJournal(), entry => entry.Kind == "test.note");
    }

    [Fact]
    public void Every_new_write_moves_the_save_to_after_the_quiet_period()
    {
        using var opened = Create();
        var before = CaseFileBytes();
        opened.AppendJournal(new JournalDraft("test.note", "Den första"));
        _fixture.Time.Advance(Quiet - TimeSpan.FromSeconds(1));
        opened.AppendJournal(new JournalDraft("test.note", "Den andra"));

        _fixture.Time.Advance(Quiet - Tick);
        Assert.Equal(before, CaseFileBytes());

        _fixture.Time.Advance(Tick);
        Assert.NotEqual(before, CaseFileBytes());
    }

    [Fact]
    public void A_case_that_is_written_to_all_the_time_is_saved_at_the_latest_after_the_maximum_delay()
    {
        using var opened = Create();
        var before = CaseFileBytes();
        var step = Quiet - TimeSpan.FromSeconds(2);
        opened.AppendJournal(new JournalDraft("test.note", "Den första"));

        var elapsed = TimeSpan.Zero;
        while (elapsed + step < Maximum)
        {
            _fixture.Time.Advance(step);
            elapsed += step;
            Assert.Equal(before, CaseFileBytes());
            opened.AppendJournal(new JournalDraft("test.note", "En till"));
        }

        _fixture.Time.Advance(Maximum - elapsed);
        Assert.NotEqual(before, CaseFileBytes());
        Assert.False(opened.HasUnsavedWrites);
    }

    [Fact]
    public void A_case_without_new_writes_is_never_saved_by_itself()
    {
        using var opened = Create();
        var before = CaseFileBytes();
        var written = File.GetLastWriteTimeUtc(_fixture.CaseFile("A1"));

        _fixture.Time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(before, CaseFileBytes());
        Assert.Equal(written, File.GetLastWriteTimeUtc(_fixture.CaseFile("A1")));
        Assert.False(opened.HasUnsavedWrites);
    }

    [Fact]
    public void A_case_that_opens_without_writing_has_nothing_to_save()
    {
        using (Create())
        {
        }

        using var reopened = (Case)_fixture.CreateStore().Open("A1");

        Assert.False(reopened.HasUnsavedWrites);
    }

    [Fact]
    public void Writes_of_every_kind_wait_to_be_saved_and_a_failed_write_does_not()
    {
        var store = _fixture.CreateStore(new TestSchema());
        using var opened = (Case)store.Create("A1");

        Assert.Throws<InvalidOperationException>(
            () => opened.Write<int>(_ => throw new InvalidOperationException("Avbryts")));
        Assert.False(opened.HasUnsavedWrites);

        opened.Write(InsertNote);
        Assert.True(opened.HasUnsavedWrites);
        opened.Save();
        Assert.False(opened.HasUnsavedWrites);

        opened.SetTitle("En titel");
        Assert.True(opened.HasUnsavedWrites);
    }

    [Fact]
    public void A_save_by_hand_leaves_nothing_for_the_timer()
    {
        using var opened = Create();
        opened.AppendJournal(new JournalDraft("test.note", "Sparas för hand"));
        opened.Save();
        var saved = CaseFileBytes();
        var written = File.GetLastWriteTimeUtc(_fixture.CaseFile("A1"));

        _fixture.Time.Advance(Maximum);

        Assert.Equal(saved, CaseFileBytes());
        Assert.Equal(written, File.GetLastWriteTimeUtc(_fixture.CaseFile("A1")));
    }

    [Fact]
    public async Task The_save_waits_while_another_operation_holds_the_case_and_comes_shortly_after()
    {
        using var opened = Create();
        opened.AppendJournal(new JournalDraft("test.note", "Väntar på läsningen"));
        var before = CaseFileBytes();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var reading = Task.Run(() => opened.Read(_ =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            return 0;
        }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));

        // The timer's callback runs here, on the test thread, and finds the case busy: it must not wait.
        _fixture.Time.Advance(Quiet);
        Assert.Equal(before, CaseFileBytes());
        Assert.True(opened.HasUnsavedWrites);

        release.Set();
        await reading;
        _fixture.Time.Advance(AutosaveSchedule.BusyWait - Tick);
        Assert.Equal(before, CaseFileBytes());

        _fixture.Time.Advance(Tick);
        Assert.NotEqual(before, CaseFileBytes());
    }

    [Fact]
    public void A_timer_that_comes_in_the_middle_of_a_write_on_the_same_thread_does_not_save_it()
    {
        var store = _fixture.CreateStore(new TestSchema());
        using var opened = (Case)store.Create("A1");
        opened.AppendJournal(new JournalDraft("test.note", "Den första"));
        var before = CaseFileBytes();

        opened.Write(transaction =>
        {
            InsertNote(transaction);
            _fixture.Time.Advance(Maximum);
            return 0;
        });

        Assert.Equal(before, CaseFileBytes());
        Assert.True(opened.HasUnsavedWrites);
        _fixture.Time.Advance(AutosaveSchedule.BusyWait);
        Assert.NotEqual(before, CaseFileBytes());
        Assert.False(opened.HasUnsavedWrites);
    }

    [Fact]
    public void A_failed_save_leaves_the_file_whole_and_is_tried_again_later()
    {
        using var opened = Create();
        var before = CaseFileBytes();
        opened.AppendJournal(new JournalDraft("test.note", "Sparas när det går"));

        // A folder where the new file is to be written makes the save fail before the file is replaced.
        var saveFile = CasePaths.For(_fixture.Directory, "A1").SaveFile;
        System.IO.Directory.CreateDirectory(saveFile);
        _fixture.Time.Advance(Quiet);
        Assert.Equal(before, CaseFileBytes());
        Assert.True(opened.HasUnsavedWrites);

        System.IO.Directory.Delete(saveFile);
        _fixture.Time.Advance(Maximum - Tick);
        Assert.Equal(before, CaseFileBytes());

        _fixture.Time.Advance(Tick);
        Assert.NotEqual(before, CaseFileBytes());
        Assert.False(opened.HasUnsavedWrites);
    }

    [Fact]
    public void A_lock_of_the_vault_between_the_checks_and_the_save_ends_the_save_quietly()
    {
        var store = _fixture.CreateStore();
        var opened = (Case)store.Create("A1");
        var before = CaseFileBytes();
        opened.AppendJournal(new JournalDraft("test.note", "Låses mitt i"));
        opened.AutosaveChecked = () => _fixture.Vault.Lock();

        // Without the fix the refusal of the closing case would leave the timer's callback and end the process.
        _fixture.Time.Advance(Quiet);
        Assert.Equal(before, CaseFileBytes());

        store.WaitForClosing();
        Assert.True(opened.IsClosed);
        _fixture.Vault.Unlock(default);
        using var reopened = store.Open("A1");
        Assert.Contains(reopened.ReadJournal(), entry => entry.Kind == "test.note");
    }

    [Fact]
    public void A_case_that_is_closing_is_left_to_the_closing()
    {
        using var opened = Create();
        var before = CaseFileBytes();
        opened.AppendJournal(new JournalDraft("test.note", "Stängningen sparar"));
        opened.BeginClosing();

        _fixture.Time.Advance(Maximum);

        Assert.Equal(before, CaseFileBytes());
    }

    [Fact]
    public void Locking_the_vault_saves_and_closes_and_the_timer_does_nothing_afterwards()
    {
        var store = _fixture.CreateStore();
        var opened = (Case)store.Create("A1");
        opened.AppendJournal(new JournalDraft("test.note", "Före låsningen"));

        _fixture.Vault.Lock();
        store.WaitForClosing();
        var closed = CaseFileBytes();
        _fixture.Time.Advance(Maximum);

        Assert.True(opened.IsClosed);
        Assert.Equal(closed, CaseFileBytes());
        _fixture.Vault.Unlock(default);
        using var reopened = store.Open("A1");
        Assert.Contains(reopened.ReadJournal(), entry => entry.Kind == "test.note");
    }

    [Fact]
    public void A_closed_case_is_not_saved_again()
    {
        var opened = Create();
        opened.AppendJournal(new JournalDraft("test.note", "Före stängningen"));
        opened.Close();
        var closed = CaseFileBytes();

        _fixture.Time.Advance(Maximum);

        Assert.Equal(closed, CaseFileBytes());
    }

    [Fact]
    public void A_crashed_case_is_not_saved_by_the_timer_and_is_recovered()
    {
        var store = _fixture.CreateStore();
        var opened = (Case)store.Create("A1");
        var before = CaseFileBytes();
        opened.AppendJournal(new JournalDraft("test.note", "Bara i arbetskopian"));
        opened.Abandon();

        _fixture.Time.Advance(Maximum);
        Assert.Equal(before, CaseFileBytes());

        using var recovered = store.Open("A1");
        Assert.Equal(
            ["case.created", "test.note", CaseJournalKinds.Recovered],
            recovered.ReadJournal().Select(entry => entry.Kind));
    }

    [Fact]
    public void A_crash_after_a_save_by_itself_is_recovered_rather_than_taken_for_a_conflict()
    {
        var store = _fixture.CreateStore();
        var opened = (Case)store.Create("A1");
        opened.AppendJournal(new JournalDraft("test.note", "Sparad av sig själv"));
        _fixture.Time.Advance(Quiet);
        opened.AppendJournal(new JournalDraft("test.note", "Bara i arbetskopian"));
        opened.Abandon();

        using var recovered = store.Open("A1");

        Assert.Equal(
            ["case.created", "test.note", "test.note", CaseJournalKinds.Recovered],
            recovered.ReadJournal().Select(entry => entry.Kind));
        Assert.True(new CaseVerifier(_fixture.Clock).Verify(recovered).IsIntact);
    }

    private static int InsertNote(ICaseTransaction transaction)
    {
        using var command = transaction.CreateCommand("INSERT INTO test_notes (note) VALUES ('en anteckning')");
        return command.ExecuteNonQuery();
    }

    private Case Create() => (Case)_fixture.CreateStore().Create("A1");

    private byte[] CaseFileBytes() => File.ReadAllBytes(_fixture.CaseFile("A1"));
}
