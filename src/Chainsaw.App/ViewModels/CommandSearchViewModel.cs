using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.App.Links;
using Chainsaw.App.Search;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The search in the command bar (sketch KR1a): what is written finds the case's labels, addresses, transactions and
/// clusters and the Guide's sections, with <em>Bästa träff</em> at the top and the rest in groups. The chosen row is
/// shown at once, in the graph or in the Guide's panel, and Enter takes it there. An address or a tx the case does not
/// have can be added as a start point, and the row says honestly that nothing is fetched yet and through which exit.
/// The search runs off the UI thread over an index built once per graph, and a newer search overtakes an older one.
/// </summary>
public sealed partial class CommandSearchViewModel : ObservableObject
{
    /// <summary>The command that searches only the Guide, as in <c>/guide utxo</c>.</summary>
    public const string GuideCommand = "/guide";

    /// <summary>What Enter does with a row in the graph, in the bar's foot.</summary>
    public const string ShowInGraphText = "visar i grafen";

    /// <summary>What Enter does with a Guide row.</summary>
    public const string OpenGuideText = "öppnar Guiden";

    /// <summary>What Enter does with a row that lies in the trash.</summary>
    public const string OpenTrashText = "öppnar soptunnan";

    /// <summary>What Enter does on a row that lies in the trash and can be put back from the row.</summary>
    public const string PutBackText = "lägger tillbaka";

    /// <summary>What Enter does with an address or a tx the case does not have.</summary>
    public const string AddText = "lägger till";

    /// <summary>What Enter does with a row the graph does not show: nothing but close the bar.</summary>
    public const string CloseText = "stänger";

    /// <summary>What is said when no case is open, so that only the Guide is searched.</summary>
    public const string NoCaseText = "Inget ärende är öppet, så bara Guiden söks.";

    /// <summary>The Guide's sections.</summary>
    private readonly GuideLibrary _library;

    /// <summary>Guards the graph and the index, which a search reads off the UI thread.</summary>
    private readonly Lock _gate = new();

    /// <summary>The open case's graph, or <see langword="null"/>.</summary>
    private GraphScene? _scene;

    /// <summary>The index over <see cref="_scene"/> and the Guide; <see langword="null"/> until built.</summary>
    private SearchIndex? _index;

    /// <summary>Stops the search under way when a newer one starts.</summary>
    private CancellationTokenSource? _running;

    /// <summary>Counts the searches, so that only the newest one's answer is shown.</summary>
    private int _generation;

    /// <summary>What was written last.</summary>
    private string _text = string.Empty;

    /// <summary>What was written for the answer shown now.</summary>
    private string _shown = string.Empty;

    /// <summary>The group folded out with Tab, or <see langword="null"/>.</summary>
    private SearchKind? _unfolded;

    /// <summary>Every row in the order the arrows go through them: the best hit, then the groups.</summary>
    private IReadOnlyList<SearchRowViewModel> _rows = [];

    /// <summary>Whether lookups can go now.</summary>
    private bool _isLive;

    /// <summary>The exit's name, for example <c>Stockholm</c> or <c>Windows</c>.</summary>
    private string _exitName = string.Empty;

    /// <summary>When lookups can go again, as the offline words say it.</summary>
    private string _until = OfflineWords.NoExitUntil;

    /// <summary>Creates the search over the Guide, with no case open.</summary>
    /// <param name="library">The Guide's sections.</param>
    public CommandSearchViewModel(GuideLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
    }

    /// <summary>
    /// Raised when the chosen row changes, so that it is shown at once: in the graph for a row with a node, in the
    /// Guide's panel for a Guide row; <see langword="null"/> when nothing is chosen any more.
    /// </summary>
    public event EventHandler<SearchRowViewModel?>? ShowWanted;

    /// <summary>Raised when a row is taken with Enter or a second click.</summary>
    public event EventHandler<SearchRowViewModel>? Accepted;

    /// <summary>Gets the best hit, drawn large at the top, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBest), nameof(IsShown))]
    public partial SearchRowViewModel? Best { get; private set; }

    /// <summary>Gets whether there is a best hit.</summary>
    public bool HasBest => Best is not null;

    /// <summary>Gets the groups under the best hit: Etiketter, Adresser, Tx, Kluster and Guide.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<SearchGroupViewModel> Groups { get; private set; } = [];

    /// <summary>Gets the chosen row, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnterText), nameof(FootEnterText), nameof(IsUnknownSelected))]
    public partial SearchRowViewModel? Selected { get; private set; }

    /// <summary>
    /// Gets whether what is written is an address or a tx that the case does not have, so that it can be added as a
    /// start point.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnterText), nameof(FootEnterText), nameof(IsUnknownSelected), nameof(IsShown))]
    public partial bool IsUnknown { get; private set; }

    /// <summary>Gets whether the start point's row is the one Enter takes.</summary>
    public bool IsUnknownSelected => IsUnknown && Selected is null;

    /// <summary>Gets what the start point's row says about what is fetched, and through which exit.</summary>
    [ObservableProperty]
    public partial string UnknownText { get; private set; } = string.Empty;

    /// <summary>Gets the chain what is written is recognised on, for example <c>₿ Bitcoin</c>, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChain))]
    public partial string ChainText { get; private set; } = string.Empty;

    /// <summary>Gets whether what is written is recognised on a chain.</summary>
    public bool HasChain => ChainText.Length > 0;

    /// <summary>Gets what is said when the case has nothing by that name, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmptyText), nameof(IsShown))]
    public partial string EmptyText { get; private set; } = string.Empty;

    /// <summary>Gets whether <see cref="EmptyText"/> is shown.</summary>
    public bool HasEmptyText => EmptyText.Length > 0;

    /// <summary>Gets whether the list under the field has anything to show.</summary>
    public bool IsShown => HasBest || IsUnknown || HasEmptyText;

    /// <summary>Gets what Enter does now, in the bar's foot.</summary>
    public string EnterText => Selected switch
    {
        { GuideSection: not null } => OpenGuideText,
        { NodeKey: not null } => ShowInGraphText,
        { Entry.TrashPlace: not null } => PutBackText,
        { Entry.IsInTrash: true } => OpenTrashText,
        not null => CloseText,
        _ when IsUnknown => AddText,
        _ => string.Empty,
    };

    /// <summary>Gets what the bar's foot says about Enter, for example <c>Enter visar i grafen</c>.</summary>
    public string FootEnterText => EnterText.Length > 0 ? "Enter " + EnterText : "Enter kör";

    /// <summary>Gets every row in the order the arrows go through them.</summary>
    public IReadOnlyList<SearchRowViewModel> Rows => _rows;

    /// <summary>Gets the newest search, which completes when its answer is shown or dropped.</summary>
    public Task Searching { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Uses the open case's graph. The index is built anew off the UI thread at the next search, and a search shown
    /// now is run again, so that the list never shows what the graph no longer has.
    /// </summary>
    /// <param name="scene">The graph, or <see langword="null"/> when no case is open.</param>
    public void UseScene(GraphScene? scene)
    {
        lock (_gate)
        {
            if (ReferenceEquals(scene, _scene))
            {
                return;
            }

            (_scene, _index) = (scene, null);
        }

        if (_text.Length > 0)
        {
            Searching = RunAsync(_text);
        }
    }

    /// <summary>Follows the way out, which the start point's row names.</summary>
    /// <param name="isLive">Whether lookups can go now.</param>
    /// <param name="name">The exit's name, for example <c>Stockholm</c> or <c>Windows</c>.</param>
    /// <param name="until">When lookups can go again, as the offline words say it.</param>
    public void FollowExit(bool isLive, string name, string until)
    {
        (_isLive, _exitName, _until) = (isLive, name ?? string.Empty, until ?? OfflineWords.NoExitUntil);
        if (IsUnknown)
        {
            UnknownText = StartPointWords(StartPointText.Parse(Parse(_text).Term));
        }
    }

    /// <summary>
    /// Searches for what is written. A command (a text that starts with <c>/</c>) empties the list, except
    /// <c>/guide</c>, which searches the Guide. A newer search overtakes this one, whose answer is then dropped.
    /// </summary>
    /// <param name="text">What is written in the bar.</param>
    /// <returns>A task that completes when the answer is shown or dropped.</returns>
    public Task SearchAsync(string? text)
    {
        var written = text ?? string.Empty;
        if (written != _text)
        {
            _unfolded = null;
        }

        Searching = RunAsync(written);
        return Searching;
    }

    /// <summary>
    /// Tab: folds out the chosen row's group when it has more than it shows, and otherwise goes on to the first row of
    /// the next group.
    /// </summary>
    /// <returns><see langword="true"/> when there were rows to act on.</returns>
    public bool Unfold()
    {
        if (Selected is not { } chosen)
        {
            return false;
        }

        var group = Groups.FirstOrDefault(group => group.Rows.Contains(chosen));
        if (group is { HasMore: true } && _unfolded != group.Kind)
        {
            _unfolded = group.Kind;
            Searching = RunAsync(_text);
            return true;
        }

        var index = group is null ? -1 : Groups.ToList().IndexOf(group);
        var next = Groups.Skip(index + 1).Concat(Groups).FirstOrDefault(other => other.Rows.Count > 0);
        if (next is not null)
        {
            Select(next.Rows[0]);
        }

        return true;
    }

    /// <summary>
    /// Moves the choice up or down (the arrows). Past the last row, an address or a tx the case does not have is
    /// chosen, so that Enter adds it.
    /// </summary>
    /// <param name="step">One down, or minus one up.</param>
    public void Move(int step)
    {
        var count = _rows.Count + (IsUnknown ? 1 : 0);
        if (count == 0)
        {
            return;
        }

        var at = Selected is { } chosen ? IndexOf(chosen) : _rows.Count;
        var next = Math.Clamp(at + step, 0, count - 1);
        Select(next < _rows.Count ? _rows[next] : null);
    }

    /// <summary>A click on a row: the first chooses it, a click on the chosen one takes it.</summary>
    /// <param name="row">The row.</param>
    [RelayCommand]
    public void Pick(SearchRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (ReferenceEquals(row, Selected))
        {
            TryAccept();
            return;
        }

        Select(row);
    }

    /// <summary>Takes the chosen row (Enter): the graph or the Guide shows it, and the bar closes.</summary>
    /// <returns><see langword="true"/> when a row was taken.</returns>
    public bool TryAccept()
    {
        if (Selected is not { } chosen)
        {
            return false;
        }

        Accepted?.Invoke(this, chosen);
        return true;
    }

    /// <summary>
    /// Gives whether the list answers this text, so that Enter pressed before a search has answered never takes a row
    /// from an older one.
    /// </summary>
    /// <param name="text">What is written.</param>
    /// <returns><see langword="true"/> when the list shown was found for it.</returns>
    public bool IsShowing(string text) => string.Equals(_shown, text ?? string.Empty, StringComparison.Ordinal);

    /// <summary>Shows the chosen row again, as when the bar comes up with what was written before.</summary>
    public void Reveal() => ShowWanted?.Invoke(this, Selected);

    /// <summary>Stops showing the chosen row, as when the bar is hidden without taking it.</summary>
    public void Dismiss()
    {
        _running?.Cancel();
        ShowWanted?.Invoke(this, null);
    }

    /// <summary>Reads what is written: the term, and whether only the Guide is searched.</summary>
    /// <param name="text">What is written.</param>
    /// <returns>The term, or <see langword="null"/> for a command; and whether only the Guide is searched.</returns>
    public static (string? Term, bool GuideOnly) Parse(string text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (!trimmed.StartsWith('/'))
        {
            return (trimmed, false);
        }

        var isGuide = trimmed.StartsWith(GuideCommand, StringComparison.OrdinalIgnoreCase)
            && (trimmed.Length == GuideCommand.Length || char.IsWhiteSpace(trimmed[GuideCommand.Length]));
        return isGuide ? (trimmed[GuideCommand.Length..].Trim(), true) : (null, false);
    }

    /// <summary>Runs a search and shows its answer, unless a newer one has started meanwhile.</summary>
    /// <param name="text">What is written.</param>
    /// <returns>A task that completes when the answer is shown or dropped.</returns>
    private async Task RunAsync(string text)
    {
        _text = text;
        _running?.Cancel();
        var generation = ++_generation;
        var (term, guideOnly) = Parse(text);
        if (string.IsNullOrEmpty(term))
        {
            _shown = text;
            Show(SearchOutcome.Nothing(term ?? string.Empty), guideOnly);
            return;
        }

        using var running = new CancellationTokenSource();
        _running = running;
        var unfolded = _unfolded;
        SearchOutcome outcome;
        try
        {
            outcome = await Task.Run(() => IndexNow().Search(term, guideOnly, unfolded, running.Token), running.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(_running, running))
            {
                _running = null;
            }
        }

        if (generation == _generation)
        {
            _shown = text;
            Show(outcome, guideOnly);
        }
    }

    /// <summary>Gives the index, building it over the graph and the Guide the first time it is asked for.</summary>
    /// <returns>The index.</returns>
    private SearchIndex IndexNow()
    {
        GraphScene? scene;
        lock (_gate)
        {
            if (_index is { } built)
            {
                return built;
            }

            scene = _scene;
        }

        var index = SearchIndex.Build(scene, _library);
        lock (_gate)
        {
            if (ReferenceEquals(scene, _scene))
            {
                _index = index;
            }
        }

        return index;
    }

    /// <summary>Shows what a search found, keeping the chosen row when it is still there.</summary>
    /// <param name="outcome">What was found.</param>
    /// <param name="guideOnly">Whether only the Guide was searched.</param>
    private void Show(SearchOutcome outcome, bool guideOnly)
    {
        var before = Selected?.Entry;
        var best = outcome.Best is { } hit ? new SearchRowViewModel(hit, isBest: true) : null;
        var groups = outcome.Groups
            .Where(group => group.Hits.Count > 0)
            .Select(group => new SearchGroupViewModel(
                group.Kind.GroupName(),
                group.Kind,
                [.. group.Hits.Select(groupHit => new SearchRowViewModel(groupHit, isBest: false))],
                group.HasMore ? $"{group.Hits.Count} av {group.Total} · Tab visar alla" : string.Empty))
            .ToList();
        _rows = [.. new[] { best }.OfType<SearchRowViewModel>(), .. groups.SelectMany(group => group.Rows)];
        Best = best;
        Groups = groups;
        OnPropertyChanged(nameof(Rows));
        ShowStartPoint(outcome, guideOnly);
        EmptyText = EmptyWords(outcome, guideOnly);
        var kept = before is null ? null : _rows.FirstOrDefault(row => IsSame(row.Entry, before));
        Select(kept ?? _rows.FirstOrDefault(), announce: kept is null);
    }

    /// <summary>
    /// Says whether what is written is an address or a tx the case does not have, and on which chain it is
    /// recognised.
    /// </summary>
    /// <param name="outcome">What was found.</param>
    /// <param name="guideOnly">Whether only the Guide was searched.</param>
    private void ShowStartPoint(SearchOutcome outcome, bool guideOnly)
    {
        var startPoint = StartPointText.Parse(outcome.Term);
        var recognised = !guideOnly && startPoint is { IsRecognised: true, Chain: not null };
        ChainText = recognised ? ChainWords(startPoint.Chain!.Value) : string.Empty;
        var held = _rows.Any(row => row.Entry.Kind is SearchKind.Address or SearchKind.Transaction
            && string.Equals(row.Entry.Text, startPoint.Value, StringComparison.OrdinalIgnoreCase));
        IsUnknown = !guideOnly && startPoint.IsRecognised && startPoint.IsSupported && !held && HasCase;
        UnknownText = IsUnknown ? StartPointWords(startPoint) : string.Empty;
    }

    /// <summary>Gets whether a case is open, whose graph the search looks in.</summary>
    private bool HasCase
    {
        get
        {
            lock (_gate)
            {
                return _scene is not null;
            }
        }
    }

    /// <summary>Says what is found when the case has nothing by what is written, or an empty text.</summary>
    /// <param name="outcome">What was found.</param>
    /// <param name="guideOnly">Whether only the Guide was searched.</param>
    /// <returns>The words.</returns>
    private string EmptyWords(SearchOutcome outcome, bool guideOnly)
    {
        if (outcome.Term.Length == 0 || IsUnknown)
        {
            return string.Empty;
        }

        var startPoint = StartPointText.Parse(outcome.Term);
        if (!guideOnly && startPoint is { IsRecognised: true, IsSupported: false })
        {
            return CaseWorkflow.NotBuiltText(startPoint);
        }

        if (guideOnly)
        {
            return outcome.Best is null ? $"Guiden har inget om {outcome.Term}." : string.Empty;
        }

        if (!HasCase)
        {
            return NoCaseText;
        }

        if (outcome.HasCaseHits)
        {
            return string.Empty;
        }

        return outcome.Best is null
            ? $"Inget i ärendet eller Guiden heter {outcome.Term}."
            : $"Inget i ärendet heter {outcome.Term}. Guiden svarar på begrepp.";
    }

    /// <summary>
    /// Says what adding a start point does: nothing is fetched until it is expanded, and then only the value, through
    /// the exit; or, offline, when it can be fetched.
    /// </summary>
    /// <param name="startPoint">The recognised start point.</param>
    /// <returns>The words.</returns>
    private string StartPointWords(ChainStartPoint startPoint)
    {
        var what = startPoint.Kind == StartPointKind.Transaction ? "tx:en" : "adressen";
        return _isLive
            ? $"Inget hämtas förrän du expanderar den, och då bara {what}, genom {_exitName}."
            : $"Läggs till nu. Inget kan hämtas förrän {_until}.";
    }

    /// <summary>Names a chain with its mark, as the chip in the field shows it.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>For example <c>₿ Bitcoin</c>.</returns>
    private static string ChainWords(Chain chain) => chain switch
    {
        Chain.Bitcoin => "₿ " + CaseTexts.ChainName(chain),
        Chain.Ethereum => "Ξ " + CaseTexts.ChainName(chain),
        Chain.Litecoin => "Ł " + CaseTexts.ChainName(chain),
        Chain.Zcash => "Z " + CaseTexts.ChainName(chain),

        _ => CaseTexts.ChainName(chain),
    };

    /// <summary>Chooses a row and shows it, in the graph or the Guide.</summary>
    /// <param name="row">The row, or <see langword="null"/> for none (or the start point's row).</param>
    /// <param name="announce">Whether to say so, so that the graph or the Guide follows.</param>
    private void Select(SearchRowViewModel? row, bool announce = true)
    {
        foreach (var each in _rows)
        {
            each.IsSelected = ReferenceEquals(each, row);
        }

        Selected = row;
        if (announce)
        {
            ShowWanted?.Invoke(this, row);
        }
    }

    /// <summary>Gives whether two entries are the same thing, also when the index was built anew.</summary>
    /// <param name="a">One entry.</param>
    /// <param name="b">The other.</param>
    /// <returns><see langword="true"/> when they are of the same kind and name.</returns>
    private static bool IsSame(SearchEntry a, SearchEntry b) =>
        a.Kind == b.Kind && string.Equals(a.Text, b.Text, StringComparison.Ordinal);

    /// <summary>Gives a row's place in the arrows' order.</summary>
    /// <param name="row">The row.</param>
    /// <returns>Its index, or the end when it is not there.</returns>
    private int IndexOf(SearchRowViewModel row)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            if (ReferenceEquals(_rows[i], row))
            {
                return i;
            }
        }

        return _rows.Count;
    }
}
