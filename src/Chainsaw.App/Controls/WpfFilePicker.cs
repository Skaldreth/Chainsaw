using System.Windows;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Security.Contracts;
using Microsoft.Win32;

namespace Chainsaw.App.Controls;

/// <summary>
/// Asks for a file with Windows' own dialog, over the main window. The automatic lock is held while the dialog is
/// open, since what the user does there never reaches the window and a lock would otherwise fall behind the dialog.
/// A file chosen after Chainsaw was locked meanwhile (Ctrl+L, Windows' lock or sleep) is dropped, so that nothing is
/// done behind the lock screen. No file is added to Windows' recent items, whose names (a case number, a request)
/// would otherwise stay in the user's profile outside the case (principle 4).
/// </summary>
/// <param name="autoLock">The automatic lock, held while the dialog is open.</param>
/// <param name="vault">The vault, which says whether Chainsaw is still unlocked when the dialog closes.</param>
public sealed class WpfFilePicker(AutoLock autoLock, IVault vault) : IFilePicker, ISaveFilePicker, ICaseFilePicker
{
    /// <summary>The dialog's filter for a request: Markdown.</summary>
    private const string RequestFilter = "Markdown (*.md)|*.md";

    /// <inheritdoc />
    public string? PickRequestFile(string suggestedName)
    {
        // The dialog never asks to write over a file: the export takes the next number instead (sketch BG1b).
        var dialog = new SaveFileDialog
        {
            Title = "Exportera begäran",
            Filter = RequestFilter,
            DefaultExt = ".md",
            AddExtension = true,
            FileName = suggestedName,
            OverwritePrompt = false,
            AddToRecent = false,
        };
        using (autoLock.Hold())
        {
            var chosen = dialog.ShowDialog(Application.Current?.MainWindow) == true;
            return chosen && vault.State == VaultState.Unlocked ? dialog.FileName : null;
        }
    }

    /// <summary>The dialog's filter for a sanction list: the SDN list is XML, and any file can be tried.</summary>
    private const string SanctionListFilter =
        "Sanktionslista eller öppen källa (*.xml;*.json)|*.xml;*.json|Alla filer (*.*)|*.*";

    /// <summary>The dialog's filter for a WireGuard file: a <c>.conf</c>, and any file can be tried.</summary>
    private const string WireGuardFilter = "WireGuard-konfiguration (*.conf)|*.conf|Alla filer (*.*)|*.*";

    /// <inheritdoc />
    public string? PickSanctionList() => Pick("Importera sanktionslista", SanctionListFilter);

    /// <inheritdoc />
    public string? PickWireGuardFile() => Pick("Lägg till WireGuard-fil", WireGuardFilter);

    /// <summary>The dialog's filter for a case file.</summary>
    private const string CaseFileFilter = "Ärende (*.cut)|*.cut";

    /// <inheritdoc />
    public string? PickCaseFile() => Pick("Öppna en kollegas fil", CaseFileFilter);

    /// <summary>Shows Windows' dialog with the lock held, and drops a file chosen after Chainsaw was locked.</summary>
    /// <param name="title">The dialog's title.</param>
    /// <param name="filter">The dialog's filter.</param>
    /// <returns>The file's path, or <see langword="null"/>.</returns>
    private string? Pick(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
            AddToRecent = false,
        };
        using (autoLock.Hold())
        {
            var chosen = dialog.ShowDialog(Application.Current?.MainWindow) == true;
            return chosen && vault.State == VaultState.Unlocked ? dialog.FileName : null;
        }
    }
}
