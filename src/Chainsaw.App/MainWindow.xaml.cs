using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Chainsaw.App.Controls;
using Chainsaw.App.Navigation;
using Chainsaw.App.ViewModels;
using Microsoft.Win32;

namespace Chainsaw.App;

/// <summary>
/// The main window (sketch SH1b): the title bar, the rail, the page and the command bar, behind the gate (sketches ID7
/// and FS1a) that shows the lock screen or the first-run setup in their place. The window only wires keys, window
/// buttons and Windows' own events; what happens lives in <see cref="ShellViewModel"/> and
/// <see cref="GateViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>The space a maximised window with its own chrome reaches past the screen's edge.</summary>
    private static readonly Thickness MaximizedInset = new(7);

    /// <summary>How often the window asks whether the automatic lock is due.</summary>
    private static readonly TimeSpan IdleCheck = TimeSpan.FromSeconds(5);

    /// <summary>The shell.</summary>
    private readonly ShellViewModel _shell;

    /// <summary>The gate in front of the shell.</summary>
    private readonly GateViewModel _gate;

    /// <summary>Asks the gate now and then whether the automatic lock is due.</summary>
    private readonly DispatcherTimer _idleTimer;

    /// <summary>Creates the window with the shell, the gate, and its shortcuts from <see cref="ViewTable"/>.</summary>
    /// <param name="shell">The shell's view model.</param>
    /// <param name="gate">The gate's view model.</param>
    public MainWindow(ShellViewModel shell, GateViewModel gate)
    {
        InitializeComponent();
        _shell = shell;
        _gate = gate;
        DataContext = shell;
        Gate.Attach(gate);
        gate.PropertyChanged += OnGateChanged;
        ShowGate();

        BindViewKeys();
        BindCommandKeys();
        BindLockKey();
        WatchActivity();
        _idleTimer = new DispatcherTimer(
            IdleCheck, DispatcherPriority.Background, (_, _) => gate.CheckIdle(), Dispatcher);
        _idleTimer.Start();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        StateChanged += (_, _) => Frame.Margin = WindowState == WindowState.Maximized ? MaximizedInset : default;
    }

    /// <summary>Stops listening to Windows when the window closes; the static events would otherwise keep it.</summary>
    /// <param name="e">The arguments.</param>
    protected override void OnClosed(EventArgs e)
    {
        _idleTimer.Stop();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        base.OnClosed(e);
    }

    /// <summary>Shows the shell or the gate as soon as the gate's mode changes.</summary>
    /// <param name="sender">The gate.</param>
    /// <param name="e">The property that changed.</param>
    private void OnGateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GateViewModel.Mode))
        {
            ShowGate();
        }
    }

    /// <summary>
    /// Collapses the shell whenever the gate is shown, so that nothing of it is drawn, reachable with the keyboard or
    /// shown in the taskbar's preview while Chainsaw is locked.
    /// </summary>
    private void ShowGate()
    {
        ShellRoot.Visibility = _gate.IsShellVisible ? Visibility.Visible : Visibility.Collapsed;
        Gate.Visibility = _gate.IsGateVisible ? Visibility.Visible : Visibility.Collapsed;
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Gives every view in the table its shortcut, so that the rail and the keys never disagree.</summary>
    private void BindViewKeys()
    {
        var show = new WhenShown(_shell.ShowCommand, () => _gate.IsShellVisible);
        foreach (var entry in ViewTable.All)
        {
            if (Shortcuts.TryParse(entry.Gesture) is { } gesture)
            {
                InputBindings.Add(new KeyBinding(show, gesture) { CommandParameter = entry.View });
            }
        }
    }

    /// <summary>Ctrl+K and Ctrl+F put the cursor in the command bar, where search and commands are.</summary>
    private void BindCommandKeys()
    {
        var focus = new RoutedCommand();
        CommandBindings.Add(new CommandBinding(
            focus, (_, _) => FocusCommandBar(), (_, e) => e.CanExecute = _gate.IsShellVisible));
        foreach (var text in new[] { ViewTable.CommandGesture, ViewTable.SearchGesture })
        {
            if (Shortcuts.TryParse(text) is { } gesture)
            {
                InputBindings.Add(new KeyBinding(focus, gesture));
            }
        }
    }

    /// <summary>Ctrl+L locks Chainsaw at once.</summary>
    private void BindLockKey()
    {
        if (Shortcuts.TryParse(ViewTable.LockGesture) is { } gesture)
        {
            InputBindings.Add(new KeyBinding(_gate.LockCommand, gesture));
        }
    }

    /// <summary>Every key, click, wheel and move in the window puts off the automatic lock.</summary>
    private void WatchActivity()
    {
        PreviewKeyDown += (_, _) => _gate.Touch();
        PreviewMouseDown += (_, _) => _gate.Touch();
        PreviewMouseWheel += (_, _) => _gate.Touch();
        PreviewMouseMove += (_, _) => _gate.Touch();
    }

    /// <summary>Locks Chainsaw when Windows is locked or the session is disconnected.</summary>
    /// <param name="sender">Windows.</param>
    /// <param name="e">What happened to the session.</param>
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect
            or SessionSwitchReason.RemoteDisconnect)
        {
            LockFromWindows();
        }
    }

    /// <summary>Locks Chainsaw when the computer goes to sleep.</summary>
    /// <param name="sender">Windows.</param>
    /// <param name="e">The change of power mode.</param>
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            LockFromWindows();
        }
    }

    /// <summary>
    /// Locks at once when Windows' event comes on the UI thread (as it normally does), so that the lock is in place
    /// before the computer sleeps; otherwise it is posted there.
    /// </summary>
    private void LockFromWindows()
    {
        if (Dispatcher.CheckAccess())
        {
            _gate.OnWindowsLocked();
        }
        else
        {
            Dispatcher.BeginInvoke(_gate.OnWindowsLocked);
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
