using System.Collections.ObjectModel;
using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>A sort in the menu <em>Områden i grafen</em> (sketch KF1b): its name, tint, count and whether it shows.</summary>
/// <param name="Sort">The sort.</param>
/// <param name="Name">Its name.</param>
/// <param name="TintKey">Its tint's resource key.</param>
/// <param name="CountText">How many areas it has now, or <c>inga</c>.</param>
/// <param name="IsShown">Whether its areas are drawn.</param>
public sealed record AreaSortItem(AreaSort Sort, string Name, string TintKey, string CountText, bool IsShown);

/// <summary>A check or suggestion that concerns the chosen actor, for the inspector's <em>Här</em>.</summary>
/// <param name="Title">The check's title.</param>
/// <param name="State">What it waits for or that it is a check, in Swedish.</param>
/// <param name="Look">Its icon and tint.</param>
/// <param name="IsWaiting">Whether it waits for a decision (dashed).</param>
public sealed record HereLine(string Title, string State, AreaLook Look, bool IsWaiting);

/// <summary>
/// A dashed line from a mixer to a likely output (CLAUDE.md, <em>Mixers är vanliga</em>): never a transfer on the chain,
/// only a candidate, as faint as its score is low.
/// </summary>
/// <param name="From">The mixer's actor.</param>
/// <param name="To">The candidate's actor.</param>
/// <param name="Score">The score, from 0 to 1.</param>
/// <param name="ScoreText">The score as written on the line, for example <c>0,82</c>.</param>
/// <param name="IsApproved">Whether the user approved it; it stays dashed, as the user's judgement.</param>
public sealed record CandidateLine(string From, string To, double Score, string ScoreText, bool IsApproved);

/// <summary>A sort the form for an own area offers.</summary>
/// <param name="Sort">The sort.</param>
/// <param name="Name">Its name in the form.</param>
public sealed record SortChoice(AreaSort Sort, string Name);

/// <summary>A colour the form for an own area offers.</summary>
/// <param name="Tint">The tint.</param>
/// <param name="TintKey">Its resource key.</param>
/// <param name="IsChosen">Whether it is the form's colour now.</param>
public sealed record TintChoice(AreaTint Tint, string TintKey, bool IsChosen);

/// <summary>What the small form beside the graph does (sketch KF1b).</summary>
public enum AreaFormMode
{
    /// <summary>No form is open.</summary>
    None,

    /// <summary>Draws a new own area around the chosen actors (Z).</summary>
    Create,

    /// <summary>Renames an own area.</summary>
    Rename,

    /// <summary>Removes an own area, with an optional reason.</summary>
    Remove,
}

/// <summary>
/// The checks and suggestions (sketch KF1a) and the areas behind the actors (sketch KF1b) for the graph shown: runs
/// the analyses in the background when the graph is read, lists them in <em>Att avgöra</em>, <em>Gjort av sig
/// självt</em> and <em>Avgjort</em>, seals the user's decisions, and draws, names and removes the user's own areas.
/// It knows nothing of WPF, so that it is tested in Chainsaw.App.Tests.
/// </summary>
/// <remarks>
/// Everything that reads or writes the case runs off the UI thread through <see cref="ICheckSource"/> (principle 6);
/// an answer about an earlier graph or another case is dropped.
/// </remarks>
public sealed partial class ChecksViewModel : ObservableObject
{
    /// <summary>What the list says before the checks have run.</summary>
    public const string NotRunText = "Kontrollerna har inte körts än.";

    /// <summary>What the list says when the checks could not run.</summary>
    public const string RunFailedText = "Kontrollerna kunde inte köras på ärendet.";

    /// <summary>What the list says when a decision or an area could not be stored.</summary>
    public const string SaveFailedText = "Det kunde inte sparas i ärendet.";

    /// <summary>What the form says when no actor is chosen.</summary>
    public const string PickFirstText = "Välj aktörer först: Ctrl+klick, sedan Z.";

    /// <summary>The sorts the form for an own area offers, with their names (sketch KF1b).</summary>
    public static IReadOnlyList<(AreaSort Sort, string Name)> FormSorts { get; } =
    [
        (AreaSort.Own, "Egen"),
        (AreaSort.Obfuscation, "Förvirring"),
        (AreaSort.Wallet, "Plånbok"),
        (AreaSort.Service, "Tjänst"),
        (AreaSort.OtherChain, "Annan kedja"),
        (AreaSort.Time, "Tid"),
    ];

    /// <summary>The open case.</summary>
    private readonly CaseSession _session;

    /// <summary>The Checks module, off the UI thread.</summary>
    private readonly ICheckSource _source;

    /// <summary>The sorts whose areas are drawn when no row is chosen.</summary>
    private readonly HashSet<AreaSort> _shown = [.. Enum.GetValues<AreaSort>()];

    /// <summary>The actors chosen with Ctrl+click, for an own area.</summary>
    private readonly List<string> _picked = [];

    /// <summary>The graph the checks were run on, or <see langword="null"/>.</summary>
    private GraphScene? _scene;

    /// <summary>The last run's checks.</summary>
    private IReadOnlyList<StoredCheck> _checks = [];

    /// <summary>The own areas in force.</summary>
    private IReadOnlyList<OwnArea> _own = [];

    /// <summary>Counts the runs, so that an answer to an older one is dropped.</summary>
    private int _version;

    /// <summary>The own area the form renames or removes.</summary>
    private OwnArea? _formArea;

    /// <summary>Creates the checks for the window's open case.</summary>
    /// <param name="session">The open case.</param>
    /// <param name="source">The Checks module.</param>
    public ChecksViewModel(CaseSession session, ICheckSource source)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(source);
        _session = session;
        _source = source;
        session.Changed += (_, _) => Reset();
    }

    /// <summary>Raised when the areas, the focus or the chosen actors change, so that the graph draws again.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when <em>Varför?</em> about a row asks for the Guide.</summary>
    public event EventHandler<GuideRequest>? GuideWanted;

    /// <summary>Raised when a row asks the graph to show its actors (a check's <em>Visa i grafen</em>).</summary>
    public event EventHandler<IReadOnlyList<string>>? ShowWanted;

    /// <summary>Gets or sets the time zone the time areas are written in; the computer's by default.</summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Local;

    /// <summary>Gets whether the list is folded out where the inspector stands (Ctrl+J).</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    /// <summary>Gets the rows that wait for a decision (<em>Att avgöra</em>).</summary>
    public ObservableCollection<CheckRowViewModel> ToDecide { get; } = [];

    /// <summary>Gets the checks that did their work by themselves (<em>Gjort av sig självt</em>).</summary>
    public ObservableCollection<CheckRowViewModel> Done { get; } = [];

    /// <summary>Gets the suggestions the user has decided (<em>Avgjort</em>).</summary>
    public ObservableCollection<CheckRowViewModel> Decided { get; } = [];

    /// <summary>Gets the chosen row, or <see langword="null"/>.</summary>
    [ObservableProperty]
    public partial CheckRowViewModel? Selected { get; private set; }

    /// <summary>Gets how many decisions wait, for the button <em>Förslag · N</em>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    public partial int WaitingCount { get; private set; }

    /// <summary>Gets the button's text in the graph's corner.</summary>
    public string ButtonText => "Förslag · " + WaitingCount.ToString(CultureInfo.InvariantCulture);

    /// <summary>Gets when the checks last ran and on how much, or why not.</summary>
    [ObservableProperty]
    public partial string StatusText { get; private set; } = NotRunText;

    /// <summary>Gets what went wrong last, or empty.</summary>
    [ObservableProperty]
    public partial string Problem { get; private set; } = string.Empty;

    /// <summary>Gets every area that can be drawn now, with the fill rule applied.</summary>
    public IReadOnlyList<AreaView> AllAreas { get; private set; } = [];

    /// <summary>
    /// Gets the areas drawn now: the chosen row's alone while the list is open, else those of the sorts shown.
    /// </summary>
    public IReadOnlyList<AreaView> Areas
    {
        get
        {
            if (IsOpen && Selected is { } row)
            {
                return [.. AllAreas.Where(area => area.CheckKey == row.Key).Select(area => area with { IsFilled = true })];
            }

            return [.. AllAreas.Where(area => _shown.Contains(area.Sort))];
        }
    }

    /// <summary>Gets the dashed lines to the candidates of the suggestions whose areas are drawn.</summary>
    public IReadOnlyList<CandidateLine> CandidateLines
    {
        get
        {
            if (_scene is not { } scene)
            {
                return [];
            }

            var graph = scene.Snapshot.Graph;
            var lines = new List<CandidateLine>();
            foreach (var area in Areas.Where(area => area.CheckKey is not null))
            {
                var check = _checks.FirstOrDefault(c => GraphAreas.KeyOf(c) == area.CheckKey);
                if (check is null || check.Finding.Candidates.Count == 0 || check.Finding.Addresses.Count == 0
                    || graph.NodeOf(check.Finding.Addresses[0]) is not { } from)
                {
                    continue;
                }

                foreach (var row in new CheckRowViewModel(check, null, []).Candidates.Where(c => !c.IsDismissed))
                {
                    if (graph.NodeOf(row.Candidate.Address) is { } to && to.Key != from.Key)
                    {
                        lines.Add(new CandidateLine(from.Key, to.Key, row.Candidate.Score, row.ScoreText, row.IsApproved));
                    }
                }
            }

            return lines;
        }
    }

    /// <summary>
    /// Gets the actors the chosen row is about, which the graph draws clearly while it tones down the rest; null
    /// when the list is closed or no row is chosen.
    /// </summary>
    public IReadOnlySet<string>? Focus => IsOpen && Selected is { } row ? row.Nodes.ToHashSet(StringComparer.Ordinal) : null;

    /// <summary>Gets the actors chosen with Ctrl+click, for an own area.</summary>
    public IReadOnlyList<string> Picked => _picked;

    /// <summary>Gets the area whose chip was clicked, whose name and tools are shown; null when none.</summary>
    [ObservableProperty]
    public partial string? ChosenArea { get; private set; }

    /// <summary>Gets whether the menu <em>Områden i grafen</em> is open.</summary>
    [ObservableProperty]
    public partial bool IsMenuOpen { get; private set; }

    /// <summary>Gets the menu's sorts with their counts.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<AreaSortItem> Sorts { get; private set; } = [];

    /// <summary>Gets how many areas are drawn, for the menu's button.</summary>
    public string AreaCountText => Areas.Count.ToString(CultureInfo.InvariantCulture);

    /// <summary>Gets what the inspector lists under <em>Här</em> for the chosen actor.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<HereLine> Here { get; private set; } = [];

    /// <summary>Gets what the small form does, or <see cref="AreaFormMode.None"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsFormOpen), nameof(FormTitle), nameof(FormSaveText), nameof(IsCreating), nameof(ShowsName),
        nameof(NoteLabel))]
    public partial AreaFormMode Form { get; private set; }

    /// <summary>Gets whether the form draws a new area, with sort and colour.</summary>
    public bool IsCreating => Form == AreaFormMode.Create;

    /// <summary>Gets whether the form asks for a name.</summary>
    public bool ShowsName => Form is AreaFormMode.Create or AreaFormMode.Rename;

    /// <summary>Gets the label of the form's last field: the note, or the reason for a removal.</summary>
    public string NoteLabel => Form == AreaFormMode.Remove ? "Skäl, valfritt" : "Anteckning, valfri";

    /// <summary>Gets the sorts the form offers.</summary>
    public IReadOnlyList<SortChoice> SortChoices { get; } =
        [.. FormSorts.Select(entry => new SortChoice(entry.Sort, entry.Name))];

    /// <summary>Gets the colours the form offers, the chosen one marked.</summary>
    public IReadOnlyList<TintChoice> TintChoices =>
        [.. AreaLooks.OwnTints.Select(tint => new TintChoice(tint, AreaLooks.BrushKey(tint), tint == FormTint))];

    /// <summary>Chooses the form's colour.</summary>
    /// <param name="tint">The colour.</param>
    [RelayCommand]
    public void ChooseTint(AreaTint tint) => FormTint = tint;

    /// <summary>Gets whether the form is open.</summary>
    public bool IsFormOpen => Form != AreaFormMode.None;

    /// <summary>Gets the form's heading.</summary>
    public string FormTitle => Form switch
    {
        AreaFormMode.Create => Picked.Count.ToString(CultureInfo.InvariantCulture) + " valda · Ny yta",
        AreaFormMode.Rename => "Byt namn",
        AreaFormMode.Remove => "Ta bort ytan " + (_formArea?.Name ?? string.Empty),
        _ => string.Empty,
    };

    /// <summary>Gets the form's store button's text.</summary>
    public string FormSaveText => Form == AreaFormMode.Remove ? "Ta bort" : "Spara";

    /// <summary>Gets or sets the form's name.</summary>
    [ObservableProperty]
    public partial string FormName { get; set; } = string.Empty;

    /// <summary>Gets or sets the form's sort.</summary>
    [ObservableProperty]
    public partial AreaSort FormSort { get; set; } = AreaSort.Own;

    /// <summary>Gets or sets the form's tint.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TintChoices))]
    public partial AreaTint FormTint { get; set; } = AreaTint.Green;

    /// <summary>Gets or sets whether the user believes the actors are the same actor.</summary>
    [ObservableProperty]
    public partial bool FormSameActor { get; set; }

    /// <summary>Gets or sets the form's note, or the reason for a removal; optional.</summary>
    [ObservableProperty]
    public partial string FormNote { get; set; } = string.Empty;

    /// <summary>Gets what the form says went wrong, or empty.</summary>
    [ObservableProperty]
    public partial string FormProblem { get; private set; } = string.Empty;

    /// <summary>Gets whether something is being stored.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>Folds the list out or in (Ctrl+J).</summary>
    [RelayCommand]
    public void Toggle()
    {
        IsOpen = !IsOpen;
        if (IsOpen && Selected is null)
        {
            Select(ToDecide.FirstOrDefault() ?? Done.FirstOrDefault());
        }

        RaiseChanged();
    }

    /// <summary>Folds the list in.</summary>
    public void Close()
    {
        if (IsOpen)
        {
            IsOpen = false;
            RaiseChanged();
        }
    }

    /// <summary>Chooses a row; the graph tones down the rest and shows its area.</summary>
    /// <param name="row">The row, or <see langword="null"/>.</param>
    [RelayCommand]
    public void Select(CheckRowViewModel? row)
    {
        foreach (var each in Rows())
        {
            each.IsSelected = each == row;
        }

        Selected = row;
        RaiseChanged();
    }

    /// <summary>Steps to the next row (J), from the top of <em>Att avgöra</em> down.</summary>
    [RelayCommand]
    public void Next() => Step(1);

    /// <summary>Steps to the row before (K).</summary>
    [RelayCommand]
    public void Previous() => Step(-1);

    /// <summary>Chooses the candidate Enter and X decide in the chosen row.</summary>
    /// <param name="candidate">The candidate.</param>
    [RelayCommand]
    public void ChooseCandidate(CandidateRowViewModel candidate) => Selected?.Choose(candidate);

    /// <summary>
    /// Enter: approves the chosen row's chosen candidate, or the suggestion; for a check, shows its actors.
    /// </summary>
    /// <returns>A task that completes when the decision is stored and the checks read again.</returns>
    [RelayCommand]
    public Task ApproveAsync()
    {
        if (Selected is { CanDecide: false } check)
        {
            ShowWanted?.Invoke(this, check.Nodes);
            return Task.CompletedTask;
        }

        return DecideAsync(CheckVerdict.Approved);
    }

    /// <summary>X: dismisses the chosen row's chosen candidate, or the suggestion.</summary>
    /// <returns>A task that completes when the decision is stored and the checks read again.</returns>
    [RelayCommand]
    public Task DismissAsync() => DecideAsync(CheckVerdict.Dismissed);

    /// <summary>?: asks the Guide why, with the row's answer and its candidates on top.</summary>
    [RelayCommand]
    public void AskWhy()
    {
        if (Selected is not { } row)
        {
            return;
        }

        var lines = new List<WhyLine> { new(row.Check.Finding.Why, TopicOf(row.Check)) };
        lines.AddRange(row.Candidates.Select(c => new WhyLine(
            $"{c.ScoreText} · {c.ShortAddress}: {c.Candidate.Reason}", GuideTopic.MixerCandidates)));
        lines.Add(new WhyLine(
            $"{row.AnalysisText} förseglade det {Stamp(row.Check.RecordedUtc)}, med tx och block det bygger på. "
            + "Allt här är en bedömning, aldrig ett faktum.",
            GuideTopic.Checks));
        var request = GuideRequest.ForWhy(row.Key, row.Title, lines, TracingMethodKind.Fifo);
        GuideWanted?.Invoke(this, request);
    }

    /// <summary>Opens or closes the menu <em>Områden i grafen</em>.</summary>
    [RelayCommand]
    public void ToggleMenu() => IsMenuOpen = !IsMenuOpen;

    /// <summary>Shows or hides a sort's areas.</summary>
    /// <param name="sort">The sort.</param>
    [RelayCommand]
    public void ToggleSort(AreaSort sort)
    {
        if (!_shown.Remove(sort))
        {
            _shown.Add(sort);
        }

        Rebuild();
    }

    /// <summary>Adds an actor to the ones chosen for an own area, or takes it away (Ctrl+click).</summary>
    /// <param name="node">The actor's key.</param>
    public void TogglePick(string node)
    {
        if (!_picked.Remove(node))
        {
            _picked.Add(node);
        }

        RaiseChanged();
    }

    /// <summary>Forgets the actors chosen for an own area.</summary>
    public void ClearPicked()
    {
        if (_picked.Count > 0)
        {
            _picked.Clear();
            RaiseChanged();
        }
    }

    /// <summary>
    /// Z: opens the form for a new own area around the chosen actors, or around the one actor chosen in the graph.
    /// </summary>
    /// <param name="chosenNode">The actor chosen in the graph, used when none is chosen with Ctrl+click.</param>
    public void OpenCreate(string? chosenNode)
    {
        if (_picked.Count == 0 && chosenNode is not null)
        {
            _picked.Add(chosenNode);
        }

        Form = AreaFormMode.Create;
        FormName = string.Empty;
        FormSort = AreaSort.Own;
        FormTint = AreaTint.Green;
        FormSameActor = false;
        FormNote = string.Empty;
        FormProblem = _picked.Count == 0 ? PickFirstText : string.Empty;
        OnPropertyChanged(nameof(FormTitle));
        RaiseChanged();
    }

    /// <summary>Chooses an area by its chip: a check's area chooses its row, an own area shows its tools.</summary>
    /// <param name="area">The area's key, or <see langword="null"/> to choose none.</param>
    public void ChooseArea(string? area)
    {
        ChosenArea = area;
        if (AllAreas.FirstOrDefault(a => a.Key == area) is { CheckKey: { } check }
            && Rows().FirstOrDefault(row => row.Key == check) is { } row)
        {
            IsOpen = true;
            Select(row);
            return;
        }

        RaiseChanged();
    }

    /// <summary>Opens the form that renames an own area.</summary>
    /// <param name="area">The area's key.</param>
    public void OpenRename(string area) => OpenFor(area, AreaFormMode.Rename);

    /// <summary>Opens the form that removes an own area, with an optional reason.</summary>
    /// <param name="area">The area's key.</param>
    public void OpenRemove(string area) => OpenFor(area, AreaFormMode.Remove);

    /// <summary>Closes the form without storing (Esc).</summary>
    [RelayCommand]
    public void CancelForm()
    {
        Form = AreaFormMode.None;
        FormProblem = string.Empty;
        _formArea = null;
        RaiseChanged();
    }

    /// <summary>Stores what the form says (Enter): a new area, a new name or a removal, sealed in the case.</summary>
    /// <returns>A task that completes when it is stored and the areas read again.</returns>
    [RelayCommand]
    public async Task SaveFormAsync()
    {
        if (IsBusy || _session.Current is not { } openCase || _scene is not { } scene)
        {
            return;
        }

        var mode = Form;
        if (mode == AreaFormMode.Create && _picked.Count == 0)
        {
            FormProblem = PickFirstText;
            return;
        }

        if (mode != AreaFormMode.Remove && string.IsNullOrWhiteSpace(FormName))
        {
            FormProblem = "Ytan behöver ett namn.";
            return;
        }

        IsBusy = true;
        try
        {
            await StoreAsync(openCase, scene, mode);
            if (_session.Current != openCase)
            {
                return;
            }

            if (mode == AreaFormMode.Create)
            {
                _picked.Clear();
            }

            Form = AreaFormMode.None;
            FormProblem = string.Empty;
            _formArea = null;
            ChosenArea = null;
            _own = await _source.GetAreasAsync(openCase, CancellationToken.None);
            Rebuild();
        }
        catch (ArgumentException exception)
        {
            FormProblem = exception.Message.Split(" (Parameter", 2)[0];
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            FormProblem = SaveFailedText;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Runs the checks on a new graph in the background, with the own areas; while it runs, the former areas are kept
    /// on the actors that are still there.
    /// </summary>
    /// <param name="scene">The graph, or <see langword="null"/> when none is shown.</param>
    /// <returns>A task that completes when the checks are shown or could not run.</returns>
    public async Task FollowAsync(GraphScene? scene)
    {
        var version = ++_version;
        _scene = scene;
        if (scene is null || _session.Current is not { } openCase)
        {
            _checks = [];
            _own = [];
            Rebuild();
            return;
        }

        Rebuild();
        try
        {
            var own = await _source.GetAreasAsync(openCase, CancellationToken.None);
            var run = await _source.RunAsync(openCase, scene.Snapshot.Graph, CancellationToken.None);
            if (version != _version || _session.Current != openCase)
            {
                return;
            }

            _own = own;
            _checks = run.Checks;
            StatusText = string.Create(
                CultureInfo.InvariantCulture,
                $"Kördes {Stamp(run.RanUtc)} på {run.Transactions} tx");
            Problem = string.Join(" ", run.Problems);
            Rebuild();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (version == _version)
            {
                // No operator profile, a closed case or a failure: the graph is shown all the same, honestly without
                // checks (principle 9).
                StatusText = RunFailedText;
                Problem = RunFailedText;
            }
        }
    }

    /// <summary>Says which checks concern an actor, for the inspector's <em>Här</em>.</summary>
    /// <param name="node">The chosen actor, or <see langword="null"/>.</param>
    public void Inspect(string? node)
    {
        Here = node is null
            ? []
            : [.. Rows().Where(row => row.Nodes.Contains(node)).Select(row => new HereLine(
                row.Title,
                StateOf(row),
                row.Look,
                row.Group == CheckRowGroup.ToDecide))];
    }

    /// <summary>Gives a row's state as the inspector writes it.</summary>
    /// <param name="row">The row.</param>
    /// <returns>For example <c>väntar</c>.</returns>
    private static string StateOf(CheckRowViewModel row) => row.Group switch
    {
        CheckRowGroup.ToDecide => "väntar",
        CheckRowGroup.Decided => "avgjort",
        _ => "kontroll",
    };

    /// <summary>Gives the Guide's section for a check.</summary>
    /// <param name="check">The check.</param>
    /// <returns>The section.</returns>
    private static GuideTopic TopicOf(StoredCheck check) => check.Analysis.Name switch
    {
        "mixer-outputs" => GuideTopic.MixerCandidates,
        "coinjoin-area" => GuideTopic.CoinJoin,
        "peel-chain-area" => GuideTopic.PeelChain,
        _ => GuideTopic.Areas,
    };

    /// <summary>Writes a time in local time, as the status and the Guide say it.</summary>
    /// <param name="utc">The time.</param>
    /// <returns>For example <c>14:02</c>, with the date when it is not today.</returns>
    private string Stamp(DateTimeOffset utc)
    {
        var local = TimeZoneInfo.ConvertTime(utc, TimeZone);
        return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Seals a decision about the chosen row and reads the checks again.</summary>
    /// <param name="verdict">Approve or dismiss.</param>
    /// <returns>A task that completes when it is stored.</returns>
    private async Task DecideAsync(CheckVerdict verdict)
    {
        if (IsBusy || Selected?.Request(verdict) is not { } request || _session.Current is not { } openCase)
        {
            return;
        }

        var key = Selected.Key;
        IsBusy = true;
        try
        {
            await _source.DecideAsync(openCase, request);
            Problem = string.Empty;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Problem = SaveFailedText;
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await FollowAsync(_scene);
        var again = Rows().FirstOrDefault(row => row.Key == key);
        Select(again is { CanDecide: true } ? again : ToDecide.FirstOrDefault() ?? again);
    }

    /// <summary>Stores what the form says through the module.</summary>
    /// <param name="openCase">The open case.</param>
    /// <param name="scene">The graph.</param>
    /// <param name="mode">What the form does.</param>
    /// <returns>A task that completes when it is stored.</returns>
    private Task StoreAsync(ICase openCase, GraphScene scene, AreaFormMode mode)
    {
        var note = string.IsNullOrWhiteSpace(FormNote) ? null : FormNote.Trim();
        return mode switch
        {
            AreaFormMode.Create => _source.AddAreaAsync(openCase, new OwnAreaDraft(
                FormName.Trim(), FormSort, FormTint, FormSameActor, note, GraphAreas.MembersOf(scene, _picked))),
            AreaFormMode.Rename when _formArea is { } area => _source.RenameAreaAsync(openCase, area.Id, FormName.Trim()),
            AreaFormMode.Remove when _formArea is { } area => _source.RemoveAreaAsync(openCase, area.Id, note),
            _ => Task.CompletedTask,
        };
    }

    /// <summary>Opens the form about an own area.</summary>
    /// <param name="area">The area's key.</param>
    /// <param name="mode">Rename or remove.</param>
    private void OpenFor(string area, AreaFormMode mode)
    {
        if (AllAreas.FirstOrDefault(a => a.Key == area)?.Own is not { } own)
        {
            return;
        }

        _formArea = own;
        FormName = own.Name;
        FormNote = string.Empty;
        FormProblem = string.Empty;
        Form = mode;
        OnPropertyChanged(nameof(FormTitle));
        RaiseChanged();
    }

    /// <summary>Steps among the rows in the list's order.</summary>
    /// <param name="step">One forward or back.</param>
    private void Step(int step)
    {
        var rows = Rows().ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var at = Selected is null ? -1 : rows.IndexOf(Selected);
        var next = at < 0 ? 0 : Math.Clamp(at + step, 0, rows.Count - 1);
        Select(rows[next]);
    }

    /// <summary>Gives every row in the list's order.</summary>
    /// <returns>The rows.</returns>
    private IEnumerable<CheckRowViewModel> Rows() => ToDecide.Concat(Done).Concat(Decided);

    /// <summary>Builds the rows, the areas and the menu again from the last run, keeping the chosen row.</summary>
    private void Rebuild()
    {
        var selected = Selected?.Key;
        var picked = _scene is { } scene ? _picked.Where(key => scene.Node(key) is not null).ToList() : [];
        _picked.Clear();
        _picked.AddRange(picked);
        ToDecide.Clear();
        Done.Clear();
        Decided.Clear();
        AllAreas = _scene is { } shown ? GraphAreas.Build(shown, _checks, _own, TimeZone) : [];
        foreach (var check in _checks)
        {
            var area = AllAreas.FirstOrDefault(a => a.CheckKey == GraphAreas.KeyOf(check));
            var row = new CheckRowViewModel(check, AnchorName(check), area?.Nodes ?? []);
            if (row.Group == CheckRowGroup.Done && check.Finding.Area != AreaSort.Obfuscation)
            {
                // Wallets are areas only (sketch KF1b); the list holds what obfuscates and what waits.
                continue;
            }

            Target(row.Group).Add(row);
        }

        WaitingCount = _checks.Sum(check => check.Waiting);
        Selected = Rows().FirstOrDefault(row => row.Key == selected);
        if (Selected is { } again)
        {
            again.IsSelected = true;
        }

        if (ChosenArea is { } chosen && AllAreas.All(area => area.Key != chosen))
        {
            ChosenArea = null;
        }

        Sorts =
        [
            .. AreaLooks.Sorts.Select(entry =>
            {
                var count = AllAreas.Count(area => area.Sort == entry.Sort);
                return new AreaSortItem(
                    entry.Sort,
                    entry.Name,
                    AreaLooks.BrushKey(AreaLooks.Of(entry.Sort).Tint),
                    count == 0 ? "inga" : count.ToString(CultureInfo.InvariantCulture),
                    _shown.Contains(entry.Sort));
            }),
        ];
        RaiseChanged();
    }

    /// <summary>Gives the collection a group's rows stand in.</summary>
    /// <param name="group">The group.</param>
    /// <returns>The collection.</returns>
    private ObservableCollection<CheckRowViewModel> Target(CheckRowGroup group) => group switch
    {
        CheckRowGroup.ToDecide => ToDecide,
        CheckRowGroup.Done => Done,
        _ => Decided,
    };

    /// <summary>Gives the name of the actor a check starts from, when it has one.</summary>
    /// <param name="check">The check.</param>
    /// <returns>The name, or <see langword="null"/>.</returns>
    private string? AnchorName(StoredCheck check)
    {
        if (_scene is not { } scene || check.Finding.Addresses.Count == 0)
        {
            return null;
        }

        var node = scene.Snapshot.Graph.NodeOf(check.Finding.Addresses[0]);
        return scene.Node(node?.Key) is { IsNamed: true } named ? named.Name : null;
    }

    /// <summary>Forgets everything about the former case.</summary>
    private void Reset()
    {
        _version++;
        _scene = null;
        _checks = [];
        _own = [];
        _picked.Clear();
        _formArea = null;
        Form = AreaFormMode.None;
        IsOpen = false;
        IsMenuOpen = false;
        ChosenArea = null;
        StatusText = NotRunText;
        Problem = string.Empty;
        Here = [];
        Rebuild();
    }

    /// <summary>Says that the areas, the focus or the picked actors changed.</summary>
    private void RaiseChanged()
    {
        OnPropertyChanged(nameof(Areas));
        OnPropertyChanged(nameof(Focus));
        OnPropertyChanged(nameof(AreaCountText));
        OnPropertyChanged(nameof(Picked));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
