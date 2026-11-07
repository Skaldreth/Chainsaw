namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>A label the user, or an analysis of behaviour, wants to set.</summary>
/// <param name="Subject">What it is on.</param>
/// <param name="Kind">What it says the subject is.</param>
/// <param name="Name">The service's or the owner's name, or <see langword="null"/>.</param>
/// <param name="Reason">Why; optional (empty for none), at most 1 000 characters.</param>
/// <param name="Note">The investigator's own note, or <see langword="null"/>; at most 4 000 characters.</param>
/// <param name="DoNotSpread">Whether the label stays in this case (<em>Sprid inte</em>).</param>
public sealed record LabelDraft(
    LabelSubject Subject,
    LabelKind Kind,
    string? Name,
    string Reason,
    string? Note = null,
    bool DoNotSpread = false)
{
    /// <summary>
    /// Gets the source: <see cref="LabelSource.Own"/> (the default), <see cref="LabelSource.Behaviour"/> or
    /// <see cref="LabelSource.OpenSource"/>. Sanction lists and other cases have their own ways in.
    /// </summary>
    public LabelSource Source { get; init; } = LabelSource.Own;

    /// <summary>Gets the analysis' or open source's name; required unless the label is the user's own.</summary>
    public string? SourceName { get; init; }

    /// <summary>Gets the analysis' or open source's version; required unless the label is the user's own.</summary>
    public string? SourceVersion { get; init; }
}
