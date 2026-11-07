using System.Globalization;

namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>
/// The name and version of a heuristic or another analysis. Every conclusion Chainsaw stores says which analysis
/// drew it and in which version (principle 2), so that a later version never silently changes what an earlier one
/// concluded.
/// </summary>
/// <param name="Name">
/// The stable name, for example <c>common-input-ownership</c>: lower-case ASCII letters, digits and <c>-</c>,
/// starting with a letter, at most 64 characters.
/// </param>
/// <param name="Version">The version, from 1; raised whenever the analysis may conclude differently.</param>
public sealed record HeuristicId(string Name, int Version)
{
    /// <summary>The longest name allowed.</summary>
    public const int MaximumNameLength = 64;

    /// <summary>Gets the stable name.</summary>
    public string Name { get; } = ValidName(Name);

    /// <summary>Gets the version, from 1.</summary>
    public int Version { get; } = Version >= 1
        ? Version
        : throw new ArgumentOutOfRangeException(nameof(Version), "A version starts at 1.");

    /// <summary>Gives the name and version as the user sees them, for example <c>coinjoin v1</c>.</summary>
    /// <returns>The name, a space, <c>v</c> and the version.</returns>
    public override string ToString() => Name + " v" + Version.ToString(CultureInfo.InvariantCulture);

    /// <summary>Checks a name against the rules in the record's documentation.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The same name.</returns>
    /// <exception cref="ArgumentException">The name breaks the rules.</exception>
    private static string ValidName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var valid = name.Length <= MaximumNameLength
            && char.IsAsciiLetterLower(name[0])
            && name.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
        return valid
            ? name
            : throw new ArgumentException(
                "A heuristic's name is lower-case ASCII letters, digits and '-'.", nameof(name));
    }
}
