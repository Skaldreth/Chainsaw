using System.Text.RegularExpressions;

namespace Chainsaw.App.Tests;

/// <summary>
/// Every <c>StaticResource</c> a XAML file names is defined before it is used, where WPF looks for it. WPF looks the
/// key up only when the view is loaded, so a missing key builds fine and stops Chainsaw when it starts.
/// </summary>
public sealed partial class XamlResourceTests
{
    /// <summary>The application's dictionaries, in the order App.xaml merges them.</summary>
    private static readonly string[] Themes =
        ["Colors", "Icons", "Logo", "Controls", "GuideFigures", "GuideFigures.Sections"];

    /// <summary>
    /// Each key is defined above its use: in the application's dictionaries, in a dictionary the file merges, or
    /// earlier in the file itself.
    /// </summary>
    [Fact]
    public void EveryStaticResourceIsDefined()
    {
        var app = Path.Combine(GraphSceneTests.Root(), "src", "Chainsaw.App");
        var known = new HashSet<string>(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var theme in Themes)
        {
            CheckFile(Path.Combine(app, "Themes", theme + ".xaml"), known, missing);
        }

        var files = Directory.GetFiles(app, "*.xaml", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Themes{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
        foreach (var file in files)
        {
            var scope = new HashSet<string>(known, StringComparer.Ordinal);
            foreach (Match merged in MergedSource().Matches(File.ReadAllText(file)))
            {
                var path = Path.Combine(Path.GetDirectoryName(file)!, merged.Groups[1].Value);
                if (File.Exists(path))
                {
                    scope.UnionWith(Keys(File.ReadAllText(path)));
                }
            }

            CheckFile(file, scope, missing);
        }

        Assert.Empty(missing);
    }

    /// <summary>
    /// Walks a file line by line, noting each key it defines and each <c>StaticResource</c> whose key is not yet in
    /// <paramref name="scope"/>.
    /// </summary>
    private static void CheckFile(string file, HashSet<string> scope, List<string> missing)
    {
        var lines = File.ReadAllLines(file);
        for (var index = 0; index < lines.Length; index++)
        {
            foreach (Match use in StaticResource().Matches(lines[index]))
            {
                if (!scope.Contains(use.Groups[1].Value))
                {
                    missing.Add($"{Path.GetFileName(file)}:{index + 1} {use.Groups[1].Value}");
                }
            }

            scope.UnionWith(Keys(lines[index]));
        }
    }

    /// <summary>The keys a piece of XAML defines.</summary>
    private static IEnumerable<string> Keys(string xaml) =>
        Key().Matches(xaml).Select(match => match.Groups[1].Value);

    /// <summary>A key definition, <c>x:Key="…"</c>.</summary>
    [GeneratedRegex("x:Key=\"([^\"]+)\"")]
    private static partial Regex Key();

    /// <summary>A use, <c>{StaticResource …}</c>, with a plain key.</summary>
    [GeneratedRegex(@"\{StaticResource ([^{}\s]+)\}")]
    private static partial Regex StaticResource();

    /// <summary>A merged dictionary by its relative source.</summary>
    [GeneratedRegex("ResourceDictionary Source=\"([^\"]+)\"")]
    private static partial Regex MergedSource();
}
