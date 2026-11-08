using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>The facit for <c>mixer-parts v1</c>: which combinations of payouts are the deposit in parts.</summary>
public sealed class MixerPartsTests
{
    private static readonly MixerPartsCheck Analysis = new();

    private static readonly string DepositKey = $"{Txid("parts-deposit").Value}:0";

    private static IReadOnlyList<CheckFinding> Run(
        IReadOnlyList<BitcoinTransaction> transactions,
        IReadOnlyList<ApprovedCandidate>? approved = null,
        IReadOnlyList<DismissedDecision>? dismissed = null) =>
        Analysis.Run(new CheckInput(
            transactions,
            [new KnownMixer([Parting.Mixer])],
            new Dictionary<Txid, TransactionConclusions>(),
            approved,
            dismissed));

    private static string Coin(string label) => $"{Txid(label).Value}:0";

    private static ApprovedCandidate Approve(string label) =>
        new(MixerPartsCheck.Identity, DepositKey, Txid(label), 0);

    private static DismissedDecision Dismiss(string label) => new(MixerPartsCheck.Identity, DepositKey, Coin(label));

    private static CheckFinding Only(IReadOnlyList<BitcoinTransaction> transactions) =>
        Assert.Single(Run(transactions));

    [Fact]
    public void Three_payouts_that_together_are_the_deposit_minus_a_fee_are_one_suggestion_with_a_candidate_each()
    {
        var finding = Only([Parting.Deposit(), .. Parting.Parts()]);

        Assert.Equal(CheckKind.Suggestion, finding.Kind);
        Assert.Equal(AreaSort.Obfuscation, finding.Area);
        Assert.Equal($"{Txid("parts-deposit").Value}:0", finding.Key);
        Assert.Equal("Delarna tillsammans", finding.Title);
        Assert.Equal("3 delar · 0,1470 av 0,1500 in · avgift 2 %", finding.Summary);
        Assert.Equal([Wpkh("part-a"), Wpkh("part-b"), Wpkh("part-c")], finding.Candidates.Select(c => c.Address));
        Assert.All(finding.Candidates, candidate => Assert.Equal(0.98, candidate.Score));
        Assert.Equal([Parting.Mixer, Wpkh("part-a"), Wpkh("part-b"), Wpkh("part-c")], finding.Addresses);
        Assert.Equal(
            [Txid("parts-deposit"), Txid("part-a"), Txid("part-b"), Txid("part-c")],
            finding.Basis.Select(basis => basis.Txid));
    }

    [Fact]
    public void A_part_says_which_part_it_is_and_the_text_says_it_is_a_judgement_with_its_limits()
    {
        var finding = Only([Parting.Deposit(), .. Parting.Parts()]);

        Assert.Equal(
            "Del 1 av 3: 0,0600 av 0,1500 in; 2 h senare; samma adresstyp (P2WPKH)", finding.Candidates[0].Reason);
        Assert.Equal($"{Txid("part-a").Value}:0", finding.Candidates[0].Key);
        Assert.Contains("mixer-parts v1", finding.Why, StringComparison.Ordinal);
        Assert.Contains("den bästa av 1 kombinationer", finding.Why, StringComparison.Ordinal);
        Assert.Contains("3 olika block", finding.Why, StringComparison.Ordinal);
        Assert.Contains("20 utbetalningarna", finding.Why, StringComparison.Ordinal);
        Assert.Contains("aldrig ett faktum", finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void Of_several_fitting_combinations_the_best_is_given_and_the_text_counts_them()
    {
        var later = Parting.Payout("part-d", Wpkh("part-d"), 3_800_000, 600);

        var finding = Only([Parting.Deposit(), .. Parting.Parts(), later]);

        Assert.Equal([Wpkh("part-a"), Wpkh("part-b"), Wpkh("part-c")], finding.Candidates.Select(c => c.Address));
        Assert.Contains("den bästa av 2 kombinationer", finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void Parts_that_together_are_too_little_or_too_much_are_no_suggestion()
    {
        var tooLittle = Parting.Payout("little", Wpkh("little"), 2_000_000, 400);
        var tooMuch = Parting.Payout("much", Wpkh("much"), 4_500_000, 400);

        Assert.Empty(Run([Parting.Deposit(), Parting.PartA(), Parting.PartB(), tooLittle]));
        Assert.Empty(Run([Parting.Deposit(), Parting.PartA(), Parting.PartB(), tooMuch]));
    }

    [Fact]
    public void A_part_smaller_than_a_tenth_of_the_deposit_is_never_a_part()
    {
        var large = Parting.Payout("large", Wpkh("large"), 13_000_000, 10);
        var small = Parting.Payout("small", Wpkh("small"), 1_400_000, 20);

        Assert.Equal(1_500_000, MixerPartsCheck.SmallestPart(new SharedKernel.Satoshi(15_000_000)));
        Assert.Empty(Run([Parting.Deposit(), large, small]));
    }

    [Fact]
    public void Five_parts_are_more_than_the_search_combines()
    {
        var five = Enumerable.Range(1, 5)
            .Select(i => Parting.Payout($"fifth-{i}", Wpkh($"fifth-{i}"), 2_940_000, i * 20));

        Assert.Empty(Run([Parting.Deposit(), .. five]));
    }

    [Fact]
    public void Four_parts_are_found()
    {
        var four = Enumerable.Range(1, 4)
            .Select(i => Parting.Payout($"fourth-{i}", Wpkh($"fourth-{i}"), 3_675_000, i * 20));

        Assert.Equal(4, Only([Parting.Deposit(), .. four]).Candidates.Count);
    }

    [Fact]
    public void Only_the_twenty_payouts_soonest_after_the_deposit_are_tried()
    {
        Assert.Equal(3, Only([Parting.Deposit(), .. Parting.Crowd(17), .. Parting.Parts()]).Candidates.Count);
        Assert.Empty(Run([Parting.Deposit(), .. Parting.Crowd(18), .. Parting.Parts()]));
    }

    [Fact]
    public void A_payment_and_its_change_in_one_transaction_are_never_parts()
    {
        var oneTx = Tx("one-tx", Parting.Height + 12)
            .In(Wpkh("one-tx-payer"), 15_000_000)
            .Out(Wpkh("one-a"), 7_400_000)
            .Out(Wpkh("one-b"), 7_300_000)
            .Build();

        Assert.Empty(Run([Parting.Deposit(), oneTx]));
    }

    [Fact]
    public void A_payout_whose_transaction_spends_another_payout_is_never_combined_with_it()
    {
        var first = Parting.PartA();
        var onward = Tx("spends-a", Parting.Height + 72).Spend(first, 0).Out(Wpkh("from-a"), 5_000_000)
            .Out(Wpkh("from-a-change"), 990_000).Build();

        Assert.Empty(Run([Parting.Deposit(), first, onward, Parting.PartC()]));
        Assert.True(MixerPartsCheck.Related(
            new MixerPayout(onward, onward.Outputs[0], TimeSpan.FromHours(12)),
            new MixerPayout(first, first.Outputs[0], TimeSpan.FromHours(2))));
    }

    [Fact]
    public void Parts_in_the_same_block_score_lower_than_parts_spread_over_blocks()
    {
        var sameBlock = new[]
        {
            Parting.Payout("same-a", Wpkh("same-a"), 7_400_000, 12),
            Parting.Payout("same-b", Wpkh("same-b"), 7_300_000, 12),
        };
        var spread = new[]
        {
            Parting.Payout("spread-a", Wpkh("spread-a"), 7_400_000, 12),
            Parting.Payout("spread-b", Wpkh("spread-b"), 7_300_000, 24),
        };

        var together = Only([Parting.Deposit(), .. sameBlock]).Candidates[0].Score;
        var apart = Only([Parting.Deposit(), .. spread]).Candidates[0].Score;

        Assert.True(together < apart, $"{together} should be lower than {apart}");
    }

    [Fact]
    public void A_dismissed_part_is_never_combined_and_stays_as_a_candidate_apart()
    {
        var later = Parting.Payout("part-d", Wpkh("part-d"), 3_800_000, 600);

        var finding = Assert.Single(Run([Parting.Deposit(), .. Parting.Parts(), later], dismissed: [Dismiss("part-c")]));

        Assert.Equal(
            [Wpkh("part-a"), Wpkh("part-b"), Wpkh("part-d"), Wpkh("part-c")],
            finding.Candidates.Select(c => c.Address));
        Assert.Equal(0, finding.Candidates[3].Score);
        Assert.StartsWith("Avfärdad", finding.Candidates[3].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_combination_that_holds_the_approved_parts_is_weighed_first()
    {
        var later = Parting.Payout("part-d", Wpkh("part-d"), 3_800_000, 600);

        var finding = Assert.Single(Run([Parting.Deposit(), .. Parting.Parts(), later], [Approve("part-d")]));

        Assert.Equal([Wpkh("part-a"), Wpkh("part-b"), Wpkh("part-d")], finding.Candidates.Select(c => c.Address));
        Assert.DoesNotContain("hör inte till", finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void An_approved_part_no_combination_can_hold_stands_apart_and_the_text_says_so()
    {
        var large = Parting.Payout("part-large", Wpkh("part-large"), 12_000_000, 30);

        var finding = Assert.Single(Run([Parting.Deposit(), .. Parting.Parts(), large], [Approve("part-large")]));

        Assert.Equal(
            [Wpkh("part-a"), Wpkh("part-b"), Wpkh("part-c"), Wpkh("part-large")],
            finding.Candidates.Select(c => c.Address));
        Assert.Equal(0, finding.Candidates[3].Score);
        Assert.Contains("hör inte till den här kombinationen", finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_depositors_own_money_and_payouts_before_or_long_after_are_never_parts()
    {
        var deposit = Parting.Deposit();
        var own = Tx("own", Parting.Height + 30).Spend(deposit, 1).Out(Wpkh("own-shop"), 980_000).Build();
        var before = Parting.Payout("parts-before", Wpkh("parts-before"), 3_700_000, -5);
        var late = Parting.Payout("parts-late", Wpkh("parts-late"), 3_700_000, 1_100);

        Assert.Empty(Run([deposit, Parting.PartA(), Parting.PartB(), own, before, late]));
    }

    [Fact]
    public void Without_a_known_mixer_nothing_is_suggested()
    {
        var found = Analysis.Run(new CheckInput(
            [Parting.Deposit(), .. Parting.Parts()], [], new Dictionary<Txid, TransactionConclusions>()));

        Assert.Empty(found);
    }

    [Fact]
    public void The_same_input_gives_the_same_findings()
    {
        IReadOnlyList<BitcoinTransaction> all =
            [Parting.Deposit(), .. Parting.Parts(), Parting.Payout("part-d", Wpkh("part-d"), 3_800_000, 600)];

        Assert.Equal(
            Run(all).Select(FindingCodec.Encode), Run([.. all.Reverse()]).Select(FindingCodec.Encode));
    }
}
