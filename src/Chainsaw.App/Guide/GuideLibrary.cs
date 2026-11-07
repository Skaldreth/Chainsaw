using System.Reflection;

namespace Chainsaw.App.Guide;

/// <summary>
/// The Guide's sections, read from the texts in <c>docs/guide</c> that are built into the program, and their index in
/// groups (sketch GD1a). The texts are in the repository and written while the functions are built; this table says
/// only where each is listed.
/// </summary>
public sealed class GuideLibrary
{
    /// <summary>The start of a text's resource name; the file's name follows.</summary>
    public const string ResourcePrefix = "Chainsaw.App.Guide.";

    /// <summary>The ending of a text's file and resource name.</summary>
    public const string FileEnding = ".md";

    /// <summary>The group a text not in <see cref="Index"/> is listed under, so that no text is ever hidden.</summary>
    public const string OtherGroup = "Övrigt";

    /// <summary>The groups and the sections in each, in the order they are read.</summary>
    public static readonly IReadOnlyList<(string Group, IReadOnlyList<string> Sections)> Index =
    [
        ("Bitcoin", ["utxo-modellen", "adresstyper", "startpunkter", "kontroll-mot-kedjan"]),
        ("Klustring", ["common-input-ownership", "vaxeladress", "peel-chain", "coinjoin"]),
        ("Spårning", ["grafen", "sparningsmetoder", "sparet-tar-slut", "kurser"]),
        ("Etiketter", ["etiketter"]),
        ("Förvirring", ["kontroller-och-forslag", "mixerns-kandidater", "omraden"]),
    ];

    /// <summary>The sections by id.</summary>
    private readonly Dictionary<string, GuideSection> _sections;

    /// <summary>Reads the texts into sections and groups.</summary>
    /// <param name="texts">The texts by section id.</param>
    public GuideLibrary(IReadOnlyDictionary<string, string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var groupOf = Index
            .SelectMany(entry => entry.Sections.Select(id => (Id: id, entry.Group)))
            .ToDictionary(entry => entry.Id, entry => entry.Group, StringComparer.Ordinal);
        _sections = texts.ToDictionary(
            text => text.Key,
            text => new GuideSection(
                text.Key, groupOf.GetValueOrDefault(text.Key, OtherGroup), GuideMarkdown.Parse(text.Key, text.Value)),
            StringComparer.Ordinal);
        var listed = Index
            .Select(entry => new GuideGroup(
                entry.Group, [.. entry.Sections.Where(_sections.ContainsKey).Select(id => _sections[id])]))
            .ToList();
        var others = _sections.Values.Where(section => section.Group == OtherGroup).OrderBy(s => s.Id).ToList();
        if (others.Count > 0)
        {
            listed.Add(new GuideGroup(OtherGroup, others));
        }

        Groups = [.. listed.Where(group => group.Sections.Count > 0)];
    }

    /// <summary>Gets the groups, each with its sections, in the order they are read.</summary>
    public IReadOnlyList<GuideGroup> Groups { get; }

    /// <summary>Gets every section, in the index's order.</summary>
    public IEnumerable<GuideSection> Sections => Groups.SelectMany(group => group.Sections);

    /// <summary>Gets the first section, shown when the Guide is opened without a section.</summary>
    public GuideSection? First => Sections.FirstOrDefault();

    /// <summary>Reads the texts built into the program (<c>docs/guide/*.md</c> as resources).</summary>
    /// <returns>The library.</returns>
    public static GuideLibrary Load() => Load(typeof(GuideLibrary).Assembly);

    /// <summary>Reads the texts built into an assembly.</summary>
    /// <param name="assembly">The assembly.</param>
    /// <returns>The library.</returns>
    public static GuideLibrary Load(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                || !name.EndsWith(FileEnding, StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            texts[name[ResourcePrefix.Length..^FileEnding.Length]] = reader.ReadToEnd();
        }

        return new GuideLibrary(texts);
    }

    /// <summary>Gives a section by its id.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The section, or <see langword="null"/> when there is none.</returns>
    public GuideSection? Section(string? id) => id is not null ? _sections.GetValueOrDefault(id) : null;
}
