namespace Chainsaw.App.Guide;

/// <summary>How a piece of a Guide text is written; the styles combine (bold inside italic).</summary>
[Flags]
public enum GuideRunStyle
{
    /// <summary>Plain text.</summary>
    None = 0,

    /// <summary>Bold (<c>**text**</c>).</summary>
    Strong = 1,

    /// <summary>Italic (<c>*text*</c>).</summary>
    Emphasis = 2,

    /// <summary>Code, an address or a txid (<c>`text`</c>), written in Cascadia Mono.</summary>
    Code = 4,
}

/// <summary>A link from one section of the Guide to another, or to a heading in a section.</summary>
/// <param name="Section">The section's id: its file in <c>docs/guide</c> without <c>.md</c>.</param>
/// <param name="Anchor">The heading's anchor (<see cref="GuideMarkdown.Anchor"/>), or <see langword="null"/>.</param>
public sealed record GuideLink(string Section, string? Anchor);

/// <summary>A piece of text in one style, and the link it belongs to.</summary>
/// <param name="Text">The text.</param>
/// <param name="Style">The style.</param>
/// <param name="Link">The link, or <see langword="null"/> when the piece is not part of one.</param>
public sealed record GuideRun(string Text, GuideRunStyle Style, GuideLink? Link)
{
    /// <summary>Gets whether the piece is bold.</summary>
    public bool IsStrong => Style.HasFlag(GuideRunStyle.Strong);

    /// <summary>Gets whether the piece is italic.</summary>
    public bool IsEmphasis => Style.HasFlag(GuideRunStyle.Emphasis);

    /// <summary>Gets whether the piece is code.</summary>
    public bool IsCode => Style.HasFlag(GuideRunStyle.Code);
}
