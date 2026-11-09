using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>The Guide's sections that the graph's <em>Varför?</em> links to (the texts in <c>docs/guide</c>).</summary>
public enum GuideTopic
{
    /// <summary><em>Startpunkter</em>.</summary>
    StartPoints,

    /// <summary><em>Spårningsmetoder: FIFO och haircut</em>.</summary>
    Methods,

    /// <summary><em>Varför ett spår tar slut</em>.</summary>
    TrailEnds,

    /// <summary><em>Etiketter och varifrån de kommer</em>.</summary>
    Labels,

    /// <summary>The common-input-ownership heuristic.</summary>
    CommonInputOwnership,

    /// <summary>The change-address heuristic.</summary>
    ChangeAddress,

    /// <summary>Peel chains.</summary>
    PeelChain,

    /// <summary>CoinJoin.</summary>
    CoinJoin,

    /// <summary><em>UTXO-modellen</em>.</summary>
    Utxo,

    /// <summary><em>Grafen och bildspråket</em>.</summary>
    Graph,

    /// <summary><em>Kontroller och förslag</em> (sketch KF1a).</summary>
    Checks,

    /// <summary><em>Områden i grafen</em> (sketch KF1b).</summary>
    Areas,

    /// <summary><em>Mixerns troliga utgångar</em>.</summary>
    MixerCandidates,

    /// <summary><em>Mixer efter mixer och delarna</em>: a deposit paid out in parts, and mixer after mixer.</summary>
    MixerChain,

    /// <summary><em>Kontomodellen</em>: how Ethereum keeps balances, and how they are traced.</summary>
    AccountModel,

    /// <summary><em>ERC-20 och tokens</em>: tokens, their marks and swaps.</summary>
    Tokens,

    /// <summary><em>Kan frysas</em>: USDT and USDC that their issuer can freeze.</summary>
    CanBeFrozen,

    /// <summary><em>Kurser och omräkning</em>: the value at the time, in SEK.</summary>
    Prices,

    /// <summary><em>Så spårar man i Chainsaw</em>: a case's course, from the empty graph (sketch GR6c).</summary>
    HowToTrace,

    /// <summary><em>Exponering</em>: which services a cluster paid to or got from (sketch EP1b).</summary>
    Exposure,
    /// <summary>
    /// <em>Hur Chainsaw kontrollerar svaren mot kedjan</em>: the answers checked against the chain, and a transaction
    /// that left its block (sketch GR9a).
    /// </summary>
    ChainCheck,

    /// <summary><em>Spåret genom en swap</em>: the traced value follows, and the choice per swap (sketch SW1a).</summary>
    Swaps,
    /// <summary><em>Vyn Tidslinje</em>: a lane per actor and the transfers in time (sketch TL3a).</summary>
    TimelineView,

    /// <summary><em>Flödet</em>: the money as bands from the victims, hop by hop (sketch FL1a).</summary>
    Flow,

    /// <summary><em>Tjänster ur beteende</em>: deposits swept to a hot wallet and payouts in a batch.</summary>
    ServiceBehaviour,

    /// <summary><em>Uppdelning i många små belopp</em>: an amount split into parts that meet again.</summary>
    Splitting,

    /// <summary><em>Fördröjning och många hopp</em>: money that only passes through, and lies still between.</summary>
    Layering,

    /// <summary><em>Damm och adressförgiftning</em>: tiny amounts sent to link addresses or to fool the victim.</summary>
    Dust,

    /// <summary><em>PayJoin</em>: two owners' inputs in one transaction, which the clustering cannot see.</summary>
    PayJoin,

    /// <summary><em>Tidsmönster</em>: the hours an owner is active, and the time zone they hint at.</summary>
    TimePattern,

    /// <summary><em>Ankaret och flera datorer</em>: why an anchor was replaced (sketch AK1a).</summary>
    Anchors,

    /// <summary>
    /// <em>En tx som inte finns längre</em>: a transaction the source no longer knows, where the trail ends, and what
    /// replaced it (sketch GR9a).
    /// </summary>
    GoneTransaction,

    /// <summary>
    /// <em>Fortsättningen på en annan kedja</em>: where money into a bridge or a swap continues, linked by hand or
    /// suggested by <c>cross-chain v1</c>.
    /// </summary>
    CrossChain,
}

/// <summary>Reads <see cref="GuideTopic"/>.</summary>
public static class GuideTopics
{
    /// <summary>The graph's heading that explains the inspector and <em>Varför?</em>.</summary>
    public const string InspectorHeading = "Inspektören och Varför?";

    /// <summary>The heading of the ends that can still be followed.</summary>
    public const string OpenEndsHeading = "Inte slut än";

    /// <summary>The heading of the real ends of a trail.</summary>
    public const string RealEndsHeading = "Riktiga slut";

    /// <summary>Gives the section's file in <c>docs/guide</c>, without <c>.md</c>.</summary>
    /// <param name="topic">The section.</param>
    /// <returns>The file's name.</returns>
    public static string FileName(this GuideTopic topic) => topic switch
    {
        GuideTopic.StartPoints => "startpunkter",
        GuideTopic.Methods => "sparningsmetoder",
        GuideTopic.TrailEnds => "sparet-tar-slut",
        GuideTopic.Labels => "etiketter",
        GuideTopic.CommonInputOwnership => "common-input-ownership",
        GuideTopic.ChangeAddress => "vaxeladress",
        GuideTopic.PeelChain => "peel-chain",
        GuideTopic.CoinJoin => "coinjoin",
        GuideTopic.Graph => "grafen",
        GuideTopic.Checks => "kontroller-och-forslag",
        GuideTopic.Areas => "omraden",
        GuideTopic.MixerCandidates => "mixerns-kandidater",
        GuideTopic.MixerChain => "mixer-efter-mixer",
        GuideTopic.AccountModel => "kontomodellen",
        GuideTopic.Tokens => "erc-20",
        GuideTopic.CanBeFrozen => "kan-frysas",
        GuideTopic.Prices => "kurser",
        GuideTopic.HowToTrace => "sa-sparar-man",
        GuideTopic.Exposure => "exponering",
        GuideTopic.ChainCheck => "kontroll-mot-kedjan",
        GuideTopic.Swaps => "swappar",
        GuideTopic.TimelineView => "vyn-tidslinje",
        GuideTopic.Flow => "flodet",
        GuideTopic.ServiceBehaviour => "tjanster-ur-beteende",
        GuideTopic.Splitting => "uppdelning",
        GuideTopic.Layering => "fordrojning-och-hopp",
        GuideTopic.Dust => "damm-och-forgiftning",
        GuideTopic.PayJoin => "payjoin",
        GuideTopic.TimePattern => "tidsmonster",
        GuideTopic.Anchors => "ankaret",
        GuideTopic.GoneTransaction => "ersatt-tx",
        GuideTopic.CrossChain => "annan-kedja",
        _ => "utxo-modellen",
    };

    /// <summary>Gives the section's title, as the link says it.</summary>
    /// <param name="topic">The section.</param>
    /// <returns>The title, in Swedish.</returns>
    public static string Title(this GuideTopic topic) => topic switch
    {
        GuideTopic.StartPoints => "Startpunkter",
        GuideTopic.Methods => "Spårningsmetoder",
        GuideTopic.TrailEnds => "Varför ett spår tar slut",
        GuideTopic.Labels => "Etiketter",
        GuideTopic.CommonInputOwnership => "Common-input-ownership",
        GuideTopic.ChangeAddress => "Växeladress",
        GuideTopic.PeelChain => "Peel chain",
        GuideTopic.CoinJoin => "CoinJoin",
        GuideTopic.Graph => "Grafen och bildspråket",
        GuideTopic.Checks => "Kontroller och förslag",
        GuideTopic.Areas => "Områden i grafen",
        GuideTopic.MixerCandidates => "Mixerns troliga utgångar",
        GuideTopic.MixerChain => "Mixer efter mixer och delarna",
        GuideTopic.AccountModel => "Kontomodellen",
        GuideTopic.Tokens => "ERC-20 och tokens",
        GuideTopic.CanBeFrozen => "Kan frysas",
        GuideTopic.Prices => "Kurser och omräkning",
        GuideTopic.HowToTrace => "Så spårar man i Chainsaw",
        GuideTopic.Exposure => "Exponering",
        GuideTopic.ChainCheck => "Hur Chainsaw kontrollerar svaren mot kedjan",
        GuideTopic.Swaps => "Spåret genom en swap",
        GuideTopic.TimelineView => "Vyn Tidslinje",
        GuideTopic.Flow => "Flödet",
        GuideTopic.ServiceBehaviour => "Tjänster ur beteende",
        GuideTopic.Splitting => "Uppdelning i många små belopp",
        GuideTopic.Layering => "Fördröjning och många hopp",
        GuideTopic.Dust => "Damm och adressförgiftning",
        GuideTopic.PayJoin => "PayJoin",
        GuideTopic.TimePattern => "Tidsmönster",
        GuideTopic.Anchors => "Ankaret och flera datorer",
        GuideTopic.GoneTransaction => "En tx som inte finns längre",
        GuideTopic.CrossChain => "Fortsättningen på en annan kedja",
        _ => "UTXO-modellen",
    };

    /// <summary>Gives the section that explains a heuristic.</summary>
    /// <param name="heuristic">The heuristic's name, from <see cref="HeuristicNames"/>.</param>
    /// <returns>The section, or <see langword="null"/> for a heuristic the Guide does not explain.</returns>
    public static GuideTopic? ForHeuristic(string heuristic) => heuristic switch
    {
        HeuristicNames.CommonInputOwnership => GuideTopic.CommonInputOwnership,
        HeuristicNames.ChangeAddress => GuideTopic.ChangeAddress,
        HeuristicNames.PeelChain => GuideTopic.PeelChain,
        HeuristicNames.CoinJoin => GuideTopic.CoinJoin,
        _ => null,
    };

    /// <summary>
    /// Gives the heading that answers best when a section is opened from <em>Varför?</em>: for a heuristic the
    /// heading that says what it looks for, for a label that a label is a judgement.
    /// </summary>
    /// <param name="topic">The section.</param>
    /// <returns>The heading as written in the text, or <see langword="null"/> to open the section at its top.</returns>
    public static string? DefaultHeading(this GuideTopic topic) => topic switch
    {
        GuideTopic.CommonInputOwnership => "Idén",
        GuideTopic.ChangeAddress => "När slås växeln ihop?",
        GuideTopic.PeelChain => "Hur Chainsaw känner igen den",
        GuideTopic.CoinJoin => "Vad Chainsaw tittar efter",
        GuideTopic.Labels => "En etikett är alltid en bedömning",
        GuideTopic.Graph => InspectorHeading,
        GuideTopic.MixerCandidates => "Hur poängen räknas",
        _ => null,
    };

    /// <summary>Gives the heading in <em>Spårningsmetoder</em> that explains a method.</summary>
    /// <param name="method">The method.</param>
    /// <returns>The heading as written in the text.</returns>
    public static string MethodHeading(TracingMethodKind method) =>
        method == TracingMethodKind.Haircut ? "Haircut (andel)" : "FIFO (först in, först ut)";

    /// <summary>
    /// Gives the heading in <em>Varför ett spår tar slut</em> for an end: the ends that can still be followed (not
    /// expanded, unspent, conflicting) or the real ones.
    /// </summary>
    /// <param name="reason">The end's reason.</param>
    /// <returns>The heading as written in the text.</returns>
    public static string EndHeading(TrailEndReason reason) =>
        reason.EndsTrail()
            ? RealEndsHeading
            : OpenEndsHeading;
}
