using System.Windows.Input;
using Chainsaw.App.Controls;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App;

/// <summary>The main window's part for the case's envelopes (sketch ÄR1b).</summary>
public partial class MainWindow
{
    /// <summary>The shortcut that shows the open case's properties.</summary>
    private const string CasePropertiesGesture = "Ctrl+I";

    /// <summary>
    /// Wires the case's dialogs: the case picker's <em>Egenskaper</em> and <em>Öppna en kollegas fil …</em>, Ctrl+I,
    /// the case picker folded in when the cases ask for a dialog, and every dialog closed when Chainsaw locks.
    /// </summary>
    /// <param name="dialogs">The dialogs.</param>
    /// <param name="toaster">The toaster, which says why a chosen file could not be opened.</param>
    private void WireCaseDialogs(CaseDialogsViewModel dialogs, Toaster toaster)
    {
        CaseDialogsView.Attach(dialogs);
        dialogs.Shown += (_, _) => _shell.CasePicker?.Close();
        if (_shell.CasePicker is { } picker)
        {
            picker.PropertiesWanted += (_, _) => _ = dialogs.ShowPropertiesAsync();
            picker.OpenFileWanted += async (_, _) =>
            {
                var problem = await dialogs.PickAndOpenFileAsync();
                if (problem.Length > 0)
                {
                    toaster.Show(ToastKind.Error, "Filen öppnades inte", problem);
                }
            };
        }

        if (Shortcuts.TryParse(CasePropertiesGesture) is { } gesture)
        {
            var show = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(
                show,
                (_, _) => _ = dialogs.ShowPropertiesAsync(),
                (_, e) => e.CanExecute = _gate.IsShellVisible));
            InputBindings.Add(new KeyBinding(show, gesture));
        }

        _gate.PropertyChanged += (_, _) =>
        {
            // Nothing stands over the lock screen.
            if (!_gate.IsShellVisible)
            {
                dialogs.CloseAll();
            }
        };
    }
}
