using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>
/// The facit for the analyses of the fourth round, each over the real clustering: what each recognises and what it
/// leaves alone. Every finding is a check that says its analysis and that it is a judgement.
/// </summary>
public sealed class RoundFourChecksTests : IDisposable
{
    /// <summary>The address a look-alike is made to resemble (P2WPKH, program from a label).</summary>
    private static readonly BitcoinAddress Genuine = BitcoinAddress.FromWitness(
        0, Convert.FromHexString("5d77641009361c41589b5a5d6c6ff4663e31167a"), BitcoinNetwork.Mainnet);

    /// <summary>
    /// An address ground to share the genuine's first and last four characters after <c>bc1q</c>
    /// (<c>bc1qt4mk…4utw</c>), as an address poisoner makes one.
    /// </summary>
    private static readonly BitcoinAddress LookAlike = BitcoinAddress.FromWitness(
        0, Convert.FromHexString("5d7764a00c0deb5a3ddfddbb37622fec9a773b4e"), BitcoinNetwork.Mainnet);

    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Many_customers_deposits_swept_again_and_again_into_one_address_are_a_likely_service()
    {
        var (deposits, sweeps) = Exchange("ex", customers: 12, sweeps: 2);

        var check = Single(_harness.Run([.. deposits, .. sweeps]), ServiceBehaviourCheck.Name, "sweep:");

        Assert.Equal(CheckKind.Check, check.Finding.Kind);
        Assert.Equal(AreaSort.Service, check.Finding.Area);
        Assert.Equal("Trolig tjänst, 12 insättningar", check.Finding.Title);
        Assert.Equal(Pkh("ex-hot-wallet"), check.Finding.Addresses[0]);
        Assert.Equal(13, check.Finding.Addresses.Count);
        Assert.Contains(check.Finding.Basis, basis => basis.Txid == sweeps[1].Txid);
        Assert.Contains(check.Finding.Basis, basis => basis.Txid == deposits[0].Txid);
        Assert.Contains("kan gälla dem", check.Finding.Why, StringComparison.Ordinal);
        AssertJudgement(check, "service-behaviour v1");
    }

    [Fact]
    public void A_scammer_who_sweeps_three_victims_payments_together_is_no_service()
    {
        var deposits = Patterns.Deposits().Take(3).ToList();
        var sweep = deposits.Aggregate(Tx("scam-sweep", 900_010), (tx, deposit) => tx.Spend(deposit, 0))
            .Out(Pkh("scammer"), 14_990_000).Build();

        Assert.DoesNotContain(
            _harness.Run([.. deposits, sweep]).Checks, check => check.Analysis.Name == ServiceBehaviourCheck.Name);
    }

    [Fact]
    public void Deposits_that_the_start_points_paid_are_never_a_services_customers()
    {
        var (deposits, sweeps) = Exchange("victims", customers: 12, sweeps: 2);
        var victims = Enumerable.Range(0, 12).Select(i => Wpkh($"victims-customer-{i}")).ToArray();

        Assert.DoesNotContain(
            _harness.RunWithStarts([.. deposits, .. sweeps], victims).Checks,
            check => check.Analysis.Name == ServiceBehaviourCheck.Name && check.Finding.Key.StartsWith("sweep:"));
    }

    [Fact]
    public void A_wallet_that_consolidates_coins_from_outside_the_graph_is_no_service()
    {
        Assert.DoesNotContain(
            _harness.Run([Patterns.Consolidation()]).Checks,
            check => check.Analysis.Name == ServiceBehaviourCheck.Name);
    }

    [Fact]
    public void A_payout_to_many_from_one_address_is_a_likely_batch_and_equal_amounts_are_not()
    {
        var run = _harness.Run([Patterns.BatchPayout(), Patterns.BatchOfEqualAmounts()]);

        var batch = Single(run, ServiceBehaviourCheck.Name, "batch:");
        Assert.Equal("batch:" + Txid("batch").Value, batch.Finding.Key);
        Assert.Equal("Trolig utbetalning i batch", batch.Finding.Title);
        Assert.Equal([Pkh("hot-wallet")], batch.Finding.Addresses);
    }

    [Fact]
    public void Two_outputs_of_a_coinjoin_spent_together_are_mixed_coins_merged()
    {
        var whirlpool = Patterns.Whirlpool();
        var remix = Tx("remix", 900_010).Spend(whirlpool, 0).Spend(whirlpool, 1).Out(Wpkh("remix-out"), 1_990_000)
            .Build();

        var check = Single(_harness.Run([whirlpool, remix]), CoinJoinMergeCheck.Name);

        Assert.Equal(remix.Txid.Value, check.Finding.Key);
        Assert.Equal("Mixade mynt slås ihop", check.Finding.Title);
        Assert.Equal(AreaSort.Obfuscation, check.Finding.Area);
        Assert.Contains(check.Finding.Basis, basis => basis.Txid == whirlpool.Txid);
        AssertJudgement(check, "coinjoin-merge v1");
    }

    [Fact]
    public void A_mixed_output_spent_with_the_rounds_change_is_merged_with_the_change()
    {
        var joinMarket = Patterns.JoinMarket();
        var toxic = Tx("toxic", 900_010).Spend(joinMarket, 0).Spend(joinMarket, 4).Out(Wpkh("toxic-out"), 42_000_000)
            .Build();

        var check = Single(_harness.Run([joinMarket, toxic]), CoinJoinMergeCheck.Name);

        Assert.Equal("Mixade mynt slås ihop med växeln", check.Finding.Title);
        Assert.Contains("toxic change", check.Finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void One_mixed_output_spent_alone_or_with_an_outside_coin_is_left_alone()
    {
        var whirlpool = Patterns.Whirlpool();
        var alone = Tx("alone", 900_010).Spend(whirlpool, 0).In(Wpkh("outside"), 500_000).Out(Wpkh("shop"), 1_400_000)
            .Build();

        Assert.DoesNotContain(
            _harness.Run([whirlpool, alone]).Checks, check => check.Analysis.Name == CoinJoinMergeCheck.Name);
    }

    [Fact]
    public void A_split_into_equal_parts_and_the_parts_joined_again_are_seen_with_the_hops_between()
    {
        var split = Split();
        var hop = Tx("pass", 900_201).Spend(split, 2).Out(Wpkh("pass-on"), 1_005_000).Build();
        var join = Tx("join", 900_210).Spend(split, 0).Spend(split, 1).Spend(hop, 0).Out(Pkh("collector"), 3_000_000)
            .Build();

        var run = _harness.Run([split, hop, join]);

        var parts = Single(run, SplitMergeCheck.Name, "split:");
        Assert.Equal("Uppdelning i 6 delar", parts.Finding.Title);
        Assert.Equal(6, parts.Finding.Addresses.Count);
        AssertJudgement(parts, "split-merge v2");
        var joined = Single(run, SplitMergeCheck.Name, "join:");
        Assert.Equal("Delarna samlas igen, 3 av 6", joined.Finding.Title);
        Assert.Contains(joined.Finding.Basis, basis => basis.Txid == hop.Txid);
        Assert.Contains(joined.Finding.Basis, basis => basis.Txid == split.Txid);
    }

    [Fact]
    public void A_payout_of_different_amounts_is_no_split()
    {
        Assert.DoesNotContain(
            _harness.Run([Patterns.BatchPayout()]).Checks, check => check.Analysis.Name == SplitMergeCheck.Name);
    }

    [Fact]
    public void Hops_that_only_pass_the_money_on_are_many_hops_with_the_delay_between_them()
    {
        var row = Hops(4, 432);

        var check = Single(_harness.Run(row), PassThroughCheck.Name);

        Assert.Equal(row[0].Txid.Value, check.Finding.Key);
        Assert.Equal("Många hopp, 4 i rad, med fördröjning", check.Finding.Title);
        Assert.Contains("3 dygn", check.Finding.Why, StringComparison.Ordinal);
        Assert.Equal(4, check.Finding.Basis.Count);
        Assert.Equal(5, check.Finding.Addresses.Count);
        AssertJudgement(check, "pass-through v2");
    }

    [Fact]
    public void Two_hops_or_a_hop_that_loses_much_are_not_many_hops()
    {
        var two = Hops(2, 1);
        var first = Tx("lossy-0", 900_300).In(Wpkh("lossy-0"), 10_000_000).Out(Wpkh("lossy-1"), 9_990_000).Build();
        var lossy = Tx("lossy-1", 900_301).Spend(first, 0).Out(Wpkh("lossy-2"), 8_000_000).Build();
        var last = Tx("lossy-2", 900_302).Spend(lossy, 0).Out(Wpkh("lossy-3"), 7_990_000).Build();

        Assert.DoesNotContain(
            _harness.Run([.. two, first, lossy, last]).Checks, check => check.Analysis.Name == PassThroughCheck.Name);
    }

    [Fact]
    public void Dust_to_many_addresses_is_dusting_and_a_few_dust_outputs_are_not()
    {
        var dusting = Dusting("dusting", 12, 600);
        var few = Dusting("few", 9, 600);

        var run = _harness.Run([dusting, few]);

        var check = Single(run, DustCheck.Name, "dust:");
        Assert.Equal("dust:" + dusting.Txid.Value, check.Finding.Key);
        Assert.Equal("Damm till 12 adresser", check.Finding.Title);
        AssertJudgement(check, "dust v1");
    }

    [Fact]
    public void Outputs_of_330_or_546_satoshi_are_said_to_be_possibly_inscriptions()
    {
        var postage = Dusting("postage", 12, 546);

        var check = Single(_harness.Run([postage]), DustCheck.Name, "dust:");

        Assert.Equal("Damm eller inskriptioner till 12 adresser", check.Finding.Title);
        Assert.Contains("ordinals", check.Finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tiny_payment_from_a_look_alike_address_is_likely_poisoning()
    {
        var payment = Tx("paid-genuine", 900_400).In(Wpkh("victim"), 10_000_000).Out(Genuine, 5_000_000)
            .Out(Wpkh("victim-change"), 4_990_000).Build();
        var poison = Tx("poison", 900_401).In(LookAlike, 2_000).Out(Wpkh("victim"), 600).Build();

        var check = Single(_harness.Run([payment, poison]), DustCheck.Name, "poison:");

        Assert.Equal("poison:" + LookAlike.Value, check.Finding.Key);
        Assert.Equal([LookAlike, Genuine], check.Finding.Addresses);
        Assert.Equal("Trolig adressförgiftning", check.Finding.Title);
        Assert.Equal(DustCheck.Look(Genuine), DustCheck.Look(LookAlike));
    }

    [Fact]
    public void A_large_payment_from_a_look_alike_or_a_tiny_one_from_another_address_is_no_poisoning()
    {
        var payment = Tx("paid-genuine", 900_400).In(Wpkh("victim"), 10_000_000).Out(Genuine, 5_000_000)
            .Out(Wpkh("victim-change"), 4_990_000).Build();
        var large = Tx("large", 900_401).In(LookAlike, 3_000_000).Out(Wpkh("victim"), 2_990_000).Build();
        var tiny = Tx("tiny", 900_402).In(Wpkh("someone"), 2_000).Out(Wpkh("victim"), 600).Build();

        Assert.DoesNotContain(
            _harness.Run([payment, large, tiny]).Checks,
            check => check.Analysis.Name == DustCheck.Name && check.Finding.Key.StartsWith("poison:"));
    }

    [Fact]
    public void Merged_inputs_with_an_input_that_was_not_needed_are_a_possible_payjoin()
    {
        var payJoin = Tx("payjoin", 900_500).In(Wpkh("pj-sender"), 3_000_000).In(Tr("pj-receiver"), 1_000_000)
            .Out(Wpkh("pj-change"), 1_490_000).Out(Tr("pj-paid"), 2_500_000).Build();

        var check = Single(_harness.Run([payJoin]), PayJoinCheck.Name);

        Assert.Equal(AreaSort.Wallet, check.Finding.Area);
        Assert.Equal("Möjlig PayJoin", check.Finding.Title);
        Assert.Equal("Mottagarens input, inget slås ihop automatiskt", check.Finding.Summary);
        AssertJudgement(check, "payjoin-hint v1");
    }

    [Fact]
    public void An_ordinary_payment_that_needs_every_input_is_no_payjoin()
    {
        Assert.DoesNotContain(
            _harness.Run([Patterns.PaymentWithChange()]).Checks, check => check.Analysis.Name == PayJoinCheck.Name);
    }

    [Fact]
    public void An_owner_who_spends_only_by_day_has_a_time_pattern_with_a_likely_time_zone()
    {
        var spends = Enumerable.Range(0, 24)
            .Select(day => Tx($"day-{day}", AtHour(day, 8 + (day % 14)))
                .In(Wpkh("worker"), 1_000_000).Out(Wpkh($"worker-paid-{day}"), 990_000).Build())
            .ToList();

        var check = Single(_harness.Run(spends), ActiveHoursCheck.Name);

        Assert.Equal(AreaSort.Time, check.Finding.Area);
        Assert.Equal("Aktiv kl. 09–23 svensk tid", check.Finding.Title);
        Assert.Contains("UTC+1", check.Finding.Summary, StringComparison.Ordinal);
        Assert.Equal([Wpkh("worker")], check.Finding.Addresses);
        Assert.Equal("hours:" + Wpkh("worker").Value, check.Finding.Key);
        AssertJudgement(check, "active-hours v2");
    }

    [Fact]
    public void Spends_at_random_hours_give_no_time_pattern()
    {
        var random = new Random(7);
        var spends = Enumerable.Range(0, 30)
            .Select(day => Tx($"random-{day}", AtHour(day, random.Next(24)))
                .In(Wpkh("random"), 1_000_000).Out(Wpkh($"random-paid-{day}"), 990_000).Build())
            .ToList();

        Assert.DoesNotContain(_harness.Run(spends).Checks, check => check.Analysis.Name == ActiveHoursCheck.Name);
    }

    [Fact]
    public void Random_hours_hardly_ever_give_a_time_pattern()
    {
        var analysis = new ActiveHoursCheck();
        var random = new Random(11);
        var patterns = 0;
        for (var owner = 0; owner < 100; owner++)
        {
            var spends = Enumerable.Range(0, 25)
                .Select(day => Tx($"o{owner}-{day}", AtHour(day, random.Next(24)))
                    .In(Wpkh($"owner-{owner}"), 1_000_000).Out(Wpkh($"o{owner}-paid-{day}"), 990_000).Build())
                .ToList();
            patterns += analysis.Run(new CheckInput(spends, [], new Dictionary<Txid, TransactionConclusions>()))
                .Count;
        }

        Assert.True(patterns <= 3, $"{patterns} av 100 slumpade ägare fick ett tidsmönster.");
    }

    [Fact]
    public void Too_few_transactions_or_every_hour_busy_give_no_time_pattern()
    {
        var few = Enumerable.Range(0, ActiveHoursCheck.MinimumTransactions - 1)
            .Select(day => Tx($"few-{day}", AtHour(day, 9)).In(Wpkh("rare"), 1_000_000)
                .Out(Wpkh($"rare-paid-{day}"), 990_000).Build());
        var always = Enumerable.Range(0, 24)
            .Select(hour => Tx($"always-{hour}", AtHour(1, hour)).In(Wpkh("bot"), 1_000_000)
                .Out(Wpkh($"bot-paid-{hour}"), 990_000).Build());

        Assert.DoesNotContain(
            _harness.Run([.. few, .. always]).Checks, check => check.Analysis.Name == ActiveHoursCheck.Name);
    }

    [Fact]
    public void A_peel_chain_whose_last_change_goes_to_an_exchange_says_so()
    {
        var chain = Patterns.PeelChain(3);
        var exchange = chain[^1].Outputs[1].Address!;

        var run = _harness.RunWithExchange(chain, "Börsen X", exchange);

        var check = Single(run, PeelChainCheck.Name);
        Assert.Equal("Peel chain till börs, 3 hopp", check.Finding.Title);
        Assert.Contains("Börsen X", check.Finding.Why, StringComparison.Ordinal);
        Assert.Equal(2, check.Analysis.Version);
    }

    [Fact]
    public void An_approved_candidate_whose_transaction_leaves_the_graph_stays_shown()
    {
        var check = Assert.Single(
            _harness.Run([Mixing.Deposit(), Mixing.Best(), Mixing.Second()], Mixing.Mixer).Checks,
            c => c.Analysis.Name == MixerOutputsCheck.Name);
        var best = check.Finding.Candidates[0];
        _harness.Service.Decide(
            _harness.Case, new DecisionRequest(check.Analysis, check.Finding.Key, best.Key, CheckVerdict.Approved));

        var after = Assert.Single(
            _harness.Run([Mixing.Deposit(), Mixing.Second()], Mixing.Mixer).Checks,
            c => c.Analysis.Name == MixerOutputsCheck.Name);

        Assert.Contains(after.Finding.Candidates, candidate => candidate.Key == best.Key);
        Assert.Contains(best.Address, after.Finding.Addresses);
        Assert.Equal(CheckVerdict.Approved, after.DecisionFor(best.Key)!.Verdict);
    }

    /// <summary>One address splits off six parts of about a million satoshi each and keeps the change.</summary>
    private static BitcoinTransaction Split()
    {
        var tx = Tx("split", 900_200).In(Wpkh("splitter"), 20_000_000);
        for (var i = 0; i < 6; i++)
        {
            tx.Out(Wpkh($"part-{i}"), 1_000_000 + (i * 10_000));
        }

        return tx.Out(Wpkh("splitter-change"), 13_800_000).Build();
    }

    /// <summary>A row of hops that each pass one coin on, the given blocks apart.</summary>
    private static List<BitcoinTransaction> Hops(int count, int blocksApart)
    {
        var row = new List<BitcoinTransaction>();
        var builder = Tx($"hop-{count}-0", 900_600).In(Wpkh($"hop-{count}-holder-0"), 50_000_000);
        long holding = 50_000_000;
        for (var i = 0; i < count; i++)
        {
            holding -= 5_000;
            var hop = builder.Out(Wpkh($"hop-{count}-holder-{i + 1}"), holding).Build();
            row.Add(hop);
            builder = Tx($"hop-{count}-{i + 1}", 900_600 + ((i + 1) * blocksApart)).Spend(hop, 0);
        }

        return row;
    }

    /// <summary>A transaction that sends 546 satoshi to each of many addresses.</summary>
    private static BitcoinTransaction Dusting(string label, int recipients, long satoshi)
    {
        var tx = Tx(label, 900_700).In(Wpkh(label + "-duster"), 100_000);
        for (var i = 0; i < recipients; i++)
        {
            tx.Out(Wpkh($"{label}-target-{i}"), satoshi);
        }

        return tx.Build();
    }

    /// <summary>
    /// An exchange: each customer pays a deposit address of its own, a few hours apart over two days, and the
    /// deposits are swept into one hot wallet in several sweeps.
    /// </summary>
    private static (List<BitcoinTransaction> Deposits, List<BitcoinTransaction> Sweeps) Exchange(
        string label, int customers, int sweeps)
    {
        var deposits = Enumerable.Range(0, customers)
            .Select(i => Tx($"{label}-deposit-{i}", 900_000 + (i * 30))
                .In(Wpkh($"{label}-customer-{i}"), 3_000_000)
                .Out(Wpkh($"{label}-deposit-{i}"), 1_000_000 + (i * 1_111))
                .Out(Wpkh($"{label}-customer-{i}-change"), 1_990_000 - (i * 1_111))
                .Build())
            .ToList();
        var perSweep = customers / sweeps;
        var swept = Enumerable.Range(0, sweeps)
            .Select(s => deposits.Skip(s * perSweep).Take(perSweep)
                .Aggregate(
                    Tx($"{label}-sweep-{s}", 900_000 + (customers * 30) + s), (tx, deposit) => tx.Spend(deposit, 0))
                .Out(Pkh($"{label}-hot-wallet"), perSweep * 1_000_000)
                .Build())
            .ToList();
        return (deposits, swept);
    }

    /// <summary>Gives the block height whose time is the given hour (UTC) of a day in the synthetic chain.</summary>
    private static long AtHour(int day, int hour) => 1_000_000 + (day * 144) + (hour * 6) - (1_000_000 % 144);

    private static StoredCheck Single(CheckRun run, string analysis, string? keyPrefix = null) =>
        Assert.Single(run.Checks, check => check.Analysis.Name == analysis
            && (keyPrefix is null || check.Finding.Key.StartsWith(keyPrefix, StringComparison.Ordinal)));

    private static void AssertJudgement(StoredCheck check, string analysis)
    {
        Assert.Contains(analysis, check.Finding.Why, StringComparison.Ordinal);
        Assert.Contains("bedömning", check.Finding.Why, StringComparison.Ordinal);
        Assert.Contains("inte ett faktum", check.Finding.Why, StringComparison.Ordinal);
    }
}
