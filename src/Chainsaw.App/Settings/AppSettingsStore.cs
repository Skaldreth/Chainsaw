using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chainsaw.App.Settings;

/// <summary>
/// Reads and writes <see cref="AppSettings"/> as <c>settings.json</c> in Chainsaw's folder under the user's local
/// application data. A missing or unreadable file gives the defaults, so that a damaged file never stops Chainsaw
/// from starting or from locking.
/// </summary>
/// <param name="directory">The folder; by default <c>%LOCALAPPDATA%\Chainsaw</c>, next to the vault.</param>
public sealed class AppSettingsStore(string directory)
{
    /// <summary>The file's name.</summary>
    public const string FileName = "settings.json";

    /// <summary>
    /// How the file is written: indented and with the choices as words, so that it can be read and changed by hand.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Lets one write at a time, so that two quick changes never meet in the temporary file.</summary>
    private readonly Lock _writing = new();

    /// <summary>Creates the store for the default folder.</summary>
    public AppSettingsStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chainsaw"))
    {
    }

    /// <summary>Gets the path of the file.</summary>
    public string FilePath => Path.Combine(directory, FileName);

    /// <summary>Reads the settings.</summary>
    /// <returns>
    /// The settings with only known values (<see cref="AppSettings.Known"/>), or the defaults when the file is missing
    /// or cannot be read.
    /// </returns>
    public AppSettings Load() => (SettingsFile.ReadJson<AppSettings>(FilePath, Json) ?? new AppSettings()).Known();

    /// <summary>
    /// Removes the file unless it can be read and says VPN, after a choice of VPN could not be written, so that the next
    /// start takes the defaults (VPN) and never a Windows that the file still says. A file that cannot be removed is
    /// left as it is.
    /// </summary>
    public void ForgetIfWindows()
    {
        lock (_writing)
        {
            try
            {
                if (File.Exists(FilePath) && !SaysVpn())
                {
                    File.Delete(FilePath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Nothing more can be done; the dialog says that the settings were not saved.
            }
        }
    }

    /// <summary>
    /// Reads the way out the file names, without the defaults: the first-run setup's step Utgång starts from it, and
    /// from Windows only when the file names none (sketch FS2a).
    /// </summary>
    /// <returns>The way out saved, or <see langword="null"/> when the file is missing, unreadable or has none.</returns>
    public Modules.Network.Contracts.ExitRoute? SavedRoute()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(SettingsFile.ReadShared(FilePath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(nameof(AppSettings.ExitRoute), out var route)
                || route.ValueKind != JsonValueKind.String
                || !Enum.TryParse<Modules.Network.Contracts.ExitRoute>(route.GetString(), out var saved)
                || !Enum.IsDefined(saved))
            {
                return null;
            }

            return saved;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads the file strictly: whether it can be read and says VPN, with no fall back to the defaults.</summary>
    /// <returns><see langword="true"/> only when the file was read and says VPN.</returns>
    private bool SaysVpn() =>
        SettingsFile.ReadJson<AppSettings>(FilePath, Json)?.ExitRoute == Modules.Network.Contracts.ExitRoute.Vpn;

    /// <summary>Writes the settings, replacing the file in one step.</summary>
    /// <param name="settings">The settings.</param>
    public void Save(AppSettings settings)
    {
        lock (_writing)
        {
            // A failed write of VPN would otherwise remove the file (ForgetIfWindows), so the write retries on Windows'
            // short locks (SettingsFile).
            SettingsFile.WriteReplacing(FilePath, JsonSerializer.Serialize(settings, Json));
        }
    }
}
