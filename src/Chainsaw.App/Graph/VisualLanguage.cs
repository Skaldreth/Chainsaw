using Chainsaw.Modules.Attribution.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>
/// What a colour in the graph means (sketches GR3a and GR3c); the view gives each its brush, so that the meaning is
/// decided here and the colour once in <c>Themes/Colors.xaml</c>.
/// </summary>
public enum GraphInk
{
    /// <summary>The accent: the victims' money and what is chosen.</summary>
    Accent,

    /// <summary>Blue: a cash-out, where the money leaves the chain.</summary>
    CashOut,

    /// <summary>Yellow: where the money is spent.</summary>
    Spent,

    /// <summary>Red: risk and the end of the trail.</summary>
    Risk,

    /// <summary>A brighter red: on a sanction list.</summary>
    Sanctioned,

    /// <summary>Purple: another chain.</summary>
    OtherChain,

    /// <summary>Light grey: a cluster.</summary>
    Cluster,

    /// <summary>Grey: an actor we know nothing about.</summary>
    Unknown,

    /// <summary>The dark grey of a line that carries none of the traced money.</summary>
    Untraced,

    /// <summary>The light disc of the start point's flag (sketch GR5, variant A).</summary>
    StartMark,
}

/// <summary>A node's frame: the shape says what it is (sketch GR3a).</summary>
public enum NodeShape
{
    /// <summary>Round: an actor we know nothing about.</summary>
    Round,

    /// <summary>Square: a service or a cluster.</summary>
    Square,
}

/// <summary>
/// The sorts of actors in the graph, after what happens to the money (sketch GR3c): the sorts of
/// <see cref="LabelKind"/>, and the graph's own sorts for what has no label.
/// </summary>
public enum NodeSort
{
    /// <summary>
    /// The victim (round, the accent): a label the user sets, never the start point's sort, since a start point may
    /// as well be the suspect's wallet (sketch GR5).
    /// </summary>
    Victim,

    /// <summary>A cluster without a label: several addresses, one owner.</summary>
    Cluster,

    /// <summary>An address without a label.</summary>
    Unknown,

    /// <summary>An exchange with customer checks.</summary>
    Exchange,

    /// <summary>An exchange without customer checks (dashed frame).</summary>
    NoKycExchange,

    /// <summary>A nested service.</summary>
    Nested,

    /// <summary>OTC and P2P.</summary>
    OtcP2P,

    /// <summary>A crypto ATM.</summary>
    Atm,

    /// <summary>A crypto card.</summary>
    Card,

    /// <summary>Gambling (two playing cards).</summary>
    Gambling,

    /// <summary>A merchant (a shop).</summary>
    Merchant,

    /// <summary>Gift cards.</summary>
    GiftCard,

    /// <summary>A darknet market.</summary>
    Darknet,

    /// <summary>An escrow market.</summary>
    Escrow,

    /// <summary>A mixer (dashed frame: the trail ends).</summary>
    Mixer,

    /// <summary>A CoinJoin (round, dashed frame: the trail ends).</summary>
    CoinJoin,

    /// <summary>On a sanction list.</summary>
    Sanctioned,

    /// <summary>A swap or a bridge (dashed frame: continues on another chain).</summary>
    SwapBridge,

    /// <summary>A transaction whose inputs belong to more than one actor, drawn as a node of its own.</summary>
    SharedTransaction,

    /// <summary>A coin whose script has no address.</summary>
    Coin,

    /// <summary>A transaction on another chain that the user linked to.</summary>
    OtherChainTransaction,
}

/// <summary>A small mark in a node's corner (sketches GR3a and GR3c).</summary>
public enum NodeBadge
{
    /// <summary>On a sanction list.</summary>
    Sanctioned,

    /// <summary>A cash-out that traced money reaches: the withdrawal point is found.</summary>
    CashOutFound,

    /// <summary>The label came from another case through the library.</summary>
    FromOtherCase,

    /// <summary>
    /// The trace starts here (sketch GR5, variant A): a small flag in the top-left corner, the same form as the other
    /// marks but on the other side, so that it never collides with them. The node keeps its sort.
    /// </summary>
    StartPoint,
}

/// <summary>How a sort is drawn.</summary>
/// <param name="Sort">The sort.</param>
/// <param name="Name">The sort's name, in Swedish, as the legend and the inspector write it.</param>
/// <param name="Icon">The icon's resource key in <c>Themes/Icons.xaml</c>.</param>
/// <param name="Ink">The colour.</param>
/// <param name="Shape">The frame's shape.</param>
/// <param name="IsDashed">Whether the frame is dashed: uncertain, or the end of the trail.</param>
public sealed record SortLook(NodeSort Sort, string Name, string Icon, GraphInk Ink, NodeShape Shape, bool IsDashed);

/// <summary>How a mark is drawn.</summary>
/// <param name="Badge">The mark.</param>
/// <param name="Name">What it means, in Swedish.</param>
/// <param name="Icon">The icon's resource key.</param>
/// <param name="Ink">The colour of its disc.</param>
public sealed record BadgeLook(NodeBadge Badge, string Name, string Icon, GraphInk Ink);

/// <summary>
/// The graph's visual language in one table (sketches GR3a and GR3c): how every sort and mark is drawn. The graph, the
/// legend behind the <em>?</em> and the inspector all read it, so that they never disagree.
/// </summary>
public static class VisualLanguage
{
    /// <summary>Gets every sort in the legend's order: the cash-outs, the spending, the risks, the rest.</summary>
    public static IReadOnlyList<SortLook> Sorts { get; } =
    [
        new(NodeSort.Exchange, "Börs", "Icon.Kind.Bank", GraphInk.CashOut, NodeShape.Square, false),
        new(NodeSort.NoKycExchange, "Utan KYC", "Icon.Kind.Bank", GraphInk.CashOut, NodeShape.Square, true),
        new(NodeSort.Nested, "Nästlad", "Icon.Kind.Nested", GraphInk.CashOut, NodeShape.Square, false),
        new(NodeSort.OtcP2P, "OTC, P2P", "Icon.Kind.Otc", GraphInk.CashOut, NodeShape.Square, false),
        new(NodeSort.Atm, "Automat", "Icon.Kind.Atm", GraphInk.CashOut, NodeShape.Square, false),
        new(NodeSort.Card, "Kort", "Icon.Kind.Card", GraphInk.CashOut, NodeShape.Square, false),
        new(NodeSort.Gambling, "Spel", "Icon.Kind.Cards", GraphInk.Spent, NodeShape.Square, false),
        new(NodeSort.Merchant, "Handel", "Icon.Kind.Store", GraphInk.Spent, NodeShape.Square, false),
        new(NodeSort.GiftCard, "Presentkort", "Icon.Kind.Gift", GraphInk.Spent, NodeShape.Square, false),
        new(NodeSort.Darknet, "Darknet", "Icon.Kind.Mask", GraphInk.Risk, NodeShape.Square, false),
        new(NodeSort.Escrow, "Garanti", "Icon.Kind.Guarantee", GraphInk.Risk, NodeShape.Square, false),
        new(NodeSort.Mixer, "Mixer", "Icon.Kind.Mixer", GraphInk.Risk, NodeShape.Square, true),
        new(NodeSort.CoinJoin, "CoinJoin", "Icon.Kind.CoinJoin", GraphInk.Risk, NodeShape.Round, true),
        new(NodeSort.Sanctioned, "Sanktion", "Icon.Kind.Ban", GraphInk.Sanctioned, NodeShape.Square, false),
        new(NodeSort.Victim, "Offer", "Icon.Kind.Person", GraphInk.Accent, NodeShape.Round, false),
        new(NodeSort.Cluster, "Kluster", "Icon.Kind.Wallet", GraphInk.Cluster, NodeShape.Square, false),
        new(NodeSort.Unknown, "Okänd", "Icon.Kind.Unknown", GraphInk.Unknown, NodeShape.Round, false),
        new(NodeSort.SwapBridge, "Swap", "Icon.Kind.Swap", GraphInk.OtherChain, NodeShape.Square, true),
        new(NodeSort.SharedTransaction, "Delad tx", "Icon.Kind.Transaction", GraphInk.Unknown, NodeShape.Square, false),
        new(NodeSort.Coin, "Mynt", "Icon.Kind.Coin", GraphInk.Unknown, NodeShape.Round, false),
        new(NodeSort.OtherChainTransaction, "Annan kedja", "Icon.Kind.Swap", GraphInk.OtherChain, NodeShape.Square,
            true),
    ];

    /// <summary>
    /// Gets every mark that Chainsaw can set now; the watched and freezable marks wait for their modules. The start
    /// point's flag is drawn in the other corner (<see cref="NodeView.IsStartPoint"/>), the rest in the top-right one.
    /// </summary>
    public static IReadOnlyList<BadgeLook> Badges { get; } =
    [
        new(NodeBadge.StartPoint, "Startpunkt", "Icon.Flag", GraphInk.StartMark),
        new(NodeBadge.CashOutFound, "Uttag funnet", "Icon.Kind.Check", GraphInk.CashOut),
        new(NodeBadge.FromOtherCase, "Etikett från ett annat ärende", "Icon.Tag", GraphInk.Cluster),
        new(NodeBadge.Sanctioned, "Sanktionerad", "Icon.Kind.Ban", GraphInk.Sanctioned),
    ];

    /// <summary>Gives how a sort is drawn.</summary>
    /// <param name="sort">The sort.</param>
    /// <returns>The look; every sort has one.</returns>
    public static SortLook Of(NodeSort sort) => Sorts.First(look => look.Sort == sort);

    /// <summary>Gives how a mark is drawn.</summary>
    /// <param name="badge">The mark.</param>
    /// <returns>The look; every mark has one.</returns>
    public static BadgeLook Of(NodeBadge badge) => Badges.First(look => look.Badge == badge);

    /// <summary>Gives the sort a label's kind draws as.</summary>
    /// <param name="kind">The label's kind.</param>
    /// <returns>The sort.</returns>
    public static NodeSort SortOf(LabelKind kind) => kind switch
    {
        LabelKind.Exchange => NodeSort.Exchange,
        LabelKind.NoKycExchange => NodeSort.NoKycExchange,
        LabelKind.NestedService => NodeSort.Nested,
        LabelKind.OtcP2P => NodeSort.OtcP2P,
        LabelKind.CryptoAtm => NodeSort.Atm,
        LabelKind.CryptoCard => NodeSort.Card,
        LabelKind.Gambling => NodeSort.Gambling,
        LabelKind.Merchant => NodeSort.Merchant,
        LabelKind.GiftCard => NodeSort.GiftCard,
        LabelKind.Darknet => NodeSort.Darknet,
        LabelKind.EscrowMarket => NodeSort.Escrow,
        LabelKind.Mixer => NodeSort.Mixer,
        LabelKind.CoinJoin => NodeSort.CoinJoin,
        LabelKind.Sanctioned => NodeSort.Sanctioned,
        LabelKind.SwapBridge => NodeSort.SwapBridge,
        LabelKind.Victim => NodeSort.Victim,
        _ => NodeSort.Unknown,
    };

    /// <summary>Says whether a sort is a cash-out: blue, where a request for information can be made.</summary>
    /// <param name="sort">The sort.</param>
    /// <returns><see langword="true"/> for the cash-outs.</returns>
    public static bool IsCashOut(NodeSort sort) => Of(sort).Ink == GraphInk.CashOut;
}
