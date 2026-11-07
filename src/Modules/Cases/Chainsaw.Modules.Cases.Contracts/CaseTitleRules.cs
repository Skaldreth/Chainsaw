namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>
/// Which case titles are accepted, in one place for the module and the user interface. A title is a short line in
/// plain language, for example <c>Investeringsbedrägeri via chatt</c>; it lives inside the encrypted case, never in a
/// file name.
/// </summary>
public static class CaseTitleRules
{
    /// <summary>The longest title that is accepted, in characters.</summary>
    public const int MaximumLength = 200;

    /// <summary>
    /// Gives the title as it is stored: white space around it removed and every run of white space inside it made one
    /// space, so that a title pasted from somewhere else stays one line.
    /// </summary>
    /// <param name="title">The title as the user wrote it.</param>
    /// <returns>The title as it is stored; empty when only white space was written.</returns>
    public static string Normalize(string? title) =>
        string.Join(' ', (title ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Checks whether a title, after <see cref="Normalize"/>, can be stored.</summary>
    /// <param name="title">The title as the user wrote it.</param>
    /// <returns>
    /// <see langword="true"/> when the normalized title is at most <see cref="MaximumLength"/> characters and holds no
    /// control characters; an empty title is accepted and means that the case has none.
    /// </returns>
    public static bool IsValid(string? title)
    {
        var normalized = Normalize(title);
        return normalized.Length <= MaximumLength && !normalized.Any(char.IsControl);
    }
}
