using System.Text.Json.Nodes;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases.Tests;

/// <summary>The findings of the second audit round, each shut and kept shut.</summary>
public sealed class AuditRound2Tests : IDisposable
{
    private readonly CaseFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void A_malformed_vault_envelope_does_not_keep_a_good_one_from_opening_the_case()
    {
        _fixture.CreateStore().Create("A1").Dispose();
        var malformed = new JsonObject { ["kind"] = "operator", ["nonce"] = "inte base64" };
        RewriteEnvelopes(_fixture.CaseFile("A1"), envelopes => [malformed, .. envelopes]);

        using var opened = _fixture.CreateStore().Open("A1");

        Assert.Equal("A1", opened.Info.CaseNumber);
    }

    [Fact]
    public void Only_malformed_vault_envelopes_make_the_file_corrupt()
    {
        _fixture.CreateStore().Create("A1").Dispose();
        var malformed = new JsonObject { ["kind"] = "operator", ["nonce"] = "inte base64" };
        RewriteEnvelopes(_fixture.CaseFile("A1"), _ => [malformed]);

        var error = Assert.Throws<CaseException>(() => _fixture.CreateStore().Open("A1"));

        Assert.Equal(CaseError.Corrupt, error.Error);
    }

    [Theory]
    [InlineData("seal_case_info_no_update", "case_info", "UPDATE")]
    [InlineData("seal_sealed_chain_no_delete", "sealed_chain", "DELETE")]
    public void A_protecting_trigger_made_to_never_abort_is_found_by_the_verification(
        string trigger, string table, string action)
    {
        _fixture.CreateStore().Create("A1").Dispose();
        CaseFileSurgery.Change(_fixture, "A1", connection =>
        {
            CaseFileSurgery.Execute(connection, $"DROP TRIGGER {trigger}");
            CaseFileSurgery.Execute(
                connection,
                $"CREATE TRIGGER {trigger} BEFORE {action} ON {table} WHEN 0 BEGIN SELECT RAISE(ABORT, 'x'); END");
        });

        using var opened = _fixture.CreateStore().Open("A1");

        Assert.Contains(CaseProblem.CaseInfoChanged, new CaseVerifier(_fixture.Clock).Verify(opened).Problems);
    }

    [Fact]
    public void An_untouched_case_still_has_its_protecting_triggers()
    {
        using var opened = _fixture.CreateStore().Create("A1");

        Assert.True(new CaseVerifier(_fixture.Clock).Verify(opened).IsIntact);
    }

    [Fact]
    public void A_chain_registered_on_a_table_of_the_cases_module_neither_guards_it_nor_stops_sealing()
    {
        _fixture.CreateStore().Create("A1").Dispose();
        CaseFileSurgery.Change(_fixture, "A1", connection => CaseFileSurgery.Execute(
            connection,
            "INSERT INTO sealed_chain (name, table_name, columns) VALUES ('evil.heads', 'chain_head', '[\"chain\"]')"));

        using var opened = _fixture.CreateStore().Open("A1");
        opened.AppendJournal(new JournalDraft("test.note", "Efter registreringen"));

        var result = new CaseVerifier(_fixture.Clock).Verify(opened);
        Assert.Contains(result.Chains, chain => chain.Chain == "evil.heads" && chain.Break is not null);
    }

    [Fact]
    public void A_full_header_refuses_another_envelope_before_journalling_and_still_closes()
    {
        using (var created = _fixture.CreateStore().Create("A1"))
        {
            created.SetSharingPassword(CaseFixture.Password);
        }

        var path = _fixture.CaseFile("A1");
        RewriteEnvelopes(path, envelopes =>
        [
            .. envelopes.Where(item => item["kind"]?.GetValue<string>() == "password"),
            .. Enumerable.Range(0, CaseContainer.MaximumEnvelopes - 1)
                .Select(index => new JsonObject { ["kind"] = "framtida", ["n"] = index }),
        ]);

        var opened = _fixture.CreateStore().OpenWithPassword(path, CaseFixture.Password);
        var before = opened.ReadJournal().Count;

        Assert.Equal(CaseError.TooManyEnvelopes, Assert.Throws<CaseException>(opened.AddOperatorAccess).Error);
        Assert.Equal(before, opened.ReadJournal().Count);
        opened.SetSharingPassword("ett annat långt delningslösenord");
        opened.Close();
        using var again = _fixture.CreateStore().OpenWithPassword(path, "ett annat långt delningslösenord");
        Assert.Equal(CaseContainer.MaximumEnvelopes, again.DescribeEnvelopes().Count);
    }

    /// <summary>Replaces the envelopes in a closed case file's header.</summary>
    private static void RewriteEnvelopes(
        string path, Func<IReadOnlyList<JsonObject>, IEnumerable<JsonObject>> change)
    {
        var bytes = File.ReadAllBytes(path);
        using var input = new MemoryStream(bytes);
        var header = CaseContainer.ReadHeader(input);
        var rest = bytes.AsSpan((int)input.Position).ToArray();
        using var output = new MemoryStream();
        CaseContainer.WriteHeader(output, header.WithEnvelopes(change(header.Envelopes)));
        output.Write(rest);
        File.WriteAllBytes(path, output.ToArray());
    }
}
