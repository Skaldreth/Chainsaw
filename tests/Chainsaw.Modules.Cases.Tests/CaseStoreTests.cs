using System.Text;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases.Tests;

public sealed class CaseStoreTests : IDisposable
{
    private readonly CaseFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void A_created_case_opens_again_with_the_operators_envelope()
    {
        var store = _fixture.CreateStore();
        CaseInfo created;
        using (var opened = store.Create("5000-K1-26"))
        {
            created = opened.Info;
            opened.AppendJournal(new JournalDraft("test.note", "En anteckning"));
        }

        Assert.False(File.Exists(_fixture.CaseFile("5000-K1-26") + ".work"));
        Assert.False(File.Exists(_fixture.CaseFile("5000-K1-26") + ".lock"));
        using var reopened = store.Open("5000-K1-26");

        Assert.Equal(created, reopened.Info);
        Assert.Equal("5000-K1-26", reopened.Info.CaseNumber);
        Assert.Equal(["case.created", "test.note"], reopened.ReadJournal().Select(entry => entry.Kind));
    }

    [Fact]
    public void The_first_journal_entry_says_who_created_the_case_and_when_in_utc_and_local_time()
    {
        var store = _fixture.CreateStore();
        using var opened = store.Create("A1");

        var entry = Assert.Single(opened.ReadJournal());

        Assert.Equal(1, entry.Sequence);
        Assert.Equal(CaseJournalKinds.Created, entry.Kind);
        Assert.Equal(_fixture.Profiles.Profile, entry.Operator);
        Assert.Equal(_fixture.Clock.UtcNow, entry.Utc);
        Assert.Equal(TimeSpan.Zero, entry.Utc.Offset);
        Assert.Equal(TimeSpan.FromHours(1), entry.Local.Offset);
        Assert.Equal(entry.Utc, entry.Local);
        Assert.Equal("Chainsaw/Test", entry.TimeZone);
        Assert.Contains(opened.Info.Id.ToString("D"), entry.Details, StringComparison.Ordinal);
        Assert.Equal(64, entry.Hash.Length);
    }

    [Fact]
    public void Another_vault_cannot_open_the_case()
    {
        using (_fixture.CreateStore().Create("A1"))
        {
        }

        using var colleague = new CaseFixture("vault-b", _fixture.Directory);
        var error = Assert.Throws<CaseException>(() => colleague.CreateStore().Open("A1"));

        Assert.Equal(CaseError.NoAccess, error.Error);
        Assert.False(File.Exists(_fixture.CaseFile("A1") + ".work"));
        Assert.False(File.Exists(_fixture.CaseFile("A1") + ".lock"));
    }

    [Fact]
    public void Nothing_opens_while_the_vault_is_locked()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        _fixture.Vault.Lock();

        Assert.Equal(CaseError.VaultLocked, Assert.Throws<CaseException>(() => store.Open("A1")).Error);
        Assert.Equal(CaseError.VaultLocked, Assert.Throws<CaseException>(() => store.Create("A2")).Error);
    }

    [Fact]
    public void Locking_the_vault_saves_and_closes_every_open_case()
    {
        var store = _fixture.CreateStore();
        var opened = store.Create("A1");
        opened.AppendJournal(new JournalDraft("test.note", "Före låsningen"));

        _fixture.Vault.Lock();

        Assert.True(opened.IsClosed);
        Assert.Equal(CaseError.Closed, Assert.Throws<CaseException>(() => opened.Save()).Error);
        store.WaitForClosing();
        Assert.Equal(CaseError.Closed, Assert.Throws<CaseException>(() => opened.ReadJournal()).Error);
        _fixture.Vault.Unlock([]);
        using var reopened = store.Open("A1");
        Assert.Equal(2, reopened.ReadJournal().Count);
    }

    [Fact]
    public void A_case_number_that_exists_is_not_created_again()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        var before = File.ReadAllBytes(_fixture.CaseFile("A1"));

        Assert.Equal(CaseError.AlreadyExists, Assert.Throws<CaseException>(() => store.Create("A1")).Error);
        Assert.Equal(before, File.ReadAllBytes(_fixture.CaseFile("A1")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../A1")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData(".hidden")]
    [InlineData("trailing ")]
    [InlineData("trailing.")]
    public void A_case_number_that_is_not_a_safe_file_name_is_refused(string caseNumber)
    {
        var store = _fixture.CreateStore();

        Assert.Equal(CaseError.InvalidCaseNumber, Assert.Throws<CaseException>(() => store.Create(caseNumber)).Error);
        Assert.Equal(CaseError.InvalidCaseNumber, Assert.Throws<CaseException>(() => store.Open(caseNumber)).Error);
    }

    [Fact]
    public void A_missing_case_is_not_found()
    {
        var error = Assert.Throws<CaseException>(() => _fixture.CreateStore().Open("Saknas"));

        Assert.Equal(CaseError.NotFound, error.Error);
    }

    [Fact]
    public void A_case_is_only_open_once()
    {
        var store = _fixture.CreateStore();
        using var opened = store.Create("A1");

        Assert.Equal(CaseError.InUse, Assert.Throws<CaseException>(() => store.Open("A1")).Error);
        Assert.Equal(CaseError.InUse, Assert.Throws<CaseException>(() => _fixture.CreateStore().Open("A1")).Error);
    }

    [Fact]
    public void Creating_needs_an_operator_profile()
    {
        _fixture.Profiles.Profile = null;

        var error = Assert.Throws<CaseException>(() => _fixture.CreateStore().Create("A1"));

        Assert.Equal(CaseError.NoOperatorProfile, error.Error);
        Assert.False(File.Exists(_fixture.CaseFile("A1")));
    }

    [Fact]
    public void The_list_shows_the_case_files_by_number_and_nothing_else()
    {
        var store = _fixture.CreateStore();
        using (store.Create("B2"))
        {
        }

        using var open = store.Create("A1");

        var listed = store.List();

        Assert.Equal(["A1", "B2"], listed.Select(file => file.CaseNumber));
        Assert.All(listed, file => Assert.True(file.Size > 0));
    }

    [Fact]
    public void The_list_is_empty_before_the_folder_exists()
    {
        Assert.Empty(_fixture.CreateStore().List());
    }

    [Fact]
    public void The_case_file_and_the_working_copy_hold_no_plaintext()
    {
        const string Marker = "HEMLIG-MARKÖR-4711";
        var store = _fixture.CreateStore();
        using (var opened = store.Create("K-1234"))
        {
            opened.AppendJournal(new JournalDraft("test.note", Marker, Marker));
            opened.Save();
            AssertHasNoPlaintext(ReadShared(_fixture.CaseFile("K-1234") + ".work"), Marker);
        }

        var bytes = File.ReadAllBytes(_fixture.CaseFile("K-1234"));
        AssertHasNoPlaintext(bytes, Marker);
        AssertHasNoPlaintext(bytes, "K-1234");
        AssertHasNoPlaintext(bytes, "Testa Testsson");
        AssertHasNoPlaintext(bytes, "SQLite format 3");
        AssertHasNoPlaintext(bytes, "journal");
    }

    [Fact]
    public void A_case_file_of_a_higher_format_version_is_refused()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        var bytes = File.ReadAllBytes(_fixture.CaseFile("A1"));
        bytes[9] = 2;
        File.WriteAllBytes(_fixture.CaseFile("A1"), bytes);

        Assert.Equal(CaseError.UnsupportedVersion, Assert.Throws<CaseException>(() => store.Open("A1")).Error);
    }

    [Theory]
    [InlineData(0, (byte)'X')]
    [InlineData(9, 0)]
    [InlineData(10, 0xFF)]
    public void A_damaged_header_is_refused_as_corrupt(int offset, byte value)
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        var bytes = File.ReadAllBytes(_fixture.CaseFile("A1"));
        bytes[offset] = value;
        File.WriteAllBytes(_fixture.CaseFile("A1"), bytes);

        Assert.Equal(CaseError.Corrupt, Assert.Throws<CaseException>(() => store.Open("A1")).Error);
    }

    [Fact]
    public void A_changed_byte_anywhere_in_the_database_is_found()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        var pristine = File.ReadAllBytes(_fixture.CaseFile("A1"));
        using var stream = new MemoryStream(pristine);
        CaseContainer.ReadHeader(stream);
        for (var offset = (int)stream.Position + 64; offset < pristine.Length; offset += 4096)
        {
            var bytes = (byte[])pristine.Clone();
            bytes[offset] ^= 0x01;
            File.WriteAllBytes(_fixture.CaseFile("A1"), bytes);

            Assert.False(OpensIntact(store), $"A change at byte {offset} was not found.");
        }
    }

    [Fact]
    public void An_envelope_of_an_unknown_kind_is_kept_when_the_file_is_saved()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        var path = _fixture.CaseFile("A1");
        CaseHeader header;
        byte[] database;
        using (var stream = File.OpenRead(path))
        {
            header = CaseContainer.ReadHeader(stream);
            using var rest = new MemoryStream();
            stream.CopyTo(rest);
            database = rest.ToArray();
        }

        var future = new System.Text.Json.Nodes.JsonObject { ["kind"] = "recovery", ["data"] = "framtid" };
        using (var output = File.Create(path))
        {
            CaseContainer.WriteHeader(output, header.WithEnvelopes([.. header.Envelopes, future]));
            output.Write(database);
        }

        using (var opened = store.Open("A1"))
        {
            opened.AppendJournal(new JournalDraft("test.note", "Sparas med det okända kuvertet"));
        }

        using var reread = File.OpenRead(path);
        Assert.Contains(
            CaseContainer.ReadHeader(reread).Envelopes, envelope => KeyEnvelopes.IsKind(envelope, "recovery"));
    }

    [Fact]
    public void An_interrupted_session_is_recovered_with_a_journal_entry()
    {
        var store = _fixture.CreateStore();
        var opened = (Case)store.Create("A1");
        opened.AppendJournal(new JournalDraft("test.note", "Bara i arbetskopian"));
        opened.Abandon();

        using var recovered = store.Open("A1");

        Assert.Equal(
            ["case.created", "test.note", CaseJournalKinds.Recovered],
            recovered.ReadJournal().Select(entry => entry.Kind));
    }

    [Fact]
    public void A_working_copy_that_belongs_to_another_file_is_left_alone()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        var workingCopy = _fixture.CaseFile("A1") + ".work";
        File.WriteAllBytes(workingCopy, new byte[8192]);

        Assert.Equal(CaseError.Conflict, Assert.Throws<CaseException>(() => store.Open("A1")).Error);
        Assert.Equal(new byte[8192], File.ReadAllBytes(workingCopy));
    }

    [Fact]
    public void A_rollback_journal_left_without_its_working_copy_is_not_played_into_a_new_one()
    {
        var store = _fixture.CreateStore(new TestSchema());
        using (var created = store.Create("A1"))
        {
            AddNotes(created, 200);
        }

        // A rollback journal of the file as it is now, as a session that crashed mid-write leaves it.
        var stale = Array.Empty<byte>();
        CaseFileSurgery.Change(_fixture, "A1", connection =>
        {
            CaseFileSurgery.Execute(connection, "PRAGMA cache_size = 1");
            using var transaction = connection.BeginTransaction();
            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM test_notes";
            delete.ExecuteNonQuery();
            stale = File.ReadAllBytes(_fixture.CaseFile("A1") + ".surgery-journal");
        });
        using (var later = store.Open("A1"))
        {
            AddNotes(later, 200);
        }

        File.WriteAllBytes(_fixture.CaseFile("A1") + ".work-journal", stale);

        Assert.NotEmpty(stale);
        Assert.True(OpensIntact(store));
    }

    private static void AddNotes(ICase opened, int count) =>
        opened.Write(transaction =>
        {
            for (var index = 0; index < count; index++)
            {
                using var insert = transaction.CreateCommand("INSERT INTO test_notes (note) VALUES ($note)");
                insert.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("$note", new string('x', 500)));
                insert.ExecuteNonQuery();
            }

            return transaction.AppendJournal(new JournalDraft("test.note", $"{count} anteckningar"));
        });

    private bool OpensIntact(CaseStore store)
    {
        try
        {
            using var opened = store.Open("A1");
            return new CaseVerifier(_fixture.Clock).Verify(opened).IsIntact;
        }
        catch (CaseException exception) when (exception.Error == CaseError.Corrupt)
        {
            return false;
        }
    }

    private static void AssertHasNoPlaintext(byte[] bytes, string text)
    {
        Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(text)));
        Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes(text)));
    }

    /// <summary>
    /// Reads a file that SQLite still has open for writing; on Windows <see cref="File.ReadAllBytes"/> asks the
    /// writer to share read only and is refused.
    /// </summary>
    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
