using System.Windows;
using System.Windows.Input;
using Chainsaw.App.Controls;
using Chainsaw.App.Navigation;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App;

/// <summary>
/// The main window (sketch SH1b): the title bar, the rail, the page and the command bar. The window only wires keys and
/// window buttons; what happens lives in <see cref="ShellViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>The space a maximised window with its own chrome reaches past the screen's edge.</summary>
    private static readonly Thickness MaximizedInset = new(7);

    private readonly ShellViewModel _shell;

    /// <summary>Creates the window with the shell as its data and its shortcuts from <see cref="ViewTable"/>.</summary>
    /// <param name="shell">The shell's view model.</param>
    public MainWindow(ShellViewModel shell)
    {
        InitializeComponent();
        _shell = shell;
        DataContext = shell;
        BindViewKeys();
        BindCommandKeys();
        StateChanged += (_, _) => Frame.Margin = WindowState == WindowState.Maximized ? MaximizedInset : default;
    }

    /// <summary>Gives every view in the table its shortcut, so that the rail and the keys never disagree.</summary>
    private void BindViewKeys()
    {
        foreach (var entry in ViewTable.All)
        {
            if (Shortcuts.TryParse(entry.Gesture) is { } gesture)
            {
                InputBindings.Add(new KeyBinding(_shell.ShowCommand, gesture) { CommandParameter = entry.View });
            }
        }
    }

    /// <summary>Ctrl+K and Ctrl+F put the cursor in the command bar, where search and commands are.</summary>
    private void BindCommandKeys()
    {
        var focus = new RoutedCommand();
        CommandBindings.Add(new CommandBinding(focus, (_, _) => FocusCommandBar()));
        foreach (var text in new[] { ViewTable.CommandGesture, ViewTable.SearchGesture })
        {
            if (Shortcuts.TryParse(text) is { } gesture)
            {
                InputBindings.Add(new KeyBinding(focus, gesture));
            }
        }
    }

    /// <summary>Moves the keyboard to the command bar and selects what is written there.</summary>
    private void FocusCommandBar()
    {
        CommandBox.Focus();
        CommandBox.SelectAll();
    }

    /// <summary>Minimises the window.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>Maximises the window, or restores it when it already is.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>Closes the window, and with it Chainsaw.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
