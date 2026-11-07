using System.Globalization;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.SharedKernel.Commands;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// Importera sanktionslista … in the menu Nytt: the user picks a saved SDN list and Attribution keeps it with its
/// hash and dates (etapp 8). Nothing is fetched: the list is read from the file the user chose.
/// </summary>
public sealed class SanctionListImport
{
    /// <summary>What the bar says when the import failed for a reason Attribution did not name.</summary>
    public const string FailedText = "Sanktionslistan gick inte att importera.";

    /// <summary>The imported sanction lists.</summary>
    private readonly ISanctionLists _lists;

    /// <summary>The dialog that asks for the file.</summary>
    private readonly IFilePicker _picker;

    /// <summary>Creates the import.</summary>
    /// <param name="lists">The imported sanction lists, in Attribution.</param>
    /// <param name="picker">The dialog that asks for the file.</param>
    public SanctionListImport(ISanctionLists lists, IFilePicker picker)
    {
        _lists = lists;
        _picker = picker;
    }

    /// <summary>
    /// Asks for the file on the UI thread, then reads and imports it off the UI thread, since a list can be large.
    /// </summary>
    /// <returns>
    /// What the import found, as the answer above the command bar, or <see langword="null"/> when the user cancelled.
    /// It never throws: every failure is an answer.
    /// </returns>
    public async Task<CommandReply?> ImportAsync()
    {
        var path = _picker.PickSanctionList();
        if (path is null)
        {
            return null;
        }

        try
        {
            var (list, known) = await Task.Run(() => Import(path));
            return CommandReply.Done(Describe(list, known));
        }
        catch (SanctionListException exception)
        {
            return CommandReply.Failed(Explain(exception.Error));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CommandReply.Failed("Filen gick inte att läsa: " + Path.GetFileName(path));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return CommandReply.Failed(FailedText);
        }
    }

    /// <summary>Says what an imported list holds: its name, publish date and addresses.</summary>
    /// <param name="list">The list.</param>
    /// <param name="known">Whether Chainsaw already had the same file, so that nothing new was imported.</param>
    /// <returns>
    /// The text, e.g. <c>OFAC SDN publicerad 2026-11-01 är importerad: 412 adresser, 97 för Bitcoin.</c>
    /// </returns>
    public static string Describe(SanctionListInfo list, bool known = false)
    {
        ArgumentNullException.ThrowIfNull(list);
        var total = list.AddressesByCurrency.Values.Sum();
        var bitcoin = list.AddressesByCurrency.GetValueOrDefault(SanctionCurrency.Bitcoin);
        var date = list.PublishDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var state = known ? "fanns redan" : "är importerad";
        var skipped = list.Skipped > 0 ? $" {list.Skipped} ogiltiga adresser hoppades över." : string.Empty;
        return $"{list.Name} publicerad {date} {state}: {total} adresser, {bitcoin} för Bitcoin.{skipped}";
    }

    /// <summary>Says why a file could not be imported.</summary>
    /// <param name="error">The reason.</param>
    /// <returns>The Swedish text shown above the command bar.</returns>
    public static string Explain(SanctionListError error) => error switch
    {
        SanctionListError.NotSdnList => "Filen är inte en SDN-lista i XML.",
        SanctionListError.NoPublishDate =>
            "Listan säger inte när den publicerades, så dess ursprung går inte att ange.",
        SanctionListError.TooLarge => "Filen är för stor för att vara en sanktionslista.",
        SanctionListError.Tampered => "En sparad sanktionslista har inte längre sin hash.",
        _ => "Sanktionslistan gick inte att läsa.",
    };

    /// <summary>Reads the file and hands it to Attribution.</summary>
    /// <param name="path">The file's path.</param>
    /// <returns>What the list holds, and whether Chainsaw already had the same file.</returns>
    private (SanctionListInfo List, bool Known) Import(string path)
    {
        var before = _lists.List().Select(other => other.Sha256).ToHashSet(StringComparer.Ordinal);
        using var stream = File.OpenRead(path);
        var list = _lists.Import(stream);
        return (list, before.Contains(list.Sha256));
    }
}
