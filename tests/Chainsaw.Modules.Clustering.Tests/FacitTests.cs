using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using static Chainsaw.Modules.Clustering.Tests.Synthetic;

namespace Chainsaw.Modules.Clustering.Tests;

/// <summary>
/// The facit: each synthetic pattern is analysed in a real case, and the test says what is merged and, as
/// important, what is not.
/// </summary>
public sealed class FacitTests : IDisposable
{
    private readonly CaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void A_payment_merges_the_inputs_and_the_change_but_not_the_shop()
    {
        _harness.Analyse(Patterns.PaymentWithChange());

        var cluster = _harness.ClusterOf(Wpkh("payer-1"));

        AssertMembers(cluster, Wpkh("payer-1"), Wpkh("payer-2"), Wpkh("payer-change"));
        Assert.False(cluster.Contains(Pkh("shop")));
        Assert.Single(_harness.ClusterOf(Pkh("shop")).Members);
    }

    [Fact]
    public void Weak_signs_only_suggest_the_change()
    {
        _harness.Analyse(Patterns.PaymentWithWeakSigns());

        var cluster = _harness.ClusterOf(Wpkh("weak-1"));

        AssertMembers(cluster, Wpkh("weak-1"), Wpkh("weak-2"));
        var suggestion = Assert.Single(cluster.Suggestions);
        Assert.Equal(HeuristicNames.ChangeAddress, suggestion.Evidence.Heuristic.Name);
        Assert.Contains(Wpkh("weak-y"), suggestion.Evidence.Addresses);
        Assert.Equal(LinkState.Suggested, suggestion.State);
    }

    [Fact]
    public void A_consolidation_merges_every_input_but_not_the_output()
    {
        _harness.Analyse(Patterns.Consolidation());

        var cluster = _harness.ClusterOf(Wpkh("cons-1"));

        Assert.Equal(6, cluster.Members.Count);
        Assert.All(Enumerable.Range(1, 6), i => Assert.True(cluster.Contains(Wpkh($"cons-{i}"))));
        Assert.False(cluster.Contains(Wpkh("cons-target")));
    }

    [Fact]
    public void An_exchange_sweep_merges_the_deposit_addresses_but_never_the_depositors_or_the_hot_wallet()
    {
        var deposits = Patterns.Deposits();
        _harness.Analyse([.. deposits, Patterns.Sweep(deposits)]);

        var exchange = _harness.ClusterOf(Wpkh("deposit-1"));

        AssertMembers(exchange, [.. Enumerable.Range(1, 5).Select(i => Wpkh($"deposit-{i}"))]);
        Assert.False(exchange.Contains(Pkh("hot-wallet")));
        foreach (var i in Enumerable.Range(1, 5))
        {
            Assert.False(exchange.Contains(Wpkh($"user-{i}")));
            Assert.False(exchange.Contains(Wpkh($"user-{i}-change")));
            Assert.Single(_harness.ClusterOf(Wpkh($"user-{i}")).Members);
        }
    }

    [Fact]
    public void A_batch_payout_merges_no_customer_with_the_payer()
    {
        _harness.Analyse(Patterns.BatchPayout());

        var payer = _harness.ClusterOf(Pkh("hot-wallet"));

        Assert.Single(payer.Members);
        Assert.All(payer.Suggestions, link => Assert.NotEqual(LinkState.Merged, link.State));
        var conclusions = _harness.Service.GetConclusions(_harness.Case, Txid("batch"));
        Assert.DoesNotContain(conclusions.Evidence, evidence => evidence.Strength == EvidenceStrength.Merge);
    }

    [Fact]
    public void A_batch_of_equal_amounts_from_one_input_is_no_coinjoin()
    {
        _harness.Analyse(Patterns.BatchOfEqualAmounts());

        var conclusions = _harness.Service.GetConclusions(_harness.Case, Txid("equal-batch"));

        Assert.DoesNotContain(conclusions.Findings, found => found.Finding is CoinJoinFinding);
        Assert.Null(conclusions.TrailEnd);
        Assert.All(Enumerable.Range(1, 3), i => Assert.Single(_harness.ClusterOf(Wpkh($"equal-{i}")).Members));
    }

    [Fact]
    public void Equal_round_withdrawals_from_several_inputs_are_only_a_possible_coinjoin()
    {
        var batch = Tx("withdrawals")
            .In(Pkh("hot-a"), 20_000_000)
            .In(Pkh("hot-b"), 20_000_000)
            .In(Pkh("hot-c"), 20_000_000)
            .Out(Wpkh("withdrawal-1"), 1_000_000)
            .Out(Wpkh("withdrawal-2"), 1_000_000)
            .Out(Wpkh("withdrawal-3"), 1_000_000)
            .Out(Pkh("hot-change"), 56_990_000)
            .Build();

        _harness.Analyse(batch);

        var conclusions = _harness.Service.GetConclusions(_harness.Case, Txid("withdrawals"));
        var coinJoin = Assert.IsType<CoinJoinFinding>(Assert.Single(conclusions.Findings,
            found => found.Finding is CoinJoinFinding).Finding);
        Assert.Equal((CoinJoinPattern.Generic, CoinJoinCertainty.Possible), (coinJoin.Pattern, coinJoin.Certainty));
        Assert.Null(conclusions.TrailEnd);
        Assert.Single(_harness.ClusterOf(Pkh("hot-a")).Members);
        Assert.NotEmpty(_harness.ClusterOf(Pkh("hot-a")).Suggestions);
    }

    [Fact]
    public void A_whirlpool_mix_is_recognised_ends_the_trail_and_merges_nothing()
    {
        var run = _harness.Analyse(Patterns.Whirlpool());

        var conclusions = _harness.Service.GetConclusions(_harness.Case, Txid("whirlpool"));
        var coinJoin = Assert.IsType<CoinJoinFinding>(conclusions.TrailEnd);
        Assert.Equal(CoinJoinPattern.Whirlpool, coinJoin.Pattern);
        Assert.Equal(5, coinJoin.EqualOutputs);
        Assert.Equal(1_000_000, coinJoin.Denomination.Value);
        Assert.Equal([Txid("whirlpool")], run.CoinJoins);
        Assert.Equal(0, run.Merges);
        AssertNothingMerged("whirl-in-", 1, 5);
        AssertNothingMerged("whirl-out-", 1, 5);
        var stopped = Assert.Single(_harness.ClusterOf(Wpkh("whirl-in-1")).Refused);
        Assert.Equal(LinkState.Stopped, stopped.State);
        Assert.Equal(HeuristicNames.CommonInputOwnership, stopped.Evidence.Heuristic.Name);
        Assert.Contains("CoinJoin", stopped.Evidence.Adjustment, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wasabi1")]
    [InlineData("wasabi2")]
    public void A_wasabi_round_is_recognised_and_merges_nothing(string label)
    {
        var transaction = label == "wasabi1" ? Patterns.Wasabi1() : Patterns.Wasabi2();
        _harness.Analyse(transaction);

        var conclusions = _harness.Service.GetConclusions(_harness.Case, Txid(label));

        Assert.Equal(CoinJoinPattern.Wasabi, Assert.IsType<CoinJoinFinding>(conclusions.TrailEnd).Pattern);
        Assert.All(conclusions.Evidence, evidence => Assert.Equal(EvidenceStrength.Stopped, evidence.Strength));
        foreach (var input in transaction.Inputs)
        {
            Assert.Single(_harness.ClusterOf(input.Address!).Members);
        }
    }

    [Fact]
    public void A_joinmarket_round_is_recognised_and_merges_nothing()
    {
        _harness.Analyse(Patterns.JoinMarket());

        var conclusions = _harness.Service.GetConclusions(_harness.Case, Txid("joinmarket"));

        Assert.Equal(CoinJoinPattern.JoinMarket, Assert.IsType<CoinJoinFinding>(conclusions.TrailEnd).Pattern);
        AssertNothingMerged("jm-in-", 0, 7);
    }

    [Fact]
    public void A_possible_coinjoin_only_suggests_and_does_not_end_the_trail()
    {
        _harness.Analyse(Patterns.PossibleCoinJoin());

        var conclusions = _harness.Service.GetConclusions(_harness.Case, Txid("possible"));
        var cluster = _harness.ClusterOf(Wpkh("possible-1"));

        var coinJoin = Assert.IsType<CoinJoinFinding>(Assert.Single(conclusions.Findings).Finding);
        Assert.Equal(CoinJoinCertainty.Possible, coinJoin.Certainty);
        Assert.Null(conclusions.TrailEnd);
        Assert.Single(cluster.Members);
        var suggestion = Assert.Single(cluster.Suggestions);
        Assert.Equal(HeuristicNames.CommonInputOwnership, suggestion.Evidence.Heuristic.Name);
        Assert.NotNull(suggestion.Evidence.Adjustment);
    }

    [Fact]
    public void Equal_dust_is_no_coinjoin()
    {
        _harness.Analyse(Patterns.EqualDust());

        AssertMembers(_harness.ClusterOf(Wpkh("dust-1")), Wpkh("dust-1"), Wpkh("dust-2"));
    }

    [Fact]
    public void A_peel_chain_merges_the_inner_holders_but_no_recipient_and_neither_end()
    {
        var chain = Patterns.PeelChain(5);
        _harness.Analyse([.. chain]);

        var holder = _harness.ClusterOf(Wpkh("peel-holder-1"));

        AssertMembers(holder, [.. Enumerable.Range(1, 4).Select(i => Wpkh($"peel-holder-{i}"))]);
        Assert.All(Enumerable.Range(0, 5), i => Assert.False(holder.Contains(Pkh($"peel-recipient-{i}"))));
        Assert.Equal(2, holder.Suggestions.Count(link => link.Evidence.Heuristic.Name == HeuristicNames.PeelChain));
        var hop = _harness.Service.GetConclusions(_harness.Case, chain[2].Txid);
        var peel = Assert.Single(hop.Findings.Select(found => found.Finding).OfType<PeelChainFinding>());
        Assert.Equal((2, 2, 5, 1, 0), (peel.HopsBefore, peel.HopsAfter, peel.ChainLength, peel.ContinuingOutput,
            peel.PeelOutput));
        Assert.Equal([chain[1].Txid.Value, chain[0].Txid.Value, chain[3].Txid.Value, chain[4].Txid.Value], peel.Hops);
    }

    [Fact]
    public void A_peel_chain_never_merges_the_victim_who_paid_into_it_or_the_exchange_it_pays_out_to()
    {
        var victim = Tx("victim-pays", 900_100)
            .In(Wpkh("victim"), 100_000_000)
            .Out(Wpkh("scammer-1"), 95_000_000)
            .Out(Wpkh("victim-change"), 4_990_000)
            .Build();
        var hop1 = Tx("scam-1", 900_101).Spend(victim, 0).Out(Pkh("scam-peel-1"), 2_000_000)
            .Out(Wpkh("scammer-2"), 92_990_000).Build();
        var hop2 = Tx("scam-2", 900_102).Spend(hop1, 1).Out(Pkh("scam-peel-2"), 2_000_000)
            .Out(Wpkh("scammer-3"), 90_980_000).Build();
        var cashOut = Tx("scam-out", 900_103).Spend(hop2, 1).Out(Wpkh("exchange-deposit"), 85_123_456)
            .Out(Wpkh("scammer-4"), 5_846_544).Build();
        var sweep = Tx("exchange-sweep", 900_104).Spend(cashOut, 0).In(Wpkh("stranger-deposit"), 10_000_000)
            .Out(Pkh("exchange-hot"), 95_000_000).Build();

        _harness.Analyse(victim, hop1, hop2, cashOut, sweep);

        var scammer = _harness.ClusterOf(Wpkh("scammer-1"));
        Assert.False(scammer.Contains(Wpkh("victim")));
        Assert.False(scammer.Contains(Wpkh("exchange-deposit")));
        Assert.False(scammer.Contains(Wpkh("stranger-deposit")));
        Assert.False(_harness.ClusterOf(Wpkh("victim")).Contains(Wpkh("stranger-deposit")));
    }

    [Fact]
    public void Two_hops_are_no_peel_chain()
    {
        _harness.Analyse([.. Patterns.PeelChain(2)]);

        var holder = _harness.ClusterOf(Wpkh("peel-holder-0"));

        Assert.Single(holder.Members);
        Assert.All(holder.Suggestions, link => Assert.Equal(HeuristicNames.ChangeAddress, link.Evidence.Heuristic.Name));
    }

    [Fact]
    public void A_peel_chain_is_recognised_when_the_later_hops_are_fetched()
    {
        var chain = Patterns.PeelChain(4);
        _harness.Analyse(chain[0], chain[1]);
        Assert.Single(_harness.ClusterOf(Wpkh("peel-holder-1")).Members);

        var run = _harness.Analyse([.. chain]);

        Assert.True(run.NewResults > 0);
        Assert.Equal(3, _harness.ClusterOf(Wpkh("peel-holder-1")).Members.Count);
    }

    [Fact]
    public void Every_member_says_why_it_is_in_the_cluster()
    {
        _harness.Analyse(Patterns.PaymentWithChange());

        var cluster = _harness.ClusterOf(Wpkh("payer-2"));

        Assert.Equal(Wpkh("payer-2"), cluster.Members[0].Address);
        Assert.Null(cluster.Members[0].JoinedBy);
        foreach (var member in cluster.Members.Skip(1))
        {
            var link = Assert.IsType<ClusterLink>(member.JoinedBy);
            Assert.Equal(LinkState.Merged, link.State);
            Assert.Equal(Txid("payment"), link.Evidence.Txid);
            Assert.Equal(1, link.Evidence.Heuristic.Version);
            Assert.False(string.IsNullOrWhiteSpace(link.Evidence.Reason));
            Assert.Contains(member.Address, link.Evidence.Addresses);
            Assert.Contains(member.Via!, link.Evidence.Addresses);
        }
    }

    [Fact]
    public void The_way_between_two_members_is_explained_link_by_link()
    {
        _harness.Analyse(Patterns.PaymentWithChange());

        var path = _harness.Service.ExplainConnection(_harness.Case, Wpkh("payer-2"), Wpkh("payer-change"));

        Assert.NotNull(path);
        Assert.Equal(
            [HeuristicNames.CommonInputOwnership, HeuristicNames.ChangeAddress],
            path.Select(link => link.Evidence.Heuristic.Name));
        Assert.Contains("3 av 5 tecken", path[1].Evidence.Reason, StringComparison.Ordinal);
        Assert.Empty(_harness.Service.ExplainConnection(_harness.Case, Wpkh("payer-2"), Wpkh("payer-2"))!);
        Assert.Null(_harness.Service.ExplainConnection(_harness.Case, Wpkh("payer-2"), Pkh("shop")));
    }

    private void AssertNothingMerged(string prefix, int first, int count)
    {
        foreach (var i in Enumerable.Range(first, count))
        {
            Assert.Single(_harness.ClusterOf(Wpkh($"{prefix}{i}")).Members);
        }
    }

    private static void AssertMembers(Cluster cluster, params BitcoinAddress[] expected) =>
        Assert.Equal(
            expected.Select(address => address.Value).Order(),
            cluster.Addresses.Select(address => address.Value).Order());
}
