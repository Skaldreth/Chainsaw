using System.Reflection;
using Chainsaw.App.Guide;

namespace Chainsaw.App.Settings;

/// <summary>
/// The licences of what Chainsaw is built on (<c>THIRD_PARTY.md</c>, built in), shown under <em>Om Chainsaw</em> in
/// the settings with the Guide's renderer.
/// </summary>
public static class Licenses
{
    /// <summary>The resource's name, given in the project file.</summary>
    public const string ResourceName = "Chainsaw.App.Licenses.md";

    /// <summary>The section's id; its links lead nowhere, since web addresses are never followed.</summary>
    private const string SectionId = "licenses";

    /// <summary>Reads the licences built into the application.</summary>
    /// <returns>The licences as a section, or <see langword="null"/> when the text is not built in.</returns>
    public static GuideSection? Load() => Load(typeof(Licenses).Assembly);

    /// <summary>Reads the licences built into an assembly.</summary>
    /// <param name="assembly">The assembly.</param>
    /// <returns>The licences as a section, or <see langword="null"/> when the text is not built in.</returns>
    public static GuideSection? Load(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return new GuideSection(SectionId, string.Empty, GuideMarkdown.Parse(SectionId, reader.ReadToEnd()));
    }
}
