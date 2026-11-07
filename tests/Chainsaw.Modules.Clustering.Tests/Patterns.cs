using Chainsaw.Modules.Chains.Contracts;
using static Chainsaw.Modules.Clustering.Tests.Synthetic;

namespace Chainsaw.Modules.Clustering.Tests;

/// <summary>
/// The facit's transaction patterns. Each one is built from synthetic addresses and says in its name what it is;
/// the tests say what must be merged and what must not.
/// </summary>
internal static class Patterns
{
    /// <summary>
    /// A payment with change: two P2WPKH inputs pay a round amount to a P2PKH shop, and the odd rest, smaller than
    /// every input, goes back to a new P2WPKH address.
    /// </summary>
    public static BitcoinTransaction PaymentWithChange() => Tx("payment")
        .In(Wpkh("payer-1"), 3_000_000)
        .In(Wpkh("payer-2"), 2_500_000)
        .Out(Pkh("shop"), 5_000_000)
        .Out(Wpkh("payer-change"), 487_650)
        .Build();

    /// <summary>A payment where only one sign (optimal change) tells the outputs apart.</summary>
    public static BitcoinTransaction PaymentWithWeakSigns() => Tx("weak")
        .In(Wpkh("weak-1"), 1_000_000)
        .In(Wpkh("weak-2"), 800_000)
        .Out(Wpkh("weak-x"), 1_234_567)
        .Out(Wpkh("weak-y"), 543_210)
        .Build();

    /// <summary>A consolidation: six inputs into one output.</summary>
    public static BitcoinTransaction Consolidation() =>
        Enumerable.Range(1, 6)
            .Aggregate(Tx("consolidation"), (tx, i) => tx.In(Wpkh($"cons-{i}"), 1_000_000 + (i * 7_777)))
            .Out(Wpkh("cons-target"), 6_100_000)
            .Build();

    /// <summary>Five users each pay into their own deposit address at an exchange, keeping their change.</summary>
    public static IReadOnlyList<BitcoinTransaction> Deposits() =>
    [
        .. Enumerable.Range(1, 5).Select(i => Tx($"deposit-{i}", 900_000 + i)
            .In(Wpkh($"user-{i}"), 10_000_000 + (i * 1_000))
            .Out(Wpkh($"deposit-{i}"), 5_000_000)
            .Out(Wpkh($"user-{i}-change"), 4_990_000 + (i * 1_000) - 2_000)
            .Build()),
    ];

    /// <summary>The exchange sweeps the five deposits into its hot wallet.</summary>
    public static BitcoinTransaction Sweep(IReadOnlyList<BitcoinTransaction> deposits) =>
        deposits.Aggregate(Tx("sweep", 900_010), (tx, deposit) => tx.Spend(deposit, 0))
            .Out(Pkh("hot-wallet"), 24_990_000)
            .Build();

    /// <summary>A batch payout from the hot wallet: ten customers and the change, from one input.</summary>
    public static BitcoinTransaction BatchPayout()
    {
        var tx = Tx("batch").In(Pkh("hot-wallet"), 100_000_000);
        for (var i = 1; i <= 10; i++)
        {
            var address = i % 2 == 0 ? Wpkh($"customer-{i}") : Pkh($"customer-{i}");
            tx.Out(address, 1_000_000 + (i * 123_457));
        }

        return tx.Out(Pkh("hot-wallet-change"), 82_000_000).Build();
    }

    /// <summary>A batch payout of three equal amounts from one input, with change.</summary>
    public static BitcoinTransaction BatchOfEqualAmounts() => Tx("equal-batch")
        .In(Wpkh("payer-batch"), 50_000_000)
        .Out(Wpkh("equal-1"), 1_000_000)
        .Out(Wpkh("equal-2"), 1_000_000)
        .Out(Wpkh("equal-3"), 1_000_000)
        .Out(Wpkh("payer-batch-change"), 46_990_000)
        .Build();

    /// <summary>A Whirlpool mix: five inputs, five outputs of the 0.01 BTC pool.</summary>
    public static BitcoinTransaction Whirlpool()
    {
        var tx = Tx("whirlpool");
        for (var i = 1; i <= 5; i++)
        {
            tx.In(Wpkh($"whirl-in-{i}"), i <= 2 ? 1_005_000 : 1_000_000);
        }

        for (var i = 1; i <= 5; i++)
        {
            tx.Out(Wpkh($"whirl-out-{i}"), 1_000_000);
        }

        return tx.Build();
    }

    /// <summary>A Wasabi 1 round: 30 inputs, 25 outputs of 0.1 BTC, the coordinator's fee and change.</summary>
    public static BitcoinTransaction Wasabi1()
    {
        var tx = Tx("wasabi1");
        for (var i = 0; i < 30; i++)
        {
            tx.In(Wpkh($"wasabi1-in-{i}"), 12_000_000 + (i * 37_000));
        }

        for (var i = 0; i < 25; i++)
        {
            tx.Out(Wpkh($"wasabi1-mixed-{i}"), 10_000_000);
        }

        tx.Out(Wpkh("wasabi1-coordinator"), 1_500_000);
        for (var i = 0; i < 5; i++)
        {
            tx.Out(Wpkh($"wasabi1-change-{i}"), 24_000_000 + (i * 1_111));
        }

        return tx.Build();
    }

    /// <summary>A Wasabi 2 round: 40 inputs and outputs in groups of standard amounts, P2TR and P2WPKH.</summary>
    public static BitcoinTransaction Wasabi2()
    {
        var tx = Tx("wasabi2");
        for (var i = 0; i < 40; i++)
        {
            tx.In(i % 2 == 0 ? Tr($"wasabi2-in-{i}") : Wpkh($"wasabi2-in-{i}"), 2_300_000 + (i * 1_000));
        }

        var groups = new (long Amount, int Count)[] { (2_097_152, 8), (5_000_000, 6), (1_000_000, 5), (1_594_323, 4) };
        var n = 0;
        foreach (var (amount, count) in groups)
        {
            for (var i = 0; i < count; i++, n++)
            {
                tx.Out(n % 2 == 0 ? Tr($"wasabi2-out-{n}") : Wpkh($"wasabi2-out-{n}"), amount);
            }
        }

        return tx.Out(Tr("wasabi2-single-1"), 10_000_000)
            .Out(Tr("wasabi2-single-2"), 11_000_000)
            .Out(Wpkh("wasabi2-single-3"), 13_000_000)
            .Build();
    }

    /// <summary>A JoinMarket round: four participants, seven inputs, four equal outputs, three makers' change.</summary>
    public static BitcoinTransaction JoinMarket()
    {
        var tx = Tx("joinmarket");
        for (var i = 0; i < 7; i++)
        {
            tx.In(Wpkh($"jm-in-{i}"), 20_000_000);
        }

        for (var i = 0; i < 4; i++)
        {
            tx.Out(Wpkh($"jm-equal-{i}"), 12_345_678);
        }

        for (var i = 0; i < 3; i++)
        {
            tx.Out(Wpkh($"jm-change-{i}"), 30_000_000);
        }

        return tx.Build();
    }

    /// <summary>Two inputs from different places and two equal outputs: perhaps a small CoinJoin.</summary>
    public static BitcoinTransaction PossibleCoinJoin() => Tx("possible")
        .In(Wpkh("possible-1"), 3_000_000)
        .In(Wpkh("possible-2"), 2_000_000)
        .Out(Wpkh("possible-out-1"), 2_400_000)
        .Out(Wpkh("possible-out-2"), 2_400_000)
        .Build();

    /// <summary>Two inputs and two equal outputs of dust beside a real payment: no CoinJoin.</summary>
    public static BitcoinTransaction EqualDust() => Tx("dust")
        .In(Wpkh("dust-1"), 3_000_000)
        .In(Wpkh("dust-2"), 2_000_000)
        .Out(Wpkh("dust-out-1"), 1_000)
        .Out(Wpkh("dust-out-2"), 1_000)
        .Out(Pkh("dust-payee"), 4_900_000)
        .Build();

    /// <summary>
    /// A peel chain: each hop has one input, peels a round amount off to a P2PKH address and sends the rest on to a
    /// new P2WPKH address (always output 1), which the next hop spends.
    /// </summary>
    /// <param name="hops">How many hops.</param>
    /// <returns>The hops, in order.</returns>
    public static IReadOnlyList<BitcoinTransaction> PeelChain(int hops)
    {
        var chain = new List<BitcoinTransaction>();
        var builder = Tx("peel-0", 900_100).In(Wpkh("peel-holder-0"), 100_000_000);
        long holding = 100_000_000;
        for (var i = 0; i < hops; i++)
        {
            var peel = 1_500_000 + (i * 100_000);
            holding = holding - peel - 10_000;
            var hop = builder.Out(Pkh($"peel-recipient-{i}"), peel).Out(Wpkh($"peel-holder-{i + 1}"), holding).Build();
            chain.Add(hop);
            builder = Tx($"peel-{i + 1}", 900_101 + i).Spend(hop, 1);
        }

        return chain;
    }
}
