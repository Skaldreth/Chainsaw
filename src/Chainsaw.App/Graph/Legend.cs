namespace Chainsaw.App.Graph;

/// <summary>Something in the legend that explains itself in the legend's bottom row when it is hovered.</summary>
public interface ILegendItem
{
    /// <summary>Gets its name, as the legend writes it.</summary>
    string Name { get; }

    /// <summary>Gets what it means, in a sentence or two, in Swedish.</summary>
    string Hint { get; }
}

/// <summary>A sort in the legend: drawn by the graph's own table, with what it means.</summary>
/// <param name="Look">How the graph draws the sort (<see cref="VisualLanguage"/>).</param>
/// <param name="Hint">What it means.</param>
public sealed record LegendSort(SortLook Look, string Hint) : ILegendItem
{
    /// <inheritdoc />
    public string Name => Look.Name;

    /// <summary>Gets whether the sort's frame is round.</summary>
    public bool IsRound => Look.Shape == NodeShape.Round;

    /// <summary>Gets the dashes of the sort's frame, or <see langword="null"/> for a whole frame.</summary>
    public IReadOnlyList<double>? FrameDashes => Look.IsDashed ? GraphDashes.Frame : null;
}

/// <summary>A row of the legend: what a colour means, and the sorts drawn in it (sketch GR7a).</summary>
/// <param name="Name">What the colour means, for example <em>Uttag</em>.</param>
/// <param name="Ink">The colour of the row's dot.</param>
/// <param name="Hint">What the colour means, in a sentence.</param>
/// <param name="Sorts">The sorts in the colour, in the legend's order.</param>
public sealed record LegendGroup(string Name, GraphInk Ink, string Hint, IReadOnlyList<LegendSort> Sorts) : ILegendItem;

/// <summary>A form in the legend's first column: what the frame's shape and stroke say.</summary>
/// <param name="Name">The form's name.</param>
/// <param name="Shape">The frame's shape.</param>
/// <param name="IsDashed">Whether the frame is dashed.</param>
/// <param name="Hint">What it means.</param>
public sealed record LegendForm(string Name, NodeShape Shape, bool IsDashed, string Hint) : ILegendItem
{
    /// <summary>Gets whether the frame is round.</summary>
    public bool IsRound => Shape == NodeShape.Round;

    /// <summary>Gets the dashes of the frame, or <see langword="null"/> for a whole frame.</summary>
    public IReadOnlyList<double>? FrameDashes => IsDashed ? GraphDashes.Frame : null;
}

/// <summary>A mark in the legend's second column, drawn by the graph's own table, with what it means.</summary>
/// <param name="Look">How the graph draws the mark (<see cref="VisualLanguage"/>).</param>
/// <param name="Hint">What it means.</param>
public sealed record LegendBadge(BadgeLook Look, string Hint) : ILegendItem
{
    /// <inheritdoc />
    public string Name => Look.Name;
}

/// <summary>A line in the legend's third column: its colour, thickness and dashes as the graph draws them.</summary>
/// <param name="Name">The line's name.</param>
/// <param name="Ink">Its colour.</param>
/// <param name="Width">Its thickness in pixels.</param>
/// <param name="Dash">Its dashes.</param>
/// <param name="IsArc">Whether it is drawn as an arc: money that turns back in the hops.</param>
/// <param name="Hint">What it means.</param>
public sealed record LegendLine(string Name, GraphInk Ink, double Width, EdgeDash Dash, bool IsArc, string Hint)
    : ILegendItem
{
    /// <summary>Gets the line's dashes in multiples of its thickness, or <see langword="null"/> when whole.</summary>
    public IReadOnlyList<double>? Dashes => GraphDashes.ForLine(Dash, Width);
}

/// <summary>
/// The legend behind the graph's <em>?</em> (sketch GR7a): the sorts ordered by what their colour means, the forms,
/// the marks and the lines, each with a sentence for the bottom row. How each is drawn comes from the graph's own
/// tables (<see cref="VisualLanguage"/>, <see cref="GraphDashes"/>, <see cref="EdgeWidth"/>), so that the legend
/// never disagrees with the graph; only the words are here.
/// </summary>
public static class Legend
{
    /// <summary>The bottom row's rule while nothing is hovered.</summary>
    public const string Rule = "Formen säger vad det är, färgen vad det betyder.";

    /// <summary>The thickness of a thin traced line, as uncertain lines and lines to another chain often are.</summary>
    private const double ThinTraced = EdgeWidth.ThinnestTraced + (EdgeWidth.TracedSpan / 10);

    /// <summary>
    /// The sorts the legend explains, in its order within each colour; the pile and the held-back clue are the graph's
    /// own structure, not sorts of actors, and are explained where they appear.
    /// </summary>
    private static readonly (NodeSort Sort, string Hint)[] SortHints =
    [
        (NodeSort.Victim,
            "Offret är en etikett som sätts på en aktör; startpunkten kan lika gärna vara den misstänktes."),
        (NodeSort.Exchange, "Börs med kundkontroll. Hit vill spåret: en begäran om uppgifter."),
        (NodeSort.NoKycExchange, "Börs utan kundkontroll. Streckad ram: ett uttag, men svårt att få uppgifter."),
        (NodeSort.Nested, "En tjänst som ligger inne i en större börs och använder dess adresser."),
        (NodeSort.OtcP2P, "Handel direkt mellan personer eller över disk."),
        (NodeSort.Atm, "Kryptoautomat: pengarna tas ut som kontanter."),
        (NodeSort.Card, "Kryptokort: pengarna spenderas med ett vanligt betalkort."),
        (NodeSort.Gambling, "Spelsajt eller casino: två spelkort i en hand."),
        (NodeSort.Merchant, "Butik eller betaltjänst."),
        (NodeSort.GiftCard, "Pengarna blir presentkort."),
        (NodeSort.Darknet, "Darknetmarknad."),
        (NodeSort.Escrow, "Garantimarknad, där en mellanhand håller pengarna."),
        (NodeSort.Mixer, "Spåret tar slut. Chainsaw föreslår troliga utgångar, alltid streckade."),
        (NodeSort.CoinJoin, "Många betalar in i samma tx. Spåret tar slut."),
        (NodeSort.Sanctioned, "Står på en sanktionslista. Ljusare rött."),
        (NodeSort.SwapBridge, "Swap eller brygga: spåret fortsätter på en annan kedja."),
        (NodeSort.OtherChainTransaction, "Tx:en på den andra kedjan, knuten för hand."),
        (NodeSort.Unknown, "En adress utan etikett."),
        (NodeSort.Cluster, "Flera adresser, en ägare, enligt heuristikerna. Ljusare grått."),
        (NodeSort.SharedTransaction, "En tx med inputs från flera aktörer, ritad som egen nod."),
        (NodeSort.Coin, "Ett mynt utan adress."),
    ];

    /// <summary>The colours' rows: what each means, its dot's colour, the inks drawn in it and a sentence.</summary>
    private static readonly (string Name, GraphInk Ink, GraphInk[] Inks, string Hint)[] Colours =
    [
        ("Offrens pengar", GraphInk.Accent, [GraphInk.Accent],
            "Accenten är offrens pengar och det valda. Offret är en etikett, inte en sort."),
        ("Uttag", GraphInk.CashOut, [GraphInk.CashOut],
            "Blått är uttag: där pengarna lämnar kedjan och en begäran om uppgifter kan skickas."),
        ("Spenderas", GraphInk.Spent, [GraphInk.Spent], "Gult är där pengarna spenderas."),
        ("Risk, slut", GraphInk.Risk, [GraphInk.Risk, GraphInk.Sanctioned],
            "Rött är risk, och där spåret tar slut."),
        ("Annan kedja", GraphInk.OtherChain, [GraphInk.OtherChain], "Lila är en annan kedja."),
        ("Okänt", GraphInk.Unknown, [GraphInk.Unknown, GraphInk.Cluster],
            "Grått är det vi ännu inte vet något om."),
    ];

    /// <summary>What each mark means, by the mark.</summary>
    private static readonly Dictionary<NodeBadge, string> BadgeHints = new()
    {
        [NodeBadge.StartPoint] = "Spåret börjar här. Flaggan sitter i vänstra hörnet; noden behåller sin sort.",
        [NodeBadge.CashOutFound] = "Spårade pengar når ett uttag här.",
        [NodeBadge.RequestExported] = "En begäran om uppgifter till uttaget är exporterad och journalförd.",
        [NodeBadge.FromOtherCase] = "Etiketten kom från ett annat ärende genom biblioteket. En bedömning här.",
        [NodeBadge.Sanctioned] = "Adressen står på en sanktionslista.",
        [NodeBadge.FormerlySanctioned] =
            "Adressen stod på en sanktionslista men är borttagen. Datumen och källan står i Varför?.",
        [NodeBadge.Reported] =
            "En källa har rapporter om bedrägeri eller annat missbruk. En bedömning, inte ett faktum.",
        [NodeBadge.CanBeFrozen] = "USDT eller USDC ligger kvar; utgivaren kan frysa på begäran.",
        [NodeBadge.Watched] = "Säger till när pengarna rör sig. Grått när utgången inte är ansluten.",
    };

    /// <summary>Gets the colours' rows, each with its sorts (sketch GR7a).</summary>
    public static IReadOnlyList<LegendGroup> Groups { get; } =
    [
        .. Colours.Select(colour => new LegendGroup(colour.Name, colour.Ink, colour.Hint,
        [
            .. SortHints
                .Where(entry => colour.Inks.Contains(VisualLanguage.Of(entry.Sort).Ink))
                .Select(entry => new LegendSort(VisualLanguage.Of(entry.Sort), entry.Hint)),
        ])),
    ];

    /// <summary>Gets the forms: round, square and dashed.</summary>
    public static IReadOnlyList<LegendForm> Forms { get; } =
    [
        new("Aktör", NodeShape.Round, false, "Rund: en aktör vi inte vet något om."),
        new("Tjänst, kluster", NodeShape.Square, false, "Fyrkantig: en tjänst eller ett kluster."),
        new("Osäkert, slut", NodeShape.Square, true, "Streckad ram: osäkert, eller där spåret tar slut."),
    ];

    /// <summary>Gets the marks, in the graph's order.</summary>
    public static IReadOnlyList<LegendBadge> Badges { get; } =
    [
        .. VisualLanguage.Badges.Select(look => new LegendBadge(look, BadgeHints[look.Badge])),
    ];

    /// <summary>Gets the lines: traced, untraced, uncertain, another chain and money that turns back.</summary>
    public static IReadOnlyList<LegendLine> Lines { get; } =
    [
        new("Spårat", GraphInk.Accent, EdgeWidth.ThinnestTraced + EdgeWidth.TracedSpan, EdgeDash.None, false,
            "Offrens pengar. Tjock linje, stort belopp; beloppet står alltid på linjen."),
        new("Inte spårat", GraphInk.Untraced, EdgeWidth.Untraced, EdgeDash.None, false, "Andras pengar i samma tx."),
        new("Osäkert", GraphInk.Accent, ThinTraced, EdgeDash.Uncertain, false,
            "Streckat är alltid osäkert: en trolig fortsättning, aldrig ett faktum."),
        new("Annan kedja", GraphInk.OtherChain, ThinTraced, EdgeDash.OtherChain, false,
            "Fortsätter på en annan kedja."),
        new("Vänder", GraphInk.Untraced, EdgeWidth.Untraced, EdgeDash.None, true,
            "Pengar som går tillbaka i hoppen ritas som en båge."),
    ];

    /// <summary>Writes the bottom row for what is hovered: its name and what it means, or the rule.</summary>
    /// <param name="item">What is hovered, or <see langword="null"/>.</param>
    /// <returns>The row's text.</returns>
    public static string HintOf(ILegendItem? item) => item is null ? Rule : $"{item.Name}: {item.Hint}";
}
