using System.Text;
using Chainsaw.Modules.Requests.Contracts;

namespace Chainsaw.Modules.Requests;

/// <summary>
/// Writes a request's blocks as Markdown, which opens in any program and can be pasted into a request. A name from a
/// label is the user's text, so what Markdown would read as markup is escaped and the file says what the preview says.
/// </summary>
internal static class RequestMarkdown
{
    /// <summary>The characters Markdown reads as markup in running text.</summary>
    private const string Markup = "\\`*_[]#<>|~&";

    /// <summary>Writes the blocks.</summary>
    /// <param name="blocks">The blocks, the title first.</param>
    /// <returns>The file's text, with Unix line ends and one empty line between blocks.</returns>
    public static string Write(IReadOnlyList<RequestBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var text = new StringBuilder();
        foreach (var block in blocks)
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            WriteBlock(text, block);
        }

        return text.ToString();
    }

    /// <summary>Escapes what Markdown would read as markup, and keeps the text on one line.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped text.</returns>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var escaped = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsControl(c))
            {
                escaped.Append(' ');
                continue;
            }

            if (Markup.Contains(c, StringComparison.Ordinal))
            {
                escaped.Append('\\');
            }

            escaped.Append(c);
        }

        return escaped.ToString();
    }

    /// <summary>
    /// Escapes what Markdown reads as a list at the start of a line: <c>-</c>, <c>+</c> and a number with a point,
    /// such as <c>1.</c>, so that a paragraph is never read as a list.
    /// </summary>
    /// <param name="text">The escaped text.</param>
    /// <returns>The text, its start escaped where needed.</returns>
    public static string EscapeLineStart(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.StartsWith('-') || text.StartsWith('+'))
        {
            return "\\" + text;
        }

        var digits = text.TakeWhile(char.IsAsciiDigit).Count();
        return digits > 0 && digits < text.Length && text[digits] is '.' or ')'
            ? text[..digits] + "\\" + text[digits..]
            : text;
    }

    /// <summary>Writes one block and the line end after it.</summary>
    /// <param name="text">The file's text, added to.</param>
    /// <param name="block">The block.</param>
    private static void WriteBlock(StringBuilder text, RequestBlock block)
    {
        switch (block.Kind)
        {
            case RequestBlockKind.Title:
                text.Append("# ").Append(Escape(block.Text)).Append('\n');
                break;
            case RequestBlockKind.Heading:
                text.Append("## ").Append(Escape(block.Text)).Append('\n');
                break;
            case RequestBlockKind.Note:
                // Emphasis needs the text to touch its stars, so the ends are trimmed.
                text.Append('*').Append(Escape(block.Text.Trim())).Append("*\n");
                break;
            case RequestBlockKind.Table:
                WriteTable(text, block);
                break;
            default:
                text.Append(EscapeLineStart(Escape(block.Text))).Append('\n');
                break;
        }
    }

    /// <summary>Writes a table: the headings, the line under them and a row per row, mono cells as code.</summary>
    /// <param name="text">The file's text, added to.</param>
    /// <param name="block">The table.</param>
    private static void WriteTable(StringBuilder text, RequestBlock block)
    {
        WriteRow(text, [.. block.Columns.Select(Escape)]);
        WriteRow(text, [.. block.Columns.Select(_ => "---")]);
        foreach (var row in block.Rows)
        {
            WriteRow(text, [.. row.Select((cell, column) => Cell(cell, block.MonoColumns.Contains(column)))]);
        }
    }

    /// <summary>Writes one row of a table.</summary>
    /// <param name="text">The file's text, added to.</param>
    /// <param name="cells">The cells, already written.</param>
    private static void WriteRow(StringBuilder text, IReadOnlyList<string> cells) =>
        text.Append("| ").Append(string.Join(" | ", cells)).Append(" |\n");

    /// <summary>
    /// Writes a cell: an address, a transaction or a hash as code, so that it is copied whole; anything else
    /// escaped. A value that holds a backtick cannot be code and is escaped instead.
    /// </summary>
    /// <param name="cell">The cell's value.</param>
    /// <param name="isMono">Whether it is an address, a transaction or a hash.</param>
    /// <returns>The written cell.</returns>
    private static string Cell(string cell, bool isMono) =>
        isMono && !cell.Contains('`', StringComparison.Ordinal) && !cell.Contains('|', StringComparison.Ordinal)
            ? "`" + cell + "`"
            : Escape(cell);
}
