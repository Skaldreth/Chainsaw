namespace Chainsaw.Modules.Cases;

/// <summary>Where the case files live and how hard a sharing password is to guess.</summary>
public sealed class CasesOptions
{
    /// <summary>The PBKDF2-SHA512 iterations for a new sharing password (the same as for the master key).</summary>
    public const int DefaultSharingIterations = 600_000;

    /// <summary>
    /// Gets or sets the folder that holds the case files. The application uses
    /// <c>%LOCALAPPDATA%\Chainsaw\Ärenden</c>, which is not synchronised to the cloud the way Documents often is;
    /// tests use a temporary folder. The folder is created when the first case is created.
    /// </summary>
    public string Directory { get; set; } = DefaultDirectory("Ärenden");

    /// <summary>
    /// Gets or sets the folder for this computer's anchors of the cases' chains (see <c>AnchorStore</c>). The
    /// application uses <c>%LOCALAPPDATA%\Chainsaw\Ankare</c>.
    /// </summary>
    public string AnchorDirectory { get; set; } = DefaultDirectory("Ankare");

    /// <summary>
    /// Gets or sets the iterations used when a sharing password is set. Only tests lower it, to stay fast; the count
    /// is stored in the envelope, so a file always opens with the count it was written with.
    /// </summary>
    internal int SharingIterations { get; set; } = DefaultSharingIterations;

    /// <summary>Gets or sets the time zone for the journal's local time; tests fix it.</summary>
    internal TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Local;

    /// <summary>Gives a folder under the application's local data folder, <c>%LOCALAPPDATA%\Chainsaw\</c>.</summary>
    /// <param name="name">The folder's name.</param>
    /// <returns>The full path of the folder.</returns>
    private static string DefaultDirectory(string name) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chainsaw", name);
}
