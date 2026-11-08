using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases.Tests;

/// <summary>The findings of the fourth audit round (cryptography and case-file integrity), each shut and kept shut.</summary>
public sealed class AuditRound4Tests : IDisposable
{
    private readonly CaseFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void A_trigger_planted_on_a_table_that_is_not_sealed_is_found_by_the_verification()
    {
        var store = _fixture.CreateStore(new TestSchema());
        store.Create("A1").Dispose();

        // Someone with the file key makes every later note vanish; no sealed table is touched.
        CaseFileSurgery.Change(_fixture, "A1", connection => CaseFileSurgery.Execute(
            connection, "CREATE TRIGGER hide BEFORE INSERT ON test_notes BEGIN SELECT RAISE(IGNORE); END"));

        using var opened = _fixture.CreateStore(new TestSchema()).Open("A1");
        var stored = opened.Write(transaction =>
        {
            using var command = transaction.CreateCommand("INSERT INTO test_notes (note) VALUES ('en anteckning')");
            return command.ExecuteNonQuery();
        });

        Assert.Equal(0, stored);
        Assert.Contains(CaseProblem.TriggerAdded, new CaseVerifier(_fixture.Clock).Verify(opened).Problems);
    }

    [Fact]
    public void The_triggers_chainsaw_makes_are_not_reported()
    {
        using var opened = _fixture.CreateStore(new TestSchema()).Create("A1");

        Assert.True(new CaseVerifier(_fixture.Clock).Verify(opened).IsIntact);
    }

    [Fact]
    public void A_module_step_cannot_make_a_trigger()
    {
        var step = new CaseSchemaStep(
            2, "En utlösare", "CREATE TRIGGER mine AFTER INSERT ON test_notes BEGIN SELECT 1; END;");

        Assert.ThrowsAny<Exception>(() => _fixture.CreateStore(new TestSchema(step)).Create("A1").Dispose());
    }
}
