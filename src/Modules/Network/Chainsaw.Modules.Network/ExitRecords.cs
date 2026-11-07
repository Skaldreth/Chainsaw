using System.Text.Json;
using System.Text.Json.Serialization;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>
/// The exits and the selected exit as named secrets in the vault. Each exit is one secret named after its id, whose
/// value holds the name, the place, the sort and the configuration; nothing about an exit is kept in plain text.
/// </summary>
/// <param name="secrets">The vault's named secrets.</param>
internal sealed class ExitRecords(IVaultSecrets secrets)
{
    /// <summary>The prefix of the secrets that hold the exits; the id follows in the <c>N</c> format.</summary>
    internal const string ExitPrefix = "network/exits/";

    /// <summary>The name of the secret that holds the selected exit's id.</summary>
    internal const string SelectedName = "network/selected-exit";

    /// <summary>The format of the id in a secret's name: 32 lower-case hexadecimal digits.</summary>
    private const string IdFormat = "N";

    /// <summary>How an exit is written: the sort as text, so that the stored value reads the same in every version.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Lists the exits, ordered by name.</summary>
    /// <returns>The exits without their configurations.</returns>
    public IReadOnlyList<ExitInfo> List() =>
        secrets.List(ExitPrefix)
            .Select(name => Guid.TryParseExact(name[ExitPrefix.Length..], IdFormat, out var id) ? Find(id) : null)
            .OfType<(ExitInfo Exit, string Configuration)>()
            .Select(found => found.Exit)
            .OrderBy(exit => exit.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(exit => exit.Id)
            .ToList();

    /// <summary>Finds an exit with its configuration.</summary>
    /// <param name="id">The exit.</param>
    /// <returns>The exit and its configuration, or <see langword="null"/> when there is none or it is unreadable.</returns>
    public (ExitInfo Exit, string Configuration)? Find(Guid id)
    {
        var value = secrets.Find(NameOf(id));
        if (value is null)
        {
            return null;
        }

        StoredExit? stored;
        try
        {
            stored = JsonSerializer.Deserialize<StoredExit>(value, JsonOptions);
        }
        catch (JsonException)
        {
            // An unreadable exit is treated as missing; it can never be connected.
            return null;
        }

        if (stored is null || stored.Name is null || stored.Place is null || string.IsNullOrEmpty(stored.Configuration)
            || !Enum.IsDefined(stored.Kind))
        {
            return null;
        }

        return (new ExitInfo(id, stored.Name, stored.Place, stored.Kind), stored.Configuration);
    }

    /// <summary>Stores an exit.</summary>
    /// <param name="id">The exit's id.</param>
    /// <param name="exit">The exit.</param>
    public void Save(Guid id, StoredExit exit) => secrets.Save(NameOf(id), JsonSerializer.Serialize(exit, JsonOptions));

    /// <summary>Removes an exit.</summary>
    /// <param name="id">The exit.</param>
    /// <returns><see langword="true"/> when it was removed.</returns>
    public bool Remove(Guid id) => secrets.Remove(NameOf(id));

    /// <summary>Gets the selected exit's id.</summary>
    /// <returns>The id, or <see langword="null"/> when none is selected.</returns>
    public Guid? SelectedId() =>
        Guid.TryParseExact(secrets.Find(SelectedName), IdFormat, out var id) ? id : null;

    /// <summary>Finds the selected exit with its configuration.</summary>
    /// <returns>The exit, or <see langword="null"/> when none is selected or the selected one is gone.</returns>
    public (ExitInfo Exit, string Configuration)? FindSelected() => SelectedId() is { } id ? Find(id) : null;

    /// <summary>Sets or clears the selection.</summary>
    /// <param name="id">The exit, or <see langword="null"/> for none.</param>
    public void SetSelected(Guid? id)
    {
        if (id is { } selected)
        {
            secrets.Save(SelectedName, selected.ToString(IdFormat));
        }
        else
        {
            secrets.Remove(SelectedName);
        }
    }

    /// <summary>Gives the name of the secret that holds an exit.</summary>
    /// <param name="id">The exit.</param>
    /// <returns>The secret's name.</returns>
    private static string NameOf(Guid id) => ExitPrefix + id.ToString(IdFormat);
}
