using System.Text.RegularExpressions;

namespace Chainsaw.Recovery.Tests;

public sealed partial class RepositoryTests
{
    /// <summary>A PEM private key with content: the marker followed by a line of Base64.</summary>
    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----\s*[A-Za-z0-9+/=]{40,}")]
    private static partial Regex PrivateKeyBlock();

    [Fact]
    public void No_file_in_the_repository_holds_a_private_key()
    {
        var root = RepositoryRoot();
        var excluded = new[] { "bin", "obj", ".git", "TestResults" };
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar)
                .Any(excluded.Contains))
            .Where(file => new FileInfo(file).Length < 4 * 1024 * 1024);

        var offenders = files.Where(file => PrivateKeyBlock().IsMatch(File.ReadAllText(file))).ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_repository_ignores_private_key_files()
    {
        var ignore = File.ReadAllText(Path.Combine(RepositoryRoot(), ".gitignore"));

        Assert.Contains("*.private.pem", ignore, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Chainsaw.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root was not found.");
    }
}
