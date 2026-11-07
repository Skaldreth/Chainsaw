namespace Chainsaw.Modules.Security;

/// <summary>Where the vault file lives and how hard its master key is to guess.</summary>
public sealed class VaultOptions
{
    /// <summary>The PBKDF2-SHA512 iterations for a new or changed master key (OWASP's recommendation).</summary>
    public const int DefaultIterations = 600_000;

    /// <summary>
    /// Gets or sets the folder that holds the vault file. The application uses <c>%LOCALAPPDATA%\Chainsaw\</c>; tests
    /// use a temporary folder. The folder is created when the vault is first written.
    /// </summary>
    public string Directory { get; set; } = DefaultDirectory();

    /// <summary>
    /// Gets or sets the iterations used when a master key is set. Only tests lower it, to stay fast; the count used is
    /// stored in the file, so a vault always opens with the count it was written with.
    /// </summary>
    internal int Iterations { get; set; } = DefaultIterations;

    /// <summary>Gives the application's data folder, <c>%LOCALAPPDATA%\Chainsaw\</c> on Windows.</summary>
    /// <returns>The full path of the folder.</returns>
    private static string DefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chainsaw");
}
