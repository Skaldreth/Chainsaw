using System.Text;

namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>
/// A label as the user sees it: the same label (subject, kind and name) from every origin merged into one, so that
/// independent origins show as confirmation. A label is a judgement, never a fact (principle 2); <see cref="Explain"/>
/// says what it rests on (principle 3).
/// </summary>
/// <param name="Subject">What it is on.</param>
/// <param name="Kind">What it says the subject is.</param>
/// <param name="Name">The name, or <see langword="null"/>.</param>
/// <param name="Entries">The sealed labels it is merged from, oldest first; never empty.</param>
public sealed record Label(LabelSubject Subject, LabelKind Kind, string? Name, IReadOnlyList<LabelEntry> Entries)
{
    /// <summary>Gets the distinct origins, oldest first.</summary>
    public IReadOnlyList<LabelOrigin> Origins { get; } =
        [.. Entries.DistinctBy(entry => entry.Origin.OriginId).Select(entry => entry.Origin)];

    /// <summary>Gets whether more than one independent origin says the same.</summary>
    public bool IsConfirmed => Origins.Count > 1;

    /// <summary>
    /// Gets whether every origin is another case: the label was not set here (<em>från ett annat ärende</em>).
    /// </summary>
    public bool IsOnlyFromOtherCases => Origins.All(origin => origin.IsFromOtherCase);

    /// <summary>Explains the label in plain Swedish: that it is a judgement, and every origin.</summary>
    /// <returns>The explanation.</returns>
    public string Explain()
    {
        var text = new StringBuilder()
            .Append("Bedömning, inte ett faktum: ")
            .Append(Subject)
            .Append(" är ")
            .Append(Kind.Describe().ToLowerInvariant());
        if (Name is not null)
        {
            text.Append(" (").Append(Name).Append(')');
        }

        text.Append('.');
        if (Subject.Kind == LabelSubjectKind.Cluster)
        {
            text.Append(" Etiketten gäller klustret och vilar också på klustringens slutsatser.");
        }

        if (IsConfirmed)
        {
            text.Append(" Bekräftad av ").Append(Origins.Count).Append(" oberoende ursprung.");
        }

        foreach (var origin in Origins)
        {
            text.AppendLine().Append("– ").Append(origin.Describe());
        }

        return text.ToString();
    }
}
