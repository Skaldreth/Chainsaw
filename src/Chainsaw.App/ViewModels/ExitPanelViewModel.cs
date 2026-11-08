using System.Collections.ObjectModel;
using Chainsaw.App.Exits;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The exits' panel under the exit in the title bar (sketch UT2b): the route from the computer through the exit to the
/// chains as a large picture, green while lookups can go and red and dashed while they cannot, and with VPN the exits
/// in the vault with their state and a button that connects or disconnects each (only one is connected at a time).
/// With Windows chosen as the way out (sketch ST1a) the route goes through Windows and no exits are shown. The
/// settings' page Nätverk shows the same exits through this view model, and only there is a WireGuard file added.
/// There each exit folds out (sketch UT3b) with its name and place to change, <em>Ta bort …</em>, and its check: the
/// address and country lookups are seen from, asked through the exit with <see cref="IExitCheck"/> on
/// <em>Kontrollera nu</em> and, when the setting is on, once each time an exit connects.
/// Everything that reaches the vault or the tunnel runs off the UI thread. It knows nothing of WPF.
/// </summary>
public sealed partial class ExitPanelViewModel : ObservableObject
{
    /// <summary>
    /// What the middle of the route says while no lookups can go: no exit is connected, or Windows is the way out and
    /// the computer has no network.
    /// </summary>
    public const string NoRouteText = "Offline";

    /// <summary>What the route says when lookups go through Windows.</summary>
    public const string WindowsRouteText = "Windows";

    /// <summary>What the panel says when the vault locked under it.</summary>
    public const string LockedText = "Valvet är låst. Lås upp och försök igen.";

    /// <summary>What the panel says when the exits could not be read.</summary>
    public const string ListFailedText = "Utgångarna gick inte att läsa.";

    /// <summary>What the panel says when an exit could not be chosen, such as one removed meanwhile.</summary>
    public const string ChooseFailedText = "Utgången gick inte att välja.";

    /// <summary>What the panel says when the exit could not be disconnected.</summary>
    public const string DisconnectFailedText = "Utgången gick inte att koppla ner.";

    /// <summary>What the step says when the chosen file could not be read.</summary>
    public const string ReadFailedText = "Filen gick inte att läsa.";

    /// <summary>What the step says when the exits' store refused the file.</summary>
    public const string RefusedText =
        "WireGuard-filen godtas inte. Den ska ha en rad DNS = med en IP-adress under [Interface].";

    /// <summary>What the step says when the exit could not be stored for another reason.</summary>
    public const string SaveFailedText = "Utgången gick inte att spara.";

    /// <summary>What a folded-out row says when its name is left empty.</summary>
    public const string NameNeededText = "Utgången behöver ett namn.";

    /// <summary>What a folded-out row says when its name or place could not be stored.</summary>
    public const string DescribeFailedText = "Namnet och platsen gick inte att spara.";

    /// <summary>What the page says when the exit could not be removed.</summary>
    public const string RemoveFailedText = "Utgången gick inte att ta bort.";

    /// <summary>The connection to the program's exit.</summary>
    private readonly IExitConnection _connection;

    /// <summary>The way out chosen in the settings.</summary>
    private readonly ExitWay _way;

    /// <summary>The exits in the vault.</summary>
    private readonly IExitStore _exits;

    /// <summary>Windows' file dialog.</summary>
    private readonly IFilePicker _picker;

    /// <summary>The check of the way out, or <see langword="null"/> where there is none.</summary>
    private readonly IExitCheck? _check;

    /// <summary>The toaster, which says when a check finds the exit in another country; none in some tests.</summary>
    private readonly Toaster? _toaster;

    /// <summary>The clock, so that today's answer says <em>i dag</em>.</summary>
    private readonly IClock _clock;

    /// <summary>The vault, so that nothing is asked on connecting while it is locked; none in some tests.</summary>
    private readonly IVault? _vault;

    /// <summary>The exit that was connected last, so that a check on connecting runs once per connection.</summary>
    private Guid? _connected;

    /// <summary>The row folded out, kept across a new read of the exits.</summary>
    private Guid? _expanded;

    /// <summary>
    /// Lets one connect at a time choose its exit and decide whether it still is the user's latest, so that two quick
    /// connects can never leave one exit chosen and the other connected.
    /// </summary>
    private readonly SemaphoreSlim _choosing = new(1, 1);

    /// <summary>The program's latest exit status.</summary>
    private ExitStatus _status;

    /// <summary>
    /// Counts the user's connects and disconnects, so that an older connect that is still choosing its exit does not
    /// connect after the user has asked for something else.
    /// </summary>
    private int _attempt;

    /// <summary>
    /// Cancels the connect that runs, when the user disconnects or connects another exit. Network cancels an attempt
    /// it has begun by itself; this also stops one that is queued but not yet begun.
    /// </summary>
    private CancellationTokenSource? _connecting;

    /// <summary>Counts the reads of the exits, so that an older read never replaces a newer one.</summary>
    private int _refresh;

    /// <summary>Counts the times the panel opened or closed, so a late answer is not shown after a close.</summary>
    private int _shown;

    /// <summary>The WireGuard file being added; only its path is kept, never its text.</summary>
    private string? _path;

    /// <summary>
    /// The last check of Windows as the way out while the computer has been online, for the title bar's tooltip;
    /// never kept beyond the session, since it belongs to no exit.
    /// </summary>
    private ExitSighting? _windowsSighting;

    /// <summary>
    /// Whether Windows as the way out has been asked since the computer came online, so that a check that failed is
    /// not asked again at every change; it is asked anew the next time the computer comes online.
    /// </summary>
    private bool _windowsAsked;

    /// <summary>Creates the panel and follows the connection's status.</summary>
    /// <param name="connection">The connection to the program's exit.</param>
    /// <param name="way">The way out chosen in the settings (VPN or Windows).</param>
    /// <param name="exits">The exits in the vault.</param>
    /// <param name="picker">Windows' file dialog.</param>
    /// <param name="ui">The UI thread.</param>
    /// <param name="check">The check of the way out (sketch UT3b); none where it cannot be.</param>
    /// <param name="toaster">
    /// The toaster, which says when an exit is seen in another country; none in some tests.
    /// </param>
    /// <param name="clock">The clock; the system's when <see langword="null"/>.</param>
    /// <param name="vault">
    /// The vault: an exit that connects while it is locked is not checked, since nothing is looked up then.
    /// </param>
    public ExitPanelViewModel(
        IExitConnection connection,
        ExitWay way,
        IExitStore exits,
        IFilePicker picker,
        IUiThread ui,
        IExitCheck? check = null,
        Toaster? toaster = null,
        IClock? clock = null,
        IVault? vault = null)
    {
        _connection = connection;
        _way = way;
        way.Changed += (_, _) => ShowRoute();
        _exits = exits;
        _picker = picker;
        _check = check;
        _toaster = toaster;
        _clock = clock ?? new SystemClock();
        _vault = vault;
        if (vault is not null)
        {
            // Unlocked, Windows as the way out may be checked; nothing is asked while Chainsaw is locked.
            vault.StateChanged += (_, _) => ui.Post(ShowRoute);
        }

        _status = connection.Status;
        connection.StatusChanged += (_, status) => ui.Post(() => Show(status));
        Show(_status);
    }

    /// <summary>Raised when the panel asks for the settings' page Nätverk, where the way out is chosen.</summary>
    public event EventHandler? SettingsWanted;

    /// <summary>Gets the exits in the vault, ordered by name.</summary>
    public ObservableCollection<ExitRowViewModel> Rows { get; } = [];

    /// <summary>Gets whether the panel is folded out.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    /// <summary>Gets whether the settings' page Nätverk shows the exits (sketch ST1a).</summary>
    [ObservableProperty]
    public partial bool IsInSettings { get; private set; }

    /// <summary>
    /// Gets whether the exits are the way out (VPN). With Windows they are toned and cannot be used here; the way out
    /// is changed in the settings.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWindowsMode))]
    public partial bool IsVpnMode { get; private set; } = true;

    /// <summary>Gets whether Windows is the way out, so that the list says where to change it.</summary>
    public bool IsWindowsMode => !IsVpnMode;

    /// <summary>Gets whether the settings' step that adds a file is shown in place of the list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListing))]
    public partial bool IsAdding { get; private set; }

    /// <summary>Gets whether the list of exits is shown.</summary>
    public bool IsListing => !IsAdding;

    /// <summary>
    /// Gets whether lookups can go now: through Windows while the computer has a network, or a connected exit (the
    /// route is green).
    /// </summary>
    [ObservableProperty]
    public partial bool IsLive { get; private set; }

    /// <summary>
    /// Gets the middle of the route: <see cref="WindowsRouteText"/>, the connected exit's name, or
    /// <see cref="NoRouteText"/>.
    /// </summary>
    [ObservableProperty]
    public partial string RouteText { get; private set; } = NoRouteText;

    /// <summary>Gets what went wrong last, in the error colour; empty when nothing did.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; private set; } = string.Empty;

    /// <summary>Gets whether something went wrong.</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>
    /// Gets what the exit's tooltip in the title bar adds about the way out while lookups can go (sketch UT3b), for
    /// example <em>syns som 185.213.154.68, Sverige</em>; empty when the way has not been checked.
    /// </summary>
    [ObservableProperty]
    public partial string SeenTip { get; private set; } = string.Empty;

    /// <summary>Gets whether exits can be checked here (sketch UT3b).</summary>
    public bool CanCheck => _check is not null;

    /// <summary>
    /// Gets or sets whether an exit is checked once each time it connects (<em>Kontrollera vid anslutning</em>);
    /// the settings set it from their file.
    /// </summary>
    public bool ChecksOnConnect { get; set; } = true;

    /// <summary>Gets whether the vault has no exits, once they have been read.</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    /// <summary>Gets whether a file is being read or stored.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial bool IsBusy { get; private set; }

    /// <summary>Gets the name of the file being added, without its folder.</summary>
    [ObservableProperty]
    public partial string FileName { get; private set; } = string.Empty;

    /// <summary>Gets what the check of the file found: <em>Giltig</em> or what is missing.</summary>
    [ObservableProperty]
    public partial string CheckText { get; private set; } = string.Empty;

    /// <summary>Gets whether the file passed the check.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial bool IsFileValid { get; private set; }

    /// <summary>Gets or sets the name the new exit is stored with.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string NewName { get; set; } = string.Empty;

    /// <summary>Gets whether the new exit can be stored: a file that passed and a name.</summary>
    public bool CanSave => IsFileValid && !IsBusy && !string.IsNullOrWhiteSpace(NewName);

    /// <summary>Folds the panel out, reading the exits anew, or in when it is out (a click on the exit).</summary>
    [RelayCommand]
    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
            return;
        }

        _shown++;
        IsOpen = true;
        ErrorText = string.Empty;
        _ = RefreshAsync();
    }

    /// <summary>
    /// Folds the panel in (Esc, a click outside, a lock). The settings' step that adds a file is left alone, since it
    /// does not belong to the panel.
    /// </summary>
    [RelayCommand]
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        if (!IsInSettings)
        {
            _shown++;
            ErrorText = string.Empty;
        }
    }

    /// <summary>
    /// Connects a row's exit, or disconnects it when it is connected or being connected (the power button). Connecting
    /// chooses the exit first, which disconnects any other, so that only one is connected at a time. A failure to
    /// connect is shown on the row by the connection's status, not thrown.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>A task that completes when the attempt has ended.</returns>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task PowerAsync(ExitRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!IsVpnMode && !row.IsOn)
        {
            // With Windows chosen an exit is not connected here; the way out is changed in the settings.
            return Task.CompletedTask;
        }

        return row.IsOn ? DisconnectAsync() : ConnectAsync(row);
    }

    /// <summary>
    /// <em>Inställningar för anslutning</em>: asks for the settings' page Nätverk, where the way out is chosen and a
    /// WireGuard file is added, and folds the panel in.
    /// </summary>
    [RelayCommand]
    public void OpenSettings()
    {
        Close();
        SettingsWanted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows the exits on the settings' page Nätverk, reading them anew.</summary>
    /// <returns>A task that completes when the rows are shown, or why they are not.</returns>
    public Task ShowInSettingsAsync()
    {
        _shown++;
        IsInSettings = true;
        ErrorText = string.Empty;
        return RefreshAsync();
    }

    /// <summary>Leaves the settings' page: the step that adds a file is left and its file forgotten.</summary>
    public void LeaveSettings()
    {
        if (!IsInSettings)
        {
            return;
        }

        _shown++;
        IsInSettings = false;
        _expanded = null;
        foreach (var row in Rows)
        {
            row.Expand(false);
        }

        CancelAdd();
        ErrorText = string.Empty;
    }

    /// <summary>
    /// <em>Lägg till WireGuard-fil …</em> on the settings' page Nätverk: asks for a file with Windows' dialog, checks
    /// it off the UI thread and shows the step with its name and the check. The file's text is cleared as soon as it is
    /// checked. The panel under the title bar has no such step (sketch UT2b).
    /// </summary>
    /// <returns>A task that completes when the step is shown, or the reason it is not.</returns>
    [RelayCommand]
    public async Task AddFileAsync()
    {
        if (IsBusy || !IsVpnMode || !IsInSettings || _picker.PickWireGuardFile() is not { } path)
        {
            return;
        }

        var shown = _shown;
        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            var verdict = await Task.Run(() => CheckFile(path));
            if (shown != _shown || !IsInSettings)
            {
                return;
            }

            _path = path;
            FileName = Path.GetFileName(path);
            NewName = ExitNames.FromFile(path);
            IsFileValid = verdict == WireGuardVerdict.Valid;
            CheckText = WireGuardCheck.TextFor(verdict);
            IsAdding = true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (shown == _shown)
            {
                ErrorText = ReadFailedText;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// <em>Spara</em>: reads and checks the file again, since it may have changed, and stores it encrypted in the vault
    /// under its name. The text becomes a string only for <see cref="IExitStore.Add"/> and its characters are cleared
    /// at once. The new exit is not chosen, so that a connected exit stays connected.
    /// </summary>
    /// <returns>A task that completes when the exit is stored, or the reason it is not is shown.</returns>
    [RelayCommand]
    public async Task SaveAsync()
    {
        if (!CanSave || _path is not { } path)
        {
            return;
        }

        var shown = _shown;
        var name = NewName.Trim();
        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            var verdict = await Task.Run(() => StoreFile(path, name));
            if (shown != _shown)
            {
                return;
            }

            if (verdict != WireGuardVerdict.Valid)
            {
                IsFileValid = false;
                CheckText = WireGuardCheck.TextFor(verdict);
                return;
            }

            CancelAdd();
            await RefreshAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (shown == _shown)
            {
                ErrorText = TextFor(exception);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Leaves the step that adds a file and forgets the file (<em>Avbryt</em>).</summary>
    [RelayCommand]
    public void CancelAdd()
    {
        _path = null;
        IsAdding = false;
        FileName = string.Empty;
        CheckText = string.Empty;
        IsFileValid = false;
        NewName = string.Empty;
    }

    /// <summary>Reads the exits and the selected one off the UI thread, and shows them with their state.</summary>
    /// <returns>A task that completes when the rows are shown, or why they are not.</returns>
    public async Task RefreshAsync()
    {
        var shown = _shown;
        var refresh = ++_refresh;
        try
        {
            var (exits, selected, sightings) = await Task.Run(ReadExits);
            if (shown != _shown || refresh != _refresh)
            {
                return;
            }

            var checking = Rows.Where(row => row.IsChecking).Select(row => row.Id).ToHashSet();
            Rows.Clear();
            var now = _clock.UtcNow;
            foreach (var exit in exits)
            {
                var row = new ExitRowViewModel(exit) { IsChosen = exit.Id == selected };
                row.Show(_status);
                row.ShowSighting(sightings.GetValueOrDefault(exit.Id), now);
                if (checking.Contains(exit.Id))
                {
                    row.StartChecking();
                }

                row.Expand(IsInSettings && exit.Id == _expanded);
                Rows.Add(row);
            }

            IsEmpty = Rows.Count == 0;
            ShowRoute();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (shown == _shown && refresh == _refresh)
            {
                ErrorText = exception is VaultException ? LockedText : ListFailedText;
            }
        }
    }

    /// <summary>
    /// Folds a row out on the settings' page with its name and place in the fields, and the others in; or folds it in
    /// when it is out. A row folded in keeps what it showed.
    /// </summary>
    /// <param name="row">The row.</param>
    [RelayCommand]
    public void ToggleExpand(ExitRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var expand = !row.IsExpanded;
        foreach (var other in Rows)
        {
            other.Expand(expand && other == row);
        }

        _expanded = expand ? row.Id : null;
        ErrorText = string.Empty;
    }

    /// <summary>
    /// Stores the folded-out row's name and place (a field is left, a country chosen, or Enter), when they changed. An
    /// empty name is not stored.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>A task that completes when they are stored, or why not is shown.</returns>
    [RelayCommand]
    public async Task DescribeAsync(ExitRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var name = row.EditName.Trim();
        var place = ExitPlace.Compose(row.EditCountry, row.EditCity);
        if (name.Length == 0)
        {
            ErrorText = NameNeededText;
            return;
        }

        if (name.Length > ExitRules.MaximumNameLength)
        {
            name = name[..ExitRules.MaximumNameLength].Trim();
        }

        ErrorText = string.Empty;
        if (name == row.Name && place == row.Place)
        {
            return;
        }

        var shown = _shown;
        try
        {
            var exit = await Task.Run(() => _exits.DescribeAsync(row.Id, name, place));
            row.Describe(exit);
            ShowRoute();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (shown == _shown)
            {
                ErrorText = exception is VaultException ? LockedText : DescribeFailedText;
            }
        }
    }

    /// <summary><em>Ta bort utgången …</em>: asks first, under the folded-out row.</summary>
    /// <param name="row">The row.</param>
    [RelayCommand]
    public void AskRemove(ExitRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        row.AskRemove(true);
    }

    /// <summary><em>Avbryt</em> (or Esc): the exit stays.</summary>
    /// <param name="row">The row.</param>
    [RelayCommand]
    public void CancelRemove(ExitRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        row.AskRemove(false);
    }

    /// <summary>
    /// <em>Ta bort</em>, once asked: removes the exit, its WireGuard file and its check from the vault, off the UI
    /// thread; a connected exit is disconnected first by Network. The cases and what was fetched through it stay.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>A task that completes when the exits are shown again, or why not is shown.</returns>
    [RelayCommand]
    public async Task RemoveAsync(ExitRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.IsRemoving)
        {
            return;
        }

        ErrorText = string.Empty;
        try
        {
            await Task.Run(() => _exits.RemoveAsync(row.Id));
            _expanded = null;
            await RefreshAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ErrorText = exception is VaultException ? LockedText : RemoveFailedText;
        }
    }

    /// <summary>
    /// <em>Kontrollera nu</em>: asks the trace through the connected exit which address and country lookups are seen
    /// from. Nothing about a case is sent, and the answer is kept with the exit in the vault, never in a case.
    /// </summary>
    /// <param name="row">The row; only a connected exit can be checked.</param>
    /// <returns>A task that completes when the answer, or that none came, is shown.</returns>
    [RelayCommand]
    public Task CheckNowAsync(ExitRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.CanCheck ? CheckAsync(row, warn: false) : Task.CompletedTask;
    }

    /// <summary>
    /// Asks the trace through the exit and shows the answer on the row; when it leaves through another exit than the
    /// row's meanwhile, the row is not given that answer.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="warn">Whether a country other than the place says is told in the toaster.</param>
    /// <returns>A task that completes when the answer, or that none came, is shown.</returns>
    private async Task CheckAsync(ExitRowViewModel row, bool warn)
    {
        if (_check is not { } check)
        {
            return;
        }

        row.StartChecking();
        try
        {
            var sighting = await Task.Run(() => check.CheckAsync());
            var target = Rows.FirstOrDefault(other => other.Id == row.Id) ?? row;
            if (sighting.ExitId != row.Id)
            {
                target.ShowCheckFailed();
                return;
            }

            target.ShowSighting(sighting, _clock.UtcNow);
            if (warn && target.Check == ExitCheckState.Mismatch)
            {
                _toaster?.Show(ToastKind.Warning, ExitCheckWords.MismatchTitle,
                    ExitCheckWords.MismatchText(target.Name, sighting, target.Place));
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            (Rows.FirstOrDefault(other => other.Id == row.Id) ?? row).ShowCheckFailed();
        }
        finally
        {
            ShowSeenTip();
        }
    }

    /// <summary>
    /// Checks Windows as the way out once each time the computer comes online with Windows chosen, when checks on
    /// connecting are on (sketch UT3b, <em>datorns eget nät</em>). The answer is kept only for the session, since it
    /// belongs to no exit; it shows in the title bar's tooltip. Nothing is asked while Chainsaw is locked.
    /// </summary>
    private void CheckWindows()
    {
        if (!_way.IsWindows || !IsLive)
        {
            // The computer's address may change with its network; the next time it is online it is checked anew.
            _windowsSighting = null;
            _windowsAsked = false;
            return;
        }

        if (_windowsAsked || !ChecksOnConnect || _check is not { } check || IsLocked)
        {
            return;
        }

        _windowsAsked = true;
        WindowsChecked = CheckWindowsAsync(check);
    }

    /// <summary>Asks the trace through Windows and keeps the answer for the tooltip, if it went out that way.</summary>
    /// <param name="check">The check.</param>
    /// <returns>A task that completes when the answer, or that none came, is kept.</returns>
    private async Task CheckWindowsAsync(IExitCheck check)
    {
        try
        {
            var sighting = await Task.Run(() => check.CheckAsync());
            if (sighting.Route == ExitRoute.Windows && _way.IsWindows && IsLive)
            {
                _windowsSighting = sighting;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Without an answer the tooltip says only where lookups go; the lookups go through Windows all the same.
        }
        finally
        {
            ShowSeenTip();
        }
    }

    /// <summary>
    /// Gets the last check of Windows as the way out, which runs off the UI thread when the computer comes online;
    /// complete when none runs.
    /// </summary>
    public Task WindowsChecked { get; private set; } = Task.CompletedTask;

    /// <summary>Says in the title bar's tooltip which address the way out is seen as, when it is known.</summary>
    private void ShowSeenTip()
    {
        var sighting = (IsLive, _way.IsWindows) switch
        {
            (false, _) => null,
            (true, true) => _windowsSighting,
            (true, false) => Rows.FirstOrDefault(row => row.Id == _status.ExitId)?.Sighting,
        };
        SeenTip = sighting is null ? string.Empty : ExitCheckWords.TipText(sighting, _clock.UtcNow);
    }

    /// <summary>Reads the exits, the selected one and each exit's latest check, off the UI thread.</summary>
    /// <returns>What was read.</returns>
    private (IReadOnlyList<ExitInfo> Exits, Guid? Selected, Dictionary<Guid, ExitSighting> Sightings) ReadExits()
    {
        var exits = _exits.List();
        var selected = _exits.FindSelected()?.Id;
        var sightings = new Dictionary<Guid, ExitSighting>();
        foreach (var exit in exits)
        {
            if (_check?.FindLast(exit.Id) is { } sighting)
            {
                sightings[exit.Id] = sighting;
            }
        }

        return (exits, selected, sightings);
    }

    /// <summary>
    /// Chooses the row's exit and connects it, off the UI thread. A connect that the user has since replaced with
    /// another connect or a disconnect stops before it connects.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>A task that completes when the attempt has ended.</returns>
    private async Task ConnectAsync(ExitRowViewModel row)
    {
        var attempt = ++_attempt;
        CancellationToken token;
        ErrorText = string.Empty;
        try
        {
            await _choosing.WaitAsync();
            try
            {
                if (attempt != _attempt)
                {
                    return;
                }

                await Task.Run(() => _exits.SelectAsync(row.Id));
                foreach (var other in Rows)
                {
                    other.IsChosen = other.Id == row.Id;
                }

                if (attempt != _attempt)
                {
                    return;
                }

                var replaced = _connecting;
                _connecting = new CancellationTokenSource();
                token = _connecting.Token;
                replaced?.Cancel();
                replaced?.Dispose();
            }
            finally
            {
                _choosing.Release();
            }

            // The gate is not held while the tunnel starts: a later connect or a disconnect cancels this one.
            await Task.Run(() => _connection.ConnectAsync(token), token);
        }
        catch (OperationCanceledException)
        {
            // Replaced by a disconnect or another connect; the status shows where it stands.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (attempt == _attempt)
            {
                ErrorText = exception is VaultException ? LockedText : ChooseFailedText;
            }
        }
    }

    /// <summary>Disconnects the exit, or stops an attempt to connect, off the UI thread.</summary>
    /// <returns>A task that completes when the tunnel is stopped.</returns>
    private async Task DisconnectAsync()
    {
        ++_attempt;
        _connecting?.Cancel();
        ErrorText = string.Empty;
        try
        {
            await Task.Run(_connection.DisconnectAsync);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ErrorText = DisconnectFailedText;
        }
    }

    /// <summary>Shows the program's exit status on every row and in the route.</summary>
    /// <param name="status">The status.</param>
    private void Show(ExitStatus status)
    {
        _status = status;
        foreach (var row in Rows)
        {
            row.Show(status);
        }

        ShowRoute();
        CheckOnConnect(status);
    }

    /// <summary>
    /// Checks an exit once when it has connected, when the setting is on (<em>Kontrollera vid anslutning</em>); a
    /// country other than its place says is told in the toaster.
    /// </summary>
    /// <param name="status">The program's exit status.</param>
    private void CheckOnConnect(ExitStatus status)
    {
        var connected = status.State == ExitState.Connected ? status.ExitId : null;
        var isNew = connected is not null && connected != _connected;
        _connected = connected;
        if (!isNew || !ChecksOnConnect || _check is null || _way.IsWindows || IsLocked)
        {
            return;
        }

        var row = Rows.FirstOrDefault(other => other.Id == connected) ?? new ExitRowViewModel(
            new ExitInfo(connected!.Value, ExitRowViewModel.ConnectedText, string.Empty, ExitKind.WireGuard));
        _ = CheckOnConnectAsync(row);
    }

    /// <summary>Gets whether the vault is locked, when it is known; nothing is asked or kept then.</summary>
    private bool IsLocked => _vault is not null && _vault.State != VaultState.Unlocked;

    /// <summary>Checks an exit that has just connected, reading its place first when it has no row.</summary>
    /// <param name="row">The exit's row, or a stand-in when the exits have not been read.</param>
    /// <returns>A task that completes when the check is done.</returns>
    private async Task CheckOnConnectAsync(ExitRowViewModel row)
    {
        if (!Rows.Contains(row))
        {
            try
            {
                var exit = await Task.Run(() => _exits.List().FirstOrDefault(other => other.Id == row.Id));
                if (exit is not null)
                {
                    row.Describe(exit);
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Without the place there is nothing to compare with; the check still runs.
            }
        }

        await CheckAsync(row, warn: true);
    }

    /// <summary>
    /// Makes the route green through Windows while the computer has a network or with the connected exit's name, or
    /// red and dashed with <em>Offline</em>.
    /// </summary>
    private void ShowRoute()
    {
        IsVpnMode = !_way.IsWindows;
        if (_way.IsWindows)
        {
            IsLive = _way.IsOnline;
            RouteText = IsLive ? WindowsRouteText : NoRouteText;
        }
        else
        {
            IsLive = _status.State == ExitState.Connected;
            var connected = IsLive ? Rows.FirstOrDefault(row => row.Id == _status.ExitId) : null;
            RouteText = connected?.Name ?? (IsLive ? ExitRowViewModel.ConnectedText : NoRouteText);
        }

        CheckWindows();
        ShowSeenTip();
    }

    /// <summary>Reads and checks a WireGuard file, and clears its text.</summary>
    /// <param name="path">The file.</param>
    /// <returns>What the check found.</returns>
    private static WireGuardVerdict CheckFile(string path)
    {
        var text = WireGuardFile.Read(path);
        if (text is null)
        {
            return WireGuardVerdict.TooLarge;
        }

        try
        {
            return WireGuardCheck.Check(text);
        }
        finally
        {
            Secret.Clear(text);
        }
    }

    /// <summary>Reads, checks and stores a WireGuard file in the vault, and clears its text.</summary>
    /// <param name="path">The file.</param>
    /// <param name="name">The exit's name.</param>
    /// <returns>What the check found; the file is stored only when it is valid.</returns>
    private WireGuardVerdict StoreFile(string path, string name)
    {
        var text = WireGuardFile.Read(path);
        if (text is null)
        {
            return WireGuardVerdict.TooLarge;
        }

        try
        {
            var verdict = WireGuardCheck.Check(text);
            if (verdict == WireGuardVerdict.Valid)
            {
                _exits.Add(name, string.Empty, ExitKind.WireGuard, new string(text));
            }

            return verdict;
        }
        finally
        {
            Secret.Clear(text);
        }
    }

    /// <summary>Gives the words for a failure to store a file.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns>The words.</returns>
    private static string TextFor(Exception exception) => exception switch
    {
        VaultException => LockedText,
        ArgumentException => RefusedText,
        IOException or UnauthorizedAccessException => ReadFailedText,
        _ => SaveFailedText,
    };
}
