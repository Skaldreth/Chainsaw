#if DEBUG
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.TestData;

/// <summary>
/// The test case's story as data: the steps that build it, in order. Fill it in when something new is built, so that
/// all of Chainsaw can be seen in one case without looking for an address that has everything.
/// </summary>
/// <remarks>
/// Some parts are expanded from the start and some are left for the user to expand: the hub (more than 50
/// transactions, so some are held back, and many counterparties), the victim's address (paid out from an exchange),
/// the CoinJoin's outputs and the scammer's second wallet (the new victim's payment that the watch caught). The
/// user's own work is there too, so that its marks and history can be seen at once: a split, decided suggestions, an
/// approved wallet, an edited comment, and a comment and an area in the trash. Everything is made up and nothing goes
/// out: the chain, the rates and the sanction list are answered on this computer.
/// </remarks>
public static class TestStory
{
    /// <summary>The story's version, in the journal and as the made-up labels' source version.</summary>
    public const string Version = "2";

    /// <summary>The made-up open source the test case's labels come from.</summary>
    public const string LabelSource = "testdata (påhittad)";

    /// <summary>Why every made-up label, link and area was made.</summary>
    public const string LabelReason = "Påhittad i testärendet (/testdata), bara för att visa funktionen.";

    /// <summary>The test case's title.</summary>
    public const string Title = "Testärende: påhittade data";

    /// <summary>The colleague's case's title.</summary>
    public const string ColleagueTitle = "Kollegans ärende: påhittade data";

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

            // Before anything else looks the cash-out up, so that the first answer is the orphaned block's.
            new ReorganisationStep(TestStoryChain.PeelHops[^1], actors.Exchange),
            .. Labels(actors),
            .. Expansions(),
            new SanctionCheckStep(),
            new ShareLabelsStep(),
            new LinkOtherChainStep(TestStoryChain.HubPayment, SwapOutput, "eth:bytarens-swap"),
            new WatchStep(actors.Consolidated, "Bedragarens andra plånbok", WatchDirection.In),
            new WatchCheckStep(),
            new ChecksStep(),
            new DecideStep(actors.MixerOut1, LabelReason),
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
        new LabelStep(actors.Nested, LabelKind.NestedService, "Inbäddade tjänsten"),
        new LabelStep(actors.Otc, LabelKind.OtcP2P, "Torgväxlaren"),
        new LabelStep(actors.Atm, LabelKind.CryptoAtm, "Automaten Centralen"),
        new LabelStep(actors.Card, LabelKind.CryptoCard, "Kryptokortet"),
        new LabelStep(actors.Gambling, LabelKind.Gambling, "Lyckohjulet"),
        new LabelStep(actors.Merchant, LabelKind.Merchant, "Elbutiken"),
        new LabelStep(actors.GiftCard, LabelKind.GiftCard, "Kortkungen"),
        new LabelStep(actors.Darknet, LabelKind.Darknet, "Skuggtorget"),
        new LabelStep(actors.Darknet, LabelKind.Sanctioned, "Skuggtorget"),
        new LabelStep(actors.Mixer, LabelKind.Mixer, "Blandaren"),
        new LabelStep(actors.SecondMixer, LabelKind.Mixer, "Virveln"),

        // A made-up former sanction beside the mixer, as Tornado Cash's on Ethereum: the toned-down mark.
        new LabelStep(actors.Mixer, LabelKind.FormerlySanctioned, "Påhittad lista 2022-08-08 – 2025-03-21"),
        new LabelStep(actors.Swap, LabelKind.SwapBridge, "Bytaren"),

        // A supplier's report of fraud (as from Chainabuse): a red mark beside the node's own sort.
        new LabelStep(actors.Scammer, LabelKind.Reported, "Nätfiske"),
    ];

    /// <summary>
    /// Gives the expansions made from the start: the money from the victim through the consolidation, the change, the
    /// distribution (which brings the mixer's payout, the CoinJoin and the hub's payment), the mixer's payout (on to an
    /// exchange and into a second mixer), the whole peel chain, and the miner's reward with the shared transaction.
    /// </summary>
    /// <returns>The steps.</returns>
    private static IEnumerable<ITestStep> Expansions() =>
    [
        new ExpandStep(TestStoryChain.VictimPayment),
        new ExpandStep(TestStoryChain.Consolidation),
        new ExpandStep(TestStoryChain.ChangePayment),
        new ExpandStep(TestStoryChain.Distribution),
        new ExpandStep(TestStoryChain.MixerPayout),
        .. TestStoryChain.PeelHops.SkipLast(1).Select(hop => new ExpandStep(hop)),
        new ExpandStep(TestStoryChain.MinerReward),
    ];
}
#endif
