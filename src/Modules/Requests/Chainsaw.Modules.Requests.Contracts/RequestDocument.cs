namespace Chainsaw.Modules.Requests.Contracts;

/// <summary>What a block of a request's document is.</summary>
public enum RequestBlockKind
{
    /// <summary>The document's title, the first block (<c>#</c>).</summary>
    Title,

    /// <summary>A section's heading (<c>##</c>).</summary>
    Heading,

    /// <summary>A paragraph of the request's own text.</summary>
    Paragraph,

    /// <summary>A quieter line: who made it and when, or where the file's hash is.</summary>
    Note,

    /// <summary>A table: the addresses, or the deposits with their transactions, heights and hashes.</summary>
    Table,

    /// <summary>An item of a list (<c>-</c>): a conclusion, a cash-out or a label in a report.</summary>
    Item,

    /// <summary>
    /// Why the item before it holds (<c>&gt;</c> under the item): the heuristic, the source, the version and the
    /// transaction a conclusion rests on (principle 3).
    /// </summary>
    Reason,

    /// <summary>A hash written whole as code, such as the top hash of the case's hash chain.</summary>
    Hash,
}

/// <summary>
/// One block of a request's document. The view draws the blocks as the preview and the file is written from the very
/// same blocks, so that what the user sees is what is exported.
/// </summary>
/// <param name="Kind">What the block is.</param>
/// <param name="Text">The text of a title, a heading, a paragraph or a note; empty for a table.</param>
public sealed record RequestBlock(RequestBlockKind Kind, string Text)
{
    /// <summary>
    /// Gets a heading's tag, a short word on how the section ends, such as <c>Uttag funnet</c>; empty for none.
    /// </summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>Gets how a heading's tag is meant: what the trail it heads ends in; none for any other block.</summary>
    public RequestTagTone TagTone { get; init; }

    /// <summary>Gets a table's column headings; empty for any other block.</summary>
    public IReadOnlyList<string> Columns { get; init; } = [];

    /// <summary>Gets a table's rows, each with one cell per column; empty for any other block.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = [];

    /// <summary>
    /// Gets which of a table's columns hold addresses, transactions or hashes, which the view writes in mono
    /// (CLAUDE.md, <em>Typsnitt</em>) and shortens only when they do not fit.
    /// </summary>
    public IReadOnlySet<int> MonoColumns { get; init; } = new HashSet<int>();

    /// <summary>Gives a table.</summary>
    /// <param name="columns">The column headings.</param>
    /// <param name="rows">The rows, each with one cell per column.</param>
    /// <param name="mono">The columns written in mono.</param>
    /// <returns>The block.</returns>
    /// <exception cref="ArgumentException">A row does not have one cell per column.</exception>
    public static RequestBlock Table(
        IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows, params int[] mono)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Any(row => row.Count != columns.Count))
        {
            throw new ArgumentException("Every row has one cell per column.", nameof(rows));
        }

        return new RequestBlock(RequestBlockKind.Table, string.Empty)
        {
            Columns = columns,
            Rows = rows,
            MonoColumns = new HashSet<int>(mono),
        };
    }
}

/// <summary>What a heading's tag says about where a trail ends, which the preview gives its colour.</summary>
public enum RequestTagTone
{
    /// <summary>No tag, or a trail that goes on.</summary>
    None,

    /// <summary>The money left the chain at a cash-out (blue in the graph).</summary>
    CashOut,

    /// <summary>The trail ends at a risk: a mixer, a sanction, a darknet (red in the graph).</summary>
    Risk,

    /// <summary>The money was spent (yellow in the graph).</summary>
    Spent,

    /// <summary>The money goes on on another chain (purple in the graph).</summary>
    OtherChain,
}

/// <summary>A request composed from its template: the blocks, the Markdown written from them, the file's name.
/// </summary>
/// <param name="Title">The document's title, for example <c>Begäran om uppgifter · Börs A</c>.</param>
/// <param name="Blocks">The blocks in order, the title first.</param>
/// <param name="Markdown">The file's text, written from <see cref="Blocks"/>.</param>
/// <param name="FileName">The name the file is offered under, ending in <c>.md</c>, safe on Windows.</param>
public sealed record RequestDocument(
    string Title, IReadOnlyList<RequestBlock> Blocks, string Markdown, string FileName)
{
    /// <summary>
    /// Gets when the request says it was made; an export passes it back (<see cref="RequestDraft.MadeUtc"/>), so that
    /// the file says what the preview showed.
    /// </summary>
    public DateTimeOffset MadeUtc { get; init; }
}

/// <summary>
/// A request that was exported: sealed in the case with a line in the journal, so that the file can always be checked
/// against its hash (principle 1).
/// </summary>
/// <param name="Sequence">The record's place in the case's sealed chain of exports.</param>
/// <param name="Kind">What it asked for.</param>
/// <param name="Language">The language it was written in.</param>
/// <param name="TargetKey">The key it was sealed under (<see cref="RequestDraft.ExportKey"/>).</param>
/// <param name="TargetName">The recipient's name.</param>
/// <param name="FileName">The file's name as written, with its number when another file had the name.</param>
/// <param name="Path">Where the file was written.</param>
/// <param name="Sha256">The file's SHA-256, in lower-case hex.</param>
/// <param name="Transactions">The transactions of the deposits it took along.</param>
/// <param name="Utc">When it was exported, in UTC.</param>
/// <param name="JournalSequence">The journal entry that records it.</param>
public sealed record RequestExport(
    long Sequence,
    RequestKind Kind,
    RequestLanguage Language,
    string TargetKey,
    string TargetName,
    string FileName,
    string Path,
    string Sha256,
    IReadOnlyList<string> Transactions,
    DateTimeOffset Utc,
    long JournalSequence);
