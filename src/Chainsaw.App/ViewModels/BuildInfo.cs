using System.Reflection;

namespace Chainsaw.App.ViewModels;

/// <summary>Which build of Chainsaw this is: its version and the commit it was built from, for *Om Chainsaw*.</summary>
/// <param name="Version">The version, for example <c>0.1.0</c>.</param>
/// <param name="Commit">The full commit hash, or <see langword="null"/> when the build did not record it.</param>
public sealed record BuildInfo(string Version, string? Commit)
{
    /// <summary>The length of the short commit hash shown in the corner.</summary>
    private const int ShortLength = 7;

    /// <summary>Gets the commit, short, as shown in the corner of the lock screen; <c>okänd</c> when unknown.</summary>
    public string ShortCommit => Commit is { Length: > ShortLength } commit ? commit[..ShortLength] : Commit ?? "okänd";

    /// <summary>
    /// Reads the build from an assembly's informational version, which the .NET SDK writes as
    /// <c>version+commit</c> when it builds from a git repository.
    /// </summary>
    /// <param name="assembly">The program's assembly.</param>
    /// <returns>The build.</returns>
    public static BuildInfo From(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return Parse(informational ?? assembly.GetName().Version?.ToString() ?? "0.0.0");
    }

    /// <summary>Reads an informational version such as <c>0.1.0+f5895ba…</c>.</summary>
    /// <param name="informational">The informational version.</param>
    /// <returns>The build; without a <c>+</c>, the commit is unknown.</returns>
    public static BuildInfo Parse(string informational)
    {
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
        {
            return new BuildInfo(informational, null);
        }

        var commit = informational[(plus + 1)..];
        return new BuildInfo(informational[..plus], commit.Length == 0 ? null : commit);
    }
}
