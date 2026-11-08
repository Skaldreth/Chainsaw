using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Chainsaw.App.Controls;

/// <summary>The brushes and faces a comment's Markdown is coloured with while it is written.</summary>
/// <param name="Mark">The dim marks (sketch KN1a: <c>##</c>, <c>**</c>, <c>-</c>).</param>
/// <param name="Text">The text, also a link's text.</param>
/// <param name="Code">The code's ground.</param>
/// <param name="Mono">The code's face.</param>
internal sealed record NoteInks(Brush Mark, Brush Text, Brush Code, FontFamily Mono);

/// <summary>
/// Colours a comment's Markdown while it is written (sketch KN1a, after Gawk's colorizer): the marks are dim, and what
/// they do is shown on the text itself, so that <c>**fet**</c> reads bold and a heading stands out before the note is
/// left and drawn.
/// </summary>
/// <remarks>
/// It follows the parser's rules closely enough for writing, not exactly: the drawn note is what counts. Code blocks
/// are tracked over the whole text, since a fence changes every line below it.
/// </remarks>
/// <param name="inks">The brushes and faces.</param>
internal sealed partial class NoteColorizer(NoteInks inks) : DocumentColorizingTransformer
{
    /// <summary>A heading's size while it is written (sketch KN1a: 15 px).</summary>
    private const double HeadingSize = 15;

    /// <summary>The code's size (sketch KN1a: 12 px).</summary>
    private const double CodeSize = 12;

    /// <summary>The text's version the code blocks were last found in.</summary>
    private ITextSourceVersion? _fencesVersion;

    /// <summary>The lines inside a code block.</summary>
    private HashSet<int> _codeLines = [];

    /// <summary>The lines that open or close a code block.</summary>
    private HashSet<int> _fenceLines = [];

    /// <summary>
    /// Finds the lines that open or close a code block and the lines inside, again when the text has changed since.
    /// </summary>
    /// <param name="document">The text.</param>
    /// <returns><see langword="true"/> when the code blocks moved, so that every line must be coloured again.</returns>
    public bool UpdateCodeBlocks(TextDocument document)
    {
        if (_fencesVersion is { } known && document.Version is { } now && known.BelongsToSameDocumentAs(now)
            && known.CompareAge(now) == 0)
        {
            return false;
        }

        var fences = new HashSet<int>();
        var code = new HashSet<int>();
        var inside = false;
        foreach (var line in document.Lines)
        {
            if (FencePattern().IsMatch(document.GetText(line)))
            {
                fences.Add(line.LineNumber);
                inside = !inside;
            }
            else if (inside)
            {
                code.Add(line.LineNumber);
            }
        }

        _fencesVersion = document.Version;
        var moved = !fences.SetEquals(_fenceLines) || !code.SetEquals(_codeLines);
        _fenceLines = fences;
        _codeLines = code;
        return moved;
    }

    /// <inheritdoc />
    protected override void ColorizeLine(DocumentLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var document = CurrentContext.Document;
        UpdateCodeBlocks(document);
        if (line.Length == 0)
        {
            return;
        }

        var start = line.Offset;
        var end = line.EndOffset;
        if (_fenceLines.Contains(line.LineNumber))
        {
            Paint(start, end, inks.Mark);
            return;
        }

        if (_codeLines.Contains(line.LineNumber))
        {
            Change(start, end, code: true);
            return;
        }

        var text = document.GetText(line);
        ColorizeInline(text, start, ColorizeLineMark(text, start, end));
    }

    /// <summary>Colours what a line starts with: a heading's hashes, a list's mark or a quote's.</summary>
    /// <param name="text">The line.</param>
    /// <param name="start">The line's offset in the text.</param>
    /// <param name="end">The line's end in the text.</param>
    /// <returns>Where the line's own text starts, after its mark.</returns>
    private int ColorizeLineMark(string text, int start, int end)
    {
        var heading = HeadingPattern().Match(text);
        if (heading.Success)
        {
            Change(start, end, bold: true, size: HeadingSize);
            Paint(start, start + heading.Length, inks.Mark);
            return heading.Length;
        }

        var list = ListPattern().Match(text);
        if (list.Success)
        {
            Paint(start, start + list.Length, inks.Mark);
            return list.Length;
        }

        var quote = QuotePattern().Match(text);
        if (quote.Success)
        {
            Paint(start, start + quote.Length, inks.Mark);
            return quote.Length;
        }

        return 0;
    }

    /// <summary>Colours code, escapes, links, bold and italic in a line, in that order.</summary>
    /// <param name="text">The line.</param>
    /// <param name="lineOffset">The line's offset in the text.</param>
    /// <param name="from">Where the line's own text starts.</param>
    private void ColorizeInline(string text, int lineOffset, int from)
    {
        // Code first: nothing inside it is Markdown.
        var taken = new List<(int Start, int End)>();
        foreach (Match match in CodePattern().Matches(text, from))
        {
            var start = lineOffset + match.Index;
            var end = start + match.Length;
            Change(start, end, code: true);
            Paint(start, start + match.Groups[1].Length, inks.Mark);
            Paint(end - match.Groups[1].Length, end, inks.Mark);
            taken.Add((match.Index, match.Index + match.Length));
        }

        foreach (Match match in EscapePattern().Matches(text, from))
        {
            if (IsFree(taken, match.Index, match.Index + match.Length))
            {
                Paint(lineOffset + match.Index, lineOffset + match.Index + 1, inks.Mark);
                taken.Add((match.Index, match.Index + match.Length));
            }
        }

        foreach (Match match in LinkPattern().Matches(text, from))
        {
            if (!IsFree(taken, match.Index, match.Index + match.Length))
            {
                continue;
            }

            var label = match.Groups[1];
            Paint(lineOffset + match.Index, lineOffset + match.Index + match.Length, inks.Mark);
            Paint(lineOffset + label.Index, lineOffset + label.Index + label.Length, inks.Text);
            taken.Add((match.Index, match.Index + match.Length));
        }

        Emphasis(text, lineOffset, from, BoldPattern(), taken, bold: true);
        Emphasis(text, lineOffset, from, ItalicPattern(), taken, bold: false);
    }

    /// <summary>Colours bold or italic text and dims its marks, outside code and a link's address.</summary>
    /// <param name="text">The line.</param>
    /// <param name="lineOffset">The line's offset in the text.</param>
    /// <param name="from">Where the line's own text starts.</param>
    /// <param name="pattern">The pattern of bold or italic text.</param>
    /// <param name="taken">What code, escapes and links already hold.</param>
    /// <param name="bold">Whether the pattern is bold.</param>
    private void Emphasis(
        string text, int lineOffset, int from, Regex pattern, List<(int Start, int End)> taken, bool bold)
    {
        foreach (Match match in pattern.Matches(text, from))
        {
            // The italic pattern has two alternatives; the second (an underscore) has its mark in group 3.
            var mark = match.Groups[1].Success ? match.Groups[1].Length : match.Groups[3].Length;
            var start = lineOffset + match.Index;
            var end = start + match.Length;
            if (!IsFree(taken, match.Index, match.Index + mark)
                || !IsFree(taken, match.Index + match.Length - mark, match.Index + match.Length))
            {
                continue;
            }

            Change(start + mark, end - mark, bold: bold, italic: !bold);
            Paint(start, start + mark, inks.Mark);
            Paint(end - mark, end, inks.Mark);
        }
    }

    /// <summary>Reads whether a part of a line is free of code, escapes and links.</summary>
    /// <param name="taken">What they hold.</param>
    /// <param name="start">The part's start.</param>
    /// <param name="end">The part's end.</param>
    /// <returns><see langword="true"/> when it is free.</returns>
    private static bool IsFree(List<(int Start, int End)> taken, int start, int end) =>
        !taken.Any(part => start < part.End && end > part.Start);

    /// <summary>Gives a part of the text a colour.</summary>
    /// <param name="start">Its start.</param>
    /// <param name="end">Its end.</param>
    /// <param name="brush">The colour.</param>
    private void Paint(int start, int end, Brush brush)
    {
        if (end > start)
        {
            ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    /// <summary>Changes a part's face, size or ground, keeping what other marks already did to it.</summary>
    /// <param name="start">Its start.</param>
    /// <param name="end">Its end.</param>
    /// <param name="bold">Whether it is bold.</param>
    /// <param name="italic">Whether it is italic.</param>
    /// <param name="code">Whether it is code: mono on the code's ground.</param>
    /// <param name="size">Its size, or 0 to keep it.</param>
    private void Change(
        int start, int end, bool bold = false, bool italic = false, bool code = false, double size = 0)
    {
        if (end <= start)
        {
            return;
        }

        ChangeLinePart(start, end, element =>
        {
            var properties = element.TextRunProperties;
            var face = properties.Typeface;
            properties.SetTypeface(new Typeface(
                code ? inks.Mono : face.FontFamily,
                italic ? FontStyles.Italic : face.Style,
                bold ? FontWeights.SemiBold : face.Weight,
                face.Stretch));
            if (code)
            {
                properties.SetBackgroundBrush(inks.Code);
                properties.SetFontRenderingEmSize(CodeSize);
            }
            else if (size > 0)
            {
                properties.SetFontRenderingEmSize(size);
            }
        });
    }

    /// <summary>A line that opens or closes a code block.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^\s*(```|~~~)")]
    private static partial Regex FencePattern();

    /// <summary>A heading's hashes and the space after them.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^(#{1,6})[ \t]+")]
    private static partial Regex HeadingPattern();

    /// <summary>A bullet's or a numbered item's mark and the space after it.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^( *)([-*+]|\d{1,9}[.)])( +)")]
    private static partial Regex ListPattern();

    /// <summary>A quote's mark.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"^>[ ]?")]
    private static partial Regex QuotePattern();

    /// <summary>Code in a line: a run of backticks, the code, and the same run again.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"(`+)(?!`)(.+?)(?<!`)\1(?!`)")]
    private static partial Regex CodePattern();

    /// <summary>A backslash that keeps a mark as text.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"\\[\\`*_{}\[\]()#+\-.!>|~]")]
    private static partial Regex EscapePattern();

    /// <summary>A link: its text in brackets and its address in parentheses.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"\[([^\]\n]+)\]\(([^)\s]+(?:\([^)\s]*\)[^)\s]*)*)\)")]
    private static partial Regex LinkPattern();

    /// <summary>Bold text between two stars or two underscores.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"(?<![*\\])(\*\*|__)(?=\S)(.+?)(?<=\S)\1(?!\*)")]
    private static partial Regex BoldPattern();

    /// <summary>One star, or an underscore at a word's edge so that <c>snake_case</c> is left alone.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(
        @"(?<![*\\])(\*)(?=[^\s*])(.+?)(?<=[^\s*])\*(?!\*)|(?<![\w\\])(_)(?=[^\s_])(.+?)(?<=[^\s_])_(?!\w)")]
    private static partial Regex ItalicPattern();
}
