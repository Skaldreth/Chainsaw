using System.Text.RegularExpressions;
using Chainsaw.App.Guide;

namespace Chainsaw.App.Tests;

/// <summary>
/// The Guide's sections about the case, security and exits, the work in the graph, labels, requests and obfuscation:
/// that each is listed in the index, opens with an everyday analogy, and that every figure it names is drawn.
/// </summary>
public sealed partial class GuideSectionsTests
{
    /// <summary>The sections these tests cover, in the groups they are listed in.</summary>
    private static readonly (string Group, string[] Sections)[] Expected =
    [
        ("Kom igång", ["vyerna", "kortkommandon"]),
        ("Ärendet", ["nytt-arende", "arendevaljaren", "arendefilen", "journalen", "offline", "kommandoraden"]),
        ("Säkerhet och utgångar",
            ["huvudnyckeln", "lasning", "utgangen", "vpn-utgangar", "kallor", "aterstallningsnyckeln"]),
        ("Klustring", ["bryta-upp-kluster"]),
        ("Arbeta i grafen",
            ["inspektoren", "rora-sig-i-grafen", "tidslinjen", "det-som-holls-tillbaka", "historik", "annan-kedja",
                "dolda-utgangar"]),
        ("Etiketter", ["etikettbiblioteket", "chainabuse"]),
        ("Uttag och begäran", ["borser", "begaran"]),
        ("Förvirring",
            ["uppdelning", "fordrojning-och-hopp", "bryggor-och-swappar", "nastlade-tjanster", "privacy-coins",
                "usdt-pa-tron"]),
        ("Ordlista", ["ordlista"]),
    ];

    /// <summary>The texts built into the tests, as into the program.</summary>
    private static readonly GuideLibrary Library = GuideLibrary.Load();

    /// <summary>Each section is built in and listed in its group, never under <em>Övrigt</em>.</summary>
    [Fact]
    public void EverySectionIsListedInItsGroup()
    {
        foreach (var (group, sections) in Expected)
        {
            foreach (var id in sections)
            {
                var section = Library.Section(id);
                Assert.True(section is not null, id);
                Assert.Equal(group, section.Group);
            }
        }
    }

    /// <summary>Each section opens with the everyday analogy as a quote, before anything else is said.</summary>
    [Fact]
    public void EverySectionOpensWithAnAnalogy()
    {
        foreach (var id in Expected.SelectMany(entry => entry.Sections))
        {
            var text = File.ReadAllText(Path.Combine(GraphSceneTests.Root(), "docs", "guide", id + ".md"));
            var firstBlock = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)[1];
            Assert.StartsWith("> **Som i vardagen:**", firstBlock, StringComparison.Ordinal);
        }
    }

    /// <summary>Every figure a Guide text names is a drawing in one of the Guide's figure dictionaries.</summary>
    [Fact]
    public void EveryFigureIsDrawn()
    {
        var root = GraphSceneTests.Root();
        var themes = Path.Combine(root, "src", "Chainsaw.App", "Themes");
        var drawn = Directory.GetFiles(themes, "GuideFigures*.xaml")
            .SelectMany(file => FigureKey().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);
        var named = Directory.GetFiles(Path.Combine(root, "docs", "guide"), "*.md")
            .SelectMany(file => FigureLine().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value))
            .ToList();

        Assert.Contains(named, id => id.StartsWith("s-", StringComparison.Ordinal));
        Assert.All(named, id => Assert.Contains(id, drawn));
    }

    /// <summary>The sections' figures are merged into the application's resources, so that the Guide finds them.</summary>
    [Fact]
    public void TheSectionsFiguresAreMerged()
    {
        var app = File.ReadAllText(Path.Combine(GraphSceneTests.Root(), "src", "Chainsaw.App", "App.xaml"));

        Assert.Contains("Themes/GuideFigures.Sections.xaml", app, StringComparison.Ordinal);
    }

    /// <summary>A figure's key in a resource dictionary: <c>x:Key="Figure.&lt;id&gt;"</c>.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex("x:Key=\"Figure\\.([a-z0-9-]+)\"")]
    private static partial Regex FigureKey();

    /// <summary>A figure in a Guide text: <c>![caption](figur:&lt;id&gt;)</c>.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"!\[[^\]]*\]\(figur:([a-z0-9-]+)\)")]
    private static partial Regex FigureLine();
}
