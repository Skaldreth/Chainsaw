using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Chainsaw.App.Controls;
using Chainsaw.App.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.Views;

/// <summary>
/// Nytt ärende (sketch NC1a). The view only wires the fields and the keys; what happens lives in
/// <see cref="NewCaseViewModel"/>. The sharing password is taken from the password boxes as characters
/// (<see cref="PasswordEntry"/>), never read as text, and the boxes are emptied whenever the panel closes or the
/// switch is turned off, so that a password never waits in a hidden field.
/// </summary>
public partial class NewCasePanel : UserControl
{
    /// <summary>The sharing password field, as the view model takes it.</summary>
    private readonly PasswordEntry _sharing;

    /// <summary>The repeat field.</summary>
    private readonly PasswordEntry _repeat;

    /// <summary>The panel's view model, once it is given.</summary>
    private NewCaseViewModel? _panel;

    /// <summary>Creates the panel, hidden.</summary>
    public NewCasePanel()
    {
        InitializeComponent();
        _sharing = new PasswordEntry(SharingBox);
        _repeat = new PasswordEntry(RepeatBox);
        InputBindings.Add(new KeyBinding(new RelayCommand(OnCreateKey), Key.Enter, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(Cancel), Key.Escape, ModifierKeys.None));
    }

    /// <summary>Gives the panel its view model, and follows whether it is shown.</summary>
    /// <param name="panel">The view model.</param>
    public void Attach(NewCaseViewModel panel)
    {
        _panel = panel;
        DataContext = panel;
        panel.PropertyChanged += OnPanelChanged;
    }

    /// <summary>
    /// Shows or hides the panel, and empties the password boxes when it closes or the switch is turned off.
    /// </summary>
    /// <param name="sender">The view model.</param>
    /// <param name="e">The property that changed.</param>
    private void OnPanelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_panel is null)
        {
            return;
        }

        if (e.PropertyName == nameof(NewCaseViewModel.IsOpen))
        {
            Visibility = _panel.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            if (_panel.IsOpen)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => CaseNumberBox.Focus());
            }
            else
            {
                ClearPasswords();
            }
        }
        else if (e.PropertyName == nameof(NewCaseViewModel.IsShared) && !_panel.IsShared)
        {
            ClearPasswords();
        }
    }

    /// <summary>Empties both password boxes.</summary>
    private void ClearPasswords()
    {
        SharingBox.Clear();
        RepeatBox.Clear();
    }

    /// <summary>Creates the case (Skapa ärende).</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private async void OnCreate(object sender, RoutedEventArgs e) => await CreateAsync();

    /// <summary>Creates the case (Ctrl+Enter).</summary>
    private async void OnCreateKey() => await CreateAsync();

    /// <summary>Creates the case through the view model, which takes the passwords only when they are needed.</summary>
    /// <returns>A task that completes when the case is created or the reason is shown.</returns>
    private async Task CreateAsync()
    {
        if (_panel is not null)
        {
            await _panel.CreateAsync(_sharing, _repeat);
        }
    }

    /// <summary>Closes the panel (Avbryt or ×).</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnCancel(object sender, RoutedEventArgs e) => Cancel();

    /// <summary>Closes the panel when the dark part of the window is clicked.</summary>
    /// <param name="sender">The dark layer.</param>
    /// <param name="e">The click.</param>
    private void OnDimClicked(object sender, MouseButtonEventArgs e) => Cancel();

    /// <summary>Closes the panel without creating anything.</summary>
    private void Cancel() => _panel?.Cancel();
}
