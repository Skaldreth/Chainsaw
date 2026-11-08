using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>The facit for <c>mixer-chain v1</c>: an output of a mixer that goes into a mixer again.</summary>
public sealed class MixerChainTests
{
    private static readonly MixerChainCheck Analysis = new();

    private static readonly IReadOnlyList<KnownMixer> TwoMixers =
        [new KnownMixer([Parting.Mixer, Parting.MixerAgain]), new KnownMixer([Parting.OtherMixer])];

    /// <summary>The likely output of the first mixer: 0.14625 BTC forty minutes later.</summary>
    private static BitcoinTransaction FirstOut() => Parting.Payout("hop-1", Wpkh("hop-1"), 14_625_000, 4);

    /// <summary>The first output paid on into a mixer, with a network fee of 25 000 sat.</summary>
    private static BitcoinTransaction SecondDeposit(BitcoinTransaction from, BitcoinAddress into) =>
        Tx("second-deposit", Parting.Height + 10).Spend(from, 0).Out(into, 14_600_000).Build();

    /// <summary>The likely output of the second mixer.</summary>
    private static BitcoinTransaction SecondOut() => Parting.Payout("hop-2", Wpkh("hop-2"), 14_300_000, 20);

    private static IReadOnlyList<BitcoinTransaction> TwoSteps(BitcoinAddress second)
    {
        var first = FirstOut();
        return [Parting.Deposit(), first, SecondDeposit(first, second), SecondOut()];
    }

    private static readonly string DepositKey = $"{Txid("parts-deposit").Value}:0";

    private static IReadOnlyList<CheckFinding> Run(
        IReadOnlyList<BitcoinTransaction> transactions,
        IReadOnlyList<ApprovedCandidate>? approved = null,
        IReadOnlyList<DismissedDecision>? dismissed = null,
        IReadOnlyList<KnownMixer>? mixers = null) =>
        Analysis.Run(new CheckInput(
            transactions, mixers ?? TwoMixers, new Dictionary<Txid, TransactionConclusions>(), approved, dismissed));

    [Fact]
    public void A_likely_output_that_goes_into_another_mixer_is_one_check_over_both_steps()
    {
        var finding = Assert.Single(Run(TwoSteps(Parting.OtherMixer)));

        Assert.Equal(CheckKind.Check, finding.Kind);
        Assert.Equal(AreaSort.Obfuscation, finding.Area);
        Assert.Equal($"{Txid("parts-deposit").Value}:0", finding.Key);
        Assert.Equal("Mixer efter mixer, 2 steg", finding.Title);
        Assert.Equal("0,1500 in · 0,1430 ut · avgifter 0,00675", finding.Summary);
        Assert.Empty(finding.Candidates);
        Assert.Equal(
            [Txid("parts-deposit"), Txid("hop-1"), Txid("second-deposit"), Txid("hop-2")],
            finding.Basis.Select(basis => basis.Txid));
        Assert.Equal(
            [Parting.Mixer, Wpkh("hop-1"), Parting.OtherMixer, Wpkh("hop-2")], finding.Addresses);
    }

    [Fact]
    public void The_text_sums_the_amount_and_the_fees_along_the_chain_and_says_it_is_a_judgement()
    {
        var finding = Assert.Single(Run(TwoSteps(Parting.OtherMixer)));

        Assert.Contains("mixer-chain v1", finding.Why, StringComparison.Ordinal);
        Assert.Contains("Steg 1: 0,1500 BTC in i mixern (block 900000); trolig utgång 0,14625 BTC", finding.Why,
            StringComparison.Ordinal);
        Assert.Contains("Steg 2: 0,1460 BTC in i en annan mixer", finding.Why, StringComparison.Ordinal);
        Assert.Contains("inte samma belopp", finding.Why, StringComparison.Ordinal);
        Assert.Contains("0,00675 BTC i troliga avgifter i mixrarna (4,5 % av den första insättningen)", finding.Why,
            StringComparison.Ordinal);
        Assert.Contains("aldrig ett faktum", finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void Back_into_the_same_mixer_says_so()
    {
        var finding = Assert.Single(Run(TwoSteps(Parting.MixerAgain)));

        Assert.Contains("Steg 2: 0,1460 BTC in i samma mixer igen", finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_new_deposit_gets_the_mixers_own_suggestion()
    {
        var found = new MixerOutputsCheck().Run(new CheckInput(
            TwoSteps(Parting.OtherMixer), TwoMixers, new Dictionary<Txid, TransactionConclusions>()));

        var second = Assert.Single(found, f => f.Key == $"{Txid("second-deposit").Value}:0");
        Assert.Equal(Wpkh("hop-2"), second.Candidates[0].Address);
    }

    [Fact]
    public void An_output_that_goes_anywhere_else_is_no_chain()
    {
        var first = FirstOut();
        var shop = Tx("to-shop", Parting.Height + 10).Spend(first, 0).Out(Wpkh("shop"), 14_600_000).Build();

        Assert.Empty(Run([Parting.Deposit(), first, shop]));
        Assert.Empty(Run([Parting.Deposit(), first]));
    }

    [Fact]
    public void An_approved_output_is_followed_before_a_likelier_one_and_called_approved()
    {
        var first = FirstOut();
        // 0.147 BTC two days later: a lower score than the first output, and more than the second deposit, so that
        // only the first deposit can have paid it.
        var lower = Parting.Payout("hop-1-lower", Wpkh("hop-1-lower"), 14_700_000, 300);
        var viaLower = Tx("via-lower", Parting.Height + 310).Spend(lower, 0).Out(Parting.OtherMixer, 14_690_000)
            .Build();
        IReadOnlyList<BitcoinTransaction> graph =
            [Parting.Deposit(), first, SecondDeposit(first, Parting.OtherMixer), lower, viaLower];
        var key = $"{Txid("parts-deposit").Value}:0";

        var likely = Assert.Single(Run(graph));
        var approved = Assert.Single(Run(
            graph, [new ApprovedCandidate(new HeuristicId(MixerOutputsCheck.Name, 1), key, Txid("hop-1-lower"), 0)]));

        Assert.Contains(Txid("second-deposit"), likely.Basis.Select(b => b.Txid));
        Assert.Contains("trolig utgång 0,14625", likely.Why, StringComparison.Ordinal);
        Assert.Contains(Txid("via-lower"), approved.Basis.Select(b => b.Txid));
        Assert.DoesNotContain(Txid("second-deposit"), approved.Basis.Select(b => b.Txid));
        Assert.Contains("godkänd utgång 0,1470 BTC", approved.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_part_of_the_best_combination_that_goes_into_a_mixer_is_followed_with_the_combinations_fee()
    {
        var parts = Parting.Parts();
        var onward = Tx("part-onward", Parting.Height + 80).Spend(parts[1], 0).Out(Parting.OtherMixer, 4_990_000)
            .Build();

        var finding = Assert.Single(Run([Parting.Deposit(), .. parts, onward]));

        Assert.Contains("trolig del 0,0500 BTC", finding.Why, StringComparison.Ordinal);
        Assert.Contains("trolig avgift 0,0030 BTC", finding.Why, StringComparison.Ordinal);
        Assert.Equal("Mixer efter mixer, 2 steg", finding.Title);
    }

    [Fact]
    public void A_chain_is_followed_at_most_ten_steps()
    {
        var graph = new List<BitcoinTransaction>
        {
            Tx("long-0", Parting.Height).In(Wpkh("long-victim"), 16_000_000).Out(Parting.Mixer, 15_000_000).Build(),
        };
        var amount = 15_000_000L;
        for (var i = 0; i < 12; i++)
        {
            var paid = amount * 98 / 100;
            var output = Parting.Payout($"long-out-{i}", Wpkh($"long-out-{i}"), paid, (i * 20) + 4);
            amount = paid - 10_000;
            graph.Add(output);
            graph.Add(Tx($"long-{i + 1}", Parting.Height + ((i + 1) * 20)).Spend(output, 0).Out(Parting.Mixer, amount)
                .Build());
        }

        var finding = Assert.Single(Run(graph));

        Assert.Equal("Mixer efter mixer, 10 steg", finding.Title);
        Assert.Contains("Steg 10:", finding.Why, StringComparison.Ordinal);
        Assert.DoesNotContain("Steg 11:", finding.Why, StringComparison.Ordinal);
        Assert.Contains("Kedjan följdes högst 10 steg", finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_known_mixer_there_is_no_chain()
    {
        var found = Analysis.Run(new CheckInput(
            TwoSteps(Parting.OtherMixer), [], new Dictionary<Txid, TransactionConclusions>()));

        Assert.Empty(found);
    }

    [Fact]
    public void The_same_input_gives_the_same_findings()
    {
        var all = TwoSteps(Parting.OtherMixer);

        Assert.Equal(
            Run(all).Select(FindingCodec.Encode), Run([.. all.Reverse()]).Select(FindingCodec.Encode));
    }

    [Fact]
    public void An_output_into_a_coinjoin_ends_the_chain_there()
    {
        using var harness = new Harness();
        var deposit = Tx("cj-deposit", Parting.Height).In(Wpkh("cj-victim"), 1_100_000).Out(Parting.Mixer, 1_030_000)
            .Build();
        var output = Parting.Payout("cj-out", Wpkh("cj-out"), 1_005_000, 4);
        var mix = Tx("cj-whirlpool", Parting.Height + 10).Spend(output, 0).In(Wpkh("cj-in-2"), 1_005_000);
        for (var i = 3; i <= 5; i++)
        {
            mix.In(Wpkh($"cj-in-{i}"), 1_000_000);
        }

        for (var i = 1; i <= 5; i++)
        {
            mix.Out(Wpkh($"cj-mixed-{i}"), 1_000_000);
        }

        var whirlpool = mix.Build();

        var check = Assert.Single(
            harness.Run([deposit, output, whirlpool], Parting.Mixer).Checks,
            c => c.Analysis.Name == MixerChainCheck.Name);

        Assert.Equal("Mixer efter mixer, 2 steg", check.Finding.Title);
        Assert.EndsWith("slutar i CoinJoin", check.Finding.Summary, StringComparison.Ordinal);
        Assert.Equal([whirlpool.Txid], check.Finding.Transactions);
        Assert.Contains("känner igen som en CoinJoin (en bedömning)", check.Finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void Approving_a_candidate_in_the_mixers_suggestion_makes_the_step_approved()
    {
        using var harness = new Harness();
        var graph = TwoSteps(Parting.MixerAgain);
        var run = harness.Run(graph, Parting.Mixer, Parting.MixerAgain);
        var mixer = Assert.Single(run.Checks, c => c.Analysis.Name == MixerOutputsCheck.Name
            && c.Finding.Key == $"{Txid("parts-deposit").Value}:0");
        var chain = Assert.Single(run.Checks, c => c.Analysis.Name == MixerChainCheck.Name);
        Assert.Contains("trolig utgång 0,14625", chain.Finding.Why, StringComparison.Ordinal);

        var hop = Assert.Single(mixer.Finding.Candidates, c => c.Address == Wpkh("hop-1"));
        harness.Service.Decide(harness.Case, new DecisionRequest(
            mixer.Analysis, mixer.Finding.Key, hop.Key, CheckVerdict.Approved, null));
        var after = Assert.Single(
            harness.Run(graph, Parting.Mixer, Parting.MixerAgain).Checks,
            c => c.Analysis.Name == MixerChainCheck.Name);

        Assert.Contains("godkänd utgång 0,14625", after.Finding.Why, StringComparison.Ordinal);
        Assert.True(harness.Verify().IsIntact);
    }

    [Fact]
    public void A_dismissed_output_is_never_followed()
    {
        var graph = TwoSteps(Parting.OtherMixer);
        var coin = $"{Txid("hop-1").Value}:0";

        Assert.Empty(Run(graph, dismissed: [new DismissedDecision(MixerOutputsCheck.Identity, DepositKey, coin)]));
        Assert.Empty(Run(graph, dismissed: [new DismissedDecision(MixerOutputsCheck.Identity, DepositKey, null)]));
        Assert.Single(Run(graph, dismissed: [new DismissedDecision(MixerPartsCheck.Identity, DepositKey, null)]));
    }

    [Fact]
    public void An_approved_part_outside_the_best_combination_is_a_part_with_the_fee_of_all_approved_parts()
    {
        var first = Parting.PartA();
        var onward = Tx("a-onward", Parting.Height + 20).Spend(first, 0).Out(Parting.OtherMixer, 5_990_000).Build();
        IReadOnlyList<ApprovedCandidate> approved =
        [
            new(MixerPartsCheck.Identity, DepositKey, Txid("part-a"), 0),
            new(MixerPartsCheck.Identity, DepositKey, Txid("part-b"), 0),
        ];

        var finding = Assert.Single(Run([Parting.Deposit(), first, Parting.PartB(), onward], approved));

        Assert.Contains("godkänd del 0,0600 BTC", finding.Why, StringComparison.Ordinal);
        Assert.Contains("trolig avgift 0,0400 BTC", finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void Approvals_in_another_analysis_are_not_read()
    {
        var first = Parting.PartA();
        var onward = Tx("a-onward", Parting.Height + 20).Spend(first, 0).Out(Parting.OtherMixer, 5_990_000).Build();
        IReadOnlyList<ApprovedCandidate> approved =
            [new(new HeuristicId("peel-chain-area", 1), DepositKey, Txid("part-a"), 0)];

        Assert.Empty(Run([Parting.Deposit(), first, Parting.PartB(), onward], approved));
    }

    [Fact]
    public void Two_actors_with_the_same_service_are_the_same_mixer()
    {
        IReadOnlyList<KnownMixer> same =
        [
            new KnownMixer([Parting.Mixer], "Mixer: Blandaren"),
            new KnownMixer([Parting.OtherMixer], "Mixer: Blandaren"),
        ];

        var finding = Assert.Single(Run(TwoSteps(Parting.OtherMixer), mixers: same));

        Assert.Contains("Steg 2: 0,1460 BTC in i samma mixer igen", finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void An_output_into_a_possible_coinjoin_is_no_end_of_the_chain()
    {
        using var harness = new Harness();
        var deposit = Tx("pcj-deposit", Parting.Height).In(Wpkh("pcj-victim"), 2_600_000).Out(Parting.Mixer, 2_500_000)
            .Build();
        var output = Parting.Payout("pcj-out", Wpkh("pcj-out"), 2_450_000, 4);
        var possible = Tx("pcj-possible", Parting.Height + 10).Spend(output, 0).In(Wpkh("pcj-other"), 2_350_000)
            .Out(Wpkh("pcj-mixed-1"), 2_400_000).Out(Wpkh("pcj-mixed-2"), 2_400_000).Build();

        var checks = harness.Run([deposit, output, possible], Parting.Mixer).Checks;

        Assert.Contains(checks, c => c.Analysis.Name == CoinJoinCheck.Name && c.Finding.Title == "Möjlig CoinJoin");
        Assert.DoesNotContain(checks, c => c.Analysis.Name == MixerChainCheck.Name);
    }

    [Fact]
    public void A_withdrawn_approval_makes_the_step_likely_again()
    {
        using var harness = new Harness();
        var graph = TwoSteps(Parting.MixerAgain);
        var mixer = Assert.Single(
            harness.Run(graph, Parting.Mixer, Parting.MixerAgain).Checks,
            c => c.Analysis.Name == MixerOutputsCheck.Name && c.Finding.Key == DepositKey);
        var hop = Assert.Single(mixer.Finding.Candidates, c => c.Address == Wpkh("hop-1"));
        var approval = harness.Service.Decide(harness.Case, new DecisionRequest(
            mixer.Analysis, mixer.Finding.Key, hop.Key, CheckVerdict.Approved, null));
        Assert.Contains(
            "godkänd utgång 0,14625",
            Chain(harness.Run(graph, Parting.Mixer, Parting.MixerAgain)).Finding.Why,
            StringComparison.Ordinal);

        harness.Service.Decide(harness.Case, DecisionRequest.TakeBack(approval));
        var after = Chain(harness.Run(graph, Parting.Mixer, Parting.MixerAgain));

        Assert.Contains("trolig utgång 0,14625", after.Finding.Why, StringComparison.Ordinal);
        Assert.True(harness.Verify().IsIntact);
    }

    private static StoredCheck Chain(CheckRun run) =>
        Assert.Single(run.Checks, c => c.Analysis.Name == MixerChainCheck.Name);
}
