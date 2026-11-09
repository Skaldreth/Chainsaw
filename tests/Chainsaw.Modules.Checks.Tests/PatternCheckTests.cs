using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>
/// The facit for <c>pattern v1</c>, the rules through the open door for pattern recognition: what each rule recognises,
/// what it leaves alone, and how often it raises a false alarm in an ordinary, random graph.
/// </summary>
public sealed class PatternCheckTests
{
    private const long Start = 900_000;

    private static readonly BitcoinAddress Mixer = Wpkh("mixer");

    private static readonly BitcoinAddress Exchange = Wpkh("exchange");

    // ---- The door ----------------------------------------------------------------------------------------------

    [Fact]
    public void The_shape_has_an_actor_per_owner_and_a_line_per_payer_and_payee_without_change()
    {
        var tx = Tx("pay", Start).In(Wpkh("a"), 1_000_000).Out(Wpkh("b"), 600_000).Out(Wpkh("a"), 390_000).Build();

        var shape = GraphShape.Of(Input([tx]));

        Assert.Equal(2, shape.Actors.Count);
        var flow = Assert.Single(shape.Flows);
        Assert.Equal(600_000, flow.Amount.Value);
        Assert.Equal(Wpkh("a"), shape.Actors[flow.From].Key);
        Assert.Equal(Wpkh("b"), shape.Actors[flow.To].Key);
        Assert.Equal(tx.Block, flow.Block);
        Assert.False(flow.IsShared);
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
    public void The_analysis_is_registered_as_an_analysis_like_the_others()
    {
        using var harness = new Harness();

        Assert.Contains(new HeuristicId(PatternCheck.Name, 1), harness.Service.Analyses);
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
    public void Two_collections_that_sweep_to_the_same_place_are_the_same_collection()
    {
        var txs = Collection("s1", Start, Wpkh("launder")).Concat(Collection("s2", Start + 500, Wpkh("launder")));

        var finding = Single(Run([.. txs]), "collect:");

        Assert.Equal("Samma insamling, 2 delar", finding.Title);
        Assert.Contains(Wpkh("s1"), finding.Addresses);
        Assert.Contains(Wpkh("s2"), finding.Addresses);
        Assert.Contains("6 avsändare", finding.Why, StringComparison.Ordinal);
        Assert.Contains(finding.Basis, basis => basis.Txid == Txid("s2-sweep"));
        AssertJudgement(finding);
    }

    [Fact]
    public void Two_collections_that_sweep_to_two_exchanges_are_alike_by_sort()
    {
        var other = Wpkh("exchange-b");
        var txs = Collection("s1", Start, Exchange).Concat(Collection("s2", Start + 500, other));

        var finding = Single(Run([.. txs], exchanges: [Exchange, other]), "collect:sort:Exchange");

        Assert.Contains("en börs", finding.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void One_collection_alone_or_two_that_sweep_apart_are_no_pattern()
    {
        Assert.Empty(Run([.. Collection("s1", Start, Wpkh("launder"))]));
        Assert.Empty(Run([.. Collection("s1", Start, Wpkh("x")).Concat(Collection("s2", Start + 500, Wpkh("y")))]));
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
                txs.Add(Tx($"{part}-fwd-{i}", Start + (i * 10) + 1).Spend(paid, 0).Out(Wpkh("launder"), 990_000).Build());
            }
        }

        Assert.DoesNotContain(Run(txs), finding => finding.Key.StartsWith("collect:", StringComparison.Ordinal));
    }

    // ---- Samma väg -------------------------------------------------------------------------------------------

    [Fact]
    public void Money_from_two_parts_through_a_mixer_to_an_exchange_goes_the_same_way()
    {
        var txs = Way("v1", Start).Concat(Way("v2", Start + 300)).ToList();

        var finding = Single(Run(txs, mixers: [Mixer], exchanges: [Exchange]), "route:");

        Assert.Equal("Samma väg, 2 delar", finding.Title);
        Assert.Equal("okänd → mixer → börs", finding.Summary);
        Assert.Contains("samma aktör", finding.Why, StringComparison.Ordinal);
        Assert.Equal(6, finding.Basis.Count);
        AssertJudgement(finding);
    }

    [Fact]
    public void One_way_or_money_moved_together_is_no_second_way()
    {
        Assert.Empty(Run([.. Way("v1", Start)], mixers: [Mixer], exchanges: [Exchange]));

        // Two victims pay the same scammer, who moves it all on in one transaction: one way, not two.
        var p1 = Tx("p1", Start).In(Wpkh("v1"), 2_000_000).Out(Wpkh("scammer"), 1_000_000).Build();
        var p2 = Tx("p2", Start + 1).In(Wpkh("v2"), 2_000_000).Out(Wpkh("scammer"), 1_000_000).Build();
        var mix = Tx("mix", Start + 2).Spend(p1, 0).Spend(p2, 0).Out(Mixer, 1_990_000).Build();
        var cash = Tx("cash", Start + 3).Spend(mix, 0).Out(Exchange, 1_980_000).Build();

        Assert.Empty(Run([p1, p2, mix, cash], mixers: [Mixer], exchanges: [Exchange]));
    }

    [Fact]
    public void A_way_without_labels_says_nothing()
    {
        Assert.Empty(Run([.. Way("v1", Start), .. Way("v2", Start + 300)]));
    }

    // ---- Rundgång ---------------------------------------------------------------------------------------------

    [Fact]
    public void Money_that_comes_back_through_two_others_is_a_round_trip()
    {
        var txs = Round("r", Start, back: 8_500_000);

        var finding = Single(Run(txs), "round:");

        Assert.Equal("round:" + Txid("r-0").Value, finding.Key);
        Assert.Equal("Rundgång, 3 steg", finding.Title);
        Assert.Contains("85 %", finding.Why, StringComparison.Ordinal);
        Assert.Equal(3, finding.Basis.Count);
        AssertJudgement(finding);
    }

    [Fact]
    public void A_refund_too_little_back_or_a_way_through_an_exchange_is_no_round_trip()
    {
        var pay = Tx("pay", Start).In(Wpkh("a"), 20_000_000).Out(Wpkh("b"), 10_000_000).Build();
        var refund = Tx("refund", Start + 5).Spend(pay, 0).Out(Wpkh("a"), 9_990_000).Build();
        Assert.Empty(Run([pay, refund]));

        Assert.Empty(Run(Round("r", Start, back: 3_000_000)));
        Assert.Empty(Run(Round("r", Start, back: 8_500_000), exchanges: [Wpkh("r-hop-1")]));
    }

    // ---- Likadana belopp i följd -----------------------------------------------------------------------------

    [Fact]
    public void Alike_amounts_in_a_row_are_structuring_and_another_actor_with_the_same_amount_is_named()
    {
        var txs = Split("p", Start, 9_950_000, count: 5, gap: 100)
            .Concat(Split("q", Start + 2_000, 9_960_000, count: 4, gap: 50))
            .ToList();

        var findings = Run(txs);

        var p = Assert.Single(findings, finding => finding.Key == "struct:" + Txid("p-0").Value);
        Assert.Equal("Likadana belopp, 5 i följd", p.Title);
        Assert.Contains(CheckTextShort(Wpkh("q")), p.Why, StringComparison.Ordinal);
        Assert.Contains("alla till", p.Why, StringComparison.Ordinal);
        Assert.Equal(5, p.Basis.Count);
        Assert.Single(findings, finding => finding.Key == "struct:" + Txid("q-0").Value);
        AssertJudgement(p);
    }

    [Fact]
    public void Too_few_too_spread_out_unlike_or_dust_amounts_are_no_structuring()
    {
        Assert.Empty(Run(Split("few", Start, 9_950_000, count: 3, gap: 100)));
        Assert.Empty(Run(Split("slow", Start, 9_950_000, count: 5, gap: 600)));
        Assert.Empty(Run(Split("dust", Start, 5_000, count: 6, gap: 10)));
        var unlike = Enumerable.Range(0, 6)
            .Select(i => Tx($"unlike-{i}", Start + i).In(Wpkh("unlike"), 50_000_000)
                .Out(Wpkh($"unlike-to-{i}"), 9_000_000 + (i * 300_000)).Build())
            .ToList();
        Assert.Empty(Run(unlike));
    }

    [Fact]
    public void An_exchanges_alike_payouts_are_never_structuring()
    {
        var txs = Split("ex", Start, 9_950_000, count: 6, gap: 10);

        Assert.Empty(Run(txs, exchanges: [Wpkh("ex")]));
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
    public void Random_graphs_hardly_ever_give_a_pattern()
    {
        var alarms = 0;
        var flows = 0;
        for (var seed = 1; seed <= 20; seed++)
        {
            var (txs, labelled) = RandomGraph(seed, addresses: 300, transactions: 1_500);
            var input = Input(txs, mixers: [labelled[0]], exchanges: [labelled[1], labelled[2]]);
            flows += GraphShape.Of(input).Flows.Count;
            alarms += Analysis.Run(input).Count;
        }

        // Measured 2026-11-09: no alarm in 300 random graphs of 1 500 transactions (450 000 lines); the test runs 20.
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

    private static string CheckTextShort(BitcoinAddress address) => address.Value[..8] + "…" + address.Value[^4..];

    /// <summary>Six victims pay a collecting address, which sweeps it all on in one transaction.</summary>
    private static IEnumerable<BitcoinTransaction> Collection(string part, long height, BitcoinAddress to)
    {
        var payments = Enumerable.Range(0, 6)
            .Select(i => Tx($"{part}-pay-{i}", height + (i * 10)).In(Wpkh($"{part}-victim-{i}"), 3_000_000)
                .Out(Wpkh(part), 2_000_000 + (i * 1_000)).Build())
            .ToList();
        var sweep = payments.Aggregate(Tx($"{part}-sweep", height + 100), (tx, paid) => tx.Spend(paid, 0))
            .Out(to, 11_900_000).Build();
        return [.. payments, sweep];
    }

    /// <summary>A victim pays a middleman, who pays the mixer, which pays the exchange, each later.</summary>
    private static IEnumerable<BitcoinTransaction> Way(string victim, long height)
    {
        var paid = Tx($"{victim}-paid", height).In(Wpkh(victim), 20_000_000).Out(Wpkh($"{victim}-mule"), 10_000_000)
            .Build();
        var mixed = Tx($"{victim}-mixed", height + 5).Spend(paid, 0).Out(Mixer, 9_900_000).Build();
        var cashed = Tx($"{victim}-cashed", height + 50).Spend(mixed, 0).Out(Exchange, 9_000_000).Build();
        return [paid, mixed, cashed];
    }

    /// <summary>An actor sends 10 BTC through two others and gets <paramref name="back"/> satoshi back.</summary>
    private static List<BitcoinTransaction> Round(string label, long height, long back)
    {
        var home = Wpkh($"{label}-home");
        var first = Tx($"{label}-0", height).In(home, 20_000_000).Out(Wpkh($"{label}-hop-1"), 10_000_000).Build();
        var second = Tx($"{label}-1", height + 10).Spend(first, 0).Out(Wpkh($"{label}-hop-2"), 9_500_000).Build();
        var third = Tx($"{label}-2", height + 20).Spend(second, 0).Out(home, back).Build();
        return [first, second, third];
    }

    /// <summary>An actor pays alike amounts to the exchange, <paramref name="gap"/> blocks apart.</summary>
    private static List<BitcoinTransaction> Split(string payer, long height, long amount, int count, int gap) =>
        [.. Enumerable.Range(0, count).Select(i => Tx($"{payer}-{i}", height + (i * gap))
            .In(Wpkh(payer), amount * 3).Out(Wpkh($"{payer}-exchange"), amount + (i * 1_000)).Build())];

    /// <summary>
    /// An ordinary, random graph: each transaction one address paying another a random amount (from 0.0001 to 10 BTC,
    /// spread evenly on a log scale) with change back, at a random later block; three addresses are labelled.
    /// </summary>
    private static (List<BitcoinTransaction> Txs, BitcoinAddress[] Labelled) RandomGraph(
        int seed, int addresses, int transactions)
    {
        var random = new Random(seed);
        var height = Start;
        var txs = new List<BitcoinTransaction>(transactions);
        for (var i = 0; i < transactions; i++)
        {
            height += random.Next(0, 4);
            var from = random.Next(addresses);
            var to = (from + 1 + random.Next(addresses - 1)) % addresses;
            var amount = (long)Math.Pow(10, 4 + (random.NextDouble() * 5));
            txs.Add(Tx($"g{seed}-{i}", height).In(Wpkh($"g{seed}-a{from}"), (amount * 3) + 10_000)
                .Out(Wpkh($"g{seed}-a{to}"), amount).Out(Wpkh($"g{seed}-a{from}"), amount * 2).Build());
        }

        return (txs, [Wpkh($"g{seed}-a0"), Wpkh($"g{seed}-a1"), Wpkh($"g{seed}-a2")]);
    }
}
