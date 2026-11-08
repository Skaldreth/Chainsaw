namespace Chainsaw.App.Guide;

/// <summary>
/// A block of a Guide section: a heading, a paragraph, a list, a quote, an analogy, a figure, code or a table.
/// </summary>
public abstract record GuideBlock;

/// <summary>A heading inside a section (<c>##</c> or <c>###</c>); the section's own title is not a block.</summary>
/// <param name="Level">The level: 2 for <c>##</c>, 3 for <c>###</c>.</param>
/// <param name="Runs">The heading's text.</param>
/// <param name="Anchor">The anchor a link and the panel's mark point to, unique in the section.</param>
public sealed record GuideHeading(int Level, IReadOnlyList<GuideRun> Runs, string Anchor) : GuideBlock
{
    /// <summary>Gets the heading as plain text, without its styles.</summary>
    public string Text => GuideMarkdown.Plain(Runs);
}

/// <summary>A paragraph.</summary>
/// <param name="Runs">The text.</param>
public sealed record GuideParagraph(IReadOnlyList<GuideRun> Runs) : GuideBlock;

/// <summary>A quote (<c>&gt; text</c>), used in the Guide for a rule or a formula that stands out.</summary>
/// <param name="Runs">The text.</param>
public sealed record GuideQuote(IReadOnlyList<GuideRun> Runs) : GuideBlock;

/// <summary>
/// An everyday analogy (<c>&gt; **Som i vardagen:** text</c>) that opens a section for a reader who is not technical,
/// drawn as a callout of its own. The label is not part of the text; the callout shows it.
/// </summary>
/// <param name="Runs">The text after the label.</param>
public sealed record GuideAnalogy(IReadOnlyList<GuideRun> Runs) : GuideBlock
{
    /// <summary>The label a quote starts with, in bold, to be an analogy.</summary>
    public const string Label = "Som i vardagen:";

    /// <summary>Gets the label as the callout shows it, without the colon.</summary>
    public static string Heading => Label.TrimEnd(':');
}

/// <summary>
/// A figure (<c>![Bildtext](figur:id)</c> on a line of its own): a vector drawing in the theme with the key
/// <see cref="ResourceKey"/>, and its caption under it. A figure whose drawing is missing shows its caption and says
/// so; it never fails.
/// </summary>
/// <param name="Id">The drawing's id, or an empty string when the target is not a <c>figur:</c> id.</param>
/// <param name="Caption">The caption.</param>
public sealed record GuideFigure(string Id, IReadOnlyList<GuideRun> Caption) : GuideBlock
{
    /// <summary>The scheme a figure's target starts with.</summary>
    public const string Scheme = "figur:";

    /// <summary>The start of a drawing's resource key in <c>Themes/GuideFigures*.xaml</c>; the id follows.</summary>
    public const string KeyPrefix = "Figure.";

    /// <summary>Gets the drawing's resource key, or <see langword="null"/> when the figure has no id.</summary>
    public string? ResourceKey => Id.Length > 0 ? KeyPrefix + Id : null;
}

/// <summary>A block of code or a drawing in characters, written as it stands in Cascadia Mono.</summary>
/// <param name="Text">The lines, with their line breaks.</param>
public sealed record GuideCode(string Text) : GuideBlock;

/// <summary>An item of a list. A nested list's items follow their parent with a greater depth.</summary>
/// <param name="Runs">The item's text.</param>
/// <param name="Depth">0 for the outer list, 1 for a list inside it, and so on.</param>
/// <param name="Number">The item's number in a numbered list, or <see langword="null"/> for a bullet.</param>
public sealed record GuideListItem(IReadOnlyList<GuideRun> Runs, int Depth, int? Number);

/// <summary>A list, with nested lists flattened into its items.</summary>
/// <param name="Items">The items, in order.</param>
public sealed record GuideList(IReadOnlyList<GuideListItem> Items) : GuideBlock;

/// <summary>A table: a row of headers and the rows under it.</summary>
/// <param name="Header">The headers' cells.</param>
/// <param name="Rows">The rows, each with as many cells as the header.</param>
public sealed record GuideTable(IReadOnlyList<IReadOnlyList<GuideRun>> Header,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<GuideRun>>> Rows) : GuideBlock;

/// <summary>A parsed Guide text: its title and its blocks.</summary>
/// <param name="Title">The title, from the first <c>#</c> line.</param>
/// <param name="Blocks">The blocks after it.</param>
public sealed record GuideDocument(string Title, IReadOnlyList<GuideBlock> Blocks)
{
    /// <summary>Gets the section's headings, in order.</summary>
    public IEnumerable<GuideHeading> Headings => Blocks.OfType<GuideHeading>();

    /// <summary>Gets the section's figures, in order.</summary>
    public IEnumerable<GuideFigure> Figures => Blocks.OfType<GuideFigure>();

    /// <summary>Gets every link in the text, in order.</summary>
    public IEnumerable<GuideLink> Links => Blocks.SelectMany(RunsOf).Select(run => run.Link).OfType<GuideLink>();

    /// <summary>Gives every piece of text in a block.</summary>
    /// <param name="block">The block.</param>
    /// <returns>Its pieces; none for code.</returns>
    private static IEnumerable<GuideRun> RunsOf(GuideBlock block) => block switch
    {
        GuideHeading heading => heading.Runs,
        GuideParagraph paragraph => paragraph.Runs,
        GuideQuote quote => quote.Runs,
        GuideAnalogy analogy => analogy.Runs,
        GuideFigure figure => figure.Caption,
        GuideList list => list.Items.SelectMany(item => item.Runs),
        GuideTable table => table.Header.Concat(table.Rows.SelectMany(row => row)).SelectMany(cell => cell),
        _ => [],
    };
}
