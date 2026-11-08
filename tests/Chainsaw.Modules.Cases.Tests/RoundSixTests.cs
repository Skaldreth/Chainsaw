using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases.Tests;

/// <summary>
/// The seal over the whole case file, the limit on envelopes, the sharing password in normalization form C, the save
/// that a lock overtakes, the generation of the anchors and the check over the recovery envelopes.
/// </summary>
public sealed class RoundSixTests : IDisposable
{
    /// <summary>The page size of the databases (SQLite3 Multiple Ciphers' default).</summary>
    private const int PageSize = 4096;

    private readonly CaseFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void A_header_changed_without_the_file_key_is_refused_and_leaves_no_working_copy()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        var path = _fixture.CaseFile("A1");
        CaseFileSurgery.RewriteHeaderWithoutKey(path, header => header.WithEnvelopes(
            [.. header.Envelopes, new JsonObject { ["kind"] = "okänd", ["data"] = "inplanterad" }]));

        Assert.Equal(CaseError.Corrupt, Assert.Throws<CaseException>(() => store.Open("A1")).Error);
        Assert.False(File.Exists(path + ".work"));
        Assert.False(File.Exists(path + ".work.tmp"));
    }

    [Fact]
    public void A_header_changed_without_the_key_is_refused_also_when_a_working_copy_is_taken_back()
    {
        var store = _fixture.CreateStore();
        var path = _fixture.CaseFile("A1");
        using (var created = store.Create("A1"))
        {
            created.SetSharingPassword(CaseFixture.Password);
        }

        ((Case)store.Open("A1")).Abandon();
        var workingCopy = File.ReadAllBytes(path + ".work");

        // Someone who can write in the folder takes the sharing envelope out and records the changed file as the base.
        CaseFileSurgery.RewriteHeaderWithoutKey(path, header => header.WithEnvelopes(
            header.Envelopes.Where(item => !KeyEnvelopes.IsKind(item, KeyEnvelopes.PasswordKind))));
        File.WriteAllBytes(path + ".work.base", SHA256.HashData(File.ReadAllBytes(path)));

        Assert.Equal(CaseError.Corrupt, Assert.Throws<CaseException>(() => store.Open("A1")).Error);
        Assert.Equal(workingCopy, File.ReadAllBytes(path + ".work"));
    }

    [Fact]
    public void A_working_copy_is_still_taken_back_into_a_sound_file()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        var crashed = (Case)store.Open("A1");
        crashed.AppendJournal(new JournalDraft("test.note", "Före kraschen"));
        crashed.Abandon();

        using var recovered = store.Open("A1");
        Assert.Contains(recovered.ReadJournal(), entry => entry.Summary == "Före kraschen");
        Assert.Contains(recovered.ReadJournal(), entry => entry.Kind == CaseJournalKinds.Recovered);
    }

    [Fact]
    public void An_older_page_of_the_same_case_swapped_in_is_refused()
    {
        var store = _fixture.CreateStore(new TestSchema());
        var path = _fixture.CaseFile("A1");
        using (var created = store.Create("A1"))
        {
            AddNotes(created, 50);
        }

        var older = File.ReadAllBytes(path);
        using (var opened = store.Open("A1"))
        {
            AddNotes(opened, 50);
        }

        var newer = File.ReadAllBytes(path);
        var (_, olderDatabase) = CaseFileSurgery.Split(older);
        var (_, newerDatabase) = CaseFileSurgery.Split(newer);
        var databaseStart = newer.Length - CaseFileSeal.TrailerLength - newerDatabase.Length;
        var page = FirstDifferentPage(olderDatabase, newerDatabase);
        Assert.True(page >= 0, "The two versions should differ in some page.");
        olderDatabase.AsSpan(page * PageSize, PageSize).CopyTo(newer.AsSpan(databaseStart + (page * PageSize)));
        File.WriteAllBytes(path, newer);

        Assert.Equal(CaseError.Corrupt, Assert.Throws<CaseException>(() => store.Open("A1")).Error);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-CaseFileSeal.TagSize)]
    [InlineData(1)]
    public void A_file_cut_short_or_with_bytes_added_at_the_end_is_refused(int change)
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        var path = _fixture.CaseFile("A1");
        var bytes = File.ReadAllBytes(path);
        var changed = change < 0 ? bytes[..(bytes.Length + change)] : [.. bytes, .. new byte[change]];
        File.WriteAllBytes(path, changed);

        Assert.Equal(CaseError.Corrupt, Assert.Throws<CaseException>(() => store.Open("A1")).Error);
    }

    [Fact]
    public void A_case_file_of_the_format_before_the_seal_is_refused_as_outdated()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        var path = _fixture.CaseFile("A1");
        var bytes = File.ReadAllBytes(path);
        bytes[9] = 1;
        File.WriteAllBytes(path, bytes[..^CaseFileSeal.TrailerLength]);

        Assert.Equal(CaseError.OutdatedFormat, Assert.Throws<CaseException>(() => store.Open("A1")).Error);
    }

    [Fact]
    public void A_header_rewritten_with_the_file_key_opens()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        var path = _fixture.CaseFile("A1");
        CaseFileSurgery.RewriteHeader(_fixture, path, header => header);

        using var reopened = store.Open("A1");
        Assert.Equal("A1", reopened.Info.CaseNumber);
    }

    [Fact]
    public void An_envelope_for_this_vault_is_refused_when_the_header_is_full_and_nothing_is_journalled()
    {
        string path;
        using (var created = _fixture.CreateStore().Create("A1"))
        {
            created.SetSharingPassword(CaseFixture.Password);
            path = created.Path;
        }

        // The colleague's file is full: their own vault's envelope would be the 65th.
        CaseFileSurgery.RewriteHeader(_fixture, path, header => header.WithEnvelopes(header.Envelopes.Concat(
            Enumerable.Range(0, CaseContainer.MaximumEnvelopes - header.Envelopes.Count)
                .Select(_ => new JsonObject { ["kind"] = "okänd" }))));
        using var colleague = new CaseFixture("vault-b");
        using var shared = colleague.CreateStore().OpenWithPassword(path, CaseFixture.Password);
        var before = shared.ReadJournal().Count;

        Assert.Equal(CaseError.TooManyEnvelopes, Assert.Throws<CaseException>(shared.AddOperatorAccess).Error);
        Assert.Equal(before, shared.ReadJournal().Count);
    }

    [Fact]
    public void A_sharing_password_is_refused_when_the_header_is_full_but_may_replace_the_one_there()
    {
        string path;
        using (var created = _fixture.CreateStore().Create("A1"))
        {
            path = created.Path;
        }

        CaseFileSurgery.RewriteHeader(_fixture, path, header => header.WithEnvelopes(header.Envelopes.Concat(
            Enumerable.Range(0, CaseContainer.MaximumEnvelopes - header.Envelopes.Count)
                .Select(_ => new JsonObject { ["kind"] = "okänd" }))));
        using (var full = _fixture.CreateStore().Open("A1"))
        {
            Assert.Equal(
                CaseError.TooManyEnvelopes,
                Assert.Throws<CaseException>(() => full.SetSharingPassword(CaseFixture.Password)).Error);
        }

        CaseFileSurgery.RewriteHeader(_fixture, path, header => header.WithEnvelopes(header.Envelopes.SkipLast(1)));
        using var room = _fixture.CreateStore().Open("A1");
        room.SetSharingPassword(CaseFixture.Password);
        room.SetSharingPassword(CaseFixture.Password + " igen");
        Assert.True(room.HasSharingPassword);
    }

    [Fact]
    public void A_sharing_password_typed_with_another_input_method_opens_the_case()
    {
        const string Composed = "lösenordet på åkern";
        string path;
        using (var created = _fixture.CreateStore().Create("A1"))
        {
            created.SetSharingPassword(Composed);
            path = created.Path;
        }

        var decomposed = Composed.Normalize(System.Text.NormalizationForm.FormD);
        Assert.NotEqual(Composed, decomposed);
        using var colleague = new CaseFixture("vault-b");
        using var shared = colleague.CreateStore().OpenWithPassword(path, decomposed);

        Assert.Equal("A1", shared.Info.CaseNumber);
    }

    [Fact]
    public void A_save_that_a_lock_overtakes_writes_the_file_and_says_so()
    {
        var store = _fixture.CreateStore();
        var opened = (Case)store.Create("A1");
        opened.AppendJournal(new JournalDraft("test.note", "Före låsningen"));
        opened.SaveStarting = () => _fixture.Vault.Lock();

        var error = Assert.Throws<CaseException>(opened.Save);

        Assert.Equal(CaseError.LockedWhileSaving, error.Error);
        store.WaitForClosing();
        _fixture.Vault.Unlock([]);
        using var reopened = store.Open("A1");
        Assert.Contains(reopened.ReadJournal(), entry => entry.Summary == "Före låsningen");
    }

    [Fact]
    public void An_anchor_file_replaced_by_an_older_one_is_sealed_in_the_journal()
    {
        var store = _fixture.CreateStore();
        var path = _fixture.CaseFile("A1");
        using (store.Create("A1"))
        {
        }

        var anchor = Directory.GetFiles(_fixture.AnchorDirectory).Single();
        var olderAnchor = File.ReadAllBytes(anchor);
        var olderCase = File.ReadAllBytes(path);
        using (var opened = store.Open("A1"))
        {
            opened.AppendJournal(new JournalDraft("test.note", "Senare"));
        }

        // Both put back together: the case reaches its anchors, but the vault has written later ones.
        File.WriteAllBytes(anchor, olderAnchor);
        File.WriteAllBytes(path, olderCase);
        using (var rolledBack = store.Open("A1"))
        {
            var entry = rolledBack.ReadJournal()[^1];
            Assert.Equal(CaseJournalKinds.AnchorsReplaced, entry.Kind);
            Assert.Contains("RolledBack", entry.Details, StringComparison.Ordinal);
            Assert.Contains("\"heads\":[{", entry.Details, StringComparison.Ordinal);
        }

        using var again = store.Open("A1");
        Assert.Single(again.ReadJournal(), item => item.Kind == CaseJournalKinds.AnchorsReplaced);
    }

    [Fact]
    public void Removed_anchors_are_missed_by_the_vault_also_without_an_own_envelope()
    {
        string path;
        using (var created = _fixture.CreateStore().Create("A1"))
        {
            created.SetSharingPassword(CaseFixture.Password);
            path = created.Path;
        }

        using var colleague = new CaseFixture("vault-b");
        using (colleague.CreateStore().OpenWithPassword(path, CaseFixture.Password))
        {
        }

        Directory.Delete(colleague.AnchorDirectory, recursive: true);
        using var reopened = colleague.CreateStore().OpenWithPassword(path, CaseFixture.Password);

        var entry = reopened.ReadJournal().Single(item => item.Kind == CaseJournalKinds.AnchorsReplaced);
        Assert.Contains("Missing", entry.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Unreadable_anchors_are_journalled_with_the_heads_of_the_chains()
    {
        var store = _fixture.CreateStore();
        string head;
        using (var created = store.Create("A1"))
        {
            head = created.ReadJournal()[^1].Hash;
        }

        var anchor = Directory.GetFiles(_fixture.AnchorDirectory).Single();
        File.WriteAllText(anchor, "inte json");
        using var reopened = store.Open("A1");

        var entry = reopened.ReadJournal()[^1];
        Assert.Contains("Unreadable", entry.Details, StringComparison.Ordinal);
        Assert.Contains(head, entry.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void A_damaged_recovery_envelope_with_the_right_fingerprint_is_replaced_when_the_case_is_opened()
    {
        using var privateKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        _fixture.RecoveryKey = RecoveryPublicKey.FromPrivateKey(privateKey);
        using (_fixture.CreateStore().Create("A1"))
        {
        }

        var path = _fixture.CaseFile("A1");
        CaseFileSurgery.RewriteHeader(_fixture, path, header => header.WithEnvelopes(header.Envelopes.Select(item =>
        {
            var copy = (JsonObject)item.DeepClone();
            if (KeyEnvelopes.IsKind(copy, RecoveryEnvelopes.Kind))
            {
                var key = Convert.FromBase64String(copy["key"]!.GetValue<string>());
                key[0] ^= 1;
                copy["key"] = Convert.ToBase64String(key);
            }

            return copy;
        })));

        using (var opened = _fixture.CreateStore().Open("A1"))
        {
            var entry = opened.ReadJournal().Last(item => item.Kind == CaseJournalKinds.RecoveryAccessChanged);
            Assert.Contains("skadat", entry.Summary, StringComparison.Ordinal);
            Assert.Contains("\"replaced\":1", entry.Details, StringComparison.Ordinal);
        }

        using var administrator = new CaseFixture("vault-admin");
        using var recovered = administrator.CreateStore().OpenWithRecoveryKey(path, privateKey, "Admin Adminsson");
        Assert.Equal("A1", recovered.Info.CaseNumber);
        using var stream = File.OpenRead(path);
        Assert.Single(CaseContainer.ReadHeader(stream).Envelopes, item => KeyEnvelopes.IsKind(item, "recovery"));
    }

    [Fact]
    public void A_sound_recovery_envelope_is_left_alone()
    {
        using var privateKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        _fixture.RecoveryKey = RecoveryPublicKey.FromPrivateKey(privateKey);
        using (_fixture.CreateStore().Create("A1"))
        {
        }

        using var opened = _fixture.CreateStore().Open("A1");
        Assert.DoesNotContain(opened.ReadJournal(), item => item.Kind == CaseJournalKinds.RecoveryAccessChanged);
    }

    [Fact]
    public void A_colleagues_envelope_carries_her_name_for_those_who_can_open_the_case_only()
    {
        string path;
        using (var created = _fixture.CreateStore().Create("A1"))
        {
            created.SetSharingPassword(CaseFixture.Password);
            path = created.Path;
        }

        using var colleague = new CaseFixture("vault-b");
        colleague.Profiles.Profile = new("Kollega Kollegasson", "Analytiker", "Andra enheten");
        using (var shared = colleague.CreateStore().OpenWithPassword(path, CaseFixture.Password))
        {
            shared.AddOperatorAccess();
        }

        using (var opened = _fixture.CreateStore().Open("A1"))
        {
            var envelopes = opened.DescribeEnvelopes();
            Assert.Equal("Testa Testsson", Assert.Single(envelopes, item => item.Kind == CaseEnvelopeKind.Yours).Name);
            var other = Assert.Single(envelopes, item => item.Kind == CaseEnvelopeKind.OtherVault);
            Assert.Equal("Kollega Kollegasson", other.Name);
        }

        // Without the file key the name is not read, and it is nowhere in the file as text.
        Assert.All(_fixture.CreateStore().DescribeEnvelopes(path), item => Assert.Null(item.Name));
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(-1, bytes.AsSpan().IndexOf("Kollegasson"u8));
    }

    [Fact]
    public void A_name_moved_from_one_envelope_to_another_is_not_read()
    {
        string path;
        using (var created = _fixture.CreateStore().Create("A1"))
        {
            created.SetSharingPassword(CaseFixture.Password);
            path = created.Path;
        }

        using var colleague = new CaseFixture("vault-b");
        colleague.Profiles.Profile = new("Kollega Kollegasson", "Analytiker", "Andra enheten");
        using (var shared = colleague.CreateStore().OpenWithPassword(path, CaseFixture.Password))
        {
            shared.AddOperatorAccess();
        }

        CaseFileSurgery.RewriteHeader(_fixture, path, header =>
        {
            var operators = header.Envelopes.Where(item => KeyEnvelopes.IsKind(item, KeyEnvelopes.OperatorKind))
                .Select(item => (JsonObject)item.DeepClone()).ToList();
            (operators[0]["label"], operators[1]["label"]) =
                (operators[1]["label"]!.DeepClone(), operators[0]["label"]!.DeepClone());
            return header.WithEnvelopes(
                [.. operators, .. header.Envelopes.Where(item => !KeyEnvelopes.IsKind(item, KeyEnvelopes.OperatorKind))]);
        });

        using var opened = _fixture.CreateStore().Open("A1");
        Assert.All(opened.DescribeEnvelopes().Where(item => item.Kind is CaseEnvelopeKind.Yours
            or CaseEnvelopeKind.OtherVault), item => Assert.Null(item.Name));
    }

    [Fact]
    public void An_unknown_envelope_says_its_kind_cleaned()
    {
        using (_fixture.CreateStore().Create("A1"))
        {
        }

        CaseFileSurgery.RewriteHeader(_fixture, _fixture.CaseFile("A1"), header => header.WithEnvelopes(
            [.. header.Envelopes, new JsonObject { ["kind"] = "fido2\n" + new string('x', 100) }]));

        using var opened = _fixture.CreateStore().Open("A1");
        var unknown = Assert.Single(opened.DescribeEnvelopes(), item => item.Kind == CaseEnvelopeKind.Unknown);
        Assert.Equal("fido2" + new string('x', 27), unknown.UnknownKind);
    }

    [Fact]
    public void A_case_only_read_is_not_written_again_when_it_closes()
    {
        var store = _fixture.CreateStore();
        var path = _fixture.CaseFile("A1");
        using (store.Create("A1"))
        {
        }

        var before = File.ReadAllBytes(path);
        using (var opened = store.Open("A1"))
        {
            Assert.NotEmpty(opened.ReadJournal());
        }

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".work"));
    }

    [Fact]
    public void A_case_written_is_saved_when_it_closes()
    {
        var store = _fixture.CreateStore();
        using (store.Create("A1"))
        {
        }

        using (var opened = store.Open("A1"))
        {
            opened.AppendJournal(new JournalDraft("test.note", "Skrivet"));
        }

        using var reopened = store.Open("A1");
        Assert.Contains(reopened.ReadJournal(), entry => entry.Summary == "Skrivet");
    }

    private static int FirstDifferentPage(byte[] older, byte[] newer)
    {
        var pages = Math.Min(older.Length, newer.Length) / PageSize;
        for (var page = 1; page < pages; page++)
        {
            if (!older.AsSpan(page * PageSize, PageSize).SequenceEqual(newer.AsSpan(page * PageSize, PageSize)))
            {
                return page;
            }
        }

        return -1;
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
}
