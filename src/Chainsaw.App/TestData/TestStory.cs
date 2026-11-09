#if DEBUG
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.TestData;

/// <summary>
/// The test case's story as data: the steps that build it, in order. Fill it in when something new is built, so that
/// all of Chainsaw can be seen in one case without looking for an address that has everything.
/// </summary>
/// <remarks>
/// Some parts are expanded from the start and some are left for the user to expand: the hub (more than 50
/// transactions, so some are held back, and many counterparties), the victim's address (paid out from an exchange),
/// the CoinJoin's outputs and the scammer's second wallet (the new victim's payment that the watch caught). The
/// obfuscation the checks recognise is there: dust, a look-alike of Sydbörsen's deposit, many hops with a pause, a
/// split whose parts meet, a possible PayJoin, a spend that is gone and replaced (with a change conclusion that rests on
/// it and is held back), Sydbörsen's sweeps into a hot wallet without a label, two victims' money going the same way
/// through the mixer to Sydbörsen, a run of alike amounts, and a payment to the swap service whose continuation on
/// Ethereum is suggested; and the fifth round's patterns: a central mixer without a label, mixer after mixer with two
/// branches through it, a split spread over four transactions, hops that pay a layering service's fee and a nested
/// service at Nordbörsen. Besides Bitcoin, TRON and Ethereum, small parts on Litecoin, Zcash (with money into the
/// shielded pool and out again), BSC and Solana each reach Sydbörsen there. The user's own work is there too, so that
/// its marks and history can be seen at once: a split, decided suggestions, an approved wallet, an edited comment, a
/// label on a transaction, and a comment and an area in the trash. Everything is made up and nothing goes out: the chain, the rates, the sanction lists and the
/// open source are answered on this computer.
/// </remarks>
public static class TestStory
{
    /// <summary>The story's version, in the journal and as the made-up labels' source version.</summary>
    public const string Version = "7";

    /// <summary>The made-up open source the test case's labels come from.</summary>
    public const string LabelSource = "testdata (påhittad)";

    /// <summary>Why every made-up label, link and area was made.</summary>
    public const string LabelReason = "Påhittad i testärendet (/testdata), bara för att visa funktionen.";

    /// <summary>The test case's title.</summary>
    public const string Title = "Testärende: påhittade data";

    /// <summary>The colleague's case's title.</summary>
    public const string ColleagueTitle = "Kollegans ärende: påhittade data";

    /// <summary>The names of the two victims whose money goes the same way through the mixer to Sydbörsen.</summary>
    private static readonly string[] PatternVictimNames = ["Fjärde offret", "Femte offret"];

    /// <summary>The output of the hub's payment that goes to the swap service.</summary>
    private const int SwapOutput = 4;

    /// <summary>Gives the steps the colleague's case is built with: the labels that come to the test case from it.</summary>
    /// <param name="actors">The actors.</param>
    /// <returns>The steps.</returns>
    public static IReadOnlyList<ITestStep> ColleagueSteps(TestActors actors)
    {
        ArgumentNullException.ThrowIfNull(actors);
        return
        [
            new LabelStep(actors.Escrow, LabelKind.EscrowMarket, "Garantitorget") { Own = true, Spreads = true },
            new LabelStep(actors.Exchange, LabelKind.Exchange, "Nordbörsen") { Own = true, Spreads = true },
        ];
    }

    /// <summary>Gives the steps the test case is built with.</summary>
    /// <param name="actors">The actors.</param>
    /// <returns>The steps, in order.</returns>
    public static IReadOnlyList<ITestStep> Steps(TestActors actors)
    {
        ArgumentNullException.ThrowIfNull(actors);
        return
        [
            new StartPointStep(TestStoryChain.VictimPayment),

            // Before anything is looked up, so that the newest block comes between the answers that a spend is gone.
            new LagStep(),

            // Before anything else looks the cash-out up, so that the first answer is the orphaned block's.
            new ReorganisationStep(TestStoryChain.PeelHops[^1], actors.Exchange),
            new ReplacedStep(TestStoryChain.ReplacedSpend),
            .. Labels(actors),
            .. Expansions(actors),

            // After the expansions, so that the change conclusion of the reused deposit rests on the replaced spend;
            // the expansion of the spend that replaced it then holds that conclusion back by itself (sketch HB1a).
            new GoneStep(TestStoryChain.ReplacedSpend),
            new ExpandStep(TestStoryChain.ReplacingSpend),
            new SanctionCheckStep(),
            new OpenSourceCheckStep(),
            new TransactionLabelStep(TestStoryChain.Join, LabelKind.SwapBridge, "Bytaren (samlad insättning)"),
            new ShareLabelsStep(),
            new LinkOtherChainStep(
                TestStoryChain.HubPayment, SwapOutput,
                new ChainTransactionRef(Chain.Tron, TestTronChain.Id(TestTronChain.SwapPayout))),
            .. OnTron(),
            .. OnEthereum(),
            .. OnOtherChains(),
            new WatchStep(actors.Consolidated, "Bedragarens andra plånbok", WatchDirection.In),
            new WatchCheckStep(),
            new ChecksStep(),
            new DecideStep(actors.MixerOut1, TestStoryChain.Distribution, LabelReason),
            new SplitStep(actors.TopUp, "Påhittad uppbrytning: påfyllnaden kom från en tx som delades med andra."),
            new OwnAreaStep(
                "Uttagsvägar", [actors.Exchange, actors.NoKycExchange],
                "Påhittad egen yta: börserna där pengarna kan ha tagits ut."),
            new CommentStep(actors.Victim, CommentText(actors, "skicka en `begäran`"), new NodePosition(16, -200)),
            new EditCommentStep(actors.Victim, CommentText(actors, "skicka en `begäran` till Nordbörsen")),
            new ThrownCommentStep(
                "Påhittad kommentar som slängts: *gammal tanke om Sydbörsen*.", new NodePosition(360, -200)),
            new ThrownAreaStep(
                "Spelsajterna", [actors.Gambling, actors.GiftCard], "Påhittad egen yta som slängts i soptunnan."),
        ];
    }

    /// <summary>
    /// Gives the made-up comment (sketch KN1a): a heading, bold, italic, a list, code and a <c>chainsaw:</c> link to
    /// the exchange's deposit, so that everything the note draws can be seen.
    /// </summary>
    /// <param name="actors">The actors.</param>
    /// <param name="request">The list's second line, which the edit changes so that the comment has a history.</param>
    /// <returns>The Markdown.</returns>
    private static string CommentText(TestActors actors, string request) => string.Join(
        '\n',
        "## Uttag via Nordbörsen?",
        "Påhittad kommentar ur /testdata: pengarna från **offret** ser ut att nå *Nordbörsen*.",
        "- kontrollera insättningen",
        $"- {request} när uttaget är klart",
        $"[Nordbörsens insättning](chainsaw:bitcoin/{actors.Exchange.Address?.Value})");

    /// <summary>
    /// Gives the labels: one of every sort in the graph's visual language, a darknet market that is also sanctioned
    /// (the mark), and the scammer's wallet by name. The sanctioned actor's label comes from the made-up sanction
    /// list instead (<see cref="SanctionCheckStep"/>).
    /// </summary>
    /// <param name="actors">The actors.</param>
    /// <returns>The steps.</returns>
    private static IEnumerable<ITestStep> Labels(TestActors actors) =>
    [
        new LabelStep(actors.Victim, LabelKind.Victim, "Offret") { Own = true },
        new LabelStep(actors.Scammer, LabelKind.Unknown, "Bedragarens plånbok") { OnCluster = true, Own = true },
        new LabelStep(actors.ExchangeHot, LabelKind.Exchange, "Nordbörsen (het plånbok)"),
        new LabelStep(actors.Exchange, LabelKind.Exchange, "Nordbörsen"),
        new LabelStep(actors.SouthExchange, LabelKind.Exchange, "Sydbörsen"),
        new LabelStep(actors.SouthExchange2, LabelKind.Exchange, "Sydbörsen"),
        new LabelStep(actors.NoKycExchange, LabelKind.NoKycExchange, "Snabbväxlaren"),
        new LabelStep(actors.NoKycDeposit, LabelKind.NoKycExchange, "Snabbväxlaren"),
        new LabelStep(actors.Nested, LabelKind.NestedService, "Inbäddade tjänsten"),
        new LabelStep(actors.Otc, LabelKind.OtcP2P, "Torgväxlaren"),
        new LabelStep(actors.Atm, LabelKind.CryptoAtm, "Automaten Centralen"),
        new LabelStep(actors.Card, LabelKind.CryptoCard, "Kryptokortet"),
        new LabelStep(actors.Gambling, LabelKind.Gambling, "Lyckohjulet"),
        new LabelStep(actors.Merchant, LabelKind.Merchant, "Elbutiken"),
        new LabelStep(actors.GiftCard, LabelKind.GiftCard, "Kortkungen"),
        new LabelStep(actors.Darknet, LabelKind.Darknet, "Skuggtorget"),
        new LabelStep(actors.Mixer, LabelKind.Mixer, "Blandaren"),
        new LabelStep(actors.SecondMixer, LabelKind.Mixer, "Virveln"),

        new LabelStep(actors.Swap, LabelKind.SwapBridge, "Bytaren"),

        // The two victims whose money goes the same way, so that the pattern rules never count them as actors.
        .. actors.PatternVictims.Select((victim, index) =>
            new LabelStep(victim, LabelKind.Victim, PatternVictimNames[index]) { Own = true }),
        new LabelStep(actors.PatternDeposit, LabelKind.Exchange, "Sydbörsen"),

        // A supplier's report of fraud (as from Chainabuse): a red mark beside the node's own sort.
        new LabelStep(actors.Scammer, LabelKind.Reported, "Nätfiske"),
    ];

    /// <summary>
    /// Gives the part on TRON (round 8): the swap service's payout of USDT to the scammer, labelled as the made-up
    /// open source names it, the scammer's TRON address as a start point, and the scammer and the mule expanded, so
    /// that USDT reaches Sydbörsen's deposit on TRON with its snowflake (Tether can freeze it there).
    /// </summary>
    /// <returns>The steps.</returns>
    private static IEnumerable<ITestStep> OnTron() =>
    [
        new AccountLabelStep(TestTronChain.Swap, LabelKind.SwapBridge, "Bytaren (TRON)"),
        new AccountLabelStep(TestTronChain.Exchange, LabelKind.Exchange, "Sydbörsen (TRON)"),
        new AccountStartPointStep(TestTronChain.Scammer),
        new AccountExpandStep(TestTronChain.Scammer),
        new AccountExpandStep(TestTronChain.Mule),
    ];

    /// <summary>
    /// Gives the part on Ethereum: the scammer's Ethereum address as a start point, expanded, so that the victim's USDT,
    /// the swap to ether through Uniswap's router (its pill with the router and the price check) and the ether into
    /// Tornado Cash can be seen; the known contracts are then labelled, so that Tornado Cash is a mixer and a former
    /// sanction, and the scammer's USDT left over has its snowflake.
    /// </summary>
    /// <returns>The steps.</returns>
    private static IEnumerable<ITestStep> OnEthereum() =>
    [
        new AccountStartPointStep(TestEthereumChain.Scammer),
        new AccountExpandStep(TestEthereumChain.Scammer),
        new KnownContractsStep(),
    ];

    /// <summary>
    /// Gives the parts on Litecoin, Zcash, BSC and Solana (round 8b and 8c): on each a victim's payment to the scammer
    /// is a start point, or the scammer's address, expanded until the money reaches Sydbörsen's deposit there, which is
    /// labelled. On Zcash the scammer's money goes into the shielded pool and almost the same amount comes out half a
    /// day later, which is expanded too, so that <c>shielded-pool v1</c> suggests it as the way out.
    /// </summary>
    /// <returns>The steps.</returns>
    private static IEnumerable<ITestStep> OnOtherChains() =>
    [
        new LabelStep(TestLitecoinChain.Exchange, LabelKind.Exchange, "Sydbörsen (Litecoin)"),
        new TransactionStartPointStep(TestLitecoinChain.TxidOf(TestLitecoinChain.VictimPayment)),
        new TransactionExpandStep(TestLitecoinChain.TxidOf(TestLitecoinChain.VictimPayment)),
        new TransactionExpandStep(TestLitecoinChain.TxidOf(TestLitecoinChain.ToMule)),
        new LabelStep(TestZcashChain.Exchange, LabelKind.Exchange, "Sydbörsen (Zcash)"),
        new TransactionStartPointStep(TestZcashChain.TxidOf(TestZcashChain.VictimPayment)),
        new TransactionExpandStep(TestZcashChain.TxidOf(TestZcashChain.VictimPayment)),
        new TransactionExpandStep(TestZcashChain.TxidOf(TestZcashChain.OutOfPool)),
        new AccountLabelStep(TestBscChain.Exchange, LabelKind.Exchange, "Sydbörsen (BSC)"),
        new AccountStartPointStep(TestBscChain.Scammer),
        new AccountExpandStep(TestBscChain.Scammer),
        new AccountLabelStep(TestSolanaChain.Exchange, LabelKind.Exchange, "Sydbörsen (Solana)"),
        new AccountStartPointStep(TestSolanaChain.Scammer),
        new AccountExpandStep(TestSolanaChain.Scammer),
        new AccountExpandStep(TestSolanaChain.Mule),
    ];

    /// <summary>
    /// Gives the expansions made from the start: the money from the victim through the consolidation, the change, the
    /// distribution (which brings the mixer's payout, the CoinJoin and the hub's payment), the mixer's payout (on to an
    /// exchange and into a second mixer), the whole peel chain, the miner's reward with the shared transaction, the
    /// hops after the CoinJoin with the split and the parts that meet, the dust, the poisoning, and the customers'
    /// payments into Sydbörsen with the sweeps that empty them.
    /// </summary>
    /// <param name="actors">The actors.</param>
    /// <returns>The steps.</returns>
    private static IEnumerable<ITestStep> Expansions(TestActors actors) =>
    [
        new ExpandStep(TestStoryChain.VictimPayment),
        new ExpandStep(TestStoryChain.Consolidation),
        new ExpandStep(TestStoryChain.ChangePayment),
        new ExpandStep(TestStoryChain.Distribution),
        new ExpandStep(TestStoryChain.MixerPayout),
        .. TestStoryChain.PeelHops.SkipLast(1).Select(hop => new ExpandStep(hop)),
        new ExpandStep(TestStoryChain.MinerReward),
        .. TestStoryChain.PostMixHops.Select(hop => new ExpandStep(hop)),
        new ExpandStep(TestStoryChain.Split),
        new ExpandStep(TestStoryChain.Dusting),
        new ExpandStep(TestStoryChain.Poisoning),
        .. TestStoryChain.SouthPayments.Select(payment => new ExpandStep(payment)),
        .. PatternExpansions(),
        .. ObfuscationExpansions(actors),
        new ExpandStep(TestStoryChain.ReusedDeposit),
    ];

    /// <summary>
    /// Gives the expansions of the fifth round's patterns (<see cref="TestStoryChain"/>, <c>Obfuscation</c>): the
    /// central mixer's pool with its whole business, the chain's deposit into Blandaren and its two parts onward (which
    /// bring the deposits into Virveln and the central mixer) and the central mixer's way out for it, the spread
    /// split's payments (which bring the parts that meet), the layering row's hops, and the nested service with its
    /// first trip to Nordbörsen (which brings Nordbörsen's sweep of the account).
    /// </summary>
    /// <param name="actors">The actors.</param>
    /// <returns>The steps.</returns>
    private static IEnumerable<ITestStep> ObfuscationExpansions(TestActors actors) =>
    [
        new AddressExpandStep(actors.Pool),
        new ExpandStep(TestStoryChain.ChainDeposit),
        new ExpandStep(TestStoryChain.ChainBranchAPayout),
        new ExpandStep(TestStoryChain.ChainBranchBPayout),
        new ExpandStep(TestStoryChain.ChainPoolPayout),
        .. TestStoryChain.SpreadPayments.SkipLast(1).Select(payment => new ExpandStep(payment)),
        .. TestStoryChain.FeeHops.SkipLast(1).Select(hop => new ExpandStep(hop)),
        new AddressExpandStep(actors.NestedKiosk),
        new ExpandStep(TestStoryChain.NestedTrips[0]),
    ];

    /// <summary>
    /// Gives the expansions of the patterns' part (<see cref="TestStoryChain"/>, <c>AlikeWays</c>): the two victims'
    /// payments (which bring the deposits into the mixer), the mixer's payout (which brings each way's first hop, the
    /// first alike amount and the payment to the swap service), the hops before each way's last, and the alike amounts
    /// before the last.
    /// </summary>
    /// <returns>The steps.</returns>
    private static IEnumerable<ITestStep> PatternExpansions() =>
    [
        .. TestStoryChain.PatternVictimPayments.Select(payment => new ExpandStep(payment)),
        new ExpandStep(TestStoryChain.PatternPayout),
        .. TestStoryChain.PatternHops.SelectMany(hops => hops.SkipLast(1)).Select(hop => new ExpandStep(hop)),
        .. TestStoryChain.StructuringPayments.SkipLast(1).Select(payment => new ExpandStep(payment)),
    ];
}
#endif
