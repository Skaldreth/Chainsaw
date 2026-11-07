using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using static Chainsaw.Modules.Clustering.Tests.Synthetic;

namespace Chainsaw.Modules.Clustering.Tests;

/// <summary>The heuristics one by one, without a case.</summary>
public sealed class HeuristicTests
{
    private static readonly KnownTransactions Nothing = new([]);

    [Fact]
    public void Common_input_ownership_needs_two_different_input_addresses()
    {
        var heuristic = new CommonInputOwnershipHeuristic();
        var single = Tx("single").In(Wpkh("a"), 1_000_000).In(Wpkh("a"), 2_000_000).Out(Wpkh("b"), 2_900_000).Build();

        Assert.Empty(heuristic.Analyse(single, Nothing).Proposals);
        var proposal = Assert.Single(heuristic.Analyse(Patterns.PaymentWithChange(), Nothing).Proposals);
        Assert.Equal([Wpkh("payer-1"), Wpkh("payer-2")], proposal.Addresses);
        Assert.Equal(EvidenceStrength.Merge, proposal.Strength);
        Assert.Equal("common-input-ownership v1", heuristic.Id.ToString());
    }

    [Fact]
    public void Common_input_ownership_leaves_a_coinbase_alone()
    {
        var coinbase = new BitcoinTransaction(
            Txid("coinbase"), Block(1), [new BitcoinInput(0, true, null, null, null, null, null, 0)],
            [new BitcoinOutput(0, BitcoinScriptType.PayToWitnessPublicKeyHash, Wpkh("miner"),
                new Chainsaw.SharedKernel.Satoshi(312_500_000), "", null)],
            Chainsaw.SharedKernel.Satoshi.Zero, 100, 400, 2, 0);

        Assert.Empty(new CommonInputOwnershipHeuristic().Analyse(coinbase, Nothing).Proposals);
        Assert.Empty(new CoinJoinHeuristic().Analyse(coinbase, Nothing).Findings);
        Assert.Empty(new ChangeAddressHeuristic().Analyse(coinbase, Nothing).Findings);
        Assert.Empty(new PeelChainHeuristic().Analyse(coinbase, Nothing).Findings);
    }

    [Fact]
    public void The_change_signs_are_counted_and_named()
    {
        var result = new ChangeAddressHeuristic().Analyse(Patterns.PaymentWithChange(), Nothing);

        var finding = Assert.IsType<ChangeFinding>(Assert.Single(result.Findings));
        Assert.Equal(1, finding.OutputIndex);
        Assert.Equal(
            [ChangeSign.SameScriptTypeAsInputs, ChangeSign.OthersRound, ChangeSign.OptimalChange], finding.Signs);
        Assert.Equal((3, 0, 5), (finding.Score, finding.SignsAgainst, finding.SignsChecked));
        var proposal = Assert.Single(result.Proposals);
        Assert.Equal([Wpkh("payer-1"), Wpkh("payer-change")], proposal.Addresses);
        Assert.Equal(1, proposal.ChangeOutput);
        Assert.Equal(EvidenceStrength.Merge, proposal.Strength);
    }

    [Fact]
    public void A_fresh_change_address_is_a_sign_only_when_the_other_address_is_known()
    {
        var earlier = Tx("earlier", 899_000).In(Wpkh("someone"), 9_000_000).Out(Pkh("shop"), 8_000_000).Build();
        var known = new KnownTransactions([earlier, Patterns.PaymentWithChange()]);

        var finding = Assert.IsType<ChangeFinding>(
            Assert.Single(new ChangeAddressHeuristic().Analyse(Patterns.PaymentWithChange(), known).Findings));

        Assert.Contains(ChangeSign.FreshAddress, finding.Signs);
        Assert.Equal(4, finding.Score);
    }

    [Fact]
    public void The_change_signs_ask_where_each_output_was_seen_only_once()
    {
        // An exchange's batch payout with a few hundred outputs once asked this for every pair of outputs, which
        // made one expansion spend seconds in the change heuristic alone.
        var batch = Tx("big-batch").In(Wpkh("exchange"), 100_000_000);
        for (var index = 0; index < 200; index++)
        {
            batch = batch.Out(Wpkh($"customer-{index}"), 400_000 + index);
        }

        var payout = batch.Build();
        var known = new CountingContext(new KnownTransactions([payout]));

        var result = new ChangeAddressHeuristic().Analyse(payout, known);

        Assert.Equal(payout.Outputs.Count, known.SeenBeforeAsked);
        Assert.Equal(
            new ChangeAddressHeuristic().Analyse(payout, Nothing).Findings.Select(finding => finding.Reason),
            result.Findings.Select(finding => finding.Reason));
    }

    [Fact]
    public void Change_back_to_an_input_address_is_a_sign_but_merges_nothing_new()
    {
        var back = Tx("back").In(Wpkh("self"), 5_000_000).Out(Wpkh("other"), 1_234_567).Out(Wpkh("self"), 3_712_345)
            .Build();

        var result = new ChangeAddressHeuristic().Analyse(back, Nothing);

        var finding = Assert.IsType<ChangeFinding>(Assert.Single(result.Findings));
        Assert.Equal(1, finding.OutputIndex);
        Assert.Contains(ChangeSign.BackToInput, finding.Signs);
        Assert.Empty(result.Proposals);
    }

    [Fact]
    public void Equal_signs_on_both_outputs_point_out_nothing()
    {
        var even = Tx("even").In(Wpkh("e"), 5_000_000).Out(Wpkh("e-1"), 1_234_567).Out(Wpkh("e-2"), 3_712_345)
            .Build();

        var result = new ChangeAddressHeuristic().Analyse(even, Nothing);

        Assert.Empty(result.Findings);
        Assert.Empty(result.Proposals);
    }

    [Fact]
    public void Change_in_a_batch_is_never_merged_however_many_signs()
    {
        var batch = Tx("batch3").In(Wpkh("b"), 5_000_000).In(Wpkh("b-2"), 8_000_000)
            .Out(Pkh("b-1"), 5_000_000).Out(Pkh("b-2"), 6_000_000).Out(Wpkh("b-change"), 1_987_654).Build();

        var result = new ChangeAddressHeuristic().Analyse(batch, Nothing);

        var proposal = Assert.Single(result.Proposals);
        Assert.Equal(EvidenceStrength.Suggestion, proposal.Strength);
        Assert.Equal(3, Assert.IsType<ChangeFinding>(Assert.Single(result.Findings)).Score);
    }

    [Theory]
    [InlineData(4, 5, CoinJoinPattern.Whirlpool)]
    [InlineData(5, 4, null)]
    public void Whirlpool_needs_five_inputs_and_five_pool_outputs(int inputs, int outputs, CoinJoinPattern? expected)
    {
        var tx = Tx($"whirl-{inputs}-{outputs}");
        for (var i = 0; i < 5; i++)
        {
            tx.In(Wpkh($"w-in-{i}"), 1_010_000);
        }

        for (var i = 0; i < outputs; i++)
        {
            tx.Out(Wpkh($"w-out-{i}"), 1_000_000);
        }

        if (outputs < 5)
        {
            tx.Out(Wpkh("w-extra"), 1_000_000 + inputs);
        }

        var finding = new CoinJoinHeuristic().Analyse(tx.Build(), Nothing).Findings.OfType<CoinJoinFinding>().Single();

        if (expected is { } pattern)
        {
            Assert.Equal(pattern, finding.Pattern);
        }
        else
        {
            Assert.NotEqual(CoinJoinPattern.Whirlpool, finding.Pattern);
        }
    }

    [Fact]
    public void A_wasabi_round_with_more_equal_outputs_than_input_addresses_is_still_recognised()
    {
        var tx = Tx("wasabi-dense");
        for (var i = 0; i < 20; i++)
        {
            tx.In(Wpkh($"dense-in-{i}"), 6_000_000);
        }

        for (var i = 0; i < 24; i++)
        {
            tx.Out(Tr($"dense-out-{i}"), 5_000_000);
        }

        var finding = Assert.IsType<CoinJoinFinding>(
            Assert.Single(new CoinJoinHeuristic().Analyse(tx.Build(), Nothing).Findings));

        Assert.Equal((CoinJoinPattern.Wasabi, true), (finding.Pattern, finding.EndsTrail));
    }

    [Fact]
    public void Equal_outputs_to_the_same_address_are_no_coinjoin()
    {
        var same = Tx("same").In(Wpkh("s-1"), 3_000_000).In(Wpkh("s-2"), 3_000_000)
            .Out(Wpkh("s-out"), 2_500_000).Out(Wpkh("s-out"), 2_500_000).Build();

        Assert.Empty(new CoinJoinHeuristic().Analyse(same, Nothing).Findings);
    }

    [Fact]
    public void Inputs_from_one_funding_transaction_are_no_coinjoin()
    {
        var one = Tx("one-funding").In(Wpkh("o-1"), 3_000_000, "f").In(Wpkh("o-2"), 3_000_000, "f")
            .Out(Wpkh("o-out-1"), 2_500_000).Out(Wpkh("o-out-2"), 2_500_000).Build();

        Assert.Empty(new CoinJoinHeuristic().Analyse(one, Nothing).Findings);
    }

    [Fact]
    public void A_recognised_coinjoin_stops_and_a_possible_one_suggests()
    {
        var recognised = new CoinJoinHeuristic().Analyse(Patterns.JoinMarket(), Nothing);
        var possible = new CoinJoinHeuristic().Analyse(Patterns.PossibleCoinJoin(), Nothing);

        Assert.Equal(VetoKind.Stop, recognised.Veto!.Kind);
        Assert.Equal(VetoKind.Suggest, possible.Veto!.Kind);
        Assert.Contains("Vem som äger vilken output", recognised.Findings[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_peel_hop_needs_one_input_and_a_small_peel()
    {
        var even = Tx("even-split", 900_100).In(Wpkh("h"), 10_000_000)
            .Out(Pkh("x"), 4_000_000).Out(Wpkh("y"), 5_990_000).Build();
        var known = new KnownTransactions([even]);

        Assert.Empty(new PeelChainHeuristic().Analyse(even, known).Findings);
    }

    [Fact]
    public void A_peel_chain_with_an_op_return_hop_is_broken_there()
    {
        var chain = Patterns.PeelChain(2);
        var marked = Tx("peel-marked", 900_200).Spend(chain[1], 1)
            .Out(Pkh("m-peel"), 1_000_000).Out(Wpkh("m-hold"), 80_000_000).OpReturn().Build();
        var known = new KnownTransactions([.. chain, marked]);

        Assert.Empty(new PeelChainHeuristic().Analyse(chain[0], known).Findings);
    }

    [Fact]
    public void Seen_before_counts_only_what_certainly_came_earlier()
    {
        var early = Tx("early", 10).In(Wpkh("x"), 2_000_000).Out(Wpkh("seen"), 1_000_000).Build();
        var same = Tx("same-block", 11).In(Wpkh("y"), 2_000_000).Out(Wpkh("same"), 1_000_000).Build();
        var pending = Tx("pending", null).In(Wpkh("z"), 2_000_000).Out(Wpkh("late"), 1_000_000).Build();
        var subject = Tx("subject", 11).In(Wpkh("q"), 2_000_000).Out(Wpkh("seen"), 1_000).Build();
        var known = new KnownTransactions([early, same, pending, subject]);

        Assert.Equal(early, known.SeenBefore(Wpkh("seen"), subject));
        Assert.Null(known.SeenBefore(Wpkh("same"), subject));
        Assert.Null(known.SeenBefore(Wpkh("late"), subject));
        Assert.Equal(early, known.SeenBefore(Wpkh("seen"), pending));
        Assert.Equal(subject, known.FindSpender(Txid("subject:funding:0"), 0));
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("Capital", 1)]
    [InlineData("1digit", 1)]
    [InlineData("under_score", 1)]
    [InlineData("ok", 0)]
    public void A_heuristic_id_has_a_plain_name_and_a_version_from_one(string name, int version)
    {
        Assert.ThrowsAny<ArgumentException>(() => new HeuristicId(name, version));
    }

    /// <summary>What the case knows, counting how often a heuristic asks where an address was seen.</summary>
    private sealed class CountingContext(IHeuristicContext inner) : IHeuristicContext
    {
        public int SeenBeforeAsked { get; private set; }

        public int Count => inner.Count;

        public BitcoinTransaction? Find(Txid txid) => inner.Find(txid);

        public BitcoinTransaction? FindSpender(Txid txid, int outputIndex) => inner.FindSpender(txid, outputIndex);

        public BitcoinTransaction? SeenBefore(BitcoinAddress address, BitcoinTransaction transaction)
        {
            SeenBeforeAsked++;
            return inner.SeenBefore(address, transaction);
        }
    }
}
