using Chainsaw.SharedKernel.Text;

namespace Chainsaw.SharedKernel.Tests;

/// <summary>
/// The keys of a comment's editor (sketch KN1a), after Gawk's tests. The texts mark the caret with <c>|</c> and a
/// selection with <c>«</c> and <c>»</c>, before and after the edit.
/// </summary>
public sealed class MarkdownEditsTests
{
    /// <summary>The text the latest <see cref="Parse"/> read, which <see cref="Render"/> applies an edit to.</summary>
    private string _text = string.Empty;

    /// <summary>Enter in a list or a quote goes on with it; on an empty item it steps out or ends the list.</summary>
    /// <param name="before">The text and the caret before Enter.</param>
    /// <param name="after">The text and the caret after it.</param>
    [Theory]
    [InlineData("- Ett|", "- Ett\n- |")]
    [InlineData("  * Ett|", "  * Ett\n  * |")]
    [InlineData("9. Nio|", "9. Nio\n10. |")]
    [InlineData("1) Ett|", "1) Ett\n2) |")]
    [InlineData("- Ett| två", "- Ett\n- | två")]
    [InlineData("> Citat|", "> Citat\n> |")]
    [InlineData("- Ett\n- |", "- Ett\n|")]
    [InlineData("- Ett\n  - |", "- Ett\n- |")]
    public void EnterGoesOnWithTheList(string before, string after)
    {
        var (text, caret, _) = Parse(before);

        var change = MarkdownEdits.ContinueList(text, caret, "\n");

        Assert.NotNull(change);
        Assert.Equal(after, Render(change.Value));
    }

    /// <summary>Enter is an ordinary new line outside a list, in an item's mark and in code.</summary>
    /// <param name="before">The text and the caret.</param>
    [Theory]
    [InlineData("Vanlig rad|")]
    [InlineData("|- Ett")]
    [InlineData("```\n- Ett|")]
    [InlineData("```\r\n- Ett|")]
    public void EnterIsOrdinaryElsewhere(string before)
    {
        var (text, caret, _) = Parse(before);

        Assert.Null(MarkdownEdits.ContinueList(text, caret, "\n"));
    }

    /// <summary>Windows' line breaks are kept: the new item gets the text's break, and a <c>\r</c> is never text.</summary>
    [Fact]
    public void WindowsLineBreaksAreKept()
    {
        var (text, caret, _) = Parse("- Ett|\r\n- Två");

        var change = MarkdownEdits.ContinueList(text, caret, "\r\n");

        Assert.Equal("- Ett\r\n- |\r\n- Två", Render(change!.Value));
        var (empty, at, _) = Parse("- Ett\r\n- |\r\nslut");
        Assert.Equal("- Ett\r\n|\r\nslut", Render(MarkdownEdits.ContinueList(empty, at, "\r\n")!.Value));
    }

    /// <summary>Tab moves an item a level in, Shift+Tab out, and a selection moves every line it touches.</summary>
    /// <param name="before">The text and the caret or selection before.</param>
    /// <param name="after">The text and the caret or selection after.</param>
    /// <param name="outdent">Whether it is Shift+Tab.</param>
    [Theory]
    [InlineData("- Ett|", "  - Ett|", false)]
    [InlineData("  - Ett|", "- Ett|", true)]
    [InlineData("«- Ett\n- Två»", "«  - Ett\n  - Två»", false)]
    [InlineData("«- Ett\r\n- Två»", "«  - Ett\r\n  - Två»", false)]
    public void TabMovesLines(string before, string after, bool outdent)
    {
        var (text, start, length) = Parse(before);

        var change = MarkdownEdits.Indent(text, start, length, outdent);

        Assert.NotNull(change);
        Assert.Equal(after, Render(change.Value));
    }

    /// <summary>Tab on an ordinary line is an ordinary key, and nothing moves out further than the edge.</summary>
    [Fact]
    public void TabLeavesAnOrdinaryLine()
    {
        Assert.Null(MarkdownEdits.Indent("Vanlig rad", 3, 0, outdent: false));
        Assert.Null(MarkdownEdits.Indent("- Ett", 3, 0, outdent: true));
    }

    /// <summary>A position is in code below an odd number of fences, whatever the line breaks.</summary>
    [Fact]
    public void CodeIsFoundBetweenFences()
    {
        Assert.True(MarkdownEdits.IsInCodeBlock("```\r\nkod", 6));
        Assert.False(MarkdownEdits.IsInCodeBlock("```\nkod\n```\ntext", 13));
        Assert.Equal(3, MarkdownEdits.LineEnd("Ett\r\nTvå", 0));
        Assert.Equal(5, MarkdownEdits.LineStart("Ett\r\nTvå", 6));
    }

    /// <summary>The edit applied to the text, with the caret or selection marked.</summary>
    /// <param name="edit">The edit; its text is the one <see cref="_text"/> holds.</param>
    /// <returns>The marked text.</returns>
    private string Render(TextEdit edit)
    {
        var text = edit.ApplyTo(_text);
        return edit.SelectionLength == 0
            ? text.Insert(edit.SelectionStart, "|")
            : text.Insert(edit.SelectionStart + edit.SelectionLength, "»").Insert(edit.SelectionStart, "«");
    }

    /// <summary>Reads a marked text: the caret <c>|</c>, or a selection in <c>«</c> and <c>»</c>.</summary>
    /// <param name="marked">The marked text.</param>
    /// <returns>The text and the selection.</returns>
    private (string Text, int Start, int Length) Parse(string marked)
    {
        var caret = marked.IndexOf('|', StringComparison.Ordinal);
        if (caret >= 0)
        {
            _text = marked.Remove(caret, 1);
            return (_text, caret, 0);
        }

        var open = marked.IndexOf('«', StringComparison.Ordinal);
        var close = marked.IndexOf('»', StringComparison.Ordinal);
        _text = marked.Remove(close, 1).Remove(open, 1);
        return (_text, open, close - open - 1);
    }
}
