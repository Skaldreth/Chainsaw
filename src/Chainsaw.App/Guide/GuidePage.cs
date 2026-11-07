using Chainsaw.App.Graph;

namespace Chainsaw.App.Guide;

/// <summary>An answer above a section, with the link to its own section when that is not the one shown.</summary>
/// <param name="Text">The answer.</param>
/// <param name="Link">The answer's section, or <see langword="null"/> when it has none or it is the one shown.</param>
/// <param name="LinkText">The link's text: the section's title; empty without a link.</param>
public sealed record GuideAnswer(string Text, GuideLink? Link, string LinkText)
{
    /// <summary>Gets whether the answer links to another section.</summary>
    public bool HasLink => Link is not null;
}

/// <summary>What a place in the Guide shows: the section, its marked heading, and the answers above it.</summary>
/// <param name="Section">The section.</param>
/// <param name="Anchor">The marked heading's anchor, or <see langword="null"/>.</param>
/// <param name="Label">What the answers are about; empty without answers.</param>
/// <param name="Answers">The answers for what is chosen; empty when the place has none.</param>
public sealed record GuidePage(
    GuideSection Section, string? Anchor, string Label, IReadOnlyList<GuideAnswer> Answers)
{
    /// <summary>Gets whether the page has answers above the section.</summary>
    public bool HasAnswers => Answers.Count > 0;

    /// <summary>Gives what a place shows, or <see langword="null"/> when its section is not in the library.</summary>
    /// <param name="library">The library.</param>
    /// <param name="place">The place.</param>
    /// <returns>The page.</returns>
    public static GuidePage? Of(GuideLibrary library, GuidePlace place)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(place);
        if (library.Section(place.Section) is not { } section)
        {
            return null;
        }

        var answers = place.Context?.Lines.Select(line => Answer(library, place, line)).ToList() ?? [];
        return new GuidePage(section, place.Anchor, place.Context?.Label ?? string.Empty, answers);
    }

    /// <summary>
    /// Gives an answer with its link: a link to the answer's section and heading unless that is what the page shows.
    /// </summary>
    /// <param name="library">The library.</param>
    /// <param name="place">The place shown.</param>
    /// <param name="line">The answer.</param>
    /// <returns>The answer.</returns>
    private static GuideAnswer Answer(GuideLibrary library, GuidePlace place, WhyLine line)
    {
        if (line.Topic is not { } topic || library.Section(topic.FileName()) is not { } section)
        {
            return new GuideAnswer(line.Text, null, string.Empty);
        }

        var anchor = section.FindAnchor(line.Heading ?? topic.DefaultHeading());
        return section.Id == place.Section && anchor == place.Anchor
            ? new GuideAnswer(line.Text, null, string.Empty)
            : new GuideAnswer(line.Text, new GuideLink(section.Id, anchor), section.Title);
    }
}
