#if DEBUG
namespace Chainsaw.App.TestData;

/// <summary>
/// The test case's chain, told as the money moves: the victim's payment, the scammer's consolidation and change, a
/// coinbase with OP_RETURN spent in a shared transaction, the distribution to a mixer, a CoinJoin, a peel chain through
/// shops to an exchange, a hub with many transactions, services of every sort and a new victim in the newest block.
/// Each transaction's label is how the steps name it.
/// </summary>
public static class TestStoryChain
{
    /// <summary>The height the story starts at (the victim's payment).</summary>
    public const long Start = 921_000;

    /// <summary>The newest block, which has the new victim's payment that the watch catches.</summary>
    public const long Tip = Start + 300;

    /// <summary>The lowest block: the coinbases that give everyone their first money come before the story.</summary>
    public const long First = Start - 200;

    /// <summary>The victim's payment to the scammer: the start point.</summary>
    public const string VictimPayment = "offrets-betalning";

    /// <summary>The scammer's consolidation of three victims' money.</summary>
    public const string Consolidation = "konsolidering";

    /// <summary>The payment after the consolidation, with change back to the scammer.</summary>
    public const string ChangePayment = "betalning-med-vaxel";

    /// <summary>The coinbase with a coin without an address and an OP_RETURN.</summary>
    public const string MinerReward = "gruvbelöning";

    /// <summary>The transaction where the miner's coin and another owner's are spent together.</summary>
    public const string Shared = "delad";

    /// <summary>The distribution to the mixer, the CoinJoin, the peel chain, the exchange without KYC and the hub.</summary>
    public const string Distribution = "fordelning";

    /// <summary>The mixer's payout.</summary>
    public const string MixerPayout = "blandarens-utbetalning";

    /// <summary>The CoinJoin (a Whirlpool mix).</summary>
    public const string CoinJoin = "whirlpool";

    /// <summary>The hub's payment to the services.</summary>
    public const string HubPayment = "navets-betalning";

    /// <summary>The new victim's payment in the newest block.</summary>
    public const string NewVictimPayment = "nytt-offer-betalar";

    /// <summary>The peel chain's hops, each peeling a payment off and passing the rest on.</summary>
    public static readonly IReadOnlyList<string> PeelHops =
        [.. Enumerable.Range(1, 6).Select(index => $"skalning-hopp-{index}")];

    /// <summary>The first block's time.</summary>
    private static readonly DateTimeOffset FirstTime = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Builds the chain and seals it.</summary>
    /// <param name="actors">The story's actors.</param>
    /// <returns>The chain.</returns>
    public static TestChain Build(TestActors actors)
    {
        ArgumentNullException.ThrowIfNull(actors);
        var chain = new TestChain(First, FirstTime);
        var funding = new Funding(chain, First);
        Victims(chain, funding, actors);
        Scammer(chain, funding, actors);
        Mixer(chain, funding, actors);
        CoinJoinRound(chain, funding, actors);
        PeelChain(chain, actors);
        HubAndServices(chain, funding, actors);
        chain.Seal(Tip);
        return chain;
    }

    /// <summary>The victims pay the scammer; the victim was paid out from an exchange first.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="funding">Gives fresh money.</param>
    /// <param name="actors">The actors.</param>
    private static void Victims(TestChain chain, Funding funding, TestActors actors)
    {
        var hot = funding.Give(actors.ExchangeHot, 5_000_000_000);
        chain.Tx("uttag-till-offret", Start - 50).From(hot.Label).Pay(actors.Victim, 200_000_000)
            .Rest(actors.ExchangeHot).Done();
        chain.Tx(VictimPayment, Start).From("uttag-till-offret").Pay(actors.Scammer, 150_000_000)
            .Rest(actors.Victim).Done();
        var second = funding.Give(actors.Victim2, 50_000_000);
        chain.Tx("offer-2-betalar", Start + 5).From(second.Label).Pay(actors.Scammer2, 40_000_000)
            .Rest(actors.Victim2).Done();
        var third = funding.Give(actors.Victim3, 70_000_000);
        chain.Tx("offer-3-betalar", Start + 8).From(third.Label).Pay(actors.Scammer3, 60_000_000)
            .Rest(actors.Victim3).Done();
        var newVictim = funding.Give(actors.NewVictim, 30_000_000);
        chain.Tx(NewVictimPayment, Tip).From(newVictim.Label).Pay(actors.Consolidated, 25_000_000)
            .Rest(actors.NewVictim).Done();
    }

    /// <summary>
    /// The scammer consolidates the three victims' money (clustered by common inputs), tops it up with a coin from a
    /// shared transaction that spends a miner's reward, and pays on with change (clustered as change).
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="funding">Gives fresh money.</param>
    /// <param name="actors">The actors.</param>
    private static void Scammer(TestChain chain, Funding funding, TestActors actors)
    {
        chain.Tx(Consolidation, Start + 20).From(VictimPayment).From("offer-2-betalar").From("offer-3-betalar")
            .Rest(actors.Consolidated).Done();
        chain.Coinbase(
            MinerReward, Start - 70, new TestOutput(actors.Miner, 312_500_000), new TestOutput(actors.Commitment, 0));
        var coOwner = funding.Give(actors.CoOwner, 50_000_000);
        chain.Tx(Shared, Start + 28).From(MinerReward).From(coOwner.Label).Pay(actors.TopUp, 1_000_000)
            .Rest(actors.CoOwner).Done();

        // A round payment to another address type, and change smaller than every input: three signs of change.
        chain.Tx(ChangePayment, Start + 40).From(Consolidation).From(Shared).Pay(actors.Distributor, 250_000_000)
            .Rest(actors.Change).Done();
        chain.Tx(Distribution, Start + 60).From(ChangePayment)
            .Pay(actors.Mixer, 80_000_000)
            .Pay(actors.CoinJoinIn, 1_000_300)
            .Pay(actors.Peel[0], 100_000_000)
            .Pay(actors.NoKycExchange, 30_000_000)
            .Rest(actors.Hub, 3_000)
            .Done();
    }

    /// <summary>
    /// The mixer pays out five hours after the deposit, from its own coins: one way out about 2 % less than the
    /// deposit, of the depositor's address type, and one 5 % less of another; the first goes on to an exchange.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="funding">Gives fresh money.</param>
    /// <param name="actors">The actors.</param>
    private static void Mixer(TestChain chain, Funding funding, TestActors actors)
    {
        var customer = funding.Give(actors.MixerDepositor, 120_000_000);
        chain.Tx("blandarens-kassa", Start - 40).From(customer.Label).Pay(actors.Mixer, 100_000_000)
            .Rest(actors.MixerDepositor).Done();
        chain.Tx(MixerPayout, Start + 90).From(Distribution, 0).From("blandarens-kassa")
            .Pay(actors.MixerOut1, 78_400_000)
            .Pay(actors.MixerOut2, 76_000_000)
            .Rest(actors.Mixer).Done();
        chain.Tx("blandat-till-sydborsen", Start + 150).From(MixerPayout).Rest(actors.SouthExchange).Done();
    }

    /// <summary>A Whirlpool mix: five inputs of the pool's amount with its fee, five equal outputs.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="funding">Gives fresh money.</param>
    /// <param name="actors">The actors.</param>
    private static void CoinJoinRound(TestChain chain, Funding funding, TestActors actors)
    {
        var draft = chain.Tx(CoinJoin, Start + 100).From(Distribution, 1);
        foreach (var other in actors.CoinJoinOthers)
        {
            draft.From(funding.Give(other, 1_000_300).Label);
        }

        foreach (var output in actors.CoinJoinOut)
        {
            draft.Pay(output, 1_000_000);
        }

        draft.Done();
    }

    /// <summary>
    /// A peel chain: each hop pays a shop or a service a little and passes the rest to a new address, until the rest
    /// lands at an exchange.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="actors">The actors.</param>
    private static void PeelChain(TestChain chain, TestActors actors)
    {
        (TestAddress To, long Value)[] peeled =
        [
            (actors.Gambling, 5_000_000), (actors.Merchant, 8_000_000), (actors.GiftCard, 6_000_000),
            (actors.Atm, 10_000_000), (actors.Card, 5_000_000),
        ];
        var (previous, index) = (Distribution, 2);
        for (var hop = 0; hop < peeled.Length; hop++)
        {
            chain.Tx(PeelHops[hop], Start + 70 + (8 * hop)).From(previous, index)
                .Pay(peeled[hop].To, peeled[hop].Value)
                .Rest(actors.Peel[hop + 1]).Done();
            (previous, index) = (PeelHops[hop], 1);
        }

        chain.Tx(PeelHops[^1], Start + 110).From(previous, index).Rest(actors.Exchange).Done();
    }

    /// <summary>
    /// The hub pays the services in one transaction, and has many more transactions with many counterparties, so that
    /// expanding it holds some back.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="funding">Gives fresh money.</param>
    /// <param name="actors">The actors.</param>
    private static void HubAndServices(TestChain chain, Funding funding, TestActors actors)
    {
        chain.Tx(HubPayment, Start + 75).From(Distribution, 4)
            .Pay(actors.Darknet, 5_000_000)
            .Pay(actors.Escrow, 4_000_000)
            .Pay(actors.Otc, 6_000_000)
            .Pay(actors.Nested, 5_000_000)
            .Pay(actors.Swap, 10_000_000)
            .Pay(actors.Sanctioned, 3_000_000)
            .Rest(actors.Hub).Done();
        for (var index = 0; index < actors.HubIn.Count; index++)
        {
            var sender = actors.HubIn[index];
            var money = funding.Give(sender, 2_000_000);
            chain.Tx($"navet-in-{index + 1}", Start + 120 + index).From(money.Label)
                .Pay(actors.Hub, 100_000 + (7_000 * index)).Rest(sender).Done();
        }

        for (var index = 0; index < actors.HubOut.Count; index++)
        {
            chain.Tx($"navet-ut-{index + 1}", Start + 170 + index).From($"navet-in-{index + 1}")
                .Rest(actors.HubOut[index], 1_000).Done();
        }
    }

    /// <summary>Gives fresh money, each as a coinbase in a block of its own before the story.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="height">The first block to use.</param>
    private sealed class Funding(TestChain chain, long height)
    {
        /// <summary>The next block to use.</summary>
        private long _next = height;

        /// <summary>Gives an address fresh money.</summary>
        /// <param name="to">The address.</param>
        /// <param name="value">The amount in satoshi.</param>
        /// <returns>The coinbase, whose output 0 is the money.</returns>
        public TestTransaction Give(TestAddress to, long value) => chain.Fund(to, value, _next++);
    }
}
#endif
