using System.Diagnostics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>
/// The facit cases from the independent review of the fifth round (its probes, made into answers): an unlabelled
/// exchange, a batched exchange and a payment processor are no likely mixer, a mixer paying in parts is one, a chain
/// never follows a clearly worse output only because it goes into a mixer, a peel chain to an exchange is no row of
/// hops, a burst on one afternoon is no time pattern, and a hub is read in time.
/// </summary>
public sealed class RoundFiveReviewTests
{
    private static readonly IReadOnlyList<KnownMixer> TwoMixers =
        [new KnownMixer([Parting.Mixer, Parting.MixerAgain]), new KnownMixer([Parting.OtherMixer])];

    [Theory]
    [InlineData(1, 30, 150, 1)]
    [InlineData(2, 30, 300, 1)]
    [InlineData(3, 30, 600, 1)]
    [InlineData(4, 30, 60, 10)]
    [InlineData(5, 30, 45, 1)]
    public void An_unlabelled_exchange_with_random_withdrawals_is_no_likely_mixer(
        int seed, int customers, int withdrawals, int perTx)
    {
        var hits = 0;
        for (var round = 0; round < 10; round++)
        {
            var (txs, owners) = Behaviour.RandomExchange((seed * 100) + round, customers, withdrawals, perTx);
            hits += new MixerBehaviourCheck().Run(Behaviour.Input(txs, owners)).Count;
        }

        Assert.True(hits == 0, $"{hits} av 10 börser lästes som en trolig mixer.");
    }

    [Theory]
    [InlineData(300, 1)]
    [InlineData(100, 2)]
    public void A_busy_central_mixer_is_a_likely_mixer(int users, int mostParts)
    {
        var found = 0;
        for (var seed = 0; seed < 3; seed++)
        {
            var (txs, owners) = Behaviour.BusyMixer(seed, users, mostParts);
            found += new MixerBehaviourCheck().Run(Behaviour.Input(txs, owners)).Count;
        }

        Assert.True(found == 3, $"{found} av 3 upptagna mixrar hittades.");
    }

    [Theory]
    [InlineData(300, 300)]
    [InlineData(100, 100)]
    public void A_busy_exchange_with_as_many_withdrawals_as_deposits_is_no_likely_mixer(int customers, int withdrawals)
    {
        for (var seed = 0; seed < 3; seed++)
        {
            var (txs, owners) = Behaviour.RandomExchange(seed, customers, withdrawals);

            Assert.Empty(new MixerBehaviourCheck().Run(Behaviour.Input(txs, owners)));
        }
    }

    [Theory]
    [InlineData(0.01, 12, 0L, 1, 3)]
    [InlineData(0.005, 40, 0L, 1, 3)]
    [InlineData(0.02, 40, 0L, 1, 3)]
    [InlineData(0.01, 40, 3_000L, 1, 3)]
    [InlineData(0.01, 40, 10_000L, 1, 3)]
    [InlineData(0.01, 40, 0L, 6, 18)]
    public void A_processor_forwarding_to_fresh_addresses_is_a_likely_mixer_or_forwarding_service(
        double fee, int payments, long fixedFee, int soonest, int latest)
    {
        var (txs, owners) = Behaviour.FreshProcessor(fee, payments, fixedFee, soonest, latest);

        var finding = Assert.Single(new MixerBehaviourCheck().Run(Behaviour.Input(txs, owners)));

        Assert.Equal(MixerBehaviourCheck.AmbiguousTitle, finding.Title);
        Assert.Contains("betaltjänst", finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mixer_with_varied_fees_and_delays_is_a_likely_mixer_and_a_steady_one_may_be_a_service()
    {
        var (varied, variedOwners) = Behaviour.CentralMixer("vm", users: 20);
        var (steady, steadyOwners) = Behaviour.SteadyMixer(20);

        var mixer = Assert.Single(new MixerBehaviourCheck().Run(Behaviour.Input(varied, variedOwners)));
        var service = Assert.Single(new MixerBehaviourCheck().Run(Behaviour.Input(steady, steadyOwners)));

        Assert.Equal(MixerBehaviourCheck.MixerTitle, mixer.Title);
        Assert.Contains("Både avgiften och fördröjningen varierar", mixer.Why, StringComparison.Ordinal);
        Assert.Equal(MixerBehaviourCheck.AmbiguousTitle, service.Title);
        Assert.Contains("Både avgiften och fördröjningen är mest jämna", service.Why, StringComparison.Ordinal);
    }

    /// <summary>The fourth review's variants: each service and mixer, and the title it must get.</summary>
    public static TheoryData<string, bool> Variants { get; } = new()
    {
        { "proc-tiers-1-0.5", true },
        { "proc-tiers-1-0.8", true },
        { "proc-3-percent-outliers", true },
        { "proc-market-network-fee", true },
        { "proc-waits-6-confirmations", true },
        { "mix-random-fee-steady-delay", true },
        { "mix-steady-fee-random-delay", true },
        { "mix-random-fee-random-delay", false },
    };

    [Theory]
    [MemberData(nameof(Variants))]
    public void Each_variant_gets_the_title_its_steadiness_says(string variant, bool mayForward)
    {
        for (var seed = 0; seed < 3; seed++)
        {
            var (txs, owners) = Behaviour.Forwarder(variant, seed, 40);

            var finding = Assert.Single(new MixerBehaviourCheck().Run(Behaviour.Input(txs, owners)));

            var title = mayForward ? MixerBehaviourCheck.AmbiguousTitle : MixerBehaviourCheck.MixerTitle;
            Assert.Equal(title, finding.Title);
        }
    }

    [Fact]
    public void A_chain_through_a_mixer_or_forwarding_service_says_both_in_the_step()
    {
        var (background, owners) = Behaviour.SteadyMixer(20);
        var pool = Pkh("sm-pool");
        var first = Parting.Payout("fw-hop-1", Wpkh("fw-hop-1"), 14_625_000, 4);
        var into = Tx("fw-into", Parting.Height + 10).Spend(first, 0).Out(Wpkh("sm-dep-x"), 14_600_000).Build();
        owners[Wpkh("sm-dep-x")] = pool;
        var outOf = Tx("fw-out", Parting.Height + 13).In(pool, 90_000_000, "fw-coin")
            .Out(Wpkh("fw-hop-2"), 14_162_000).Out(pool, 10_000_000).Build();
        var onward = Tx("fw-onward", Parting.Height + 30).Spend(outOf, 0).Out(Parting.OtherMixer, 14_150_000).Build();

        var findings = new MixerChainCheck().Run(
            Behaviour.Input([.. background, Parting.Deposit(), first, into, outOf, onward], owners, TwoMixers));

        var chain = Assert.Single(findings, finding => finding.Basis.Any(basis => basis.Txid == Txid("fw-out")));
        Assert.Contains("in i en trolig mixer eller vidarebetalande tjänst utan etikett", chain.Why,
            StringComparison.Ordinal);
        Assert.Contains("då är steget en vidarebetalning, inte en mixning", chain.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mixer_with_only_a_third_of_its_deposits_in_the_graph_is_not_seen()
    {
        // A known limit (Guide and BACKLOG): the payouts of deposits the graph does not hold lie in the same week, so
        // the deposits explain too few of the payouts. Expand the mixer's deposits for it to be seen.
        var (txs, owners) = Behaviour.PartlySeenMixer();

        Assert.Empty(new MixerBehaviourCheck().Run(Behaviour.Input(txs, owners)));
    }

    [Fact]
    public void A_payment_processor_that_forwards_to_its_merchants_is_no_likely_mixer()
    {
        var (txs, owners) = Behaviour.Processor();

        Assert.Empty(new MixerBehaviourCheck().Run(Behaviour.Input(txs, owners)));
    }

    [Fact]
    public void A_mixer_that_pays_each_deposit_out_in_two_parts_is_a_likely_mixer()
    {
        // Deposits ten hours apart, so that the parts of one are not crowded by the parts of the next.
        var (txs, owners) = Behaviour.TwoPartMixer(spacing: 60);

        var finding = Assert.Single(new MixerBehaviourCheck().Run(Behaviour.Input(txs, owners)));

        Assert.Contains("i delar", finding.Why, StringComparison.Ordinal);
        Assert.Contains(Txid("m2-out2-5"), finding.Basis.Select(basis => basis.Txid));
    }

    [Fact]
    public void A_crowded_mixer_paying_in_parts_is_not_told_from_chance()
    {
        // Deposits an hour apart: pairs of other users' parts match as often as the real ones do (BACKLOG).
        var (txs, owners) = Behaviour.TwoPartMixer(spacing: 6);

        Assert.Empty(new MixerBehaviourCheck().Run(Behaviour.Input(txs, owners)));
    }

    [Fact]
    public void A_chain_never_follows_a_clearly_worse_output_only_because_it_goes_into_a_mixer()
    {
        // The likeliest output (2,5 %, forty minutes) goes nowhere; a worse one (7,3 %, three days, another type) goes
        // into the other mixer.
        var best = Parting.Payout("cw-best", Wpkh("cw-best"), 14_625_000, 4);
        var worse = Parting.Payout("cw-worse", Pkh("cw-worse"), 13_900_000, 432);
        var onward = Tx("cw-onward", Parting.Height + 440).Spend(worse, 0).Out(Parting.OtherMixer, 13_890_000).Build();

        Assert.Empty(new MixerChainCheck().Run(
            Behaviour.Input([Parting.Deposit(), best, worse, onward], mixers: TwoMixers)));
    }

    [Fact]
    public void A_nearly_as_likely_output_that_goes_into_a_mixer_is_followed_and_says_its_rank()
    {
        var best = Parting.Payout("nl-best", Wpkh("nl-best"), 14_625_000, 4);
        var near = Parting.Payout("nl-near", Wpkh("nl-near"), 14_200_000, 144);
        var onward = Tx("nl-onward", Parting.Height + 150).Spend(near, 0).Out(Parting.OtherMixer, 14_190_000).Build();

        var finding = Assert.Single(new MixerChainCheck().Run(
            Behaviour.Input([Parting.Deposit(), best, near, onward], mixers: TwoMixers)));

        Assert.Contains("inte den troligaste: nummer 2 av 2", finding.Why, StringComparison.Ordinal);
        Assert.Contains("men den som går vidare in i en mixer", finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_chain_through_a_likely_mixer_takes_the_payout_the_mixer_reading_assigned()
    {
        var (background, owners) = Behaviour.CentralMixer("sb", users: 8, start: Parting.Height - 400);
        var first = Parting.Payout("sbhop-1", Wpkh("sbhop-1"), 14_625_000, 4);
        var intoMid = Tx("sb-into", Parting.Height + 10).Spend(first, 0).Out(Wpkh("sb-deposit-x"), 14_600_000)
            .Build();
        owners[Wpkh("sb-deposit-x")] = Behaviour.Pool("sb");
        var best = Tx("sb-best", Parting.Height + 11).In(Behaviour.Pool("sb"), 90_000_000, "sb-coin-best")
            .Out(Wpkh("sb-best"), 14_162_000).Out(Behaviour.Pool("sb"), 75_000_000).Build();
        var worse = Tx("sb-worse", Parting.Height + 870).In(Behaviour.Pool("sb"), 90_000_000, "sb-coin-worse")
            .Out(Pkh("sb-worse"), 13_885_000).Out(Behaviour.Pool("sb"), 75_000_000).Build();
        var intoOther = Tx("sb-other", Parting.Height + 900).Spend(worse, 0).Out(Parting.OtherMixer, 13_880_000)
            .Build();
        IReadOnlyList<BitcoinTransaction> txs =
            [.. background, Parting.Deposit(), first, intoMid, best, worse, intoOther];

        var findings = new MixerChainCheck().Run(Behaviour.Input(txs, owners, TwoMixers));

        Assert.DoesNotContain(findings, finding => finding.Basis.Any(basis => basis.Txid == Txid("sb-other")));
    }

    [Fact]
    public void A_peel_chain_that_cashes_out_at_a_labelled_exchange_is_no_row_of_hops()
    {
        var row = PeelToOneAddress(Wpkh("pc-exchange-deposit"));
        var input = Behaviour.Input(row) with
        {
            Services = [new KnownService([Wpkh("pc-exchange-deposit")], true, "Börsen Y")],
        };

        Assert.Empty(new PassThroughCheck().Run(input));
    }

    [Fact]
    public void A_peel_chain_the_clustering_reads_is_the_peel_chains_and_no_row_of_hops()
    {
        using var harness = new Harness();
        var row = PeelToOneAddress(Wpkh("pc-deposit"));

        var run = harness.Run(row);

        Assert.Contains(run.Checks, check => check.Analysis.Name == PeelChainCheck.Name);
        Assert.DoesNotContain(run.Checks, check => check.Analysis.Name == PassThroughCheck.Name);
    }

    [Fact]
    public void A_burst_of_spends_on_one_afternoon_is_no_time_pattern()
    {
        var txs = Enumerable.Range(0, 24)
            .Select(i => Tx($"burst-{i}", 1_000_000 + (i % 18)).In(Wpkh("burst-owner"), 1_000_000, $"burst-c-{i}")
                .Out(Wpkh($"burst-paid-{i}"), 990_000).Build())
            .ToList();

        Assert.Empty(new ActiveHoursCheck().Run(Behaviour.Input(txs)));
    }

    [Fact]
    public void A_labelled_services_receipts_are_no_time_pattern()
    {
        var receipts = Enumerable.Range(0, 24)
            .Select(day => Tx($"svc-recv-{day}", 1_000_000 + (day * 144) + ((day % 12) * 6) + 54)
                .In(Wpkh($"svc-payer-{day}"), 1_000_000).Out(Wpkh("svc-shop"), 990_000).Build())
            .ToList();
        var input = Behaviour.Input(receipts) with
        {
            Services = [new KnownService([Wpkh("svc-shop")], false, "Butiken")],
        };

        Assert.DoesNotContain(
            new ActiveHoursCheck().Run(input), finding => finding.Key.StartsWith("hours-in:", StringComparison.Ordinal));
        Assert.Single(new ActiveHoursCheck().Run(Behaviour.Input(receipts)));
    }

    [Fact]
    public void A_hub_with_thousands_of_deposits_and_payouts_is_read_in_time()
    {
        var (txs, owners) = Behaviour.Hub();
        var input = Behaviour.Input(txs, owners, [new KnownMixer([Parting.OtherMixer])]);
        ICheckAnalysis[] analyses =
        [
            new MixerBehaviourCheck(), new SplitMergeCheck(), new NestedServiceCheck(), new ActiveHoursCheck(),
            new MixerChainCheck(), new PassThroughCheck(),
        ];

        foreach (var analysis in analyses)
        {
            var watch = Stopwatch.StartNew();
            analysis.Run(input);
            watch.Stop();

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"{analysis.Id}: {watch.Elapsed.TotalSeconds:0.00} s.");
        }
    }

    /// <summary>A peel chain that pays two percent to the same address in each of five hops.</summary>
    private static List<BitcoinTransaction> PeelToOneAddress(BitcoinAddress paid)
    {
        var row = new List<BitcoinTransaction>();
        var holding = 50_000_000L;
        var builder = Tx("pc-0", 900_000).In(Wpkh("pc-holder-0"), holding);
        for (var i = 0; i < 5; i++)
        {
            var peel = holding * 2 / 100;
            holding -= peel + 2_000;
            var hop = builder.Out(Wpkh($"pc-holder-{i + 1}"), holding).Out(paid, peel).Build();
            row.Add(hop);
            builder = Tx($"pc-{i + 1}", 900_000 + ((i + 1) * 6)).Spend(hop, 0);
        }

        return row;
    }
}
