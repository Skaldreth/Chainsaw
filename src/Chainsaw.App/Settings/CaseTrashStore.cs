using System.Text.Json;

namespace Chainsaw.App.Settings;

/// <summary>A case thrown in the trash on the start page (sketch SO1a): hidden there, never taken off the disk.</summary>
/// <param name="CaseNumber">The case.</param>
/// <param name="ThrownUtc">When it was thrown.</param>
public sealed record TrashedCase(string CaseNumber, DateTimeOffset ThrownUtc);

/// <summary>Keeps the cases thrown on the start page on this computer, never in the cases (like the piles).</summary>
public interface ICaseTrashStore
{
    /// <summary>Reads the thrown cases.</summary>
    /// <returns>The cases, or none when there are none or they cannot be read.</returns>
    IReadOnlyList<TrashedCase> Load();

    /// <summary>Writes the thrown cases in place of those saved before.</summary>
    /// <param name="cases">The cases.</param>
    /// <returns><see langword="true"/> when they were written.</returns>
    bool Save(IReadOnlyList<TrashedCase> cases);
}

/// <summary>
/// Keeps the cases thrown on the start page as <c>trash.json</c> in Chainsaw's folder under the user's local
/// application data, next to <c>piles.json</c> and written the same way (<see cref="SettingsFile"/>). It holds only
/// case numbers and times, which the start page already shows unlocked; a missing or unreadable file gives none, so
/// that a damaged file never hides a case for good.
/// </summary>
/// <param name="directory">The folder; by default <c>%LOCALAPPDATA%\Chainsaw</c>.</param>
public sealed class CaseTrashStore(string directory) : ICaseTrashStore
{
    /// <summary>The file's name.</summary>
    public const string FileName = "trash.json";

    /// <summary>How the file is written: indented, so that it can be read by hand.</summary>
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Lets one write at a time.</summary>
    private readonly Lock _writing = new();

    /// <summary>Creates the store for the default folder.</summary>
    public CaseTrashStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chainsaw"))
    {
    }

    /// <summary>Gets the path of the file.</summary>
    public string FilePath => Path.Combine(directory, FileName);

    /// <inheritdoc />
    public IReadOnlyList<TrashedCase> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            var file = JsonSerializer.Deserialize<TrashFile>(SettingsFile.ReadShared(FilePath), Json);
            return
            [
                .. (file?.Cases ?? []).Where(saved => !string.IsNullOrWhiteSpace(saved?.Case))
                    .Select(saved => new TrashedCase(saved!.Case!, saved.Thrown)),
            ];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <inheritdoc />
    public bool Save(IReadOnlyList<TrashedCase> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        var file = new TrashFile([.. cases.Select(each => new SavedCase(each.CaseNumber, each.ThrownUtc))]);
        lock (_writing)
        {
            try
            {
                SettingsFile.WriteReplacing(FilePath, JsonSerializer.Serialize(file, Json));
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>The file as it is written.</summary>
    /// <param name="Cases">The thrown cases.</param>
    private sealed record TrashFile(List<SavedCase?>? Cases);

    /// <summary>A thrown case as it is written.</summary>
    /// <param name="Case">The case number.</param>
    /// <param name="Thrown">When it was thrown.</param>
    private sealed record SavedCase(string? Case, DateTimeOffset Thrown);
}
