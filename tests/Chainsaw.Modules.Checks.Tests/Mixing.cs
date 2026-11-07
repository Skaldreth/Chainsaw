using Chainsaw.Modules.Chains.Contracts;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>
/// The facit's mixer pattern: a victim pays 0.15 BTC into a mixer, and later payments in the graph that are and are not
/// likely outputs. A block is ten minutes; the deposit is in block 900 000.
/// </summary>
internal static class Mixing
{
    public const long DepositHeight = 900_000;

    public static BitcoinAddress Mixer { get; } = Wpkh("mixer-deposit");

    public static BitcoinAddress MixerOther { get; } = Wpkh("mixer-other");

    public static BitcoinAddress Victim { get; } = Wpkh("victim");

    /// <summary>The victim pays 0.15 BTC into the mixer and keeps the change.</summary>
    public static BitcoinTransaction Deposit() => Tx("deposit", DepositHeight)
        .In(Victim, 20_000_000)
        .Out(Mixer, 15_000_000)
        .Out(Wpkh("victim-change"), 4_990_000)
        .Build();

    /// <summary>A payment of an amount to an address, from someone else, some blocks after the deposit.</summary>
    public static BitcoinTransaction Payout(string label, BitcoinAddress to, long satoshi, long blocksAfter) =>
        Tx(label, DepositHeight + blocksAfter)
            .In(Wpkh(label + "-payer"), satoshi + 50_000)
            .Out(to, satoshi)
            .Build();

    /// <summary>Fee 2,5 %, four blocks (40 min) later, the victim's address type: the best candidate.</summary>
    public static BitcoinTransaction Best() => Payout("best", Wpkh("out-best"), 14_625_000, 4);

    /// <summary>Fee 5,3 %, a day later, the same type.</summary>
    public static BitcoinTransaction Second() => Payout("second", Wpkh("out-second"), 14_200_000, 144);

    /// <summary>Fee 7,3 %, three days later, P2PKH: another type.</summary>
    public static BitcoinTransaction Third() => Payout("third", Pkh("out-third"), 13_900_000, 432);

    /// <summary>More than came in: never a candidate.</summary>
    public static BitcoinTransaction TooMuch() => Payout("too-much", Wpkh("out-too-much"), 15_100_000, 6);

    /// <summary>A fee of 20 %: never a candidate.</summary>
    public static BitcoinTransaction TooLittle() => Payout("too-little", Wpkh("out-too-little"), 12_000_000, 6);

    /// <summary>Before the deposit: never a candidate.</summary>
    public static BitcoinTransaction Before() => Payout("before", Wpkh("out-before"), 14_700_000, -10);

    /// <summary>Ten days later, beyond the window: never a candidate.</summary>
    public static BitcoinTransaction TooLate() => Payout("too-late", Wpkh("out-too-late"), 14_700_000, 1_440);

    /// <summary>The victim spends its own change later: the depositor's own money, never a candidate.</summary>
    public static BitcoinTransaction VictimSpends(BitcoinTransaction deposit) => Tx("victim-spends", DepositHeight + 3)
        .Spend(deposit, 1)
        .Out(Wpkh("victim-shop"), 4_900_000)
        .Build();

    /// <summary>A payment into another of the mixer's addresses: the mixer's own, never a candidate.</summary>
    public static BitcoinTransaction ToMixer() => Payout("to-mixer", MixerOther, 14_600_000, 5);

    /// <summary>Everything above, in a mixed order.</summary>
    public static IReadOnlyList<BitcoinTransaction> All()
    {
        var deposit = Deposit();
        return
        [
            Third(), deposit, TooMuch(), Best(), TooLittle(), Before(), Second(), TooLate(), VictimSpends(deposit),
            ToMixer(),
        ];
    }
}
