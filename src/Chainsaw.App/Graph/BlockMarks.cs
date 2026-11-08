using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
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
        + "Troligen spenderar en annan tx samma pengar i den nya kedjan, och spåret efter den är osäkert. Båda svaren "
        + "står kvar i ärendet.";

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

        var byTxid = changes.ToDictionary(change => change.Txid.Value, StringComparer.Ordinal);
        foreach (var edge in scene.Edges)
        {
            var gravest = edge.Edge.Transfers
                .Where(transfer => transfer.Transaction.Chain == Chain.Bitcoin)
                .Select(transfer => byTxid.GetValueOrDefault(transfer.Transaction.Id))
                .OfType<BlockChange>()
                .MaxBy(change => Gravity(change.Kind));
            if (gravest is not null)
            {
                marks[edge.Key] = Of(gravest, now, zone);
            }
        }

        return marks;
    }

    /// <summary>Makes the mark of one change.</summary>
    /// <param name="change">The change.</param>
    /// <param name="now">The time now.</param>
    /// <param name="zone">The time zone the times are written in.</param>
    /// <returns>The mark.</returns>
    public static BlockMark Of(BlockChange change, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(change);
        var before = Row(change.Before, isBefore: true, change.Kind, now, zone);
        var after = Row(change.Now, isBefore: false, change.Kind, now, zone);
        var (title, icon, foot) = change.Kind switch
        {
            BlockChangeKind.Moved => (MovedTitle, "Icon.BlockMoved", MovedFoot),
            BlockChangeKind.Unconfirmed => (UnconfirmedTitle, "Icon.Clock", UnconfirmedFoot),
            _ => (GoneTitle, "Icon.Kind.Ban", GoneFoot),
        };
        return new BlockMark(change, title, title + TipTail, icon, [before, after], foot);
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
