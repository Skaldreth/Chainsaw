#if DEBUG
namespace Chainsaw.App.TestData;

/// <summary>
/// The test case's chain, told as the money moves: the victim's payment, the scammer's consolidation and change, a
/// coinbase with OP_RETURN spent in a shared transaction, the distribution to a mixer, a CoinJoin, a peel chain through
/// shops to an exchange, a hub with many transactions, services of every sort and a new victim in the newest block;
/// and the obfuscation the checks recognise: dust, a look-alike address, many hops with a pause, a split whose parts
/// meet, a possible PayJoin, an exchange's sweeps into its hot wallet, a spend replaced by a higher fee, two victims'
/// money going the same way, a run of alike amounts and a payment to a swap service. Each transaction's label is how
/// the steps name it.
/// </summary>
public static class TestStoryChain
{
    /// <summary>The height the story starts at (the victim's payment).</summary>
    public const long Start = 921_000;

    /// <summary>
    /// The newest block, which has the new victim's payment that the watch catches: five days after the start, so that
    /// the money can lie still for days between two hops.
    /// </summary>
    public const long Tip = Start + 720;

    /// <summary>
    /// The block of the payment to the swap service whose continuation is suggested: two hours before the swap on
    /// Ethereum pays out ether (<see cref="TestEthereumChain.Swap"/>), within the analysis's window.
    /// </summary>
    public const long BridgeHeight = Start + 488;

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

    /// <summary>The mixer's less likely way out paid into a second mixer: mixer after mixer.</summary>
    public const string SecondMixerDeposit = "blandat-till-virveln";

    /// <summary>The CoinJoin (a Whirlpool mix).</summary>
    public const string CoinJoin = "whirlpool";

    /// <summary>The hub's payment to the services.</summary>
    public const string HubPayment = "navets-betalning";

    /// <summary>The new victim's payment in the newest block.</summary>
    public const string NewVictimPayment = "nytt-offer-betalar";

    /// <summary>The duster's transaction: tiny amounts to many addresses in the graph.</summary>
    public const string Dusting = "damm";

    /// <summary>The poisoner's tiny payment to the mixer's way out, from an address like Sydbörsen's deposit.</summary>
    public const string Poisoning = "forgiftning";

    /// <summary>The split of the CoinJoin output into parts of about the same size, after the hops.</summary>
    public const string Split = "uppdelning";

    /// <summary>The transaction where some of the parts meet again.</summary>
    public const string Join = "delarna-mots";

    /// <summary>A possible PayJoin: a part pays someone who adds a coin of its own.</summary>
    public const string PayJoin = "mojlig-payjoin";

    /// <summary>
    /// The scammer's spend of a CoinJoin output to a new deposit address at the exchange without KYC with a low fee,
    /// seen in a block that fell out of the chain and then replaced by <see cref="ReplacingSpend"/> (RBF): a
    /// transaction that is gone.
    /// </summary>
    public const string ReplacedSpend = "efter-whirlpool-forsta";

    /// <summary>
    /// The spend that replaced <see cref="ReplacedSpend"/>: the same coin with a higher fee, to the exchange's address
    /// the scammer used before.
    /// </summary>
    public const string ReplacingSpend = "efter-whirlpool";

    /// <summary>
    /// The scammer's later payment from another CoinJoin output to the deposit address the gone spend paid first,
    /// with change to a new address: the clustering takes the new address as change because the deposit address was
    /// seen before, in the gone spend, so that the conclusion rests on a transaction that is gone (sketch HB1a).
    /// </summary>
    public const string ReusedDeposit = "insattningen-igen";

    /// <summary>The peel chain's hops, each peeling a payment off and passing the rest on.</summary>
    public static readonly IReadOnlyList<string> PeelHops =
        [.. Enumerable.Range(1, 6).Select(index => $"skalning-hopp-{index}")];

    /// <summary>The hops a CoinJoin output of the scammer's passes through, one in and one out each, with a pause.</summary>
    public static readonly IReadOnlyList<string> PostMixHops =
        [.. Enumerable.Range(1, TestActors.HopCount).Select(index => $"efter-mix-hopp-{index}")];

    /// <summary>The customers' payments into Sydbörsen's deposits, swept into its hot wallet.</summary>
    public static readonly IReadOnlyList<string> SouthPayments =
        [.. Enumerable.Range(1, TestActors.SouthDeposits).Select(index => $"sydborsen-kund-betalar-{index}")];

    /// <summary>The two sweeps of Sydbörsen's deposits into its hot wallet.</summary>
    public static readonly IReadOnlyList<string> SouthSweeps = ["sydborsen-svep-1", "sydborsen-svep-2"];

    /// <summary>The mixer's payout to the two alike ways, the run of alike amounts and the payment to the swap.</summary>
    public const string PatternPayout = "monster-blandarens-utbetalning";

    /// <summary>The way out's payment to the swap service, whose continuation on Ethereum is suggested.</summary>
    public const string BridgePayment = "till-bryggan-betalar";

    /// <summary>The two victims' payments to the scammers whose money takes the same way.</summary>
    public static readonly IReadOnlyList<string> PatternVictimPayments =
        [.. Enumerable.Range(1, TestActors.PatternWays).Select(way => $"monster-offer-{way}-betalar")];

    /// <summary>
    /// Each alike way's hops after the mixer, way by way: the way out pays the first mule, the first mule the second,
    /// and the second pays Sydbörsen's deposit.
    /// </summary>
    public static readonly IReadOnlyList<IReadOnlyList<string>> PatternHops =
    [
        .. Enumerable.Range(1, TestActors.PatternWays).Select(way => (IReadOnlyList<string>)
            [.. Enumerable.Range(1, TestActors.PatternMulesPerWay + 1).Select(hop => $"monster-vag-{way}-hopp-{hop}")]),
    ];

    /// <summary>The alike amounts, in satoshi: within 1 % of the first and not round, as the rule reads them.</summary>
    private static readonly long[] StructuringAmounts = [9_480_000, 9_475_000, 9_490_000, 9_470_000];

    /// <summary>The run of alike amounts the way out pays into Sydbörsen's deposit, each spending the last's change.</summary>
    public static readonly IReadOnlyList<string> StructuringPayments =
        [.. Enumerable.Range(1, StructuringAmounts.Length).Select(index => $"likadana-belopp-{index}")];

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
        DustAndPoison(chain, funding, actors);
        SouthExchangeSweeps(chain, funding, actors);
        HopsAndParts(chain, funding, actors);
        Replacement(chain, actors);
        AlikeWays(chain, funding, actors);
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
    /// deposit, of the depositor's address type, and one 5 % less of another; the first goes on to an exchange, the
    /// second into another mixer.
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

        // The receiver adds a coin of its own and pays two equal deposits: a possible CoinJoin to the heuristics, so
        // that its two inputs are only proposed as one wallet, for the user to approve.
        var reserve = funding.Give(actors.MixerOut1Reserve, 21_600_000);
        chain.Tx("blandat-till-sydborsen", Start + 150).From(MixerPayout).From(reserve.Label)
            .Pay(actors.SouthExchange, 49_999_000)
            .Pay(actors.SouthExchange2, 49_999_000)
            .Done();
        chain.Tx(SecondMixerDeposit, Start + 160).From(MixerPayout, 1).Rest(actors.SecondMixer).Done();
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

    /// <summary>
    /// Dust and address poisoning (sketch DM1a): a duster sends 600 satoshi each to twelve addresses in the graph, and a
    /// poisoner sends a tiny amount to the mixer's way out from an address that looks like the Sydbörsen deposit it
    /// paid, in the hope that it copies the wrong one from its history next time.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="funding">Gives fresh money.</param>
    /// <param name="actors">The actors.</param>
    private static void DustAndPoison(TestChain chain, Funding funding, TestActors actors)
    {
        var dust = funding.Give(actors.Duster, 100_000);
        var draft = chain.Tx(Dusting, Start + 250).From(dust.Label);
        TestAddress[] dusted =
        [
            .. actors.CoinJoinOut, actors.Gambling, actors.Merchant, actors.GiftCard, actors.Atm, actors.Card,
            actors.MixerOut2, actors.Distributor,
        ];
        foreach (var address in dusted)
        {
            draft.Pay(address, 600);
        }

        draft.Rest(actors.Duster).Done();
        var poison = funding.Give(actors.LookAlike, 5_000);
        chain.Tx(Poisoning, Start + 158).From(poison.Label).Pay(actors.MixerOut1, 1_000)
            .Rest(actors.LookAlike, 500).Done();
    }

    /// <summary>
    /// Sydbörsen behaves as an exchange (sketch TJ1b): ten customers pay deposit addresses of their own over almost two
    /// days, and the deposits, with the two the mixer's way out paid, are swept into one hot wallet twice. The hot
    /// wallet has no label, so that the service is to be found from how it behaves.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="funding">Gives fresh money.</param>
    /// <param name="actors">The actors.</param>
    private static void SouthExchangeSweeps(TestChain chain, Funding funding, TestActors actors)
    {
        for (var index = 0; index < SouthPayments.Count; index++)
        {
            var customer = actors.SouthCustomers[index];
            var money = funding.Give(customer, 30_000_000);
            chain.Tx(SouthPayments[index], Start + 130 + (29 * index)).From(money.Label)
                .Pay(actors.SouthCustomerDeposits[index], 5_000_000 + (310_000 * index)).Rest(customer).Done();
        }

        var half = SouthPayments.Count / 2;
        var first = chain.Tx(SouthSweeps[0], Start + 300).From("blandat-till-sydborsen", 0);
        var second = chain.Tx(SouthSweeps[1], Start + 420).From("blandat-till-sydborsen", 1);
        for (var index = 0; index < SouthPayments.Count; index++)
        {
            (index < half ? first : second).From(SouthPayments[index]);
        }

        first.Rest(actors.SouthHot).Done();
        second.Rest(actors.SouthHot).Done();
    }

    /// <summary>
    /// A CoinJoin output of the scammer's passes through five hops of one in and one out, lying still for more than
    /// three days between the third and the fourth (sketch HP1a); then it is split into six parts of the same size,
    /// some of which meet again, one after a hop of its own, and one pays someone who adds a coin of their own: a
    /// possible PayJoin.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="funding">Gives fresh money.</param>
    /// <param name="actors">The actors.</param>
    private static void HopsAndParts(TestChain chain, Funding funding, TestActors actors)
    {
        long[] heights = [Start + 110, Start + 120, Start + 130, Start + 580, Start + 590];
        var (previous, index) = (CoinJoin, 1);
        for (var hop = 0; hop < PostMixHops.Count; hop++)
        {
            chain.Tx(PostMixHops[hop], heights[hop]).From(previous, index).Rest(actors.Hops[hop]).Done();
            (previous, index) = (PostMixHops[hop], 0);
        }

        // The CoinJoin's output less five fees, in six equal parts and a fee.
        var split = chain.Tx(Split, Start + 600).From(previous, index);
        foreach (var part in actors.Parts)
        {
            split.Pay(part, 164_000);
        }

        split.Done();
        chain.Tx("del-2-hoppar", Start + 610).From(Split, 1).Rest(actors.PartHop).Done();
        chain.Tx(Join, Start + 625).From(Split, 0).From("del-2-hoppar").From(Split, 3).Rest(actors.Joined).Done();

        // The payee's own coin is of a type no other input has, and its output of that type is the larger: the strong
        // signs of a PayJoin, so the clustering only proposes that the inputs are one wallet.
        var payee = funding.Give(actors.PayJoinPayee, 20_000);
        chain.Tx(PayJoin, Start + 635).From(Split, 2).From(payee.Label)
            .Pay(actors.PayJoinReceived, 120_000).Rest(actors.PayJoinChange).Done();
    }

    /// <summary>
    /// The scammer spends a CoinJoin output to a new deposit address at the exchange without KYC with too low a fee,
    /// and replaces it with a higher one (RBF) to the address used before, in the block before the newest. The first
    /// spend was seen in a block that fell out of the chain, so that it is gone and replaced (sketch GR9a,
    /// <em>Ersatt av</em>). In the newest block the scammer pays the new deposit address after all, from another
    /// CoinJoin output, with change: the change conclusion rests on the gone spend, where the address was first seen,
    /// and is held back once a run no longer knows it (sketch HB1a).
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="actors">The actors.</param>
    private static void Replacement(TestChain chain, TestActors actors)
    {
        chain.Tx(ReplacedSpend, Tip - 1).From(CoinJoin, 0).Rest(actors.NoKycDeposit, 300).Replaced();
        chain.Tx(ReplacingSpend, Tip - 1).From(CoinJoin, 0).Rest(actors.NoKycExchange, 3_000).Done();
        chain.Tx(ReusedDeposit, Tip).From(CoinJoin, 2).Pay(actors.NoKycDeposit, 500_000)
            .Rest(actors.ReusedDepositChange).Done();
    }

    /// <summary>
    /// The patterns <c>pattern v1</c> recognises, and a payment to a swap service whose continuation is suggested
    /// (<c>cross-chain v1</c>): two more victims pay two scammers, who deposit into the mixer; the mixer pays out from
    /// its own coins, and the two victims' money (less 2 %) goes the same way, through two mules each, to the same
    /// deposit address at Sydbörsen (<em>Samma väg</em>). The same payout gives a third way out that pays Sydbörsen's
    /// deposit four alike amounts in a row (<em>Likadana belopp</em>), and a fourth that pays the swap service about
    /// two hours before the swap on Ethereum pays out ether of about the same value.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="funding">Gives fresh money.</param>
    /// <param name="actors">The actors.</param>
    private static void AlikeWays(TestChain chain, Funding funding, TestActors actors)
    {
        long[] deposited = [50_000_000, 45_000_000];
        for (var way = 0; way < TestActors.PatternWays; way++)
        {
            var victim = actors.PatternVictims[way];
            var money = funding.Give(victim, 60_000_000);
            chain.Tx(PatternVictimPayments[way], Start + 170 + (5 * way)).From(money.Label)
                .Pay(actors.PatternScammers[way], deposited[way]).Rest(victim).Done();
            chain.Tx($"monster-bedragare-{way + 1}-till-blandaren", Start + 180 + (5 * way))
                .From(PatternVictimPayments[way]).Rest(actors.Mixer).Done();
        }

        // From the mixer's own coins: each way out is its deposit less 2 %, as a mixer's fee.
        var pool = funding.Give(actors.Mixer, 150_000_000);
        var payout = chain.Tx(PatternPayout, Start + 215).From(pool.Label);
        for (var way = 0; way < TestActors.PatternWays; way++)
        {
            payout.Pay(actors.PatternOuts[way], deposited[way] * 98 / 100);
        }

        payout.Pay(actors.Structurer, 38_120_000).Pay(actors.BridgePayer, 10_200_000).Rest(actors.Mixer).Done();
        for (var way = 0; way < TestActors.PatternWays; way++)
        {
            var (previous, index) = (PatternPayout, way);
            var hops = PatternHops[way];
            for (var hop = 0; hop < hops.Count; hop++)
            {
                var to = hop < TestActors.PatternMulesPerWay
                    ? actors.PatternMules[(way * TestActors.PatternMulesPerWay) + hop]
                    : actors.PatternDeposit;
                chain.Tx(hops[hop], Start + 230 + (15 * hop) + (5 * way)).From(previous, index).Rest(to).Done();
                (previous, index) = (hops[hop], 0);
            }
        }

        var (spent, output) = (PatternPayout, TestActors.PatternWays);
        for (var index = 0; index < StructuringPayments.Count; index++)
        {
            chain.Tx(StructuringPayments[index], Start + 280 + (20 * index)).From(spent, output)
                .Pay(actors.PatternDeposit, StructuringAmounts[index]).Rest(actors.Structurer).Done();
            (spent, output) = (StructuringPayments[index], 1);
        }

        chain.Tx(BridgePayment, BridgeHeight).From(PatternPayout, TestActors.PatternWays + 1).Rest(actors.Swap).Done();
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
