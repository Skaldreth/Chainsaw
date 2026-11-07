using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases.Tests;

public sealed class TitleTests : IDisposable
{
    private readonly CaseFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void A_new_case_has_no_title()
    {
        using var created = _fixture.CreateStore().Create("A1");

        Assert.Equal(string.Empty, created.Title);
    }

    [Fact]
    public void A_title_is_sealed_with_a_line_in_the_journal_and_kept_in_the_file()
    {
        using (var created = _fixture.CreateStore().Create("A1"))
        {
            Assert.True(created.SetTitle("  Investeringsbedrägeri \t via   chatt "));
            Assert.Equal("Investeringsbedrägeri via chatt", created.Title);
            var last = created.ReadJournal()[^1];
            Assert.Equal(CaseJournalKinds.TitleSet, last.Kind);
            Assert.Equal("Titeln sattes till Investeringsbedrägeri via chatt", last.Summary);
            using var details = System.Text.Json.JsonDocument.Parse(last.Details!);
            Assert.Equal("Investeringsbedrägeri via chatt", details.RootElement.GetProperty("title").GetString());
        }

        using var opened = _fixture.CreateStore().Open("A1");
        Assert.Equal("Investeringsbedrägeri via chatt", opened.Title);
    }

    [Fact]
    public void A_changed_title_keeps_the_earlier_one_in_its_chain()
    {
        using var created = _fixture.CreateStore().Create("A1");
        created.SetTitle("Första");

        Assert.True(created.SetTitle("Andra"));

        Assert.Equal("Andra", created.Title);
        Assert.Equal("Titeln ändrades till Andra", created.ReadJournal()[^1].Summary);
        var titles = created.Read(reader =>
        {
            using var select = reader.CreateCommand("SELECT title FROM case_title ORDER BY sequence");
            using var rows = select.ExecuteReader();
            var all = new List<string>();
            while (rows.Read())
            {
                all.Add(rows.GetString(0));
            }

            return all;
        });
        Assert.Equal(["Första", "Andra"], titles);
        var title = new CaseVerifier(_fixture.Clock).Verify(created).Chains.Single(c => c.Chain == "cases.title");
        Assert.Null(title.Break);
        Assert.Equal(2, title.RecordCount);
    }

    [Fact]
    public void The_same_title_again_writes_nothing()
    {
        using var created = _fixture.CreateStore().Create("A1");
        created.SetTitle("Titel");
        var count = created.ReadJournal().Count;

        Assert.False(created.SetTitle(" Titel "));

        Assert.Equal(count, created.ReadJournal().Count);
    }

    [Fact]
    public void A_title_that_is_too_long_or_holds_control_characters_is_refused()
    {
        using var created = _fixture.CreateStore().Create("A1");

        Assert.Throws<ArgumentException>(() => created.SetTitle(new string('x', CaseTitleRules.MaximumLength + 1)));
        Assert.Throws<ArgumentException>(() => created.SetTitle("a\u0007b"));
        Assert.Equal(string.Empty, created.Title);
    }

    [Fact]
    public void Line_breaks_in_a_title_become_spaces()
    {
        Assert.Equal("a b", CaseTitleRules.Normalize("a\r\nb"));
        Assert.True(CaseTitleRules.IsValid("a\r\nb"));
        Assert.True(CaseTitleRules.IsValid(new string('x', CaseTitleRules.MaximumLength)));
    }

    [Fact]
    public void A_title_cannot_be_rewritten_by_a_module()
    {
        using var created = _fixture.CreateStore().Create("A1");
        created.SetTitle("Titel");

        Assert.ThrowsAny<Exception>(() => created.Write(writer =>
        {
            using var update = writer.CreateCommand("UPDATE case_title SET title = 'Annan'");
            return update.ExecuteNonQuery();
        }));

        Assert.Equal("Titel", created.Title);
    }

    [Fact]
    public void A_closed_case_refuses_a_title()
    {
        var created = _fixture.CreateStore().Create("A1");
        created.Dispose();

        var error = Assert.Throws<CaseException>(() => created.SetTitle("Titel"));

        Assert.Equal(CaseError.Closed, error.Error);
    }
}
