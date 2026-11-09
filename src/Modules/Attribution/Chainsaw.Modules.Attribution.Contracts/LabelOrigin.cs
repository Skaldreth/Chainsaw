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
/// <param name="Reason">Why, in plain language; it follows a label into other cases.</param>
/// <param name="Note">The investigator's own note, or <see langword="null"/>; it follows a label into other cases.
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

    /// <summary>
    /// Gets the number of the case the label was set in, for a label that came from another case;
    /// <see langword="null"/> for a label set in this case, and for a copy whose case is not known (the user's
    /// decision 2026-11-07, <em>Allt följer med</em>).
    /// </summary>
    public string? CaseNumber { get; init; }

    /// <summary>Gets whether the label came from another case through the library.</summary>
    public bool IsFromOtherCase => Source == LabelSource.OtherCase;

    /// <summary>Gets the licence of an open source Chainsaw knows, after a comma, or nothing.</summary>
    private string LicenseText => OpenSourceLicenses.Of(SourceName) is { } license ? ", " + license : string.Empty;

    /// <summary>Describes the origin in plain Swedish, for <em>varför?</em>.</summary>
    /// <returns>One line.</returns>
    public string Describe()
    {
        var who = $"{SetBy.Name} {SetUtc.UtcDateTime:yyyy-MM-dd HH:mm} UTC";
        var what = OriginalSource switch
        {
            LabelSource.Own => $"egen etikett av {who}",
            LabelSource.SanctionList => $"{SourceName} ({SourceVersion}), kontrollerad av {who}",
            LabelSource.OpenSource => $"öppen källa {SourceName} ({SourceVersion}{LicenseText}), läst av {who}",
            LabelSource.Behaviour => $"beteende enligt {SourceName} {SourceVersion}, av {who}",
            LabelSource.Provider => $"underleverantören {SourceName} ({SourceVersion}), hämtad av {who}",
            _ => $"{SourceName} ({SourceVersion}), av {who}",
        };
        var line = (IsFromOtherCase, CaseNumber) switch
        {
            (true, { } number) => $"Från ärende {number}: {what}",
            (true, null) => $"Från ett annat ärende: {what}",
            _ => char.ToUpperInvariant(what[0]) + what[1..],
        };
        return Reason is null ? line + "." : $"{line}: {Reason}";
    }
}
