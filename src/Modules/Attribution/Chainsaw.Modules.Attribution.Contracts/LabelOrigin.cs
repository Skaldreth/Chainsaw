using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>
/// Where one label comes from: the source, who, when, why and the version (principles 2 and 3). A label is always a
/// judgement, never a fact, and its origin says what it rests on.
/// </summary>
/// <param name="OriginId">
/// The origin's stable identity, the same in every case the label reaches: a random id for an own label, and for a
/// sanction list the list's hash, the entry and the address. Two labels with the same origin are one and the same
/// judgement and never count as confirming each other.
/// </param>
/// <param name="Source">How this case got the label.</param>
/// <param name="OriginalSource">
/// Where the label first came from; the same as <paramref name="Source"/> except for
/// <see cref="LabelSource.OtherCase"/>, where it says what the label was in the case it came from.
/// </param>
/// <param name="SourceName">The source's name, for example <c>egen</c>, <c>OFAC SDN</c> or an analysis.</param>
/// <param name="SourceVersion">
/// The source's version: for a sanction list its publish date and SHA-256, for an analysis its version.
/// </param>
/// <param name="SetBy">Who set the label, or who ran the check that found it.</param>
/// <param name="SetUtc">When, in UTC.</param>
/// <param name="Reason">
/// Why, in plain language; <see langword="null"/> for a label from another case, whose reason stays there.
/// </param>
/// <param name="Note">
/// The investigator's own note; <see langword="null"/> when there is none or the label came from another case.
/// </param>
public sealed record LabelOrigin(
    string OriginId,
    LabelSource Source,
    LabelSource OriginalSource,
    string SourceName,
    string SourceVersion,
    OperatorProfile SetBy,
    DateTimeOffset SetUtc,
    string? Reason,
    string? Note)
{
    /// <summary>
    /// Gets what makes the origin independent of another: the person (name, title and organisation) for an own
    /// label, and the source for a list, an open source or an analysis, so that the same person setting a label
    /// twice, or a newer version of the same list or analysis, never counts as confirmation.
    /// </summary>
    public string IndependenceKey => OriginalSource == LabelSource.Own
        ? $"{LabelSource.Own}/{SetBy.Name}/{SetBy.Title}/{SetBy.Organization}"
        : $"{OriginalSource}/{SourceName}";

    /// <summary>Gets whether the label came from another case through the library.</summary>
    public bool IsFromOtherCase => Source == LabelSource.OtherCase;

    /// <summary>Describes the origin in plain Swedish, for <em>varför?</em>.</summary>
    /// <returns>One line.</returns>
    public string Describe()
    {
        var who = $"{SetBy.Name} {SetUtc.UtcDateTime:yyyy-MM-dd HH:mm} UTC";
        var what = OriginalSource switch
        {
            LabelSource.Own => $"egen etikett av {who}",
            LabelSource.SanctionList => $"{SourceName} ({SourceVersion}), kontrollerad av {who}",
            LabelSource.OpenSource => $"öppen källa {SourceName} ({SourceVersion}), hämtad av {who}",
            LabelSource.Behaviour => $"beteende enligt {SourceName} {SourceVersion}, av {who}",
            _ => $"{SourceName} ({SourceVersion}), av {who}",
        };
        var line = IsFromOtherCase ? $"Från ett annat ärende: {what}" : char.ToUpperInvariant(what[0]) + what[1..];
        return Reason is null ? line + "." : $"{line}: {Reason}";
    }
}
