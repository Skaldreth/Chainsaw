using System.Text.Json;

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

    /// <summary>How the file is written: indented, so that it can be read and changed by hand.</summary>
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Creates the store for the default folder.</summary>
    public AppSettingsStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chainsaw"))
    {
    }

    /// <summary>Gets the path of the file.</summary>
    public string FilePath => Path.Combine(directory, FileName);

    /// <summary>Reads the settings.</summary>
    /// <returns>The settings, or the defaults when the file is missing or cannot be read.</returns>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Writes the settings, replacing the file in one step.</summary>
    /// <param name="settings">The settings.</param>
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(directory);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
