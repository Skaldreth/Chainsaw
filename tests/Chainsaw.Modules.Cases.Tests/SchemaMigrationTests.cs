using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases.Tests;

public sealed class SchemaMigrationTests : IDisposable
{
    private static readonly CaseSchemaStep Step2 = new(2, "Steg två", "INSERT INTO test_log (step) VALUES (2);");

    private static readonly CaseSchemaStep Step3 = new(
        3, "Steg tre", "ALTER TABLE test_notes ADD COLUMN extra TEXT; INSERT INTO test_log (step) VALUES (3);");

    private readonly CaseFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Steps_run_once_and_in_order()
    {
        using (_fixture.CreateStore(new TestSchema(Step2)).Create("A1"))
        {
        }

        using var reopened = _fixture.CreateStore(new TestSchema(Step2)).Open("A1");

        Assert.Equal([1L, 2L], Log(reopened));
        Assert.DoesNotContain(reopened.ReadJournal(), entry => entry.Kind == CaseJournalKinds.SchemaUpgraded);
    }

    [Fact]
    public void Only_new_steps_run_when_a_newer_build_opens_the_case_and_the_journal_says_so()
    {
        using (_fixture.CreateStore(new TestSchema()).Create("A1"))
        {
        }

        using var upgraded = _fixture.CreateStore(new TestSchema(Step2, Step3)).Open("A1");

        Assert.Equal([1L, 2L, 3L], Log(upgraded));
        var entry = upgraded.ReadJournal()[^1];
        Assert.Equal(CaseJournalKinds.SchemaUpgraded, entry.Kind);
        Assert.Equal("[\"test 2\",\"test 3\"]", entry.Details);
    }

    [Fact]
    public void A_module_added_later_gets_its_tables_and_triggers_when_the_case_is_opened()
    {
        using (_fixture.CreateStore().Create("A1"))
        {
        }

        using var opened = _fixture.CreateStore(new TestSchema()).Open("A1");
        opened.Write(transaction => transaction.AppendSealed("test.sealed", ["anteckning", 1L]));

        var verification = new CaseVerifier(_fixture.Clock).Verify(opened);
        Assert.True(verification.IsIntact);
        Assert.Equal(1, verification.Chains.Single(chain => chain.Chain == "test.sealed").RecordCount);
    }

    [Fact]
    public void A_case_written_by_a_newer_build_is_refused()
    {
        using (_fixture.CreateStore(new TestSchema(Step2)).Create("A1"))
        {
        }

        var error = Assert.Throws<CaseException>(() => _fixture.CreateStore(new TestSchema()).Open("A1"));

        Assert.Equal(CaseError.UnsupportedVersion, error.Error);
        Assert.False(File.Exists(_fixture.CaseFile("A1") + ".work"));
        using var stillOpens = _fixture.CreateStore(new TestSchema(Step2)).Open("A1");
    }

    [Theory]
    [InlineData("UPDATE journal SET summary = 'x'")]
    [InlineData("DELETE FROM journal")]
    [InlineData("DROP TRIGGER seal_journal_no_update")]
    [InlineData("INSERT INTO chain_head (chain, sequence, hash) VALUES ('x', 1, x'00')")]
    [InlineData("CREATE TRIGGER hide BEFORE INSERT ON journal BEGIN SELECT RAISE(IGNORE); END")]
    [InlineData("ATTACH DATABASE 'copy.db' AS copy")]
    [InlineData("COMMIT")]
    public void A_step_cannot_touch_what_belongs_to_the_cases_module(string sql)
    {
        using (_fixture.CreateStore().Create("A1"))
        {
        }

        var bad = new BadSchema(sql);

        Assert.Throws<InvalidOperationException>(() => _fixture.CreateStore(bad).Open("A1"));
        using var opened = _fixture.CreateStore().Open("A1");
        Assert.True(new CaseVerifier(_fixture.Clock).Verify(opened).IsIntact);
    }

    [Fact]
    public void Contributors_with_bad_names_or_versions_are_refused()
    {
        var repeated = new TestSchema(new CaseSchemaStep(1, "Igen", "SELECT 1"));
        Assert.Throws<ArgumentException>(() => _fixture.CreateStore(repeated));
        Assert.Throws<ArgumentException>(() => _fixture.CreateStore(new TestSchema(), new TestSchema()));
        Assert.Throws<ArgumentException>(() => _fixture.CreateStore(new BadSchema("SELECT 1", "Fel Namn")));
    }

    private static long[] Log(ICase opened) => opened.Read(reader =>
    {
        using var command = reader.CreateCommand("SELECT step FROM test_log ORDER BY id");
        using var rows = command.ExecuteReader();
        var steps = new List<long>();
        while (rows.Read())
        {
            steps.Add(rows.GetInt64(0));
        }

        return steps.ToArray();
    });

    private sealed class BadSchema(string sql, string module = "bad") : ICaseSchemaContributor
    {
        public string Module => module;

        public IReadOnlyList<CaseSchemaStep> Steps { get; } = [new(1, "Elak", sql)];

        public IReadOnlyList<SealedChain> SealedChains { get; } = [];
    }
}
