using System.Data.Common;
using System.Text;
using Chainsaw.Modules.Cases.Contracts;
using Microsoft.Data.Sqlite;

namespace Chainsaw.Modules.Cases.Tests;

/// <summary>The holes the independent review found, each shut and kept shut.</summary>
public sealed class ReviewFindingTests : IDisposable
{
    private readonly CaseFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void A_command_kept_past_its_work_no_longer_runs_and_hands_out_no_connection()
    {
        using var opened = _fixture.CreateStore(new TestSchema()).Create("A1");
        DbCommand? kept = null;
        opened.Write(transaction =>
        {
            kept = transaction.CreateCommand("INSERT INTO test_notes (note) VALUES ('x')");
            Assert.Null(kept.Connection);
            Assert.Null(kept.Transaction);
            return 0;
        });

        Assert.ThrowsAny<Exception>(() => kept!.ExecuteNonQuery());
        Assert.ThrowsAny<Exception>(() => kept!.Connection = new SqliteConnection());
    }

    [Theory]
    [InlineData("PRAGMA rekey = ''")]
    [InlineData("PRAGMA key = 'x'")]
    [InlineData("PRAGMA cipher = 'sqlcipher'")]
    [InlineData("CREATE TEMP TABLE journal (x INTEGER)")]
    [InlineData("SELECT sqlite3mc_config('default:cipher', 'sqlcipher')")]
    [InlineData("SAVEPOINT s")]
    public void Changing_the_key_shadowing_tables_and_savepoints_are_refused(string sql)
    {
        using var opened = _fixture.CreateStore(new TestSchema()).Create("A1");

        var error = Assert.Throws<SqliteException>(() => opened.Write(transaction =>
        {
            using var command = transaction.CreateCommand(sql);
            return command.ExecuteNonQuery();
        }));

        Assert.True(
            error.SqliteErrorCode == 23 || error.Message.Contains("not authorized", StringComparison.Ordinal),
            error.Message);
    }

    [Fact]
    public void Between_operations_the_guard_refuses_everything_even_the_cases_module()
    {
        using var opened = _fixture.CreateStore().Create("A1");

        using var command = ((Case)opened).Database.Command("SELECT count(*) FROM journal");

        Assert.Equal(23, Assert.Throws<SqliteException>(() => command.ExecuteScalar()).SqliteErrorCode);
    }

    [Fact]
    public void A_stale_working_copy_is_not_recovered_over_a_newer_case_file()
    {
        var store = _fixture.CreateStore();
        var path = _fixture.CaseFile("A1");
        using (store.Create("A1"))
        {
        }

        var crashed = (Case)store.Open("A1");
        crashed.Abandon();
        var stale = File.ReadAllBytes(path + ".work");
        File.Delete(path + ".work");
        File.Delete(path + ".work.base");
        using (var newer = store.Open("A1"))
        {
            newer.AppendJournal(new JournalDraft("test.note", "Den nyare filen"));
        }

        var newerFile = File.ReadAllBytes(path);
        File.WriteAllBytes(path + ".work", stale);
        File.WriteAllBytes(path + ".work.base", new byte[32]);

        Assert.Equal(CaseError.Conflict, Assert.Throws<CaseException>(() => store.Open("A1")).Error);
        Assert.Equal(newerFile, File.ReadAllBytes(path));
        Assert.Equal(stale, File.ReadAllBytes(path + ".work"));
    }

    [Fact]
    public void A_sealed_chain_of_a_module_this_build_does_not_know_is_still_guarded_and_verified()
    {
        using (var created = _fixture.CreateStore(new TestSchema()).Create("A1"))
        {
            created.Write(transaction => transaction.AppendSealed("test.sealed", ["anteckning", 1L]));
        }

        using var opened = _fixture.CreateStore().Open("A1");

        Assert.Throws<SqliteException>(() => opened.Write(transaction =>
        {
            using var command = transaction.CreateCommand(
                "INSERT INTO test_sealed (sequence, note, amount, hash) VALUES (2, 'x', 1, x'00')");
            return command.ExecuteNonQuery();
        }));
        var verification = new CaseVerifier(_fixture.Clock).Verify(opened);
        Assert.True(verification.IsIntact);
        Assert.Equal(["cases.journal", "test.sealed"], verification.Chains.Select(chain => chain.Chain));
    }

    [Fact]
    public void A_head_without_a_registered_chain_is_reported()
    {
        using (_fixture.CreateStore().Create("A1"))
        {
        }

        CaseFileSurgery.Change(_fixture, "A1", connection => CaseFileSurgery.Execute(
            connection, "INSERT INTO chain_head (chain, sequence, hash) VALUES ('hidden', 3, x'00')"));
        using var opened = _fixture.CreateStore().Open("A1");

        var hidden = new CaseVerifier(_fixture.Clock).Verify(opened).Chains.Single(chain => chain.Chain == "hidden");

        Assert.Equal(new ChainBreak(0, ChainBreakReason.Unregistered), hidden.Break);
    }

    [Theory]
    [InlineData("CREATE UNIQUE INDEX dos ON journal (kind)")]
    [InlineData("DROP TRIGGER seal_case_info_no_update")]
    [InlineData("CREATE INDEX own ON test_sealed (note)")]
    public void A_step_cannot_index_protected_tables_or_drop_their_triggers(string sql)
    {
        var schema = new TestSchema(new CaseSchemaStep(2, "Elak", sql));

        Assert.Throws<InvalidOperationException>(() => _fixture.CreateStore(schema).Create("A1"));
        Assert.False(File.Exists(_fixture.CaseFile("A1")));
    }

    [Fact]
    public void A_case_replaced_by_an_older_copy_is_sealed_in_the_journal_once()
    {
        var store = _fixture.CreateStore();
        var path = _fixture.CaseFile("A1");
        using (store.Create("A1"))
        {
        }

        var older = File.ReadAllBytes(path);
        string laterHash;
        using (var opened = store.Open("A1"))
        {
            laterHash = opened.AppendJournal(new JournalDraft("test.note", "Senare")).Hash;
        }

        File.WriteAllBytes(path, older);
        using (var rolledBack = store.Open("A1"))
        {
            var entry = rolledBack.ReadJournal()[^1];
            Assert.Equal(CaseJournalKinds.AnchorsReplaced, entry.Kind);
            Assert.Contains("Diverged", entry.Details, StringComparison.Ordinal);
            Assert.Contains(laterHash, entry.Details, StringComparison.Ordinal);
            Assert.Equal(
                [CaseProblem.AnchorsReplaced], new CaseVerifier(_fixture.Clock).Verify(rolledBack).Problems);
        }

        using var again = store.Open("A1");
        Assert.Single(again.ReadJournal(), item => item.Kind == CaseJournalKinds.AnchorsReplaced);
    }

    [Fact]
    public void A_chain_cut_and_rewritten_with_the_key_is_sealed_in_the_journal()
    {
        var store = _fixture.CreateStore();
        string removedHash;
        using (var opened = store.Create("A1"))
        {
            removedHash = opened.AppendJournal(new JournalDraft("test.note", "Ska bort")).Hash;
        }

        CaseFileSurgery.Change(_fixture, "A1", connection =>
        {
            CaseFileSurgery.Execute(connection, "DROP TRIGGER seal_journal_no_delete");
            CaseFileSurgery.Execute(connection, "DELETE FROM journal WHERE sequence = 2");
            CaseFileSurgery.Execute(
                connection,
                "UPDATE chain_head SET sequence = 1, hash = (SELECT hash FROM journal WHERE sequence = 1)");
            CaseFileSurgery.Execute(connection, HashChain.Triggers("journal")[1].Sql);
        });
        using var cut = store.Open("A1");

        var entry = cut.ReadJournal(fromSequence: 2).Single();
        Assert.Equal(CaseJournalKinds.AnchorsReplaced, entry.Kind);
        Assert.Contains(removedHash, entry.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Changed_anchors_are_sealed_in_the_journal()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        var anchor = Directory.GetFiles(_fixture.AnchorDirectory).Single();
        var text = File.ReadAllText(anchor);
        File.WriteAllText(anchor, text.Replace("\"tag\":\"", "\"tag\":\"AA", StringComparison.Ordinal));
        using var reopened = store.Open("A1");

        var entry = reopened.ReadJournal()[^1];
        Assert.Equal(CaseJournalKinds.AnchorsReplaced, entry.Kind);
        Assert.Contains("Unreadable", entry.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Removed_anchors_of_a_case_this_vault_has_saved_are_sealed_in_the_journal()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        Directory.Delete(_fixture.AnchorDirectory, recursive: true);
        using var reopened = store.Open("A1");

        var entry = reopened.ReadJournal()[^1];
        Assert.Equal(CaseJournalKinds.AnchorsReplaced, entry.Kind);
        Assert.Contains("Missing", entry.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void A_colleague_without_an_own_envelope_has_no_anchors_to_miss()
    {
        string path;
        using (var opened = _fixture.CreateStore().Create("A1"))
        {
            opened.SetSharingPassword(CaseFixture.Password);
            path = opened.Path;
        }

        using var colleague = new CaseFixture("vault-b");
        using var shared = colleague.CreateStore().OpenWithPassword(path, CaseFixture.Password);

        Assert.DoesNotContain(shared.ReadJournal(), entry => entry.Kind == CaseJournalKinds.AnchorsReplaced);
    }

    [Fact]
    public void Statements_left_in_an_open_reader_get_no_more_rights_than_the_work()
    {
        using var opened = _fixture.CreateStore(new TestSchema()).Create("A1");

        // Whether the rest fails loudly or is never run, none of it may take effect.
        _ = Record.Exception(() => opened.Write(transaction =>
        {
            var command = transaction.CreateCommand(
                "SELECT 1; CREATE TRIGGER evil BEFORE INSERT ON journal BEGIN SELECT RAISE(IGNORE); END; "
                + "INSERT INTO chain_head (chain, sequence, hash) VALUES ('x', 1, x'00')");
            var reader = command.ExecuteReader();
            return reader.Read();
        }));

        opened.AppendJournal(new JournalDraft("test.note", "Syns fortfarande"));
        Assert.Equal(2, opened.ReadJournal().Count);
        var verification = new CaseVerifier(_fixture.Clock).Verify(opened);
        Assert.True(verification.IsIntact);
        Assert.DoesNotContain(verification.Chains, chain => chain.Chain == "x");
    }

    [Fact]
    public void Sql_from_another_thread_during_a_write_is_refused()
    {
        using var opened = _fixture.CreateStore(new TestSchema()).Create("A1");

        var error = opened.Write(transaction =>
        {
            using var command = transaction.CreateCommand("INSERT INTO test_notes (note) VALUES ('annan tråd')");
            Exception? failure = null;
            var thread = new Thread(() => failure = Record.Exception(() => command.ExecuteNonQuery()));
            thread.Start();
            thread.Join();
            return failure;
        });

        Assert.IsType<InvalidOperationException>(error);
    }

    [Fact]
    public void A_module_cannot_seal_in_the_journal_chain()
    {
        using var opened = _fixture.CreateStore(new TestSchema()).Create("A1");

        Assert.Throws<ArgumentException>(() => opened.Write(transaction => transaction.AppendSealed(
            "cases.journal",
            ["2001-01-01T00:00:00.0000000+00:00", "2001-01-01T01:00:00.0000000+01:00", "x", "Någon annan", "Chef",
             "Annan", CaseJournalKinds.SharingPasswordRemoved, "Förfalskad", null])));
        Assert.Single(opened.ReadJournal());
    }

    [Fact]
    public void The_transaction_refuses_every_other_thread()
    {
        using var opened = _fixture.CreateStore(new TestSchema()).Create("A1");

        var failures = opened.Write(transaction =>
        {
            var found = new List<Exception?>();
            var thread = new Thread(() =>
            {
                found.Add(Record.Exception(() => transaction.AppendSealed("test.sealed", ["x", 1L])));
                found.Add(Record.Exception(() => transaction.AppendJournal(new JournalDraft("test.note", "Annan"))));
                found.Add(Record.Exception(() => transaction.CreateCommand("SELECT 1")));
            });
            thread.Start();
            thread.Join();
            return found;
        });

        Assert.All(failures, failure => Assert.IsType<InvalidOperationException>(failure));
        Assert.Single(opened.ReadJournal());
    }

    [Fact]
    public void A_contributor_that_changes_after_the_check_changes_nothing()
    {
        var fickle = new FickleSchema();
        using var opened = _fixture.CreateStore(fickle).Create("A1");
        fickle.Columns[0] = "note, hash) VALUES (99, 'n', x'00'); DELETE FROM chain_head; --";
        fickle.Name = "cases";

        opened.Write(transaction => transaction.AppendSealed("fickle.records", ["anteckning"]));

        Assert.True(new CaseVerifier(_fixture.Clock).Verify(opened).IsIntact);
    }

    [Fact]
    public void A_reader_disposed_from_an_exception_filter_while_cases_seals_runs_nothing()
    {
        using var opened = _fixture.CreateStore(new TestSchema()).Create("A1");

        opened.Write(transaction =>
        {
            var command = transaction.CreateCommand(
                "SELECT 1; UPDATE chain_head SET sequence = 999 WHERE chain = 'cases.journal'");
            var reader = command.ExecuteReader();
            reader.Read();
            try
            {
                transaction.AppendSealed("test.sealed", [1.5m, 1L]);
            }
            catch (ArgumentException) when (DisposeAndKeepGoing(reader))
            {
            }
            catch (ArgumentException)
            {
            }

            return 0;
        });

        Assert.True(new CaseVerifier(_fixture.Clock).Verify(opened).IsIntact);
    }

    [Fact]
    public void A_reader_disposed_from_a_first_chance_handler_while_cases_seals_runs_nothing()
    {
        using var opened = _fixture.CreateStore(new TestSchema()).Create("A1");
        var thread = Environment.CurrentManagedThreadId;
        DbDataReader? reader = null;
        void Handler(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs args)
        {
            if (Environment.CurrentManagedThreadId == thread && reader is not null)
            {
                var kept = reader;
                reader = null;
                kept.Dispose();
            }
        }

        AppDomain.CurrentDomain.FirstChanceException += Handler;
        try
        {
            _ = Record.Exception(() => opened.Write(transaction =>
            {
                var command = transaction.CreateCommand(
                    "SELECT 1; UPDATE chain_head SET sequence = 4242 WHERE chain = 'cases.journal'");
                reader = command.ExecuteReader();
                reader.Read();
                return transaction.AppendSealed("test.sealed", ["x", "inte ett tal"]);
            }));
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= Handler;
        }

        Assert.True(new CaseVerifier(_fixture.Clock).Verify(opened).IsIntact);
    }

    [Fact]
    public void An_operation_cannot_be_entered_again_from_inside_another()
    {
        using var opened = _fixture.CreateStore().Create("A1");
        var thread = Environment.CurrentManagedThreadId;
        Exception? nested = null;
        var armed = true;
        void Handler(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs args)
        {
            if (armed && Environment.CurrentManagedThreadId == thread)
            {
                armed = false;
                nested = Record.Exception(() => opened.AppendJournal(new JournalDraft("test.note", "Inuti")));
            }
        }

        AppDomain.CurrentDomain.FirstChanceException += Handler;
        try
        {
            _ = Record.Exception(() => opened.SetSharingPassword("för kort"));
            _ = Record.Exception(() => opened.ReadJournal(maximumCount: -1));
            armed = true;
            opened.Save();
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= Handler;
        }

        Assert.True(nested is null or InvalidOperationException);
    }

    [Fact]
    public void A_missing_step_is_refused_as_an_argument()
    {
        Assert.Throws<ArgumentException>(() => _fixture.CreateStore(new TestSchema([null!])));
    }

    [Fact]
    public void A_module_named_cases_is_refused()
    {
        Assert.Throws<ArgumentException>(() => _fixture.CreateStore(new FickleSchema { Name = "cases" }));
    }

    [Fact]
    public void A_list_of_values_cannot_run_sql_while_the_cases_module_seals()
    {
        using var opened = _fixture.CreateStore(new TestSchema()).Create("A1");

        var error = Record.Exception(() => opened.Write(transaction =>
            transaction.AppendSealed("test.sealed", new SneakyValues(transaction))));

        Assert.Null(error);
        Assert.Single(opened.ReadJournal());
        Assert.True(new CaseVerifier(_fixture.Clock).Verify(opened).IsIntact);
    }

    [Fact]
    public void Modules_cannot_write_the_cases_modules_own_journal_kinds()
    {
        using var opened = _fixture.CreateStore().Create("A1");

        Assert.Throws<ArgumentException>(() => opened.AppendJournal(
            new JournalDraft(CaseJournalKinds.AnchorsReplaced, "Låtsas")));
        Assert.Throws<ArgumentException>(() => opened.Write(transaction =>
            transaction.AppendJournal(new JournalDraft(CaseJournalKinds.Recovered, "Låtsas"))));
    }

    [Fact]
    public void Repeated_failed_saves_still_recover_into_the_file_on_disk()
    {
        var store = _fixture.CreateStore();
        var path = _fixture.CaseFile("A1");
        using (store.Create("A1"))
        {
        }

        var opened = (Case)store.Open("A1");
        opened.AppendJournal(new JournalDraft("test.note", "Bara i arbetskopian"));
        var original = File.ReadAllBytes(path);
        File.Delete(path);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "hinder"), "x");
        Assert.ThrowsAny<IOException>(opened.Save);
        Assert.ThrowsAny<IOException>(opened.Save);
        Directory.Delete(path, recursive: true);
        File.WriteAllBytes(path, original);
        opened.Abandon();

        using var recovered = store.Open("A1");

        Assert.Contains(recovered.ReadJournal(), entry => entry.Summary == "Bara i arbetskopian");
        Assert.Equal(CaseJournalKinds.Recovered, recovered.ReadJournal()[^1].Kind);
    }

    [Fact]
    public void A_reader_hands_out_no_statement_handle()
    {
        using var opened = _fixture.CreateStore().Create("A1");

        var type = opened.Read(reader =>
        {
            using var command = reader.CreateCommand("SELECT 1");
            using var rows = command.ExecuteReader();
            return rows.GetType();
        });

        Assert.False(typeof(SqliteDataReader).IsAssignableFrom(type));
    }

    [Fact]
    public void Changed_case_information_is_reported()
    {
        using (_fixture.CreateStore().Create("A1"))
        {
        }

        CaseFileSurgery.Change(_fixture, "A1", connection =>
        {
            CaseFileSurgery.Execute(connection, "DROP TRIGGER seal_case_info_no_update");
            CaseFileSurgery.Execute(connection, "UPDATE case_info SET case_number = 'B2'");
        });
        using var opened = _fixture.CreateStore().Open("A1");

        Assert.Equal([CaseProblem.CaseInfoChanged], new CaseVerifier(_fixture.Clock).Verify(opened).Problems);
    }

    [Fact]
    public void Only_finite_numbers_are_sealed_and_negative_zero_verifies()
    {
        using var opened = _fixture.CreateStore(new RealSchema()).Create("A1");

        Assert.Throws<ArgumentException>(() => opened.Write(transaction =>
            transaction.AppendSealed("real.values", [double.NaN])));
        Assert.Throws<ArgumentException>(() => opened.Write(transaction =>
            transaction.AppendSealed("real.values", [double.PositiveInfinity])));
        opened.Write(transaction => transaction.AppendSealed("real.values", [-0.0]));
        opened.Write(transaction => transaction.AppendSealed("real.values", [1.5]));

        Assert.True(new CaseVerifier(_fixture.Clock).Verify(opened).IsIntact);
    }

    [Fact]
    public void A_header_with_duplicate_keys_is_corrupt()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        var path = _fixture.CaseFile("A1");
        var bytes = File.ReadAllBytes(path);
        var json = "{\"caseId\":\"x\",\"caseId\":\"y\",\"envelopes\":[]}"u8.ToArray();
        var prefix = bytes[..14];
        prefix[10] = 0;
        prefix[11] = 0;
        prefix[12] = 0;
        prefix[13] = (byte)json.Length;
        File.WriteAllBytes(path, [.. prefix, .. json]);

        Assert.Equal(CaseError.Corrupt, Assert.Throws<CaseException>(() => store.Open("A1")).Error);
    }

    [Fact]
    public void A_summary_is_one_line()
    {
        using var opened = _fixture.CreateStore().Create("A1");

        Assert.Throws<ArgumentException>(() => opened.AppendJournal(new JournalDraft("test.note", "två\nrader")));
    }

    [Fact]
    public void The_anchors_hold_no_case_content()
    {
        using (_fixture.CreateStore().Create("HEMLIGT-NUMMER"))
        {
        }

        var anchor = Directory.GetFiles(_fixture.AnchorDirectory).Single();

        Assert.DoesNotContain("HEMLIGT-NUMMER", File.ReadAllText(anchor, Encoding.UTF8), StringComparison.Ordinal);
    }

    /// <summary>A list of values whose indexer tries to run SQL, as a module could.</summary>
    private sealed class SneakyValues(ICaseTransaction transaction) : IReadOnlyList<object?>
    {
        public int Count => 2;

        public object? this[int index]
        {
            get
            {
                // Runs while the list is copied with the work's rights: the journal stays out of reach.
                _ = Record.Exception(() =>
                {
                    using var command = transaction.CreateCommand("DELETE FROM chain_head");
                    command.ExecuteNonQuery();
                });
                return index == 0 ? "anteckning" : 1L;
            }
        }

        public IEnumerator<object?> GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
            {
                yield return this[index];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static bool DisposeAndKeepGoing(DbDataReader reader)
    {
        reader.Dispose();
        return false;
    }

    /// <summary>A contributor that changes its answers after the store has checked them.</summary>
    private sealed class FickleSchema : ICaseSchemaContributor
    {
        public string Name { get; set; } = "fickle";

        public List<string> Columns { get; } = ["note"];

        public string Module => Name;

        public IReadOnlyList<CaseSchemaStep> Steps { get; } =
        [
            new(
                1,
                "Poster",
                "CREATE TABLE fickle_records (sequence INTEGER PRIMARY KEY, note TEXT, hash BLOB NOT NULL) STRICT;"),
        ];

        public IReadOnlyList<SealedChain> SealedChains => [new("fickle.records", "fickle_records", Columns)];
    }

    private sealed class RealSchema : ICaseSchemaContributor
    {
        public string Module => "real";

        public IReadOnlyList<CaseSchemaStep> Steps { get; } =
        [
            new(
                1,
                "Tal",
                "CREATE TABLE real_values (sequence INTEGER PRIMARY KEY, value REAL, hash BLOB NOT NULL) STRICT;"),
        ];

        public IReadOnlyList<SealedChain> SealedChains { get; } = [new("real.values", "real_values", ["value"])];
    }
}
