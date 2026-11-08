using System.Text.RegularExpressions;

namespace Chainsaw.SharedKernel.Text;

/// <summary>
/// A change to a text as one replacement, and the caret or selection after it. Applied as one step, so that Ctrl+Z
/// undoes it in one go.
/// </summary>
/// <param name="Offset">Where the replaced text starts.</param>
/// <param name="Length">How long the replaced text is.</param>
/// <param name="Text">The text that takes its place.</param>
/// <param name="SelectionStart">Where the selection starts afterwards, in the changed text.</param>
/// <param name="SelectionLength">How long the selection is afterwards; 0 leaves just the caret.</param>
public readonly record struct TextEdit(int Offset, int Length, string Text, int SelectionStart, int SelectionLength)
{
    /// <summary>Applies the change to a text, as the editor does, for example in tests.</summary>
    /// <param name="text">The text before the change.</param>
    /// <returns>The text after the change.</returns>
    public string ApplyTo(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Concat(text.AsSpan(0, Offset), Text, text.AsSpan(Offset + Length));
    }
}

/// <summary>
/// The keys of a comment's editor (sketch KN1a) as pure functions of the text and the caret: Enter in a list goes on
/// with the list, and Tab and Shift+Tab move lines a level in or out. Taken over from Gawk's <c>MarkdownEdits</c>
/// (the user's decision 2026-11-07), with only the keys the note has.
/// </summary>
/// <remarks>
/// Kept free of WPF so that the rules are tested where WPF cannot run. A line may end in <c>\n</c> or
/// <c>\r\n</c>, since the editor writes the line breaks Windows uses; the <c>\r</c> is never taken for text.
/// </remarks>
public static partial class MarkdownEdits
{
    /// <summary>How far one level of a list is indented, as the Markdown reader reads it.</summary>
    public const string IndentUnit = "  ";

    /// <summary>
    /// Enter in a list or a quote: the next line gets the same mark (the next number for a numbered item). Enter on
    /// an item with nothing written steps out a level, or ends the list at the top, as in most editors.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="caret">Where the caret is; a selection is replaced first, as by any key.</param>
    /// <param name="newLine">The line break the text uses.</param>
    /// <returns>The change, or <see langword="null"/> when Enter is an ordinary new line.</returns>
    public static TextEdit? ContinueList(string text, int caret, string newLine)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(newLine);
        if (IsInCodeBlock(text, caret))
        {
            return null;
        }

        var lineStart = LineStart(text, caret);
        var line = text[lineStart..LineEnd(text, caret)];
        if (NextMark(line) is not { } marks)
        {
            return null;
        }

        var (prefix, next) = marks;

        // In the mark itself, Enter is an ordinary new line above the item.
        if (caret - lineStart < prefix.Length)
        {
            return null;
        }

        if (line[prefix.Length..].Trim().Length > 0)
        {
            var insert = newLine + next;
            return new TextEdit(caret, 0, insert, caret + insert.Length, 0);
        }

        // An empty nested item steps out a level; an empty item at the top ends the list.
        var list = ListPattern().Match(line);
        var replacement = list.Success && list.Groups[1].Length > 0
            ? Outdented(list.Groups[1].Value) + line.TrimStart(' ')
            : string.Empty;
        return new TextEdit(lineStart, line.Length, replacement, lineStart + replacement.Length, 0);
    }

    /// <summary>
    /// Tab and Shift+Tab: the selected lines move a level in or out. With just a caret, only a list item moves; on
    /// other lines Tab is an ordinary key.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">Where the selection starts.</param>
    /// <param name="length">How long the selection is.</param>
    /// <param name="outdent">Whether the lines move out (Shift+Tab).</param>
    /// <returns>The change, or <see langword="null"/> when nothing moves.</returns>
    public static TextEdit? Indent(string text, int start, int length, bool outdent)
    {
        ArgumentNullException.ThrowIfNull(text);
        var block = LineBlock.Of(text, start, length);
        var lines = block.Lines;
        if (lines.Length == 1 && !ListPattern().IsMatch(lines[0]) && !(outdent && lines[0].StartsWith(' ')))
        {
            return null;
        }

        var changed = new string[lines.Length];
        var prefixes = new (int Old, int New)[lines.Length];
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var spaces = LeadingSpaces(line);
            var newSpaces = IndentedSpaces(line, spaces, outdent);
            changed[i] = newSpaces + line[spaces.Length..];
            prefixes[i] = (spaces.Length, newSpaces.Length);
        }

        return changed.SequenceEqual(lines) ? null : block.Replace(changed, prefixes, start, length);
    }

    /// <summary>Gets a value indicating whether a position is in a code block, where Markdown is not read.</summary>
    /// <param name="text">The text.</param>
    /// <param name="position">The position.</param>
    /// <returns><see langword="true"/> when an odd number of fences stand above its line.</returns>
    public static bool IsInCodeBlock(string text, int position)
    {
        ArgumentNullException.ThrowIfNull(text);
        var inside = false;
        var lineStart = 0;
        var caretLine = LineStart(text, position);
        while (lineStart < caretLine)
        {
            var lineEnd = LineEnd(text, lineStart);
            if (IsFence(text.AsSpan(lineStart, lineEnd - lineStart)))
            {
                inside = !inside;
            }

            lineStart = NextLineStart(text, lineEnd);
        }

        return inside;
    }

    /// <summary>Gets a value indicating whether a line opens or closes a code block (<c>```</c>, <c>~~~</c>).</summary>
    /// <param name="line">The line.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public static bool IsFence(ReadOnlySpan<char> line)
    {
        var trimmed = line.TrimStart(' ');
        return line.Length - trimmed.Length <= 3
            && (trimmed.StartsWith("```", StringComparison.Ordinal)
                || trimmed.StartsWith("~~~", StringComparison.Ordinal));
    }

    /// <summary>Gets where the line holding a position starts.</summary>
    /// <param name="text">The text.</param>
    /// <param name="position">The position.</param>
    /// <returns>The offset of the line's first character.</returns>
    public static int LineStart(string text, int position)
    {
        ArgumentNullException.ThrowIfNull(text);
        return position <= 0 ? 0 : text.LastIndexOf('\n', Math.Min(position, text.Length) - 1) + 1;
    }

    /// <summary>Gets where the line holding a position ends, before its break (<c>\r\n</c> or <c>\n</c>).</summary>
    /// <param name="text">The text.</param>
    /// <param name="position">The position.</param>
    /// <returns>The offset just after the line's last character.</returns>
    public static int LineEnd(string text, int position)
    {
        ArgumentNullException.ThrowIfNull(text);
        var end = position >= text.Length ? -1 : text.IndexOf('\n', Math.Max(position, 0));
        if (end < 0)
        {
            return text.Length;
        }

        return end > 0 && text[end - 1] == '\r' ? end - 1 : end;
    }

    /// <summary>Gives the mark an item or a quote starts with, and the mark the next line gets.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The line's mark and the next line's, or <see langword="null"/> when the line has none.</returns>
    private static (string Prefix, string Next)? NextMark(string line)
    {
        var list = ListPattern().Match(line);
        if (list.Success)
        {
            var mark = list.Groups[2].Value;
            var nextMark = char.IsDigit(mark[0]) ? NextNumber(mark) : mark;
            return (list.Value, list.Groups[1].Value + nextMark + list.Groups[3].Value);
        }

        var quote = QuotePattern().Match(line);
        return quote.Success ? (quote.Value, "> ") : null;
    }

    /// <summary>Gives the spaces a line gets when it moves a level in or out; an empty line keeps its own.</summary>
    /// <param name="line">The line.</param>
    /// <param name="spaces">The spaces it starts with.</param>
    /// <param name="outdent">Whether it moves out.</param>
    /// <returns>The new spaces.</returns>
    private static string IndentedSpaces(string line, string spaces, bool outdent)
    {
        if (line.Trim().Length == 0)
        {
            return spaces;
        }

        return outdent ? Outdented(spaces) : spaces + IndentUnit;
    }

    /// <summary>Gives the next number of a numbered item's mark, <c>3.</c> after <c>2.</c>.</summary>
    /// <param name="mark">The mark, digits and <c>.</c> or <c>)</c>.</param>
    /// <returns>The next mark.</returns>
    private static string NextNumber(string mark)
    {
        var digits = mark[..^1];
        return int.TryParse(digits, out var n) && n < int.MaxValue ? $"{n + 1}{mark[^1]}" : mark;
    }

    /// <summary>Gives the spaces a line starts with.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The spaces.</returns>
    private static string LeadingSpaces(string line) => line[..(line.Length - line.TrimStart(' ').Length)];

    /// <summary>Takes one level off an indentation, or all of it when it is less than a level.</summary>
    /// <param name="spaces">The indentation.</param>
    /// <returns>The shorter indentation.</returns>
    private static string Outdented(string spaces) =>
        spaces.Length >= IndentUnit.Length ? spaces[IndentUnit.Length..] : string.Empty;

    /// <summary>Gives where the line after a line's end starts, past its line break.</summary>
    /// <param name="text">The text.</param>
    /// <param name="lineEnd">The line's end, before its break.</param>
    /// <returns>The next line's start; past the text at its end.</returns>
    private static int NextLineStart(string text, int lineEnd)
    {
        var breakAt = text.IndexOf('\n', lineEnd);
        return breakAt < 0 ? text.Length + 1 : breakAt + 1;
    }

    /// <summary>
    /// A list item: indentation, the mark (<c>-</c>, <c>*</c>, <c>+</c>, <c>1.</c>, <c>1)</c>) and the space after it.
    /// </summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^( *)([-*+]|\d{1,9}[.)])( +)")]
    private static partial Regex ListPattern();

    /// <summary>A quote's mark and the space after it.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^>[ ]?")]
    private static partial Regex QuotePattern();

    /// <summary>
    /// The whole lines a selection touches. A selection that ends at the very start of a line does not take that line,
    /// as in code editors.
    /// </summary>
    /// <param name="Start">Where the first line starts.</param>
    /// <param name="End">Where the last line ends, before its break.</param>
    /// <param name="Lines">
    /// The lines, each with any <c>\r</c> it ends in, so that joining them with <c>\n</c> keeps the text.
    /// </param>
    private readonly record struct LineBlock(int Start, int End, string[] Lines)
    {
        /// <summary>Finds the lines a selection touches.</summary>
        /// <param name="text">The text.</param>
        /// <param name="start">Where the selection starts.</param>
        /// <param name="length">How long it is.</param>
        /// <returns>The lines.</returns>
        public static LineBlock Of(string text, int start, int length)
        {
            var end = start + length;
            if (length > 0 && text[end - 1] == '\n')
            {
                end--;
            }

            var blockStart = LineStart(text, start);
            var blockEnd = LineEnd(text, Math.Max(end, blockStart));
            return new LineBlock(blockStart, blockEnd, text[blockStart..blockEnd].Split('\n'));
        }

        /// <summary>
        /// Replaces the lines. A caret stays where it was in the text of its line, past the mark; a selection grows to
        /// the whole lines, so that the next edit takes the same lines.
        /// </summary>
        /// <param name="changed">The new lines.</param>
        /// <param name="prefixes">How long each line's indentation was and is.</param>
        /// <param name="start">Where the selection started.</param>
        /// <param name="length">How long it was.</param>
        /// <returns>The change.</returns>
        public TextEdit Replace(string[] changed, (int Old, int New)[] prefixes, int start, int length)
        {
            var replacement = string.Join('\n', changed);
            return length == 0
                ? new TextEdit(Start, End - Start, replacement, Map(start, changed, prefixes), 0)
                : new TextEdit(Start, End - Start, replacement, Start, replacement.Length);
        }

        /// <summary>Gives where a caret stands after the lines have changed.</summary>
        /// <param name="position">Where it stood.</param>
        /// <param name="changed">The new lines.</param>
        /// <param name="prefixes">How long each line's indentation was and is.</param>
        /// <returns>Where it stands.</returns>
        private int Map(int position, string[] changed, (int Old, int New)[] prefixes)
        {
            var oldLineStart = Start;
            var newLineStart = Start;
            for (var i = 0; i < Lines.Length; i++)
            {
                var oldLineEnd = oldLineStart + Lines[i].Length;
                if (position <= oldLineEnd || i == Lines.Length - 1)
                {
                    var column = Math.Max(0, Math.Min(position, oldLineEnd) - oldLineStart - prefixes[i].Old);
                    return newLineStart + prefixes[i].New + column;
                }

                oldLineStart = oldLineEnd + 1;
                newLineStart += changed[i].Length + 1;
            }

            return newLineStart;
        }
    }
}
