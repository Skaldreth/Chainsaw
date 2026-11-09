using System.Text.Json;

namespace Chainsaw.App.Settings;

/// <summary>A pile of cases the user made on the start page (sketch GR8a).</summary>
/// <param name="Id">The pile's own id, which never changes when it is named or filled.</param>
/// <param name="Name">The name the user wrote in it; empty while it has none.</param>
/// <param name="CaseNumbers">The cases in it, the one put there last first.</param>
public sealed record CasePile(string Id, string Name, IReadOnlyList<string> CaseNumbers);

/// <summary>Keeps the start page's piles on this computer, never in the cases (sketch GR8a).</summary>
public interface ICasePileStore
{
    /// <summary>Reads the piles.</summary>
    /// <returns>The piles, or none when there are none or they cannot be read.</returns>
    IReadOnlyList<CasePile> Load();

    /// <summary>Writes the piles in place of those saved before.</summary>
    /// <param name="piles">The piles.</param>
    /// <returns><see langword="true"/> when they were written.</returns>
    bool Save(IReadOnlyList<CasePile> piles);
}

/// <summary>
/// Keeps the start page's piles as <c>piles.json</c> in Chainsaw's folder under the user's local application data,
/// next to <c>settings.json</c> and written the same way (<see cref="SettingsFile"/>). It holds only what the start
/// page already shows unlocked, case numbers, and the names the user gave the piles; nothing from inside a case. A
/// missing or unreadable file gives no piles, so that a damaged file never stops the start page.
/// </summary>
/// <param name="directory">The folder; by default <c>%LOCALAPPDATA%\Chainsaw</c>.</param>
public sealed class CasePileStore(string directory) : ICasePileStore
{
    /// <summary>The file's name.</summary>
    public const string FileName = "piles.json";

    /// <summary>How the file is written: indented, so that it can be read by hand.</summary>
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Lets one write at a time, so that two quick changes never meet in the temporary file.</summary>
    private readonly Lock _writing = new();

    /// <summary>Creates the store for the default folder.</summary>
    public CasePileStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chainsaw"))
    {
    }

    /// <summary>Gets the path of the file.</summary>
    public string FilePath => Path.Combine(directory, FileName);

    /// <inheritdoc />
    public IReadOnlyList<CasePile> Load() =>
        [.. (SettingsFile.ReadJson<PileFile>(FilePath, Json)?.Piles ?? []).Where(IsWhole).Select(ToPile)];

    /// <inheritdoc />
    public bool Save(IReadOnlyList<CasePile> piles)
    {
        ArgumentNullException.ThrowIfNull(piles);
        var file = new PileFile([.. piles.Select(pile => new SavedPile(pile.Id, pile.Name, [.. pile.CaseNumbers]))]);
        lock (_writing)
        {
            return SettingsFile.TryWriteJson(FilePath, file, Json);
        }
    }

    /// <summary>Whether a pile read has an id and cases, so that a hand-edited file cannot break it.</summary>
    /// <param name="saved">The pile as read.</param>
    /// <returns><see langword="true"/> when it can be shown.</returns>
    private static bool IsWhole(SavedPile? saved) =>
        saved is { Id.Length: > 0, Cases: not null } && saved.Cases.Any(number => !string.IsNullOrWhiteSpace(number));

    /// <summary>Turns a pile read from the file into a pile, without empty case numbers.</summary>
    /// <param name="saved">The pile as read.</param>
    /// <returns>The pile.</returns>
    private static CasePile ToPile(SavedPile saved) => new(
        saved.Id, saved.Name ?? string.Empty, [.. saved.Cases!.Where(number => !string.IsNullOrWhiteSpace(number))]);

    /// <summary>The file as it is written.</summary>
    /// <param name="Piles">The piles.</param>
    private sealed record PileFile(List<SavedPile>? Piles);

    /// <summary>A pile as it is written.</summary>
    /// <param name="Id">The pile's id.</param>
    /// <param name="Name">Its name.</param>
    /// <param name="Cases">The case numbers in it.</param>
    private sealed record SavedPile(string Id, string? Name, List<string>? Cases);
}
