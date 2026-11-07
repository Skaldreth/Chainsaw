using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Chainsaw.App.Controls;
using Chainsaw.App.ViewModels;
using Microsoft.Win32;

namespace Chainsaw.App.Views;

/// <summary>
/// The gate's page (sketches ID7 and FS1a): the field with the chain, and the lock screen's form or the first-run
/// setup's. The view only wires the fields and the keyboard; what happens lives in <see cref="GateViewModel"/>. The
/// master key is taken from the password boxes as characters (<see cref="PasswordEntry"/>) and never read as text.
/// </summary>
public partial class GateView : UserControl
{
    /// <summary>The lock screen's master key field, as the view model takes it.</summary>
    private readonly PasswordEntry _lockKey;

    /// <summary>The setup's master key field.</summary>
    private readonly PasswordEntry _firstKey;

    /// <summary>The setup's repeat field.</summary>
    private readonly PasswordEntry _repeatKey;

    /// <summary>The gate, once it is given.</summary>
    private GateViewModel? _gate;

    /// <summary>Creates the page.</summary>
    public GateView()
    {
        InitializeComponent();
        _lockKey = new PasswordEntry(LockKey);
        _firstKey = new PasswordEntry(FirstKey);
        _repeatKey = new PasswordEntry(RepeatKey);
    }

    /// <summary>Gives the page its gate, and follows what the gate shows.</summary>
    /// <param name="gate">The gate.</param>
    public void Attach(GateViewModel gate)
    {
        _gate = gate;
        DataContext = gate;
        gate.PropertyChanged += OnGateChanged;
        gate.FirstRun.PropertyChanged += OnFirstRunChanged;
        ShowMode();
    }

    /// <summary>Follows the gate: the field is quiet around the form shown, and the keyboard goes to its field.</summary>
    /// <param name="sender">The gate.</param>
    /// <param name="e">The property that changed.</param>
    private void OnGateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GateViewModel.Mode))
        {
            ShowMode();
        }
    }

    /// <summary>
    /// Follows the setup's step: the master key fields are emptied as soon as their step is left, so that a typed key
    /// never waits in a hidden field, and the keyboard goes to the new step's first field.
    /// </summary>
    /// <param name="sender">The setup.</param>
    /// <param name="e">The property that changed.</param>
    private void OnFirstRunChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FirstRunViewModel.Step) || _gate is null)
        {
            return;
        }

        if (!_gate.FirstRun.IsMasterKey)
        {
            FirstKey.Clear();
            RepeatKey.Clear();
        }

        FocusLater(FirstFieldOfStep());
    }

    /// <summary>Points the field at the form shown, empties hidden secret fields and moves the keyboard.</summary>
    private void ShowMode()
    {
        if (_gate is null)
        {
            return;
        }

        AboutPopup.IsOpen = false;
        switch (_gate.Mode)
        {
            case GateMode.Locked:
                FirstKey.Clear();
                RepeatKey.Clear();
                Field.Form = LockForm;
                FocusLater(LockKey);
                break;
            case GateMode.FirstRun:
                Field.Form = FirstRunForm;
                FocusLater(FirstFieldOfStep());
                break;
            default:
                LockKey.Clear();
                FirstKey.Clear();
                RepeatKey.Clear();
                break;
        }
    }

    /// <summary>Gives the field the keyboard should go to on the setup's step.</summary>
    /// <returns>The field, or the button to add an exit on the exit step.</returns>
    private IInputElement FirstFieldOfStep() => _gate?.FirstRun.Step switch
    {
        FirstRunStep.MasterKey => FirstKey,
        FirstRunStep.Exit => AddExitButton,
        FirstRunStep.FirstCase => CaseNumberBox,
        _ => NameBox,
    };

    /// <summary>Moves the keyboard to an element once it has been laid out and shown.</summary>
    /// <param name="element">The element.</param>
    private void FocusLater(IInputElement element) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(element));

    /// <summary>Opens the vault with the lock screen's field; on failure the keyboard returns to the field.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private async void OnUnlock(object sender, RoutedEventArgs e) => await UnlockAsync();

    /// <summary>Enter in the lock screen's field opens the vault.</summary>
    /// <param name="sender">The field.</param>
    /// <param name="e">The key.</param>
    private async void OnLockKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await UnlockAsync();
        }
    }

    /// <summary>Gives the lock screen's field to the vault.</summary>
    /// <returns>A task that completes when the vault has answered.</returns>
    private async Task UnlockAsync()
    {
        if (_gate is not null && !await _gate.LockScreen.UnlockAsync(_lockKey))
        {
            LockKey.Focus();
        }
    }

    /// <summary>Goes on from the setup's step.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private async void OnNext(object sender, RoutedEventArgs e) => await NextAsync();

    /// <summary>Enter in a field of the setup goes on, as the main button; a focused button clicks itself.</summary>
    /// <param name="sender">The form.</param>
    /// <param name="e">The key.</param>
    private async void OnFirstRunKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.OriginalSource is not ButtonBase)
        {
            e.Handled = true;
            await NextAsync();
        }
    }

    /// <summary>Goes on from the setup's step with the master key fields.</summary>
    /// <returns>A task that completes when the step is done.</returns>
    private async Task NextAsync()
    {
        if (_gate is not null)
        {
            await _gate.FirstRun.NextAsync(_firstKey, _repeatKey);
        }
    }

    /// <summary>Goes back one step.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnBack(object sender, RoutedEventArgs e) => _gate?.FirstRun.Back();

    /// <summary>Leaves the exit for later.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnLater(object sender, RoutedEventArgs e) => _gate?.FirstRun.Later();

    /// <summary>Skips the first case.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnSkip(object sender, RoutedEventArgs e) => _gate?.FirstRun.Skip();

    /// <summary>Lets the user choose a WireGuard file and stores it as the exit.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private async void OnAddExit(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Lägg till en WireGuard-fil",
            Filter = "WireGuard-konfiguration (*.conf)|*.conf|Alla filer (*.*)|*.*",
            CheckFileExists = true,
        };
        if (_gate is not null && dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            await _gate.FirstRun.AddExitAsync(dialog.FileName);
        }
    }

    /// <summary>Shows or hides what *Om Chainsaw* tells.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnAbout(object sender, RoutedEventArgs e) => AboutPopup.IsOpen = !AboutPopup.IsOpen;

    /// <summary>Minimises the window.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(Window.GetWindow(this));

    /// <summary>Maximises the window, or restores it when it already is.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window.WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(window);
        }
        else
        {
            SystemCommands.MaximizeWindow(window);
        }
    }

    /// <summary>Closes the window, and with it Chainsaw.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnClose(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(Window.GetWindow(this));
}
