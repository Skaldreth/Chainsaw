using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>The panel's words for <c>mixer-parts</c> and <c>mixer-chain</c> (KF2b): chips, fee and look.</summary>
public sealed class MixerWordsTests
{
    private static CheckCandidate Part(int tx, long satoshi) =>
        new($"{Tx[tx].Value}:0", tx == 1 ? U : E, Tx[tx], 0, new Satoshi(satoshi), 0.9, "Del.");

    private static StoredCheck Parts(params CheckDecision[] decisions) => new(
        7,
        new HeuristicId("mixer-parts", 1),
        "Delarna tillsammans",
        new CheckFinding(
            "deposit:0", CheckKind.Suggestion, AreaSort.Obfuscation, "Delarna tillsammans", "2 delar",
            "mixer-parts v1 … aldrig ett faktum.", [V, U, E], [],
            [new CheckBasis(Tx[0], Block(100)), new CheckBasis(Tx[1], Block(101)), new CheckBasis(Tx[2], Block(102))],
            [Part(1, 60_000_000), Part(2, 38_000_000)]),
        5,
        new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.Zero),
        decisions);

    [Fact]
    public void The_parts_fee_is_the_deposit_against_all_the_parts_together()
    {
        var row = new CheckRowViewModel(Parts(), null, [], new Satoshi(100_000_000), TimeZoneInfo.Utc);

        Assert.Equal("−2 % trolig avgift, 2 delar", row.Flow.FeeText);
        Assert.Equal(["Delarna tillsammans", "Tid", "Spridda block", "Adresstyp"], row.Basis.Select(c => c.Label));
        Assert.Equal("Trolig del", row.Words.CandidateNoun);
    }

    [Fact]
    public void A_dismissed_part_leaves_the_parts_fee()
    {
        var dismissed = new CheckDecision(
            1, new HeuristicId("mixer-parts", 1), "deposit:0", $"{Tx[2].Value}:0", CheckVerdict.Dismissed, null,
            CheckSamples.Who, new DateTimeOffset(2026, 11, 8, 9, 5, 0, TimeSpan.Zero), 6);

        var row = new CheckRowViewModel(Parts(dismissed), null, [], new Satoshi(100_000_000), TimeZoneInfo.Utc);

        Assert.Equal("−40 % trolig avgift, 1 del", row.Flow.FeeText);
    }

    [Fact]
    public void Parts_that_are_more_than_came_in_say_so_instead_of_a_fee_below_none()
    {
        var row = new CheckRowViewModel(Parts(), null, [], new Satoshi(90_000_000), TimeZoneInfo.Utc);

        Assert.Equal(CheckRowViewModel.PartsTooMuchText, row.Flow.FeeText);
    }

    [Fact]
    public void Without_any_part_left_no_fee_is_shown()
    {
        CheckDecision Dismissed(int tx, long sequence) => new(
            sequence, new HeuristicId("mixer-parts", 1), "deposit:0", $"{Tx[tx].Value}:0", CheckVerdict.Dismissed,
            null, CheckSamples.Who, new DateTimeOffset(2026, 11, 8, 9, 5, 0, TimeSpan.Zero), sequence);

        var row = new CheckRowViewModel(
            Parts(Dismissed(1, 1), Dismissed(2, 2)), null, [], new Satoshi(100_000_000), TimeZoneInfo.Utc);

        Assert.Equal(string.Empty, row.Flow.FeeText);
    }

    [Fact]
    public void An_output_larger_than_came_in_has_no_fee_below_none()
    {
        var row = new CheckRowViewModel(CheckSamples.Mixer(), null, [], new Satoshi(500), TimeZoneInfo.Utc);

        Assert.Equal("−0 % trolig avgift", row.Flow.FeeText);
    }

    [Fact]
    public void A_single_output_keeps_its_own_fee()
    {
        var row = new CheckRowViewModel(CheckSamples.Mixer(), null, [], new Satoshi(1_000), TimeZoneInfo.Utc);

        Assert.False(row.Words.CandidatesAreParts);
        Assert.Equal("−0 % trolig avgift", row.Flow.FeeText);
    }

    [Fact]
    public void Mixer_after_mixer_has_its_chips_the_mixers_look_and_its_guide_section()
    {
        Assert.Equal(
            ["Mixer efter mixer", "Godkänd före trolig", "Avgifter längs kedjan"],
            SuggestionWords.Of("mixer-chain").Chips.Select(c => c.Label));
        var mixer = new AreaLook("Icon.Kind.Mixer", AreaTint.Red);
        Assert.Equal(mixer, AreaLooks.OfAnalysis("mixer-chain", AreaSort.Obfuscation));
        Assert.Equal(mixer, AreaLooks.OfAnalysis("mixer-parts", AreaSort.Obfuscation));
        Assert.Equal("mixer-efter-mixer", GuideTopic.MixerChain.FileName());
    }
}
