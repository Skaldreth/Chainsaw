using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>One answer in a block mark's box: where the transaction stood, and when the case saw it there.</summary>
/// <param name="Height">The block's height, or <c>mempool</c> or a dash.</param>
/// <param name="Hash">The block's hash shortened, or what stands in its place.</param>
/// <param name="FullHash">The hash in full, for hovering, or <see langword="null"/>.</param>
/// <param name="When">When the answer came, for example <c>sett 13:51</c>.</param>
/// <param name="Endpoint">The endpoint the answer came from, or an empty text.</param>
/// <param name="IsBefore">Whether it is the earlier answer, written struck through.</param>
public sealed record BlockMarkRow(
    string Height, string Hash, string? FullHash, string When, string Endpoint, bool IsBefore)
{
    /// <summary>Gets when and from where, as the row writes it, for example <c>sett 13:51 · mempool.space</c>.</summary>
    public string WhenAndWhere => Endpoint.Length > 0 ? $"{When} · {Endpoint}" : When;
}

/// <summary>
/// The row of a gone transaction's box that names what replaced it (<em>Ersatt av …</em>), with the link that follows
/// the replacer or takes that choice back. That the replacer carries the same money on is the user's judgement
/// (principle 2), so the trace follows it only by that choice.
/// </summary>
/// <param name="Gone">The gone transaction.</param>
/// <param name="Replacer">The transaction that spends its coins now.</param>
/// <param name="Text">
/// What the row says, with the replacer's block or that it is unconfirmed, for example
/// <c>Ersatt av 3f2a…9b1c · 921 345</c>.
/// </param>
/// <param name="FullTxid">The replacer's txid in full, for hovering and copying.</param>
/// <param name="Follow">The user's choice in force to follow it, or <see langword="null"/>.</param>
public sealed record BlockMarkReplacement(
    Txid Gone, Txid Replacer, string Text, string FullTxid, ReplacementFollow? Follow)
{
    /// <summary>Gets whether the trace follows the replacer by the user's choice.</summary>
    public bool IsFollowed => Follow is not null;

    /// <summary>Gets the link's text: follow it, or take the choice back.</summary>
    public string LinkText => IsFollowed ? BlockMarks.TakeBackText : BlockMarks.FollowText;
}

/// <summary>The graph's reading of a gone transaction, as its block mark shows it (sketch GR9a).</summary>
/// <param name="Gone">The transaction and what the case knows replaced it.</param>
/// <param name="Follows">The user's choices in force to follow its replacers.</param>
public sealed record GoneReading(GoneTransaction Gone, IReadOnlyList<ReplacementFollow> Follows);

/// <summary>
/// The round mark in the corner of a line's amount when a transaction on it has left the block the case saw it in
/// (sketch GR9a), with the box that says before and now. The chain's answers are facts, and both stay in the case
/// (principle 1); the mark only shows that they differ.
/// </summary>
/// <param name="Change">The change it shows.</param>
/// <param name="Title">The box's title, for example <c>Bytte block</c>.</param>
/// <param name="Tip">The mark's tooltip.</param>
/// <param name="Icon">The mark's icon's resource key.</param>
/// <param name="Rows">Before and now.</param>
/// <param name="Foot">What it means, in a sentence or two.</param>
public sealed record BlockMark(
    BlockChange Change, string Title, string Tip, string Icon, IReadOnlyList<BlockMarkRow> Rows, string Foot)
{
    /// <summary>Gets whether the transaction is gone: the mark is red and the amount struck through.</summary>
    public bool IsGone => Change.Kind == BlockChangeKind.Gone;

    /// <summary>
    /// Gets what replaced a gone transaction, one row per replacer (a double spend can have several, each choosable),
    /// as the case's sealed answers say; empty when nothing is known to, or the answers do not settle that it is gone.
    /// </summary>
    public IReadOnlyList<BlockMarkReplacement> Replacements { get; init; } = [];
}

/// <summary>Makes the block marks of a graph's lines (sketch GR9a), without WPF.</summary>
public static class BlockMarks
{
    /// <summary>The title of a transaction in another block.</summary>
    public const string MovedTitle = "Bytte block";

    /// <summary>The title of a transaction unconfirmed again.</summary>
    public const string UnconfirmedTitle = "Tillbaka i mempool";

    /// <summary>The title of a transaction the source no longer knows.</summary>
    public const string GoneTitle = "Finns inte längre";

    /// <summary>What the row of a gone transaction's replacer starts with.</summary>
    public const string ReplacedByText = "Ersatt av ";

    /// <summary>The link that follows the replacer (a judgement, sealed with who and when).</summary>
    public const string FollowText = "Följ ersättaren";

    /// <summary>The link that takes back the choice to follow the replacer.</summary>
    public const string TakeBackText = "Ta tillbaka";

    /// <summary>What a gone transaction's box says when another transaction spends its coins now.</summary>
    public const string ReplacedFoot =
        "Källan känner inte längre till tx:en, och en annan tx spenderar samma mynt i stället (RBF eller en "
        + "dubbelspendering). Spåret slutar här; att ersättaren för samma pengar vidare är en bedömning, så spåret "
        + "följer den bara om du väljer det. Alla svar står kvar i ärendet.";

    /// <summary>What a gone transaction's box says when the trace follows the replacer by the user's choice.</summary>
    public const string FollowedFoot =
        "Källan känner inte längre till tx:en, och en annan tx spenderar samma mynt i stället. Du valde att spåret "
        + "följer ersättaren; linjerna efter den är streckade som en bedömning. Alla svar står kvar i ärendet.";

    /// <summary>
    /// What a gone transaction's box says when the answers do not settle that it is gone: the trace stops there
    /// without an end.
    /// </summary>
    public const string UncertainFoot =
        "Källan säger att den inte känner till tx:en, men det är inte bekräftat: bara en värd har sagt det, eller "
        + "svaren om mynten säger att tx:en fortfarande spenderar dem. Spåret stannar här utan att sluta, i stället "
        + "för att gissa. Alla svar står kvar i ärendet.";

    /// <summary>
    /// What a box adds when the coins' transactions have only partly been asked about, with how many: the rest are
    /// asked at the next lookup.
    /// </summary>
    public const string PartlyAskedFormat = "Frågat om {0} av {1} tx som mynten kom ur; resten vid nästa uppslag.";

    /// <summary>What a row says after the replacer's id when it is unconfirmed.</summary>
    public const string UnconfirmedText = "obekräftad";

    /// <summary>The most replacers a box names, each on its own row.</summary>
    public const int MostReplacementRows = 3;

    /// <summary>What a gone transaction's box says when nothing spends its coins: it was dropped.</summary>
    public const string DroppedFoot =
        "Källan känner inte längre till tx:en, och ingen annan tx spenderar mynten: den föll bort utan ersättare, "
        + "och pengarna ligger kvar där de kom ifrån. Spåret slutar här. Alla svar står kvar i ärendet.";

    /// <summary>What a mark's tooltip adds to its title.</summary>
    public const string TipTail = " · klicka för förut och nu";

    /// <summary>What a moved transaction's box says: an assessment of what the source answered (principle 2).</summary>
    public const string MovedFoot =
        "Källan svarar nu att tx:en ligger i ett annat block än det ärendet först såg den i. Troligen organiserades "
        + "kedjan om; tx:en är densamma och bekräftad igen. Båda svaren står kvar i ärendet.";

    /// <summary>What a box says when the transaction is unconfirmed again: an assessment (principle 2).</summary>
    public const string UnconfirmedFoot =
        "Källan svarar nu att tx:en är obekräftad, och den hade då kommit minst lika långt som blocket. Troligen föll "
        + "blocket bort ur kedjan, och tx:en väntar på ett nytt. Båda svaren står kvar i ärendet.";

    /// <summary>What a box says when the source no longer knows the transaction: an assessment (principle 2).</summary>
    public const string GoneFoot =
        "Källan svarar nu att den inte känner till tx:en, och den hade då kommit minst lika långt som blocket. "
        + "Troligen spenderar en annan tx samma pengar i den nya kedjan. Spåret slutar här, och det efter tx:en är "
        + "osäkert. Båda svaren står kvar i ärendet.";

    /// <summary>How many characters of a hash are kept at its start, where a block's hash has its zeros.</summary>
    private const int HashHead = 6;

    /// <summary>How many characters of a hash are kept at its end, where it differs.</summary>
    private const int HashTail = 8;

    /// <summary>The Swedish culture the times are written in.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>
    /// Gives each line that carries a changed transaction its mark: when a line carries several, the gravest (gone
    /// before unconfirmed before moved).
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <param name="changes">The changes the case has seen.</param>
    /// <param name="now">The time now, which says whether a sighting is written with its day.</param>
    /// <param name="zone">The time zone the times are written in.</param>
    /// <returns>The marks, by the line's key.</returns>
    public static IReadOnlyDictionary<string, BlockMark> ForLines(
        GraphScene? scene, IReadOnlyList<BlockChange> changes, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var marks = new Dictionary<string, BlockMark>(StringComparer.Ordinal);
        if (scene is null || changes.Count == 0)
        {
            return marks;
        }

        // A transaction is named by its chain and its id, so that Bitcoin's and Ethereum's never meet.
        var byTransaction = changes
            .GroupBy(change => (change.Chain, Id: change.TransactionId.ToLowerInvariant()))
            .ToDictionary(group => group.Key, group => group.MaxBy(change => Gravity(change.Kind))!);
        foreach (var edge in scene.Edges)
        {
            var gravest = edge.Edge.Transfers
                .Select(transfer => transfer.Transaction)
                .Select(transaction => byTransaction.GetValueOrDefault((transaction.Chain, transaction.Id)))
                .OfType<BlockChange>()
                .MaxBy(change => Gravity(change.Kind));
            if (gravest is not null)
            {
                marks[edge.Key] = Of(gravest, now, zone, GoneOf(scene.Snapshot.Graph, gravest));
            }
        }

        return marks;
    }

    /// <summary>Makes the mark of one change.</summary>
    /// <param name="change">The change.</param>
    /// <param name="now">The time now.</param>
    /// <param name="zone">The time zone the times are written in.</param>
    /// <param name="gone">
    /// The graph's reading of a gone transaction, with what replaced it and whether the trace follows the replacer;
    /// <see langword="null"/> when the change is not that or the graph does not hold it.
    /// </param>
    /// <returns>The mark.</returns>
    public static BlockMark Of(
        BlockChange change, DateTimeOffset now, TimeZoneInfo zone, GoneReading? gone = null)
    {
        ArgumentNullException.ThrowIfNull(change);
        var before = Row(change.Before, isBefore: true, change.Kind, now, zone);
        var after = Row(change.Now, isBefore: false, change.Kind, now, zone);
        var (title, icon, foot) = change.Kind switch
        {
            BlockChangeKind.Moved => (MovedTitle, "Icon.BlockMoved", MovedFoot),
            BlockChangeKind.Unconfirmed => (UnconfirmedTitle, "Icon.Clock", UnconfirmedFoot),
            _ => (GoneTitle, "Icon.Kind.Ban", GoneFootOf(gone)),
        };
        return new BlockMark(change, title, title + TipTail, icon, [before, after], foot)
        {
            Replacements = change.Kind == BlockChangeKind.Gone ? ReplacementsOf(gone) : [],
        };
    }

    /// <summary>Gives what a gone transaction's box says, after what the case knows replaced it.</summary>
    /// <param name="gone">The graph's reading of it, or <see langword="null"/>.</param>
    /// <returns>The foot.</returns>
    private static string GoneFootOf(GoneReading? gone)
    {
        var foot = BaseGoneFoot(gone);
        if (gone?.Gone is { RestsOnOneSource: true } single)
        {
            foot += " " + GoneTransaction.OneSourceText(single.OneSource);
        }

        if (gone?.Gone.Replacement is { } replacement && replacement.PreviousAnswered < replacement.PreviousCount)
        {
            var (answered, count) = (replacement.PreviousAnswered, replacement.PreviousCount);
            foot += " " + string.Format(CultureInfo.InvariantCulture, PartlyAskedFormat, answered, count);
        }

        return foot;
    }

    /// <summary>Gives the first part of a gone transaction's foot, after how far the answers go.</summary>
    /// <param name="gone">The graph's reading of it, or <see langword="null"/>.</param>
    /// <returns>The text.</returns>
    private static string BaseGoneFoot(GoneReading? gone)
    {
        if (gone is { Gone.IsSettled: false })
        {
            return UncertainFoot;
        }

        if (gone is { Follows.Count: > 0 })
        {
            return FollowedFoot;
        }

        return (gone?.Gone.Replacement?.Kind ?? ReplacementKind.Unknown) switch
        {
            ReplacementKind.Replaced or ReplacementKind.PartlyReplaced => ReplacedFoot,
            ReplacementKind.Dropped => DroppedFoot,
            _ => GoneFoot,
        };
    }

    /// <summary>
    /// Gives the rows that name a gone transaction's replacers, each with its block, those the trace follows first, at
    /// most <see cref="MostReplacementRows"/>; none while the answers do not settle that it is gone.
    /// </summary>
    /// <param name="gone">The graph's reading of it, or <see langword="null"/>.</param>
    /// <returns>The rows.</returns>
    private static IReadOnlyList<BlockMarkReplacement> ReplacementsOf(GoneReading? gone)
    {
        if (gone is not { Gone.IsSettled: true } known || known.Gone.Replacement is not { } replacement)
        {
            return [];
        }

        return
        [
            .. replacement.Replacers
                .OrderBy(replacer => known.Follows.Any(follow => follow.Replacer == replacer) ? 0 : 1)
                .Take(MostReplacementRows)
                .Select(replacer => new BlockMarkReplacement(
                    known.Gone.Txid,
                    replacer,
                    $"{ReplacedByText}{GraphScene.Shorten(replacer.Value)} · {BlockOf(replacement, replacer)}",
                    replacer.Value,
                    known.Follows.FirstOrDefault(follow => follow.Replacer == replacer))),
        ];
    }

    /// <summary>Writes the block a replacer is in, as the answers about the coins say, or that it is in none.</summary>
    /// <param name="replacement">The answers.</param>
    /// <param name="replacer">The replacer.</param>
    /// <returns>For example <c>921 345</c> or <c>obekräftad</c>.</returns>
    private static string BlockOf(TransactionReplacement replacement, Txid replacer) =>
        replacement.Coins.Select(coin => coin.SpentBy).FirstOrDefault(by => by?.Txid == replacer)?.Block is { } block
            ? Height(block.Height)
            : UnconfirmedText;

    /// <summary>
    /// Finds the graph's reading of a gone Bitcoin transaction, with the user's choice in force to follow its
    /// replacer.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="change">The change.</param>
    /// <returns>The reading, or <see langword="null"/>.</returns>
    private static GoneReading? GoneOf(TraceGraph graph, BlockChange change)
    {
        if (change is not { Chain: Chain.Bitcoin, Kind: BlockChangeKind.Gone }
            || !Txid.TryParse(change.TransactionId, out var txid) || !graph.Gone.TryGetValue(txid, out var gone))
        {
            return null;
        }

        return new GoneReading(gone, [.. graph.FollowedReplacements.Where(follow => follow.Gone == txid)]);
    }

    /// <summary>Writes a time the way the graph's small boxes do: the clock alone today, the day before it else.</summary>
    /// <param name="time">The time.</param>
    /// <param name="now">The time now.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>For example <c>13:51</c> or <c>5 nov 13:51</c>.</returns>
    public static string ShortTime(DateTimeOffset time, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(time, zone);
        var today = TimeZoneInfo.ConvertTime(now, zone).Date == local.Date;
        var day = string.Create(Swedish, $"{local.Day} {local.ToString("MMM", Swedish).TrimEnd('.')} ");
        return (today ? string.Empty : day) + local.ToString("HH:mm", Swedish);
    }

    /// <summary>Writes a height with its thousands apart, as the chain's heights are written in the graph.</summary>
    /// <param name="height">The height.</param>
    /// <returns>For example <c>921 340</c>.</returns>
    public static string Height(long height) => height.ToString("#,0", Swedish).Replace(' ', ' ');

    /// <summary>Makes one row of the box.</summary>
    /// <param name="sighting">The answer.</param>
    /// <param name="isBefore">Whether it is the earlier.</param>
    /// <param name="kind">What became of the transaction.</param>
    /// <param name="now">The time now.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>The row.</returns>
    private static BlockMarkRow Row(
        BlockSighting sighting, bool isBefore, BlockChangeKind kind, DateTimeOffset now, TimeZoneInfo zone)
    {
        var when = "sett " + ShortTime(sighting.Seen, now, zone);
        if (sighting is { Height: { } height, Hash: { } hash })
        {
            return new BlockMarkRow(Height(height), ShortHash(hash), hash, when, sighting.Endpoint, isBefore);
        }

        return kind == BlockChangeKind.Gone
            ? new BlockMarkRow("–", "okänd för källan", null, when, sighting.Endpoint, isBefore)
            : new BlockMarkRow("mempool", "obekräftad", null, when, sighting.Endpoint, isBefore);
    }

    /// <summary>Shortens a hash in the middle.</summary>
    /// <param name="hash">The hash.</param>
    /// <returns>For example <c>000000…9e0f3fa1</c>.</returns>
    public static string ShortHash(string hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        return hash.Length <= HashHead + HashTail + 1 ? hash : $"{hash[..HashHead]}…{hash[^HashTail..]}";
    }

    /// <summary>Orders the kinds of change by how grave they are for the trail.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>A higher number for a graver change.</returns>
    private static int Gravity(BlockChangeKind kind) => kind switch
    {
        BlockChangeKind.Gone => 2,
        BlockChangeKind.Unconfirmed => 1,
        _ => 0,
    };
}
