using System.Collections.ObjectModel;
using Chainsaw.App.Links;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel.Commands;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// Nytt ärende (sketch NC2a): the panel from the right that darkens the rest. The case number large, the title
/// optional, the start points as a list where each row fetches in advance through the exit what it can (see
/// <see cref="IStartPointPrefetch"/>), and *Kan öppnas av en kollega* with a sharing password. The sharing password is
/// never a property: it is taken from the two fields as characters when the case is created, given to Cases and
/// cleared.
/// </summary>
public sealed partial class NewCaseViewModel : ObservableObject
{
    /// <summary>The most start points at once; more are refused rather than silently dropped.</summary>
    public const int MaximumLines = CaseWorkflow.MaximumStartPoints;

    /// <summary>The longest text typed in the add row: no address or txid on any known chain is longer.</summary>
    public const int MaximumAddLength = ChainStartPoint.MaximumLength;

    /// <summary>The file name's ending.</summary>
    public const string FileExtension = ".cut";

    /// <summary>What the exit's tag says while no exit is live.</summary>
    public const string OfflineText = "Offline";

    /// <summary>The characters that part the lines of what is pasted.</summary>
    private static readonly char[] LineBreaks = ['\r', '\n'];

    /// <summary>The characters that part start points on one pasted line: tabs, spaces, commas and semicolons.</summary>
    private static readonly char[] Separators = ['\t', ' ', ',', ';'];

    /// <summary>Creates and opens the case.</summary>
    private readonly CaseWorkflow _workflow;

    /// <summary>Fetches the start points in advance; <see langword="null"/> where nothing is fetched.</summary>
    private readonly IStartPointPrefetch? _prefetch;

    /// <summary>Stops every lookup of the panel's current filling, when it is emptied or the case is created.</summary>
    private CancellationTokenSource _lookups = new();

    /// <summary>Stops the wait after typing.</summary>
    private CancellationTokenSource? _typing;

    /// <summary>The lookups under way, which the creation waits for once they are stopped.</summary>
    private readonly HashSet<Task> _inFlight = [];

    /// <summary>How Chainsaw says it is offline, as the way out last told (<see cref="ExitWay.Offline"/>).</summary>
    private OfflineWords _offline = OfflineWords.NoExit;

    /// <summary>Creates the panel, closed.</summary>
    /// <param name="workflow">Creates and opens the case.</param>
    /// <param name="prefetch">Fetches the start points in advance, or <see langword="null"/> for no lookups.</param>
    public NewCaseViewModel(CaseWorkflow workflow, IStartPointPrefetch? prefetch = null)
    {
        _workflow = workflow;
        _prefetch = prefetch;
        Rows.CollectionChanged += (_, _) => OnRowsChanged();
    }

    /// <summary>Raised when a case has been created and the panel has closed, with the answer to show.</summary>
    public event EventHandler<CommandReply>? Created;

    /// <summary>Gets whether the panel is shown.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    /// <summary>Gets or sets the case number.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNumber), nameof(IsNumberValid), nameof(FileName), nameof(IsNumberTaken))]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    public partial string CaseNumber { get; set; } = string.Empty;

    /// <summary>Gets or sets the title.</summary>
    [ObservableProperty]
    public partial string CaseTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets what is written in the add row, before it becomes a start point.</summary>
    [ObservableProperty]
    public partial string AddText { get; set; } = string.Empty;

    /// <summary>Gets the start points, one row each, in the order they were added.</summary>
    public ObservableCollection<StartPointRowViewModel> Rows { get; } = [];

    /// <summary>Gets or sets whether the case gets a sharing envelope (*Kan öppnas av en kollega*).</summary>
    [ObservableProperty]
    public partial bool IsShared { get; set; }

    /// <summary>Gets why the case was not created, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; private set; } = string.Empty;

    /// <summary>Gets whether an error is shown.</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>Gets whether the case is being created.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    public partial bool IsBusy { get; private set; }

    /// <summary>Gets whether lookups can go out now, as the shell last told.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExitChipText), nameof(IsOfflineNoteShown))]
    public partial bool IsExitLive { get; private set; }

    /// <summary>Gets whether Windows is the way out (the monitor in the exit's tag; a VPN exit has the shield).</summary>
    [ObservableProperty]
    public partial bool IsExitWindows { get; private set; }

    /// <summary>Gets the exit's name, as the title bar shows it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExitChipText))]
    public partial string ExitName { get; private set; } = string.Empty;

    /// <summary>Gets the small tag by the heading: <c>via Windows</c>, or <c>Offline</c>.</summary>
    public string ExitChipText => IsExitLive ? "via " + ExitName : OfflineText;

    /// <summary>Gets the tag's tooltip: what is sent and through what, or that nothing is.</summary>
    public string ExitChipTip => IsExitLive
        ? $"Uppslagen går genom {ExitName} och skickar bara adressen eller tx:en"
        : _offline.Text;

    /// <summary>Gets whether a case number is written.</summary>
    public bool HasNumber => CaseNumber.Trim().Length > 0;

    /// <summary>Gets whether the case number can be a file and no case has it yet (the check in the field).</summary>
    public bool IsNumberValid => CaseNumberRules.IsValid(CaseNumber.Trim()) && !IsNumberTaken;

    /// <summary>Gets whether a case in the folder already has the number.</summary>
    public bool IsNumberTaken => HasNumber && _workflow.Files.Any(file =>
        string.Equals(file.CaseNumber, CaseNumber.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Gets the file the case becomes: <c>2026-0117.cut</c>.</summary>
    public string FileName => CaseNumber.Trim() + FileExtension;

    /// <summary>Gets whether *Skapa ärende* can be pressed: a number is written and nothing runs.</summary>
    public bool CanCreate => HasNumber && !IsBusy;

    /// <summary>Gets whether there are start points.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>Gets how many start points there are, by the heading.</summary>
    public string RowCountText => Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets whether the line under the list says, once, that the rows are fetched when the exit is connected.
    /// </summary>
    public bool IsOfflineNoteShown => !IsExitLive && Rows.Any(row => row.CanFetch);

    /// <summary>
    /// Gets or sets how long typing must pause before a recognised value in the add row becomes a start point.
    /// </summary>
    public TimeSpan TypingPause { get; set; } = TimeSpan.FromMilliseconds(700);

    /// <summary>
    /// Shows the panel. A panel that is already shown keeps what is written, and a start point is added as a new row;
    /// otherwise the panel starts empty.
    /// </summary>
    /// <param name="startPoint">A start point to fill in, for example from a <c>chainsaw:</c> link.</param>
    /// <param name="caseNumber">A case number to fill in, for example from <c>/new 2026-0117</c>.</param>
    public void Show(string? startPoint = null, string? caseNumber = null)
    {
        if (!IsOpen)
        {
            Reset();
        }

        if (!string.IsNullOrWhiteSpace(caseNumber))
        {
            CaseNumber = caseNumber.Trim();
        }

        if (!string.IsNullOrWhiteSpace(startPoint))
        {
            Add(startPoint);
        }

        IsOpen = true;
    }

    /// <summary>
    /// Follows the way out as the title bar shows it. When lookups can go again, the rows that wait for the exit are
    /// fetched.
    /// </summary>
    /// <param name="isLive">Whether lookups can go out now.</param>
    /// <param name="isWindows">Whether Windows is the way out.</param>
    /// <param name="name">The exit's name, for example <c>Windows</c> or <c>Stockholm</c>.</param>
    /// <param name="offline">
    /// How Chainsaw says it is offline (<see cref="ExitWay.Offline"/>), in the tag's tooltip and on the rows that
    /// wait; no exit connected when <see langword="null"/>.
    /// </param>
    public void FollowExit(bool isLive, bool isWindows, string name, OfflineWords? offline = null)
    {
        var wasLive = IsExitLive;
        _offline = offline ?? OfflineWords.NoExit;
        (IsExitWindows, ExitName, IsExitLive) = (isWindows, name, isLive);
        OnPropertyChanged(nameof(ExitChipTip));
        foreach (var row in Rows)
        {
            row.FollowOffline(_offline.Waiting);
        }

        if (isLive && !wasLive)
        {
            foreach (var row in Rows.Where(row => row.WantsFetch))
            {
                Fetch(row);
            }
        }
    }

    /// <summary>
    /// Adds what was pasted (Ctrl+V): a line whose every part is an address or a tx gives a row per part, so that
    /// several lines, or a list with commas, add several. A line with anything else in it, such as running text with
    /// a file's SHA-256 that looks like a txid, is not looked up (principle 4): it is said, and can be written in the
    /// add row with Enter. When nothing is recognised the text goes into the add row, to be corrected.
    /// </summary>
    /// <param name="text">The text pasted.</param>
    public void Paste(string text)
    {
        var lines = (text ?? string.Empty).Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length > 0)
            .ToList();
        var clean = lines.Where(parts => parts.All(part => StartPointText.Parse(part).IsRecognised)).ToList();
        if (clean.Count == 0)
        {
            AddText = string.Join(' ', lines.SelectMany(parts => parts));
            return;
        }

        ErrorText = string.Empty;
        foreach (var part in clean.SelectMany(parts => parts))
        {
            Add(part);
        }

        var skipped = lines.Count - clean.Count;
        if (skipped > 0 && ErrorText.Length == 0)
        {
            ErrorText = skipped == 1
                ? "En rad med annan text än adresser och tx hämtades inte. Skriv in värdet och tryck Enter."
                : $"{skipped} rader med annan text än adresser och tx hämtades inte. Skriv in värdena och tryck Enter.";
        }
    }

    /// <summary>
    /// Makes what is written in the add row a start point (Enter), also when it is not recognised: the row then says
    /// so and can be removed.
    /// </summary>
    public void CommitAddText()
    {
        _typing?.Cancel();
        if (AddText.Trim().Length > 0)
        {
            Add(AddText);
            AddText = string.Empty;
        }
    }

    /// <summary>Removes a start point; its lookup stops.</summary>
    /// <param name="row">The row.</param>
    public void Remove(StartPointRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        row.Lookup?.Cancel();
        Rows.Remove(row);
        ErrorText = string.Empty;
    }

    /// <summary>Closes the panel without creating anything (Avbryt, Esc or ×); what was fetched is dropped.</summary>
    public void Cancel()
    {
        if (!IsBusy)
        {
            IsOpen = false;
            Reset();
        }
    }

    /// <summary>
    /// Closes the panel and empties it whatever runs, when Chainsaw locks; a creation that runs ends on its own, and
    /// a case it creates while locked is closed again by the workflow.
    /// </summary>
    public void Dismiss()
    {
        IsOpen = false;
        Reset();
    }

    /// <summary>
    /// Creates the case (Skapa ärende, Ctrl+Enter) after checking every field. The two password fields are read only
    /// when the case is to be shared, and are then always emptied and their characters cleared.
    /// </summary>
    /// <param name="password">The sharing password field.</param>
    /// <param name="repeat">The field where the sharing password is written again.</param>
    /// <returns>A task that completes when the case is created or the reason is shown.</returns>
    public async Task CreateAsync(ISecretEntry password, ISecretEntry repeat)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(repeat);
        if (IsBusy || !IsOpen)
        {
            return;
        }

        ErrorText = string.Empty;
        CommitRecognisedText();
        if (Check() is { } request)
        {
            var sharing = IsShared ? TakePassword(password, repeat) : null;
            if (!IsShared || sharing is not null)
            {
                await CreateAsync(request, sharing);
            }
        }
    }

    /// <summary>
    /// Waits for typing to pause, and then makes a recognised value in the add row a start point, so that a lookup
    /// never starts on every key (a value half written is not recognised, since every address and txid has a fixed
    /// form and most a checksum).
    /// </summary>
    /// <param name="value">The new text.</param>
    partial void OnAddTextChanged(string value)
    {
        _typing?.Cancel();
        if (!StartPointText.Parse(value).IsRecognised)
        {
            return;
        }

        _typing = new CancellationTokenSource();
        _ = AddAfterPauseAsync(value, _typing.Token);
    }

    /// <summary>Clears an old error as soon as the number changes, so that it never answers another number.</summary>
    /// <param name="value">The new number.</param>
    partial void OnCaseNumberChanged(string value) => ErrorText = string.Empty;

    /// <summary>Adds the value after the pause, unless more was typed meanwhile.</summary>
    /// <param name="value">The value.</param>
    /// <param name="cancellationToken">Stopped by more typing.</param>
    /// <returns>A task that completes when the value is added or the wait was stopped.</returns>
    private async Task AddAfterPauseAsync(string value, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TypingPause, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (AddText == value)
        {
            CommitAddText();
        }
    }

    /// <summary>Makes a recognised value left in the add row a start point before the case is created.</summary>
    private void CommitRecognisedText()
    {
        if (StartPointText.Parse(AddText).IsRecognised)
        {
            CommitAddText();
        }
    }

    /// <summary>
    /// Adds one start point as a row and fetches it, unless the same value is a row already or the list is full.
    /// </summary>
    /// <param name="text">The text.</param>
    private void Add(string text)
    {
        var startPoint = StartPointText.Parse(text);
        if (Rows.Any(row => row.StartPoint.Value == startPoint.Value))
        {
            return;
        }

        if (Rows.Count >= MaximumLines)
        {
            ErrorText = $"Högst {MaximumLines} startpunkter åt gången.";
            return;
        }

        var added = new StartPointRowViewModel(startPoint);
        added.FollowOffline(_offline.Waiting);
        Rows.Add(added);
        Fetch(added);
    }

    /// <summary>Starts a row's lookup; what comes back is shown only while the row is still in the list.</summary>
    /// <param name="row">The row.</param>
    private void Fetch(StartPointRowViewModel row)
    {
        if (_prefetch is null || !row.StartPoint.IsRecognised)
        {
            return;
        }

        row.Lookup?.Cancel();
        var lookup = CancellationTokenSource.CreateLinkedTokenSource(_lookups.Token);
        row.Lookup = lookup;
        if (IsExitLive && row.CanFetch)
        {
            row.Show(StartPointPreview.Only(PreviewState.Fetching, row.Preview.SanctionTip));
        }

        _inFlight.RemoveWhere(task => task.IsCompleted);
        _inFlight.Add(FetchAsync(_prefetch, row, IsExitLive, lookup));
    }

    /// <summary>Asks the prefetch and shows the answer, unless the row was removed or the lookup stopped.</summary>
    /// <param name="prefetch">The prefetch.</param>
    /// <param name="row">The row.</param>
    /// <param name="mayFetch">Whether lookups may go out.</param>
    /// <param name="lookup">Stops the lookup; disposed here when the lookup is over.</param>
    /// <returns>A task that completes when the answer is shown or dropped.</returns>
    private async Task FetchAsync(
        IStartPointPrefetch prefetch, StartPointRowViewModel row, bool mayFetch, CancellationTokenSource lookup)
    {
        var cancellationToken = lookup.Token;
        StartPointPreview preview;
        try
        {
            preview = await prefetch.PreviewAsync(row.StartPoint, mayFetch, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (row.Lookup == lookup)
            {
                row.Lookup = null;
            }

            lookup.Dispose();
        }

        if (!cancellationToken.IsCancellationRequested && Rows.Contains(row))
        {
            row.Show(preview);
            OnPropertyChanged(nameof(IsOfflineNoteShown));
        }
    }

    /// <summary>Tells what follows from the rows when one is added or removed.</summary>
    private void OnRowsChanged()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(RowCountText));
        OnPropertyChanged(nameof(IsOfflineNoteShown));
    }

    /// <summary>Checks the case number, the title and the start points.</summary>
    /// <returns>The request, or <see langword="null"/> when <see cref="ErrorText"/> says what is wrong.</returns>
    private NewCaseRequest? Check()
    {
        var number = CaseNumber.Trim();
        if (number.Length == 0)
        {
            ErrorText = "Skriv ett ärendenummer.";
            return null;
        }

        if (!CaseNumberRules.IsValid(number))
        {
            ErrorText = CaseTexts.InvalidCaseNumber;
            return null;
        }

        if (!CaseTitleRules.IsValid(CaseTitle))
        {
            ErrorText = CaseTexts.InvalidTitle;
            return null;
        }

        if (Rows.FirstOrDefault(row => !row.StartPoint.IsRecognised) is { } unknown)
        {
            ErrorText = $"{unknown.Short} känns inte igen som en adress eller en tx. Ta bort raden.";
            return null;
        }

        return new NewCaseRequest(
            number, CaseTitleRules.Normalize(CaseTitle), Rows.Select(row => row.StartPoint).ToList());
    }

    /// <summary>Takes the sharing password from its two fields and checks it; both fields are always emptied.</summary>
    /// <param name="passwordEntry">The password field.</param>
    /// <param name="repeatEntry">The repeat field.</param>
    /// <returns>The password, which the caller clears, or <see langword="null"/> when it is refused.</returns>
    private char[]? TakePassword(ISecretEntry passwordEntry, ISecretEntry repeatEntry)
    {
        var password = passwordEntry.Take();
        var repeat = repeatEntry.Take();
        try
        {
            if (password.Length < SharingPasswordRules.MinimumLength)
            {
                ErrorText = CaseTexts.For(CaseError.PasswordTooShort);
            }
            else if (!password.AsSpan().SequenceEqual(repeat))
            {
                ErrorText = "Ärendelösenorden är inte lika. Skriv samma lösenord två gånger.";
            }
            else
            {
                return password;
            }

            Secret.Clear(password);
            return null;
        }
        finally
        {
            Secret.Clear(repeat);
        }
    }

    /// <summary>
    /// Creates the case through the workflow and closes the panel when it exists. The lookups under way are stopped
    /// and waited for first, so that what is sealed in the case is exactly what was fetched and nothing comes in
    /// after; if the case is not created they start again.
    /// </summary>
    /// <param name="request">The checked request.</param>
    /// <param name="sharing">The sharing password, or <see langword="null"/>; cleared here.</param>
    /// <returns>A task that completes when the case is created or the reason is shown.</returns>
    private async Task CreateAsync(NewCaseRequest request, char[]? sharing)
    {
        IsBusy = true;
        StopLookups();
        CaseCreation creation;
        try
        {
            await Task.WhenAll([.. _inFlight]);
            _inFlight.Clear();
            creation = await _workflow.CreateAsync(request, sharing, _prefetch);
        }
        finally
        {
            if (sharing is not null)
            {
                Secret.Clear(sharing);
            }

            IsBusy = false;
        }

        if (creation.Exists || creation.IsCreated || !IsOpen)
        {
            // What was fetched is in the case now, or the case could not take it and said so, or the panel was
            // dismissed meanwhile (Chainsaw locked); either way it is done.
            _prefetch?.Forget();
        }

        if (creation.IsCreated)
        {
            IsOpen = false;
            Reset();
            Created?.Invoke(this, creation.Reply);
        }
        else if (IsOpen)
        {
            ErrorText = creation.Reply.Text;
            foreach (var row in Rows.Where(row => row.WantsFetch))
            {
                Fetch(row);
            }
        }
    }

    /// <summary>Stops every lookup under way and makes a new source of stops for the next.</summary>
    private void StopLookups()
    {
        _lookups.Cancel();
        _lookups.Dispose();
        _lookups = new CancellationTokenSource();
    }

    /// <summary>
    /// Empties every field, stops the lookups and drops what was fetched, unless a creation reads it now; the creation
    /// drops it then.
    /// </summary>
    private void Reset()
    {
        _typing?.Cancel();
        StopLookups();
        if (!IsBusy)
        {
            // While the case is created it reads what was fetched; the creation drops it when it is done.
            _prefetch?.Forget();
        }

        CaseNumber = string.Empty;
        CaseTitle = string.Empty;
        AddText = string.Empty;
        Rows.Clear();
        IsShared = false;
        ErrorText = string.Empty;
    }
}
