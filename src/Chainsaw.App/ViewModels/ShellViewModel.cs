using Chainsaw.App.Commands;
using Chainsaw.App.Links;
using Chainsaw.App.Navigation;
using Chainsaw.App.Settings;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel.Commands;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The shell (sketch SH1b): the rail of views, the view shown, the open case and the way out in the title bar, the
/// command bar with its commands, hidden until Ctrl+K or Ctrl+F brings it up as a Spotlight, Nytt ärende (NC1a) and
/// the settings (ST1a). It knows nothing of WPF, so that it is tested in Chainsaw.App.Tests.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    /// <summary>What the title bar says while no case is open.</summary>
    public const string NoCaseText = "Inget ärende öppet";

    /// <summary>What the title bar says while no exit is connected.</summary>
    public const string NoExitText = "Ingen utgång";

    /// <summary>What the title bar says while an exit is connected and its name is still being read.</summary>
    public const string ConnectedText = "Utgång ansluten";

    /// <summary>What the title bar says when lookups go through Windows.</summary>
    public const string WindowsText = "Windows";

    /// <summary>What the exit's tooltip says when lookups go through Windows.</summary>
    public const string WindowsSourceText = "Uppslag går genom Windows";

    /// <summary>What the title bar says when Windows is the way out and the computer has no network.</summary>
    public const string WindowsOfflineText = "Offline";

    /// <summary>What the exit's tooltip says when Windows is the way out and the computer has no network.</summary>
    public const string WindowsOfflineSourceText = OfflineWords.NoNetworkText;

    /// <summary>
    /// What Chainsaw says while no exit is connected (principle 9), in the exit's tooltip and in the graph alike, so
    /// that offline is said one way only.
    /// </summary>
    public const string OfflineText = OfflineWords.NoExitText;

    /// <summary>What the title bar says while a fallen tunnel is connected again by itself.</summary>
    public const string ReconnectingText = "Ansluter igen …";

    /// <summary>What the bar says when a sanction list is chosen while one is being imported.</summary>
    public const string ImportRunningText = "En sanktionslista importeras redan.";

    /// <summary>The window's open case, named in the title bar.</summary>
    private readonly CaseSession _session;

    /// <summary>The command bar's commands.</summary>
    private readonly CommandRegistry _commands;

    /// <summary>The exits, for the connected exit's name.</summary>
    private readonly IExitStore _exits;

    /// <summary>The cases, listed anew when the shell is shown.</summary>
    private readonly CaseWorkflow _workflow;

    /// <summary>The UI thread, where the exit's name is shown.</summary>
    private readonly IUiThread _ui;

    /// <summary>The way out and the exit, shown in the title bar.</summary>
    private readonly ExitWay _way;

    /// <summary>The import of a sanction list, from the menu Nytt.</summary>
    private readonly SanctionListImport _sanctions;

    /// <summary>
    /// The toaster, for an answer that comes while the command bar is hidden; or <see langword="null"/>.
    /// </summary>
    private readonly Toaster? _toaster;

    /// <summary>Whether a sanction list is being imported, so that a second import waits for the first.</summary>
    private bool _importing;

    /// <summary>The ways to finish the text that Tab goes through.</summary>
    private IReadOnlyList<string> _completions = [];

    /// <summary>Which of <see cref="_completions"/> is in the bar.</summary>
    private int _completionIndex;

    /// <summary>The text Tab put in the bar last, so that the next Tab goes on to the next way.</summary>
    private string? _completed;

    /// <summary>Creates the shell with every view of <see cref="ViewTable"/> and the start view shown.</summary>
    /// <param name="session">The open case, named in the title bar.</param>
    /// <param name="commands">The command bar's commands.</param>
    /// <param name="newCase">Nytt ärende.</param>
    /// <param name="workflow">The cases, whose list is read anew when the shell is shown.</param>
    /// <param name="exit">The way out and the exit, shown in the title bar.</param>
    /// <param name="exits">The exits, for the connected exit's name.</param>
    /// <param name="sanctions">The import of a sanction list, from the menu Nytt.</param>
    /// <param name="exitPanel">The exits' panel under the exit in the title bar (sketch UT1a).</param>
    /// <param name="settings">The settings dialog (sketch ST1a).</param>
    /// <param name="ui">The UI thread.</param>
    /// <param name="monitoring">The view Bevakning (sketch BV1a), or <see langword="null"/>.</param>
    /// <param name="watches">
    /// The watches, whose unseen hits put a dot on the pill's eye; or <see langword="null"/>.
    /// </param>
    /// <param name="toaster">
    /// The toaster, which says an answer that comes while the command bar is hidden; or <see langword="null"/>.
    /// </param>
    /// <param name="casePicker">
    /// The case picker under the case in the title bar (sketch ÄV1a), or <see langword="null"/>.
    /// </param>
    public ShellViewModel(
        CaseSession session,
        CommandRegistry commands,
        NewCaseViewModel newCase,
        CaseWorkflow workflow,
        ExitWay exit,
        IExitStore exits,
        SanctionListImport sanctions,
        ExitPanelViewModel exitPanel,
        SettingsViewModel settings,
        IUiThread ui,
        MonitoringViewModel? monitoring = null,
        WatchBoard? watches = null,
        Toaster? toaster = null,
        CasePickerViewModel? casePicker = null)
    {
        _toaster = toaster;
        CasePicker = casePicker;
        Monitoring = monitoring;
        _way = exit;
        Settings = settings;
        exitPanel.SettingsWanted += (_, _) => Show(AppView.Settings, SettingsPage.Network);
        _session = session;
        _sanctions = sanctions;
        _commands = commands;
        _exits = exits;
        _ui = ui;
        _workflow = workflow;
        NewCase = newCase;
        ExitPanel = exitPanel;
        exitPanel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ExitPanelViewModel.IsOpen) && exitPanel.IsOpen)
            {
                DismissMenus();
                CasePicker?.Close();
            }
        };
        if (casePicker is not null)
        {
            FollowCasePicker(casePicker);
        }

        workflow.CasesListed += (_, _) => ui.Post(() => CommandHint = _commands.Hint(CommandText));
        session.Changed += (_, _) => OnCaseChanged();
        newCase.Created += (_, reply) => OnCreated(reply);
        newCase.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NewCaseViewModel.IsOpen) && newCase.IsOpen)
            {
                DismissAll();
            }
        };
        exit.Changed += (_, _) => ShowExit();
        Top = ViewTable.In(RailGroup.Top).Select(e => new RailItemViewModel(e)).ToList();
        Work = ViewTable.In(RailGroup.Work).Select(e => new RailItemViewModel(e)).ToList();
        Bottom = ViewTable.In(RailGroup.Bottom).Select(e => new RailItemViewModel(e)).ToList();
        foreach (var menu in Submenus)
        {
            menu.Chosen += (_, action) => OnChosen(action);
            menu.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SubmenuViewModel.IsOpen))
                {
                    OnPropertyChanged(nameof(OpenSubmenu));
                }
            };
        }

        if (watches is not null)
        {
            watches.Changed += (_, _) => ShowWatchDot(watches);
            ShowWatchDot(watches);
        }

        OnCaseChanged();
        Show(ViewTable.Start);
        ShowExit();
        NewCase.FollowExit(IsExitLive, _way.IsWindows, ExitText, _way.Offline);
    }

    /// <summary>Gets Nytt ärende, the panel from the right.</summary>
    public NewCaseViewModel NewCase { get; }

    /// <summary>Gets the exits' panel, folded out under the exit in the title bar (sketch UT1a).</summary>
    public ExitPanelViewModel ExitPanel { get; }

    /// <summary>
    /// Gets the case picker under the case in the title bar (sketch ÄV1a), opened with a click or Ctrl+O; or
    /// <see langword="null"/> where there is none.
    /// </summary>
    public CasePickerViewModel? CasePicker { get; }

    /// <summary>Gets the settings dialog (sketch ST1a), opened with Ctrl+, or the pill's cog.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>Gets the views at the top of the rail.</summary>
    public IReadOnlyList<RailItemViewModel> Top { get; }

    /// <summary>Gets the views that follow the money.</summary>
    public IReadOnlyList<RailItemViewModel> Work { get; }

    /// <summary>Gets the views at the bottom of the rail.</summary>
    public IReadOnlyList<RailItemViewModel> Bottom { get; }

    /// <summary>Gets every view of the rail, from the top down.</summary>
    public IEnumerable<RailItemViewModel> Items => Top.Concat(Work).Concat(Bottom);

    /// <summary>Gets the icon whose submenu is open, or <see langword="null"/> when none is.</summary>
    public RailItemViewModel? OpenSubmenu => Items.FirstOrDefault(item => item.Submenu?.IsOpen == true);

    /// <summary>
    /// Raised when the command bar is brought up and wants the keyboard: Ctrl+K, Ctrl+F, or an action such as Nytt →
    /// Startpunkt.
    /// </summary>
    public event EventHandler? CommandBarWanted;

    /// <summary>
    /// Gets whether any of the work views' icons stands in the rail; without a case none does, and the rail drops the
    /// group's line so that two lines never stand on top of each other.
    /// </summary>
    [ObservableProperty]
    public partial bool IsWorkShown { get; private set; }

    /// <summary>
    /// Gets whether the command bar is up. It is hidden by default and comes up like Spotlight (Ctrl+K or Ctrl+F);
    /// Esc, a click outside or a command that did what it was asked hides it again.
    /// </summary>
    [ObservableProperty]
    public partial bool IsCommandBarOpen { get; private set; }

    /// <summary>Gets the view shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentEntry), nameof(IsGraphShown), nameof(IsGuideShown),
        nameof(IsMonitoringShown), nameof(IsPlaceholderShown))]
    public partial AppView Current { get; private set; }

    /// <summary>Gets whether the graph is shown (sketch GR4).</summary>
    public bool IsGraphShown => Current == AppView.Graph;

    /// <summary>Gets whether the view Guide is shown (F1, sketch GD1a in a simple form).</summary>
    public bool IsGuideShown => Current == AppView.Guide;

    /// <summary>Gets whether the view Bevakning is shown (sketch BV1a).</summary>
    public bool IsMonitoringShown => Current == AppView.Monitoring && Monitoring is not null;

    /// <summary>Gets whether the shown view is not built yet, so that the page says so.</summary>
    public bool IsPlaceholderShown => !IsGraphShown && !IsGuideShown && !IsMonitoringShown;

    /// <summary>Gets the view Bevakning, or <see langword="null"/> when the shell is built without it.</summary>
    public MonitoringViewModel? Monitoring { get; }

    /// <summary>Gets the entry of the view shown: its name and icon for the header and the empty page.</summary>
    public ViewEntry CurrentEntry => ViewTable.For(Current);

    /// <summary>Gets or sets the text in the command bar.</summary>
    [ObservableProperty]
    public partial string CommandText { get; set; } = string.Empty;

    /// <summary>Gets the short answer under the command bar's field, or an empty text when there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownAbove), nameof(IsShowingError))]
    public partial string CommandAnswer { get; private set; } = string.Empty;

    /// <summary>Gets whether the answer is an error.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingError))]
    public partial bool IsAnswerError { get; private set; }

    /// <summary>Gets whether an error is shown under the bar's field (in the error colour).</summary>
    public bool IsShowingError => IsAnswerError && CommandAnswer.Length > 0;

    /// <summary>Gets the hint for what is written, for example what Tab fills in; empty when there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownAbove))]
    public partial string CommandHint { get; private set; } = string.Empty;

    /// <summary>Gets what is shown under the bar's field: the answer, or else the hint.</summary>
    public string ShownAbove => CommandAnswer.Length > 0 ? CommandAnswer : CommandHint;

    /// <summary>Gets whether a command runs.</summary>
    [ObservableProperty]
    public partial bool IsRunning { get; private set; }

    /// <summary>Gets the case in the title bar: its number and title, or that none is open.</summary>
    public string CaseTitle => _session.Current is null ? NoCaseText : _session.DisplayName;

    /// <summary>
    /// Gets what the title bar says about the way out: Windows, the exit's name, or that none is connected.
    /// </summary>
    [ObservableProperty]
    public partial string ExitText { get; private set; } = NoExitText;

    /// <summary>Gets whether lookups can go: through Windows or a connected exit (a green dot).</summary>
    [ObservableProperty]
    public partial bool IsExitLive { get; private set; }

    /// <summary>Gets what the exit's tooltip in the title bar says about where lookups go.</summary>
    [ObservableProperty]
    public partial string SourceText { get; private set; } = OfflineText;

    /// <summary>
    /// Gets what the amber dot by the exit in the title bar says on hover while a lookup waits for a source (its pace
    /// or a wait it asked for), such as <c>Väntar på källan · Bitcoin</c>; empty otherwise.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaiting))]
    public partial string WaitText { get; private set; } = string.Empty;

    /// <summary>Gets whether a lookup waits for a source now.</summary>
    public bool IsWaiting => WaitText.Length > 0;

    /// <summary>Gets the submenus of the rail's icons.</summary>
    private IEnumerable<SubmenuViewModel> Submenus =>
        Items.Select(item => item.Submenu).OfType<SubmenuViewModel>();

    /// <summary>
    /// Presses an icon in the pill (a click, or Enter or Space on it): an icon with a submenu folds it out, or in when
    /// it is out (sketch PN1); any other icon shows its view.
    /// </summary>
    /// <param name="item">The icon.</param>
    [RelayCommand]
    public void Press(RailItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ExitPanel.Close();
        CasePicker?.Close();
        if (NewCase.IsOpen || Settings.IsOpen)
        {
            // The panel or the dialog lies over the pill: a menu opened behind it would take the keys unseen.
            DismissSubmenus();
            return;
        }

        if (item.Submenu is not { } menu)
        {
            Show(item.Entry.View);
            return;
        }

        foreach (var other in Submenus.Where(other => !ReferenceEquals(other, menu)))
        {
            other.Dismiss();
        }

        menu.Toggle();
    }

    /// <summary>
    /// Closes any open submenu without taking the keyboard back, for example when the window is left. The exits'
    /// panel stays, since Windows' file dialog leaves the window while a WireGuard file is chosen.
    /// </summary>
    public void DismissSubmenus() => DismissMenus();

    /// <summary>
    /// Closes the command bar, any open submenu, the exits' panel and the settings, for example when Chainsaw locks or
    /// a view is shown.
    /// </summary>
    public void DismissAll()
    {
        CloseCommandBar();
        DismissMenus();
        ExitPanel.Close();
        CasePicker?.Close();
        Settings.Close();
    }

    /// <summary>
    /// Ctrl+W: saves and closes the open case and goes to the start (the user's decision 2026-11-08). Nothing happens
    /// without an open case; a case that cannot be closed says why in a toast and stays open.
    /// </summary>
    /// <returns>A task that completes when the case is closed, or why not is said.</returns>
    public async Task CloseCaseAsync()
    {
        if (_session.Current is null)
        {
            return;
        }

        var reply = await _workflow.CloseAsync();
        if (reply.IsError)
        {
            _toaster?.Show(ToastKind.Error, reply.Text);
            return;
        }

        Show(ViewTable.Start);
    }

    /// <summary>
    /// Opens the case picker (Ctrl+O), unless Nytt ärende or the settings lie over the title bar's space.
    /// </summary>
    public void OpenCasePicker()
    {
        if (CasePicker is null || NewCase.IsOpen || Settings.IsOpen)
        {
            return;
        }

        CasePicker.Open();
    }

    /// <summary>
    /// Shows a view and marks its icon in the rail, and closes any open submenu. *Nytt* is not a page but the panel
    /// from the right (NC1a), which its shortcut opens directly, and *Inställningar* is the dialog over the view shown
    /// (ST1a), which stays marked.
    /// </summary>
    /// <param name="view">The view to show.</param>
    [RelayCommand]
    public void Show(AppView view) => Show(view, null);

    /// <summary>Shows a view, or the settings at a page.</summary>
    /// <param name="view">The view to show.</param>
    /// <param name="page">The settings' page, when the view is the settings; else ignored.</param>
    public void Show(AppView view, SettingsPage? page)
    {
        if (view == AppView.Settings && Settings.IsOpen)
        {
            // Already open: only the page changes, so that nothing written is closed and read again.
            Settings.Open(page);
            return;
        }

        DismissAll();
        if (view == AppView.New)
        {
            NewCase.Show();
            return;
        }

        if (view == AppView.Settings)
        {
            if (!NewCase.IsOpen)
            {
                Settings.Open(page);
            }

            return;
        }

        if (ViewTable.For(view).NeedsCase && _session.Current is null)
        {
            // A view that needs a case is folded away without one; its shortcut does nothing.
            return;
        }

        Current = view;
        foreach (var item in Items)
        {
            item.IsSelected = item.Entry.View == view;
        }
    }

    /// <summary>
    /// Runs what is written in the command bar (Enter). A command that did what it was asked empties the bar and hides
    /// it, its answer said by the toaster; one that could not keeps the bar up with the text, so that it can be
    /// corrected.
    /// </summary>
    /// <returns>A task that completes when the answer is shown.</returns>
    [RelayCommand]
    public async Task SubmitAsync()
    {
        var text = CommandText;
        if (IsRunning || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        IsRunning = true;
        CommandReply reply;
        try
        {
            reply = await _commands.RunAsync(text);
        }
        finally
        {
            IsRunning = false;
        }

        if (!reply.IsError && CommandText == text)
        {
            CommandText = string.Empty;
            CloseCommandBar();
        }

        Answer(reply);
    }

    /// <summary>
    /// Fills in what is written (Tab): a command's name, or its argument, such as a case number for <c>/open</c>.
    /// Tab again goes on to the next way.
    /// </summary>
    [RelayCommand]
    public void Complete()
    {
        if (_completed is not null && CommandText == _completed && _completions.Count > 1)
        {
            _completionIndex = (_completionIndex + 1) % _completions.Count;
        }
        else
        {
            _completions = _commands.Complete(CommandText);
            _completionIndex = 0;
        }

        if (_completions.Count == 0)
        {
            return;
        }

        var chosen = _completions[_completionIndex];
        _completed = chosen;
        CommandText = chosen;
    }

    /// <summary>Clears the command bar and its answer, for example when Chainsaw locks.</summary>
    [RelayCommand]
    public void ClearCommand()
    {
        CommandText = string.Empty;
        CommandAnswer = string.Empty;
        _completed = null;
    }

    /// <summary>
    /// Brings up the command bar (Ctrl+K or Ctrl+F) and asks for the keyboard there. What was written stays, so that
    /// it can be run again or written over; an old answer from while it was hidden does not. Nytt ärende lies over the
    /// page, so the bar stays hidden while it is open.
    /// </summary>
    [RelayCommand]
    public void OpenCommandBar()
    {
        if (NewCase.IsOpen)
        {
            return;
        }

        if (!IsCommandBarOpen)
        {
            CommandAnswer = string.Empty;
            IsCommandBarOpen = true;
        }

        CommandBarWanted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Hides the command bar (Esc, a click outside, or a command that did what it was asked). What is written stays
    /// for the next time; the answer goes.
    /// </summary>
    [RelayCommand]
    public void CloseCommandBar()
    {
        if (!IsCommandBarOpen)
        {
            return;
        }

        IsCommandBarOpen = false;
        CommandAnswer = string.Empty;
        _completed = null;
    }

    /// <summary>
    /// Offers a start point from a <c>chainsaw:</c> link. Nothing is written: with Nytt ärende shown it is added as a
    /// line there; with a case open the bar comes up with it, for Enter to add; with none it fills in Nytt ärende.
    /// The value is written with its chain when it is not Bitcoin (<see cref="StartPointText"/>), so that a TRON tx is
    /// never read as a Bitcoin txid. What the user is writing in the bar is never replaced.
    /// </summary>
    /// <param name="startPoint">The recognised start point.</param>
    public void Offer(ChainStartPoint startPoint)
    {
        ArgumentNullException.ThrowIfNull(startPoint);
        var text = StartPointText.Write(startPoint);
        if (NewCase.IsOpen || _session.Current is null)
        {
            NewCase.Show(text);
            return;
        }

        OpenCommandBar();
        if (CommandText.Length > 0)
        {
            Answer(CommandReply.Done($"Från en länk: {text}. Raden har redan text, så den lades inte in."));
            return;
        }

        CommandText = text;
        Answer(startPoint.IsSupported
            ? CommandReply.Done($"Från en länk: Enter lägger den som startpunkt i {_session.DisplayName}.")
            : CommandReply.Failed("Från en länk: " + CaseWorkflow.NotBuiltText(startPoint)));
    }

    /// <summary>Shows an answer that came from elsewhere, for example the first case of the setup.</summary>
    /// <param name="reply">The answer.</param>
    public void Say(CommandReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        Answer(reply);
    }

    /// <summary>Called when the shell is shown: the case files are listed anew off the UI thread.</summary>
    public void OnShown() => _ = _workflow.RefreshCasesAsync();

    /// <summary>
    /// Does what a row of a submenu stands for. A row that is not built, or needs a case that is not open, cannot be
    /// chosen, so only what is built arrives here.
    /// </summary>
    /// <param name="action">The row's action.</param>
    private void OnChosen(SubmenuAction action)
    {
        switch (action)
        {
            case SubmenuAction.StartPoint:
                AddStartPoint();
                break;
            case SubmenuAction.ImportSanctionList:
                _ = ImportSanctionListAsync();
                break;
            case SubmenuAction.NewCase:
                NewCase.Show();
                break;
            case SubmenuAction.Watch when Monitoring is not null:
                Show(AppView.Monitoring);
                Monitoring.New();
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Follows the case picker: while it is out, the submenus and the exits' panel fold in, and its
    /// <em>Nytt ärende …</em> opens the panel from the right.
    /// </summary>
    /// <param name="picker">The case picker.</param>
    private void FollowCasePicker(CasePickerViewModel picker)
    {
        picker.NewCaseWanted += (_, _) => Show(AppView.New);
        picker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CasePickerViewModel.IsOpen) && picker.IsOpen)
            {
                DismissMenus();
                ExitPanel.Close();
            }
        };
    }

    /// <summary>Puts a dot on the pill's eye while a watch has hits the user has not seen (sketch BV1a).</summary>
    /// <param name="watches">The watches.</param>
    private void ShowWatchDot(WatchBoard watches)
    {
        if (Items.FirstOrDefault(item => item.Entry.View == AppView.Monitoring) is { } eye)
        {
            eye.Waiting = watches.Snapshot.Unseen;
        }
    }

    /// <summary>
    /// Nytt → Startpunkt: with a case open the keyboard goes to the command bar, where an address or a tx and Enter add
    /// a start point (etapp 11); with none, Nytt ärende opens, since a start point starts a case.
    /// </summary>
    private void AddStartPoint()
    {
        if (_session.Current is null)
        {
            NewCase.Show();
            return;
        }

        OpenCommandBar();
        if (CommandText.Length == 0)
        {
            Answer(CommandReply.Done(
                $"Skriv en adress eller en tx och tryck Enter, så blir den en startpunkt i {_session.DisplayName}."));
        }
    }

    /// <summary>Nytt → Importera sanktionslista …: asks for the file and says what was imported, or why not.</summary>
    /// <returns>A task that completes when the answer is shown.</returns>
    private async Task ImportSanctionListAsync()
    {
        if (_importing)
        {
            Answer(CommandReply.Failed(ImportRunningText));
            return;
        }

        _importing = true;
        try
        {
            if (await _sanctions.ImportAsync() is { } reply)
            {
                Answer(reply);
            }
        }
        finally
        {
            _importing = false;
        }
    }

    /// <summary>Closes any open submenu of the pill, for example when the exits' panel opens.</summary>
    private void DismissMenus()
    {
        foreach (var menu in Submenus)
        {
            menu.Dismiss();
        }
    }

    /// <summary>Names the case in the title bar and says which rows of the submenus can be chosen.</summary>
    private void OnCaseChanged()
    {
        var hasCase = _session.Current is not null;
        foreach (var menu in Submenus)
        {
            menu.Update(hasCase);
        }

        foreach (var item in Top.Concat(Work).Concat(Bottom))
        {
            item.IsShown = hasCase || item.Entry.IsShownWithoutCase;
        }

        IsWorkShown = Work.Any(item => item.IsShown);

        if (!hasCase && CurrentEntry.NeedsCase)
        {
            Show(ViewTable.Start);
        }

        OnPropertyChanged(nameof(CaseTitle));
    }

    /// <summary>
    /// Shows an answer under the bar's field, and through the toaster as well while the bar is hidden, so that an
    /// answer is never lost.
    /// </summary>
    /// <param name="reply">The answer.</param>
    private void Answer(CommandReply reply)
    {
        CommandAnswer = reply.Text;
        IsAnswerError = reply.IsError;
        if (!IsCommandBarOpen && reply.Text.Length > 0)
        {
            _toaster?.Show(reply.IsError ? ToastKind.Error : ToastKind.Information, reply.Text);
        }
    }

    /// <summary>Shows the new case: the graph, where the work starts, and the answer from the creation.</summary>
    /// <param name="reply">The creation's answer.</param>
    private void OnCreated(CommandReply reply)
    {
        Show(AppView.Graph);
        Answer(reply);
    }

    /// <summary>
    /// Shows the way out honestly: Windows, or offline when the computer has no network, or the exit connected with
    /// its name, connecting, being connected again after it fell, or not connected.
    /// </summary>
    private void ShowExit()
    {
        IsExitLive = _way.IsLive;
        WaitText = _way.WaitText;
        if (_way.IsWindowsOffline)
        {
            (ExitText, SourceText) = (WindowsOfflineText, WindowsOfflineSourceText);
            return;
        }

        if (_way.IsWindows)
        {
            (ExitText, SourceText) = (WindowsText, WindowsSourceText);
            return;
        }

        var status = _way.Status;
        (ExitText, SourceText) = status.State switch
        {
            ExitState.Connected => (ConnectedText, "Uppslag går genom utgången"),
            _ when _way.IsReconnecting => (ReconnectingText, _way.Offline.Text),
            ExitState.Connecting => ("Ansluter …", _way.Offline.Text),
            ExitState.Failed => ("Utgången är inte ansluten", _way.Offline.Text),
            _ => (NoExitText, _way.Offline.Text),
        };
        if (IsExitLive && status.ExitId is { } id)
        {
            _ = NameExitAsync(id);
        }
    }

    /// <summary>Tells Nytt ärende the way out whenever the title bar's changes (its tag <c>via …</c>).</summary>
    /// <param name="value">The new text.</param>
    partial void OnExitTextChanged(string value) => NewCase.FollowExit(IsExitLive, _way.IsWindows, value, _way.Offline);

    /// <summary>Tells Nytt ärende whether lookups can go, so that rows waiting for the exit are fetched.</summary>
    /// <param name="value">Whether they can.</param>
    partial void OnIsExitLiveChanged(bool value) => NewCase.FollowExit(value, _way.IsWindows, ExitText, _way.Offline);

    /// <summary>
    /// Reads the connected exit's name from the vault off the UI thread, and shows it if the exit is still connected.
    /// </summary>
    /// <param name="id">The exit.</param>
    /// <returns>A task that completes when the name is shown or could not be read.</returns>
    private async Task NameExitAsync(Guid id)
    {
        ExitInfo? exit;
        try
        {
            exit = (await Task.Run(_exits.List)).FirstOrDefault(item => item.Id == id);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The vault may have locked meanwhile; the words without the name stay.
            return;
        }

        if (exit is not null)
        {
            _ui.Post(() =>
            {
                if (IsExitLive && !_way.IsWindows && _way.Status.ExitId == id)
                {
                    var name = exit.Place.Length > 0 ? exit.Place : exit.Name;
                    (ExitText, SourceText) = (name, $"Uppslag går genom utgången {name}");
                }
            });
        }
    }

    /// <summary>
    /// Clears an old answer as soon as the text changes, so that it never answers something else, and gives the hint
    /// for the new text.
    /// </summary>
    /// <param name="value">The new text.</param>
    partial void OnCommandTextChanged(string value)
    {
        CommandAnswer = string.Empty;
        CommandHint = _commands.Hint(value);
    }
}
