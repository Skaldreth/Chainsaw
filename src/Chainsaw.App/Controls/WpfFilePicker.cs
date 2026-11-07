using System.Windows;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Security.Contracts;
using Microsoft.Win32;

namespace Chainsaw.App.Controls;

/// <summary>
/// Asks for a file with Windows' own dialog, over the main window. The automatic lock is held while the dialog is
/// open, since what the user does there never reaches the window and a lock would otherwise fall behind the dialog.
/// A file chosen after Chainsaw was locked meanwhile (Ctrl+L, Windows' lock or sleep) is dropped, so that nothing is
/// done behind the lock screen.
/// </summary>
/// <param name="autoLock">The automatic lock, held while the dialog is open.</param>
/// <param name="vault">The vault, which says whether Chainsaw is still unlocked when the dialog closes.</param>
public sealed class WpfFilePicker(AutoLock autoLock, IVault vault) : IFilePicker
{
    /// <summary>The dialog's filter: the SDN list is XML, and any file can be tried.</summary>
    private const string SanctionListFilter = "Sanktionslista (*.xml)|*.xml|Alla filer (*.*)|*.*";

    /// <inheritdoc />
    public string? PickSanctionList()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importera sanktionslista",
            Filter = SanctionListFilter,
            CheckFileExists = true,
            Multiselect = false,
        };
        using (autoLock.Hold())
        {
            var chosen = dialog.ShowDialog(Application.Current?.MainWindow) == true;
            return chosen && vault.State == VaultState.Unlocked ? dialog.FileName : null;
        }
    }
}
