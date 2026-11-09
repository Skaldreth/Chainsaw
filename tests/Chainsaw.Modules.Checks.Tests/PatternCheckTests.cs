using System.Diagnostics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Microsoft.Extensions.DependencyInjection;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>
/// The facit for <c>pattern v1</c>, the rules through the open door for pattern recognition: what each rule recognises,
/// what it leaves alone (the everyday shapes the review probed with among them), and how often it raises a false alarm
/// in an ordinary, random graph whose coins are linked.
/// </summary>
public sealed class PatternCheckTests
{
    private const long Start = 900_000;

    /// <summary>Thirty-one days in blocks of ten minutes: outside the walks' window.</summary>
    private const long MonthAndADay = 31 * 144;

    private static readonly BitcoinAddress Mixer = Wpkh("mixer");

    private static readonly BitcoinAddress Exchange = Wpkh("exchange");

    // ---- The door ----------------------------------------------------------------------------------------------

    [Fact]
    public void The_shape_has_an_actor_per_owner_and_a_line_per_payer_and_payee_with_its_coins()
    {
        var tx = Tx("pay", Start).In(Wpkh("a"), 1_000_000, "funding").Out(Wpkh("b"), 600_000)
            .Out(Wpkh("a"), 390_000).Build();

        var shape = GraphShape.Of(Input([tx]));

        Assert.Equal(2, shape.Actors.Count);
        var flow = Assert.Single(shape.Flows);
        Assert.Equal(600_000, flow.Amount.Value);
        Assert.Equal(Wpkh("a").Value, shape.Actors[flow.From].Key);
        Assert.Equal(Wpkh("b").Value, shape.Actors[flow.To].Key);
        Assert.Equal(tx.Block, flow.Block);
        Assert.False(flow.IsShared);
        Assert.True(flow.ChangeBack);
        Assert.Equal([new ShapeCoin(Txid("funding"), 0)], flow.Spent);
        Assert.Equal([new ShapeCoin(tx.Txid, 0)], flow.Paid);
    }

    [Fact]
    public void A_line_that_spends_another_lines_coin_is_linked_to_it()
    {
        var first = Tx("first", Start).In(Wpkh("a"), 2_000_000).Out(Wpkh("b"), 1_000_000).Build();
        var second = Tx("second", Start + 1).Spend(first, 0).Out(Wpkh("c"), 990_000).Build();
        var other = Tx("other", Start + 2).In(Wpkh("b"), 2_000_000).Out(Wpkh("c"), 990_000).Build();

        var shape = GraphShape.Of(Input([first, second, other]));

        Assert.True(GraphShape.Spends(shape.Flows[0], shape.Flows[1]));
        Assert.False(GraphShape.Spends(shape.Flows[0], shape.Flows[2]));
        Assert.Equal([1], shape.SpentBy(new ShapeCoin(first.Txid, 0)));
    }

    [Fact]
    public void A_transaction_with_several_payers_is_an_actor_with_a_line_per_payer_and_per_payee()
    {
        var coinJoin = CoinJoin("cj", Start, participants: 5);

        var shape = GraphShape.Of(Input([coinJoin]));

        var middle = Assert.Single(shape.Actors, actor => actor.Sort == ActorSort.Transaction);
        Assert.Equal(coinJoin.Txid, middle.Transaction);
        Assert.Equal(10, shape.Flows.Count);
        Assert.All(shape.Flows, flow => Assert.True(flow.IsShared));
        Assert.All(shape.Flows, flow => Assert.True(flow.From == middle.Index || flow.To == middle.Index));
    }

    [Fact]
    public void Within_a_block_a_spender_comes_after_what_it_spends_whatever_the_txids()
    {
        var first = Tx("zz-first", Start).In(Wpkh("a"), 2_000_000).Out(Wpkh("b"), 1_000_000).Build();
        var second = Tx("aa-second", Start).Spend(first, 0).Out(Wpkh("c"), 990_000).Build();
        var (early, late) = string.CompareOrdinal(first.Txid.Value, second.Txid.Value) < 0
            ? (first, second)
            : (second, first);

        var shape = GraphShape.Of(Input([late, early]));

        Assert.Equal([first.Txid, second.Txid], shape.Flows.Select(flow => flow.Txid));
    }

    [Fact]
    public void The_shape_knows_the_labels_sorts_and_the_start_points()
    {
        var tx = Tx("in", Start).In(Wpkh("victim"), 1_000_000).Out(Mixer, 500_000).Out(Exchange, 490_000).Build();
        var input = Input([tx], mixers: [Mixer], exchanges: [Exchange]) with
        {
            Protected = new HashSet<BitcoinAddress> { Wpkh("victim") },
        };

        var shape = GraphShape.Of(input);

        Assert.Equal(
            [ActorSort.Protected, ActorSort.Mixer, ActorSort.Exchange],
            shape.Actors.Select(actor => actor.Sort));
    }

    [Fact]
    public void The_analysis_is_registered_as_an_analysis_like_the_others_and_has_no_weights()
    {
        using var harness = new Harness();

        Assert.Contains(new HeuristicId(PatternCheck.Name, 1), harness.Service.Analyses);
        Assert.Null(((IGraphShapeAnalysis)new PatternCheck()).WeightsSha256);
    }

    [Fact]
    public void Another_shape_analysis_is_added_by_registering_it_and_nothing_else()
    {
        using var harness = new Harness(services => services.AddSingleton<ICheckAnalysis, CountingAnalysis>());
        var tx = Tx("pay", Start).In(Wpkh("a"), 1_000_000).Out(Wpkh("b"), 600_000).Build();

        var run = harness.Run([tx]);

        var check = Assert.Single(run.Checks, check => check.Analysis.Name == CountingAnalysis.Name);
        Assert.Equal("1 linjer", check.Finding.Title);
        Assert.Equal([tx.Txid], check.Finding.Basis.Select(basis => basis.Txid));
    }

    // ---- Samma insamling --------------------------------------------------------------------------------------

    [Fact]
    public void Two_collections_that_sweep_to_the_same_actor_are_the_same_collection()
    {
        var txs = Collection("s1", Start, Wpkh("launder")).Concat(Collection("s2", Start + 500, Wpkh("launder")));

        var finding = Single(Run([.. txs]), "collect:");

        Assert.Equal("collect:" + Wpkh("launder").Value, finding.Key);
        Assert.Equal("Samma insamling, 2 aktörer", finding.Title);
        Assert.Contains(Wpkh("s1"), finding.Addresses);
        Assert.Contains(Wpkh("s2"), finding.Addresses);
        Assert.Contains("6 avsändare", finding.Why, StringComparison.Ordinal);
        Assert.Contains("bland de hämtade tx:erna", finding.Why, StringComparison.Ordinal);
        Assert.Contains(finding.Basis, basis => basis.Txid == Txid("s2-sweep"));
        AssertJudgement(finding);
    }

    [Fact]
    public void Every_collection_counts_not_only_the_first()
    {
        // s1 first collects and sweeps elsewhere, then collects again and sweeps where s2 does.
        var txs = Collection("s1a", Start, Wpkh("elsewhere"), collector: Wpkh("s1"))
            .Concat(Collection("s1b", Start + 300, Wpkh("launder"), collector: Wpkh("s1")))
            .Concat(Collection("s2", Start + 600, Wpkh("launder")));

        Assert.Single(Run([.. txs]), finding => finding.Key == "collect:" + Wpkh("launder").Value);
    }

    [Fact]
    public void The_key_holds_when_a_later_collection_is_fetched()
    {
        var two = Collection("s1", Start, Wpkh("launder")).Concat(Collection("s2", Start + 500, Wpkh("launder")))
            .ToList();
        var three = two.Concat(Collection("s3", Start + 900, Wpkh("launder"))).ToList();

        Assert.Equal(Single(Run(two), "collect:").Key, Single(Run(three), "collect:").Key);
    }

    [Fact]
    public void One_collection_alone_or_two_that_sweep_apart_are_no_pattern()
    {
        Assert.Empty(Run([.. Collection("s1", Start, Wpkh("launder"))]));
        Assert.Empty(Run([.. Collection("s1", Start, Wpkh("x")).Concat(Collection("s2", Start + 500, Wpkh("y")))]));
    }

    [Fact]
    public void Two_shops_that_sweep_to_exchanges_are_no_pattern_the_same_exchange_or_two()
    {
        var other = Wpkh("exchange-b");
        var apart = Collection("shop1", Start, Exchange).Concat(Collection("shop2", Start + 500, other)).ToList();
        var together = Collection("shop1", Start, Exchange).Concat(Collection("shop2", Start + 500, Exchange))
            .ToList();

        Assert.Empty(Run(apart, exchanges: [Exchange, other]));
        Assert.Empty(Run(together, exchanges: [Exchange]));
    }

    [Fact]
    public void Hot_wallets_that_sweep_their_deposit_addresses_to_the_same_cold_wallet_are_no_collection()
    {
        var txs = HotWallet("h1", Start, Wpkh("cold")).Concat(HotWallet("h2", Start + 500, Wpkh("cold"))).ToList();

        Assert.Empty(Run(txs));
    }

    [Fact]
    public void An_address_that_forwards_each_payment_at_once_is_no_collection()
    {
        var txs = new List<BitcoinTransaction>();
        foreach (var part in new[] { "s1", "s2" })
        {
            for (var i = 0; i < 6; i++)
            {
                var paid = Tx($"{part}-pay-{i}", Start + (i * 10)).In(Wpkh($"{part}-victim-{i}"), 2_000_000)
                    .Out(Wpkh(part), 1_000_000).Build();
                txs.Add(paid);
                txs.Add(Tx($"{part}-fwd-{i}", Start + (i * 10) + 1).Spend(paid, 0).Out(Wpkh("launder"), 990_000)
                    .Build());
            }
        }

        Assert.DoesNotContain(Run(txs), finding => finding.Key.StartsWith("collect:", StringComparison.Ordinal));
    }

    // ---- Samma väg -------------------------------------------------------------------------------------------

    [Fact]
    public void Coins_from_two_parts_passed_through_two_mules_each_to_the_same_exchange_go_the_same_way()
    {
        var txs = Way("v1", Start).Concat(Way("v2", Start + 300)).ToList();

        var finding = Single(Run(txs, exchanges: [Exchange]), "route:");

        Assert.Equal($"route:{Exchange.Value}>3", finding.Key);
        Assert.Equal("Samma väg, 2 delar", finding.Title);
        Assert.Contains("spenderar mynt ur det förra", finding.Why, StringComparison.Ordinal);
        Assert.Equal(6, finding.Basis.Count);
        AssertJudgement(finding);
    }

    [Fact]
    public void A_way_ends_at_the_first_labelled_actor_and_is_never_followed_through_a_mixer()
    {
        // Victim → mule → mixer → exchange: the way ends at the mixer after two lines, which is too short.
        var txs = new List<BitcoinTransaction>();
        foreach (var victim in new[] { "v1", "v2" })
        {
            var paid = Tx($"{victim}-paid", Start).In(Wpkh(victim), 20_000_000).Out(Wpkh($"{victim}-mule"), 10_000_000)
                .Build();
            var mixed = Tx($"{victim}-mixed", Start + 5).Spend(paid, 0).Out(Mixer, 9_900_000).Build();
            var cashed = Tx($"{victim}-cashed", Start + 50).Spend(mixed, 0).Out(Exchange, 9_000_000).Build();
            txs.AddRange([paid, mixed, cashed]);
        }

        Assert.Empty(Run(txs, mixers: [Mixer], exchanges: [Exchange]));
    }

    [Fact]
    public void One_way_money_moved_together_or_payments_from_other_coins_are_no_second_way()
    {
        Assert.Empty(Run([.. Way("v1", Start)], exchanges: [Exchange]));

        // Two victims pay the same scammer, who moves it all on in one transaction: one way, not two.
        var p1 = Tx("p1", Start).In(Wpkh("v1"), 2_000_000).Out(Wpkh("scammer"), 1_000_000).Build();
        var p2 = Tx("p2", Start + 1).In(Wpkh("v2"), 2_000_000).Out(Wpkh("scammer"), 1_000_000).Build();
        var hop = Tx("hop", Start + 2).Spend(p1, 0).Spend(p2, 0).Out(Wpkh("mule"), 1_990_000).Build();
        var cash = Tx("cash", Start + 3).Spend(hop, 0).Out(Exchange, 1_980_000).Build();
        Assert.Empty(Run([p1, p2, hop, cash], exchanges: [Exchange]));

        // The mules pay the exchange from other coins than the victims': the coins do not follow.
        Assert.Empty(Run([.. Way("v1", Start, linked: false), .. Way("v2", Start + 300, linked: false)],
            exchanges: [Exchange]));
    }

    [Fact]
    public void A_way_that_takes_longer_than_the_window_or_has_no_label_says_nothing()
    {
        Assert.Empty(Run([.. Way("v1", Start, gap: MonthAndADay), .. Way("v2", Start + 300, gap: MonthAndADay)],
            exchanges: [Exchange]));
        Assert.Empty(Run([.. Way("v1", Start), .. Way("v2", Start + 300)]));
    }

    // ---- Rundgång ---------------------------------------------------------------------------------------------

    [Fact]
    public void Coins_that_come_back_through_two_others_one_of_them_busy_are_a_round_trip()
    {
        var txs = Round("r", Start, back: 8_500_000);

        var finding = Single(Run(txs), "round:");

        Assert.Equal($"round:{Txid("r-0").Value}:{Txid("r-2").Value}", finding.Key);
        Assert.Equal("Rundgång, 3 steg", finding.Title);
        Assert.Contains("85 %", finding.Why, StringComparison.Ordinal);
        Assert.Contains("spenderar mynt ur det förra", finding.Why, StringComparison.Ordinal);
        Assert.Equal(3, finding.Basis.Count);
        AssertJudgement(finding);
    }

    [Fact]
    public void Two_round_trips_from_the_same_transaction_have_their_own_keys()
    {
        var home = Wpkh("two-home");
        var out1 = Tx("two-0", Start).In(home, 30_000_000).Out(Wpkh("two-a"), 10_000_000)
            .Out(Wpkh("two-b"), 10_000_000).Build();
        var a = Tx("two-a1", Start + 1).Spend(out1, 0).Out(Wpkh("two-a2"), 9_800_000).Build();
        var a2 = Tx("two-a2", Start + 2).Spend(a, 0).Out(home, 9_500_000).Build();
        var b = Tx("two-b1", Start + 3).Spend(out1, 1).Out(Wpkh("two-b2"), 9_800_000).Build();
        var b2 = Tx("two-b2", Start + 4).Spend(b, 0).Out(home, 9_500_000).Build();
        var busy = new[] { Busy("two-a", Start), Busy("two-b", Start) };

        var keys = Run([out1, a, a2, b, b2, .. busy]).Where(finding => finding.Key.StartsWith("round:",
            StringComparison.Ordinal)).Select(finding => finding.Key).ToList();

        Assert.Equal(2, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_refund_too_little_back_unlinked_coins_or_only_passing_actors_is_no_round_trip()
    {
        var pay = Tx("pay", Start).In(Wpkh("a"), 20_000_000).Out(Wpkh("b"), 10_000_000).Build();
        var refund = Tx("refund", Start + 5).Spend(pay, 0).Out(Wpkh("a"), 9_990_000).Build();
        Assert.Empty(Run([pay, refund]));

        Assert.Empty(Run(Round("r", Start, back: 3_000_000)));
        Assert.Empty(Run(Round("r", Start, back: 8_500_000, linked: false)));
        Assert.Empty(Run(Round("r", Start, back: 8_500_000, busy: false)));
        Assert.Empty(Run(Round("r", Start, back: 8_500_000, gap: MonthAndADay)));
    }

    [Fact]
    public void A_deposit_and_a_withdrawal_at_an_exchange_without_a_label_is_no_round_trip()
    {
        // The user pays a deposit address, which is swept to the hot wallet; the hot wallet pays the user back, once
        // with the swept coin and once with another.
        var user = Wpkh("user");
        var hot = Wpkh("hot");
        var customers = Enumerable.Range(0, 12)
            .Select(i => Tx($"customer-{i}", Start + i).In(Wpkh($"customer-{i}"), 5_000_000).Out(hot, 4_000_000)
                .Build())
            .ToList();
        var deposit = Tx("deposit", Start + 20).In(user, 20_000_000).Out(Wpkh("deposit-address"), 10_000_000)
            .Build();
        var sweep = Tx("sweep", Start + 21).Spend(deposit, 0).Out(hot, 9_990_000).Build();
        var withdrawals = Enumerable.Range(0, 12)
            .Select(i => Tx($"withdrawal-{i}", Start + 25).Spend(customers[i], 0)
                .Out(Wpkh($"withdrawer-{i}"), 3_990_000).Build())
            .ToList();
        var sameCoin = Tx("withdraw", Start + 30).Spend(sweep, 0).Out(user, 9_900_000).Build();
        var otherCoin = Tx("withdraw-other", Start + 30).In(hot, 8_000_000).Out(user, 7_900_000).Build();

        Assert.Empty(Run([.. customers, .. withdrawals, deposit, sweep, sameCoin]));
        Assert.Empty(Run([.. customers, .. withdrawals, deposit, sweep, otherCoin]));
    }

    // ---- Likadana belopp i följd -----------------------------------------------------------------------------

    [Fact]
    public void Alike_amounts_in_a_row_to_one_recipient_are_structuring_and_another_payer_of_it_is_named()
    {
        var recipient = Wpkh("deposit-x");
        var txs = Split("p", Start, 9_950_000, count: 5, gap: 100, recipient)
            .Concat(Split("q", Start + 2_000, 9_960_000, count: 4, gap: 50, recipient))
            .ToList();

        var findings = Run(txs, exchanges: [recipient]);

        var p = Assert.Single(findings, finding => finding.Key == "struct:" + Txid("p-0").Value);
        Assert.Equal("Likadana belopp, 5 i följd", p.Title);
        Assert.Contains(CheckTextShort(Wpkh("q")), p.Why, StringComparison.Ordinal);
        Assert.Contains("bland de hämtade tx:erna", p.Why, StringComparison.Ordinal);
        Assert.Equal(5, p.Basis.Count);
        Assert.Single(findings, finding => finding.Key == "struct:" + Txid("q-0").Value);
        AssertJudgement(p);
    }

    [Fact]
    public void Another_payer_of_the_same_amount_to_another_recipient_is_not_named()
    {
        var txs = Split("p", Start, 9_950_000, count: 5, gap: 100, Wpkh("x"))
            .Concat(Split("q", Start + 2_000, 9_960_000, count: 4, gap: 50, Wpkh("y")))
            .ToList();

        var p = Assert.Single(Run(txs, exchanges: [Wpkh("x"), Wpkh("y")]),
            finding => finding.Key == "struct:" + Txid("p-0").Value);

        Assert.DoesNotContain(CheckTextShort(Wpkh("q")), p.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_is_capped_and_a_longer_row_gives_a_new_run()
    {
        var txs = Split("long", Start, 9_871_234, count: StructuringPattern.MaxRun + 5, gap: 1, Wpkh("x"), step: 3);

        var findings = Run(txs, exchanges: [Wpkh("x")]);

        Assert.Equal(2, findings.Count);
        Assert.Equal($"Likadana belopp, {StructuringPattern.MaxRun} i följd", findings[0].Title);
    }

    [Fact]
    public void Round_amounts_alike_amounts_to_many_recipients_and_batch_payouts_are_no_structuring()
    {
        Assert.Empty(Run(Split("round", Start, 1_000_000, count: 8, gap: 10, Wpkh("x"), step: 0)));
        Assert.Empty(Run(Split("quarter", Start, 25_000_000, count: 8, gap: 10, Wpkh("x"), step: 0)));
        var many = Enumerable.Range(0, 6)
            .Select(i => Tx($"many-{i}", Start + i).In(Wpkh("payroll"), 30_000_000)
                .Out(Wpkh($"employee-{i}"), 9_950_000).Build())
            .ToList();
        Assert.Empty(Run(many));
        var batches = Enumerable.Range(0, 5)
            .Select(batch => Enumerable.Range(0, 20).Aggregate(
                    Tx($"batch-{batch}", Start + batch).In(Wpkh("payout-service"), 500_000_000),
                    (tx, i) => tx.Out(Wpkh($"customer-{batch}-{i}"), 9_950_000 + (i * 7)))
                .Build())
            .ToList();
        Assert.Empty(Run(batches));
    }

    [Fact]
    public void Too_few_too_spread_out_unlike_or_dust_amounts_are_no_structuring()
    {
        Assert.Empty(Run(Split("few", Start, 9_950_000, count: 3, gap: 100, Wpkh("x"))));
        Assert.Empty(Run(Split("slow", Start, 9_950_000, count: 5, gap: 600, Wpkh("x"))));
        Assert.Empty(Run(Split("dust", Start, 5_123, count: 6, gap: 10, Wpkh("x"))));
        var unlike = Enumerable.Range(0, 6)
            .Select(i => Tx($"unlike-{i}", Start + i).In(Wpkh("unlike"), 50_000_000)
                .Out(Wpkh("x"), 9_000_123 + (i * 300_000)).Build())
            .ToList();
        Assert.Empty(Run(unlike));
    }

    [Fact]
    public void Payouts_of_an_exchange_or_of_a_mixer_without_a_label_are_never_structuring()
    {
        var payouts = Enumerable.Range(0, 12)
            .Select(i => Tx($"mx-{i}", Start + i).In(Wpkh("mixer-hub"), 30_000_000)
                .Out(Wpkh($"mx-customer-{i % 11}"), 9_950_000 + (i * 100)).Build())
            .ToList();

        Assert.Empty(Run(payouts));
        Assert.Empty(Run(Split("ex", Start, 9_950_000, count: 6, gap: 10, Wpkh("x")), exchanges: [Wpkh("ex")]));
    }

    // ---- Large frauds are no hubs (I-A) ----------------------------------------------------------------------

    [Theory]
    [InlineData(12)]
    [InlineData(30)]
    public void Many_collecting_addresses_that_sweep_to_the_same_wallet_are_one_collection(int collectors)
    {
        var txs = Enumerable.Range(0, collectors)
            .SelectMany(i => Collection($"c{i}", Start + (i * 200), Wpkh("launder")))
            .ToList();

        var finding = Single(Run(txs), "collect:");

        Assert.Equal($"Samma insamling, {collectors} aktörer", finding.Title);
        Assert.Contains("handlare", finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scammer_paid_by_twelve_victims_who_splits_the_money_is_no_hub()
    {
        var victims = Enumerable.Range(0, 12)
            .Select(i => Tx($"victim-{i}", Start + i).In(Wpkh($"victim-{i}"), 30_000_000)
                .Out(Wpkh("scammer"), 20_000_000 + (i * 1_234)).Out(Wpkh($"victim-{i}"), 9_000_000).Build())
            .ToList();
        var split = Split("scammer", Start + 20, 9_950_000, count: 5, gap: 10, Exchange);
        var input = Input([.. victims, .. split], exchanges: [Exchange]) with
        {
            Protected = new HashSet<BitcoinAddress> { Wpkh("victim-0"), Wpkh("victim-1") },
        };

        Assert.Single(Analysis.Run(input), finding => finding.Key == "struct:" + Txid("scammer-0").Value);
    }

    [Fact]
    public void An_exchange_without_a_label_that_sweeps_its_deposit_addresses_on_in_bulk_is_no_collection()
    {
        // Probe X3: twenty clusters with five payers each are swept to a hot wallet, which pays on in large amounts.
        var hot = Wpkh("unlabelled-hot");
        var deposits = Enumerable.Range(0, 20)
            .Select(i => Collection($"d{i}", Start + (i * 50), hot, payers: 5).ToList())
            .ToList();
        var sweeps = deposits.Select(cluster => cluster[^1]).ToList();
        var half = sweeps.Count / 2;
        var onward = new[]
        {
            sweeps.Take(half).Aggregate(Tx("hot-onward-1", Start + 2_000), (tx, sweep) => tx.Spend(sweep, 0))
                .Out(Wpkh("cold-1"), sweeps.Take(half).Sum(sweep => sweep.Outputs[0].Amount.Value) - 5_000).Build(),
            sweeps.Skip(half).Aggregate(Tx("hot-onward-2", Start + 2_010), (tx, sweep) => tx.Spend(sweep, 0))
                .Out(Wpkh("cold-2"), sweeps.Skip(half).Sum(sweep => sweep.Outputs[0].Amount.Value) - 5_000).Build(),
        };

        Assert.Empty(Run([.. deposits.SelectMany(cluster => cluster), .. onward]));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(12)]
    [InlineData(30)]
    public void Collections_whose_wallet_cashes_out_in_bulk_are_silent_without_victims_and_found_with_them(
        int collectors)
    {
        // Probes B and C: the collections are swept to a wallet that forwards everything in one tx to an exchange.
        var wallet = Wpkh("cash-out-wallet");
        var clusters = Enumerable.Range(0, collectors)
            .Select(i => Collection($"b{i}", Start + (i * 200), wallet).ToList())
            .ToList();
        var sweeps = clusters.Select(cluster => cluster[^1]).ToList();
        var cashOut = sweeps
            .Aggregate(Tx("b-cash-out", Start + (collectors * 200) + 100), (tx, sweep) => tx.Spend(sweep, 0))
            .Out(Exchange, sweeps.Sum(sweep => sweep.Outputs[0].Amount.Value) - 10_000).Build();
        var txs = clusters.SelectMany(cluster => cluster).Append(cashOut).ToList();
        var victims = clusters.SelectMany(cluster => cluster.Take(cluster.Count - 1))
            .SelectMany(paid => paid.Inputs.Select(spent => spent.Address!))
            .ToHashSet();

        var silent = Analysis.Run(Input(txs, exchanges: [Exchange]));
        var withVictims = Analysis.Run(Input(txs, exchanges: [Exchange]) with { Protected = victims });

        Assert.DoesNotContain(silent, finding => finding.Key.StartsWith("collect:", StringComparison.Ordinal));
        var finding = Single(withVictims, "collect:");
        Assert.Equal($"Samma insamling, {collectors} aktörer", finding.Title);
        Assert.Contains("börs utan etikett", finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_possible_exchange_is_named_only_when_the_wallet_cashes_out_in_bulk()
    {
        var txs = Collection("s1", Start, Wpkh("launder")).Concat(Collection("s2", Start + 500, Wpkh("launder")));

        Assert.DoesNotContain("börs utan etikett", Single(Run([.. txs]), "collect:").Why, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unlabelled_hot_wallet_whose_customers_spend_their_withdrawals_onward_is_no_collection()
    {
        // Probe X3'': twenty deposit clusters to a hot wallet that pays twelve withdrawals, each spent onward.
        var hot = Wpkh("hot-x3b");
        var deposits = Enumerable.Range(0, 20)
            .SelectMany(i => Collection($"x{i}", Start + (i * 50), hot, payers: 5))
            .ToList();
        var swept = Enumerable.Range(0, 20).Select(i => deposits.Single(tx => tx.Txid == Txid($"x{i}-sweep"))).ToList();
        var payouts = new List<BitcoinTransaction>();
        for (var i = 0; i < 12; i++)
        {
            var withdrawal = Tx($"x3b-payout-{i}", Start + 2_000 + i).Spend(swept[i], 0)
                .Out(Wpkh($"x3b-customer-{i}"), 5_000_000).Out(hot, swept[i].Outputs[0].Amount.Value - 5_010_000)
                .Build();
            var onward = Tx($"x3b-onward-{i}", Start + 2_100 + i).Spend(withdrawal, 0)
                .Out(Wpkh($"x3b-shop-{i}"), 4_990_000).Build();
            payouts.AddRange([withdrawal, onward]);
        }

        Assert.DoesNotContain(Run([.. deposits, .. payouts]),
            finding => finding.Key.StartsWith("collect:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_scammer_who_pays_out_through_twelve_mules_is_no_hub_and_the_ways_are_found()
    {
        // Probe N4b: twelve victims pay a scammer, who passes each payment on to its own mule, who pays it in at the
        // same deposit address.
        var deposit = Wpkh("exchange-deposit-a");
        var txs = new List<BitcoinTransaction>();
        for (var i = 0; i < 12; i++)
        {
            var paid = Tx($"n4-paid-{i}", Start + (i * 20)).In(Wpkh($"n4-victim-{i}"), 20_000_000)
                .Out(Wpkh("n4-scammer"), 10_000_000 + (i * 1_111)).Build();
            var mule = Tx($"n4-mule-{i}", Start + (i * 20) + 3).Spend(paid, 0)
                .Out(Wpkh($"n4-mule-{i}"), 9_900_000).Build();
            var cashed = Tx($"n4-cashed-{i}", Start + (i * 20) + 6).Spend(mule, 0).Out(deposit, 9_800_000).Build();
            txs.AddRange([paid, mule, cashed]);
        }

        var input = Input(txs) with { Services = [new KnownService([Exchange, deposit], true, "Börsen")] };

        var finding = Single(Analysis.Run(input), "route:");

        Assert.Equal($"Samma väg, {RoutePattern.MostWays} delar", finding.Title);
    }

    // ---- The same deposit address, never just the same exchange (I-B) --------------------------------------

    [Fact]
    public void Two_ways_into_one_exchange_at_different_deposit_addresses_are_not_the_same_way()
    {
        var depositA = Wpkh("exchange-deposit-a");
        var depositB = Wpkh("exchange-deposit-b");
        var txs = Way("v1", Start, to: depositA).Concat(Way("v2", Start + 300, to: depositB)).ToList();
        var input = Input(txs) with { Services = [new KnownService([Exchange, depositA, depositB], true, "Börsen")] };

        Assert.Empty(Analysis.Run(input));
    }

    [Fact]
    public void Two_ways_into_one_exchange_at_the_same_deposit_address_are_the_same_way()
    {
        var depositA = Wpkh("exchange-deposit-a");
        var depositB = Wpkh("exchange-deposit-b");
        var txs = Way("v1", Start, to: depositA).Concat(Way("v2", Start + 300, to: depositA)).ToList();
        var input = Input(txs) with { Services = [new KnownService([Exchange, depositA, depositB], true, "Börsen")] };

        var finding = Single(Analysis.Run(input), "route:");

        Assert.Equal($"route:{depositA.Value}>3", finding.Key);
    }

    // ---- Structuring must add up (M-d) -----------------------------------------------------------------------

    [Fact]
    public void A_daily_payment_of_the_same_odd_amount_to_someone_is_no_structuring()
    {
        Assert.Empty(Run(Split("subscriber", Start, 1_234_567, count: 7, gap: 144, Wpkh("someone"), step: 0)));
    }

    [Fact]
    public void Alike_payments_that_the_recipient_spends_together_add_up_and_are_structuring()
    {
        var split = Split("p", Start, 9_950_000, count: 5, gap: 100, Wpkh("collector"));
        var together = split.Aggregate(Tx("together", Start + 600), (tx, paid) => tx.Spend(paid, 0))
            .Out(Wpkh("onward"), 49_000_000).Build();

        Assert.Single(Run([.. split, together]), finding => finding.Key == "struct:" + Txid("p-0").Value);
    }

    // ---- A model's weights (M-e) -----------------------------------------------------------------------------

    [Fact]
    public void A_models_weights_are_sealed_with_every_finding()
    {
        using var harness = new Harness(services => services.AddSingleton<ICheckAnalysis, WeightedAnalysis>());
        var tx = Tx("pay", Start).In(Wpkh("a"), 1_000_000).Out(Wpkh("b"), 600_000).Build();

        var run = harness.Run([tx]);

        var check = Assert.Single(run.Checks, check => check.Analysis.Name == WeightedAnalysis.Name);
        Assert.EndsWith($"Modellens vikter: SHA-256 {WeightedAnalysis.Weights}.", check.Finding.Why,
            StringComparison.Ordinal);
        Assert.True(harness.Verify().IsIntact);
    }

    // ---- Everyday shapes ---------------------------------------------------------------------------------------

    [Fact]
    public void A_coinjoin_and_its_outputs_spent_on_give_no_pattern()
    {
        var coinJoin = CoinJoin("cj", Start, participants: 6);
        var spent = Enumerable.Range(0, 6)
            .Select(i => Tx($"cj-on-{i}", Start + 10 + i).Spend(coinJoin, i).Out(Wpkh($"cj-next-{i}"), 4_990_000)
                .Build())
            .ToList();

        Assert.Empty(Run([coinJoin, .. spent]));
    }

    [Fact]
    public void A_hot_wallet_that_sweeps_to_cold_storage_again_and_again_gives_no_pattern()
    {
        var txs = HotWallet("h", Start, Wpkh("cold")).Concat(HotWallet("h", Start + 500, Wpkh("cold"), round: 1))
            .ToList();

        Assert.Empty(Run(txs));
    }

    // ---- Through the service ----------------------------------------------------------------------------------

    [Fact]
    public void A_pattern_is_sealed_as_a_check_of_pattern_v1_and_the_case_still_verifies()
    {
        using var harness = new Harness();
        var txs = Collection("s1", Start, Wpkh("launder")).Concat(Collection("s2", Start + 500, Wpkh("launder")))
            .ToList();

        var run = harness.Run(txs);

        var check = Assert.Single(run.Checks, check => check.Analysis.Name == PatternCheck.Name);
        Assert.Equal("pattern v1", check.Analysis.ToString());
        Assert.Equal("Mönster", check.AnalysisName);
        Assert.Equal(CheckKind.Check, check.Finding.Kind);
        Assert.Equal(AreaSort.Obfuscation, check.Finding.Area);
        Assert.Empty(run.Problems);
        Assert.True(harness.Verify().IsIntact);
    }

    // ---- False alarms and speed --------------------------------------------------------------------------------

    [Fact]
    public void Random_graphs_whose_coins_are_linked_hardly_ever_give_a_pattern()
    {
        var alarms = 0;
        var flows = 0;
        var linked = 0;
        for (var seed = 1; seed <= 20; seed++)
        {
            var (txs, labelled) = RandomGraph(seed, addresses: 300, transactions: 1_500);
            var input = Input(txs, mixers: [labelled[0]], exchanges: [labelled[1], labelled[2]]);
            var shape = GraphShape.Of(input);
            flows += shape.Flows.Count;
            linked += shape.Flows.Count(flow => flow.Spent.Any(coin => shape.PayerOf(coin) is not null));
            alarms += Analysis.Run(input).Count;
        }

        // The graphs are not empty of what the rules follow: two lines in five spend a coin another line paid.
        Assert.True(linked * 5 > flows * 2, $"Bara {linked} av {flows} linjer följer ett mynt.");

        // Measured 2026-11-09: no alarm in 300 such graphs of 1 500 transactions; the test runs 20.
        Assert.True(alarms == 0, $"{alarms} mönster i 20 slumpade grafer med sammanlagt {flows} linjer.");
    }

    [Fact]
    public void A_graph_of_thousands_of_actors_is_read_in_time()
    {
        var (txs, labelled) = RandomGraph(99, addresses: 5_000, transactions: 20_000);
        var input = Input(txs, mixers: [labelled[0]], exchanges: [labelled[1]]);

        var watch = Stopwatch.StartNew();
        Analysis.Run(input);
        watch.Stop();

        Assert.True(GraphShape.Of(input).Actors.Count >= 4_000);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Det tog {watch.Elapsed.TotalSeconds:0.0} s.");
    }

    // ---- Helpers ----------------------------------------------------------------------------------------------

    private static ICheckAnalysis Analysis { get; } = new PatternCheck();

    private static IReadOnlyList<CheckFinding> Run(
        IReadOnlyList<BitcoinTransaction> txs,
        IReadOnlyList<BitcoinAddress>? mixers = null,
        IReadOnlyList<BitcoinAddress>? exchanges = null) =>
        Analysis.Run(Input(txs, mixers, exchanges));

    private static CheckInput Input(
        IReadOnlyList<BitcoinTransaction> txs,
        IReadOnlyList<BitcoinAddress>? mixers = null,
        IReadOnlyList<BitcoinAddress>? exchanges = null) =>
        new(txs, [.. (mixers ?? []).Select(address => new KnownMixer([address]))],
            new Dictionary<Txid, TransactionConclusions>())
        {
            Services = [.. (exchanges ?? []).Select(address => new KnownService([address], true))],
        };

    private static CheckFinding Single(IReadOnlyList<CheckFinding> findings, string keyStart) =>
        Assert.Single(findings, finding => finding.Key.StartsWith(keyStart, StringComparison.Ordinal));

    private static void AssertJudgement(CheckFinding finding)
    {
        Assert.Equal(CheckKind.Check, finding.Kind);
        Assert.Contains("pattern v1", finding.Why, StringComparison.Ordinal);
        Assert.Contains("inte ett faktum", finding.Why, StringComparison.Ordinal);
        Assert.NotEmpty(finding.Basis);
        Assert.All(finding.Basis, basis => Assert.NotNull(basis.Block));
    }

    private static string CheckTextShort(BitcoinAddress address) => address.Value[..8] + "…" + address.Value[^4..];

    /// <summary>Six victims pay a collecting address, which sweeps their coins on in one transaction.</summary>
    private static IEnumerable<BitcoinTransaction> Collection(
        string part, long height, BitcoinAddress to, BitcoinAddress? collector = null, int payers = 6)
    {
        var payments = Enumerable.Range(0, payers)
            .Select(i => Tx($"{part}-pay-{i}", height + (i * 10)).In(Wpkh($"{part}-victim-{i}"), 3_000_000)
                .Out(collector ?? Wpkh(part), 2_000_000 + (i * 1_000)).Out(Wpkh($"{part}-victim-{i}"), 990_000)
                .Build())
            .ToList();
        var collected = payments.Sum(paid => paid.Outputs[0].Amount.Value);
        var sweep = payments.Aggregate(Tx($"{part}-sweep", height + 100), (tx, paid) => tx.Spend(paid, 0))
            .Out(to, collected * 99 / 100).Build();
        return [.. payments, sweep];
    }

    /// <summary>
    /// An exchange's hot wallet: six customers pay their own deposit addresses, which forward everything to the hot
    /// wallet, and the hot wallet sweeps it on.
    /// </summary>
    private static IEnumerable<BitcoinTransaction> HotWallet(string name, long height, BitcoinAddress to, int round = 0)
    {
        var forwards = new List<BitcoinTransaction>();
        for (var i = 0; i < 6; i++)
        {
            var paid = Tx($"{name}-{round}-customer-{i}", height + i).In(Wpkh($"{name}-{round}-customer-{i}"), 3_000_000)
                .Out(Wpkh($"{name}-{round}-deposit-{i}"), 2_000_000 + (i * 1_000)).Build();
            forwards.Add(paid);
            forwards.Add(Tx($"{name}-{round}-forward-{i}", height + 10 + i).Spend(paid, 0)
                .Out(Wpkh(name), 1_999_000 + (i * 1_000)).Build());
        }

        var sweep = forwards.Where((_, index) => index % 2 == 1)
            .Aggregate(Tx($"{name}-{round}-sweep", height + 100), (tx, forward) => tx.Spend(forward, 0))
            .Out(to, 11_900_000).Build();
        return [.. forwards, sweep];
    }

    /// <summary>A victim pays a mule, who pays a second mule, who pays the exchange, each later.</summary>
    private static IEnumerable<BitcoinTransaction> Way(
        string victim, long height, bool linked = true, long gap = 5, BitcoinAddress? to = null)
    {
        var paid = Tx($"{victim}-paid", height).In(Wpkh(victim), 20_000_000).Out(Wpkh($"{victim}-mule"), 10_000_000)
            .Build();
        var hop = Tx($"{victim}-hop", height + gap).Spend(paid, 0).Out(Wpkh($"{victim}-mule2"), 9_900_000).Build();
        var cashed = linked
            ? Tx($"{victim}-cashed", height + (2 * gap)).Spend(hop, 0).Out(to ?? Exchange, 9_800_000).Build()
            : Tx($"{victim}-cashed", height + (2 * gap)).In(Wpkh($"{victim}-mule2"), 10_000_000)
                .Out(to ?? Exchange, 9_800_000).Build();
        return [paid, hop, cashed];
    }

    /// <summary>
    /// An actor sends 10 BTC through two others and gets <paramref name="back"/> satoshi back; the first other also
    /// does business of its own when <paramref name="busy"/>.
    /// </summary>
    private static List<BitcoinTransaction> Round(
        string label, long height, long back, bool linked = true, bool busy = true, long gap = 10)
    {
        var home = Wpkh($"{label}-home");
        var first = Tx($"{label}-0", height).In(home, 20_000_000).Out(Wpkh($"{label}-hop-1"), 10_000_000).Build();
        var second = Tx($"{label}-1", height + gap).Spend(first, 0).Out(Wpkh($"{label}-hop-2"), 9_500_000).Build();
        var third = linked
            ? Tx($"{label}-2", height + (2 * gap)).Spend(second, 0).Out(home, back).Build()
            : Tx($"{label}-2", height + (2 * gap)).In(Wpkh($"{label}-hop-2"), 10_000_000).Out(home, back).Build();
        return busy ? [first, second, third, Busy($"{label}-hop-1", height)] : [first, second, third];
    }

    /// <summary>A payment an actor gets from someone else, so that it does more than pass money on.</summary>
    private static BitcoinTransaction Busy(string label, long height) =>
        Tx($"{label}-busy", height - 50).In(Wpkh($"{label}-friend"), 3_000_000).Out(Wpkh(label), 1_234_567).Build();

    /// <summary>
    /// An actor pays alike amounts to one recipient, <paramref name="gap"/> blocks apart, each <paramref name="step"/>
    /// satoshi more.
    /// </summary>
    private static List<BitcoinTransaction> Split(
        string payer, long height, long amount, int count, int gap, BitcoinAddress to, long step = 1_000) =>
        [.. Enumerable.Range(0, count).Select(i => Tx($"{payer}-{i}", height + (i * gap))
            .In(Wpkh(payer), amount * 3).Out(to, amount + (i * step)).Build())];

    /// <summary>A CoinJoin: each participant puts in 5 BTC and gets 4,99 BTC out at a new address.</summary>
    private static BitcoinTransaction CoinJoin(string label, long height, int participants) =>
        Enumerable.Range(0, participants)
            .Aggregate(Tx(label, height), (tx, i) => tx.In(Wpkh($"{label}-in-{i}"), 500_000_000))
            .Apply(tx => Enumerable.Range(0, participants)
                .Aggregate(tx, (builder, i) => builder.Out(Wpkh($"{label}-out-{i}"), 499_000_000)))
            .Build();

    /// <summary>
    /// An ordinary, random graph whose coins are linked: each transaction spends a coin its payer got earlier in the
    /// graph (or one from outside the first time), pays another address a random share of it and takes the rest back
    /// as change; three addresses are labelled.
    /// </summary>
    private static (List<BitcoinTransaction> Txs, BitcoinAddress[] Labelled) RandomGraph(
        int seed, int addresses, int transactions)
    {
        var random = new Random(seed);
        var height = Start;
        var txs = new List<BitcoinTransaction>(transactions);
        var coins = Enumerable.Range(0, addresses).Select(_ => new List<(BitcoinTransaction Tx, int Index)>())
            .ToArray();
        for (var i = 0; i < transactions; i++)
        {
            height += random.Next(0, 4);
            var from = random.Next(addresses);
            var to = (from + 1 + random.Next(addresses - 1)) % addresses;
            var builder = Tx($"g{seed}-{i}", height);
            long available;
            if (coins[from].Count > 0)
            {
                var pick = random.Next(coins[from].Count);
                var (previous, index) = coins[from][pick];
                coins[from].RemoveAt(pick);
                builder = builder.Spend(previous, index);
                available = previous.Outputs[index].Amount.Value;
            }
            else
            {
                available = (long)Math.Pow(10, 5 + (random.NextDouble() * 4));
                builder = builder.In(Wpkh($"g{seed}-a{from}"), available);
            }

            var paid = Math.Max(1_000, (long)(available * (0.05 + (random.NextDouble() * 0.9))));
            var change = available - paid - 500;
            builder = builder.Out(Wpkh($"g{seed}-a{to}"), paid);
            if (change > 1_000)
            {
                builder = builder.Out(Wpkh($"g{seed}-a{from}"), change);
            }

            var tx = builder.Build();
            txs.Add(tx);
            coins[to].Add((tx, 0));
            if (change > 1_000)
            {
                coins[from].Add((tx, 1));
            }
        }

        return (txs, [Wpkh($"g{seed}-a0"), Wpkh($"g{seed}-a1"), Wpkh($"g{seed}-a2")]);
    }

    /// <summary>A stand-in for a local model: it has weights, and says nothing of them itself.</summary>
    private sealed class WeightedAnalysis : IGraphShapeAnalysis
    {
        public const string Name = "weighted";

        public const string Weights = "3b7e1c5d9f0a2b4c6d8e0f1a3b5c7d9e1f2a4b6c8d0e2f4a6b8c0d2e4f6a8b0c";

        public HeuristicId Id { get; } = new(Name, 1);

        public string DisplayName => "Vägd";

        public string? WeightsSha256 => Weights;

        public IReadOnlyList<CheckFinding> Run(GraphShape shape) =>
        [
            new CheckFinding("all", CheckKind.Check, AreaSort.Obfuscation, "Allt", "", "En bedömning.",
                [], [], [.. shape.Flows.Select(flow => new CheckBasis(flow.Txid, flow.Block))], []),
        ];
    }

    /// <summary>A stand-in for a later analysis through the door: it counts the shape's lines.</summary>
    private sealed class CountingAnalysis : IGraphShapeAnalysis
    {
        public const string Name = "line-count";

        public HeuristicId Id { get; } = new(Name, 1);

        public string DisplayName => "Linjer";

        public IReadOnlyList<CheckFinding> Run(GraphShape shape) =>
        [
            new CheckFinding("lines", CheckKind.Check, AreaSort.Obfuscation, $"{shape.Flows.Count} linjer", "", "",
                [], [], [.. shape.Flows.Select(flow => new CheckBasis(flow.Txid, flow.Block))], []),
        ];
    }
}

/// <summary>A small helper to keep a builder chain readable.</summary>
internal static class BuilderExtensions
{
    /// <summary>Applies a step to a builder and gives what it returns.</summary>
    public static TxBuilder Apply(this TxBuilder builder, Func<TxBuilder, TxBuilder> step) => step(builder);
}
