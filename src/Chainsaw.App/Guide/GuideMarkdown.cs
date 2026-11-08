using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Chainsaw.App.Guide;

/// <summary>
/// Reads the Guide's texts in <c>docs/guide</c>: a small Markdown reader for what the texts use, and nothing more.
/// Headings (<c>#</c>, <c>##</c>, <c>###</c>), paragraphs, lists (<c>-</c> and <c>1.</c>, nested by indentation),
/// quotes (<c>&gt;</c>, and <c>&gt; **Som i vardagen:**</c> as an analogy), figures (<c>![Bildtext](figur:id)</c> on
/// a line of their own), code between <c>```</c>, tables, and inside the text <c>**bold**</c>, <c>*italic*</c>,
/// <c>`code`</c> and links to other sections (<c>[text](vaxeladress.md#anchor)</c> or <c>[text](#anchor)</c>).
/// Anything else is read as plain text, so a text never fails to show.
/// </summary>
public static partial class GuideMarkdown
{
    /// <summary>The marker that opens and closes a block of code.</summary>
    private const string Fence = "```";

    /// <summary>Reads a section's text.</summary>
    /// <param name="section">The section's id, so that a link inside it (<c>#anchor</c>) knows where it points.</param>
    /// <param name="markdown">The text.</param>
    /// <returns>The title and the blocks; the title is the section's id when the text has no <c>#</c> line.</returns>
    public static GuideDocument Parse(string section, string markdown)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(markdown);
        var reader = new BlockReader(section, markdown.ReplaceLineEndings("\n").Split('\n'));
        return reader.Read();
    }

    /// <summary>Reads a line of text into its styled pieces.</summary>
    /// <param name="section">The section the text is in.</param>
    /// <param name="text">The text.</param>
    /// <returns>The pieces, with neighbours of the same style and link joined.</returns>
    public static IReadOnlyList<GuideRun> Inline(string section, string text)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(text);
        var runs = new List<GuideRun>();
        Scan(section, text, GuideRunStyle.None, null, runs);
        return Join(runs);
    }

    /// <summary>
    /// Gives a heading's anchor: lower case, letters (å, ä and ö too) and digits kept, spaces and dashes made one
    /// dash, the rest left out. <c>FIFO (först in, först ut)</c> becomes <c>fifo-först-in-först-ut</c>.
    /// </summary>
    /// <param name="heading">The heading's plain text.</param>
    /// <returns>The anchor.</returns>
    public static string Anchor(string heading)
    {
        ArgumentNullException.ThrowIfNull(heading);
        var anchor = new StringBuilder(heading.Length);
        foreach (var character in heading.ToLower(CultureInfo.InvariantCulture))
        {
            if (char.IsLetterOrDigit(character))
            {
                anchor.Append(character);
            }
            else if (character is ' ' or '-' && anchor.Length > 0 && anchor[^1] != '-')
            {
                anchor.Append('-');
            }
        }

        return anchor.ToString().TrimEnd('-');
    }

    /// <summary>Writes styled pieces as plain text.</summary>
    /// <param name="runs">The pieces.</param>
    /// <returns>Their text, joined.</returns>
    public static string Plain(IEnumerable<GuideRun> runs) => string.Concat(runs.Select(run => run.Text));

    /// <summary>Reads a link's target: <c>file.md</c>, <c>file.md#anchor</c> or <c>#anchor</c>.</summary>
    /// <param name="section">The section the link is in.</param>
    /// <param name="target">The target, as written between the parentheses.</param>
    /// <returns>The link, or <see langword="null"/> for anything else (a web address is never followed).</returns>
    public static GuideLink? Target(string section, string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var hash = target.IndexOf('#', StringComparison.Ordinal);
        var file = hash < 0 ? target : target[..hash];
        var anchor = hash < 0 ? null : target[(hash + 1)..];
        if (file.Length == 0)
        {
            return anchor is { Length: > 0 } ? new GuideLink(section, anchor) : null;
        }

        var ending = GuideLibrary.FileEnding;
        if (!file.EndsWith(ending, StringComparison.Ordinal) || !SectionId().IsMatch(file[..^ending.Length]))
        {
            return null;
        }

        return new GuideLink(file[..^ending.Length], anchor is { Length: > 0 } ? anchor : null);
    }

    /// <summary>
    /// Reads text into pieces: code spans first (nothing inside them is a marker), then bold, italic and links. A
    /// marker without its closing partner is kept as text.
    /// </summary>
    /// <param name="section">The section the text is in.</param>
    /// <param name="text">The text.</param>
    /// <param name="outer">The style around the text.</param>
    /// <param name="link">The link around the text, or <see langword="null"/>.</param>
    /// <param name="runs">Where the pieces go.</param>
    private static void Scan(string section, string text, GuideRunStyle outer, GuideLink? link, List<GuideRun> runs)
    {
        var buffer = new StringBuilder();
        var style = outer;

        // Adds the text gathered so far as a piece in the style so far, before the style or the link changes.
        void Flush()
        {
            if (buffer.Length > 0)
            {
                runs.Add(new GuideRun(buffer.ToString(), style, link));
                buffer.Clear();
            }
        }

        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '`' && text.IndexOf('`', i + 1) is var end and > 0)
            {
                Flush();
                runs.Add(new GuideRun(text[(i + 1)..end], style | GuideRunStyle.Code, link));
                i = end + 1;
            }
            else if (string.CompareOrdinal(text, i, "**", 0, 2) == 0
                && Toggles(text, i, "**", style, GuideRunStyle.Strong))
            {
                Flush();
                style ^= GuideRunStyle.Strong;
                i += 2;
            }
            else if (text[i] == '*' && Toggles(text, i, "*", style, GuideRunStyle.Emphasis))
            {
                Flush();
                style ^= GuideRunStyle.Emphasis;
                i += 1;
            }
            else if (text[i] == '[' && link is null && LinkAt(section, text, i) is var (inner, target, next))
            {
                Flush();
                Scan(section, inner, style, target, runs);
                i = next;
            }
            else
            {
                buffer.Append(text[i]);
                i++;
            }
        }

        Flush();
    }

    /// <summary>
    /// Says whether a marker at a place turns its style on or off: off when the style is on, on when the marker is
    /// closed later in the text (and a single <c>*</c> is not half of a <c>**</c>).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="at">Where the marker is.</param>
    /// <param name="marker">The marker.</param>
    /// <param name="style">The style now.</param>
    /// <param name="flag">The style the marker stands for.</param>
    /// <returns><see langword="true"/> when the marker is a marker, not text.</returns>
    private static bool Toggles(string text, int at, string marker, GuideRunStyle style, GuideRunStyle flag)
    {
        if (style.HasFlag(flag))
        {
            return true;
        }

        var after = at + marker.Length;
        if (after >= text.Length || char.IsWhiteSpace(text[after]))
        {
            return false;
        }

        if (marker.Length == 1)
        {
            for (var i = after; i < text.Length; i++)
            {
                if (text[i] == '*' && (i + 1 >= text.Length || text[i + 1] != '*') && text[i - 1] != '*')
                {
                    return true;
                }
            }

            return false;
        }

        return text.IndexOf(marker, after, StringComparison.Ordinal) > after;
    }

    /// <summary>Reads a link that starts at a <c>[</c>: <c>[text](target)</c>.</summary>
    /// <param name="section">The section the text is in.</param>
    /// <param name="text">The text.</param>
    /// <param name="at">Where the <c>[</c> is.</param>
    /// <returns>
    /// The link's text, its target (<see langword="null"/> when it does not point into the Guide) and where the
    /// text goes on; <see langword="null"/> when there is no link here.
    /// </returns>
    private static (string Inner, GuideLink? Target, int Next)? LinkAt(string section, string text, int at)
    {
        var close = text.IndexOf("](", at + 1, StringComparison.Ordinal);
        if (close < 0 || text.IndexOf('[', at + 1, close - at - 1) >= 0)
        {
            return null;
        }

        var end = text.IndexOf(')', close + 2);
        if (end < 0)
        {
            return null;
        }

        return (text[(at + 1)..close], Target(section, text[(close + 2)..end]), end + 1);
    }

    /// <summary>Joins neighbouring pieces of the same style and link.</summary>
    /// <param name="runs">The pieces.</param>
    /// <returns>The joined pieces.</returns>
    private static IReadOnlyList<GuideRun> Join(List<GuideRun> runs)
    {
        var joined = new List<GuideRun>(runs.Count);
        foreach (var run in runs.Where(run => run.Text.Length > 0))
        {
            if (joined.Count > 0 && joined[^1] is var last && last.Style == run.Style && last.Link == run.Link)
            {
                joined[^1] = last with { Text = last.Text + run.Text };
            }
            else
            {
                joined.Add(run);
            }
        }

        return joined;
    }

    /// <summary>A section's id: lower case letters, digits and dashes.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex("^[a-z0-9-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SectionId();

    /// <summary>A list item's start: its indentation, and a dash, a star or a number with a dot.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^(?<indent> *)(?:[-*]|(?<number>\d+)\.) (?<text>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ListItem();

    /// <summary>A heading: one to three <c>#</c> and a space.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^(?<level>#{1,3}) (?<text>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex Heading();

    /// <summary>
    /// A figure on a line of its own: <c>![caption](target)</c>, where the caption may hold links of its own.
    /// </summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(
        @"^!\[(?<caption>(?:[^\[\]]|\[[^\[\]]*\]\([^)]*\))*)\]\((?<target>[^)]*)\)\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Figure();

    /// <summary>The line under a table's headers: dashes and colons between pipes.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^\|?(\s*:?-+:?\s*\|)+\s*:?-*:?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TableRule();

    /// <summary>Reads a text's lines into blocks, one block at a time.</summary>
    /// <param name="section">The section's id.</param>
    /// <param name="lines">The lines.</param>
    private sealed class BlockReader(string section, string[] lines)
    {
        /// <summary>The blocks read so far.</summary>
        private readonly List<GuideBlock> _blocks = [];

        /// <summary>The anchors given so far, so that two headings with the same text get different ones.</summary>
        private readonly HashSet<string> _anchors = new(StringComparer.Ordinal);

        /// <summary>The line being read.</summary>
        private int _at;

        /// <summary>The title, once read.</summary>
        private string? _title;

        /// <summary>Reads every line.</summary>
        /// <returns>The document.</returns>
        public GuideDocument Read()
        {
            while (_at < lines.Length)
            {
                var line = lines[_at];
                if (line.Trim().Length == 0)
                {
                    _at++;
                }
                else if (line.StartsWith(Fence, StringComparison.Ordinal))
                {
                    ReadCode();
                }
                else if (Heading().Match(line) is { Success: true } heading)
                {
                    ReadHeading(heading);
                }
                else if (line.StartsWith('>'))
                {
                    _blocks.Add(Quote(Gather(next => next.StartsWith('>'), Unquote)));
                }
                else if (Figure().Match(line) is { Success: true } figure)
                {
                    _at++;
                    _blocks.Add(ReadFigure(figure));
                }
                else if (ListItem().IsMatch(line))
                {
                    ReadList();
                }
                else if (IsTableStart())
                {
                    ReadTable();
                }
                else
                {
                    _blocks.Add(new GuideParagraph(Inline(section, Gather(IsParagraphLine, text => text.Trim()))));
                }
            }

            return new GuideDocument(_title ?? section, _blocks);
        }

        /// <summary>Removes a quote's <c>&gt;</c> and the space after it.</summary>
        /// <param name="line">The line.</param>
        /// <returns>The text.</returns>
        private static string Unquote(string line) => line.TrimStart('>').Trim();

        /// <summary>Says whether a line goes on a paragraph: it is not empty and starts no other block.</summary>
        /// <param name="line">The line.</param>
        /// <returns><see langword="true"/> when it does.</returns>
        private static bool IsParagraphLine(string line) =>
            line.Trim().Length > 0 && !line.StartsWith(Fence, StringComparison.Ordinal) && !line.StartsWith('>')
            && !line.StartsWith('|') && !Heading().IsMatch(line) && !ListItem().IsMatch(line)
            && !Figure().IsMatch(line);

        /// <summary>
        /// Reads a quote's text: an analogy when it starts with the bold label <see cref="GuideAnalogy.Label"/>, a
        /// quote otherwise.
        /// </summary>
        /// <param name="text">The quote's text, its lines joined.</param>
        /// <returns>The block.</returns>
        private GuideBlock Quote(string text)
        {
            var label = "**" + GuideAnalogy.Label + "**";
            return text.StartsWith(label, StringComparison.Ordinal)
                ? new GuideAnalogy(Inline(section, text[label.Length..].Trim()))
                : new GuideQuote(Inline(section, text));
        }

        /// <summary>
        /// Reads a figure: its caption, and its id when the target is <c>figur:</c> and an id; any other target gives
        /// a figure without an id, which shows its caption and says that the picture is missing.
        /// </summary>
        /// <param name="match">The figure's match.</param>
        /// <returns>The figure.</returns>
        private GuideFigure ReadFigure(Match match)
        {
            var target = match.Groups["target"].Value;
            var id = target.StartsWith(GuideFigure.Scheme, StringComparison.Ordinal)
                ? target[GuideFigure.Scheme.Length..]
                : string.Empty;
            var caption = Inline(section, match.Groups["caption"].Value.Trim());
            return new GuideFigure(SectionId().IsMatch(id) ? id : string.Empty, caption);
        }

        /// <summary>Joins the lines from here on that belong together, with a space between them.</summary>
        /// <param name="belongs">Whether a following line belongs (the first always does).</param>
        /// <param name="clean">What is kept of a line.</param>
        /// <returns>The joined text.</returns>
        private string Gather(Func<string, bool> belongs, Func<string, string> clean)
        {
            var parts = new List<string> { clean(lines[_at++]) };
            while (_at < lines.Length && belongs(lines[_at]))
            {
                parts.Add(clean(lines[_at++]));
            }

            return string.Join(' ', parts.Where(part => part.Length > 0));
        }

        /// <summary>Reads a heading: the first <c>#</c> is the title, the rest are headings with anchors.</summary>
        /// <param name="match">The heading's match.</param>
        private void ReadHeading(Match match)
        {
            _at++;
            var level = match.Groups["level"].Length;
            var text = match.Groups["text"].Value.Trim();
            if (level == 1 && _title is null)
            {
                _title = Plain(Inline(section, text));
                return;
            }

            var runs = Inline(section, text);
            var anchor = Anchor(Plain(runs));
            var unique = anchor;
            for (var n = 2; !_anchors.Add(unique); n++)
            {
                unique = string.Create(CultureInfo.InvariantCulture, $"{anchor}-{n}");
            }

            _blocks.Add(new GuideHeading(Math.Max(level, 2), runs, unique));
        }

        /// <summary>Reads code up to the closing fence, or to the end when it is never closed.</summary>
        private void ReadCode()
        {
            _at++;
            var code = new List<string>();
            while (_at < lines.Length && !lines[_at].StartsWith(Fence, StringComparison.Ordinal))
            {
                code.Add(lines[_at++]);
            }

            _at++;
            _blocks.Add(new GuideCode(string.Join('\n', code)));
        }

        /// <summary>
        /// Reads a list: an item per marker, with indented lines after it as the item's continuation, and indented
        /// markers as a nested list (two spaces a level).
        /// </summary>
        private void ReadList()
        {
            var items = new List<(int Depth, int? Number, List<string> Text)>();
            while (_at < lines.Length)
            {
                var line = lines[_at];
                if (ListItem().Match(line) is { Success: true } item)
                {
                    int? number = item.Groups["number"].Success
                        ? int.Parse(item.Groups["number"].Value, CultureInfo.InvariantCulture)
                        : null;
                    items.Add((item.Groups["indent"].Length / 2, number, [item.Groups["text"].Value.Trim()]));
                }
                else if (line.StartsWith(' ') && line.Trim().Length > 0)
                {
                    items[^1].Text.Add(line.Trim());
                }
                else
                {
                    break;
                }

                _at++;
            }

            _blocks.Add(new GuideList([.. items.Select(item =>
                new GuideListItem(Inline(section, string.Join(' ', item.Text)), item.Depth, item.Number))]));
        }

        /// <summary>Says whether a table starts here: a line with pipes, and the rule line under it.</summary>
        /// <returns><see langword="true"/> when it does.</returns>
        private bool IsTableStart() =>
            lines[_at].StartsWith('|') && _at + 1 < lines.Length && TableRule().IsMatch(lines[_at + 1]);

        /// <summary>Reads a table: the headers, the rule line, and the rows up to a line without pipes.</summary>
        private void ReadTable()
        {
            var header = Cells(lines[_at]);
            _at += 2;
            var rows = new List<IReadOnlyList<IReadOnlyList<GuideRun>>>();
            while (_at < lines.Length && lines[_at].StartsWith('|'))
            {
                var cells = Cells(lines[_at++]);
                rows.Add([.. Enumerable.Range(0, header.Count)
                    .Select(i => i < cells.Count ? cells[i] : (IReadOnlyList<GuideRun>)[])]);
            }

            _blocks.Add(new GuideTable(header, rows));
        }

        /// <summary>Splits a table's line into its cells, without the pipes at its ends.</summary>
        /// <param name="line">The line.</param>
        /// <returns>The cells' text.</returns>
        private List<IReadOnlyList<GuideRun>> Cells(string line)
        {
            var text = line.Trim();
            text = text.StartsWith('|') ? text[1..] : text;
            text = text.EndsWith('|') ? text[..^1] : text;
            return [.. text.Split('|').Select(cell => Inline(section, cell.Trim()))];
        }
    }
}
