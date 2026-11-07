using System.Windows;
using Chainsaw.App.ViewModels;
using Microsoft.Win32;

namespace Chainsaw.App.Controls;

/// <summary>Asks for a file with Windows' own dialog, over the main window.</summary>
public sealed class WpfFilePicker : IFilePicker
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
        return dialog.ShowDialog(Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }
}
