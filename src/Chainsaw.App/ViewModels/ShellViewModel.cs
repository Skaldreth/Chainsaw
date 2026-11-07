using Chainsaw.App.Commands;
using Chainsaw.App.Links;
using Chainsaw.App.Navigation;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel.Commands;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The shell (sketch SH1b): the rail of views, the view shown, the open case and the exit in the title bar, the
/// command bar at the bottom with its commands, Nytt ärende (NC1a) and the status line. It knows nothing of WPF, so
/// that it is tested in Chainsaw.App.Tests.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    /// <summary>What the title bar says while no case is open.</summary>
    public const string NoCaseText = "Inget ärende öppet";

    /// <summary>What the title bar says while no exit is connected.</summary>
    public const string NoExitText = "Ingen utgång";

    /// <summary>What the status line says while no exit is connected: nothing can be looked up (principle 9).</summary>
    public const string OfflineText = "Offline: ingen utgång ansluten, så inget nytt kan hämtas";

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

    /// <summary>The UI thread, where the exit's changes are shown.</summary>
    private readonly IUiThread _ui;

    /// <summary>The import of a sanction list, from the menu Nytt.</summary>
    private readonly SanctionListImport _sanctions;

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
    /// <param name="exit">The connection to the exit, shown in the title bar and the status line.</param>
    /// <param name="exits">The exits, for the connected exit's name.</param>
    /// <param name="sanctions">The import of a sanction list, from the menu Nytt.</param>
    /// <param name="exitPanel">The exits' panel under the exit in the title bar (sketch UT1a).</param>
    /// <param name="ui">The UI thread.</param>
    public ShellViewModel(
        CaseSession session,
        CommandRegistry commands,
        NewCaseViewModel newCase,
        CaseWorkflow workflow,
        IExitConnection exit,
        IExitStore exits,
        SanctionListImport sanctions,
        ExitPanelViewModel exitPanel,
        IUiThread ui)
    {
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
            }
        };
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
        exit.StatusChanged += (_, status) => ui.Post(() => ShowExit(status));
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

        OnCaseChanged();
        Show(ViewTable.Start);
        ShowExit(exit.Status);
    }

    /// <summary>Gets Nytt ärende, the panel from the right.</summary>
    public NewCaseViewModel NewCase { get; }

    /// <summary>Gets the exits' panel, folded out under the exit in the title bar (sketch UT1a).</summary>
    public ExitPanelViewModel ExitPanel { get; }

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

    /// <summary>Raised when an action wants the keyboard in the command bar, for example Nytt → Startpunkt.</summary>
    public event EventHandler? CommandBarWanted;

    /// <summary>Gets the view shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentEntry), nameof(IsGraphShown), nameof(IsGuideShown),
        nameof(IsPlaceholderShown))]
    public partial AppView Current { get; private set; }

    /// <summary>Gets whether the graph is shown (sketch GR4).</summary>
    public bool IsGraphShown => Current == AppView.Graph;

    /// <summary>Gets whether the view Guide is shown (F1, sketch GD1a in a simple form).</summary>
    public bool IsGuideShown => Current == AppView.Guide;

    /// <summary>Gets whether the shown view is not built yet, so that the page says so.</summary>
    public bool IsPlaceholderShown => !IsGraphShown && !IsGuideShown;

    /// <summary>Gets the entry of the view shown: its name and icon for the header and the empty page.</summary>
    public ViewEntry CurrentEntry => ViewTable.For(Current);

    /// <summary>Gets or sets the text in the command bar.</summary>
    [ObservableProperty]
    public partial string CommandText { get; set; } = string.Empty;

    /// <summary>Gets the short answer above the command bar, or an empty text when there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownAbove), nameof(IsShowingError))]
    public partial string CommandAnswer { get; private set; } = string.Empty;

    /// <summary>Gets whether the answer is an error.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingError))]
    public partial bool IsAnswerError { get; private set; }

    /// <summary>Gets whether an error is shown above the bar (in the error colour).</summary>
    public bool IsShowingError => IsAnswerError && CommandAnswer.Length > 0;

    /// <summary>Gets the hint for what is written, for example what Tab fills in; empty when there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownAbove))]
    public partial string CommandHint { get; private set; } = string.Empty;

    /// <summary>Gets what is shown above the bar: the answer, or else the hint.</summary>
    public string ShownAbove => CommandAnswer.Length > 0 ? CommandAnswer : CommandHint;

    /// <summary>Gets whether a command runs.</summary>
    [ObservableProperty]
    public partial bool IsRunning { get; private set; }

    /// <summary>Gets the case in the title bar: its number and title, or that none is open.</summary>
    public string CaseTitle => _session.Current is null ? NoCaseText : _session.DisplayName;

    /// <summary>Gets what the title bar says about the exit: its name, or that none is connected.</summary>
    [ObservableProperty]
    public partial string ExitText { get; private set; } = NoExitText;

    /// <summary>Gets whether the exit is connected (a green dot).</summary>
    [ObservableProperty]
    public partial bool IsExitConnected { get; private set; }

    /// <summary>Gets what the status line says about where lookups go.</summary>
    [ObservableProperty]
    public partial string SourceText { get; private set; } = OfflineText;

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
        if (NewCase.IsOpen)
        {
            // The panel lies over the pill: a menu opened behind it would take the keys unseen.
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

    /// <summary>Closes any open submenu and the exits' panel, for example when Chainsaw locks or a view is shown.</summary>
    public void DismissAll()
    {
        DismissMenus();
        ExitPanel.Close();
    }

    /// <summary>
    /// Shows a view and marks its icon in the rail, and closes any open submenu. *Nytt* is not a page but the panel
    /// from the right (NC1a), which its shortcut opens directly.
    /// </summary>
    /// <param name="view">The view to show.</param>
    [RelayCommand]
    public void Show(AppView view)
    {
        DismissAll();
        if (view == AppView.New)
        {
            NewCase.Show();
            return;
        }

        Current = view;
        foreach (var item in Items)
        {
            item.IsSelected = item.Entry.View == view;
        }
    }

    /// <summary>
    /// Runs what is written in the command bar (Enter). A command that did what it was asked empties the bar; one
    /// that could not keeps the text, so that it can be corrected.
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

    /// <summary>Clears the command bar and its answer, as Esc does.</summary>
    [RelayCommand]
    public void ClearCommand()
    {
        CommandText = string.Empty;
        CommandAnswer = string.Empty;
        _completed = null;
    }

    /// <summary>
    /// Offers a start point from a <c>chainsaw:</c> link. Nothing is written: with Nytt ärende shown it is added as a
    /// line there; with a case open it is put in an empty bar, for Enter to add; with none it fills in Nytt ärende.
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
            default:
                break;
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

        CommandBarWanted?.Invoke(this, EventArgs.Empty);
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

        OnPropertyChanged(nameof(CaseTitle));
    }

    /// <summary>Shows an answer above the bar.</summary>
    /// <param name="reply">The answer.</param>
    private void Answer(CommandReply reply)
    {
        CommandAnswer = reply.Text;
        IsAnswerError = reply.IsError;
    }

    /// <summary>Shows the new case: the graph, where the work starts, and the answer from the creation.</summary>
    /// <param name="reply">The creation's answer.</param>
    private void OnCreated(CommandReply reply)
    {
        Show(AppView.Graph);
        Answer(reply);
    }

    /// <summary>Shows the exit's state honestly: connected with its name, connecting, or not connected.</summary>
    /// <param name="status">The exit's status.</param>
    private void ShowExit(ExitStatus status)
    {
        IsExitConnected = status.State == ExitState.Connected;
        (ExitText, SourceText) = status.State switch
        {
            ExitState.Connected => ("Utgång ansluten", "Uppslag går genom utgången"),
            ExitState.Connecting => ("Ansluter …", OfflineText),
            ExitState.Failed => ("Utgången är inte ansluten", OfflineText),
            _ => (NoExitText, OfflineText),
        };
        if (IsExitConnected && status.ExitId is { } id)
        {
            _ = NameExitAsync(id);
        }
    }

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
                if (IsExitConnected)
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
