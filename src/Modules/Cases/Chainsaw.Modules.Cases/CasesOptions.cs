namespace Chainsaw.Modules.Cases;

/// <summary>Where the case files live and how hard a sharing password is to guess.</summary>
public sealed class CasesOptions
{
    /// <summary>The PBKDF2-SHA512 iterations for a new sharing password (the same as for the master key).</summary>
    public const int DefaultSharingIterations = 600_000;

    /// <summary>
    /// Gets or sets the folder that holds the case files. The application uses <c>Dokument\Chainsaw\Ärenden</c>;
    /// tests use a temporary folder. The folder is created when the first case is created.
    /// </summary>
    public string Directory { get; set; } = DefaultDirectory();

    /// <summary>
    /// Gets or sets the iterations used when a sharing password is set. Only tests lower it, to stay fast; the count
    /// is stored in the envelope, so a file always opens with the count it was written with.
    /// </summary>
    internal int SharingIterations { get; set; } = DefaultSharingIterations;

    /// <summary>Gets or sets the time zone for the journal's local time; tests fix it.</summary>
    internal TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Local;

    /// <summary>Gives the default cases folder, <c>Dokument\Chainsaw\Ärenden</c>.</summary>
    /// <returns>The full path of the folder.</returns>
    private static string DefaultDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Chainsaw", "Ärenden");
}
