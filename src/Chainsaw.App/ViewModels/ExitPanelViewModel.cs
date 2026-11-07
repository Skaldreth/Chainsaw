using System.Collections.ObjectModel;
using Chainsaw.App.Exits;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The exits' panel under the exit in the title bar (sketch UT1a): the route from the computer through the exit to the
/// sources, green while lookups can go and red and dashed while they cannot, the exits in the vault with their state,
/// a button that connects or disconnects each (only one is connected at a time), and the step that adds a WireGuard
/// file. Everything that reaches the vault or the tunnel runs off the UI thread. It knows nothing of WPF.
/// </summary>
public sealed partial class ExitPanelViewModel : ObservableObject
{
    /// <summary>What the route says in place of an exit while none is connected.</summary>
    public const string NoRouteText = "Ingen";

    /// <summary>What the panel says when the vault locked under it.</summary>
    public const string LockedText = "Valvet är låst. Lås upp och försök igen.";

    /// <summary>What the panel says when the exits could not be read.</summary>
    public const string ListFailedText = "Utgångarna gick inte att läsa.";

    /// <summary>What the panel says when an exit could not be chosen, for example if it was removed meanwhile.</summary>
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

    /// <summary>The connection to the program's exit.</summary>
    private readonly IExitConnection _connection;

    /// <summary>The exits in the vault.</summary>
    private readonly IExitStore _exits;

    /// <summary>Windows' file dialog.</summary>
    private readonly IFilePicker _picker;

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

    /// <summary>Counts the times the panel opened or closed, so that a late answer is not shown after a close.</summary>
    private int _shown;

    /// <summary>The WireGuard file being added; only its path is kept, never its text.</summary>
    private string? _path;

    /// <summary>Creates the panel and follows the connection's status.</summary>
    /// <param name="connection">The connection to the program's exit.</param>
    /// <param name="exits">The exits in the vault.</param>
    /// <param name="picker">Windows' file dialog.</param>
    /// <param name="ui">The UI thread.</param>
    public ExitPanelViewModel(IExitConnection connection, IExitStore exits, IFilePicker picker, IUiThread ui)
    {
        _connection = connection;
        _exits = exits;
        _picker = picker;
        _status = connection.Status;
        connection.StatusChanged += (_, status) => ui.Post(() => Show(status));
        Show(_status);
    }

    /// <summary>Gets the exits in the vault, ordered by name.</summary>
    public ObservableCollection<ExitRowViewModel> Rows { get; } = [];

    /// <summary>Gets whether the panel is folded out.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    /// <summary>Gets whether the step that adds a file is shown in place of the list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListing))]
    public partial bool IsAdding { get; private set; }

    /// <summary>Gets whether the list of exits is shown.</summary>
    public bool IsListing => !IsAdding;

    /// <summary>Gets whether lookups can go now: an exit is connected (the route is green).</summary>
    [ObservableProperty]
    public partial bool IsLive { get; private set; }

    /// <summary>Gets the exit in the route: the connected exit's name, or <see cref="NoRouteText"/>.</summary>
    [ObservableProperty]
    public partial string RouteText { get; private set; } = NoRouteText;

    /// <summary>Gets what went wrong last, in the error colour; empty when nothing did.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; private set; } = string.Empty;

    /// <summary>Gets whether something went wrong.</summary>
    public bool HasError => ErrorText.Length > 0;

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

    /// <summary>Folds the panel in and leaves the step that adds a file (Esc, a click outside, a lock).</summary>
    [RelayCommand]
    public void Close()
    {
        if (!IsOpen && !IsAdding)
        {
            return;
        }

        _shown++;
        IsOpen = false;
        CancelAdd();
        ErrorText = string.Empty;
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
        return row.IsOn ? DisconnectAsync() : ConnectAsync(row);
    }

    /// <summary>
    /// <em>Lägg till WireGuard-fil …</em>: asks for a file with Windows' dialog, checks it off the UI thread and
    /// shows the step with its name and the check. The file's text is cleared as soon as it is checked.
    /// </summary>
    /// <returns>A task that completes when the step is shown, or the reason it is not.</returns>
    [RelayCommand]
    public async Task AddFileAsync()
    {
        if (IsBusy || _picker.PickWireGuardFile() is not { } path)
        {
            return;
        }

        var shown = _shown;
        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            var verdict = await Task.Run(() => CheckFile(path));
            if (shown != _shown || !IsOpen)
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorText = ReadFailedText;
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
        try
        {
            var (exits, selected) = await Task.Run(() => (_exits.List(), _exits.FindSelected()?.Id));
            if (shown != _shown)
            {
                return;
            }

            Rows.Clear();
            foreach (var exit in exits)
            {
                var row = new ExitRowViewModel(exit) { IsChosen = exit.Id == selected };
                row.Show(_status);
                Rows.Add(row);
            }

            IsEmpty = Rows.Count == 0;
            ShowRoute();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (shown == _shown)
            {
                ErrorText = exception is VaultException ? LockedText : ListFailedText;
            }
        }
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
            }
            finally
            {
                _choosing.Release();
            }

            // A later choice of another exit cancels this attempt in Network, so the gate is not held while it runs.
            await Task.Run(() => _connection.ConnectAsync());
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
    }

    /// <summary>Makes the route green with the connected exit's name, or red and dashed with <em>Ingen</em>.</summary>
    private void ShowRoute()
    {
        IsLive = _status.State == ExitState.Connected;
        var connected = IsLive ? Rows.FirstOrDefault(row => row.Id == _status.ExitId) : null;
        RouteText = connected?.Name ?? (IsLive ? "Ansluten" : NoRouteText);
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
