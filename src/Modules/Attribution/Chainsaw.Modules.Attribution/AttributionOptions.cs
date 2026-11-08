namespace Chainsaw.Modules.Attribution;

/// <summary>Where the Attribution module keeps what is shared between cases on this computer.</summary>
public sealed class AttributionOptions
{
    /// <summary>The name of the sanction lists' folder.</summary>
    private const string SanctionFolder = "Sanktionslistor";

    /// <summary>The name of the label library's folder.</summary>
    private const string LibraryFolder = "Etiketter";

    /// <summary>
    /// Gets or sets the application's data folder; the application uses <c>%LOCALAPPDATA%\Chainsaw</c>, tests a
    /// temporary folder. The sanction lists live in <c>Sanktionslistor</c> and the label library in <c>Etiketter</c>
    /// under it; the folders are created when something is first written.
    /// </summary>
    public string Directory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chainsaw");

    /// <summary>Gets or sets Chainabuse's API address; tests and the user's own proxy can change it.</summary>
    public Uri ChainabuseUri { get; set; } = new("https://api.chainabuse.com/v0/");

    /// <summary>Gets the sanction lists' folder.</summary>
    internal string SanctionDirectory => Path.Combine(Directory, SanctionFolder);

    /// <summary>Gets the label library's folder.</summary>
    internal string LibraryDirectory => Path.Combine(Directory, LibraryFolder);
}
