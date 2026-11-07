using Chainsaw.Modules.Cases.Contracts;
using Microsoft.Data.Sqlite;

namespace Chainsaw.Modules.Cases.Tests;

public sealed class JournalProtectionTests : IDisposable
{
    private readonly CaseFixture _fixture = new();

    private readonly ICase _case;

    public JournalProtectionTests()
    {
        _case = _fixture.CreateStore(new TestSchema()).Create("A1");
        _case.AppendJournal(new JournalDraft("test.note", "Andra raden"));
    }

    public void Dispose()
    {
        _case.Dispose();
        _fixture.Dispose();
    }

    [Theory]
    [InlineData("UPDATE journal SET summary = 'ändrad' WHERE sequence = 2")]
    [InlineData("DELETE FROM journal WHERE sequence = 2")]
    [InlineData("DELETE FROM journal")]
    [InlineData("INSERT INTO journal (sequence, utc, local, time_zone, operator_name, operator_title, "
        + "operator_organization, kind, summary, hash) VALUES (3, '', '', '', '', '', '', 'x', 'x', x'00')")]
    [InlineData("UPDATE chain_head SET sequence = 1")]
    [InlineData("DELETE FROM chain_head")]
    [InlineData("UPDATE case_info SET case_number = 'B2'")]
    [InlineData("UPDATE schema_version SET version = 99")]
    [InlineData("DROP TRIGGER seal_journal_no_update")]
    [InlineData("DROP TABLE journal")]
    [InlineData("ALTER TABLE journal ADD COLUMN extra TEXT")]
    [InlineData("CREATE TEMP TRIGGER hide BEFORE INSERT ON journal BEGIN SELECT RAISE(IGNORE); END")]
    [InlineData("CREATE TRIGGER hide BEFORE INSERT ON journal BEGIN SELECT RAISE(IGNORE); END")]
    [InlineData("CREATE TABLE extra (x INTEGER)")]
    [InlineData("ATTACH DATABASE 'copy.db' AS copy")]
    [InlineData("PRAGMA writable_schema = ON")]
    [InlineData("PRAGMA foreign_keys = OFF")]
    [InlineData("COMMIT")]
    [InlineData("UPDATE test_sealed SET note = 'x'")]
    [InlineData("INSERT INTO test_sealed (sequence, note, amount, hash) VALUES (9, 'x', 1, x'00')")]
    public void Another_modules_sql_cannot_touch_what_is_sealed(string sql)
    {
        var error = Assert.Throws<SqliteException>(() => _case.Write(transaction =>
        {
            using var command = transaction.CreateCommand(sql);
            return command.ExecuteNonQuery();
        }));

        Assert.Equal(23, error.SqliteErrorCode);
        Assert.Equal(2, _case.ReadJournal().Count);
        Assert.True(new CaseVerifier(_fixture.Clock).Verify(_case).IsIntact);
    }

    [Fact]
    public void Another_module_writes_its_own_tables()
    {
        _case.Write(transaction =>
        {
            using var command = transaction.CreateCommand("INSERT INTO test_notes (note) VALUES ($note)");
            command.AddParameter("$note", "egen rad");
            return command.ExecuteNonQuery();
        });

        var count = _case.Read(reader =>
        {
            using var command = reader.CreateCommand("SELECT count(*) FROM test_notes");
            return (long)command.ExecuteScalar()!;
        });
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData("INSERT INTO test_notes (note) VALUES ('x')")]
    [InlineData("DELETE FROM test_notes")]
    [InlineData("CREATE TABLE extra (x INTEGER)")]
    public void A_read_cannot_write(string sql)
    {
        var error = Assert.Throws<SqliteException>(() => _case.Read(reader =>
        {
            using var command = reader.CreateCommand(sql);
            return command.ExecuteNonQuery();
        }));

        Assert.Equal(23, error.SqliteErrorCode);
    }

    [Fact]
    public void A_reader_cannot_be_used_to_seal()
    {
        Assert.Throws<InvalidOperationException>(() => _case.Read(reader =>
            ((ICaseTransaction)reader).AppendJournal(new JournalDraft("test.note", "Från en läsning"))));
    }

    [Fact]
    public void The_guard_also_stops_the_cases_module_itself_from_changing_sealed_records()
    {
        var database = ((Case)_case).Database;

        foreach (var sql in new[]
        {
            "UPDATE journal SET summary = 'ändrad' WHERE sequence = 2",
            "DELETE FROM journal",
            "DROP TRIGGER seal_journal_no_delete",
            "DROP TABLE journal",
        })
        {
            using var command = database.Command(sql);
            Assert.Equal(23, Assert.Throws<SqliteException>(() => command.ExecuteNonQuery()).SqliteErrorCode);
        }

        Assert.Equal(2, _case.ReadJournal().Count);
    }

    [Fact]
    public void A_copy_of_the_database_cannot_be_written_out()
    {
        var copy = Path.Combine(_fixture.Directory, "copy.db");

        Assert.Throws<SqliteException>(() => _case.Write(transaction =>
        {
            using var command = transaction.CreateCommand($"VACUUM INTO '{copy}'");
            return command.ExecuteNonQuery();
        }));
        Assert.Throws<SqliteException>(() => _case.Read(reader =>
        {
            using var command = reader.CreateCommand($"VACUUM INTO '{copy}'");
            return command.ExecuteNonQuery();
        }));
        Assert.False(File.Exists(copy));
    }

    [Fact]
    public void Entries_in_a_write_that_fails_are_rolled_back_together()
    {
        Assert.Throws<InvalidOperationException>(() => _case.Write<int>(transaction =>
        {
            transaction.AppendJournal(new JournalDraft("test.note", "Ska inte finnas"));
            transaction.AppendSealed("test.sealed", ["anteckning", 5L]);
            throw new InvalidOperationException("Avbrutet");
        }));

        Assert.Equal(2, _case.ReadJournal().Count);
        var verification = new CaseVerifier(_fixture.Clock).Verify(_case);
        Assert.True(verification.IsIntact);
        Assert.Equal(0, verification.Chains.Single(chain => chain.Chain == "test.sealed").RecordCount);
    }

    [Fact]
    public void A_write_commits_the_journal_and_the_sealed_records_together()
    {
        var (entry, record) = _case.Write(transaction =>
            (transaction.AppendJournal(new JournalDraft("test.note", "Tredje raden")),
             transaction.AppendSealed("test.sealed", ["anteckning", 5])));

        Assert.Equal(3, entry.Sequence);
        Assert.Equal(1, record.Sequence);
        Assert.Equal(entry, _case.ReadJournal(fromSequence: 3).Single());
    }

    [Fact]
    public void Values_that_do_not_match_the_sealed_columns_are_refused()
    {
        Assert.Throws<ArgumentException>(() => _case.Write(transaction =>
            transaction.AppendSealed("test.sealed", [5L, "fel ordning"])));
        Assert.Throws<ArgumentException>(() => _case.Write(transaction =>
            transaction.AppendSealed("test.sealed", ["för få"])));
        Assert.Throws<ArgumentException>(() => _case.Write(transaction =>
            transaction.AppendSealed("test.sealed", ["fel typ", 1.5])));
        Assert.Throws<ArgumentException>(() => _case.Write(transaction =>
            transaction.AppendSealed("test.sealed", ["\ud800", 1L])));
        Assert.Throws<ArgumentException>(() => _case.Write(transaction =>
            transaction.AppendSealed("saknas", ["x", 1L])));
    }

    [Theory]
    [InlineData("", "Text")]
    [InlineData("Stora", "Text")]
    [InlineData("1kind", "Text")]
    [InlineData("kind with space", "Text")]
    [InlineData("test.note", "")]
    [InlineData("test.note", "  ")]
    public void A_draft_with_a_bad_kind_or_summary_is_refused(string kind, string summary)
    {
        Assert.Throws<ArgumentException>(() => _case.AppendJournal(new JournalDraft(kind, summary)));
    }

    [Fact]
    public void The_case_cannot_be_used_from_inside_its_own_work()
    {
        Assert.Throws<InvalidOperationException>(() => _case.Write(_ => _case.ReadJournal()));
        Assert.Equal(2, _case.ReadJournal().Count);
    }

    [Fact]
    public void A_transaction_kept_after_the_work_no_longer_works()
    {
        var kept = _case.Write(transaction => transaction);

        Assert.Throws<InvalidOperationException>(() => kept.AppendJournal(new JournalDraft("test.note", "Sent")));
        Assert.Throws<InvalidOperationException>(() => kept.CreateCommand("SELECT 1"));
    }
}
