using Chainsaw.Modules.Checks.Contracts;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>The user's own areas: drawn, renamed and removed, each sealed with who and when.</summary>
public sealed class OwnAreaTests
{
    private static OwnAreaDraft Draft(string name = "Säljaren på P2P", string? note = "Samma belopp och tid.") => new(
        name,
        AreaSort.Own,
        AreaTint.Green,
        true,
        note,
        [CheckRules.MemberOf(Wpkh("a")), CheckRules.MemberOf(Wpkh("b")), CheckRules.MemberOf(Txid("t"))]);

    [Fact]
    public void An_area_is_sealed_with_who_when_and_its_note_and_read_back_after_reopening()
    {
        using var harness = new Harness();

        var area = harness.Service.AddArea(harness.Case, Draft());
        harness.Reopen();
        var read = Assert.Single(harness.Service.GetAreas(harness.Case));

        Assert.Equal(area.Id, read.Id);
        Assert.Equal("Säljaren på P2P", read.Name);
        Assert.Equal("Samma belopp och tid.", read.Note);
        Assert.True(read.SameActor);
        Assert.Equal(AreaTint.Green, read.Tint);
        Assert.Equal(3, read.Members.Count);
        Assert.Equal("Testa Testsson", read.CreatedBy.Name);
        Assert.Single(harness.Journal(CheckJournalKinds.AreaAdded));
        Assert.True(harness.Verify().IsIntact);
    }

    [Fact]
    public void A_rename_is_a_new_sealed_record_and_the_old_name_stays_in_the_case()
    {
        using var harness = new Harness();
        var area = harness.Service.AddArea(harness.Case, Draft());

        var renamed = harness.Service.RenameArea(harness.Case, area.Id, "Kontot på börsen");

        Assert.Equal("Kontot på börsen", renamed!.Name);
        Assert.Equal("Kontot på börsen", Assert.Single(harness.Service.GetAreas(harness.Case)).Name);
        Assert.Equal(2, harness.Count("checks_areas"));
        Assert.Single(harness.Journal(CheckJournalKinds.AreaRenamed));
        Assert.Null(harness.Service.RenameArea(harness.Case, "none", "x"));
    }

    [Fact]
    public void A_removal_takes_the_area_out_of_force_with_an_optional_reason_and_cannot_be_made_twice()
    {
        using var harness = new Harness();
        var area = harness.Service.AddArea(harness.Case, Draft(note: null));

        Assert.True(harness.Service.RemoveArea(harness.Case, area.Id, null));
        Assert.False(harness.Service.RemoveArea(harness.Case, area.Id, "Igen."));
        Assert.Empty(harness.Service.GetAreas(harness.Case));
        Assert.Equal(2, harness.Count("checks_areas"));
        Assert.True(harness.Verify().IsIntact);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_area_needs_a_name(string name)
    {
        using var harness = new Harness();

        Assert.Throws<ArgumentException>(() => harness.Service.AddArea(harness.Case, Draft(name)));
    }

    [Fact]
    public void An_area_needs_actors_that_are_addresses_or_txids()
    {
        using var harness = new Harness();

        Assert.Throws<ArgumentException>(() => harness.Service.AddArea(harness.Case, Draft() with { Members = [] }));
        Assert.Throws<ArgumentException>(() =>
            harness.Service.AddArea(harness.Case, Draft() with { Members = ["address:nonsense"] }));
        Assert.Throws<ArgumentException>(() =>
            harness.Service.AddArea(harness.Case, Draft() with { Members = ["cluster:x"] }));
        Assert.Equal(0, harness.Count("checks_areas"));
    }
}
