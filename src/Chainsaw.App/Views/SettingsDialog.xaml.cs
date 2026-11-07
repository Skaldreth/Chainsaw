using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Chainsaw.App.Controls;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The settings dialog (sketch ST1a). The view only wires keys, focus and the master key's fields; what happens lives
/// in <see cref="SettingsViewModel"/>.
/// </summary>
public partial class SettingsDialog : UserControl
{
    /// <summary>The field with the current master key, read as characters.</summary>
    private readonly PasswordEntry _current;

    /// <summary>The field with the new master key.</summary>
    private readonly PasswordEntry _next;

    /// <summary>The field with the new master key again.</summary>
    private readonly PasswordEntry _repeat;

    /// <summary>The settings, once attached.</summary>
    private SettingsViewModel? _settings;

    /// <summary>Creates the dialog.</summary>
    public SettingsDialog()
    {
        InitializeComponent();
        _current = new PasswordEntry(CurrentKeyBox);
        _next = new PasswordEntry(NewKeyBox);
        _repeat = new PasswordEntry(RepeatKeyBox);
    }

    /// <summary>Shows the settings and follows when they open and close.</summary>
    /// <param name="settings">The settings' view model.</param>
    public void Attach(SettingsViewModel settings)
    {
        _settings = settings;
        DataContext = settings;
        settings.PropertyChanged += OnSettingsChanged;
        settings.Exits.PropertyChanged += OnExitsChanged;
    }

    /// <summary>
    /// Shows or hides the dialog when it opens or closes, gives it the keyboard, and empties the master key's fields
    /// whenever its form folds in or the dialog closes.
    /// </summary>
    /// <param name="sender">The settings.</param>
    /// <param name="e">The property that changed.</param>
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_settings is null)
        {
            return;
        }

        if (e.PropertyName == nameof(SettingsViewModel.IsOpen))
        {
            Visibility = _settings.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            if (_settings.IsOpen)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, FocusFirst);
            }
        }

        if (e.PropertyName is nameof(SettingsViewModel.IsOpen) or nameof(SettingsViewModel.IsChangingKey)
            && !_settings.IsChangingKey)
        {
            ClearKeys();
        }
    }

    /// <summary>Gives the name of a new exit the keyboard when the step that adds a file opens.</summary>
    /// <param name="sender">The exits.</param>
    /// <param name="e">The property that changed.</param>
    private void OnExitsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ExitPanelViewModel.IsAdding) || _settings is not { IsOpen: true } settings)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (settings.Exits.IsAdding)
            {
                ExitNameBox.Focus();
                ExitNameBox.SelectAll();
            }
            else if (settings.IsOpen && settings.IsVpn)
            {
                AddButton.Focus();
            }
        });
    }

    /// <summary>Moves the keyboard into the dialog, to its first control.</summary>
    private void FocusFirst()
    {
        if (_settings is { IsOpen: true })
        {
            MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
    }

    /// <summary>Empties the master key's fields.</summary>
    private void ClearKeys()
    {
        CurrentKeyBox.Clear();
        NewKeyBox.Clear();
        RepeatKeyBox.Clear();
    }

    /// <summary>
    /// Esc leaves the step that adds a file or the master key's form first, and else closes the dialog; it never goes
    /// on to the window behind.
    /// </summary>
    /// <param name="sender">The dialog.</param>
    /// <param name="e">The key.</param>
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _settings is not { IsOpen: true } settings)
        {
            return;
        }

        if (settings.Exits.IsAdding)
        {
            settings.Exits.CancelAdd();
        }
        else if (settings.IsChangingKey)
        {
            settings.ToggleChangeKey();
        }
        else
        {
            settings.Close();
        }

        e.Handled = true;
    }

    /// <summary>Saves the profile when one of its fields is left.</summary>
    /// <param name="sender">The field.</param>
    /// <param name="e">The change of focus.</param>
    private async void OnProfileLeft(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_settings is not null)
        {
            await _settings.SaveProfileAsync();
        }
    }

    /// <summary>Saves the profile with Enter in one of its fields.</summary>
    /// <param name="sender">The field.</param>
    /// <param name="e">The key.</param>
    private async void OnProfileKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _settings is not null)
        {
            e.Handled = true;
            await _settings.SaveProfileAsync();
        }
    }

    /// <summary>Enter in the last field of the master key's form changes the key.</summary>
    /// <param name="sender">The field.</param>
    /// <param name="e">The key.</param>
    private async void OnKeyFormKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await ChangeKeyAsync();
        }
    }

    /// <summary><em>Byt huvudnyckel</em> changes the key.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private async void OnChangeKey(object sender, RoutedEventArgs e) => await ChangeKeyAsync();

    /// <summary>Gives the three fields to the view model, which takes and clears what is written.</summary>
    /// <returns>A task that completes when the vault has answered.</returns>
    private async Task ChangeKeyAsync()
    {
        if (_settings is not null)
        {
            await _settings.ChangeMasterKeyAsync(_current, _next, _repeat);
        }
    }

    /// <summary>A click on the dark part closes the dialog.</summary>
    /// <param name="sender">The dark part.</param>
    /// <param name="e">The click.</param>
    private void OnDimClicked(object sender, MouseButtonEventArgs e)
    {
        _settings?.Close();
        e.Handled = true;
    }
}
