using Chainsaw.App.Guide;

namespace Chainsaw.App.Search;

/// <summary>
/// Reads the Guide's sections for the search (sketch KR1a), so that a concept such as <c>utxo</c> finds its section,
/// written as the user writes it or as the section's file is named (<c>vaxeladress</c> finds <em>Växeladress</em>).
/// </summary>
internal static class GuideEntries
{
    /// <summary>How long the second line's start of the text may be.</summary>
    public const int SubLength = 72;

    /// <summary>Reads every section.</summary>
    /// <param name="library">The Guide.</param>
    /// <returns>An entry per section.</returns>
    public static IEnumerable<SearchEntry> Read(GuideLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        return library.Sections.Select(Entry);
    }

    /// <summary>Makes a section's entry: its title, its group and the text's first sentence.</summary>
    /// <param name="section">The section.</param>
    /// <returns>The entry.</returns>
    private static SearchEntry Entry(GuideSection section)
    {
        var lead = Lead(section);
        var sub = lead.Length == 0 ? section.Group : $"{section.Group} · {Shorten(FirstSentence(lead))}";
        IReadOnlyList<string> aliases =
        [
            section.Id.Replace('-', ' '),
            section.Group,
            .. section.Document.Headings.Select(heading => GuideMarkdown.Plain(heading.Runs)),
        ];
        return new SearchEntry(
            SearchKind.Guide, section.Title, sub, string.Empty, lead, SearchLook.Guide, null, section.Id)
        {
            Aliases = aliases,
        };
    }

    /// <summary>Gives the section's first paragraph as plain text, or an empty text when it has none.</summary>
    /// <param name="section">The section.</param>
    /// <returns>The text.</returns>
    public static string Lead(GuideSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return section.Document.Blocks.OfType<GuideParagraph>().FirstOrDefault() is { } paragraph
            ? GuideMarkdown.Plain(paragraph.Runs)
            : string.Empty;
    }

    /// <summary>Gives a text's first sentence.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Up to and without the first full stop followed by a space.</returns>
    private static string FirstSentence(string text)
    {
        var end = text.IndexOf(". ", StringComparison.Ordinal);
        return end > 0 ? text[..end] : text.TrimEnd('.');
    }

    /// <summary>Shortens a text to the second line's length, at a word.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text, with <c>…</c> when it was cut.</returns>
    private static string Shorten(string text)
    {
        if (text.Length <= SubLength)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', SubLength);
        return string.Concat(text.AsSpan(0, cut > 0 ? cut : SubLength), SearchText.Ellipsis);
    }
}
