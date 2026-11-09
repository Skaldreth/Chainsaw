using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Chainsaw.App.Controls;
using Chainsaw.App.Figure;
using Chainsaw.App.ViewModels;
using Microsoft.Win32;

namespace Chainsaw.App.Views;

/// <summary>
/// The gate's page (sketches ID8f, LS1b and FS1a): the band of chains and helix, and above it the lock screen's form or
/// the first-run setup's. The view only wires the fields, the keyboard and the figure; what happens lives in
/// <see cref="GateViewModel"/>. The master key is taken from the password boxes as characters
/// (<see cref="PasswordEntry"/>) and never read as text. After the right key the page stays over the shell for a moment
/// while the helix's middle becomes the node Nytt ärende, and then fades to show the shell with the real node under it.
/// </summary>
public partial class GateView : UserControl
{
    /// <summary>The room kept free for the band under the lock screen's form: the axis and the helix's widest half.</summary>
    public static readonly Thickness BandRoom = new(0, 0, 0, ChainBand.AxisFromBottom + 100);

    /// <summary>The room kept for the band under the setup's taller steps, which may reach a little into it.</summary>
    public static readonly Thickness FirstRunBandRoom = new(0, 0, 0, ChainBand.AxisFromBottom + 30);

    /// <summary>The node Nytt ärende's size, as the graph's start page draws it, for when it cannot be found.</summary>
    private static readonly Size NodeSize = new(220, 54);

    /// <summary>How far the field shakes after a wrong key, in pixels.</summary>
    private const double ShakeWidth = 9;

    /// <summary>Whether the last key given was wrong, so the field's edge is red until it changes.</summary>
    private bool _wasWrong;

    /// <summary>What to do once the way to the shell is over, while it runs.</summary>
    private Action? _whenOpen;

    /// <summary>Finds the node Nytt ärende on the shell while the band is on its way to it; null otherwise.</summary>
    private Func<FrameworkElement?>? _findNode;

    /// <summary>Where the node Nytt ärende stood when the band was last told, on the figure.</summary>
    private Rect _nodeBounds;

    /// <summary>The mode shown last, to tell an unlock from the lock screen from the end of the setup.</summary>
    private GateMode _shown;

    /// <summary>How many characters the lock screen's field held last, to tell a typed one from a removed one.</summary>
    private int _typed;
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

    /// <summary>Follows the gate: the keyboard goes to the field of the form shown.</summary>
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

    /// <summary>Empties hidden secret fields and moves the keyboard to the form shown.</summary>
    private void ShowMode()
    {
        if (_gate is null)
        {
            return;
        }

        AboutPopup.IsOpen = false;
        HelpPopup.IsOpen = false;
        _gate.LockScreen.CloseAbout();
        if (_gate.Mode != GateMode.Unlocked)
        {
            _shown = _gate.Mode;
        }

        switch (_gate.Mode)
        {
            case GateMode.Locked:
                FirstKey.Clear();
                RepeatKey.Clear();
                LockPage.Visibility = Visibility.Visible;
                _wasWrong = false;
                ShowField();
                FocusLater(LockKey);
                break;
            case GateMode.FirstRun:
                LockPage.Visibility = Visibility.Collapsed;
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
        FirstRunStep.Exit => WindowsCard,
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

    /// <summary>
    /// Gives the lock screen's field to the vault. A wrong key jerks the band, pulses it red and shakes the field with a
    /// red edge; the keyboard stays in the field.
    /// </summary>
    /// <returns>A task that completes when the vault has answered.</returns>
    private async Task UnlockAsync()
    {
        if (_gate is null || LockKey.SecurePassword.Length == 0)
        {
            return;
        }

        if (!await _gate.LockScreen.UnlockAsync(_lockKey))
        {
            _wasWrong = true;
            Figure.Wrong();
            ShakeField();
            ShowField();
            LockKey.Focus();
        }
    }

    /// <summary>
    /// Follows the lock screen's field: a typed character lets a base go of the helix, the hint and the button follow
    /// whether anything is typed, and the edge leaves the red of a wrong key.
    /// </summary>
    /// <param name="sender">The field.</param>
    /// <param name="e">Not used.</param>
    private void OnLockKeyChanged(object sender, RoutedEventArgs e)
    {
        var length = LockKey.SecurePassword.Length;
        if (length > _typed)
        {
            Figure.Typed();
        }

        _typed = length;
        _wasWrong = false;
        ShowField();
    }

    /// <summary>
    /// Shows the field as it stands (sketch LS1b): the hint while it is empty, a quiet edge then, the accent once
    /// something is typed and red after a wrong key; the button only once something is typed.
    /// </summary>
    private void ShowField()
    {
        var typed = LockKey.SecurePassword.Length > 0;
        LockHint.Visibility = typed ? Visibility.Collapsed : Visibility.Visible;
        UnlockButton.Visibility = typed ? Visibility.Visible : Visibility.Hidden;
        string edge;
        if (_wasWrong)
        {
            edge = "Brush.Error";
        }
        else
        {
            edge = typed ? "Brush.Accent" : "Brush.BorderStrong";
        }

        LockKey.SetResourceReference(Control.BorderBrushProperty, edge);
    }

    /// <summary>Shakes the field sideways a few times and lets it settle, unless Windows' animations are off.</summary>
    private void ShakeField()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(480) };
        double[] steps = [ShakeWidth, -ShakeWidth * 0.8, ShakeWidth * 0.55, -ShakeWidth * 0.3, ShakeWidth * 0.12, 0];
        for (var i = 0; i < steps.Length; i++)
        {
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(steps[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80 * (i + 1)))));
        }

        LockShake.BeginAnimation(TranslateTransform.XProperty, shake);
    }

    /// <summary>
    /// Plays the way from the lock screen to the shell (sketch LS1b), when the vault was opened from the lock screen:
    /// the page stays over the shell while the form fades and the helix's middle becomes the node Nytt ärende where it
    /// stands, and then fades itself to show the shell with the real node under the drawn one.
    /// </summary>
    /// <param name="node">Finds the node Nytt ärende on the shell, once it is laid out; null when it is not shown.</param>
    /// <param name="whenOpen">What to do once the way is over: hide the page.</param>
    /// <returns>Whether the way is played; otherwise the page is hidden at once.</returns>
    public bool PlayUnlock(Func<FrameworkElement?> node, Action whenOpen)
    {
        if (_shown != GateMode.Locked || !IsVisible || Figure.ActualWidth < 1)
        {
            return false;
        }

        _whenOpen = whenOpen;
        _findNode = node;
        IsHitTestVisible = false;
        LockPage.Visibility = Visibility.Visible;
        Figure.Stepped += OnUnlockStep;
        _nodeBounds = NodeBounds(node());
        Figure.Right(_nodeBounds);
        return true;
    }

    /// <summary>Gives where the node stands on the figure, or a node's size in the middle of the page without it.</summary>
    /// <param name="node">The node, or null.</param>
    /// <returns>Its bounds on the figure.</returns>
    private Rect NodeBounds(FrameworkElement? node)
    {
        if (LaidOut(node) is { } shown)
        {
            return shown.TransformToVisual(Figure).TransformBounds(new Rect(shown.RenderSize));
        }

        return new Rect(
            (Figure.ActualWidth - NodeSize.Width) / 2, (Figure.ActualHeight - NodeSize.Height) / 2, NodeSize.Width, NodeSize.Height);
    }

    /// <summary>Gives the node when it is shown and laid out on the screen, so that it can be measured.</summary>
    /// <param name="node">The node, or null.</param>
    /// <returns>The node, or null when it cannot be measured.</returns>
    private static FrameworkElement? LaidOut(FrameworkElement? node) =>
        node is { IsVisible: true, ActualWidth: > 0 } && PresentationSource.FromVisual(node) is not null ? node : null;

    /// <summary>
    /// Lets the band follow the node Nytt ärende while it is on its way: the latest cases are listed off the UI thread
    /// after the unlock, and when they come in the node moves from the first time's place (higher up, over the texts)
    /// into the middle of their ring. Measured once, the band would land where the node no longer stands.
    /// </summary>
    private void FollowNode()
    {
        if (_findNode is null)
        {
            return;
        }

        // Only a node that is laid out is followed: a ring just built has none yet, and the page's middle is no place.
        if (LaidOut(_findNode()) is { } node && NodeBounds(node) is var bounds && bounds != _nodeBounds)
        {
            _nodeBounds = bounds;
            Figure.MoveNode(bounds);
        }
    }

    /// <summary>Fades the form and then the page with the figure's way, and lets the page go once it is over.</summary>
    /// <param name="sender">The figure.</param>
    /// <param name="e">Not used.</param>
    private void OnUnlockStep(object? sender, EventArgs e)
    {
        var motion = Figure.Motion;
        FollowNode();
        LockForm.Opacity = 1 - motion.FormGone();
        Opacity = 1 - motion.PageGone();
        if (!motion.IsUnlocked)
        {
            return;
        }

        Figure.Stepped -= OnUnlockStep;
        _findNode = null;
        var whenOpen = _whenOpen;
        _whenOpen = null;
        whenOpen?.Invoke();
        Opacity = 1;
        LockForm.Opacity = 1;
        IsHitTestVisible = true;
        _shown = GateMode.Unlocked;
    }

    /// <summary>Shows what the ? tells: the key, the way out, the lock and a watch's hit.</summary>
    /// <param name="sender">The ?.</param>
    /// <param name="e">Not used.</param>
    private void OnHelpShown(object sender, RoutedEventArgs e) => HelpPopup.IsOpen = true;

    /// <summary>Hides what the ? tells once neither the pointer nor the keyboard is on it.</summary>
    /// <param name="sender">The ?.</param>
    /// <param name="e">Not used.</param>
    private void OnHelpHidden(object sender, RoutedEventArgs e) =>
        HelpPopup.IsOpen = HelpButton.IsMouseOver || HelpButton.IsKeyboardFocused;

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
            AddToRecent = false,
        };
        if (_gate is not null && dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            await _gate.FirstRun.AddExitAsync(dialog.FileName);
        }
    }

    /// <summary>
    /// Shows <em>Om Chainsaw</em> over the lock screen (sketch ST2a), or only the version where the page cannot be
    /// shown.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnAbout(object sender, RoutedEventArgs e)
    {
        if (_gate?.LockScreen.About is null)
        {
            AboutPopup.IsOpen = !AboutPopup.IsOpen;
            return;
        }

        _gate.LockScreen.OpenAbout();
        FocusLater(AboutClose);
    }

    /// <summary>Closes <em>Om Chainsaw</em> and gives the keyboard back to the form.</summary>
    private void CloseAbout()
    {
        if (_gate is null || !_gate.LockScreen.IsAboutOpen)
        {
            return;
        }

        _gate.LockScreen.CloseAbout();
        FocusLater(_gate.Mode == GateMode.FirstRun ? FirstFieldOfStep() : LockKey);
    }

    /// <summary>Closes <em>Om Chainsaw</em> with ×.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnAboutClose(object sender, RoutedEventArgs e) => CloseAbout();

    /// <summary>Closes <em>Om Chainsaw</em> with a click on the dark around it.</summary>
    /// <param name="sender">The dark.</param>
    /// <param name="e">The click.</param>
    private void OnAboutDimClicked(object sender, MouseButtonEventArgs e) => CloseAbout();

    /// <summary>Esc closes <em>Om Chainsaw</em> when it is shown.</summary>
    /// <param name="sender">The page.</param>
    /// <param name="e">The key.</param>
    private void OnGateKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _gate is { LockScreen.IsAboutOpen: true })
        {
            e.Handled = true;
            CloseAbout();
        }
    }

    /// <summary>Chooses Windows as the way out on the step Utgång.</summary>
    /// <param name="sender">The card.</param>
    /// <param name="e">The click.</param>
    private void OnChooseWindows(object sender, RoutedEventArgs e) =>
        _gate?.FirstRun.ChooseRoute(Modules.Network.Contracts.ExitRoute.Windows);

    /// <summary>Chooses VPN as the way out on the step Utgång.</summary>
    /// <param name="sender">The card.</param>
    /// <param name="e">The click.</param>
    private void OnChooseVpn(object sender, RoutedEventArgs e) =>
        _gate?.FirstRun.ChooseRoute(Modules.Network.Contracts.ExitRoute.Vpn);

    /// <summary>Stores a WireGuard file dropped on the field as the exit.</summary>
    /// <param name="sender">The field.</param>
    /// <param name="e">The drop.</param>
    private async void OnExitFileDropped(object sender, DragEventArgs e)
    {
        if (_gate is not null && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            e.Handled = true;
            await _gate.FirstRun.AddExitAsync(files[0]);
        }
    }

    /// <summary>Connects or disconnects the stored exit.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private async void OnToggleExit(object sender, RoutedEventArgs e)
    {
        if (_gate is not null)
        {
            await _gate.FirstRun.ToggleExitAsync();
        }
    }

    /// <summary>Takes the stored exit away again.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private async void OnRemoveExit(object sender, RoutedEventArgs e)
    {
        if (_gate is not null)
        {
            await _gate.FirstRun.RemoveExitAsync();
        }
    }

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
