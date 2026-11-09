#if DEBUG
namespace Chainsaw.App.TestData;

/// <summary>
/// The fifth round's patterns in the test case's chain (DEV-LOG, <em>Omgång 5</em>), each built so that its check
/// recognises it: a central mixer without a label (<c>mixer-behaviour</c>), a deposit into Blandaren paid out in two
/// parts that go on into Virveln and into the central mixer, whose way out goes into Virveln too (<c>mixer-chain</c>
/// with branches, <c>mixer-parts</c>), a split spread over four transactions whose parts partly meet again
/// (<c>split-merge</c>), a row of hops where every other pays a layering service's fee (<c>pass-through</c>) and a
/// nested service that runs its customers' money through an account at Nordbörsen (<c>nested-service</c>).
/// </summary>
public static partial class TestStoryChain
{
    /// <summary>The central mixer's coins, split from one reward so that each payout spends a coin of its own.</summary>
    public const string PoolCoins = "malstrommen-kassa";

    /// <summary>The chain's payer's deposit into Blandaren, where the chain of mixers starts.</summary>
    public const string ChainDeposit = "kedja-in-blandaren";

    /// <summary>Blandaren's payout of the first part, from coins of its own.</summary>
    public const string ChainBranchAPayout = "kedja-gren-a-ut";

    /// <summary>Blandaren's payout of the second part, from other coins of its own, four hours later.</summary>
    public const string ChainBranchBPayout = "kedja-gren-b-ut";

    /// <summary>The second part's deposit into the central mixer without a label.</summary>
    public const string ChainIntoPool = "kedja-gren-b-in-malstrommen";

    /// <summary>The central mixer's payout of the second part's money, less its fee.</summary>
    public const string ChainPoolPayout = "malstrommen-ut-kedjan";

    /// <summary>The fourth spread part's hop before it meets the first.</summary>
    public const string SpreadPartHop = "spridd-del-4-hoppar";

    /// <summary>The transaction where the first and the fourth spread parts meet again.</summary>
    public const string SpreadJoin = "spridda-delar-mots";

    /// <summary>The nested service's two payments into its account at Nordbörsen.</summary>
    public static readonly IReadOnlyList<string> NestedTrips = ["vaxelkiosken-till-borsen-1", "vaxelkiosken-till-borsen-2"];

    /// <summary>The central mixer's customers' deposits, in order.</summary>
    public static readonly IReadOnlyList<string> PoolDeposits =
        [.. Enumerable.Range(1, TestActors.PoolCustomers).Select(index => $"malstrommen-in-{index}")];

    /// <summary>The spread split's transactions, two parts in each, an hour apart.</summary>
    public static readonly IReadOnlyList<string> SpreadPayments =
        [.. Enumerable.Range(1, TestActors.SpreadParts / 2).Select(index => $"spridning-{index}")];

    /// <summary>The layering row's hops, in order.</summary>
    public static readonly IReadOnlyList<string> FeeHops =
        [.. Enumerable.Range(1, TestActors.FeeHopCount).Select(index => $"skiktning-hopp-{index}")];

    /// <summary>
    /// The central mixer's customers' deposits, in satoshi: each about 1.35 times the one before, so that no deposit
    /// scaled for the baseline (0.8, 0.88, 1.12, 1.25) lands near another's payout.
    /// </summary>
    private static readonly long[] PoolAmounts =
        [400_000, 540_000, 729_000, 984_000, 1_329_000, 1_794_000, 2_421_000, 3_269_000, 4_413_000];

    /// <summary>The central mixer's fee for each customer, in thousandths: varied, as a mixer varies it.</summary>
    private static readonly int[] PoolFees = [12, 31, 22, 38, 15, 27, 35, 18, 24];

    /// <summary>
    /// How many blocks after each deposit the central mixer pays it out: from an hour to most of a day, varied, and all
    /// before the chain's deposit into Blandaren, so that none of them is weighed as one of its parts.
    /// </summary>
    private static readonly int[] PoolDelays = [8, 40, 95, 20, 130, 60, 12, 70, 45];

    /// <summary>The chain's deposit into Blandaren, in satoshi.</summary>
    private const long ChainDeposited = 30_000_000;

    /// <summary>Blandaren's first part: together with the second the deposit less 2 %.</summary>
    private const long ChainPartA = 17_900_000;

    /// <summary>Blandaren's second part.</summary>
    private const long ChainPartB = 11_500_000;

    /// <summary>The central mixer's payout of the second part: what it deposited less about 2.3 %.</summary>
    private const long ChainPoolPaid = 11_234_000;

    /// <summary>Builds the fifth round's patterns.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="funding">Gives fresh money.</param>
    /// <param name="actors">The actors.</param>
    private static void Obfuscation(TestChain chain, Funding funding, TestActors actors)
    {
        CentralMixer(chain, funding, actors);
        MixerAfterMixer(chain, funding, actors);
        SpreadSplit(chain, funding, actors);
        LayeringFees(chain, funding, actors);
        NestedService(chain, funding, actors);
    }

    /// <summary>
    /// A central mixer without a label: nine customers pay its pool over two days, and each is paid back an hour to most
    /// of a day later, less a fee of 1–4 %, from coins of the pool's own (split from one reward), to a fresh address.
    /// The coin for the chain's second part is split off with them.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="funding">Gives fresh money.</param>
    /// <param name="actors">The actors.</param>
    private static void CentralMixer(TestChain chain, Funding funding, TestActors actors)
    {
        var paid = PoolAmounts.Select((amount, index) => amount - (amount * PoolFees[index] / 1_000)).ToList();
        var reward = funding.Give(actors.Pool, paid.Sum() + ChainPoolPaid + 1_000_000);
        var coins = chain.Tx(PoolCoins, Start + 290).From(reward.Label);
        foreach (var amount in paid.Append(ChainPoolPaid))
        {
            coins.Pay(actors.Pool, amount + TestChain.DefaultFee);
        }

        coins.Rest(actors.Pool).Done();
        for (var index = 0; index < TestActors.PoolCustomers; index++)
        {
            var height = Start + 300 + (30 * index);
            var money = funding.Give(actors.PoolDepositors[index], PoolAmounts[index] + TestChain.DefaultFee);
            chain.Tx(PoolDeposits[index], height).From(money.Label).Rest(actors.Pool).Done();
            chain.Tx($"malstrommen-ut-{index + 1}", height + PoolDelays[index]).From(PoolCoins, index)
                .Rest(actors.PoolRecipients[index]).Done();
        }
    }

    /// <summary>
    /// Mixer after mixer with branches: a payer deposits into Blandaren, which pays the deposit out less 2 % in two
    /// parts from coins of its own; the first part goes into Virveln, the second into the central mixer without a
    /// label, whose way out for it goes into Virveln as well.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="funding">Gives fresh money.</param>
    /// <param name="actors">The actors.</param>
    private static void MixerAfterMixer(TestChain chain, Funding funding, TestActors actors)
    {
        var payer = funding.Give(actors.ChainPayer, ChainDeposited + TestChain.DefaultFee);
        chain.Tx(ChainDeposit, Start + 600).From(payer.Label).Rest(actors.Mixer).Done();

        // Each part from a coin of its own, so that the two are independent payouts that may be combined.
        var coinA = funding.Give(actors.Mixer, ChainPartA + 500_000);
        chain.Tx(ChainBranchAPayout, Start + 612).From(coinA.Label).Pay(actors.ChainBranchA, ChainPartA)
            .Rest(actors.Mixer).Done();
        var coinB = funding.Give(actors.Mixer, ChainPartB + 500_000);
        chain.Tx(ChainBranchBPayout, Start + 636).From(coinB.Label).Pay(actors.ChainBranchB, ChainPartB)
            .Rest(actors.Mixer).Done();
        chain.Tx("kedja-gren-a-in-virveln", Start + 650).From(ChainBranchAPayout).Rest(actors.SecondMixer).Done();
        chain.Tx(ChainIntoPool, Start + 645).From(ChainBranchBPayout).Rest(actors.Pool).Done();
        chain.Tx(ChainPoolPayout, Start + 680).From(PoolCoins, TestActors.PoolCustomers).Rest(actors.ChainOut).Done();
        chain.Tx("kedja-ut-in-virveln", Start + 700).From(ChainPoolPayout).Rest(actors.SecondMixer).Done();
    }

    /// <summary>
    /// A split spread over several transactions: within four hours the spreader pays eight recipients about 0.02 BTC
    /// each, two per transaction, each spending the change of the one before; two of the parts later meet again, one
    /// after a hop of its own.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="funding">Gives fresh money.</param>
    /// <param name="actors">The actors.</param>
    private static void SpreadSplit(TestChain chain, Funding funding, TestActors actors)
    {
        var money = funding.Give(actors.Spreader, 20_000_000);
        var (spent, output) = (money.Label, 0);
        for (var payment = 0; payment < SpreadPayments.Count; payment++)
        {
            var draft = chain.Tx(SpreadPayments[payment], Start + 400 + (6 * payment)).From(spent, output);
            foreach (var part in new[] { 2 * payment, (2 * payment) + 1 })
            {
                draft.Pay(actors.SpreadPartsTo[part], 2_000_000 + (5_000 * part));
            }

            draft.Rest(actors.Spreader).Done();
            (spent, output) = (SpreadPayments[payment], 2);
        }

        // The fourth part passes a hop of its own first, so that spending the two together makes the hop's address,
        // not the fourth part's, one owner with the first part's: the eight parts stay eight recipients.
        chain.Tx(SpreadPartHop, Start + 440).From(SpreadPayments[1], 1).Rest(actors.SpreadPartHop).Done();
        chain.Tx(SpreadJoin, Start + 450).From(SpreadPayments[0], 0).From(SpreadPartHop)
            .Rest(actors.SpreadJoined).Done();
    }

    /// <summary>
    /// A row of hops through a layering service: each hop passes almost all of a coin on to a new address, and every
    /// other hop also pays one percent to the service's fee address, as a service that charges per hop does. The plain
    /// hops between keep the row from being read as a peel chain.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="funding">Gives fresh money.</param>
    /// <param name="actors">The actors.</param>
    private static void LayeringFees(TestChain chain, Funding funding, TestActors actors)
    {
        var holding = 40_000_000L;
        var money = funding.Give(actors.FeeHolders[0], holding);
        var spent = money.Label;
        for (var hop = 0; hop < FeeHops.Count; hop++)
        {
            var draft = chain.Tx(FeeHops[hop], Start + 460 + (12 * hop)).From(spent);
            var fee = hop % 2 == 0 ? holding / 100 : 0;
            holding -= fee + TestChain.DefaultFee;
            draft.Pay(actors.FeeHolders[hop + 1], holding);
            if (fee > 0)
            {
                draft.Pay(actors.LayeringFee, fee);
            }

            draft.Done();
            spent = FeeHops[hop];
        }
    }

    /// <summary>
    /// A nested service at Nordbörsen: nine customers pay a small exchange without a label over two days, which twice
    /// pays what it took into its account at Nordbörsen and twice takes a withdrawal back from Nordbörsen's hot wallet,
    /// from which it pays nine payees of its own; Nordbörsen then sweeps the account into its hot wallet.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="funding">Gives fresh money.</param>
    /// <param name="actors">The actors.</param>
    private static void NestedService(TestChain chain, Funding funding, TestActors actors)
    {
        var customers = new List<string>();
        for (var index = 0; index < TestActors.NestedCustomers; index++)
        {
            var label = $"vaxelkiosken-kund-{index + 1}-betalar";
            var money = funding.Give(actors.NestedCustomersFrom[index], 5_000_000 + (130_000 * index) + 2_000);
            chain.Tx(label, Start + 250 + (40 * index)).From(money.Label).Rest(actors.NestedKiosk).Done();
            customers.Add(label);
        }

        (int From, int Count, long Height)[] trips = [(0, 4, Start + 420), (4, 5, Start + 600)];
        for (var trip = 0; trip < trips.Length; trip++)
        {
            var draft = chain.Tx(NestedTrips[trip], trips[trip].Height);
            foreach (var customer in customers.Skip(trips[trip].From).Take(trips[trip].Count))
            {
                draft.From(customer);
            }

            draft.Rest(actors.NestedAccount).Done();
            var hot = funding.Give(actors.ExchangeHot, 100_000_000);
            var withdrawal = $"nordborsen-uttag-till-kiosken-{trip + 1}";
            chain.Tx(withdrawal, trips[trip].Height + 20).From(hot.Label).Pay(actors.NestedKiosk, 50_000_000)
                .Rest(actors.ExchangeHot).Done();
            var payees = chain.Tx($"vaxelkiosken-betalar-{trip + 1}", trips[trip].Height + 40).From(withdrawal);
            foreach (var payee in Enumerable.Range(trips[trip].From, trips[trip].Count))
            {
                payees.Pay(actors.NestedPayees[payee], 6_000_000 + (500_000 * payee));
            }

            payees.Rest(actors.NestedKiosk).Done();
        }

        chain.Tx("nordborsen-sveper-kontot", Start + 680).From(NestedTrips[0]).From(NestedTrips[1])
            .From("nordborsen-uttag-till-kiosken-2", 1).Rest(actors.ExchangeHot).Done();
    }
}
#endif
