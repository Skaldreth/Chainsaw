using Chainsaw.App.Graph;

namespace Chainsaw.App.Search;

/// <summary>How a hit is drawn: the node's form and colour from the graph (sketch KR1a), and its icon.</summary>
/// <param name="Icon">The icon's resource key, for example <c>Icon.Kind.Bank</c>.</param>
/// <param name="Ink">The colour.</param>
/// <param name="Shape">Round for an actor we know nothing about, square for a service or a cluster.</param>
/// <param name="IsDashed">Whether the frame is dashed, as uncertain things are.</param>
public sealed record SearchLook(string Icon, GraphInk Ink, NodeShape Shape, bool IsDashed)
{
    /// <summary>The look of a Guide section.</summary>
    public static SearchLook Guide { get; } = new("Icon.Guide", GraphInk.Unknown, NodeShape.Square, false);

    /// <summary>The look of a transaction that carries traced money.</summary>
    public static SearchLook TracedTransaction { get; } =
        new("Icon.Kind.Transaction", GraphInk.Accent, NodeShape.Square, false);

    /// <summary>The look of a transaction that carries none of the traced money.</summary>
    public static SearchLook Transaction { get; } =
        new("Icon.Kind.Transaction", GraphInk.Unknown, NodeShape.Square, false);

    /// <summary>Gives the look of a sort in the graph.</summary>
    /// <param name="look">The sort's look.</param>
    /// <returns>The hit's look.</returns>
    public static SearchLook Of(SortLook look)
    {
        ArgumentNullException.ThrowIfNull(look);
        return new(look.Icon, look.Ink, look.Shape, look.IsDashed);
    }
}

/// <summary>
/// One thing the command bar's search can find (sketch KR1a), read from the open case's graph or the Guide. It is
/// built off the UI thread and never changes.
/// </summary>
/// <param name="Kind">What it is.</param>
/// <param name="Text">What it is called: the label's name, the address, the txid or the section's title.</param>
/// <param name="Sub">The second line: what it is and where it stands, in a few words.</param>
/// <param name="Meta">What stands to the right, such as an amount; empty when nothing.</param>
/// <param name="Lead">The line under the best hit that says why it is what it is.</param>
/// <param name="Look">How it is drawn.</param>
/// <param name="NodeKey">
/// The graph's node it is shown at: the actor, the pile it lies in, or a transaction's receiver; <see langword="null"/>
/// when it is not in the graph or is a Guide section.
/// </param>
/// <param name="GuideSection">The Guide section's id, for a Guide hit; otherwise <see langword="null"/>.</param>
public sealed record SearchEntry(
    SearchKind Kind,
    string Text,
    string Sub,
    string Meta,
    string Lead,
    SearchLook Look,
    string? NodeKey,
    string? GuideSection)
{
    /// <summary>Gets whether the text is a hash or an address, in the mono font and cut in the middle.</summary>
    public bool IsMono { get; init; }

    /// <summary>Gets whether every origin of a label is another case (the dashed ring of sketch KR1a).</summary>
    public bool IsFromOtherCase { get; init; }

    /// <summary>
    /// Gets whether it lies in the trash (sketch SO1a): the graph hides it, so it is not shown there, and Enter opens
    /// the trash where it can be put back.
    /// </summary>
    public bool IsInTrash { get; init; }

    /// <summary>
    /// Gets other words it is found by, such as a section's id and group, or a cluster's addresses; matched less
    /// well than <see cref="Text"/>.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];
}
