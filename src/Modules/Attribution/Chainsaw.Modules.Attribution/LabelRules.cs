using Chainsaw.Modules.Attribution.Contracts;

namespace Chainsaw.Modules.Attribution;

/// <summary>The rules for what a label may hold, stated once.</summary>
internal static class LabelRules
{
    /// <summary>The longest reason.</summary>
    public const int MaximumReasonLength = 1_000;

    /// <summary>The longest note.</summary>
    public const int MaximumNoteLength = 4_000;

    /// <summary>The longest name, source name or source version.</summary>
    public const int MaximumNameLength = 200;

    /// <summary>The source name of the user's own labels.</summary>
    public const string OwnSourceName = "egen";

    /// <summary>The version of the user's own labels: the label record's form.</summary>
    public const string OwnSourceVersion = "v1";

    /// <summary>Checks a required text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="maximum">The longest allowed.</param>
    /// <param name="name">The parameter's name.</param>
    /// <returns>The text, trimmed.</returns>
    /// <exception cref="ArgumentException">It is empty or too long.</exception>
    public static string Required(string? text, int maximum, string name)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > maximum)
        {
            throw new ArgumentException($"A text of 1 to {maximum} characters is needed.", name);
        }

        return trimmed;
    }

    /// <summary>Checks an optional text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="maximum">The longest allowed.</param>
    /// <param name="name">The parameter's name.</param>
    /// <returns>The text, trimmed, or <see langword="null"/> when it is empty.</returns>
    /// <exception cref="ArgumentException">It is too long.</exception>
    public static string? Optional(string? text, int maximum, string name)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : Required(trimmed, maximum, name);
    }

    /// <summary>
    /// Says whether a label may leave its case for the library. A label on a victim never does, whatever it is
    /// marked, since who was robbed is the most sensitive thing in a case.
    /// </summary>
    /// <param name="label">The label.</param>
    /// <returns><see langword="true"/> when it may spread.</returns>
    public static bool MaySpread(LabelEntry label) => !label.DoNotSpread && label.Kind != LabelKind.Victim;

    /// <summary>
    /// Merges labels that say the same about the same subject into one <see cref="Label"/> each, the best supported
    /// first: more independent origins, then labels set in this case, then the oldest.
    /// </summary>
    /// <param name="entries">The labels.</param>
    /// <returns>The merged labels.</returns>
    public static IReadOnlyList<Label> Merge(IEnumerable<LabelEntry> entries) =>
    [
        .. entries
            .GroupBy(entry => (entry.Subject, entry.Kind, Name: LabelStore.NameKey(entry.Name)))
            .Select(group => new Label(group.Key.Subject, group.Key.Kind, group.First().Name, [.. group]))
            .OrderByDescending(label => label.IndependentOrigins)
            .ThenBy(label => label.IsOnlyFromOtherCases)
            .ThenBy(label => label.Entries[0].Sequence),
    ];
}
