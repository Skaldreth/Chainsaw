#if DEBUG
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.TestData;

/// <summary>
/// One way to build a test case: the whole story, or a small part of it that shows one thing, so that a function can be
/// tried in a graph that is not full of everything else (the user's wish 2026-11-09). Every variant is built on the
/// same made-up chain, so everything that is not expanded from the start can still be expanded by hand.
/// </summary>
/// <param name="Number">The variant's number, as <c>/testdata -N</c> picks it; 0 is the whole story.</param>
/// <param name="Name">The variant's short name, in the case's title.</param>
/// <param name="Summary">What it shows and what to try in it, in plain Swedish.</param>
/// <param name="WithColleague">Whether a colleague's case is built first, for the label from another case.</param>
/// <param name="Steps">Gives the steps it is built with.</param>
public sealed record TestScenario(
    int Number, string Name, string Summary, bool WithColleague, Func<TestActors, IReadOnlyList<ITestStep>> Steps)
{
    /// <summary>Gets the argument that picks it: empty for the whole story, otherwise <c>-N</c>.</summary>
    public string Argument => Number == 0 ? string.Empty : $"-{Number}";
}

/// <summary>The test case's variants, from the whole story (0) to the small ones (<c>-1</c>, <c>-2</c> …).</summary>
public static class TestScenarios
{
    /// <summary>The variant with the whole story, built when <c>/testdata</c> is given nothing.</summary>
    public static TestScenario Whole { get; } = new(
        0, "allt", "Hela berättelsen: allt Chainsaw kan visa i ett ärende.", true, TestStory.Steps);

    /// <summary>Gets every variant, in number order.</summary>
    public static IReadOnlyList<TestScenario> All { get; } =
    [
        Whole,
        new(1, "grunden", "Bara offrets betalning som startpunkt. Expandera själv steg för steg.", false, Basics),
        new(2, "till börsen", "Pengarna från offret genom en peel chain till Nordbörsen: uttag och Begäran.", false,
            ToTheExchange),
        new(3, "mixern", "Pengarna in i en mixer, med förslag på troliga utgångar att godkänna eller avfärda.", false,
            Mixer),
        new(4, "bevakning", "Bedragarens plånbok bevakas, och ett nytt offer har just betalat in: en träff.", false,
            Watch),
        new(5, "förvirring",
            "Damm, en liknande adress, hopp med paus, uppdelningar som möts, mixer efter mixer och lager.", false,
            Obfuscation),
        new(6, "andra kedjor",
            "USDT på TRON, Ethereum med Uniswap och Tornado Cash, och Litecoin, Zcash, BSC och Solana.", false,
            OtherChains),
        new(7, "eget arbete", "Etiketter, en från kollegans ärende, uppbrytning, egen yta, kommentarer och soptunnan.",
            true, OwnWork),
        new(8, "block som byts", "Ett block som föll bort, en ersatt tx och en slutsats som hålls tillbaka.", false,
            ChangedBlocks),
    ];

    /// <summary>
    /// Gives the variant an argument picks: nothing or <c>0</c> for the whole story, <c>-N</c> or <c>N</c>.
    /// </summary>
    /// <param name="argument">What was written after <c>/testdata</c>.</param>
    /// <returns>The variant, or <see langword="null"/> when there is none by that number.</returns>
    public static TestScenario? Find(string? argument)
    {
        var written = (argument ?? string.Empty).Trim().TrimStart('-');
        if (written.Length == 0)
        {
            return Whole;
        }

        return int.TryParse(written, out var number) ? All.FirstOrDefault(scenario => scenario.Number == number) : null;
    }

    /// <summary>Gives the variants as lines to read: the argument, the name and what it shows.</summary>
    /// <returns>The text.</returns>
    public static string Listing() => string.Join(
        " ", All.Select(scenario => $"{(scenario.Number == 0 ? "/testdata" : scenario.Argument)} {scenario.Name}."));

    /// <summary>The victim's payment as the start point, with the victim's label.</summary>
    /// <param name="actors">The actors.</param>
    /// <returns>The steps.</returns>
    private static IReadOnlyList<ITestStep> Basics(TestActors actors) =>
    [
        new StartPointStep(TestStoryChain.VictimPayment),
        new LabelStep(actors.Victim, LabelKind.Victim, "Offret") { Own = true },
    ];

    /// <summary>The start, every label, and the money from the victim to the scammer's change.</summary>
    /// <param name="actors">The actors.</param>
    /// <returns>The steps.</returns>
    private static IEnumerable<ITestStep> Begin(TestActors actors) =>
    [
        new StartPointStep(TestStoryChain.VictimPayment),
        .. TestStory.Labels(actors),
        new ExpandStep(TestStoryChain.VictimPayment),
        new ExpandStep(TestStoryChain.Consolidation),
        new ExpandStep(TestStoryChain.ChangePayment),
        new ExpandStep(TestStoryChain.Distribution),
    ];

    /// <summary>The peel chain to Nordbörsen.</summary>
    /// <param name="actors">The actors.</param>
    /// <returns>The steps.</returns>
    private static IReadOnlyList<ITestStep> ToTheExchange(TestActors actors) =>
    [
        .. Begin(actors),
        .. TestStoryChain.PeelHops.SkipLast(1).Select(hop => new ExpandStep(hop)),
    ];

    /// <summary>The mixer's payout and the suggestions about it.</summary>
    /// <param name="actors">The actors.</param>
    /// <returns>The steps.</returns>
    private static IReadOnlyList<ITestStep> Mixer(TestActors actors) =>
    [
        .. Begin(actors),
        new ExpandStep(TestStoryChain.MixerPayout),
        new ChecksStep(),
    ];

    /// <summary>The scammer's consolidated wallet watched, with the new victim's payment as the hit.</summary>
    /// <param name="actors">The actors.</param>
    /// <returns>The steps.</returns>
    private static IReadOnlyList<ITestStep> Watch(TestActors actors) =>
    [
        new StartPointStep(TestStoryChain.VictimPayment),
        new LabelStep(actors.Victim, LabelKind.Victim, "Offret") { Own = true },
        new LabelStep(actors.Scammer, LabelKind.Unknown, "Bedragarens plånbok") { OnCluster = true, Own = true },
        new ExpandStep(TestStoryChain.VictimPayment),
        new ExpandStep(TestStoryChain.Consolidation),
        new WatchStep(actors.Consolidated, "Bedragarens andra plånbok", WatchDirection.In),
        new WatchCheckStep(),
    ];

    /// <summary>
    /// The hops after the CoinJoin, the split, the dust, the poisoning and the fifth round's patterns, and the checks.
    /// </summary>
    /// <param name="actors">The actors.</param>
    /// <returns>The steps.</returns>
    private static IReadOnlyList<ITestStep> Obfuscation(TestActors actors) =>
    [
        .. Begin(actors),
        new ExpandStep(TestStoryChain.MixerPayout),
        .. TestStoryChain.PostMixHops.Select(hop => new ExpandStep(hop)),
        new ExpandStep(TestStoryChain.Split),
        new ExpandStep(TestStoryChain.Dusting),
        new ExpandStep(TestStoryChain.Poisoning),
        .. TestStory.ObfuscationExpansions(actors),
        new ChecksStep(),
    ];

    /// <summary>Only the parts on the other chains: TRON, Ethereum, Litecoin, Zcash, BSC and Solana.</summary>
    /// <param name="actors">The actors.</param>
    /// <returns>The steps.</returns>
    private static IReadOnlyList<ITestStep> OtherChains(TestActors actors) =>
    [
        .. TestStory.OnTron(),
        .. TestStory.OnEthereum(),
        .. TestStory.OnOtherChains(),
    ];

    /// <summary>
    /// The user's own work: labels, the label from another case, a split, an area, comments and the trash.
    /// </summary>
    /// <param name="actors">The actors.</param>
    /// <returns>The steps.</returns>
    private static IReadOnlyList<ITestStep> OwnWork(TestActors actors) =>
    [
        .. Begin(actors),
        new SanctionCheckStep(),
        new OpenSourceCheckStep(),
        new ShareLabelsStep(),
        new SplitStep(actors.TopUp, "Påhittad uppbrytning: påfyllnaden kom från en tx som delades med andra."),
        new OwnAreaStep(
            "Uttagsvägar", [actors.Exchange, actors.NoKycExchange],
            "Påhittad egen yta: börserna där pengarna kan ha tagits ut."),
        new CommentStep(
            actors.Victim, TestStory.CommentText(actors, "skicka en `begäran`"), new NodePosition(16, -200)),
        new EditCommentStep(actors.Victim, TestStory.CommentText(actors, "skicka en `begäran` till Nordbörsen")),
        new ThrownCommentStep(
            "Påhittad kommentar som slängts: *gammal tanke om Sydbörsen*.", new NodePosition(360, -200)),
        new ThrownAreaStep(
            "Spelsajterna", [actors.Gambling, actors.GiftCard], "Påhittad egen yta som slängts i soptunnan."),
    ];

    /// <summary>
    /// A block that fell out under the cash-out, a spend replaced by a higher fee, and the change conclusion that rests
    /// on it held back, in the order the whole story builds them.
    /// </summary>
    /// <param name="actors">The actors.</param>
    /// <returns>The steps.</returns>
    private static IReadOnlyList<ITestStep> ChangedBlocks(TestActors actors) =>
    [
        new StartPointStep(TestStoryChain.VictimPayment),
        new LagStep(),
        new ReorganisationStep(TestStoryChain.PeelHops[^1], actors.Exchange),
        new ReplacedStep(TestStoryChain.ReplacedSpend),
        .. TestStory.Labels(actors),
        new ExpandStep(TestStoryChain.VictimPayment),
        new ExpandStep(TestStoryChain.Consolidation),
        new ExpandStep(TestStoryChain.ChangePayment),
        new ExpandStep(TestStoryChain.Distribution),
        .. TestStoryChain.PeelHops.SkipLast(1).Select(hop => new ExpandStep(hop)),
        new ExpandStep(TestStoryChain.ReusedDeposit),
        new GoneStep(TestStoryChain.ReplacedSpend),
        new ExpandStep(TestStoryChain.ReplacingSpend),
    ];
}
#endif
