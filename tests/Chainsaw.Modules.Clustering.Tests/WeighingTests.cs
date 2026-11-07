using Chainsaw.Modules.Clustering.Contracts;
using static Chainsaw.Modules.Clustering.Tests.Synthetic;

namespace Chainsaw.Modules.Clustering.Tests;

/// <summary>How the analyses of one transaction are weighed against each other.</summary>
public sealed class WeighingTests
{
    private static readonly HeuristicId First = new("first", 1);
    private static readonly HeuristicId Second = new("second", 1);
    private static readonly HeuristicId Judge = new("judge", 1);

    [Fact]
    public void Different_change_outputs_turn_both_merges_into_suggestions()
    {
        var weighed = ResultWeighing.Weigh([Result(First, change: 0), Result(Second, change: 1)]);

        Assert.All(weighed.SelectMany(result => result.Proposals), proposal =>
        {
            Assert.Equal(EvidenceStrength.Suggestion, proposal.Strength);
            Assert.Equal(ResultWeighing.ChangeDisagreement, proposal.Adjustment);
        });
    }

    [Fact]
    public void The_same_change_output_is_no_disagreement()
    {
        var weighed = ResultWeighing.Weigh([Result(First, change: 1), Result(Second, change: 1)]);

        Assert.All(weighed.SelectMany(result => result.Proposals), proposal =>
            Assert.Equal(EvidenceStrength.Merge, proposal.Strength));
    }

    [Theory]
    [InlineData(VetoKind.Stop, EvidenceStrength.Merge, EvidenceStrength.Stopped)]
    [InlineData(VetoKind.Stop, EvidenceStrength.Suggestion, EvidenceStrength.Stopped)]
    [InlineData(VetoKind.Suggest, EvidenceStrength.Merge, EvidenceStrength.Suggestion)]
    [InlineData(VetoKind.Suggest, EvidenceStrength.Suggestion, EvidenceStrength.Suggestion)]
    public void A_veto_lowers_the_other_analyses(VetoKind veto, EvidenceStrength given, EvidenceStrength expected)
    {
        var judge = HeuristicResult.Nothing(Judge, Txid("t")) with { Veto = new OwnershipVeto(veto, "Därför.") };

        var weighed = ResultWeighing.Weigh([Result(First, strength: given), judge]);

        var proposal = Assert.Single(weighed[0].Proposals);
        Assert.Equal(expected, proposal.Strength);
        Assert.Equal(given == expected ? null : "Därför.", proposal.Adjustment);
    }

    [Fact]
    public void An_analysis_never_vetoes_its_own_proposals()
    {
        var own = Result(Judge) with { Veto = new OwnershipVeto(VetoKind.Stop, "Själv.") };

        var weighed = ResultWeighing.Weigh([own]);

        Assert.Equal(EvidenceStrength.Merge, Assert.Single(weighed[0].Proposals).Strength);
    }

    [Fact]
    public void The_strongest_veto_wins()
    {
        var suggest = HeuristicResult.Nothing(Judge, Txid("t")) with { Veto = new(VetoKind.Suggest, "Svag.") };
        var stop = HeuristicResult.Nothing(Second, Txid("t")) with { Veto = new(VetoKind.Stop, "Stark.") };

        var weighed = ResultWeighing.Weigh([Result(First), suggest, stop]);

        var proposal = Assert.Single(weighed[0].Proposals);
        Assert.Equal((EvidenceStrength.Stopped, "Stark."), (proposal.Strength, proposal.Adjustment));
    }

    [Fact]
    public void The_fingerprint_changes_with_the_strength()
    {
        var plain = ResultWeighing.Weigh([Result(First)])[0];
        var lowered = ResultWeighing.Weigh(
            [Result(First), HeuristicResult.Nothing(Judge, Txid("t")) with { Veto = new(VetoKind.Suggest, "x") }])[0];

        var again = ResultWeighing.Weigh([Result(First)])[0];
        Assert.Equal(EvidenceStore.Fingerprint(plain), EvidenceStore.Fingerprint(again));
        Assert.NotEqual(EvidenceStore.Fingerprint(plain), EvidenceStore.Fingerprint(lowered));
    }

    private static HeuristicResult Result(
        HeuristicId heuristic, int? change = null, EvidenceStrength strength = EvidenceStrength.Merge) =>
        new(heuristic, Txid("t"), [new OwnershipProposal([Wpkh("a"), Wpkh("b")], strength, "Skäl.", change)], []);
}
