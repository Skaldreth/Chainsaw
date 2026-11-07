using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.App.Navigation;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>The Guide's reader, index, history and panel (sketch GD1b), without WPF.</summary>
public sealed class GuideTests
{
    /// <summary>The texts built into the tests, as into the program.</summary>
    private static readonly GuideLibrary Library = GuideLibrary.Load();

    /// <summary>A small library of two sections that link to each other.</summary>
    /// <returns>The library.</returns>
    private static GuideLibrary Small() => new(new Dictionary<string, string>
    {
        ["utxo-modellen"] = "# UTXO\n\nSe [Växel](vaxeladress.md#när-slås-växeln-ihop).\n\n## Avgiften\n\nText.",
        ["vaxeladress"] = "# Växeladress\n\nTillbaka till [UTXO](utxo-modellen.md).\n\n## När slås växeln ihop?\n\nX.",
        ["etiketter"] = "# Etiketter\n\n## En etikett är alltid en bedömning\n\nY.",
    });

    /// <summary>Headings, paragraphs joined over lines, lists, quotes, code and tables are read.</summary>
    [Fact]
    public void ReadsTheBlocks()
    {
        const string text = """
            # Titel

            Ett stycke
            över två rader.

            ## En rubrik

            - Första
              fortsättning
              - Inuti
            - Andra

            1. Ett
            2. Två

            > summan av inputs = summan av outputs

            ```
            A ──▶ B
            ```

            | Typ | Börjar |
            | --- | --- |
            | **P2PKH** | `1` |
            """;

        var document = GuideMarkdown.Parse("test", text);

        Assert.Equal("Titel", document.Title);
        Assert.Collection(
            document.Blocks,
            block => Assert.Equal("Ett stycke över två rader.", Plain(block)),
            block => Assert.Equal(new GuideHeading(2, ((GuideHeading)block).Runs, "en-rubrik"), block),
            block =>
            {
                var list = Assert.IsType<GuideList>(block);
                Assert.Equal(["Första fortsättning", "Inuti", "Andra"], list.Items.Select(i => Text(i.Runs)));
                Assert.Equal([0, 1, 0], list.Items.Select(item => item.Depth));
                Assert.All(list.Items, item => Assert.Null(item.Number));
            },
            block => Assert.Equal([1, 2], Assert.IsType<GuideList>(block).Items.Select(item => item.Number ?? 0)),
            block => Assert.Equal("summan av inputs = summan av outputs", Text(Assert.IsType<GuideQuote>(block).Runs)),
            block => Assert.Equal("A ──▶ B", Assert.IsType<GuideCode>(block).Text),
            block =>
            {
                var table = Assert.IsType<GuideTable>(block);
                Assert.Equal(["Typ", "Börjar"], table.Header.Select(Text));
                Assert.True(table.Rows[0][0][0].IsStrong);
                Assert.True(table.Rows[0][1][0].IsCode);
            });
    }

    /// <summary>Bold, italic, code and links are read; a code span hides markers; a lone star stays text.</summary>
    [Fact]
    public void ReadsTheText()
    {
        var runs = GuideMarkdown.Inline(
            "utxo-modellen",
            "**fet *och kursiv*** och `a*b` och [Växel](vaxeladress.md#idén), [här](#avgiften) 2 * 3");

        Assert.Equal("fet och kursiv och a*b och Växel, här 2 * 3", Text(runs));
        Assert.Equal(GuideRunStyle.Strong, runs[0].Style);
        Assert.Equal(GuideRunStyle.Strong | GuideRunStyle.Emphasis, runs[1].Style);
        Assert.Contains(runs, run => run is { Text: "a*b", IsCode: true });
        Assert.Contains(runs, run => run.Text == "Växel" && run.Link == new GuideLink("vaxeladress", "idén"));
        Assert.Contains(runs, run => run.Text == "här" && run.Link == new GuideLink("utxo-modellen", "avgiften"));
        Assert.EndsWith(" 2 * 3", runs[^1].Text, StringComparison.Ordinal);
    }

    /// <summary>A link out of the Guide is never followed: its text stays, without a link.</summary>
    [Fact]
    public void WebLinksAreText()
    {
        var runs = GuideMarkdown.Inline("x", "Se [sidan](https://example.org) och [x](../CLAUDE.md).");

        Assert.Equal("Se sidan och x.", Text(runs));
        Assert.All(runs, run => Assert.Null(run.Link));
    }

    /// <summary>Anchors keep Swedish letters and digits; two headings with one text get different anchors.</summary>
    [Fact]
    public void AnchorsAreUnique()
    {
        Assert.Equal("fifo-först-in-först-ut", GuideMarkdown.Anchor("FIFO (först in, först ut)"));
        Assert.Equal("när-slås-växeln-ihop", GuideMarkdown.Anchor("När slås växeln ihop?"));

        var document = GuideMarkdown.Parse("x", "# T\n\n## Samma\n\n## Samma");

        Assert.Equal(["samma", "samma-2"], document.Headings.Select(heading => heading.Anchor));
    }

    /// <summary>Every text in docs/guide is built in, listed in the index, and read without a stray marker.</summary>
    [Fact]
    public void EveryTextIsBuiltInListedAndClean()
    {
        var files = Directory.GetFiles(Path.Combine(GraphSceneTests.Root(), "docs", "guide"), "*.md")
            .Select(Path.GetFileNameWithoutExtension)
            .Order(StringComparer.Ordinal);

        Assert.Equal(files, Library.Sections.Select(section => section.Id).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(Library.Groups, group => group.Name == GuideLibrary.OtherGroup);
        foreach (var section in Library.Sections)
        {
            Assert.NotEqual(section.Id, section.Title);
            var text = string.Concat(section.Document.Blocks.Select(Plain));
            Assert.DoesNotContain("**", text, StringComparison.Ordinal);
            Assert.DoesNotContain("](", text, StringComparison.Ordinal);
            Assert.DoesNotContain("`", text, StringComparison.Ordinal);
        }
    }

    /// <summary>Every link in the texts points to a section and a heading that exist.</summary>
    [Fact]
    public void EveryLinkLeadsSomewhere()
    {
        var links = Library.Sections.SelectMany(section => section.Document.Links).ToList();

        Assert.NotEmpty(links);
        Assert.All(links, link =>
        {
            var section = Library.Section(link.Section);
            Assert.True(section is not null, link.Section);
            Assert.True(link.Anchor is null || section.FindAnchor(link.Anchor) == link.Anchor, link.ToString());
        });
    }

    /// <summary>Every section the graph links to exists, with every heading the graph opens it at.</summary>
    [Fact]
    public void EveryTopicAndHeadingExists()
    {
        foreach (var topic in Enum.GetValues<GuideTopic>())
        {
            var section = Library.Section(topic.FileName());
            Assert.True(section is not null, topic.ToString());
            Assert.StartsWith(topic.Title(), section.Title, StringComparison.Ordinal);
            if (topic.DefaultHeading() is { } heading)
            {
                Assert.True(section.FindAnchor(heading) is not null, heading);
            }
        }

        var methods = Library.Section(GuideTopic.Methods.FileName())!;
        Assert.All(Enum.GetValues<TracingMethodKind>(), method =>
            Assert.NotNull(methods.FindAnchor(GuideTopics.MethodHeading(method))));
        var ends = Library.Section(GuideTopic.TrailEnds.FileName())!;
        Assert.All(Enum.GetValues<TrailEndReason>(), reason =>
            Assert.NotNull(ends.FindAnchor(GuideTopics.EndHeading(reason))));
    }

    /// <summary>A section's related sections are those it links to, without itself.</summary>
    [Fact]
    public void RelatedAreTheLinkedSections()
    {
        var library = Small();

        Assert.Equal(["vaxeladress"], library.Section("utxo-modellen")!.Related);
        Assert.Equal("Bitcoin", library.Section("utxo-modellen")!.Group);
        Assert.Equal(["Bitcoin", "Klustring", "Etiketter"], library.Groups.Select(group => group.Name));
    }

    /// <summary>A text not in the index is listed under Övrigt, never hidden.</summary>
    [Fact]
    public void UnlistedTextsAreShown()
    {
        var library = new GuideLibrary(new Dictionary<string, string> { ["ny"] = "# Ny\n\nText." });

        Assert.Equal(GuideLibrary.OtherGroup, Assert.Single(library.Groups).Name);
    }

    /// <summary>The way back goes one step at a time and never past the first place.</summary>
    [Fact]
    public void HistoryGoesBack()
    {
        var history = new GuideHistory();
        var a = new GuidePlace("a", null, null, "k1");
        var b = new GuidePlace("b", null, null, null);

        history.Start(a);
        history.Push(b);
        history.Push(b);

        Assert.Equal(2, history.Count);
        Assert.True(history.Back());
        Assert.Equal(a, history.Current);
        Assert.False(history.Back());
        Assert.Equal(a, history.Current);
    }

    /// <summary>A fuller answer replaces the latest about the same thing, even under a followed link.</summary>
    [Fact]
    public void HistoryUpdatesTheLatestAnswer()
    {
        var history = new GuideHistory();
        history.Start(new GuidePlace("a", null, null, "k1"));
        history.Push(new GuidePlace("b", null, null, null));

        Assert.True(history.UpdateLatest(new GuidePlace("c", null, null, "k1")));
        Assert.False(history.UpdateLatest(new GuidePlace("d", null, null, "k2")));

        Assert.Equal("b", history.Current!.Section);
        history.Back();
        Assert.Equal("c", history.Current!.Section);
    }

    /// <summary>The way back keeps at most its limit of places.</summary>
    [Fact]
    public void HistoryIsBounded()
    {
        var history = new GuideHistory();
        for (var i = 0; i < GuideHistory.MostPlaces + 10; i++)
        {
            var id = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            history.Push(new GuidePlace(id, null, null, null));
        }

        Assert.Equal(GuideHistory.MostPlaces, history.Count);
    }

    /// <summary>Varför? opens the most telling answer's section: an end before a heuristic before a label.</summary>
    [Fact]
    public void WhyPicksTheMostTellingSection()
    {
        WhyLine[] lines =
        [
            new("Etikett", GuideTopic.Labels),
            new("Länk", GuideTopic.ChangeAddress),
            new("Spårat", GuideTopic.Methods),
            new("Hopp", null),
        ];

        var request = GuideRequest.ForWhy("k", "bc1q…x0wl", lines, TracingMethodKind.Haircut);

        Assert.Equal(GuideTopic.ChangeAddress, request.Topic);
        Assert.Equal("När slås växeln ihop?", request.Heading);
        Assert.Equal("Varför? · bc1q…x0wl", request.Context!.Label);
        Assert.Equal("Haircut (andel)", request.Context.Lines[2].Heading);
        Assert.Null(request.Context.Lines[3].Heading);

        var ended = GuideRequest.ForWhy(
            "k", "x", [.. lines, new WhyLine("Mixer", GuideTopic.TrailEnds, "Riktiga slut")], TracingMethodKind.Fifo);
        Assert.Equal((GuideTopic.TrailEnds, "Riktiga slut"), (ended.Topic, ended.Heading));
    }

    /// <summary>Without any answer that links to the Guide, the graph's section opens at the inspector.</summary>
    [Fact]
    public void WhyWithoutTopicsOpensTheInspector()
    {
        var request = GuideRequest.ForWhy("k", "x", [new WhyLine("Hopp", null)], TracingMethodKind.Fifo);

        Assert.Equal((GuideTopic.Graph, GuideTopics.InspectorHeading), (request.Topic, request.Heading));
    }

    /// <summary>
    /// A page links each answer to its own section, except the one shown at the heading shown; an answer without a
    /// section has no link.
    /// </summary>
    [Fact]
    public void PageLinksTheOtherAnswers()
    {
        var library = Small();
        var request = GuideRequest.ForWhy(
            "k",
            "x",
            [new("Etikett", GuideTopic.Labels), new("Länk", GuideTopic.ChangeAddress), new("Hopp", null)],
            TracingMethodKind.Fifo);
        var section = library.Section("vaxeladress")!;
        var place = new GuidePlace(section.Id, section.FindAnchor(request.Heading), request.Context, request.Subject);

        var page = GuidePage.Of(library, place)!;

        Assert.Equal(new GuideLink("etiketter", "en-etikett-är-alltid-en-bedömning"), page.Answers[0].Link);
        Assert.Equal("Etiketter", page.Answers[0].LinkText);
        Assert.False(page.Answers[1].HasLink);
        Assert.False(page.Answers[2].HasLink);
        Assert.Equal("när-slås-växeln-ihop", page.Anchor);
    }

    /// <summary>
    /// The panel opens at the section with the heading marked, follows links, goes back, and closes; Varför? starts
    /// anew, while the ? and the method step on when the panel is open.
    /// </summary>
    [Fact]
    public void PanelOpensFollowsAndCloses()
    {
        var panel = new GuidePanelViewModel(Small());
        var why = new GuideRequest(GuideTopic.ChangeAddress, "När slås växeln ihop?", null, "k1");

        panel.Ask(why);

        Assert.True(panel.IsOpen);
        Assert.Equal("vaxeladress", panel.Page!.Section.Id);
        Assert.Equal("när-slås-växeln-ihop", panel.Page.Anchor);
        Assert.Equal("Guide · Klustring", panel.HeaderText);
        Assert.False(panel.BackCommand.CanExecute(null));

        panel.Go(new GuideLink("utxo-modellen", null));
        Assert.Equal("utxo-modellen", panel.Page.Section.Id);
        Assert.True(panel.BackCommand.CanExecute(null));

        panel.Ask(GuideRequest.ForTopic(GuideTopic.Labels));
        Assert.Equal("etiketter", panel.Page.Section.Id);

        panel.Back();
        panel.Back();
        Assert.Equal("vaxeladress", panel.Page.Section.Id);

        panel.Ask(why);
        Assert.False(panel.CanGoBack);

        panel.Close();
        Assert.False(panel.IsOpen);
        Assert.Equal(string.Empty, panel.HeaderText);
    }

    /// <summary>
    /// While open, the panel follows the choice: something else is a step on, a fuller answer about the same thing
    /// replaces the old, and nothing chosen changes nothing. A closed panel never opens by itself.
    /// </summary>
    [Fact]
    public void PanelFollowsTheChoice()
    {
        var panel = new GuidePanelViewModel(Small());
        var first = new GuideRequest(GuideTopic.Labels, null, new GuideContext("Varför? · A", []), "a");

        panel.Follow(first);
        Assert.False(panel.IsOpen);

        panel.Open(first, fresh: true);
        panel.Follow(new GuideRequest(GuideTopic.ChangeAddress, null, new GuideContext("Varför? · B", []), "b"));
        Assert.Equal("Varför? · B", panel.Page!.Label);
        Assert.True(panel.CanGoBack);

        var fuller = new GuideContext("Varför? · B", [new WhyLine("Länk", GuideTopic.Utxo)]);
        panel.Follow(new GuideRequest(GuideTopic.ChangeAddress, null, fuller, "b"));
        panel.Follow(null);

        Assert.Equal("Länk", Assert.Single(panel.Page.Answers).Text);
        panel.Back();
        Assert.Equal("Varför? · A", panel.Page.Label);
        Assert.False(panel.CanGoBack);
    }

    /// <summary>Öppna i Guide closes the panel and hands over the place shown.</summary>
    [Fact]
    public void PanelHandsOverToTheView()
    {
        var library = Small();
        var panel = new GuidePanelViewModel(library);
        var guide = new GuideViewModel(library);
        GuidePlace? wanted = null;
        panel.FullGuideWanted += (_, place) => wanted = place;

        panel.OpenInGuide();
        Assert.Null(wanted);

        panel.Open(new GuideRequest(GuideTopic.ChangeAddress, "När slås växeln ihop?", null, null), fresh: true);
        panel.OpenInGuide();

        Assert.False(panel.IsOpen);
        guide.Open(wanted!, AppView.Graph);
        Assert.Equal("vaxeladress", guide.Section!.Id);
        Assert.Equal("när-slås-växeln-ihop", guide.Anchor);
        Assert.Equal("Tillbaka till grafen", guide.ReturnText);
    }

    /// <summary>The view lists the groups, marks the section shown, follows links and goes back.</summary>
    [Fact]
    public void ViewShowsTheIndex()
    {
        var guide = new GuideViewModel(Small());
        AppView? returned = null;
        guide.ReturnWanted += (_, view) => returned = view;

        Assert.Equal("utxo-modellen", guide.Section!.Id);
        Assert.True(guide.Groups[0].Entries[0].IsCurrent);
        Assert.False(guide.HasReturn);

        guide.Show(guide.Groups[2].Entries[0]);
        Assert.Equal("etiketter", guide.Section.Id);
        Assert.Null(guide.Anchor);
        Assert.False(guide.Groups[0].Entries[0].IsCurrent);
        Assert.True(guide.Groups[2].Entries[0].IsCurrent);

        guide.Go(new GuideLink("vaxeladress", "när-slås-växeln-ihop"));
        Assert.Equal("när-slås-växeln-ihop", guide.Anchor);
        guide.Go(new GuideLink("finns-inte", null));
        Assert.Equal("vaxeladress", guide.Section.Id);

        guide.Return();
        Assert.Null(returned);
        guide.Open(new GuidePlace("etiketter", null, null, null), AppView.Graph);
        guide.Return();
        Assert.Equal(AppView.Graph, returned);
        Assert.False(guide.HasReturn);
    }

    /// <summary>Without texts the view shows no section and the panel does not open.</summary>
    [Fact]
    public void EmptyLibraryShowsNothing()
    {
        var empty = new GuideLibrary(new Dictionary<string, string>());
        var panel = new GuidePanelViewModel(empty);

        panel.Open(GuideRequest.ForTopic(GuideTopic.Graph), fresh: true);

        Assert.False(panel.IsOpen);
        Assert.False(new GuideViewModel(empty).HasSection);
    }

    /// <summary>Writes a block's text plainly.</summary>
    /// <param name="block">The block.</param>
    /// <returns>The text.</returns>
    private static string Plain(GuideBlock block) => block switch
    {
        GuideParagraph paragraph => Text(paragraph.Runs),
        GuideHeading heading => heading.Text,
        GuideQuote quote => Text(quote.Runs),
        GuideList list => string.Join(' ', list.Items.Select(item => Text(item.Runs))),
        GuideTable table => string.Join(' ', table.Header.Concat(table.Rows.SelectMany(row => row)).Select(Text)),
        _ => string.Empty,
    };

    /// <summary>Writes pieces plainly.</summary>
    /// <param name="runs">The pieces.</param>
    /// <returns>The text.</returns>
    private static string Text(IEnumerable<GuideRun> runs) => GuideMarkdown.Plain(runs);
}
