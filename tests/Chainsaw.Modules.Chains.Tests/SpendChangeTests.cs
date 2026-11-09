using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// The answer about who spends a transaction's outputs, compared with the case's last (etapp 15): another spender, the
/// same in another block, or unspent again is said; a first spend is no change. Synthetic answers.
/// </summary>
public sealed class SpendChangeTests
{
    private static readonly Txid First = Txid.Parse(Fixtures.Repeat("b1", 32));

    private static readonly Txid Second = Txid.Parse(Fixtures.Repeat("b2", 32));

    [Fact]
    public void A_first_spend_or_the_same_answer_is_no_change()
    {
        IReadOnlyList<BitcoinOutputSpend> unspent = [new(0, null)];
        IReadOnlyList<BitcoinOutputSpend> spent = [new(0, new BitcoinSpend(First, 0, Block(800_001, "c1")))];

        Assert.Null(EsploraBitcoinSource.SpendChange(unspent, spent));
        Assert.Null(EsploraBitcoinSource.SpendChange(spent, spent));
    }

    [Fact]
    public void Another_spender_another_block_or_unspent_again_is_said()
    {
        IReadOnlyList<BitcoinOutputSpend> before =
        [
            new(0, new BitcoinSpend(First, 0, Block(800_001, "c1"))),
            new(1, new BitcoinSpend(First, 1, Block(800_001, "c1"))),
            new(2, new BitcoinSpend(First, 2, Block(800_001, "c1"))),
        ];
        IReadOnlyList<BitcoinOutputSpend> now =
        [
            new(0, new BitcoinSpend(Second, 0, Block(800_002, "c2"))),
            new(1, new BitcoinSpend(First, 1, Block(800_002, "c2"))),
            new(2, null),
        ];

        var change = EsploraBitcoinSource.SpendChange(before, now)!;

        Assert.Contains($"utgång 0 spenderades av {First} men nu av {Second}", change, StringComparison.Ordinal);
        Assert.Contains("utgång 1 spenderas av samma tx, men den låg i block 800001 och ligger nu i block 800002",
            change, StringComparison.Ordinal);
        Assert.Contains($"utgång 2 spenderades av {First} men är nu ospenderad", change, StringComparison.Ordinal);
    }

    /// <summary>Makes a block reference with a hash from a repeated byte.</summary>
    private static BlockReference Block(long height, string fill) =>
        new(height, BlockHash.Parse(Fixtures.Repeat(fill, 32)), DateTimeOffset.UnixEpoch);
}
