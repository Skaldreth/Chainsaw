using Chainsaw.Modules.Cases.Contracts;
using Microsoft.Data.Sqlite;

namespace Chainsaw.Modules.Cases.Tests;

public sealed class HashChainTests : IDisposable
{
    private readonly CaseFixture _fixture = new();

    private readonly CaseStore _store;

    public HashChainTests()
    {
        _store = _fixture.CreateStore(new TestSchema());
        using var opened = _store.Create("A1");
        for (var number = 2; number <= 5; number++)
        {
            opened.AppendJournal(new JournalDraft("test.note", $"Rad {number}"));
        }

        opened.Write(transaction => transaction.AppendSealed("test.sealed", ["första", 1L]));
        opened.Write(transaction => transaction.AppendSealed("test.sealed", ["andra", 2L]));
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void An_untouched_case_verifies_and_gives_the_head_of_each_chain()
    {
        using var opened = _store.Open("A1");

        var verification = new CaseVerifier(_fixture.Clock).Verify(opened);

        Assert.True(verification.IsIntact);
        Assert.Equal(_fixture.Clock.UtcNow, verification.CheckedUtc);
        Assert.Equal(["cases.journal", "test.sealed"], verification.Chains.Select(chain => chain.Chain));
        var journal = verification.Chains[0];
        Assert.Equal(5, journal.RecordCount);
        Assert.Equal(5, journal.HeadSequence);
        Assert.Equal(opened.ReadJournal()[^1].Hash, journal.HeadHash);
        Assert.Equal(2, verification.Chains[1].HeadSequence);
    }

    [Fact]
    public void The_triggers_stop_a_change_made_with_sqlite_and_the_key()
    {
        CaseFileSurgery.Change(_fixture, "A1", connection =>
        {
            var update = Assert.Throws<SqliteException>(() =>
                CaseFileSurgery.Execute(connection, "UPDATE journal SET summary = 'ändrad' WHERE sequence = 2"));
            Assert.Contains("sealed records cannot be changed", update.Message, StringComparison.Ordinal);
            var delete = Assert.Throws<SqliteException>(() =>
                CaseFileSurgery.Execute(connection, "DELETE FROM test_sealed"));
            Assert.Contains("sealed records cannot be removed", delete.Message, StringComparison.Ordinal);
        });

        Assert.True(Verify().IsIntact);
    }

    [Fact]
    public void A_changed_record_breaks_the_chain_at_that_record()
    {
        Tamper(
            "DROP TRIGGER seal_journal_no_update",
            "UPDATE journal SET summary = 'ändrad' WHERE sequence = 3",
            HashChain.Triggers("journal")[0].Sql);

        var journal = Verify().Chains[0];

        Assert.Equal(new ChainBreak(3, ChainBreakReason.RecordChanged), journal.Break);
        Assert.Equal(2, journal.HeadSequence);
    }

    [Fact]
    public void A_changed_record_with_a_recomputed_hash_breaks_the_chain_at_the_next_record()
    {
        byte[] previous = [];
        CaseFileSurgery.Change(_fixture, "A1", connection =>
        {
            using var select = connection.CreateCommand();
            select.CommandText = "SELECT hash FROM journal WHERE sequence = 2";
            previous = (byte[])select.ExecuteScalar()!;
        });
        var values = new object?[] { "a", "b", "c", "d", "e", "f", "test.note", "ändrad", null };
        var forged = SealCodec.Hash(previous, "cases.journal", 3, values);
        Tamper(
            "DROP TRIGGER seal_journal_no_update",
            "UPDATE journal SET utc = 'a', local = 'b', time_zone = 'c', operator_name = 'd', operator_title = 'e', "
            + "operator_organization = 'f', summary = 'ändrad', details = NULL, "
            + $"hash = x'{Convert.ToHexStringLower(forged)}' WHERE sequence = 3",
            HashChain.Triggers("journal")[0].Sql);

        Assert.Equal(new ChainBreak(4, ChainBreakReason.RecordChanged), Verify().Chains[0].Break);
    }

    [Fact]
    public void A_removed_record_in_the_middle_breaks_the_chain_as_a_gap()
    {
        Tamper(
            "DROP TRIGGER seal_journal_no_delete",
            "DELETE FROM journal WHERE sequence = 3",
            HashChain.Triggers("journal")[1].Sql);

        Assert.Equal(new ChainBreak(3, ChainBreakReason.SequenceGap), Verify().Chains[0].Break);
    }

    [Fact]
    public void A_removed_last_record_breaks_the_chain_at_its_head()
    {
        Tamper(
            "DROP TRIGGER seal_test_sealed_no_delete",
            "DELETE FROM test_sealed WHERE sequence = 2",
            HashChain.Triggers("test_sealed")[1].Sql);

        var sealedChain = Verify().Chains[1];

        Assert.Equal(new ChainBreak(2, ChainBreakReason.HeadMismatch), sealedChain.Break);
        Assert.Equal(1, sealedChain.HeadSequence);
    }

    [Fact]
    public void A_removed_last_record_and_a_moved_head_still_break_the_head()
    {
        Tamper(
            "DROP TRIGGER seal_journal_no_delete",
            "DELETE FROM journal WHERE sequence = 5",
            "UPDATE chain_head SET sequence = 4 WHERE chain = 'cases.journal'",
            HashChain.Triggers("journal")[1].Sql);

        Assert.Equal(new ChainBreak(4, ChainBreakReason.HeadMismatch), Verify().Chains[0].Break);
    }

    [Fact]
    public void A_dropped_trigger_is_reported_as_missing_protection()
    {
        Tamper("DROP TRIGGER seal_journal_no_update");

        Assert.Equal(new ChainBreak(0, ChainBreakReason.ProtectionMissing), Verify().Chains[0].Break);
    }

    [Fact]
    public void An_added_trigger_on_a_sealed_table_is_reported_as_missing_protection()
    {
        Tamper("CREATE TRIGGER hide BEFORE INSERT ON journal BEGIN SELECT RAISE(IGNORE); END");

        Assert.Equal(new ChainBreak(0, ChainBreakReason.ProtectionMissing), Verify().Chains[0].Break);
    }

    [Fact]
    public void A_replaced_trigger_that_does_nothing_is_reported_as_missing_protection()
    {
        Tamper(
            "DROP TRIGGER seal_journal_no_update",
            "CREATE TRIGGER seal_journal_no_update BEFORE UPDATE ON journal BEGIN SELECT 1; END");

        Assert.Equal(new ChainBreak(0, ChainBreakReason.ProtectionMissing), Verify().Chains[0].Break);
    }

    [Fact]
    public void Adding_to_a_chain_after_a_removal_keeps_the_gap_visible()
    {
        Tamper(
            "DROP TRIGGER seal_journal_no_delete",
            "DELETE FROM journal WHERE sequence = 5",
            HashChain.Triggers("journal")[1].Sql);

        using var opened = _store.Open("A1");
        var entry = opened.AppendJournal(new JournalDraft("test.note", "Efter borttagningen"));
        var journal = new CaseVerifier(_fixture.Clock).Verify(opened).Chains[0];

        Assert.Equal(6, entry.Sequence);
        Assert.Equal(new ChainBreak(5, ChainBreakReason.SequenceGap), journal.Break);
    }

    [Fact]
    public void A_dropped_sealed_table_is_reported_as_changed()
    {
        Tamper("DROP TABLE test_sealed");

        Assert.Equal(new ChainBreak(0, ChainBreakReason.TableChanged), Verify().Chains[1].Break);
    }

    [Fact]
    public void The_hash_encoding_tells_apart_values_that_would_concatenate_alike()
    {
        var genesis = SealCodec.Genesis;

        Assert.NotEqual(
            SealCodec.Hash(genesis, "c", 1, ["ab", "c"]),
            SealCodec.Hash(genesis, "c", 1, ["a", "bc"]));
        Assert.NotEqual(
            SealCodec.Hash(genesis, "c", 1, [1L]),
            SealCodec.Hash(genesis, "c", 1, ["1"]));
        Assert.NotEqual(
            SealCodec.Hash(genesis, "c", 1, [null]),
            SealCodec.Hash(genesis, "c", 1, [string.Empty]));
        Assert.NotEqual(
            SealCodec.Hash(genesis, "c", 1, ["x"]),
            SealCodec.Hash(genesis, "c", 2, ["x"]));
        Assert.NotEqual(
            SealCodec.Hash(genesis, "c", 1, ["x"]),
            SealCodec.Hash(genesis, "d", 1, ["x"]));
        Assert.NotEqual(
            SealCodec.Hash(genesis, "c", 1, [new byte[] { 1 }]),
            SealCodec.Hash(genesis, "c", 1, [1L]));
    }

    private void Tamper(params string[] statements) =>
        CaseFileSurgery.Change(_fixture, "A1", connection =>
        {
            foreach (var statement in statements)
            {
                CaseFileSurgery.Execute(connection, statement);
            }
        });

    private CaseVerification Verify()
    {
        using var opened = _store.Open("A1");
        return new CaseVerifier(_fixture.Clock).Verify(opened);
    }
}
