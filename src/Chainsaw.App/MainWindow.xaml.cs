using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Chainsaw.App.Controls;
using Chainsaw.App.Interop;
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

    /// <summary>
    /// The icon whose submenu was open when the window was left, so that its button gets the keyboard back when the
    /// window is active again; <see langword="null"/> otherwise.
    /// </summary>
    private RailItemViewModel? _leftWithSubmenu;

    /// <summary>The Guide's panel beside the work (sketch GD1b).</summary>
    private readonly GuidePanelViewModel _guidePanel;

    /// <summary>The graph, whose inspector's open form takes Esc before the Guide's panel (sketch IN1a).</summary>
    private readonly GraphViewModel _graph;

    /// <summary>Asks the gate now and then whether the automatic lock is due.</summary>
    private readonly DispatcherTimer _idleTimer;

    /// <summary>Creates the window with the shell, the gate, and its shortcuts from <see cref="ViewTable"/>.</summary>
    /// <param name="shell">The shell's view model.</param>
    /// <param name="gate">The gate's view model.</param>
    /// <param name="graph">The graph's view model (sketch GR4).</param>
    /// <param name="guidePanel">The Guide's panel (sketch GD1b).</param>
    /// <param name="guide">The view Guide (F1).</param>
    /// <param name="monitoring">The view Bevakning (sketch BV1a).</param>
    /// <param name="watchRunner">The watches' checks while Chainsaw is open and unlocked, started here.</param>
    /// <param name="background">
    /// The background watching: the plan, the hold on the open case and its state, started here.
    /// </param>
    public MainWindow(
        ShellViewModel shell,
        GateViewModel gate,
        GraphViewModel graph,
        GuidePanelViewModel guidePanel,
        GuideViewModel guide,
        MonitoringViewModel monitoring,
        WatchRunner watchRunner,
        BackgroundLink background)
    {
        InitializeComponent();
        _shell = shell;
        _gate = gate;
        _guidePanel = guidePanel;
        _graph = graph;
        DataContext = shell;
        Gate.Attach(gate);
        NewCasePanel.Attach(shell.NewCase);
        ExitPanelView.Attach(shell.ExitPanel);
        SettingsDialog.Attach(shell.Settings);
        GraphPage.Attach(graph);
        GuidePage.Attach(guide);
        GuidePanel.Attach(guidePanel);
        WireGuide(graph, guide);
        WireWatches(shell, graph, monitoring);
        watchRunner.Start();
        shell.Settings.SettingsChanged += (_, settings) => _ = background.ApplyAsync(settings);
        background.Start();
        gate.PropertyChanged += OnGateChanged;
        ShowGate();

        BindViewKeys();
        BindCommandKeys();
        BindLockKey();
        WatchSubmenus();
        WatchExitPanel();
        WatchActivity();
        _idleTimer = new DispatcherTimer(
            IdleCheck, DispatcherPriority.Background, (_, _) => gate.CheckIdle(), Dispatcher);
        _idleTimer.Start();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SourceInitialized += (_, _) => WindowFrame.ApplyRoundedDarkFrame(new WindowInteropHelper(this).Handle);
        StateChanged += (_, _) => Frame.Margin = WindowState == WindowState.Maximized ? MaximizedInset : default;
    }

    /// <summary>
    /// Wires the watches (sketches BV1a and BV2a): the graph and the view Bevakning share the form and the watches, the
    /// view names counterparties by the graph's nodes, <em>Öppna i grafen</em> shows the graph, and a hit is told in
    /// the graph's notice.
    /// </summary>
    /// <param name="shell">The shell.</param>
    /// <param name="graph">The graph.</param>
    /// <param name="monitoring">The view Bevakning.</param>
    private void WireWatches(ShellViewModel shell, GraphViewModel graph, MonitoringViewModel monitoring)
    {
        MonitoringPage.Attach(monitoring);
        graph.FollowWatches(monitoring.Board, monitoring.Form);
        monitoring.Names = graph.NameIn;
        monitoring.OpenInGraphWanted += (_, watch) =>
        {
            shell.Show(AppView.Graph);
            graph.ShowWatch(watch);
        };
        monitoring.Board.HitsFound += (_, hits) => graph.TellHits(hits);
    }

    /// <summary>
    /// Wires the Guide (sketch GD1b): the graph's <em>Varför?</em>, <em>?</em> and method open the panel, the panel
    /// follows what is chosen, <em>Öppna i Guide</em> shows the view Guide with a way back, and the panel closes when
    /// another view is shown. Esc closes the panel before it reaches the graph.
    /// </summary>
    /// <param name="graph">The graph's view model.</param>
    /// <param name="guide">The view Guide's view model.</param>
    private void WireGuide(GraphViewModel graph, GuideViewModel guide)
    {
        graph.GuideWanted += (_, request) => _guidePanel.Ask(request);
        graph.WhyChanged += (_, request) => _guidePanel.Follow(request);
        graph.CaseChanged += (_, _) => _guidePanel.Close();
        _guidePanel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GuidePanelViewModel.IsOpen))
            {
                MakeRoomForGuide();
            }
        };
        _guidePanel.FullGuideWanted += (_, place) =>
        {
            guide.Open(place, _shell.Current);
            _shell.Show(AppView.Guide);
        };
        guide.ReturnWanted += (_, view) => _shell.Show(view);
        _shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ShellViewModel.Current))
            {
                return;
            }

            if (!_shell.IsGraphShown)
            {
                _guidePanel.Close();
            }

            if (!_shell.IsGuideShown)
            {
                guide.Leave();
            }
        };
        PreviewKeyDown += OnGuideEscape;
    }

    /// <summary>
    /// The keys of the open Guide panel, wherever the keyboard is: Esc closes it, and F1 takes its place to the view
    /// Guide (as <em>Öppna i Guide</em>). The command bar, Nytt ärende, the settings, an open submenu, the exits' panel
    /// and an open form in the graph's inspector keep their own Esc.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The key.</param>
    private void OnGuideEscape(object sender, KeyEventArgs e)
    {
        if (!_guidePanel.IsOpen || !_gate.IsShellVisible || _shell.NewCase.IsOpen || _shell.Settings.IsOpen)
        {
            return;
        }

        if (e.Key == Key.F1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            _guidePanel.OpenInGuide();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape || _shell.OpenSubmenu is not null || _shell.ExitPanel.IsOpen
            || CommandBox.IsKeyboardFocusWithin || _graph.Forms.IsOpen || _graph.History.IsOpen)
        {
            return;
        }

        _guidePanel.Close();
        e.Handled = true;
    }

    /// <summary>
    /// Keeps what the Guide's panel would cover in view while it is open: the graph's corner moves left of it, and
    /// the command bar is centred in the space beside it.
    /// </summary>
    private void MakeRoomForGuide()
    {
        var covered = _guidePanel.IsOpen ? GuidePanel.Width : 0;
        GraphPage.MakeRoom(covered);
        CommandArea.Margin = new Thickness(0, 0, covered, 20);
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
        if (!_gate.IsShellVisible)
        {
            _shell.DismissAll();
        }

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

    /// <summary>
    /// Follows the pill's submenus (sketch PN1): the open one is shown over its button, the keyboard goes back to the
    /// button when a menu closes by Esc, a click outside or a choice, a click outside closes it, and so does leaving
    /// the window, whose return gives the keyboard back to the button. The menu follows its button when the window
    /// changes size. An action that wants the command bar gets it.
    /// </summary>
    private void WatchSubmenus()
    {
        _shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.OpenSubmenu))
            {
                ShowSubmenu();
            }
        };
        foreach (var item in _shell.Items)
        {
            if (item.Submenu is { } menu)
            {
                menu.Closed += (_, returnFocus) =>
                {
                    if (returnFocus)
                    {
                        ButtonOf(item)?.Focus();
                    }
                };
            }
        }

        _shell.CommandBarWanted += (_, _) => FocusCommandBar();
        PreviewMouseDown += OnPreviewMouseDown;
        SubmenuLayer.SizeChanged += (_, _) => ShowSubmenu();
        Deactivated += (_, _) =>
        {
            _leftWithSubmenu = _shell.OpenSubmenu;
            _shell.DismissSubmenus();
        };
        Activated += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, ReturnFromElsewhere);
    }

    /// <summary>
    /// Gives the keyboard back to the button whose menu was open when the window was left, unless something else in
    /// the window already has it: Windows would give it back to the closed menu, where it would be lost, or to the
    /// window itself.
    /// </summary>
    private void ReturnFromElsewhere()
    {
        var item = _leftWithSubmenu;
        _leftWithSubmenu = null;
        if (item is not null && _gate.IsShellVisible
            && (Keyboard.FocusedElement is not UIElement { IsVisible: true } focused
                || ReferenceEquals(focused, this) || ReferenceEquals(focused, Submenu)))
        {
            ButtonOf(item)?.Focus();
        }
    }

    /// <summary>Shows the open submenu with its tab over its button, or hides it when none is open.</summary>
    private void ShowSubmenu()
    {
        var item = _shell.OpenSubmenu;
        var button = item is null ? null : ButtonOf(item);
        if (item is null || button is null)
        {
            Submenu.Show(null, default);
            return;
        }

        Submenu.Show(item, button.TranslatePoint(default, SubmenuLayer));
    }

    /// <summary>Tells whether an element is a part, or is itself, of another.</summary>
    /// <param name="target">The element pressed; one not drawn (a run of text) counts as outside.</param>
    /// <param name="part">The other element.</param>
    /// <returns><see langword="true"/> when it is within.</returns>
    private static bool IsWithin(DependencyObject? target, Visual part) =>
        target is Visual visual && (visual == part || part.IsAncestorOf(visual));

    /// <summary>
    /// Closes an open submenu, or the exits' panel, when the pointer presses outside it, before the press reaches its
    /// target.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The press.</param>
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var target = e.OriginalSource as DependencyObject;
        if (_shell.OpenSubmenu?.Submenu is { } menu && !IsWithin(target, Submenu))
        {
            menu.Close();
        }

        // The title bar lies outside the space that catches clicks around the exits' panel.
        if (_shell.ExitPanel.IsOpen && !IsWithin(target, ExitPanelView) && !IsWithin(target, ExitChip))
        {
            _shell.ExitPanel.Close();
        }
    }

    /// <summary>Finds the pill's button of an icon.</summary>
    /// <param name="item">The icon.</param>
    /// <returns>The button, or <see langword="null"/> when it is not shown.</returns>
    private Button? ButtonOf(RailItemViewModel item) => FindButton(ShellRoot, item);

    /// <summary>Looks through the visual tree for the button whose data is the icon.</summary>
    /// <param name="parent">Where to look.</param>
    /// <param name="item">The icon.</param>
    /// <returns>The button, or <see langword="null"/>.</returns>
    private static Button? FindButton(DependencyObject parent, RailItemViewModel item)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button { DataContext: var data } button && ReferenceEquals(data, item))
            {
                return button;
            }

            if (FindButton(child, item) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Follows the exits' panel (sketch UT1a): the keyboard goes into it when it folds out, and back to the exit in the
    /// title bar when it folds in while the keyboard was in it.
    /// </summary>
    private void WatchExitPanel()
    {
        _shell.ExitPanel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ExitPanelViewModel.IsOpen))
            {
                return;
            }

            if (_shell.ExitPanel.IsOpen)
            {
                ExitPanelView.TakeFocus();
            }
            else if (ExitPanelView.IsKeyboardFocusWithin && _gate.IsShellVisible)
            {
                ExitChip.Focus();
            }
        };
    }

    /// <summary>A click under the title bar outside the exits' panel folds it in, and goes no further.</summary>
    /// <param name="sender">The space around the panel.</param>
    /// <param name="e">The click.</param>
    private void OnExitPanelOutside(object sender, MouseButtonEventArgs e)
    {
        _shell.ExitPanel.Close();
        e.Handled = true;
    }

    /// <summary>Every key, click, wheel and move in the window puts off the automatic lock.</summary>
    private void WatchActivity()
    {
        PreviewKeyDown += (_, _) => _gate.Touch();
        PreviewMouseDown += (_, _) => _gate.Touch();
        PreviewMouseWheel += (_, _) => _gate.Touch();
        PreviewMouseMove += (_, _) => _gate.Touch();
    }

    /// <summary>
    /// Locks Chainsaw when the session is disconnected, and when Windows is locked unless the settings say otherwise.
    /// </summary>
    /// <param name="sender">Windows.</param>
    /// <param name="e">What happened to the session.</param>
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock)
        {
            LockFromWindows(_gate.OnWindowsSessionLocked);
        }
        else if (e.Reason is SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        {
            LockFromWindows(_gate.OnWindowsLocked);
        }
    }

    /// <summary>Locks Chainsaw when the computer goes to sleep.</summary>
    /// <param name="sender">Windows.</param>
    /// <param name="e">The change of power mode.</param>
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            LockFromWindows(_gate.OnWindowsLocked);
        }
    }

    /// <summary>
    /// Locks at once when Windows' event comes on the UI thread (as it normally does), so that the lock is in place
    /// before the computer sleeps; otherwise it is posted there.
    /// </summary>
    /// <param name="lockNow">The gate's way to lock for this event.</param>
    private void LockFromWindows(Action lockNow)
    {
        if (Dispatcher.CheckAccess())
        {
            lockNow();
        }
        else
        {
            Dispatcher.BeginInvoke(lockNow);
        }
    }

    /// <summary>
    /// Brings the window forward when another start of Chainsaw sent a link (or only asked to be shown); restores it
    /// when it is minimised. What is shown stays behind the gate.
    /// </summary>
    public void BringForward()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        if (_gate.IsShellVisible && !_shell.NewCase.IsOpen && !_shell.Settings.IsOpen && _shell.CommandText.Length > 0)
        {
            FocusCommandBar();
        }
    }

    /// <summary>
    /// Tab in the command bar fills in (a command's name, or a case number for <c>/open</c>) instead of moving the
    /// keyboard on, and leaves the caret at the end of the text.
    /// </summary>
    /// <param name="sender">The command bar's text box.</param>
    /// <param name="e">The key.</param>
    private void OnCommandKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        _shell.Complete();
        CommandBox.CaretIndex = CommandBox.Text.Length;
        e.Handled = true;
    }

    /// <summary>Moves the keyboard to the command bar, closing what lies over it, and selects what is written.</summary>
    private void FocusCommandBar()
    {
        _shell.ExitPanel.Close();
        _shell.Settings.Close();
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
