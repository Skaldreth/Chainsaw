using Chainsaw.Modules.Chains.Contracts;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>
/// The facit's patterns for a mixer that pays a deposit out in parts and for mixer after mixer. The deposits are in
/// block 900 000 and a block is ten minutes; every address is synthetic.
/// </summary>
internal static class Parting
{
    public const long Height = 900_000;

    public static BitcoinAddress Mixer { get; } = Wpkh("parts-mixer");

    public static BitcoinAddress MixerAgain { get; } = Wpkh("parts-mixer-again");

    public static BitcoinAddress OtherMixer { get; } = Wpkh("other-mixer");

    /// <summary>A victim pays 0.15 BTC into the mixer.</summary>
    public static BitcoinTransaction Deposit() => Tx("parts-deposit", Height)
        .In(Wpkh("parts-victim"), 16_000_000)
        .Out(Mixer, 15_000_000)
        .Out(Wpkh("parts-victim-change"), 990_000)
        .Build();

    /// <summary>A payment of an amount from someone else, some blocks after the deposit.</summary>
    public static BitcoinTransaction Payout(string label, BitcoinAddress to, long satoshi, long blocksAfter) =>
        Tx(label, Height + blocksAfter)
            .In(Wpkh(label + "-payer"), satoshi + 50_000)
            .Out(to, satoshi)
            .Build();

    /// <summary>The first part: 0.06 BTC two hours later.</summary>
    public static BitcoinTransaction PartA() => Payout("part-a", Wpkh("part-a"), 6_000_000, 12);

    /// <summary>The second part: 0.05 BTC twelve hours later.</summary>
    public static BitcoinTransaction PartB() => Payout("part-b", Wpkh("part-b"), 5_000_000, 72);

    /// <summary>The third part: 0.037 BTC fifty hours later; the three are the deposit minus 2 %.</summary>
    public static BitcoinTransaction PartC() => Payout("part-c", Wpkh("part-c"), 3_700_000, 300);

    /// <summary>The three parts.</summary>
    public static IReadOnlyList<BitcoinTransaction> Parts() => [PartA(), PartB(), PartC()];

    /// <summary>
    /// Payouts of 0.14 BTC soon after the deposit, each a block apart: too large to combine with anything, but they
    /// take the places in the pool.
    /// </summary>
    public static IReadOnlyList<BitcoinTransaction> Crowd(int count) =>
    [
        .. Enumerable.Range(1, count).Select(i => Payout($"crowd-{i}", Wpkh($"crowd-{i}"), 14_000_000, i)),
    ];
}
