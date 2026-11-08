using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The view Etiketter (sketch ET1a): every label in the case in one dense list, with the sources as filters to the
/// left, the sanction hits in the graph gathered at the top in red, and the chosen label to the right with each of its
/// origins (who, when, why and which case), <em>Sprid inte</em>, <em>Ta tillbaka</em> and <em>Visa i grafen</em> (G).
/// A switch shows the label library instead, with the case numbers of each label. What is written goes through
/// Attribution, sealed with a line in the journal as in the inspector (principle 1), and every label is shown as a
/// judgement with its origin (principle 2). The case is read off the UI thread (principle 6). No WPF, so that it is
/// tested in Chainsaw.App.Tests.
/// </summary>
public sealed partial class LabelsViewModel : ObservableObject
{
    /// <summary>What the list says while the case has no labels.</summary>
    public const string EmptyText =
        "Inga etiketter i ärendet än. En etikett sätts på en nod i grafen (L); sanktionslistan, underleverantörerna "
        + "och biblioteket lägger till sina när grafen hämtas.";

    /// <summary>What the list says while the library has nothing.</summary>
    public const string LibraryEmptyText = "Biblioteket är tomt. Det fylls med etiketterna ur de ärenden du öppnar.";

    /// <summary>What the list says when the filters or the filter field hide every label.</summary>
    public const string FilteredText = "Inga etiketter syns med filtren som är valda.";

    /// <summary>What is said when the library cannot be read, for example while the vault is locked.</summary>
    public const string LibraryClosedText = "Biblioteket kan inte läsas just nu.";

    /// <summary>The toast's title after a label was taken back.</summary>
    public const string RetractedTitle = "Etiketten är tagen tillbaka";

    /// <summary>The toast's text after a label was taken back.</summary>
    public const string RetractedDetail = "Också i biblioteket, med en ny förseglad post.";

    /// <summary>
    /// What the toast adds when the row also has a copy from another case: the copy is that case's own and stays.
    /// </summary>
    public const string CopyStaysText = "Kopian från det andra ärendet står kvar.";

    /// <summary>What the list says when the case's labels could not be read.</summary>
    public const string ReadFailedText = "Etiketterna kunde inte läsas: {0}";

    /// <summary>The toast's title after a label was marked <em>Sprid inte</em>.</summary>
    public const string NoSpreadTitle = "Etiketten sprids inte";

    /// <summary>The toast's text after a label was marked <em>Sprid inte</em>: honest about the copies.</summary>
    public const string NoSpreadDetail =
        "Den stannar i ärendet och lämnar biblioteket. Kopior som redan gått till andra ärenden ligger kvar där.";

    /// <summary>The toast's title after a label was allowed to spread again.</summary>
    public const string SpreadTitle = "Etiketten får spridas";

    /// <summary>The toast's text after a label was allowed to spread again.</summary>
    public const string SpreadDetail = "Den går till biblioteket och kan följa med till andra ärenden.";

    /// <summary>The toast's title when a change could not be sealed.</summary>
    public const string FailedTitle = "Etiketten kunde inte ändras";

    /// <summary>The labels in the case.</summary>
    private readonly IAttributionService _attribution;

    /// <summary>The label library on this computer.</summary>
    private readonly ILabelLibrary _library;

    /// <summary>The imported sanction lists, for what the list's source says.</summary>
    private readonly ISanctionLists _sanctions;

    /// <summary>Where a label is taken back or marked, as the inspector does it.</summary>
    private readonly IGraphEditor _editor;

    /// <summary>The open case.</summary>
    private readonly CaseSession _session;

    /// <summary>The UI thread, where what is read off it is shown.</summary>
    private readonly IUiThread _ui;

    /// <summary>The toaster, which says what a change did; or <see langword="null"/>.</summary>
    private readonly Toaster? _toaster;

    /// <summary>The zone local times are written in.</summary>
    private readonly TimeZoneInfo _zone;

    /// <summary>The suppliers' names, as the source of their labels is called.</summary>
    private readonly string _providers;

    /// <summary>The graph shown, or <see langword="null"/>.</summary>
    private GraphScene? _scene;

    /// <summary>What was last read from the case and the library.</summary>
    private LabelReading _reading = LabelReading.Empty;

    /// <summary>The rows of the case, as last built.</summary>
    private IReadOnlyList<LabelRowViewModel> _caseRows = [];

    /// <summary>The rows of the library, as last built.</summary>
    private IReadOnlyList<LabelRowViewModel> _libraryRows = [];

    /// <summary>Why the case's labels could not be read, or <see langword="null"/>.</summary>
    private string? _problem;

    /// <summary>Counts the readings, so that only the latest one is shown.</summary>
    private int _readings;

    /// <summary>Creates the view.</summary>
    /// <param name="attribution">The labels in the case.</param>
    /// <param name="library">The label library.</param>
    /// <param name="sanctions">The imported sanction lists.</param>
    /// <param name="providers">The suppliers of attribution, for their names.</param>
    /// <param name="editor">Where a label is taken back or marked.</param>
    /// <param name="session">The open case.</param>
    /// <param name="ui">The UI thread.</param>
    /// <param name="toaster">The toaster, or <see langword="null"/>.</param>
    /// <param name="zone">The zone local times are written in; the computer's own when <see langword="null"/>.</param>
    public LabelsViewModel(
        IAttributionService attribution,
        ILabelLibrary library,
        ISanctionLists sanctions,
        IAttributionProviders providers,
        IGraphEditor editor,
        CaseSession session,
        IUiThread ui,
        Toaster? toaster = null,
        TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _attribution = attribution;
        _library = library;
        _sanctions = sanctions;
        _editor = editor;
        _session = session;
        _ui = ui;
        _toaster = toaster;
        _zone = zone ?? TimeZoneInfo.Local;
        _providers = providers.Providers.Count == 0
            ? "Underleverantörer"
            : string.Join(", ", providers.Providers.Select(provider => provider.DisplayName));
        Sources = [.. LabelFilters.All.Select(filter =>
            new LabelSourceViewModel(filter, LabelFilters.ColumnName(filter, _providers)))];
        session.Changed += (_, _) => OnCaseChanged();
        OnCaseChanged();
    }

    /// <summary>Raised when <em>Visa i grafen</em> asks for a node to be chosen and shown in the graph.</summary>
    public event EventHandler<string>? ShowInGraphWanted;

    /// <summary>Raised when a label was changed in the case, so that the graph is read again.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when <em>Varför?</em> asks for the Guide's section on labels.</summary>
    public event EventHandler? WhyWanted;

    /// <summary>Gets the reading that runs off the UI thread; complete when none is running.</summary>
    public Task Reading { get; private set; } = Task.CompletedTask;

    /// <summary>Gets the sources in the left column, each a filter.</summary>
    public IReadOnlyList<LabelSourceViewModel> Sources { get; }

    /// <summary>Gets whether the case's labels or the library's are listed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibrary))]
    public partial LabelScope Scope { get; private set; }

    /// <summary>Gets whether the library is listed.</summary>
    public bool IsLibrary => Scope == LabelScope.Library;

    /// <summary>Gets or sets what the filter field holds: a name, an address or a case number.</summary>
    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    /// <summary>Gets what the list shows: headings and rows, in order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems))]
    public partial IReadOnlyList<object> Items { get; private set; } = [];

    /// <summary>Gets whether anything is listed.</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>Gets what the list says when nothing is listed.</summary>
    [ObservableProperty]
    public partial string EmptyMessage { get; private set; } = EmptyText;

    /// <summary>Gets the chosen label, or <see langword="null"/>; without one the right panel is removed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HasSelected), nameof(CanShowInGraph), nameof(HasActions), nameof(ShowsRetractButton))]
    [NotifyCanExecuteChangedFor(nameof(ShowInGraphCommand), nameof(ToggleSpreadCommand), nameof(BeginRetractCommand))]
    public partial LabelRowViewModel? Selected { get; private set; }

    /// <summary>Gets whether a label is chosen.</summary>
    public bool HasSelected => Selected is not null;

    /// <summary>Gets whether the chosen label's node is in the graph.</summary>
    public bool CanShowInGraph => Selected?.IsInGraph == true;

    /// <summary>Gets whether the chosen label has actions at the foot: in the case, never in the library.</summary>
    public bool HasActions => !IsLibrary && Selected?.HasActions == true;

    /// <summary>Gets how many labels the case has, for the switch.</summary>
    [ObservableProperty]
    public partial int CaseCount { get; private set; }

    /// <summary>Gets how many labels the library has, or a dash when it cannot be read.</summary>
    [ObservableProperty]
    public partial string LibraryCount { get; private set; } = "–";

    /// <summary>Gets how many labels came in from the library (<em>Kom in</em>).</summary>
    [ObservableProperty]
    public partial int CameIn { get; private set; }

    /// <summary>Gets how many of the case's labels are in the library (<em>Gick ut</em>).</summary>
    [ObservableProperty]
    public partial string WentOut { get; private set; } = "–";

    /// <summary>Gets how many labels stay in the case (<em>Sprids inte</em>).</summary>
    [ObservableProperty]
    public partial int NoSpreadCount { get; private set; }

    /// <summary>Gets whether the field for <em>Ta tillbaka</em> is folded out.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsRetractButton))]
    public partial bool IsRetracting { get; private set; }

    /// <summary>Gets whether <em>Ta tillbaka …</em> is shown: a label to take back, the field folded in.</summary>
    public bool ShowsRetractButton => !IsRetracting && CanBeginRetract();

    /// <summary>Gets or sets the reason for taking back, which is optional.</summary>
    [ObservableProperty]
    public partial string RetractReason { get; set; } = string.Empty;

    /// <summary>Gets whether a change is being sealed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleSpreadCommand), nameof(ConfirmRetractCommand))]
    public partial bool IsSaving { get; private set; }

    /// <summary>Follows the graph shown: what is in the graph changes, and the case is read again.</summary>
    /// <param name="scene">The graph, or <see langword="null"/>.</param>
    public void Update(GraphScene? scene)
    {
        _scene = scene;
        Reading = ReadAsync();
    }

    /// <summary>Lists the case's labels or the library's.</summary>
    /// <param name="scope">What to list.</param>
    [RelayCommand]
    public void ChooseScope(LabelScope scope)
    {
        if (scope == Scope)
        {
            return;
        }

        Scope = scope;
        Refilter();
    }

    /// <summary>Switches a source off or on; the list shows the case again, as the filters are the case's.</summary>
    /// <param name="source">The source.</param>
    [RelayCommand]
    public void ToggleSource(LabelSourceViewModel? source)
    {
        if (source is null)
        {
            return;
        }

        source.IsOn = !source.IsOn;
        Scope = LabelScope.Case;
        Refilter();
    }

    /// <summary>Chooses a row.</summary>
    /// <param name="row">The row.</param>
    [RelayCommand]
    public void Pick(LabelRowViewModel? row)
    {
        if (row is not null)
        {
            Choose(row);
        }
    }

    /// <summary>Chooses the next or the previous row (the arrow keys).</summary>
    /// <param name="step">One down, or minus one up.</param>
    public void Move(int step)
    {
        var rows = Items.OfType<LabelRowViewModel>().ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var index = Selected is null ? -1 : rows.IndexOf(Selected);
        Choose(rows[Math.Clamp(index + step, 0, rows.Count - 1)]);
    }

    /// <summary><em>Visa i grafen</em> (G): the chosen label's node is chosen and shown in the graph.</summary>
    /// <param name="row">The row, or <see langword="null"/> for the chosen one.</param>
    [RelayCommand(CanExecute = nameof(CanShowRow))]
    public void ShowInGraph(LabelRowViewModel? row)
    {
        if ((row ?? Selected)?.NodeKey is { } key)
        {
            ShowInGraphWanted?.Invoke(this, key);
        }
    }

    /// <summary><em>Varför?</em>: the Guide's section on labels.</summary>
    [RelayCommand]
    public void Why() => WhyWanted?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Turns <em>Sprid inte</em> on the chosen label: every own label in the row is marked alike, sealed with a line in
    /// the journal, and the library follows.
    /// </summary>
    /// <returns>A task that completes when it is sealed or could not be.</returns>
    [RelayCommand(CanExecute = nameof(CanToggleSpread))]
    public async Task ToggleSpreadAsync()
    {
        if (_session.Current is not { } open || Selected is not { } row)
        {
            return;
        }

        var doNotSpread = !row.IsNoSpread;
        var outcomes = await SealAsync(
            row.Spreadable, sequence => _editor.SetDoNotSpreadAsync(open, sequence, doNotSpread));
        if (outcomes is not null)
        {
            Tell(
                outcomes,
                doNotSpread ? NoSpreadTitle : SpreadTitle,
                doNotSpread ? NoSpreadDetail : SpreadDetail);
        }
    }

    /// <summary><em>Ta tillbaka …</em>: folds out the field for the optional reason.</summary>
    [RelayCommand(CanExecute = nameof(CanBeginRetract))]
    public void BeginRetract()
    {
        RetractReason = string.Empty;
        IsRetracting = true;
    }

    /// <summary>Folds the field for taking back in again without writing anything (Esc).</summary>
    [RelayCommand]
    public void CancelRetract()
    {
        IsRetracting = false;
        RetractReason = string.Empty;
    }

    /// <summary>
    /// Takes the chosen label back, also a colleague's: a new sealed record with a line in the journal for each of the
    /// row's labels the user can take back, and the library follows (the user's decision 2026-11-07).
    /// </summary>
    /// <returns>A task that completes when it is sealed or could not be.</returns>
    [RelayCommand(CanExecute = nameof(CanConfirmRetract))]
    public async Task ConfirmRetractAsync()
    {
        if (_session.Current is not { } open || Selected is not { } row)
        {
            return;
        }

        var reason = RetractReason.Trim().Length == 0 ? null : RetractReason.Trim();
        var outcomes = await SealAsync(row.Retractable, sequence => _editor.RetractLabelAsync(open, sequence, reason));
        if (outcomes is not null)
        {
            CancelRetract();
            var detail = row.IsFromOtherCase ? RetractedDetail + " " + CopyStaysText : RetractedDetail;
            Tell(outcomes, RetractedTitle, detail);
        }
    }

    /// <summary>Says whether a row's node can be shown in the graph.</summary>
    /// <param name="row">The row, or <see langword="null"/> for the chosen one.</param>
    /// <returns><see langword="true"/> when its node is in the graph.</returns>
    private bool CanShowRow(LabelRowViewModel? row) => (row ?? Selected)?.IsInGraph == true;

    /// <summary>Says whether <em>Sprid inte</em> can be turned now.</summary>
    /// <returns><see langword="true"/> for an own label in the case while nothing is being sealed.</returns>
    private bool CanToggleSpread() => !IsSaving && !IsLibrary && Selected?.CanChangeSpread == true;

    /// <summary>Says whether <em>Ta tillbaka …</em> can be begun.</summary>
    /// <returns><see langword="true"/> for a label in the case the user can take back.</returns>
    private bool CanBeginRetract() => !IsLibrary && Selected?.CanRetract == true;

    /// <summary>Says whether the taking back can be sealed now.</summary>
    /// <returns><see langword="true"/> while the field is out and nothing is being sealed.</returns>
    private bool CanConfirmRetract() => !IsSaving && CanBeginRetract();

    /// <summary>Seals a change to each of a row's labels, one after the other, and reads the case again.</summary>
    /// <param name="sequences">The labels.</param>
    /// <param name="seal">How one is sealed.</param>
    /// <returns>What each came to, or <see langword="null"/> when it failed and the user was told.</returns>
    private async Task<IReadOnlyList<UndoOutcome>?> SealAsync(
        IReadOnlyList<long> sequences, Func<long, Task<UndoOutcome>> seal)
    {
        IsSaving = true;
        try
        {
            var outcomes = new List<UndoOutcome>();
            foreach (var sequence in sequences)
            {
                outcomes.Add(await seal(sequence));
            }

            return outcomes;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _toaster?.Show(ToastKind.Error, FailedTitle, InspectorFormsViewModel.Failure(exception));
            return null;
        }
        finally
        {
            // Read again also after a failure: some of the row's labels may be sealed before it.
            IsSaving = false;
            Changed?.Invoke(this, EventArgs.Empty);
            Reading = ReadAsync();
        }
    }

    /// <summary>Says what a change did, and honestly when the library could not follow.</summary>
    /// <param name="outcomes">What each label came to.</param>
    /// <param name="title">The toast's title.</param>
    /// <param name="detail">The toast's text.</param>
    private void Tell(IReadOnlyList<UndoOutcome> outcomes, string title, string detail)
    {
        if (outcomes.FirstOrDefault(outcome => outcome.LibraryProblem is not null) is { } late)
        {
            _toaster?.Show(ToastKind.Warning, title, late.LibraryProblem!);
            return;
        }

        if (outcomes.Any(outcome => outcome.Done))
        {
            _toaster?.Show(ToastKind.Success, title, detail);
        }
    }

    /// <summary>Forgets the old case and reads the new one.</summary>
    private void OnCaseChanged()
    {
        Show(LabelBuild.Empty);
        CancelRetract();
        Reading = ReadAsync();
    }

    /// <summary>Reads the case's labels, the library and the sanction list off the UI thread, and shows them.</summary>
    /// <returns>A task that completes when they are shown.</returns>
    private async Task ReadAsync()
    {
        var number = ++_readings;
        if (_session.Current is not { } open)
        {
            return;
        }

        var scene = _scene;
        LabelBuild built;
        try
        {
            // The rows are built off the UI thread too; the filters only filter them (principle 6).
            built = await Task.Run(() => Build(Read(open), scene));
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            // Said honestly, never shown as an empty case; a case that closed meanwhile shows nothing anyway.
            built = LabelBuild.Failed(string.Format(
                CultureInfo.InvariantCulture, ReadFailedText, InspectorFormsViewModel.Failure(exception)));
        }

        _ui.Post(() =>
        {
            if (number == _readings && ReferenceEquals(_session.Current, open))
            {
                Show(built);
            }
        });
    }

    /// <summary>Reads what the view shows; the library and the list only as far as they can be read.</summary>
    /// <param name="open">The case.</param>
    /// <returns>What was read.</returns>
    private LabelReading Read(ICase open)
    {
        var history = _attribution.GetHistory(open);
        IReadOnlyList<LibraryLabel>? library;
        try
        {
            library = _library.List();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The vault is locked or the library unreadable: said honestly, never guessed.
            library = null;
        }

        SanctionListInfo? list;
        try
        {
            list = _sanctions.Current;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            list = null;
        }

        return new LabelReading(open.Info.CaseNumber, history, library, list);
    }

    /// <summary>Builds the rows from what was read and the graph it is shown against; off the UI thread.</summary>
    /// <param name="reading">What was read.</param>
    /// <param name="scene">The graph, or <see langword="null"/>.</param>
    /// <returns>The rows of the case and of the library.</returns>
    private LabelBuild Build(LabelReading reading, GraphScene? scene)
    {
        var words = new LabelWords(reading.CaseNumber, _providers, _zone);
        return new LabelBuild(
            reading,
            LabelRows.ForCase(reading.History, scene, words),
            reading.Library is { } library ? LabelRows.ForLibrary(library, scene, words) : [],
            null);
    }

    /// <summary>Shows rows that were built: the counts, the sources and the list.</summary>
    /// <param name="built">The rows.</param>
    private void Show(LabelBuild built)
    {
        _reading = built.Reading;
        _caseRows = built.CaseRows;
        _libraryRows = built.LibraryRows;
        _problem = built.Problem;
        Count();
        Refilter();
    }

    /// <summary>Lists the rows already built by the switch, the filters and the field, keeping the choice.</summary>
    private void Refilter()
    {
        var key = Selected?.Key;
        Items = IsLibrary ? LibraryItems() : CaseItems();
        EmptyMessage = EmptyMessageFor();
        var rows = Items.OfType<LabelRowViewModel>().ToList();
        Choose(rows.FirstOrDefault(row => row.Key == key) ?? rows.FirstOrDefault());
    }

    /// <summary>Counts what the left column and the switch show.</summary>
    private void Count()
    {
        CaseCount = _caseRows.Count(row => !row.IsRetracted);
        CameIn = _caseRows.Count(row => !row.IsRetracted && row.IsFromOtherCase);
        NoSpreadCount = _caseRows.Count(row => !row.IsRetracted && row.IsNoSpread);
        LibraryCount = _reading.Library?.Count.ToString(CultureInfo.InvariantCulture) ?? "–";
        WentOut = _reading.Library is { } library
            ? library.Count(label => label.CaseNumbers.Contains(_reading.CaseNumber))
                .ToString(CultureInfo.InvariantCulture)
            : "–";
        foreach (var source in Sources)
        {
            source.Count = _caseRows.Count(row => row.Sources.Contains(source.Filter));
            source.Detail = DetailOf(source.Filter);
        }
    }

    /// <summary>Writes the line under a source: what it is now.</summary>
    /// <param name="filter">The source.</param>
    /// <returns>The line.</returns>
    private string DetailOf(LabelFilter filter) => filter switch
    {
        LabelFilter.Sanction when _reading.List is { } list =>
            string.Create(CultureInfo.InvariantCulture, $"{list.Name} · {list.PublishDate:yyyy-MM-dd}"),
        LabelFilter.Sanction => "Ingen lista importerad",
        LabelFilter.Own => "Satta i ärendet",
        LabelFilter.Provider => "Underleverantör",
        LabelFilter.Behaviour => "Förslag från analyserna",
        _ => "Genom biblioteket",
    };

    /// <summary>
    /// Gives the case's list: the sanction hits in the graph gathered at the top, then every other label shown by the
    /// filters and the filter field.
    /// </summary>
    /// <returns>Headings and rows.</returns>
    private IReadOnlyList<object> CaseItems()
    {
        var shown = _caseRows.Where(IsShown).ToList();
        var hits = shown.Where(IsHitInGraph).ToList();
        var rest = shown.Except(hits).ToList();
        var items = new List<object>();
        if (hits.Count > 0)
        {
            var note = _reading.List is { } list
                ? string.Create(
                    CultureInfo.InvariantCulture, $"{list.Name} · publicerad {list.PublishDate:yyyy-MM-dd}")
                : string.Empty;
            items.Add(new LabelGroupHeader("SANKTIONSTRÄFFAR I GRAFEN", hits.Count, note, IsRisk: true));
            items.AddRange(hits);
        }

        if (rest.Count > 0)
        {
            items.Add(new LabelGroupHeader("ETIKETTER I ÄRENDET", rest.Count, string.Empty, IsRisk: false));
            items.AddRange(rest);
        }

        return items;
    }

    /// <summary>Gives the library's list, filtered by the filter field.</summary>
    /// <returns>A heading and rows.</returns>
    private IReadOnlyList<object> LibraryItems()
    {
        var shown = _libraryRows.Where(Matches).ToList();
        if (shown.Count == 0)
        {
            return [];
        }

        var note = shown.Count == _libraryRows.Count
            ? "Krypterat i valvet"
            : $"Visar {shown.Count} · krypterat i valvet";
        return [new LabelGroupHeader("BIBLIOTEKET", _libraryRows.Count, note, IsRisk: false), .. shown];
    }

    /// <summary>Says what the list says when it is empty.</summary>
    /// <returns>The text.</returns>
    private string EmptyMessageFor() => (IsLibrary, _reading.Library) switch
    {
        (false, _) when _problem is not null => _problem,
        (true, null) => LibraryClosedText,
        (true, { Count: 0 }) => LibraryEmptyText,
        (true, _) => FilteredText,
        _ when _caseRows.Count == 0 => EmptyText,
        _ => FilteredText,
    };

    /// <summary>
    /// Says whether a row is a hit on a sanction list in the graph: it stands at the top, in red.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns><see langword="true"/> for a hit that counts, on a node in the graph.</returns>
    private static bool IsHitInGraph(LabelRowViewModel row) =>
        row.IsSanction && row.Place != LabelPlace.None && !row.IsRetracted;

    /// <summary>
    /// Says whether a row is shown: its source is on (and, when it came from another case, that source too), and it
    /// matches the filter field.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns><see langword="true"/> when it is shown.</returns>
    private bool IsShown(LabelRowViewModel row)
    {
        var off = Sources.Where(source => !source.IsOn).Select(source => source.Filter).ToHashSet();
        return !off.Contains(row.Source)
            && !(row.IsFromOtherCase && off.Contains(LabelFilter.OtherCase))
            && Matches(row);
    }

    /// <summary>Says whether a row matches the filter field.</summary>
    /// <param name="row">The row.</param>
    /// <returns><see langword="true"/> when the field is empty or the row holds its text.</returns>
    private bool Matches(LabelRowViewModel row) =>
        Query.Trim().Length == 0 || row.SearchText.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Chooses a row, or none.</summary>
    /// <param name="row">The row.</param>
    private void Choose(LabelRowViewModel? row)
    {
        if (!ReferenceEquals(row, Selected))
        {
            CancelRetract();
        }

        foreach (var other in Items.OfType<LabelRowViewModel>())
        {
            other.IsSelected = ReferenceEquals(other, row);
        }

        Selected = row;
    }

    /// <summary>Folds the field for taking back in and says again which actions the chosen label has.</summary>
    /// <param name="value">The new scope.</param>
    partial void OnScopeChanged(LabelScope value)
    {
        CancelRetract();
        OnPropertyChanged(nameof(HasActions));
        OnPropertyChanged(nameof(ShowsRetractButton));
        ToggleSpreadCommand.NotifyCanExecuteChanged();
        BeginRetractCommand.NotifyCanExecuteChanged();
        ConfirmRetractCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Lists anew when the filter field changes.</summary>
    /// <param name="value">The new text.</param>
    partial void OnQueryChanged(string value) => Refilter();

    /// <summary>What was read from the case and the library.</summary>
    /// <param name="CaseNumber">The case's number.</param>
    /// <param name="History">Every label in the case.</param>
    /// <param name="Library">The library's labels, or <see langword="null"/> when it cannot be read.</param>
    /// <param name="List">The current sanction list, or <see langword="null"/>.</param>
    private sealed record LabelReading(
        string CaseNumber,
        IReadOnlyList<LabelHistoryEntry> History,
        IReadOnlyList<LibraryLabel>? Library,
        SanctionListInfo? List)
    {
        /// <summary>Gets the reading of no case.</summary>
        public static LabelReading Empty { get; } = new(string.Empty, [], null, null);
    }

    /// <summary>The rows built from a reading, or why there are none.</summary>
    /// <param name="Reading">What was read.</param>
    /// <param name="CaseRows">The case's rows.</param>
    /// <param name="LibraryRows">The library's rows.</param>
    /// <param name="Problem">Why the case could not be read, or <see langword="null"/>.</param>
    private sealed record LabelBuild(
        LabelReading Reading,
        IReadOnlyList<LabelRowViewModel> CaseRows,
        IReadOnlyList<LabelRowViewModel> LibraryRows,
        string? Problem)
    {
        /// <summary>Gets the rows of no case.</summary>
        public static LabelBuild Empty { get; } = new(LabelReading.Empty, [], [], null);

        /// <summary>Gives no rows, with why.</summary>
        /// <param name="problem">Why the case could not be read.</param>
        /// <returns>The build.</returns>
        public static LabelBuild Failed(string problem) => new(LabelReading.Empty, [], [], problem);
    }
}
