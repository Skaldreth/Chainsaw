using System.Text;

namespace Chainsaw.SharedKernel.Text;

/// <summary>What a line of a note is, which sets how it is drawn.</summary>
public enum MarkdownBlockKind
{
    /// <summary>Plain text.</summary>
    Paragraph,

    /// <summary>A heading, from <c># rubrik</c>.</summary>
    Heading,

    /// <summary>A subheading, from <c>## underrubrik</c>, and the deeper levels <c>###</c> to <c>######</c>.</summary>
    Subheading,

    /// <summary>A bullet, from <c>- punkt</c>, <c>* punkt</c> or <c>+ punkt</c>.</summary>
    Bullet,

    /// <summary>
/// A numbered item, from <c>1. punkt</c> or <c>1) punkt</c>; <see cref="MarkdownBlock.Marker"/> holds the number as
/// typed.
/// </summary>
    Numbered,

    /// <summary>A quote, from <c>&gt; citat</c>.</summary>
    Quote,

    /// <summary>Lines between <c>```</c> fences, kept exactly as typed and never read as Markdown.</summary>
    Code,
}

/// <summary>How a piece of text within a line is drawn; the styles combine, e.g. bold and italic.</summary>
[Flags]
public enum MarkdownStyle
{
    /// <summary>Plain text.</summary>
    None = 0,

    /// <summary>Bold, from <c>**fet**</c> or <c>__fet__</c>.</summary>
    Bold = 1,

    /// <summary>Italic, from <c>*kursiv*</c> or <c>_kursiv_</c>.</summary>
    Italic = 2,

    /// <summary>Code, from <c>`kod`</c>, drawn in mono on a grey background.</summary>
    Code = 4,
}

/// <summary>A piece of text within a line.</summary>
/// <param name="Text">The text, without the marks around it.</param>
/// <param name="Style">How it is drawn.</param>
/// <param name="Target">
/// The address when the piece is (part of) a link, from <c>[text](adress)</c>, otherwise <see langword="null"/>.
/// </param>
public sealed record MarkdownSpan(string Text, MarkdownStyle Style = MarkdownStyle.None, string? Target = null)
{
    /// <summary>Gets a value indicating whether the piece is part of a link.</summary>
    public bool IsLink => Target is not null;
}

/// <summary>A line of a note, or a whole fenced code block.</summary>
/// <param name="Kind">What the line is.</param>
/// <param name="Spans">
/// Its text, piece by piece. A code block is one piece whose lines are separated by <c>\n</c>.
/// </param>
/// <param name="Level">
/// How deep a bullet or numbered item is indented: 0 at the margin, at most <see cref="Markdown.MaxLevel"/>.
/// </param>
/// <param name="Marker">
/// The number of a numbered item as typed, e.g. <c>2.</c>, otherwise <see langword="null"/>.
/// </param>
public sealed record MarkdownBlock(
    MarkdownBlockKind Kind, IReadOnlyList<MarkdownSpan> Spans, int Level = 0, string? Marker = null)
{
    /// <summary>Gets the text without any marks, e.g. for a title or a tooltip.</summary>
    public string PlainText => string.Concat(Spans.Select(s => s.Text));

    /// <summary>Gets how many lines the block was typed as: the lines of a code block, otherwise one.</summary>
    public int LineCount => Kind == MarkdownBlockKind.Code ? PlainText.Split('\n').Length : 1;
}

/// <summary>A block of a note with the lines it was typed as.</summary>
/// <param name="Block">The block.</param>
/// <param name="Source">The lines as typed, with a code block's fences.</param>
/// <param name="IsClosed">
/// Whether the block ends where it should: <see langword="false"/> for a code block whose fence is never closed, which
/// runs to the end of the note.
/// </param>
public sealed record MarkdownSourceBlock(MarkdownBlock Block, string Source, bool IsClosed = true)
{
    /// <summary>Gets a value indicating whether the block is a code block without lines, which draws nothing.</summary>
    public bool IsEmpty => Block.Kind == MarkdownBlockKind.Code && Block.Spans.Count == 0;
}

/// <summary>
/// The part of Markdown that notes and the graph's comments use (taken over from Gawk, the user's decision 2026-11-07,
/// for sketch KN1a): headings, bullets and numbered items, quotes, fenced code,
/// bold, italic, code and links, following CommonMark where it applies.
/// </summary>
/// <remarks>
/// Every non-empty line is a block of its own, so a note reads the same as it was typed. Marks that are not closed
/// are kept as they are, since a note is material in an investigation and nothing written in it may disappear;
/// only a backslash before a punctuation mark, which the writer typed to keep the mark, is left out.
/// </remarks>
public static class Markdown
{
    /// <summary>The deepest indentation of a bullet or numbered item.</summary>
    public const int MaxLevel = 3;

    /// <summary>Splits a note into its lines and their pieces.</summary>
    /// <param name="text">The note.</param>
    /// <returns>The non-empty lines and code blocks, in order.</returns>
    public static IReadOnlyList<MarkdownBlock> Parse(string? text) =>
        [.. ReadBlocks(text).Where(b => !b.IsEmpty).Select(b => b.Block)];

    /// <summary>
    /// Splits a note into its blocks, each with the lines it was typed as, so that what the pieces no longer show can
    /// still be read, e.g. a link written inside code. An empty code block is kept here, unlike in
    /// <see cref="Parse"/>, so that a fence that is never closed is seen.
    /// </summary>
    /// <param name="text">The note.</param>
    /// <returns>The non-empty lines and the code blocks, in order.</returns>
    public static IReadOnlyList<MarkdownSourceBlock> ReadBlocks(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var blocks = new List<MarkdownSourceBlock>();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (line.Trim().Length == 0)
            {
                continue;
            }

            if (TryReadFence(line, out var fence))
            {
                // Everything up to the closing fence, or the end of the note if there is none, as CommonMark does.
                var first = i;
                var code = new List<string>();
                for (i++; i < lines.Length && !IsClosingFence(lines[i], fence); i++)
                {
                    code.Add(lines[i].TrimEnd());
                }

                var isClosed = i < lines.Length;
                var last = Math.Min(i, lines.Length - 1);
                var source = string.Join('\n', lines[first..(last + 1)]);
                var block = new MarkdownBlock(MarkdownBlockKind.Code, CodeSpans(code));
                blocks.Add(new MarkdownSourceBlock(block, source, isClosed));
                continue;
            }

            blocks.Add(new MarkdownSourceBlock(ParseLine(line), lines[i]));
        }

        return blocks;
    }

    /// <summary>The one piece of a code block, its lines separated by <c>\n</c>; none when it has no lines.</summary>
    /// <param name="code">The block's lines.</param>
    /// <returns>The piece, or none.</returns>
    private static MarkdownSpan[] CodeSpans(List<string> code) =>
        code.Count == 0 ? [] : [new MarkdownSpan(string.Join('\n', code), MarkdownStyle.Code)];

    /// <summary>Splits a line into plain, bold, italic, code and link pieces.</summary>
    /// <param name="text">The line, without its block mark.</param>
    /// <returns>The pieces, in order; adjacent pieces drawn the same way are one piece.</returns>
    public static IReadOnlyList<MarkdownSpan> ParseInline(string text)
    {
        var spans = new List<MarkdownSpan>();
        Flatten(ParseNodes(text), MarkdownStyle.None, null, spans);
        return spans;
    }

    /// <summary>Reads one line that is not part of a code block.</summary>
    /// <param name="line">The line, without spaces at its end.</param>
    /// <returns>The block.</returns>
    private static MarkdownBlock ParseLine(string line)
    {
        // A tab counts as four spaces, and two spaces make one level.
        var indent = 0;
        for (var i = 0; i < line.Length && line[i] is ' ' or '\t'; i++)
        {
            indent += line[i] == '\t' ? 4 : 1;
        }

        var content = line.TrimStart();
        var level = Math.Min(indent / 2, MaxLevel);

        var hashes = 0;
        while (hashes < content.Length && content[hashes] == '#')
        {
            hashes++;
        }

        if (hashes is >= 1 and <= 6 && hashes < content.Length && content[hashes] == ' '
            && content[hashes..].Trim().Length > 0)
        {
            var kind = hashes == 1 ? MarkdownBlockKind.Heading : MarkdownBlockKind.Subheading;
            return new MarkdownBlock(kind, ParseInline(content[(hashes + 1)..].Trim()));
        }

        if (content.Length > 2 && content[0] is '-' or '*' or '+' && content[1] == ' '
            && content[2..].Trim().Length > 0)
        {
            return new MarkdownBlock(MarkdownBlockKind.Bullet, ParseInline(content[2..].Trim()), level);
        }

        var digits = 0;
        while (digits < content.Length && digits < 9 && char.IsAsciiDigit(content[digits]))
        {
            digits++;
        }

        if (digits > 0 && digits + 2 < content.Length && content[digits] is '.' or ')' && content[digits + 1] == ' '
            && content[(digits + 2)..].Trim().Length > 0)
        {
            return new MarkdownBlock(
                MarkdownBlockKind.Numbered,
                ParseInline(content[(digits + 2)..].Trim()),
                level,
                content[..(digits + 1)]);
        }

        // "> citat" and ">citat" alike; a lone ">" is an empty quote line and kept as text, so that nothing disappears.
        if (content.Length > 1 && content[0] == '>' && content[1..].Trim().Length > 0)
        {
            return new MarkdownBlock(MarkdownBlockKind.Quote, ParseInline(content[1..].Trim()));
        }

        return new MarkdownBlock(MarkdownBlockKind.Paragraph, ParseInline(content));
    }

    /// <summary>Reads whether a line opens a code block, with three or more backticks or tildes.</summary>
    /// <param name="line">The line.</param>
    /// <param name="fence">The fence's marks, for finding where the block closes.</param>
    /// <returns><see langword="true"/> when the line is a fence.</returns>
    private static bool TryReadFence(string line, out string fence)
    {
        var trimmed = line.TrimStart();
        fence = string.Empty;
        if (trimmed.Length < 3 || trimmed[0] is not ('`' or '~'))
        {
            return false;
        }

        var length = 0;
        while (length < trimmed.Length && trimmed[length] == trimmed[0])
        {
            length++;
        }

        // A backtick fence may not have backticks after it, or "```kod``` i en rad" would start a block.
        if (length < 3 || (trimmed[0] == '`' && trimmed.AsSpan(length).Contains('`')))
        {
            return false;
        }

        fence = trimmed[..length];
        return true;
    }

    /// <summary>Reads whether a line closes a code block: as many of its marks or more, and nothing else.</summary>
    /// <param name="line">The line.</param>
    /// <param name="fence">The opening fence.</param>
    /// <returns><see langword="true"/> when it closes the block.</returns>
    private static bool IsClosingFence(string line, string fence)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= fence.Length && trimmed.All(c => c == fence[0]);
    }

    // ---- Inline: the CommonMark tokens and its algorithm for emphasis, cut down to what notes use. ----

    /// <summary>A piece of a line while it is read.</summary>
    private abstract class Node;

    /// <summary>Plain text.</summary>
    /// <param name="text">The text.</param>
    private sealed class TextNode(string text) : Node
    {
        /// <summary>Gets the text.</summary>
        public string Text { get; } = text;
    }

    /// <summary>Code between backticks.</summary>
    /// <param name="text">The code.</param>
    private sealed class CodeNode(string text) : Node
    {
        /// <summary>Gets the code.</summary>
        public string Text { get; } = text;
    }

    /// <summary>A link with the pieces of its text.</summary>
    /// <param name="children">The pieces of its text.</param>
    /// <param name="target">Its address.</param>
    private sealed class LinkNode(List<Node> children, string target) : Node
    {
        /// <summary>Gets the pieces of its text.</summary>
        public List<Node> Children { get; } = children;

        /// <summary>Gets its address.</summary>
        public string Target { get; } = target;
    }

    /// <summary>Bold or italic text.</summary>
    /// <param name="children">The pieces it holds.</param>
    /// <param name="strong">Whether it is bold.</param>
    private sealed class EmphasisNode(List<Node> children, bool strong) : Node
    {
        /// <summary>Gets the pieces it holds.</summary>
        public List<Node> Children { get; } = children;

        /// <summary>Gets a value indicating whether it is bold.</summary>
        public bool Strong { get; } = strong;
    }

    /// <summary>
    /// A run of <c>*</c> or <c>_</c> that may open or close emphasis; what is left of it is drawn as text.
    /// </summary>
    /// <param name="mark">The mark.</param>
    /// <param name="count">How many marks the run has.</param>
    /// <param name="canOpen">Whether the run may open emphasis.</param>
    /// <param name="canClose">Whether the run may close emphasis.</param>
    private sealed class DelimiterNode(char mark, int count, bool canOpen, bool canClose) : Node
    {
        /// <summary>Gets the mark.</summary>
        public char Mark { get; } = mark;

        /// <summary>Gets how many marks the run had when it was read.</summary>
        public int OriginalCount { get; } = count;

        /// <summary>Gets or sets how many marks are left after the pairing.</summary>
        public int Count { get; set; } = count;

        /// <summary>Gets a value indicating whether the run may open emphasis.</summary>
        public bool CanOpen { get; } = canOpen;

        /// <summary>Gets a value indicating whether the run may close emphasis.</summary>
        public bool CanClose { get; } = canClose;
    }

    /// <summary>Reads a line's pieces: text, code, links and runs of marks, then pairs the marks.</summary>
    /// <param name="text">The line.</param>
    /// <returns>The pieces.</returns>
    private static List<Node> ParseNodes(string text)
    {
        var nodes = new LinkedList<Node>();
        var plain = new StringBuilder();

        // Ends the plain text read so far as a piece of its own.
        void FlushPlain()
        {
            if (plain.Length > 0)
            {
                nodes.AddLast(new TextNode(plain.ToString()));
                plain.Clear();
            }
        }

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && char.IsAscii(text[i + 1]) && IsPunctuation(text[i + 1]))
            {
                plain.Append(text[i + 1]);
                i += 2;
            }
            else if (c == '`')
            {
                var run = RunLength(text, i);
                var close = FindBacktickRun(text, i + run, run);
                if (close < 0)
                {
                    plain.Append('`', run);
                    i += run;
                    continue;
                }

                FlushPlain();
                nodes.AddLast(new CodeNode(TrimCode(text[(i + run)..close])));
                i = close + run;
            }
            else if (c == '[' && TryReadLink(text, i, out var link, out var length))
            {
                FlushPlain();
                nodes.AddLast(link);
                i += length;
            }
            else if (c is '*' or '_')
            {
                var run = RunLength(text, i);
                var before = i > 0 ? text[i - 1] : ' ';
                var after = i + run < text.Length ? text[i + run] : ' ';
                var left = !char.IsWhiteSpace(after)
                    && (!IsPunctuation(after) || char.IsWhiteSpace(before) || IsPunctuation(before));
                var right = !char.IsWhiteSpace(before)
                    && (!IsPunctuation(before) || char.IsWhiteSpace(after) || IsPunctuation(after));

                // An underscore inside a word is part of it, e.g. snake_case_namn.
                var canOpen = c == '*' ? left : left && (!right || IsPunctuation(before));
                var canClose = c == '*' ? right : right && (!left || IsPunctuation(after));
                FlushPlain();
                nodes.AddLast(new DelimiterNode(c, run, canOpen, canClose));
                i += run;
            }
            else
            {
                plain.Append(c);
                i++;
            }
        }

        FlushPlain();
        ProcessEmphasis(nodes);
        return [.. nodes];
    }

    /// <summary>
    /// Pairs openers with closers as CommonMark does: each closer, left to right, takes the nearest opener of the same
    /// mark, two marks when both have them (bold), otherwise one (italic). Unpaired marks stay as text.
    /// </summary>
    /// <param name="nodes">The pieces, changed in place.</param>
    private static void ProcessEmphasis(LinkedList<Node> nodes)
    {
        var current = nodes.First;
        while (current is not null)
        {
            if (current.Value is not DelimiterNode { CanClose: true, Count: > 0 } closer)
            {
                current = current.Next;
                continue;
            }

            var opener = current.Previous;
            while (opener is not null && !Matches(opener.Value, closer))
            {
                opener = opener.Previous;
            }

            if (opener is null)
            {
                current = current.Next;
                continue;
            }

            var open = (DelimiterNode)opener.Value;
            var strong = open.Count >= 2 && closer.Count >= 2;
            var used = strong ? 2 : 1;

            var children = new List<Node>();
            while (opener.Next != current)
            {
                children.Add(opener.Next!.Value);
                nodes.Remove(opener.Next!);
            }

            nodes.AddAfter(opener, new EmphasisNode(children, strong));
            open.Count -= used;
            closer.Count -= used;
            if (open.Count == 0)
            {
                nodes.Remove(opener);
            }

            if (closer.Count == 0)
            {
                var next = current.Next;
                nodes.Remove(current);
                current = next;
            }
        }
    }

    /// <summary>Reads whether a piece is an opener that a closer may pair with.</summary>
    /// <param name="node">The piece.</param>
    /// <param name="closer">The closer.</param>
    /// <returns><see langword="true"/> when they pair.</returns>
    private static bool Matches(Node node, DelimiterNode closer)
    {
        if (node is not DelimiterNode { CanOpen: true, Count: > 0 } opener || opener.Mark != closer.Mark)
        {
            return false;
        }

        // The rule of three, so that "*a**b*" pairs the single stars.
        var bothWays = opener.CanClose || closer.CanOpen;
        return !bothWays
            || (opener.OriginalCount + closer.OriginalCount) % 3 != 0
            || (opener.OriginalCount % 3 == 0 && closer.OriginalCount % 3 == 0);
    }

    /// <summary>Reads a link, <c>[text](adress)</c>, where a bracket opens.</summary>
    /// <param name="text">The line.</param>
    /// <param name="start">Where the bracket is.</param>
    /// <param name="link">The link.</param>
    /// <param name="length">How many characters it takes.</param>
    /// <returns><see langword="true"/> when a link stands there.</returns>
    private static bool TryReadLink(string text, int start, out LinkNode link, out int length)
    {
        link = null!;
        length = 0;

        // The text ends at the matching bracket; escaped brackets and brackets in code do not count.
        var depth = 0;
        var close = -1;
        for (var i = start; i < text.Length && close < 0; i++)
        {
            switch (text[i])
            {
                case '\\':
                    i++;
                    break;
                case '`':
                    var run = RunLength(text, i);
                    var end = FindBacktickRun(text, i + run, run);
                    i = end < 0 ? i + run - 1 : end + run - 1;
                    break;
                case '[':
                    depth++;
                    break;
                case ']' when --depth == 0:
                    close = i;
                    break;
            }
        }

        if (close <= start + 1 || close + 1 >= text.Length || text[close + 1] != '(')
        {
            return false;
        }

        // The address may hold balanced parentheses, e.g. https://sv.wikipedia.org/wiki/A_(B), but no spaces.
        var parens = 0;
        var targetEnd = -1;
        for (var i = close + 2; i < text.Length && targetEnd < 0; i++)
        {
            switch (text[i])
            {
                case '\\':
                    i++;
                    break;
                case '(':
                    parens++;
                    break;
                case ')' when parens == 0:
                    targetEnd = i;
                    break;
                case ')':
                    parens--;
                    break;
                case var c when char.IsWhiteSpace(c):
                    return false;
            }
        }

        if (targetEnd <= close + 2)
        {
            return false;
        }

        link = new LinkNode(ParseNodes(text[(start + 1)..close]), text[(close + 2)..targetEnd]);
        length = targetEnd - start + 1;
        return true;
    }

    /// <summary>Writes the pieces as spans, each with the styles and link around it.</summary>
    /// <param name="nodes">The pieces.</param>
    /// <param name="style">The styles around them.</param>
    /// <param name="target">The link around them, or <see langword="null"/>.</param>
    /// <param name="spans">Where the spans go.</param>
    private static void Flatten(
        IEnumerable<Node> nodes, MarkdownStyle style, string? target, List<MarkdownSpan> spans)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode text:
                    Add(spans, text.Text, style, target);
                    break;
                case DelimiterNode delimiter when delimiter.Count > 0:
                    Add(spans, new string(delimiter.Mark, delimiter.Count), style, target);
                    break;
                case CodeNode code:
                    Add(spans, code.Text, style | MarkdownStyle.Code, target);
                    break;
                case LinkNode link:
                    Flatten(link.Children, style, link.Target, spans);
                    break;
                case EmphasisNode emphasis:
                    var emphasised = emphasis.Strong ? MarkdownStyle.Bold : MarkdownStyle.Italic;
                    Flatten(emphasis.Children, style | emphasised, target, spans);
                    break;
            }
        }
    }

    /// <summary>Adds a span, joined to the one before when it is drawn the same way.</summary>
    /// <param name="spans">The spans.</param>
    /// <param name="text">The text.</param>
    /// <param name="style">Its styles.</param>
    /// <param name="target">Its link, or <see langword="null"/>.</param>
    private static void Add(List<MarkdownSpan> spans, string text, MarkdownStyle style, string? target)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (spans.Count > 0 && spans[^1] is var last && last.Style == style && last.Target == target)
        {
            spans[^1] = last with { Text = last.Text + text };
        }
        else
        {
            spans.Add(new MarkdownSpan(text, style, target));
        }
    }

    /// <summary>Counts how many of the same character stand in a row.</summary>
    /// <param name="text">The text.</param>
    /// <param name="start">Where the run starts.</param>
    /// <returns>Its length.</returns>
    private static int RunLength(string text, int start)
    {
        var end = start;
        while (end < text.Length && text[end] == text[start])
        {
            end++;
        }

        return end - start;
    }

    /// <summary>Finds a run of exactly <paramref name="length"/> backticks from <paramref name="start"/>.</summary>
    /// <param name="text">The text.</param>
    /// <param name="start">Where to look from.</param>
    /// <param name="length">The run's length.</param>
    /// <returns>Where it starts, or -1.</returns>
    private static int FindBacktickRun(string text, int start, int length)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] != '`')
            {
                continue;
            }

            var run = RunLength(text, i);
            if (run == length)
            {
                return i;
            }

            i += run - 1;
        }

        return -1;
    }

    /// <summary>Removes one space on each side, so that "`` `kod` ``" can show backticks, as CommonMark does.</summary>
    /// <param name="code">The code between the backticks.</param>
    /// <returns>The code to draw.</returns>
    private static string TrimCode(string code) =>
        code.Length >= 2 && code[0] == ' ' && code[^1] == ' ' && code.Trim().Length > 0 ? code[1..^1] : code;

    /// <summary>Reads whether a character is punctuation or a symbol, as CommonMark counts them.</summary>
    /// <param name="c">The character.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool IsPunctuation(char c) => char.IsPunctuation(c) || char.IsSymbol(c);
}
