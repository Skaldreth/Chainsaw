using Chainsaw.SharedKernel.Text;

namespace Chainsaw.SharedKernel.Tests;

public sealed class MarkdownTests
{
    [Fact]
    public void Parse_ReadsEveryKindOfLineAndSkipsEmptyOnes()
    {
        var blocks = Markdown.Parse("# Profilen\r\n\nText\n## Omständigheter\n- Punkt\n> Citat\n   \n");

        Assert.Equal(
            [
                MarkdownBlockKind.Heading, MarkdownBlockKind.Paragraph, MarkdownBlockKind.Subheading,
                MarkdownBlockKind.Bullet, MarkdownBlockKind.Quote,
            ],
            blocks.Select(b => b.Kind));
        Assert.Equal(["Profilen", "Text", "Omständigheter", "Punkt", "Citat"], blocks.Select(b => b.PlainText));
    }

    [Fact]
    public void ParseInline_ReadsBoldItalicCodeAndLinks()
    {
        var spans = Markdown.ParseInline(
            "Inlägget från **2 september** anger *070-123*, `annadesign` och [Säkring #13](säkring:13).");

        Assert.Equal(
            [
                new MarkdownSpan("Inlägget från "),
                new MarkdownSpan("2 september", MarkdownStyle.Bold),
                new MarkdownSpan(" anger "),
                new MarkdownSpan("070-123", MarkdownStyle.Italic),
                new MarkdownSpan(", "),
                new MarkdownSpan("annadesign", MarkdownStyle.Code),
                new MarkdownSpan(" och "),
                new MarkdownSpan("Säkring #13", Target: "säkring:13"),
                new MarkdownSpan("."),
            ],
            spans);
    }

    [Theory]
    [InlineData("2 * 3 * 4")]
    [InlineData("**inte stängd")]
    [InlineData("en `bit")]
    [InlineData("[text](")]
    [InlineData("[](adress)")]
    [InlineData("[text](med mellanslag)")]
    [InlineData("#inte rubrik")]
    [InlineData("#")]
    [InlineData(">")]
    [InlineData("---")]
    [InlineData("snake_case_namn")]
    public void Parse_KeepsUnclosedOrLooseMarksAsText(string text)
    {
        var block = Assert.Single(Markdown.Parse(text));

        Assert.Equal(MarkdownBlockKind.Paragraph, block.Kind);
        Assert.Equal(text, block.PlainText);
    }

    [Theory]
    [InlineData("### Tredje nivån", MarkdownBlockKind.Subheading, "Tredje nivån")]
    [InlineData("* Stjärna", MarkdownBlockKind.Bullet, "Stjärna")]
    [InlineData("+ Plus", MarkdownBlockKind.Bullet, "Plus")]
    [InlineData(">Citat utan mellanslag", MarkdownBlockKind.Quote, "Citat utan mellanslag")]
    public void Parse_ReadsTheOtherSpellingsOfHeadingsBulletsAndQuotes(
        string text, MarkdownBlockKind kind, string plain)
    {
        var block = Assert.Single(Markdown.Parse(text));

        Assert.Equal(kind, block.Kind);
        Assert.Equal(plain, block.PlainText);
    }

    [Fact]
    public void Parse_ReadsNumberedItemsWithTheirNumbersAsTyped()
    {
        var blocks = Markdown.Parse("1. Första\n3) Tredje");

        Assert.All(blocks, b => Assert.Equal(MarkdownBlockKind.Numbered, b.Kind));
        Assert.Equal(["1.", "3)"], blocks.Select(b => b.Marker));
        Assert.Equal(["Första", "Tredje"], blocks.Select(b => b.PlainText));
    }

    [Fact]
    public void Parse_ReadsHowDeepAnItemIsIndented()
    {
        var blocks = Markdown.Parse("- Ett\n  - Två\n    1. Tre\n\t\t\t- Djupt");

        Assert.Equal([0, 1, 2, Markdown.MaxLevel], blocks.Select(b => b.Level));
    }

    [Fact]
    public void Parse_KeepsFencedLinesAsTypedWithoutReadingThemAsMarkdown()
    {
        var blocks = Markdown.Parse(
            "Svar från servern:\n```http\n# inte rubrik\n  - inte punkt **inte fet**\n```\nEfter");

        Assert.Equal(
            [MarkdownBlockKind.Paragraph, MarkdownBlockKind.Code, MarkdownBlockKind.Paragraph],
            blocks.Select(b => b.Kind));
        Assert.Equal("# inte rubrik\n  - inte punkt **inte fet**", blocks[1].PlainText);
    }

    [Fact]
    public void Parse_AnUnclosedFenceRunsToTheEnd()
    {
        var block = Assert.Single(Markdown.Parse("~~~\nett\ntvå"));

        Assert.Equal(MarkdownBlockKind.Code, block.Kind);
        Assert.Equal("ett\ntvå", block.PlainText);
    }

    [Fact]
    public void Parse_BackticksWithinALineAreNotAFence()
    {
        var block = Assert.Single(Markdown.Parse("```kod``` i en rad"));

        Assert.Equal(MarkdownBlockKind.Paragraph, block.Kind);
        Assert.Equal([new MarkdownSpan("kod", MarkdownStyle.Code), new MarkdownSpan(" i en rad")], block.Spans);
    }

    [Fact]
    public void ParseInline_NestsItalicLinksAndCodeInBold()
    {
        var spans = Markdown.ParseInline("**fet med *kursiv*, [länk](https://a.se) och `kod`**");

        Assert.Equal(
            [
                new MarkdownSpan("fet med ", MarkdownStyle.Bold),
                new MarkdownSpan("kursiv", MarkdownStyle.Bold | MarkdownStyle.Italic),
                new MarkdownSpan(", ", MarkdownStyle.Bold),
                new MarkdownSpan("länk", MarkdownStyle.Bold, "https://a.se"),
                new MarkdownSpan(" och ", MarkdownStyle.Bold),
                new MarkdownSpan("kod", MarkdownStyle.Bold | MarkdownStyle.Code),
            ],
            spans);
    }

    [Fact]
    public void ParseInline_BoldInsideItalicIsBothAndDoesNotEndIt()
    {
        var spans = Markdown.ParseInline("*kursiv **och** mer*");

        Assert.Equal(
            [
                new MarkdownSpan("kursiv ", MarkdownStyle.Italic),
                new MarkdownSpan("och", MarkdownStyle.Bold | MarkdownStyle.Italic),
                new MarkdownSpan(" mer", MarkdownStyle.Italic),
            ],
            spans);
    }

    [Fact]
    public void ParseInline_ThreeStarsAreBoldAndItalic()
    {
        var span = Assert.Single(Markdown.ParseInline("***viktigt***"));

        Assert.Equal(new MarkdownSpan("viktigt", MarkdownStyle.Bold | MarkdownStyle.Italic), span);
    }

    [Fact]
    public void ParseInline_ReadsUnderscoresOnlyAtWordEdges()
    {
        var spans = Markdown.ParseInline("__fet__, _kursiv_ och fil_namn_här");

        Assert.Equal(
            [
                new MarkdownSpan("fet", MarkdownStyle.Bold),
                new MarkdownSpan(", "),
                new MarkdownSpan("kursiv", MarkdownStyle.Italic),
                new MarkdownSpan(" och fil_namn_här"),
            ],
            spans);
    }

    [Fact]
    public void ParseInline_KeepsEscapedMarksWithoutTheBackslash()
    {
        var span = Assert.Single(Markdown.ParseInline(@"\*inte kursiv\* och C:\Temp"));

        Assert.Equal(new MarkdownSpan(@"*inte kursiv* och C:\Temp"), span);
    }

    [Fact]
    public void ParseInline_LinkAddressMayHoldBalancedParentheses()
    {
        var spans = Markdown.ParseInline("[Wiki](https://sv.wikipedia.org/wiki/A_(B)).");

        Assert.Equal(
            [new MarkdownSpan("Wiki", Target: "https://sv.wikipedia.org/wiki/A_(B)"), new MarkdownSpan(".")],
            spans);
    }

    [Fact]
    public void ParseInline_DoubleBackticksCanHoldABacktick()
    {
        var span = Assert.Single(Markdown.ParseInline("`` a ` b ``"));

        Assert.Equal(new MarkdownSpan("a ` b", MarkdownStyle.Code), span);
    }

    [Fact]
    public void ParseInline_MarksInCodeAreText()
    {
        var span = Assert.Single(Markdown.ParseInline("`**inte fet** [inte](länk)`"));

        Assert.Equal(new MarkdownSpan("**inte fet** [inte](länk)", MarkdownStyle.Code), span);
    }

    [Fact]
    public void ReadBlocks_KeepsTheLinesEachBlockWasTypedAs()
    {
        var blocks = Markdown.ReadBlocks("- punkt\n\n```\nkod [a](b)\n```\ntext");

        Assert.Equal(["- punkt", "```\nkod [a](b)\n```", "text"], blocks.Select(b => b.Source));
        Assert.All(blocks, b => Assert.True(b.IsClosed));
        Assert.Equal(MarkdownBlockKind.Code, blocks[1].Block.Kind);
    }

    [Theory]
    [InlineData("text\n```\nhemlig rad\n\nnästa rad")]
    [InlineData("text\n~~~\nhemlig rad")]
    [InlineData("text\n```")]
    public void ReadBlocks_SeesAFenceThatIsNeverClosed(string text)
    {
        var blocks = Markdown.ReadBlocks(text);

        Assert.False(blocks[^1].IsClosed);
        Assert.Equal(text[(text.IndexOf('\n', StringComparison.Ordinal) + 1)..], blocks[^1].Source);
    }

    [Fact]
    public void Parse_LeavesOutAnEmptyCodeBlockThatReadBlocksKeeps()
    {
        Assert.Equal(["text"], Markdown.Parse("text\n```\n```").Select(b => b.PlainText));
        Assert.True(Markdown.ReadBlocks("text\n```\n```")[1].IsEmpty);
    }

    [Fact]
    public void Parse_ReadsTheSketchesComment()
    {
        var blocks = Markdown.Parse(
            "## Uttag via Kraken?\nBeloppet **0,42 BTC** stämmer.\n- kom 4 h efter mixern\n"
            + "[tx 9f3c…a1](chainsaw:btc/9f3ca1)");

        Assert.Equal(
            [
                MarkdownBlockKind.Subheading, MarkdownBlockKind.Paragraph, MarkdownBlockKind.Bullet,
                MarkdownBlockKind.Paragraph,
            ],
            blocks.Select(b => b.Kind));
        Assert.Equal(new MarkdownSpan("0,42 BTC", MarkdownStyle.Bold), blocks[1].Spans[1]);
        Assert.Equal(new MarkdownSpan("tx 9f3c…a1", Target: "chainsaw:btc/9f3ca1"), Assert.Single(blocks[3].Spans));
    }

    [Theory]
    [InlineData("[", "a", "](x)")]
    [InlineData("**", "a", "**")]
    [InlineData("*", "a", "*")]
    public void Parse_ReadsDeepNestingOnASmallStackWithoutLosingText(string open, string middle, string close)
    {
        // The UI thread has a 1 MB stack; a sealed comment like this is read every time its case opens.
        var count = 19_000 / (open.Length + close.Length);
        var text = string.Concat(Enumerable.Repeat(open, count)) + middle
            + string.Concat(Enumerable.Repeat(close, count));
        IReadOnlyList<MarkdownBlock>? blocks = null;
        var thread = new Thread(() => blocks = Markdown.Parse(text), 1024 * 1024);

        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));

        var block = Assert.Single(blocks!);
        Assert.Contains(middle, block.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseInline_ReadsLinksOnlyToTheDeepestNesting()
    {
        var depth = Markdown.MaxNesting + 5;
        var text = string.Concat(Enumerable.Repeat("[", depth)) + "a" + string.Concat(Enumerable.Repeat("](x)", depth));

        var spans = Markdown.ParseInline(text);

        Assert.Contains(spans, span => span.Target == "x");
        Assert.Contains("[", string.Concat(spans.Select(span => span.Text)), StringComparison.Ordinal);
    }

    [Fact]
    public void ParseInline_ReadsALongLineOfBracketsQuickly()
    {
        var text = string.Concat(Enumerable.Repeat("[a", 10_000));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var span = Assert.Single(Markdown.ParseInline(text));

        Assert.Equal(text, span.Text);
        Assert.True(clock.ElapsedMilliseconds < 1_000, $"{clock.ElapsedMilliseconds} ms");
    }
}
