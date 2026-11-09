using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>
/// The facit for the fifth round: a likely mixer without a label, mixer after mixer through it, with parts that go on
/// together and outputs that branch, splits spread over several transactions, hops that pay a fee, the time pattern in
/// Swedish time for spenders and receivers, and a nested service. What each recognises, what it leaves alone, and the
/// false alarms on random graphs.
/// </summary>
public sealed class RoundFiveChecksTests
{
    private static readonly IReadOnlyList<KnownMixer> TwoMixers =
        [new KnownMixer([Parting.Mixer, Parting.MixerAgain]), new KnownMixer([Parting.OtherMixer])];

    private static readonly string DepositKey = $"{Txid("parts-deposit").Value}:0";

    // ---- A likely mixer without a label ------------------------------------------------------------------------

    [Fact]
    public void Deposits_that_come_out_again_minus_a_fee_from_other_coins_are_a_likely_mixer()
    {
        var (txs, owners) = Behaviour.CentralMixer("cm", users: 10);

        var finding = Assert.Single(new MixerBehaviourCheck().Run(Behaviour.Input(txs, owners)));

        Assert.Equal("likely-mixer:" + Behaviour.Pool("cm").Value, finding.Key);
        Assert.Equal(CheckKind.Check, finding.Kind);
        Assert.Equal(AreaSort.Obfuscation, finding.Area);
        Assert.Equal("Trolig mixer utan etikett", finding.Title);
        Assert.Contains("10 av 10 insättningar", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("3 %–3 %", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("mixer-behaviour v1", finding.Why, StringComparison.Ordinal);
        Assert.Contains("inte ett faktum", finding.Why, StringComparison.Ordinal);
        Assert.Contains(Txid("cm-out-4"), finding.Basis.Select(basis => basis.Txid));
    }

    [Fact]
    public void An_exchange_a_wallet_with_few_senders_or_a_labelled_mixer_is_no_likely_mixer()
    {
        var (exchange, exchangeOwners) = Behaviour.Exchange("ux", customers: 12);
        var (few, fewOwners) = Behaviour.CentralMixer("few", users: 6);
        var (labelled, labelledOwners) = Behaviour.CentralMixer("lab", users: 10);
        var mixers = new[] { new KnownMixer([Behaviour.Pool("lab")]) };

        var analysis = new MixerBehaviourCheck();

        Assert.Empty(analysis.Run(Behaviour.Input(exchange, exchangeOwners)));
        Assert.Empty(analysis.Run(Behaviour.Input(few, fewOwners)));
        Assert.Empty(analysis.Run(Behaviour.Input(labelled, labelledOwners, mixers)));
    }

    [Fact]
    public void A_mixer_that_pays_back_with_the_deposits_own_coins_is_no_likely_mixer()
    {
        var pool = Behaviour.Pool("own");
        var owners = new Dictionary<BitcoinAddress, BitcoinAddress> { [pool] = pool };
        var txs = new List<BitcoinTransaction>();
        for (var i = 0; i < 10; i++)
        {
            var deposit = Wpkh($"own-deposit-{i}");
            owners[deposit] = pool;
            var into = Tx($"own-in-{i}", Behaviour.Height + (i * 6))
                .In(Wpkh($"own-user-{i}"), 3_030_000 + (i * 1_000_000))
                .Out(deposit, 3_000_000 + (i * 1_000_000)).Build();
            txs.Add(into);
            txs.Add(Tx($"own-out-{i}", Behaviour.Height + (i * 6) + 3).Spend(into, 0)
                .Out(Wpkh($"own-paid-{i}"), (3_000_000 + (i * 1_000_000)) * 97 / 100).Build());
        }

        Assert.Empty(new MixerBehaviourCheck().Run(Behaviour.Input(txs, owners)));
    }

    // ---- Mixer after mixer ------------------------------------------------------------------------------------

    [Fact]
    public void A_chain_passes_through_a_likely_mixer_without_a_label()
    {
        var (background, owners) = Behaviour.CentralMixer("mid", users: 8, start: Parting.Height - 200);
        var first = Parting.Payout("hop-1", Wpkh("hop-1"), 14_625_000, 4);
        var intoMid = Tx("into-mid", Parting.Height + 10).Spend(first, 0).Out(Wpkh("mid-deposit-x"), 14_600_000)
            .Build();
        owners[Wpkh("mid-deposit-x")] = Behaviour.Pool("mid");
        var outOfMid = Tx("out-of-mid", Parting.Height + 20).In(Behaviour.Pool("mid"), 90_000_000, "mid-coin-x")
            .Out(Wpkh("hop-2"), 14_162_000).Out(Behaviour.Pool("mid"), 75_828_000).Build();
        var intoOther = Tx("into-other", Parting.Height + 30).Spend(outOfMid, 0).Out(Parting.OtherMixer, 14_150_000)
            .Build();
        IReadOnlyList<BitcoinTransaction> txs = [.. background, Parting.Deposit(), first, intoMid, outOfMid, intoOther];

        var findings = new MixerChainCheck().Run(Behaviour.Input(txs, owners, TwoMixers));

        var finding = Assert.Single(findings);
        Assert.Equal(DepositKey, finding.Key);
        Assert.Equal("Mixer efter mixer, 3 steg", finding.Title);
        Assert.Contains("Steg 2: 0,1460 BTC in i en trolig mixer utan etikett", finding.Why, StringComparison.Ordinal);
        Assert.Contains("Steg 3: 0,1415 BTC in i en annan mixer", finding.Why, StringComparison.Ordinal);
        Assert.Contains("mixer-behaviour v1:s bedömning", finding.Why, StringComparison.Ordinal);
        Assert.Contains(Txid("out-of-mid"), finding.Basis.Select(basis => basis.Txid));
    }

    [Fact]
    public void Parts_spent_together_into_the_next_mixer_are_followed_together_with_their_sum()
    {
        var parts = Parting.Parts();
        var together = Tx("ab-onward", Parting.Height + 80).Spend(parts[0], 0).Spend(parts[1], 0)
            .Out(Parting.OtherMixer, 10_990_000).Build();

        var finding = Assert.Single(new MixerChainCheck().Run(
            Behaviour.Input([Parting.Deposit(), .. parts, together], mixers: TwoMixers)));

        Assert.Equal("Mixer efter mixer, 2 steg", finding.Title);
        Assert.Contains("2 troliga delar på sammanlagt 0,1100 BTC", finding.Why, StringComparison.Ordinal);
        Assert.Contains("de går vidare i tx", finding.Why, StringComparison.Ordinal);
        Assert.Contains("trolig avgift 0,0030 BTC", finding.Why, StringComparison.Ordinal);
        Assert.Contains(Wpkh("part-a"), finding.Addresses);
        Assert.Contains(Wpkh("part-b"), finding.Addresses);
    }

    [Fact]
    public void Two_approved_outputs_into_different_mixers_branch_the_chain()
    {
        var outA = Parting.Payout("branch-a", Wpkh("branch-a"), 7_000_000, 6);
        var outB = Parting.Payout("branch-b", Wpkh("branch-b"), 7_500_000, 9);
        var intoOther = Tx("branch-a-on", Parting.Height + 20).Spend(outA, 0).Out(Parting.OtherMixer, 6_990_000)
            .Build();
        var intoAgain = Tx("branch-b-on", Parting.Height + 25).Spend(outB, 0).Out(Parting.MixerAgain, 7_490_000)
            .Build();
        IReadOnlyList<ApprovedCandidate> approved =
        [
            new(MixerOutputsCheck.Identity, DepositKey, Txid("branch-a"), 0),
            new(MixerOutputsCheck.Identity, DepositKey, Txid("branch-b"), 0),
        ];
        var input = Behaviour.Input([Parting.Deposit(), outA, outB, intoOther, intoAgain], mixers: TwoMixers)
            with { Approved = approved };

        var finding = Assert.Single(new MixerChainCheck().Run(input));

        Assert.Equal("Mixer efter mixer, 3 steg, 2 grenar", finding.Title);
        Assert.Contains("kedjan delar sig i 2 vägar: (a) godkänd del", finding.Why, StringComparison.Ordinal);
        Assert.Contains("Steg 3 (efter steg 1):", finding.Why, StringComparison.Ordinal);
        Assert.Contains("samma mixer igen", finding.Why, StringComparison.Ordinal);
        Assert.Contains("en annan mixer", finding.Why, StringComparison.Ordinal);
        Assert.StartsWith("0,1500 in · avgifter 0,0050", finding.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_branched_chain_gives_the_same_finding_in_any_order()
    {
        var parts = Parting.Parts();
        var together = Tx("ab-onward", Parting.Height + 80).Spend(parts[0], 0).Spend(parts[1], 0)
            .Out(Parting.OtherMixer, 10_990_000).Build();
        IReadOnlyList<BitcoinTransaction> txs = [Parting.Deposit(), .. parts, together];
        var analysis = new MixerChainCheck();

        Assert.Equal(
            analysis.Run(Behaviour.Input(txs, mixers: TwoMixers)).Select(FindingCodec.Encode),
            analysis.Run(Behaviour.Input([.. txs.Reverse()], mixers: TwoMixers)).Select(FindingCodec.Encode));
    }

    // ---- Splits spread over several transactions ---------------------------------------------------------------

    [Fact]
    public void Equal_parts_to_many_recipients_in_several_transactions_within_a_day_are_a_spread_split()
    {
        var txs = SpreadSplit("sp", parts: 6);

        var split = Assert.Single(new SplitMergeCheck().Run(Behaviour.Input(txs)));

        Assert.Equal("spread:" + Txid("sp-0").Value, split.Key);
        Assert.Equal("Uppdelning i 6 delar över 3 tx", split.Title);
        Assert.Equal(3, split.Transactions.Count);
        Assert.Contains("split-merge v2", split.Why, StringComparison.Ordinal);
        Assert.Contains("inte ett faktum", split.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_parts_of_a_spread_split_that_meet_after_five_hops_are_seen()
    {
        var txs = SpreadSplit("sp", parts: 6);
        var hops = new List<BitcoinTransaction>();
        var previous = txs[0];
        for (var i = 0; i < 5; i++)
        {
            var hop = Tx($"long-hop-{i}", Behaviour.Height + 100 + i).Spend(previous, 0)
                .Out(Wpkh($"long-hop-{i}"), 1_000_000 - ((i + 1) * 1_000)).Build();
            hops.Add(hop);
            previous = hop;
        }

        var join = Tx("long-join", Behaviour.Height + 200).Spend(previous, 0).Spend(txs[1], 0)
            .Out(Wpkh("long-collector"), 1_990_000).Build();

        var joined = Assert.Single(
            new SplitMergeCheck().Run(Behaviour.Input([.. txs, .. hops, join])),
            finding => finding.Key.StartsWith("join:", StringComparison.Ordinal));

        Assert.Equal("Delarna samlas igen, 2 av 6", joined.Title);
        Assert.Contains("genom 5 mellanled", joined.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Payments_of_different_amounts_or_spread_over_days_are_no_spread_split()
    {
        var different = Enumerable.Range(0, 3)
            .Select(t => Tx($"diff-{t}", Behaviour.Height + t)
                .In(Wpkh("diff-payer"), 10_000_000, $"diff-coin-{t}")
                .Out(Wpkh($"diff-{t}-a"), 1_000_000 + (t * 400_000))
                .Out(Wpkh($"diff-{t}-b"), 2_500_000 + (t * 700_000))
                .Build());
        var slow = Enumerable.Range(0, 6)
            .Select(t => Tx($"slow-{t}", Behaviour.Height + (t * 200))
                .In(Wpkh("slow-payer"), 3_000_000, $"slow-coin-{t}")
                .Out(Wpkh($"slow-{t}"), 1_000_000)
                .Build());

        Assert.Empty(new SplitMergeCheck().Run(Behaviour.Input([.. different, .. slow])));
    }

    [Fact]
    public void A_payer_whose_equal_parts_are_the_lesser_share_of_what_it_paid_is_no_spread_split()
    {
        var txs = SpreadSplit("busy", parts: 5).ToList();
        for (var i = 0; i < 8; i++)
        {
            txs.Add(Tx($"busy-other-{i}", Behaviour.Height + 10 + i)
                .In(Wpkh("busy-payer"), 50_000_000, $"busy-other-coin-{i}")
                .Out(Wpkh($"busy-other-{i}"), 3_000_000 + (i * 2_000_000))
                .Build());
        }

        Assert.Empty(new SplitMergeCheck().Run(Behaviour.Input(txs)));
    }

    // ---- Many hops with a fee ---------------------------------------------------------------------------------

    [Fact]
    public void Hops_that_pay_a_small_fee_to_the_same_address_are_still_many_hops()
    {
        var row = FeeHops("fee", 4, Wpkh("fee-collector"));

        var finding = Assert.Single(new PassThroughCheck().Run(Behaviour.Input(row)));

        Assert.Equal("Många hopp, 4 i rad", finding.Title);
        Assert.Contains("4 av hoppen betalar också en liten avgift", finding.Why, StringComparison.Ordinal);
        Assert.Contains("pass-through v2", finding.Why, StringComparison.Ordinal);
        Assert.Equal(5, finding.Addresses.Count);
        Assert.DoesNotContain(Wpkh("fee-collector"), finding.Addresses);
    }

    [Fact]
    public void Hops_that_pay_a_labelled_service_are_hops_and_name_the_service()
    {
        var row = FeeHops("svc", 3, null);
        var input = Behaviour.Input(row) with
        {
            Services = [new KnownService([Wpkh("svc-fee-0"), Wpkh("svc-fee-1"), Wpkh("svc-fee-2")], false, "Lagret")],
        };

        var finding = Assert.Single(new PassThroughCheck().Run(input));

        Assert.Equal("Många hopp, 3 i rad", finding.Title);
        Assert.Contains("tjänsten Lagret", finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_peel_chain_that_pays_small_amounts_to_different_addresses_is_no_row_of_hops()
    {
        var row = FeeHops("peel", 5, null);

        Assert.Empty(new PassThroughCheck().Run(Behaviour.Input(row)));
    }

    // ---- The time pattern --------------------------------------------------------------------------------------

    [Fact]
    public void The_exact_chance_of_a_quiet_run_matches_a_simulation()
    {
        var random = new Random(3);
        foreach (var (transactions, quiet) in new[] { (20, 6), (25, 6), (12, 8), (40, 4) })
        {
            var asLong = 0;
            const int draws = 20_000;
            for (var draw = 0; draw < draws; draw++)
            {
                var busy = new bool[24];
                for (var i = 0; i < transactions; i++)
                {
                    busy[random.Next(24)] = true;
                }

                asLong += LongestQuiet(busy) >= quiet ? 1 : 0;
            }

            var simulated = (double)asLong / draws;
            Assert.InRange(ActiveHoursCheck.Chance(transactions, quiet), simulated - 0.01, simulated + 0.01);
        }

        Assert.Equal(1, ActiveHoursCheck.Chance(5, 0));
        Assert.Equal(0, ActiveHoursCheck.Chance(5, 24));
    }

    [Fact]
    public void An_owner_who_receives_only_by_day_has_a_time_pattern_as_a_receiver()
    {
        var receipts = Enumerable.Range(0, 24)
            .Select(day => Tx($"recv-{day}", AtUtc(new DateTimeOffset(2043, 1, 5 + day, 9 + (day % 12), 0, 0,
                    TimeSpan.Zero)))
                .In(Wpkh($"recv-payer-{day}"), 1_000_000).Out(Wpkh("receiver"), 990_000).Build())
            .ToList();

        var finding = Assert.Single(new ActiveHoursCheck().Run(Behaviour.Input(receipts)));

        Assert.Equal("hours-in:" + Wpkh("receiver").Value, finding.Key);
        Assert.Equal("Tar emot kl. 10–22 svensk tid", finding.Title);
        Assert.Contains("active-hours v2", finding.Why, StringComparison.Ordinal);
        Assert.Contains("Benjamini–Hochberg", finding.Why, StringComparison.Ordinal);
        Assert.Equal([Wpkh("receiver")], finding.Addresses);
    }

    [Fact]
    public void Hours_that_follow_the_swedish_clock_over_the_change_to_summer_time_keep_their_quiet_run()
    {
        // From February to May at 07–15 Swedish time: in UTC the habit moves an hour at the end of March.
        var spends = Enumerable.Range(0, 30)
            .Select(i =>
            {
                var day = new DateTime(2043, 2, 1).AddDays(i * 3);
                var local = new DateTime(day.Year, day.Month, day.Day, 7 + (i % 9), 0, 0);
                var utc = TimeZoneInfo.ConvertTimeToUtc(local, Stockholm());
                return Tx($"dst-{i}", AtUtc(new DateTimeOffset(utc, TimeSpan.Zero)))
                    .In(Wpkh("dst-owner"), 1_000_000).Out(Wpkh($"dst-paid-{i}"), 990_000).Build();
            })
            .ToList();

        var finding = Assert.Single(
            new ActiveHoursCheck().Run(Behaviour.Input(spends)),
            found => found.Key.StartsWith("hours:", StringComparison.Ordinal));

        Assert.Equal("Aktiv kl. 07–16 svensk tid", finding.Title);
        Assert.Contains("tyst 15 h i rad", finding.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Many_owners_at_random_hours_give_no_time_pattern_after_the_correction()
    {
        var analysis = new ActiveHoursCheck();
        var random = new Random(11);
        var patterns = 0;
        for (var round = 0; round < 5; round++)
        {
            var txs = new List<BitcoinTransaction>();
            for (var owner = 0; owner < 100; owner++)
            {
                txs.AddRange(Enumerable.Range(0, 22)
                    .Select(day => Tx($"m{round}-o{owner}-{day}", AtHour(day, random.Next(24)))
                        .In(Wpkh($"m{round}-owner-{owner}"), 1_000_000)
                        .Out(Wpkh($"m{round}-o{owner}-paid-{day}"), 990_000)
                        .Build()));
            }

            patterns += analysis.Run(Behaviour.Input(txs)).Count;
        }

        Assert.True(patterns == 0, $"{patterns} tidsmönster bland 500 slumpade ägare.");
    }

    // ---- A nested service ---------------------------------------------------------------------------------------

    [Fact]
    public void A_wallet_with_many_customers_that_trades_both_ways_with_an_exchange_is_a_likely_nested_service()
    {
        var finding = Assert.Single(new NestedServiceCheck().Run(Behaviour.NestedInput(Behaviour.NestedService())));

        Assert.Equal("nested:" + Behaviour.Service.Value, finding.Key);
        Assert.Equal(CheckKind.Suggestion, finding.Kind);
        Assert.Equal(AreaSort.Service, finding.Area);
        Assert.Equal("Trolig nästlad tjänst hos Börsen X", finding.Title);
        Assert.Equal("9 avsändare, 9 mottagare · 2 insättningar, 2 uttag", finding.Summary);
        Assert.Contains(Behaviour.Account, finding.Addresses);
        Assert.Equal(Behaviour.Service, finding.Addresses[0]);
        Assert.Contains("nested-service v1", finding.Why, StringComparison.Ordinal);
        Assert.Contains("inte ett faktum", finding.Why, StringComparison.Ordinal);
        Assert.Empty(finding.Candidates);
    }

    [Fact]
    public void A_customer_with_few_counterparties_or_one_trip_or_no_label_is_no_nested_service()
    {
        var analysis = new NestedServiceCheck();
        var service = Behaviour.NestedService();

        Assert.Empty(analysis.Run(Behaviour.NestedInput(Behaviour.NestedService(customers: 4))));
        Assert.Empty(analysis.Run(Behaviour.NestedInput(Behaviour.NestedService(trips: 1))));
        Assert.Empty(analysis.Run(Behaviour.Input(service)));
    }

    [Fact]
    public void Payments_from_start_points_or_victims_are_never_a_services_customers()
    {
        var victims = Enumerable.Range(0, 9).Select(i => Wpkh($"ns-customer-{i}")).ToHashSet();
        var input = Behaviour.NestedInput(Behaviour.NestedService()) with { Protected = victims };

        Assert.Empty(new NestedServiceCheck().Run(input));
    }

    [Fact]
    public void The_nested_service_is_a_suggestion_that_can_be_approved_in_a_case()
    {
        using var harness = new Harness();
        var txs = Behaviour.NestedService();

        var run = harness.RunWithExchange(txs, "Börsen X", Behaviour.Hot, Behaviour.Account);
        var check = Assert.Single(run.Checks, c => c.Analysis.Name == NestedServiceCheck.Name);
        harness.Service.Decide(
            harness.Case, new DecisionRequest(check.Analysis, check.Finding.Key, null, CheckVerdict.Approved, null));
        var after = Assert.Single(
            harness.RunWithExchange(txs, "Börsen X", Behaviour.Hot, Behaviour.Account).Checks,
            c => c.Analysis.Name == NestedServiceCheck.Name);

        Assert.True(after.IsSettled);
        Assert.True(harness.Verify().IsIntact);
    }

    // ---- False alarms -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(300, 1_500)]
    [InlineData(40, 1_500)]
    public void Random_graphs_hardly_ever_give_the_fifth_rounds_findings(int addresses, int transactions)
    {
        ICheckAnalysis[] analyses =
        [
            new MixerBehaviourCheck(), new MixerChainCheck(), new NestedServiceCheck(), new SplitMergeCheck(),
            new PassThroughCheck(),
        ];
        var alarms = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var seed = 1; seed <= 20; seed++)
        {
            var (txs, labelled) = Behaviour.RandomGraph(seed, addresses, transactions);
            var input = Behaviour.RandomInput(txs, labelled);
            foreach (var analysis in analyses)
            {
                var found = analysis.Run(input).Count(IsNew);
                alarms[analysis.Id.Name] = alarms.GetValueOrDefault(analysis.Id.Name) + found;
            }
        }

        // Measured 2026-11-09: no alarm in 20 sparse and 20 dense graphs of 1 500 transactions each.
        Assert.All(alarms, entry => Assert.True(entry.Value == 0, $"{entry.Key}: {entry.Value} fynd."));
    }

    [Fact]
    public void A_graph_of_thousands_of_actors_is_read_in_time()
    {
        var (txs, labelled) = Behaviour.RandomGraph(99, addresses: 5_000, transactions: 20_000);
        var input = Behaviour.RandomInput(txs, labelled);
        ICheckAnalysis[] analyses =
        [
            new MixerBehaviourCheck(), new NestedServiceCheck(), new SplitMergeCheck(), new PassThroughCheck(),
            new ActiveHoursCheck(),
        ];

        foreach (var analysis in analyses)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            analysis.Run(input);
            watch.Stop();

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"{analysis.Id}: {watch.Elapsed.TotalSeconds:0.0} s.");
        }
    }

    /// <summary>
    /// Says whether a finding is one of what the fifth round added: every finding but a split in one transaction and
    /// a chain through labelled mixers alone, unbranched, as <c>mixer-chain v1</c> gave it on the random graphs'
    /// labelled mixer (whose random payouts are weighed as likely outputs); a chain through a likely mixer, branched
    /// or with parts together is new.
    /// </summary>
    private static bool IsNew(CheckFinding finding)
    {
        if (finding.Key.StartsWith("split:", StringComparison.Ordinal))
        {
            return false;
        }

        return !finding.Title.StartsWith("Mixer efter mixer", StringComparison.Ordinal)
            || finding.Title.Contains("grenar", StringComparison.Ordinal)
            || finding.Why.Contains("utan etikett", StringComparison.Ordinal)
            || finding.Why.Contains("följs tillsammans", StringComparison.Ordinal);
    }

    // ---- Helpers ----------------------------------------------------------------------------------------------

    /// <summary>One payer pays equal parts of a million satoshi, two per transaction, an hour apart.</summary>
    private static List<BitcoinTransaction> SpreadSplit(string label, int parts)
    {
        var txs = new List<BitcoinTransaction>();
        for (var t = 0; t * 2 < parts; t++)
        {
            var tx = Tx($"{label}-{t}", Behaviour.Height + (t * 6))
                .In(Wpkh($"{label}-payer"), 3_000_000, $"{label}-c{t}");
            for (var p = t * 2; p < Math.Min(parts, (t * 2) + 2); p++)
            {
                tx.Out(Wpkh($"{label}-part-{p}"), 1_000_000 + (p * 5_000));
            }

            txs.Add(tx.Out(Wpkh($"{label}-payer"), 900_000).Build());
        }

        return txs;
    }

    /// <summary>
    /// A row of hops that each pass a coin on and pay one percent as a fee: to the given address every time, or to
    /// an address of its own each time when none is given.
    /// </summary>
    private static List<BitcoinTransaction> FeeHops(string label, int count, BitcoinAddress? collector)
    {
        var row = new List<BitcoinTransaction>();
        var holding = 50_000_000L;
        var builder = Tx($"{label}-0", Behaviour.Height).In(Wpkh($"{label}-holder-0"), holding);
        for (var i = 0; i < count; i++)
        {
            var fee = holding / 100;
            holding -= fee + 2_000;
            var hop = builder.Out(Wpkh($"{label}-holder-{i + 1}"), holding)
                .Out(collector ?? Wpkh($"{label}-fee-{i}"), fee)
                .Build();
            row.Add(hop);
            builder = Tx($"{label}-{i + 1}", Behaviour.Height + ((i + 1) * 6)).Spend(hop, 0);
        }

        return row;
    }

    /// <summary>Gives the block height whose time is the given time (UTC) in the synthetic chain.</summary>
    private static long AtUtc(DateTimeOffset utc) =>
        (long)Math.Round((utc - new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalMinutes / 10);

    /// <summary>Gives the block height whose time is the given hour (UTC) of a day in the synthetic chain.</summary>
    private static long AtHour(int day, int hour) => 1_000_000 + (day * 144) + (hour * 6) - (1_000_000 % 144);

    /// <summary>Gives Swedish time.</summary>
    private static TimeZoneInfo Stockholm() => TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    /// <summary>The longest run of quiet hours around the clock.</summary>
    private static int LongestQuiet(bool[] busy)
    {
        var best = 0;
        for (var start = 0; start < 24; start++)
        {
            var length = 0;
            while (length < 24 && !busy[(start + length) % 24])
            {
                length++;
            }

            best = Math.Max(best, length);
        }

        return best;
    }
}
