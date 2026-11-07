namespace Chainsaw.App.Guide;

/// <summary>A section of the Guide: one text in <c>docs/guide</c>, read, with its place in the index.</summary>
/// <param name="Id">The id: the file's name without <c>.md</c>, for example <c>vaxeladress</c>.</param>
/// <param name="Group">The group it is listed under, for example <c>Klustring</c>.</param>
/// <param name="Document">The text, read.</param>
public sealed record GuideSection(string Id, string Group, GuideDocument Document)
{
    /// <summary>Gets the title.</summary>
    public string Title => Document.Title;

    /// <summary>
    /// Gets the other sections the text links to, in the order they first come, for <em>Läs också</em> under the
    /// text.
    /// </summary>
    public IReadOnlyList<string> Related =>
        [.. Document.Links.Select(link => link.Section).Where(id => id != Id).Distinct(StringComparer.Ordinal)];

    /// <summary>Finds a heading by its anchor or by its text.</summary>
    /// <param name="heading">The anchor, or the heading as written.</param>
    /// <returns>The heading's anchor, or <see langword="null"/> when the section has no such heading.</returns>
    public string? FindAnchor(string? heading)
    {
        if (heading is null)
        {
            return null;
        }

        var anchor = GuideMarkdown.Anchor(heading);
        return Document.Headings.FirstOrDefault(h => h.Anchor == heading || h.Anchor == anchor)?.Anchor;
    }
}

/// <summary>A group of sections in the index, in the order they are read.</summary>
/// <param name="Name">The group's name.</param>
/// <param name="Sections">The sections.</param>
public sealed record GuideGroup(string Name, IReadOnlyList<GuideSection> Sections);
