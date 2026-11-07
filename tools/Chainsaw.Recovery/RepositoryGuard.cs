namespace Chainsaw.Recovery;

/// <summary>
/// Keeps the private key out of every git repository: a key pair is never written into a folder that is inside a
/// work tree, where it could be committed by mistake.
/// </summary>
internal static class RepositoryGuard
{
    /// <summary>The name of git's folder (or, in a linked work tree, file) at the root of a work tree.</summary>
    private const string GitEntry = ".git";

    /// <summary>Finds the root of the git work tree a folder is in.</summary>
    /// <param name="directory">The folder, which need not exist yet.</param>
    /// <returns>The root of the work tree, or <see langword="null"/> when the folder is in none.</returns>
    public static string? FindRepositoryRoot(string directory)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(directory));
            current is not null;
            current = current.Parent)
        {
            var git = Path.Combine(current.FullName, GitEntry);
            if (Directory.Exists(git) || File.Exists(git))
            {
                return current.FullName;
            }
        }

        return null;
    }
}
