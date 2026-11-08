using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using static Chainsaw.Modules.Clustering.Tests.Synthetic;

namespace Chainsaw.Modules.Clustering.Tests;

/// <summary>
/// The facit for what the fourth round added to the clustering: Wasabi 2's standard amounts and Ashigaru's pools
/// (<c>coinjoin v2</c>), the wallet's fingerprint as a sign of change (<c>change-address v2</c>), peel chains that
/// are topped up or whose change is the smaller output (<c>peel-chain v2</c>), and a version that is drawn back.
/// </summary>
public sealed class RoundFourTests : IDisposable
{
    private readonly CaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void An_ashigaru_mix_of_the_new_pool_is_a_whirlpool_coinjoin()
    {
        var tx = Tx("ashigaru");
        for (var i = 1; i <= 5; i++)
        {
            tx.In(Wpkh($"ash-in-{i}"), 2_500_000 + (i * 1_000));
        }

        for (var i = 1; i <= 5; i++)
        {
            tx.Out(Wpkh($"ash-out-{i}"), 2_500_000);
        }

        var result = new CoinJoinHeuristic().Analyse(tx.Build(), new KnownTransactions([]));

        var finding = Assert.IsType<CoinJoinFinding>(Assert.Single(result.Findings));
        Assert.Equal((CoinJoinPattern.Whirlpool, CoinJoinCertainty.Recognised), (finding.Pattern, finding.Certainty));
    }

    [Fact]
    public void Wasabi_2s_standard_amounts_are_the_powers_and_the_one_two_five_series()
    {
        var amounts = CoinJoinHeuristic.Wasabi2Denominations;

        Assert.Contains(8_192L, amounts);
        Assert.Contains(6_561L, amounts);
        Assert.Contains(13_122L, amounts);
        Assert.Contains(50_000L, amounts);
        Assert.Contains(200_000_000L, amounts);
        Assert.DoesNotContain(4_096L, amounts);
        Assert.DoesNotContain(30_000L, amounts);
        Assert.DoesNotContain(1_234_567L, amounts);
    }

    [Fact]
    public void A_large_batch_of_one_round_amount_is_never_a_wasabi_2_round()
    {
        var tx = Tx("big-batch");
        for (var i = 1; i <= 25; i++)
        {
            tx.In(Wpkh($"bb-in-{i}"), 2_000_000 + i);
        }

        for (var i = 1; i <= 25; i++)
        {
            tx.Out(Wpkh($"bb-out-{i}"), 1_000_000);
        }

        var result = new CoinJoinHeuristic().Analyse(tx.Build(), new KnownTransactions([]));

        var finding = Assert.IsType<CoinJoinFinding>(Assert.Single(result.Findings));
        Assert.NotEqual(CoinJoinPattern.Wasabi2, finding.Pattern);
    }

    [Fact]
    public void The_output_the_same_wallet_spends_later_has_the_fingerprint_sign()
    {
        var (payment, change, other) = FingerprintCase(changeSpenderVersion: 2, otherSpenderVersion: 1);

        var finding = ChangeOf(payment, change, other);

        Assert.Equal(1, finding.OutputIndex);
        Assert.Contains(ChangeSign.SameFingerprintSpent, finding.Signs);
        Assert.Contains(change.Txid.Value, finding.SeenIn);
        Assert.Contains("samma sorts plånbok", finding.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_spenders_with_the_same_fingerprint_give_no_fingerprint_sign()
    {
        var (payment, change, other) = FingerprintCase(changeSpenderVersion: 2, otherSpenderVersion: 2);

        var result = new ChangeAddressHeuristic().Analyse(payment, new KnownTransactions([payment, change, other]));

        Assert.DoesNotContain(
            result.Findings.OfType<ChangeFinding>(), found => found.Signs.Contains(ChangeSign.SameFingerprintSpent));
    }

    [Fact]
    public void An_output_nobody_is_known_to_spend_gives_no_fingerprint_sign()
    {
        var (payment, change, _) = FingerprintCase(changeSpenderVersion: 2, otherSpenderVersion: 1);

        var result = new ChangeAddressHeuristic().Analyse(payment, new KnownTransactions([payment, change]));

        Assert.DoesNotContain(
            result.Findings.OfType<ChangeFinding>(), found => found.Signs.Contains(ChangeSign.SameFingerprintSpent));
    }

    [Fact]
    public void A_peel_chain_topped_up_with_more_inputs_is_followed()
    {
        var hop1 = Tx("top-1", 900_300).In(Wpkh("top-holder-0"), 50_000_000)
            .Out(Pkh("top-peel-1"), 1_200_000).Out(Wpkh("top-holder-1"), 48_790_000).Build();
        var hop2 = Tx("top-2", 900_301).Spend(hop1, 1).In(Wpkh("top-extra"), 20_000_000)
            .Out(Pkh("top-peel-2"), 1_300_000).Out(Wpkh("top-holder-2"), 67_480_000).Build();
        var hop3 = Tx("top-3", 900_302).Spend(hop2, 1)
            .Out(Pkh("top-peel-3"), 1_400_000).Out(Wpkh("top-holder-3"), 66_070_000).Build();

        _harness.Analyse(hop1, hop2, hop3);

        var finding = Peel(hop2);
        Assert.Equal((3, 1, 1), (finding.ChainLength, finding.ContinuingOutput, finding.ExtraInputs));
        Assert.Contains("fylls på", finding.Reason, StringComparison.Ordinal);
        var proposal = Assert.Single(
            _harness.Service.GetConclusions(_harness.Case, hop2.Txid).Evidence,
            evidence => evidence.Heuristic.Name == HeuristicNames.PeelChain);
        Assert.Equal([Wpkh("top-holder-1"), Wpkh("top-holder-2")], proposal.Addresses);
        Assert.Equal(EvidenceStrength.Suggestion, proposal.Strength);
    }

    [Fact]
    public void Two_payments_of_the_same_shape_that_do_not_follow_each_other_are_no_peel_chain()
    {
        var first = Tx("lone-1", 900_500).In(Wpkh("lone-a"), 10_000_000)
            .Out(Pkh("lone-shop"), 6_000_000).Out(Wpkh("lone-a-change"), 3_990_000).Build();
        var second = Tx("lone-2", 900_501).In(Wpkh("lone-b"), 10_000_000)
            .Out(Pkh("lone-shop-2"), 6_000_000).Out(Wpkh("lone-b-change"), 3_990_000).Build();

        _harness.Analyse(first, second);

        Assert.DoesNotContain(
            _harness.Service.GetConclusions(_harness.Case, first.Txid).Findings,
            found => found.Finding is PeelChainFinding);
    }

    [Fact]
    public void A_peel_chain_whose_smaller_output_goes_on_is_followed_when_the_larger_is_known_spent_elsewhere()
    {
        var hop1 = Tx("small-1", 900_400).In(Wpkh("small-holder-0"), 50_000_000)
            .Out(Pkh("small-big-1"), 30_000_000).Out(Wpkh("small-holder-1"), 19_990_000).Build();
        var shop = Tx("small-shop", 900_401).Spend(hop1, 0).Out(Pkh("small-shop-wallet"), 29_990_000).Build();
        var hop2 = Tx("small-2", 900_401).Spend(hop1, 1)
            .Out(Pkh("small-peel-2"), 2_000_000).Out(Wpkh("small-holder-2"), 17_980_000).Build();
        var hop3 = Tx("small-3", 900_402).Spend(hop2, 1)
            .Out(Pkh("small-peel-3"), 1_500_000).Out(Wpkh("small-holder-3"), 16_470_000).Build();

        _harness.Analyse(hop1, shop, hop2, hop3);

        var finding = Peel(hop1);
        Assert.Equal((1, 0), (finding.ContinuingOutput, finding.PeelOutput));
        Assert.Contains("den mindre", finding.Reason, StringComparison.Ordinal);
        Assert.Contains(shop.Txid.Value, finding.Hops.Concat(Basis(hop1)));
        Assert.Equal(3, Peel(hop2).ChainLength);
    }

    [Fact]
    public void A_payment_the_investigator_followed_is_never_taken_for_the_victims_change()
    {
        var victim = Tx("victim-pay", 900_700).In(Wpkh("victim"), 250_000_000)
            .Out(Wpkh("scammer"), 50_000_000).Out(Wpkh("victim-change"), 199_990_000).Build();
        var scam1 = Tx("scam-1", 900_701).Spend(victim, 0)
            .Out(Wpkh("s-a"), 30_000_000).Out(Wpkh("s-b"), 19_990_000).Build();
        var scam2 = Tx("scam-2", 900_702).Spend(scam1, 0)
            .Out(Wpkh("s-c"), 25_000_000).Out(Wpkh("s-d"), 4_990_000).Build();

        _harness.Analyse(victim, scam1, scam2);

        Assert.DoesNotContain(
            _harness.Service.GetConclusions(_harness.Case, victim.Txid).Findings,
            found => found.Finding is PeelChainFinding);
        Assert.False(_harness.ClusterOf(Wpkh("victim")).Contains(Wpkh("scammer")));
        Assert.DoesNotContain(
            _harness.ClusterOf(Wpkh("victim")).Suggestions,
            link => link.Evidence.Addresses.Contains(Wpkh("scammer")));
    }

    [Fact]
    public void The_fingerprint_never_makes_a_merge_of_the_victim_and_the_scammer()
    {
        // The victim pays the scammer's P2WPKH from two P2WPKH coins and gets the change to a P2TR address. The
        // scammer spends with the common fingerprint, the victim later without RBF.
        var pay = Tx("fp2-pay", 900_900).In(Wpkh("fp2-victim-a"), 5_000_000).In(Wpkh("fp2-victim-b"), 5_000_000)
            .Out(Wpkh("fp2-scammer"), 2_345_678).Out(Tr("fp2-victim-change"), 7_644_000).Build();
        var scam = Tx("fp2-scam-spend", 900_901).Spend(pay, 0).Out(Wpkh("fp2-scam-next"), 2_340_000).Build();
        var victim = Tx("fp2-victim-spend", 900_902).Final().Spend(pay, 1).Out(Wpkh("fp2-shop"), 7_640_000).Build();

        _harness.Analyse(pay, scam, victim);

        Assert.False(_harness.ClusterOf(Wpkh("fp2-victim-a")).Contains(Wpkh("fp2-scammer")));
        var change = Assert.Single(
            _harness.Service.GetConclusions(_harness.Case, pay.Txid).Evidence,
            evidence => evidence.Heuristic.Name == HeuristicNames.ChangeAddress);
        Assert.Equal(EvidenceStrength.Suggestion, change.Strength);
        Assert.Contains("räknas aldrig för att slå ihop", change.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_spender_of_only_one_output_never_seals_an_unchanged_conclusion_again()
    {
        var payment = Tx("rs-pay", 900_800).In(Wpkh("rs-payer"), 10_000_000)
            .Out(Wpkh("rs-shop"), 4_000_000).Out(Wpkh("rs-change"), 5_990_000).Build();
        _harness.Analyse(payment);
        var before = _harness.Count("clustering_evidence");
        var spend = Tx("rs-spend", 900_801).Spend(payment, 0).Out(Wpkh("rs-x"), 3_990_000).Build();

        _harness.Analyse(payment, spend);

        Assert.Equal(before, _harness.Count("clustering_evidence"));
    }

    [Fact]
    public void A_version_drawn_back_stops_counting_and_the_older_runs_instead()
    {
        _harness.Analyse(Patterns.PaymentWithChange());
        Assert.True(_harness.ClusterOf(Wpkh("payer-1")).Contains(Wpkh("payer-change")));
        var stored = _harness.Count("clustering_evidence");

        var withdrawal = _harness.Service.Withdraw(
            _harness.Case, new HeuristicId(HeuristicNames.ChangeAddress, 2), "Slog ihop fel i provet.");

        Assert.False(_harness.ClusterOf(Wpkh("payer-1")).Contains(Wpkh("payer-change")));
        Assert.True(_harness.ClusterOf(Wpkh("payer-1")).Contains(Wpkh("payer-2")));
        Assert.Equal(stored, _harness.Count("clustering_evidence"));
        Assert.Equal(withdrawal, Assert.Single(_harness.Service.GetWithdrawals(_harness.Case)));
        Assert.Equal("Slog ihop fel i provet.", withdrawal.Reason);
        Assert.Single(_harness.Case.ReadJournal(), entry => entry.Kind == ClusteringJournalKinds.VersionWithdrawn);

        var run = _harness.Analyse(Patterns.PaymentWithChange());

        Assert.Contains("change-address v1", run.Journal!.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("change-address v2", run.Journal.Details, StringComparison.Ordinal);
        var change = Assert.Single(
            _harness.Service.GetConclusions(_harness.Case, Txid("payment")).Findings,
            found => found.Heuristic.Name == HeuristicNames.ChangeAddress);
        Assert.Equal(1, change.Heuristic.Version);
        Assert.True(_harness.ClusterOf(Wpkh("payer-1")).Contains(Wpkh("payer-change")));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_new_coinjoin_after_the_running_version_is_drawn_back_is_still_recognised()
    {
        _harness.Service.Withdraw(_harness.Case, new HeuristicId(HeuristicNames.CoinJoin, 2), null);

        _harness.Analyse(Patterns.Whirlpool());

        var conclusions = _harness.Service.GetConclusions(_harness.Case, Txid("whirlpool"));
        var coinJoin = Assert.IsType<CoinJoinFinding>(conclusions.TrailEnd);
        Assert.Equal(CoinJoinPattern.Whirlpool, coinJoin.Pattern);
        Assert.Equal(1, Assert.Single(conclusions.Findings).Heuristic.Version);
        Assert.All(conclusions.Evidence, evidence => Assert.Equal(EvidenceStrength.Stopped, evidence.Strength));
    }

    [Fact]
    public void A_version_drawn_back_never_vetoes_what_it_weighed_before()
    {
        _harness.Analyse(Patterns.Whirlpool());
        var withdrawal = _harness.Service.Withdraw(_harness.Case, new HeuristicId(HeuristicNames.CoinJoin, 2), null);

        var weighed = _harness.Service.GetConclusions(_harness.Case, Txid("whirlpool"));

        Assert.Null(weighed.TrailEnd);
        var stopped = Assert.Single(weighed.Evidence);
        Assert.Equal(EvidenceStrength.Suggestion, stopped.Strength);
        Assert.Equal(EvidenceStore.WithdrawnWeighing, stopped.Adjustment);

        _harness.Analyse(Patterns.Whirlpool());
        Assert.Equal(EvidenceStrength.Stopped, Assert.Single(
            _harness.Service.GetConclusions(_harness.Case, Txid("whirlpool")).Evidence).Strength);

        var undo = _harness.Service.TakeBackWithdrawal(_harness.Case, withdrawal.Sequence, "Fel version.");
        Assert.NotNull(undo);
        Assert.False(Assert.Single(_harness.Service.GetWithdrawals(_harness.Case)).IsInForce);
        Assert.Null(_harness.Service.TakeBackWithdrawal(_harness.Case, withdrawal.Sequence, null));
        Assert.Equal(2, _harness.Service.GetConclusions(_harness.Case, Txid("whirlpool")).Findings
            .Single(found => found.Finding is CoinJoinFinding).Heuristic.Version);
        Assert.Single(_harness.Case.ReadJournal(), entry => entry.Kind == ClusteringJournalKinds.WithdrawalTakenBack);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void Only_a_registered_version_with_another_left_can_be_drawn_back_and_twice_gives_the_first()
    {
        var change = new HeuristicId(HeuristicNames.ChangeAddress, 2);
        var first = _harness.Service.Withdraw(_harness.Case, change, null);

        Assert.Equal(first, _harness.Service.Withdraw(_harness.Case, change, "Igen."));
        Assert.Equal(1, _harness.Count("clustering_withdrawals"));
        Assert.Throws<ArgumentException>(() =>
            _harness.Service.Withdraw(_harness.Case, new HeuristicId(UserMerges.Name, 1), null));
        Assert.Throws<ArgumentException>(() =>
            _harness.Service.Withdraw(_harness.Case, new HeuristicId("no-such", 9), null));
        Assert.Throws<ArgumentException>(() =>
            _harness.Service.Withdraw(_harness.Case, new HeuristicId(HeuristicNames.ChangeAddress, 1), null));
        Assert.Throws<ArgumentException>(() => _harness.Service.Withdraw(
            _harness.Case, new HeuristicId(HeuristicNames.CommonInputOwnership, 1), null));
        Assert.Equal(1, _harness.Count("clustering_withdrawals"));
    }

    [Fact]
    public void An_older_version_runs_again_when_the_newer_is_drawn_back()
    {
        using var harness = new CaseHarness(services =>
            services.AddSingleton<IClusteringHeuristic, SilentCommonInputV2>());
        harness.Analyse(Patterns.Consolidation());
        Assert.Single(harness.ClusterOf(Wpkh("cons-1")).Members);

        harness.Service.Withdraw(harness.Case, new HeuristicId(HeuristicNames.CommonInputOwnership, 2), null);
        harness.Analyse(Patterns.Consolidation());

        Assert.Equal(6, harness.ClusterOf(Wpkh("cons-1")).Members.Count);
        Assert.All(
            harness.Service.GetConclusions(harness.Case, Txid("consolidation")).Evidence,
            evidence => Assert.Equal(1, evidence.Heuristic.Version));
    }

    [Fact]
    public void A_payjoin_where_the_payee_adds_a_coin_of_its_own_type_holds_the_merge_back()
    {
        var payJoin = PayJoin();

        _harness.Analyse(payJoin);

        Assert.False(_harness.ClusterOf(Wpkh("pj-sender")).Contains(Tr("pj-receiver")));
        var common = Assert.Single(
            _harness.Service.GetConclusions(_harness.Case, payJoin.Txid).Evidence,
            evidence => evidence.Heuristic.Name == HeuristicNames.CommonInputOwnership);
        Assert.Equal(EvidenceStrength.Suggestion, common.Strength);
        Assert.Contains("PayJoin", common.Adjustment, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("scammer")]
    [InlineData("wallet")]
    public void An_ordinary_payment_with_an_unneeded_input_is_still_merged(string label)
    {
        // The scammer pays an exchange from three victims' deposits, one of them not needed; or a wallet pays a shop
        // from two coins of its own type, one of them not needed.
        var payment = label == "scammer"
            ? Tx("pj-ord", 901_000).In(Wpkh("dep-1"), 3_000_000).In(Wpkh("dep-2"), 2_500_000)
                .In(Wpkh("dep-3"), 200_000).Out(Wpkh("exch"), 5_000_000).Out(Wpkh("chg"), 690_000).Build()
            : Tx("pj-wallet", 901_001).In(Wpkh("w-1"), 3_000_000).In(Wpkh("w-2"), 1_000_000)
                .Out(Pkh("w-shop"), 2_000_000).Out(Wpkh("w-change"), 1_990_000).Build();

        _harness.Analyse(payment);

        var first = payment.Inputs[0].Address!;
        Assert.True(_harness.ClusterOf(first).Contains(payment.Inputs[1].Address!));
        var finding = Assert.IsType<GenericFinding>(Assert.Single(
            _harness.Service.GetConclusions(_harness.Case, payment.Txid).Findings,
            found => found.Heuristic.Name == HeuristicNames.PayJoin).Finding);
        Assert.Contains("\"strong\":false", finding.Data, StringComparison.Ordinal);
    }

    [Fact]
    public void A_case_can_switch_the_payjoin_veto_off_by_drawing_version_2_back()
    {
        _harness.Service.Withdraw(_harness.Case, new HeuristicId(HeuristicNames.PayJoin, 2), "Inte i det här ärendet.");

        _harness.Analyse(PayJoin());

        Assert.True(_harness.ClusterOf(Wpkh("pj-sender")).Contains(Tr("pj-receiver")));
        Assert.Contains(
            _harness.Service.GetConclusions(_harness.Case, Txid("pj-real")).Findings,
            found => found.Heuristic == new HeuristicId(HeuristicNames.PayJoin, 1));
    }

    [Fact]
    public void A_batch_payout_of_round_amounts_is_never_a_wasabi_2_round()
    {
        var tx = Tx("round-payout");
        for (var i = 1; i <= 25; i++)
        {
            tx.In(Wpkh($"rp-in-{i}"), 10_000_000 + i);
        }

        var amounts = new long[] { 1_000_000, 5_000_000, 10_000_000 };
        for (var i = 0; i < 24; i++)
        {
            tx.Out(Wpkh($"rp-out-{i}"), amounts[i % 3]);
        }

        var result = new CoinJoinHeuristic().Analyse(tx.Build(), new KnownTransactions([]));

        Assert.DoesNotContain(
            result.Findings.OfType<CoinJoinFinding>(), finding => finding.Pattern == CoinJoinPattern.Wasabi2);
    }

    [Fact]
    public void A_finding_of_a_sort_of_its_own_is_sealed_and_read_back()
    {
        using var harness = new CaseHarness(services =>
            services.AddSingleton<IClusteringHeuristic, GenericProbe>());

        harness.Analyse(Patterns.PaymentWithChange());

        var found = Assert.IsType<GenericFinding>(Assert.Single(
            harness.Service.GetConclusions(harness.Case, Txid("payment")).Findings,
            stored => stored.Heuristic.Name == GenericProbe.Name).Finding);
        Assert.Equal(("prov", "{\"outputs\":2}", "Ett prov."), (found.Sort, found.Data, found.Reason));
        Assert.True(harness.Verify().IsIntact);
    }

    /// <summary>
    /// A PayJoin: the payer pays from a P2WPKH coin, the payee adds a P2TR coin of its own and gets the payment and its
    /// coin back to a P2TR address; the payer's change goes back to P2WPKH.
    /// </summary>
    private static BitcoinTransaction PayJoin() => Tx("pj-real", 901_100)
        .In(Wpkh("pj-sender"), 3_000_000).In(Tr("pj-receiver"), 1_000_000)
        .Out(Wpkh("pj-change"), 1_490_000).Out(Tr("pj-paid"), 2_500_000).Build();

    /// <summary>An analysis that gives a finding of a sort of its own.</summary>
    private sealed class GenericProbe : IUtxoHeuristic
    {
        public const string Name = "generic-probe";

        public HeuristicId Id { get; } = new(Name, 1);

        public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

        public HeuristicResult Analyse(BitcoinTransaction transaction, IHeuristicContext context) =>
            new(Id, transaction.Txid, [], [new GenericFinding("Ett prov.", "prov", "{\"outputs\":2}")]);
    }

    [Fact]
    public void Many_transactions_conclusions_are_read_at_once_as_one_by_one()
    {
        var chain = Patterns.PeelChain(3);
        _harness.Analyse([.. chain, Patterns.PaymentWithChange()]);
        var txids = chain.Select(hop => hop.Txid).Append(Txid("payment")).Append(Txid("never")).ToList();

        var all = _harness.Service.GetConclusions(_harness.Case, txids);

        Assert.Equal(txids.Count, all.Count);
        foreach (var txid in txids)
        {
            var one = _harness.Service.GetConclusions(_harness.Case, txid);
            Assert.Equal(one.Evidence.Select(e => e.Sequence), all[txid].Evidence.Select(e => e.Sequence));
            Assert.Equal(one.Findings.Select(f => f.Sequence), all[txid].Findings.Select(f => f.Sequence));
        }

        Assert.Empty(all[Txid("never")].Findings);
    }

    /// <summary>
    /// A payment with two outputs of the same type and no other sign, whose outputs are each spent later by a
    /// transaction of the given version.
    /// </summary>
    private static (BitcoinTransaction Payment, BitcoinTransaction Change, BitcoinTransaction Other) FingerprintCase(
        long changeSpenderVersion, long otherSpenderVersion)
    {
        var payment = Tx("fp-payment", 900_600).In(Wpkh("fp-payer"), 10_000_000)
            .Out(Wpkh("fp-shop"), 4_123_456).Out(Wpkh("fp-change"), 5_866_544).Build();
        var change = Tx("fp-change-spend", 900_601).Version(changeSpenderVersion).Spend(payment, 1)
            .Out(Wpkh("fp-next"), 5_850_000).Build();
        var other = Tx("fp-shop-spend", 900_602).Version(otherSpenderVersion).Spend(payment, 0)
            .Out(Wpkh("fp-shop-next"), 4_100_000).Build();
        return (payment, change, other);
    }

    private static ChangeFinding ChangeOf(params BitcoinTransaction[] transactions)
    {
        var result = new ChangeAddressHeuristic().Analyse(transactions[0], new KnownTransactions(transactions));
        return Assert.IsType<ChangeFinding>(Assert.Single(result.Findings));
    }

    private IEnumerable<string> Basis(BitcoinTransaction hop) =>
        _harness.Case.Read(reader =>
        {
            using var command = reader.CreateCommand(
                $"SELECT basis FROM clustering_evidence WHERE heuristic = 'peel-chain' AND txid = '{hop.Txid.Value}'");
            return (string)command.ExecuteScalar()!;
        }).Split('"').Where(part => part.Length == 64);

    private PeelChainFinding Peel(BitcoinTransaction hop) =>
        Assert.Single(_harness.Service.GetConclusions(_harness.Case, hop.Txid).Findings
            .Select(found => found.Finding).OfType<PeelChainFinding>());
}
