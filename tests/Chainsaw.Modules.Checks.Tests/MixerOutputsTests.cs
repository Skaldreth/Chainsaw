using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>The facit for <c>mixer-outputs v1</c>: what is and is not a likely output of a mixer.</summary>
public sealed class MixerOutputsTests
{
    private static readonly MixerOutputsCheck Analysis = new();

    private static IReadOnlyList<CheckFinding> Run(IReadOnlyList<BitcoinTransaction> transactions) =>
        Analysis.Run(new CheckInput(
            transactions,
            [new KnownMixer([Mixing.Mixer, Mixing.MixerOther])],
            new Dictionary<Txid, TransactionConclusions>()));

    private static CheckFinding DepositOf(IReadOnlyList<CheckFinding> found) =>
        Assert.Single(found, finding => finding.Key == $"{Txid("deposit").Value}:0");

    [Fact]
    public void A_payment_into_another_of_the_mixers_addresses_is_a_deposit_of_its_own()
    {
        var found = Run(Mixing.All());

        Assert.Equal(2, found.Count);
        Assert.Contains(found, finding => finding.Key == $"{Txid("to-mixer").Value}:0");
    }

    [Fact]
    public void The_candidates_are_the_likely_outputs_best_first()
    {
        var finding = DepositOf(Run(Mixing.All()));

        Assert.Equal(CheckKind.Suggestion, finding.Kind);
        Assert.Equal(AreaSort.Obfuscation, finding.Area);
        Assert.Equal($"{Txid("deposit").Value}:0", finding.Key);
        Assert.Equal(
            [Wpkh("out-best"), Wpkh("out-second"), Pkh("out-third")],
            finding.Candidates.Select(candidate => candidate.Address));
        Assert.True(finding.Candidates[0].Score > finding.Candidates[1].Score);
        Assert.True(finding.Candidates[1].Score > finding.Candidates[2].Score);
        Assert.Equal(1.0, finding.Candidates[0].Score);
    }

    [Fact]
    public void Too_much_too_little_too_early_too_late_the_victims_own_and_the_mixers_own_are_never_candidates()
    {
        var finding = DepositOf(Run(Mixing.All()));
        var never = new[]
        {
            "out-too-much", "out-too-little", "out-before", "out-too-late", "victim-shop", "mixer-other",
            "victim-change",
        }.Select(Wpkh);

        Assert.DoesNotContain(finding.Candidates, candidate => never.Contains(candidate.Address));
    }

    [Fact]
    public void A_candidate_says_why_in_plain_Swedish_and_rests_on_its_transaction_and_block()
    {
        var finding = DepositOf(Run(Mixing.All()));
        var best = finding.Candidates[0];

        Assert.Equal("0,14625 ut mot 0,1500 in, avgift 2,5 %; 40 min senare; samma adresstyp (P2WPKH)", best.Reason);
        Assert.Equal($"{Txid("best").Value}:0", best.Key);
        Assert.Contains(finding.Basis, basis => basis.Txid == Txid("deposit") && basis.Block!.Height == 900_000);
        Assert.Contains(finding.Basis, basis => basis.Txid == Txid("best") && basis.Block!.Height == 900_004);
        Assert.Contains("aldrig ett faktum", finding.Why, StringComparison.Ordinal);
        Assert.Equal("annan adresstyp (P2PKH mot P2WPKH)", finding.Candidates[2].Reason.Split("; ")[^1]);
    }

    [Fact]
    public void The_area_holds_the_mixers_address_and_the_candidates()
    {
        var finding = DepositOf(Run(Mixing.All()));

        Assert.Equal(
            [Mixing.Mixer, Wpkh("out-best"), Wpkh("out-second"), Pkh("out-third")], finding.Addresses);
    }

    [Fact]
    public void Without_a_known_mixer_nothing_is_suggested()
    {
        var found = Analysis.Run(new CheckInput(Mixing.All(), [], new Dictionary<Txid, TransactionConclusions>()));

        Assert.Empty(found);
    }

    [Fact]
    public void A_deposit_without_any_candidate_gives_no_suggestion()
    {
        Assert.Empty(Run([Mixing.Deposit(), Mixing.TooMuch(), Mixing.TooLate()]));
    }

    [Fact]
    public void Money_moved_between_the_mixers_own_addresses_is_no_deposit()
    {
        var inside = Tx("inside", 900_000).In(Mixing.MixerOther, 15_000_000).Out(Mixing.Mixer, 14_990_000).Build();

        Assert.Empty(Run([inside, Mixing.Best()]));
    }

    [Fact]
    public void An_unconfirmed_payout_is_never_a_candidate()
    {
        var unconfirmed = Tx("unconfirmed", null).In(Wpkh("u-payer"), 15_000_000).Out(Wpkh("u-out"), 14_600_000).Build();

        Assert.Empty(Run([Mixing.Deposit(), unconfirmed]));
    }

    [Fact]
    public void At_most_five_candidates_are_kept()
    {
        var payouts = Enumerable.Range(1, 8)
            .Select(i => Mixing.Payout($"many-{i}", Wpkh($"many-out-{i}"), 14_600_000 - (i * 10_000), 2 + i));

        var finding = Assert.Single(Run([Mixing.Deposit(), .. payouts]));

        Assert.Equal(MixerOutputsCheck.MostCandidates, finding.Candidates.Count);
    }

    [Theory]
    [InlineData(-0.01, 0)]
    [InlineData(0, 0.5)]
    [InlineData(0.0025, 0.75)]
    [InlineData(0.005, 1)]
    [InlineData(0.025, 1)]
    [InlineData(0.05, 1)]
    [InlineData(0.075, 0.5)]
    [InlineData(0.10, 0)]
    [InlineData(0.11, 0)]
    public void The_fee_counts_in_full_between_half_a_per_cent_and_five(double fee, double score)
    {
        Assert.Equal(score, MixerOutputsCheck.FeeScore(fee), 6);
    }

    [Fact]
    public void The_time_counts_less_the_later_and_nothing_outside_the_window()
    {
        Assert.Equal(0, MixerOutputsCheck.TimeScore(TimeSpan.Zero));
        Assert.Equal(1, MixerOutputsCheck.TimeScore(TimeSpan.FromTicks(1)), 6);
        Assert.Equal(0.6, MixerOutputsCheck.TimeScore(TimeSpan.FromDays(3.5)), 6);
        Assert.Equal(0.2, MixerOutputsCheck.TimeScore(MixerOutputsCheck.Window), 6);
        Assert.Equal(0, MixerOutputsCheck.TimeScore(TimeSpan.FromDays(8)));
    }

    [Fact]
    public void The_same_input_gives_the_same_findings()
    {
        Assert.Equal(
            Run(Mixing.All()).Select(FindingCodec.Encode),
            Run([.. Mixing.All().Reverse()]).Select(FindingCodec.Encode));
    }
}
