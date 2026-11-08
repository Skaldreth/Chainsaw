using System.Globalization;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.SharedKernel.Commands;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// Importera sanktionslista … in the menu Nytt: the user picks a saved list and Attribution keeps it with its hash and
/// dates (etapp 8): a sanction list in XML (OFAC's SDN list or the EU's consolidated list), or an open source in JSON
/// (Ransomwhere), told apart by the file's first character. Nothing is fetched: the list is read from the file the user
/// chose.
/// </summary>
public sealed class SanctionListImport
{
    /// <summary>What the bar says when the import failed for a reason Attribution did not name.</summary>
    public const string FailedText = "Sanktionslistan gick inte att importera.";

    /// <summary>The imported sanction lists.</summary>
    private readonly ISanctionLists _lists;

    /// <summary>The dialog that asks for the file.</summary>
    private readonly IFilePicker _picker;

    /// <summary>The imported open sources, or <see langword="null"/> where none can be imported.</summary>
    private readonly IOpenSourceLists? _openSources;

    /// <summary>Creates the import.</summary>
    /// <param name="lists">The imported sanction lists, in Attribution.</param>
    /// <param name="picker">The dialog that asks for the file.</param>
    /// <param name="openSources">The imported open sources, in Attribution; none where they cannot be imported.</param>
    public SanctionListImport(ISanctionLists lists, IFilePicker picker, IOpenSourceLists? openSources = null)
    {
        _lists = lists;
        _picker = picker;
        _openSources = openSources;
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
            return CommandReply.Done(await Task.Run(() => Import(path)));
        }
        catch (SanctionListException exception)
        {
            return CommandReply.Failed(Explain(exception.Error));
        }
        catch (OpenSourceListException exception)
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

    /// <summary>
    /// Says what an imported open source holds: its name, the date of its data, its addresses and licence.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="known">Whether Chainsaw already had the same file.</param>
    /// <returns>
    /// The text, e.g. <c>Ransomwhere med data till 2024-10-27 är importerad: 11 178 adresser (CC BY 4.0, …).</c>
    /// </returns>
    public static string Describe(OpenSourceListInfo source, bool known = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        var total = source.AddressesByChain.Values.Sum();
        var date = source.DataDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var state = known ? "fanns redan" : "är importerad";
        return $"{source.Name} med data till {date} {state}: {total} adresser ({source.License}). " +
            "En öppen källa, inte en sanktionslista.";
    }

    /// <summary>Says why a file could not be imported as an open source.</summary>
    /// <param name="error">The reason.</param>
    /// <returns>The Swedish text shown above the command bar.</returns>
    public static string Explain(OpenSourceListError error) => error switch
    {
        OpenSourceListError.NotKnown => "Filen är ingen öppen källa som Chainsaw läser (Ransomwhere i JSON).",
        OpenSourceListError.NoDate => "Källan säger inte hur ny datan är, så dess ursprung går inte att ange.",
        OpenSourceListError.TooLarge => "Filen är för stor för att vara en öppen källa.",
        OpenSourceListError.Tampered => "En sparad öppen källa har inte längre sin hash.",
        _ => "Den öppna källan gick inte att läsa.",
    };

    /// <summary>Says why a file could not be imported.</summary>
    /// <param name="error">The reason.</param>
    /// <returns>The Swedish text shown above the command bar.</returns>
    public static string Explain(SanctionListError error) => error switch
    {
        SanctionListError.NotSdnList =>
            "Filen är ingen sanktionslista som Chainsaw läser (OFAC:s SDN-lista eller EU:s lista i XML).",
        SanctionListError.NoPublishDate =>
            "Listan säger inte när den publicerades, så dess ursprung går inte att ange.",
        SanctionListError.TooLarge => "Filen är för stor för att vara en sanktionslista.",
        SanctionListError.Tampered => "En sparad sanktionslista har inte längre sin hash.",
        SanctionListError.FutureDate => "Listan säger att den publicerades efter i dag, så den importerades inte.",
        _ => "Sanktionslistan gick inte att läsa.",
    };

    /// <summary>Reads the file and hands it to Attribution, as a sanction list or as an open source.</summary>
    /// <param name="path">The file's path.</param>
    /// <returns>What the list holds, said for the bar.</returns>
    private string Import(string path)
    {
        using var stream = File.OpenRead(path);
        if (_openSources is { } openSources && IsJson(stream))
        {
            var known = openSources.List().Select(other => other.Sha256).ToHashSet(StringComparer.Ordinal);
            var source = openSources.Import(stream);
            return Describe(source, known.Contains(source.Sha256));
        }

        var before = _lists.List().Select(other => other.Sha256).ToHashSet(StringComparer.Ordinal);
        var list = _lists.Import(stream);
        return Describe(list, before.Contains(list.Sha256));
    }

    /// <summary>
    /// Says whether a file is JSON: its first character after white space and a byte order mark is a bracket. The
    /// stream is put back at its start.
    /// </summary>
    /// <param name="stream">The file, at its start.</param>
    /// <returns><see langword="true"/> when it starts with <c>[</c> or <c>{</c>.</returns>
    private static bool IsJson(Stream stream)
    {
        try
        {
            int read;
            while ((read = stream.ReadByte()) >= 0)
            {
                if (read is ' ' or '\t' or '\r' or '\n' or 0xEF or 0xBB or 0xBF)
                {
                    continue;
                }

                return read is '[' or '{';
            }

            return false;
        }
        finally
        {
            stream.Position = 0;
        }
    }
}
