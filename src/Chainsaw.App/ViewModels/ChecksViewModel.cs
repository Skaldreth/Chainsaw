using System.Collections.ObjectModel;
using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// A sort in the menu <em>Områden i grafen</em> (sketch KF1b): its name, tint, count and whether it shows.
/// </summary>
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
/// A dashed line from a mixer to a likely output (CLAUDE.md, <em>Mixers är vanliga</em>): never a transfer on the
/// chain,
/// only a candidate, as faint as its score is low.
/// </summary>
/// <param name="From">The mixer's actor.</param>
/// <param name="To">The candidate's actor.</param>
/// <param name="Score">The score, from 0 to 1.</param>
/// <param name="ScoreText">The score as written on the line, for example <c>0,82</c>.</param>
/// <param name="IsApproved">Whether the user approved it; it stays dashed, as the user's judgement.</param>
/// <param name="Rank">The candidate's rank in its suggestion, from 1, as the panel's table numbers it.</param>
/// <param name="IsChosen">Whether it is the candidate the panel's Enter approves now.</param>
public sealed record CandidateLine(
    string From, string To, double Score, string ScoreText, bool IsApproved, int Rank = 0, bool IsChosen = false);

/// <summary>What the panel shows (sketch KF2b).</summary>
public enum ChecksMode
{
    /// <summary><em>Översikt</em>: a short list with <em>Gå igenom N att avgöra</em>.</summary>
    Overview,

    /// <summary>One suggestion at a time, with the decision bar at the bottom.</summary>
    Review,

    /// <summary><em>Allt avgjort</em>: the ring, a summary line and the log of the decisions.</summary>
    Settled,
}

/// <summary>Where a suggestion in the round stands, as a segment of the progress bar.</summary>
public enum SegmentState
{
    /// <summary>Decided (green).</summary>
    Decided,

    /// <summary>The one shown now (accent).</summary>
    Current,

    /// <summary>Left to decide (faint).</summary>
    Left,
}

/// <summary>A segment of the progress bar at the top of the panel.</summary>
/// <param name="State">Decided, shown now or left.</param>
/// <param name="Tip">What it is, on hover.</param>
public sealed record ProgressSegment(SegmentState State, string Tip);

/// <summary>A sort the form for an own area offers.</summary>
/// <param name="Sort">The sort.</param>
/// <param name="Name">Its name in the form.</param>
public sealed record SortChoice(AreaSort Sort, string Name);

/// <summary>A colour the form for an own area offers.</summary>
/// <param name="Tint">The tint.</param>
/// <param name="TintKey">Its resource key.</param>
/// <param name="IsChosen">Whether it is the form's colour now.</param>
public sealed record TintChoice(AreaTint Tint, string TintKey, bool IsChosen)
{
    /// <summary>Gets the colour's name, in Swedish.</summary>
    public string Name => AreaLooks.TintName(Tint);
}

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
/// The checks and suggestions (sketch KF2b) and the areas behind the actors (sketch KF1b) for the graph shown: runs
/// the analyses in the background when the graph is read, shows the suggestions one at a time as a queue, seals the
/// user's decisions (approve, dismiss and take back) through the Checks module, keeps <em>Senare</em> in memory only,
/// and draws, names and removes the user's own areas.
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

    /// <summary>What the form says when the own area it renames or removes is gone already.</summary>
    public const string GoneText = "Ytan finns inte längre i ärendet.";

    /// <summary>The toast's action that takes a decision back.</summary>
    public const string UndoText = "Ångra · Ctrl+Z";

    /// <summary>The toast that says a decision was taken back.</summary>
    public const string UndoneText = "Beslutet ångrat";

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

    /// <summary>
    /// The sorts whose areas are drawn when no row is chosen: none until the user picks them in the menu (TODO 34,
    /// principle 7), since every area drawn at once lay on top of each other; a check's area is otherwise drawn only
    /// for the suggestion shown.
    /// </summary>
    private readonly HashSet<AreaSort> _shown = [];

    /// <summary>The actors picked with Ctrl+click or a box, for an own area and for Delete.</summary>
    private readonly GraphSelection _picked = new();

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

    /// <summary>The actor the inspector shows, whose <em>Här</em> follows the checks.</summary>
    private string? _inspected;

    /// <summary>The queue's order of the suggestions that wait; <em>Senare</em> moves one to the back.</summary>
    private readonly List<string> _order = [];

    /// <summary>The suggestions of this round through the queue, by key, for the progress bar.</summary>
    private readonly List<string> _round = [];

    /// <summary>
    /// The decisions the last approve or dismiss sealed, with the case they are in, which Ctrl+Z takes back; null when
    /// none.
    /// </summary>
    private MadeDecisions? _undo;

    /// <summary>
    /// The suggestions decided since the checks were last read in full, so that a run that was overtaken never shows
    /// one of them as waiting again (it could be decided twice).
    /// </summary>
    private readonly HashSet<string> _justDecided = new(StringComparer.Ordinal);

    /// <summary>The run's version when the last decision was sealed; a run after it reads the decisions.</summary>
    private int _decidedAt;

    /// <summary>When the checks last ran, or <see langword="null"/>.</summary>
    private DateTimeOffset? _ranUtc;

    /// <summary>The areas drawn now, worked out when something changes rather than on every drawing.</summary>
    private IReadOnlyList<AreaView> _areas = [];

    /// <summary>The candidates' lines drawn now, worked out with the areas.</summary>
    private IReadOnlyList<CandidateLine> _lines = [];

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

    /// <summary>Raised when a decision is stored, for a toast with <em>Ångra</em> (sketch TO1a).</summary>
    public event EventHandler<ToastRequest>? ToastWanted;

    /// <summary>Gets or sets the time zone the time areas are written in; the computer's by default.</summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Local;

    /// <summary>Gets whether the panel is folded out where the inspector stands (Ctrl+J).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndoLast))]
    public partial bool IsOpen { get; private set; }

    /// <summary>Gets what the panel shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverview), nameof(IsReview), nameof(IsSettled))]
    public partial ChecksMode Mode { get; private set; }

    /// <summary>Gets whether the panel shows the overview.</summary>
    public bool IsOverview => Mode == ChecksMode.Overview;

    /// <summary>Gets whether the panel shows one suggestion at a time.</summary>
    public bool IsReview => Mode == ChecksMode.Review;

    /// <summary>Gets whether the panel says that all is decided.</summary>
    public bool IsSettled => Mode == ChecksMode.Settled;

    /// <summary>Gets the progress bar's segments: one per suggestion in this round.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ProgressSegment> Segments { get; private set; } = [];

    /// <summary>Gets where in the round the shown suggestion is, for example <c>1 av 3</c>.</summary>
    [ObservableProperty]
    public partial string PositionText { get; private set; } = string.Empty;

    /// <summary>Gets the ring's segments when all is decided: approved (green) or dismissed (faint).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<bool> Ring { get; private set; } = [];

    /// <summary>Gets the line under <em>Allt avgjort</em>, for example <c>2 godkända · 1 avfärdad · …</c>.</summary>
    [ObservableProperty]
    public partial string SettledText { get; private set; } = string.Empty;

    /// <summary>Gets whether the optional reason's field is folded out (<em>+ Skäl, frivilligt</em>).</summary>
    [ObservableProperty]
    public partial bool IsReasonOpen { get; private set; }

    /// <summary>Gets the overview's button, for example <c>Gå igenom 3 att avgöra</c>.</summary>
    public string StartText => $"Gå igenom {WaitingCount.ToString(CultureInfo.InvariantCulture)} att avgöra";

    /// <summary>Gets how many checks did their work by themselves, for the folded line.</summary>
    public string DoneCountText => Done.Count.ToString(CultureInfo.InvariantCulture);

    /// <summary>Gets whether anything did its work by itself, so that the folded line shows.</summary>
    public bool HasDone => Done.Count > 0;

    /// <summary>Gets whether the user has decided anything, so that the log's group shows.</summary>
    public bool HasDecided => Decided.Count > 0;

    /// <summary>Gets whether Ctrl+Z takes back a decision now.</summary>
    public bool CanUndoLast => _undo is not null && IsOpen;

    /// <summary>Gets the rows that wait for a decision (<em>Att avgöra</em>).</summary>
    public ObservableCollection<CheckRowViewModel> ToDecide { get; } = [];

    /// <summary>Gets the checks that did their work by themselves (<em>Gjort av sig självt</em>).</summary>
    public ObservableCollection<CheckRowViewModel> Done { get; } = [];

    /// <summary>Gets the suggestions the user has decided (<em>Avgjort</em>).</summary>
    public ObservableCollection<CheckRowViewModel> Decided { get; } = [];

    /// <summary>Gets the chosen row, or <see langword="null"/>.</summary>
    [ObservableProperty]
    public partial CheckRowViewModel? Selected { get; private set; }

    /// <summary>Gets how many suggestions wait for a decision, for the button <em>Förslag · N</em>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ButtonText), nameof(HasWaiting), nameof(ToolTipText), nameof(WaitingText), nameof(StartText))]
    public partial int WaitingCount { get; private set; }

    /// <summary>Gets the count on the button's badge.</summary>
    public string WaitingText => WaitingCount.ToString(CultureInfo.InvariantCulture);

    /// <summary>Gets whether something waits for the user, so that the corner's icon carries a dot.</summary>
    public bool HasWaiting => WaitingCount > 0;

    /// <summary>Gets the corner's icon's tip: what it is, how many wait and its key.</summary>
    public string ToolTipText => WaitingCount > 0
        ? $"Kontroller och förslag · {WaitingCount.ToString(CultureInfo.InvariantCulture)} att avgöra · Ctrl+J"
        : "Kontroller och förslag · Ctrl+J";

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
    public IReadOnlyList<AreaView> Areas => _areas;

    /// <summary>Gets the dashed lines to the candidates: every suggestion's, or the shown one's alone.</summary>
    public IReadOnlyList<CandidateLine> CandidateLines => _lines;

    /// <summary>
    /// Gets the actors the chosen row is about, which the graph draws clearly while it tones down the rest; null
    /// when the list is closed or no row is chosen.
    /// </summary>
    public IReadOnlySet<string>? Focus =>
        IsOpen && IsReview && Selected is { } row ? row.Nodes.ToHashSet(StringComparer.Ordinal) : null;

    /// <summary>
    /// Gets the actors picked with Ctrl+click or with a box on the empty surface: an own area is drawn around them (Z)
    /// and Delete throws them.
    /// </summary>
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

    /// <summary>Gets whether any areas are drawn, so that the corner's icon carries a dot.</summary>
    public bool HasAreas => Areas.Count > 0;

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

    /// <summary>
    /// Folds the panel out or in (Ctrl+J): out at the overview while something waits, else at <em>Allt avgjort</em>.
    /// </summary>
    [RelayCommand]
    public void Toggle()
    {
        IsOpen = !IsOpen;
        if (IsOpen)
        {
            ShowOverview();
        }

        RaiseChanged();
    }

    /// <summary>Folds the panel in.</summary>
    public void Close()
    {
        if (IsOpen)
        {
            IsOpen = false;
            RaiseChanged();
        }
    }

    /// <summary>Shows the overview, or <em>Allt avgjort</em> when nothing waits (the folded line's click).</summary>
    [RelayCommand]
    public void ShowOverview()
    {
        Mode = ToDecide.Count > 0 ? ChecksMode.Overview : ChecksMode.Settled;
        Choose(null);
    }

    /// <summary>
    /// Starts going through the queue (<em>Gå igenom N att avgöra</em>, Enter in the overview), at a row or at the
    /// first that waits.
    /// </summary>
    /// <param name="row">The row to start at, or <see langword="null"/> for the first.</param>
    [RelayCommand]
    public void StartReview(CheckRowViewModel? row)
    {
        if (ToDecide.Count == 0)
        {
            return;
        }

        _round.Clear();
        _round.AddRange(ToDecide.Select(each => each.Key));
        IsOpen = true;
        Mode = ChecksMode.Review;
        Choose(row is { CanDecide: true } ? row : ToDecide[0]);
    }

    /// <summary>
    /// A row in the overview or the log was clicked: a suggestion that waits is gone through from there; a check
    /// shows its actors in the graph.
    /// </summary>
    /// <param name="row">The row.</param>
    [RelayCommand]
    public void Select(CheckRowViewModel? row)
    {
        if (row is { CanDecide: true })
        {
            StartReview(row);
        }
        else if (row is not null)
        {
            ShowWanted?.Invoke(this, row.Nodes);
        }
    }

    /// <summary>Steps to the next suggestion that waits, in the round's order (the header's arrow).</summary>
    [RelayCommand]
    public void Next() => Step(1);

    /// <summary>Steps to the suggestion before (K).</summary>
    [RelayCommand]
    public void Previous() => Step(-1);

    /// <summary>
    /// <em>Senare</em> (J): moves the shown suggestion to the back of the queue without deciding anything, and shows
    /// the next. Nothing is written in the case.
    /// </summary>
    [RelayCommand]
    public void Later()
    {
        if (!IsReview || Selected is not { } row)
        {
            return;
        }

        var at = _round.IndexOf(row.Key);
        _round.Remove(row.Key);
        _round.Add(row.Key);
        _order.Remove(row.Key);
        _order.Add(row.Key);
        Reorder();
        Choose(NextWaiting(Math.Max(0, at)) ?? row);
    }

    /// <summary>Chooses the candidate Enter approves in the shown suggestion (a click in the table).</summary>
    /// <param name="candidate">The candidate.</param>
    [RelayCommand]
    public void ChooseCandidate(CandidateRowViewModel candidate)
    {
        Selected?.Choose(candidate);
        RaiseChanged();
    }

    /// <summary>Folds the optional reason's field out or in (<em>+ Skäl, frivilligt</em>).</summary>
    [RelayCommand]
    public void ToggleReason() => IsReasonOpen = !IsReasonOpen;

    /// <summary>
    /// Enter: in the overview starts going through the queue; in a suggestion approves its chosen candidate, or the
    /// whole when it has none.
    /// </summary>
    /// <returns>A task that completes when the decision is stored and the checks read again.</returns>
    [RelayCommand]
    public Task ApproveAsync()
    {
        if (IsOpen && IsOverview)
        {
            StartReview(null);
            return Task.CompletedTask;
        }

        return IsReview && Selected?.ApproveRequest() is { } request ? DecideAsync([request]) : Task.CompletedTask;
    }

    /// <summary>
    /// X: dismisses the shown suggestion as a whole, or the candidates that still wait once one is approved.
    /// </summary>
    /// <returns>A task that completes when the decision is stored and the checks read again.</returns>
    [RelayCommand]
    public Task DismissAsync() =>
        IsReview && Selected?.DismissRequests() is { Count: > 0 } requests
            ? DecideAsync(requests)
            : Task.CompletedTask;

    /// <summary>
    /// Ctrl+Z while the panel is open: takes back the last decision, sealed in the case. Closed, Ctrl+Z does nothing
    /// here, so that undoing something else in the graph never takes back a decision without a word.
    /// </summary>
    /// <returns>A task that completes when it is stored and the checks read again.</returns>
    [RelayCommand]
    public Task UndoAsync() => IsOpen && _undo is { } last ? TakeBackAsync(last) : Task.CompletedTask;

    /// <summary><em>Ångra</em> on a row in the log: takes back every decision in force about it.</summary>
    /// <param name="row">The row.</param>
    /// <returns>A task that completes when it is stored and the checks read again.</returns>
    [RelayCommand]
    public Task UndoRowAsync(CheckRowViewModel? row) =>
        row is { CanUndo: true } && _session.Current is { } openCase && row.InForce() is { Count: > 0 } decisions
            ? TakeBackAsync(new MadeDecisions(openCase, decisions))
            : Task.CompletedTask;

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
            $"{c.ScoreText} · {c.ShortAddress}: {c.Candidate.Reason}", TopicOf(row.Check))));
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

    /// <summary>Closes the menu of areas, for example after a click outside it.</summary>
    public void CloseMenu() => IsMenuOpen = false;

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
        _picked.Toggle(node);
        RaiseChanged();
    }

    /// <summary>Says whether an actor is picked, at no cost, for the surface that asks for every node.</summary>
    /// <param name="node">The actor's key.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool IsPicked(string node) => _picked.Contains(node);

    /// <summary>Makes the pick exactly the given actors, as a box drawn on the surface picks them.</summary>
    /// <param name="nodes">The actors, in their order.</param>
    public void SetPicked(IReadOnlyList<string> nodes)
    {
        if (_picked.SetTo(nodes))
        {
            RaiseChanged();
        }
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
            && ToDecide.FirstOrDefault(row => row.Key == check) is { } row)
        {
            StartReview(row);
            RaiseChanged();
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
            var stored = await StoreAsync(openCase, scene, mode);
            if (_session.Current != openCase)
            {
                return;
            }

            if (!stored)
            {
                FormProblem = GoneText;
                var still = await _source.GetAreasAsync(openCase, CancellationToken.None);
                if (_session.Current == openCase)
                {
                    _own = still;
                    Rebuild();
                }

                return;
            }

            if (mode == AreaFormMode.Create)
            {
                // The user just drew it, so its sort is shown, as if picked in the menu.
                _shown.Add(FormSort);
                _picked.Clear();
            }

            Form = AreaFormMode.None;
            FormProblem = string.Empty;
            _formArea = null;
            ChosenArea = null;
            var own = await _source.GetAreasAsync(openCase, CancellationToken.None);
            if (_session.Current == openCase)
            {
                _own = own;
                Rebuild();
            }
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
            _ranUtc = run.RanUtc;
            if (version > _decidedAt)
            {
                // The run started after the last decision was sealed, so it reads every decision there is.
                _justDecided.Clear();
            }

            StatusText = string.Create(
                CultureInfo.InvariantCulture,
                $"Kördes {Stamp(run.RanUtc)} på {run.Transactions} tx") + AccountNote(scene);
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

    /// <summary>
    /// What the list says when the graph has Ethereum: the analyses are written for the UTXO model (CoinJoin, peel
    /// chain, the mixer's candidates and the wallets from Clustering) and are not run on an account chain.
    /// </summary>
    public const string AccountChainsText =
        "Kontrollerna gäller UTXO-modellen (Bitcoin) och körs inte på Ethereum än; "
        + "Ethereum-delen är inte kontrollerad.";

    /// <summary>Says, after the status, that the checks did not run on the graph's Ethereum part.</summary>
    /// <param name="scene">The graph.</param>
    /// <returns>The note with a leading separator, or an empty text.</returns>
    public static string AccountNote(GraphScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.Nodes.Any(node => node.Node.Chain != Chain.Bitcoin) ? ". " + AccountChainsText : string.Empty;
    }

    /// <summary>Says which checks concern an actor, for the inspector's <em>Här</em>.</summary>
    /// <param name="node">The chosen actor, or <see langword="null"/>.</param>
    public void Inspect(string? node)
    {
        _inspected = node;
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
        "mixer-parts" or "mixer-chain" => GuideTopic.MixerChain,
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

    /// <summary>
    /// Seals an approve or a dismiss about the shown suggestion, reads the checks again, and says so in a toast with
    /// <em>Ångra</em>. A suggestion that still waits after an approval stays shown with its next candidate chosen, since
    /// a mixer pays out in several parts; otherwise the next that waits is shown (or <em>Allt avgjort</em>).
    /// </summary>
    /// <param name="requests">The decisions, sealed together.</param>
    /// <returns>A task that completes when they are stored.</returns>
    private async Task DecideAsync(IReadOnlyList<DecisionRequest> requests)
    {
        if (IsBusy || Selected is not { } row || _justDecided.Contains(row.Key)
            || _session.Current is not { } openCase)
        {
            return;
        }

        var toast = ToastOf(row, requests);
        var at = Math.Max(0, _round.IndexOf(row.Key));
        var settles = WouldSettle(row, requests);
        if (await StoreAsync(openCase, requests, settles ? row.Key : null) is { } made)
        {
            var decided = new MadeDecisions(openCase, made);
            _undo = decided;
            OnPropertyChanged(nameof(CanUndoLast));
            IsReasonOpen = false;
            if (!settles && ToDecide.FirstOrDefault(each => each.Key == row.Key) is { } again)
            {
                Choose(again);
            }
            else
            {
                Advance(at);
            }

            ToastWanted?.Invoke(this, toast with { Action = () => _ = TakeBackAsync(decided) });
        }
    }

    /// <summary>
    /// Takes decisions back, sealed in the case, and shows the suggestion again where it waits. Only the decisions
    /// still in force in the case they were made in are taken back: an old <em>Ångra</em> after a newer decision, or
    /// after another case was opened, does nothing.
    /// </summary>
    /// <param name="decided">The decisions to take back, with their case.</param>
    /// <returns>A task that completes when it is stored and the checks read again.</returns>
    private async Task TakeBackAsync(MadeDecisions decided)
    {
        if (decided.Case != _session.Current || decided.Decisions.Count == 0)
        {
            return;
        }

        var first = decided.Decisions[0];
        var key = GraphAreas.KeyOf(first.Analysis, first.FindingKey);
        var inForce = decided.Decisions.Where(StillInForce).ToList();
        if (inForce.Count == 0)
        {
            // Taken back already, from the log or another toast: nothing is left to undo.
            ForgetUndo(key);
            return;
        }

        if (await StoreAsync(decided.Case, [.. inForce.Select(DecisionRequest.TakeBack)], null) is null)
        {
            return;
        }

        ForgetUndo(key);
        ToastWanted?.Invoke(this, new ToastRequest(ToastKind.Information, UndoneText));
        if (ToDecide.FirstOrDefault(row => row.Key == key) is { } again && IsOpen)
        {
            if (!IsReview)
            {
                _round.Clear();
                _round.AddRange(ToDecide.Select(each => each.Key));
                Mode = ChecksMode.Review;
            }

            Choose(again);
        }
    }

    /// <summary>
    /// Says whether a decision is still the one in force about its subject, as the checks were last read.
    /// </summary>
    /// <param name="decision">The decision.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private bool StillInForce(CheckDecision decision) =>
        _checks.FirstOrDefault(check => check.Analysis == decision.Analysis
                && check.Finding.Key == decision.FindingKey)
            ?.DecisionFor(decision.CandidateKey)?.Sequence == decision.Sequence;

    /// <summary>Forgets what Ctrl+Z would take back when it concerns a suggestion just taken back.</summary>
    /// <param name="key">The suggestion's key.</param>
    private void ForgetUndo(string key)
    {
        if (_undo is { } last
            && GraphAreas.KeyOf(last.Decisions[0].Analysis, last.Decisions[0].FindingKey) == key)
        {
            _undo = null;
            OnPropertyChanged(nameof(CanUndoLast));
        }
    }

    /// <summary>
    /// Stores decisions in one transaction and reads the checks again; busy until then, so that a second Enter or X
    /// never decides the same suggestion twice.
    /// </summary>
    /// <param name="openCase">The case the decisions are made in.</param>
    /// <param name="requests">The decisions.</param>
    /// <param name="decidedKey">
    /// The suggestion an approve or a dismiss settles, kept from being shown as waiting until the checks are read
    /// again; <see langword="null"/> for a take-back.
    /// </param>
    /// <returns>The sealed decisions, or <see langword="null"/> when they could not be stored.</returns>
    private async Task<IReadOnlyList<CheckDecision>?> StoreAsync(
        ICase openCase, IReadOnlyList<DecisionRequest> requests, string? decidedKey)
    {
        if (IsBusy || requests.Count == 0 || _session.Current != openCase)
        {
            return null;
        }

        IsBusy = true;
        try
        {
            IReadOnlyList<CheckDecision> made;
            try
            {
                made = await _source.DecideAsync(openCase, requests);
                Problem = string.Empty;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Problem = SaveFailedText;
                return null;
            }

            if (decidedKey is not null)
            {
                _justDecided.Add(decidedKey);
            }

            _decidedAt = _version;
            await FollowAsync(_scene);
            return _session.Current == openCase ? made : null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Shows the next suggestion that waits after a decision, or <em>Allt avgjort</em> when none does.
    /// </summary>
    /// <param name="at">Where in the round the decided suggestion stood.</param>
    private void Advance(int at)
    {
        if (NextWaiting(at) is { } next)
        {
            Choose(next);
            return;
        }

        Mode = ChecksMode.Settled;
        Choose(null);
    }

    /// <summary>
    /// Says whether decisions settle a suggestion: a dismissal of the whole or of the rest, or an approval of the last
    /// candidate that waits; one approval among several leaves it waiting.
    /// </summary>
    /// <param name="row">The suggestion.</param>
    /// <param name="requests">The decisions.</param>
    /// <returns><see langword="true"/> when nothing waits after them.</returns>
    private static bool WouldSettle(CheckRowViewModel row, IReadOnlyList<DecisionRequest> requests) =>
        requests.Any(request => request.CandidateKey is null)
        || row.Candidates.Count(c => c.IsWaiting) <= requests.Count;

    /// <summary>Gives the toast that says decisions were stored, without its action.</summary>
    /// <param name="row">The suggestion.</param>
    /// <param name="requests">The decisions.</param>
    /// <returns>The toast.</returns>
    private static ToastRequest ToastOf(CheckRowViewModel row, IReadOnlyList<DecisionRequest> requests)
    {
        var request = requests[0];
        if (request.Verdict == CheckVerdict.Dismissed)
        {
            var title = request.CandidateKey is null ? "Förslaget avfärdat" : "Resten avfärdat";
            return new ToastRequest(ToastKind.Success, title, row.Title, UndoText);
        }

        return row.ChosenCandidate is { } chosen
            ? new ToastRequest(
                ToastKind.Success,
                row.Words.CandidateNoun + " godkänd",
                $"{chosen.ShortAddress} · {chosen.AmountText} BTC",
                UndoText)
            : new ToastRequest(ToastKind.Success, "Förslaget godkänt", row.Title, UndoText);
    }

    /// <summary>Stores what the form says through the module.</summary>
    /// <param name="openCase">The open case.</param>
    /// <param name="scene">The graph.</param>
    /// <param name="mode">What the form does.</param>
    /// <returns><see langword="false"/> when the area is gone already.</returns>
    private async Task<bool> StoreAsync(ICase openCase, GraphScene scene, AreaFormMode mode)
    {
        var note = string.IsNullOrWhiteSpace(FormNote) ? null : FormNote.Trim();
        switch (mode)
        {
            case AreaFormMode.Create:
                await _source.AddAreaAsync(openCase, new OwnAreaDraft(
                    FormName.Trim(), FormSort, FormTint, FormSameActor, note, GraphAreas.MembersOf(scene, _picked)));
                return true;
            case AreaFormMode.Rename when _formArea is { } area:
                return await _source.RenameAreaAsync(openCase, area.Id, FormName.Trim()) is not null;
            case AreaFormMode.Remove when _formArea is { } area:
                return await _source.RemoveAreaAsync(openCase, area.Id, note);
            default:
                return false;
        }
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

    /// <summary>Steps among the suggestions that wait, in the round's order; stops at the ends.</summary>
    /// <param name="step">One forward or back.</param>
    private void Step(int step)
    {
        var waiting = _round.Where(key => ToDecide.Any(row => row.Key == key)).ToList();
        if (!IsReview || waiting.Count == 0)
        {
            return;
        }

        var at = Selected is null ? -1 : waiting.IndexOf(Selected.Key);
        var next = at < 0 ? 0 : Math.Clamp(at + step, 0, waiting.Count - 1);
        Choose(ToDecide.First(row => row.Key == waiting[next]));
    }

    /// <summary>Gives the first suggestion that waits from a place in the round on, going round to the start.</summary>
    /// <param name="from">The place in the round.</param>
    /// <returns>The suggestion, or <see langword="null"/> when none waits.</returns>
    private CheckRowViewModel? NextWaiting(int from)
    {
        var keys = _round.Skip(from).Concat(_round.Take(from));
        return keys.Select(key => ToDecide.FirstOrDefault(row => row.Key == key && Waits(row)))
                .FirstOrDefault(row => row is not null)
            ?? ToDecide.FirstOrDefault(Waits);
    }

    /// <summary>
    /// Says whether a row waits for a decision and was not just decided (see <see cref="_justDecided"/>).
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns><see langword="true"/> when it can be decided now.</returns>
    private bool Waits(CheckRowViewModel row) => row.CanDecide && !_justDecided.Contains(row.Key);

    /// <summary>Shows a suggestion in the panel, or none, and follows it with the progress and the graph.</summary>
    /// <param name="row">The suggestion, or <see langword="null"/>.</param>
    private void Choose(CheckRowViewModel? row)
    {
        foreach (var each in Rows())
        {
            each.IsSelected = each == row;
        }

        if (row != Selected)
        {
            IsReasonOpen = false;
        }

        Selected = row;
        UpdateProgress();
        RaiseChanged();
    }

    /// <summary>Works out the progress bar and where in the round the shown suggestion is.</summary>
    private void UpdateProgress()
    {
        Segments = [.. _round.Select(SegmentOf)];
        var at = Selected is null ? -1 : _round.IndexOf(Selected.Key);
        PositionText = at < 0
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"{at + 1} av {_round.Count}");
    }

    /// <summary>Gives a suggestion's segment in the progress bar.</summary>
    /// <param name="key">The suggestion's key.</param>
    /// <returns>Shown now, left or decided.</returns>
    private ProgressSegment SegmentOf(string key)
    {
        if (key == Selected?.Key)
        {
            return new ProgressSegment(SegmentState.Current, "Nu");
        }

        return ToDecide.Any(row => row.Key == key)
            ? new ProgressSegment(SegmentState.Left, "Kvar")
            : new ProgressSegment(SegmentState.Decided, "Avgjort");
    }

    /// <summary>Gives a suggestion's place in the queue; one the queue has not seen yet goes last.</summary>
    /// <param name="key">The suggestion's key.</param>
    /// <returns>The place.</returns>
    private int PlaceOf(string key)
    {
        var at = _order.IndexOf(key);
        return at < 0 ? int.MaxValue : at;
    }

    /// <summary>Puts the rows that wait in the queue's order, new ones last in the order the run gave them.</summary>
    private void Reorder()
    {
        var ordered = ToDecide.OrderBy(row => PlaceOf(row.Key)).ToList();
        ToDecide.Clear();
        foreach (var row in ordered)
        {
            ToDecide.Add(row);
        }

        _order.Clear();
        _order.AddRange(ordered.Select(row => row.Key));
    }

    /// <summary>Gives every row in the list's order.</summary>
    /// <returns>The rows.</returns>
    private IEnumerable<CheckRowViewModel> Rows() => ToDecide.Concat(Done).Concat(Decided);

    /// <summary>
    /// Builds the rows, the areas and the menu again from the last run, keeping the queue's order, the round and the
    /// shown suggestion.
    /// </summary>
    private void Rebuild()
    {
        var selected = Selected?.Key;
        if (_scene is { } scene)
        {
            _picked.KeepWhere(key => scene.Node(key) is not null);
        }
        else
        {
            _picked.Clear();
        }

        ToDecide.Clear();
        Done.Clear();
        Decided.Clear();
        AllAreas = _scene is { } shown ? GraphAreas.Build(shown, _checks, _own, TimeZone) : [];
        foreach (var check in _checks)
        {
            var area = AllAreas.FirstOrDefault(a => a.CheckKey == GraphAreas.KeyOf(check));
            var row = new CheckRowViewModel(check, AnchorName(check), NodesOf(check, area), Incoming(check), TimeZone);
            if (row.Group == CheckRowGroup.Done && check.Finding.Area != AreaSort.Obfuscation)
            {
                // Wallets are areas only (sketch KF1b); the panel holds what obfuscates and what waits.
                continue;
            }

            Target(row.Group).Add(row);
        }

        Reorder();
        if (IsReview)
        {
            _round.AddRange(ToDecide.Select(row => row.Key).Where(key => !_round.Contains(key)));
        }

        WaitingCount = ToDecide.Count;
        Selected = Rows().FirstOrDefault(row => row.Key == selected);
        if (Selected is { } again)
        {
            again.IsSelected = true;
        }

        if (IsOpen && IsReview && (Selected is null || !Waits(Selected)))
        {
            Advance(0);
        }

        if (IsOpen && IsOverview && ToDecide.Count == 0)
        {
            Mode = ChecksMode.Settled;
        }

        if (ChosenArea is { } chosen && AllAreas.All(area => area.Key != chosen))
        {
            ChosenArea = null;
        }

        Inspect(_inspected);
        UpdateProgress();
        UpdateSettled();
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
        OnPropertyChanged(nameof(DoneCountText));
        OnPropertyChanged(nameof(HasDone));
        OnPropertyChanged(nameof(HasDecided));
        RaiseChanged();
    }

    /// <summary>
    /// Works out the ring and the line under <em>Allt avgjort</em>: how many the user approved and dismissed, and
    /// since when nothing new has come.
    /// </summary>
    private void UpdateSettled()
    {
        Ring = [.. Decided.Select(row => row.IsApproved)];
        var approved = Decided.Count(row => row.IsApproved);
        var dismissed = Decided.Count - approved;
        var parts = new List<string>();
        if (approved > 0)
        {
            parts.Add(Counted(approved, "godkänd", "godkända"));
        }

        if (dismissed > 0)
        {
            parts.Add(Counted(dismissed, "avfärdad", "avfärdade"));
        }

        if (parts.Count == 0)
        {
            parts.Add("Inget att avgöra");
        }

        if (_checks.Count > 0)
        {
            var newest = _checks.Max(check => check.RecordedUtc);
            parts.Add("inget nytt sedan " + Clock(newest));
        }

        SettledText = string.Join(" · ", parts);
    }

    /// <summary>Writes a count with its word in the singular or the plural.</summary>
    /// <param name="count">The count.</param>
    /// <param name="one">The word for one.</param>
    /// <param name="many">The word for more than one.</param>
    /// <returns>For example <c>2 godkända</c>.</returns>
    private static string Counted(int count, string one, string many) =>
        count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? one : many);

    /// <summary>Writes a time as the summary says it: the clock when it is the run's day, else with the date.</summary>
    /// <param name="utc">The time.</param>
    /// <returns>For example <c>14:40</c>.</returns>
    private string Clock(DateTimeOffset utc)
    {
        var local = TimeZoneInfo.ConvertTime(utc, TimeZone);
        var day = _ranUtc is { } ran ? TimeZoneInfo.ConvertTime(ran, TimeZone).Date : local.Date;
        var format = local.Date == day ? "HH:mm" : "yyyy-MM-dd HH:mm";
        return local.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gives what came in to a suggestion, as the graph knows it: the deposit's output the finding is about (its key is
    /// <c>txid:index</c>), or, for a finding keyed otherwise, what the first transaction it rests on paid to its first
    /// address.
    /// </summary>
    /// <param name="check">The suggestion.</param>
    /// <returns>The amount, or <see langword="null"/> when it is not a suggestion with candidates or not
    /// known.</returns>
    private Satoshi? Incoming(StoredCheck check)
    {
        var finding = check.Finding;
        if (_scene is not { } scene || finding.Candidates.Count == 0 || finding.Addresses.Count == 0
            || finding.Basis.Count == 0
            || !scene.Snapshot.Graph.Transactions.TryGetValue(finding.Basis[0].Txid, out var known))
        {
            return null;
        }

        var outputs = known.Transaction.Outputs;
        if (OutputIndexOf(finding.Key, finding.Basis[0].Txid) is { } index)
        {
            return outputs.FirstOrDefault(output => output.Index == index && output.Address == finding.Addresses[0])
                ?.Amount;
        }

        var paid = outputs.Where(output => output.Address == finding.Addresses[0]).ToList();
        return paid.Count == 0 ? null : new Satoshi(paid.Sum(output => output.Amount.Value));
    }

    /// <summary>Reads the output's index from a finding keyed <c>txid:index</c> on the given transaction.</summary>
    /// <param name="key">The finding's key.</param>
    /// <param name="txid">The transaction it rests on first.</param>
    /// <returns>The index, or <see langword="null"/> when the key is not of that form.</returns>
    private static int? OutputIndexOf(string key, Txid txid)
    {
        var colon = key.LastIndexOf(':');
        return colon > 0 && key[..colon] == txid.Value
            && int.TryParse(key[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                ? index
                : null;
    }

    /// <summary>
    /// Gives a row's actors: its area's, or, when it draws no area (a suggestion dismissed as a whole), every actor
    /// it was about, so that the log and <em>Här</em> still find it.
    /// </summary>
    /// <param name="check">The check.</param>
    /// <param name="area">Its area, or <see langword="null"/>.</param>
    /// <returns>The actors' keys.</returns>
    private IReadOnlyList<string> NodesOf(StoredCheck check, AreaView? area)
    {
        if (area is not null)
        {
            return area.Nodes;
        }

        return _scene is { } scene
            ? GraphAreas.NodesOf(scene, check.Finding.Addresses, check.Finding.Transactions)
            : [];
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
        _order.Clear();
        _round.Clear();
        _undo = null;
        _justDecided.Clear();
        _ranUtc = null;
        Form = AreaFormMode.None;
        IsOpen = false;
        Mode = ChecksMode.Overview;
        IsReasonOpen = false;
        IsMenuOpen = false;
        ChosenArea = null;
        StatusText = NotRunText;
        Problem = string.Empty;
        _inspected = null;
        Rebuild();
    }

    /// <summary>
    /// Works out the areas drawn: the chosen row's alone while the list is open, else the sorts shown.
    /// </summary>
    /// <returns>The areas.</returns>
    private IReadOnlyList<AreaView> ShownAreas()
    {
        if (IsOpen && IsReview && Selected is { } row)
        {
            return
            [
                .. AllAreas.Where(area => area.CheckKey == row.Key).Select(area => area with { IsFilled = true }),
            ];
        }

        return GraphAreas.Fill(AllAreas.Where(area => _shown.Contains(area.Sort)));
    }

    /// <summary>Works out the dashed lines to the candidates: every suggestion's, or the shown one's alone.</summary>
    /// <returns>The lines.</returns>
    private IReadOnlyList<CandidateLine> ShownLines()
    {
        if (_scene is not { } scene)
        {
            return [];
        }

        var graph = scene.Snapshot.Graph;
        var lines = new List<CandidateLine>();
        // The candidates' dashed lines do not wait for the menu: an approved way out of a mixer stays in the graph
        // whether or not its area is drawn; while a suggestion is shown, only its own lines are.
        var reviewing = IsOpen && IsReview && Selected is not null;
        foreach (var area in (reviewing ? _areas : AllAreas).Where(area => area.CheckKey is not null))
        {
            var check = _checks.FirstOrDefault(c => GraphAreas.KeyOf(c) == area.CheckKey);
            if (check is null || check.Finding.Candidates.Count == 0 || check.Finding.Addresses.Count == 0
                || graph.NodeOf(check.Finding.Addresses[0]) is not { } from)
            {
                continue;
            }

            // The suggestion shown in the panel gives its own rows, so that the chosen candidate's line stands out.
            var shown = Selected is { } chosen && chosen.Key == area.CheckKey
                ? chosen
                : new CheckRowViewModel(check, null, []);
            foreach (var row in shown.Candidates.Where(c => !c.IsDismissed))
            {
                if (graph.NodeOf(row.Candidate.Address) is { } to && to.Key != from.Key)
                {
                    lines.Add(new CandidateLine(
                        from.Key, to.Key, row.Candidate.Score, row.ScoreText, row.IsApproved, row.Rank, row.IsChosen));
                }
            }
        }

        return lines;
    }

    /// <summary>Says that the areas, the focus or the picked actors changed.</summary>
    private void RaiseChanged()
    {
        _areas = ShownAreas();
        _lines = ShownLines();
        OnPropertyChanged(nameof(Areas));
        OnPropertyChanged(nameof(Focus));
        OnPropertyChanged(nameof(AreaCountText));
        OnPropertyChanged(nameof(HasAreas));
        OnPropertyChanged(nameof(Picked));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Decisions sealed together, with the case they are sealed in, which <em>Ångra</em> takes back.</summary>
/// <param name="Case">The case.</param>
/// <param name="Decisions">The sealed decisions.</param>
internal sealed record MadeDecisions(ICase Case, IReadOnlyList<CheckDecision> Decisions);
