using System.Collections.ObjectModel;
using System.Globalization;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>How a watch's state is drawn: the dot's colour in the list.</summary>
public enum WatchLight
{
    /// <summary>Green: it is checked.</summary>
    Active,

    /// <summary>Yellow: it waits for the exit, the sources or its first check.</summary>
    Waiting,

    /// <summary>Grey: it is paused.</summary>
    Paused,
}

/// <summary>A fact in the chosen watch's details.</summary>
/// <param name="Key">What it is.</param>
/// <param name="Value">Its value.</param>
public sealed record WatchFact(string Key, string Value);

/// <summary>A counterparty of a watch in its row, with the direction it is watched in.</summary>
/// <param name="Text">The party's name or address shortened.</param>
/// <param name="IsOut">Whether it is a receiver (money out); otherwise a sender.</param>
public sealed record WatchRowParty(string Text, bool IsOut);

/// <summary>A hit in the chosen watch's details.</summary>
/// <param name="Hit">The hit.</param>
/// <param name="Text">The hit in one line.</param>
/// <param name="When">When and in which block.</param>
/// <param name="IsNew">Whether the user has not seen it.</param>
public sealed record WatchHitRow(WatchHit Hit, string Text, string When, bool IsNew)
{
    /// <summary>Gets whether the money went out.</summary>
    public bool IsOut => Hit.Match.Direction == WatchDirection.Out;
}

/// <summary>A watch in the list of the view Bevakning (sketch BV1a).</summary>
public sealed partial class WatchRowViewModel : ObservableObject
{
    /// <summary>Creates the row.</summary>
    /// <param name="watch">The watch.</param>
    /// <param name="light">Its state as a dot.</param>
    /// <param name="stateText">Its state in words.</param>
    /// <param name="parties">Its counterparties.</param>
    /// <param name="partiesText">Its counterparties in words.</param>
    /// <param name="lastText">When it was last checked.</param>
    /// <param name="newHits">How many hits the user has not seen.</param>
    public WatchRowViewModel(
        Watch watch, WatchLight light, string stateText, IReadOnlyList<WatchRowParty> parties, string partiesText,
        string lastText, int newHits)
    {
        Watch = watch;
        Light = light;
        StateText = stateText;
        Parties = parties;
        PartiesText = partiesText;
        LastText = lastText;
        NewHits = newHits;
    }

    /// <summary>Gets the watch.</summary>
    public Watch Watch { get; }

    /// <summary>Gets the watch's id.</summary>
    public string Id => Watch.Id;

    /// <summary>Gets the node's name.</summary>
    public string Name => Watch.Subject.Name;

    /// <summary>Gets the line under the name: the cluster or the address shortened.</summary>
    public string Short => Watch.Subject.Kind == WatchSubjectKind.Cluster
        ? "Kluster · " + WatchWords.Short(Watch.Subject.Anchor)
        : WatchWords.Short(Watch.Subject.Anchor);

    /// <summary>Gets whether the node is a cluster (a square icon) rather than an address (a round one).</summary>
    public bool IsCluster => Watch.Subject.Kind == WatchSubjectKind.Cluster;

    /// <summary>Gets the chain's name.</summary>
    public string ChainName => Watch.Chain.ToString();

    /// <summary>Gets whether money in is watched (the arrow in).</summary>
    public bool WatchesIn => Watch.Filter.WatchesIn;

    /// <summary>Gets whether money out is watched (the arrow out).</summary>
    public bool WatchesOut => Watch.Filter.WatchesOut;

    /// <summary>Gets the direction in words, for the tooltip.</summary>
    public string DirectionText => WatchWords.Direction(Watch.Filter.Direction);

    /// <summary>Gets the counterparties as chips.</summary>
    public IReadOnlyList<WatchRowParty> Parties { get; }

    /// <summary>Gets whether every counterparty counts.</summary>
    public bool AllParties => Parties.Count == 0;

    /// <summary>Gets the counterparties in words, for the tooltip.</summary>
    public string PartiesText { get; }

    /// <summary>Gets the asset part.</summary>
    public string AssetText => "· " + WatchWords.Asset(Watch.Filter);

    /// <summary>Gets when it was last checked.</summary>
    public string LastText { get; }

    /// <summary>Gets its state as a dot.</summary>
    public WatchLight Light { get; }

    /// <summary>Gets its state in words.</summary>
    public string StateText { get; }

    /// <summary>Gets whether it is paused, so that the row is faint.</summary>
    public bool IsPaused => Watch.State == WatchState.Paused;

    /// <summary>Gets how many hits the user has not seen.</summary>
    public int NewHits { get; }

    /// <summary>Gets whether the row has the dot for new hits.</summary>
    public bool HasNew => NewHits > 0;

    /// <summary>Gets the dot's tooltip.</summary>
    public string NewTip => NewHits == 1 ? "1 ny sedan du tittade" : $"{NewHits} nya sedan du tittade";

    /// <summary>Gets or sets whether the row is the chosen one.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// The view Bevakning (sketch BV1a): a list with a row per watch, the chosen one with details and hits to the right,
/// <em>Ny bevakning …</em> with the same form as the graph's, Ändra, Pausa, Ta bort (a sealed taking back, as in AN1b)
/// and Öppna i grafen, and the honest state of the watching in the head. It knows nothing of WPF.
/// </summary>
public sealed partial class MonitoringViewModel : ObservableObject
{
    /// <summary>The watches.</summary>
    private readonly WatchBoard _board;

    /// <summary>The clock, for <em>i dag</em>.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the view.</summary>
    /// <param name="board">The watches.</param>
    /// <param name="form">The form, shared with the graph.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="background">The background watching, whose state the head says; none in some tests.</param>
    public MonitoringViewModel(
        WatchBoard board, WatchFormViewModel form, IClock clock, BackgroundLink? background = null)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(clock);
        _board = board;
        _clock = clock;
        _background = background;
        Form = form;
        board.Changed += (_, _) => Show();
        if (background is not null)
        {
            background.Changed += (_, _) => Show();
        }

        Show();
    }

    /// <summary>Raised when the user wants a watch's node shown in the graph.</summary>
    public event EventHandler<Watch>? OpenInGraphWanted;

    /// <summary>Gets the form.</summary>
    public WatchFormViewModel Form { get; }

    /// <summary>Gets the watches the view shows.</summary>
    public WatchBoard Board => _board;

    /// <summary>Gets the rows.</summary>
    public ObservableCollection<WatchRowViewModel> Rows { get; } = [];

    /// <summary>
    /// Gets names for addresses: the graph's node names, set by the window; the address shortened otherwise.
    /// </summary>
    public Func<ChainAddress, string?> Names { get; set; } = _ => null;

    /// <summary>Gets the chosen row, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(PauseText))]
    public partial WatchRowViewModel? Selected { get; private set; }

    /// <summary>Gets the chosen watch's facts.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<WatchFact> Facts { get; private set; } = [];

    /// <summary>Gets the chosen watch's hits, newest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HitsTitle), nameof(HasNoHits))]
    public partial IReadOnlyList<WatchHitRow> Hits { get; private set; } = [];

    /// <summary>Gets the line in the head: how many watches, active ones and new hits.</summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    /// <summary>Gets the honest state of the watching in the head.</summary>
    [ObservableProperty]
    public partial string BackgroundText { get; private set; } = WatchWords.RunningText;

    /// <summary>Gets the head's tooltip.</summary>
    [ObservableProperty]
    public partial string BackgroundTip { get; private set; } = WatchWords.RunningTip;

    /// <summary>Gets whether the watching waits (yellow, dashed) rather than runs.</summary>
    [ObservableProperty]
    public partial bool IsWaiting { get; private set; }

    /// <summary>Gets whether the background is off (grey, dashed): it watches only while Chainsaw is open.</summary>
    [ObservableProperty]
    public partial bool IsOff { get; private set; }

    /// <summary>The background watching, or <see langword="null"/>.</summary>
    private readonly BackgroundLink? _background;

    /// <summary>Gets whether the removal's confirmation is open.</summary>
    [ObservableProperty]
    public partial bool IsRemoving { get; private set; }

    /// <summary>Gets or sets the optional reason of a removal.</summary>
    [ObservableProperty]
    public partial string RemoveReason { get; set; } = string.Empty;

    /// <summary>Gets the problem, or an empty text.</summary>
    [ObservableProperty]
    public partial string Problem { get; private set; } = string.Empty;

    /// <summary>Gets whether a case is open.</summary>
    [ObservableProperty]
    public partial bool HasCase { get; private set; }

    /// <summary>Gets whether there are watches.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>Gets what the empty view says.</summary>
    public string EmptyText => HasCase
        ? "Inga bevakningar än. Högerklicka på en nod i grafen och välj Bevaka …, eller tryck B för en ny."
        : "Inget ärende öppet.";

    /// <summary>Gets whether a watch is chosen.</summary>
    public bool HasSelection => Selected is not null;

    /// <summary>Gets the pause button's text.</summary>
    public string PauseText => Selected?.IsPaused == true ? "Fortsätt" : "Pausa";

    /// <summary>Gets the title above the hits.</summary>
    public string HitsTitle => $"Träffar · {Hits.Count.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Gets whether the chosen watch has no hits.</summary>
    public bool HasNoHits => Hits.Count == 0;

    /// <summary>Chooses a row: its details are shown, and its hits count as seen.</summary>
    /// <param name="row">The row, or <see langword="null"/>.</param>
    [RelayCommand]
    public void Choose(WatchRowViewModel? row)
    {
        IsRemoving = false;
        Selected = row;
        foreach (var each in Rows)
        {
            each.IsSelected = ReferenceEquals(each, row);
        }

        ShowDetails();
        if (row?.HasNew == true)
        {
            _ = _board.MarkSeenAsync(row.Id);
        }
    }

    /// <summary>Opens the form for a new watch on a typed address (B).</summary>
    [RelayCommand]
    public void New()
    {
        if (_board.HasCase)
        {
            Form.OpenNew();
        }
    }

    /// <summary>Opens the form to change the chosen watch.</summary>
    [RelayCommand]
    public void Change()
    {
        if (Selected is { } row)
        {
            Form.OpenChange(row.Watch, Name);
        }
    }

    /// <summary>Pauses or resumes the chosen watch, sealed.</summary>
    /// <returns>A task that completes when it is saved.</returns>
    [RelayCommand]
    public async Task TogglePauseAsync()
    {
        if (Selected is not { } row || _board.Case is not { } openCase)
        {
            return;
        }

        var source = _board.Source;
        await _board.WriteAsync(() => source.SetPausedAsync(openCase, row.Id, !row.IsPaused));
    }

    /// <summary>Opens the confirmation of a removal, with an optional reason.</summary>
    [RelayCommand]
    public void AskRemove()
    {
        if (Selected is not null)
        {
            RemoveReason = string.Empty;
            IsRemoving = true;
        }
    }

    /// <summary>Closes the removal's confirmation.</summary>
    [RelayCommand]
    public void CancelRemove() => IsRemoving = false;

    /// <summary>Takes the chosen watch back as a new sealed record; it and its hits stay in the case.</summary>
    /// <returns>A task that completes when it is saved.</returns>
    [RelayCommand]
    public async Task RemoveAsync()
    {
        if (Selected is not { } row || _board.Case is not { } openCase || !IsRemoving)
        {
            return;
        }

        var reason = RemoveReason;
        var source = _board.Source;
        IsRemoving = false;
        await _board.WriteAsync(() => source.RemoveAsync(openCase, row.Id, reason));
    }

    /// <summary>Asks the graph to show the chosen watch's node.</summary>
    [RelayCommand]
    public void OpenInGraph()
    {
        if (Selected is { } row)
        {
            OpenInGraphWanted?.Invoke(this, row.Watch);
        }
    }

    /// <summary>Names an address: its node's name in the graph, or the address shortened.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The name.</returns>
    public string Name(ChainAddress address) => Names(address) ?? WatchWords.Short(address);

    /// <summary>Shows the board's snapshot, keeping the chosen watch when it is still there.</summary>
    private void Show()
    {
        var snapshot = _board.Snapshot;
        HasCase = _board.HasCase;
        Problem = _board.Problem;
        var chosen = Selected?.Id;
        Rows.Clear();
        foreach (var watch in snapshot.Watches)
        {
            Rows.Add(RowOf(snapshot, watch));
        }

        var selected = Rows.FirstOrDefault(row => row.Id == chosen) ?? Rows.FirstOrDefault();
        if (selected?.Id != chosen)
        {
            // A removal's confirmation belongs to the watch it was opened for, never to the next one.
            IsRemoving = false;
            RemoveReason = string.Empty;
        }

        Selected = selected;
        if (selected is not null)
        {
            selected.IsSelected = true;
        }

        ShowDetails();
        ShowHead(snapshot);
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(EmptyText));
    }

    /// <summary>Makes a watch's row.</summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="watch">The watch.</param>
    /// <returns>The row.</returns>
    private WatchRowViewModel RowOf(WatchSnapshot snapshot, Watch watch)
    {
        var gate = _board.Gate(watch.Chain);
        var failed = snapshot.Checked.TryGetValue(watch.Id, out var last)
            && last.Status is WatchFeedStatus.Failed or WatchFeedStatus.NotAsked;
        var running = gate == WatchGate.Open && watch.StartHeight is not null && !failed;
        var light = watch.State == WatchState.Paused ? WatchLight.Paused
            : running ? WatchLight.Active : WatchLight.Waiting;
        // A cluster picked as a counterparty brings all its addresses; the row names its node once.
        var parties = watch.Filter.Receivers.Select(party => new WatchRowParty(Name(party), true))
            .Concat(watch.Filter.Senders.Select(party => new WatchRowParty(Name(party), false)))
            .Distinct()
            .ToList();
        return new WatchRowViewModel(
            watch,
            light,
            failed && watch.State == WatchState.Active && gate == WatchGate.Open
                ? "Senaste uppslaget misslyckades"
                : WatchWords.State(watch, gate),
            parties,
            WatchWords.Parties(watch.Filter, Name),
            LastOf(snapshot, watch),
            snapshot.HitsOf(watch.Id).Count(snapshot.IsNew));
    }

    /// <summary>Writes when a watch was last checked.</summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="watch">The watch.</param>
    /// <returns>For example <c>14:02 · block 921 344</c>, or <c>Pausad 6 nov 17:20</c>.</returns>
    private string LastOf(WatchSnapshot snapshot, Watch watch)
    {
        if (watch.State == WatchState.Paused)
        {
            return "Pausad " + WatchWords.When(watch.ChangedUtc, _clock.UtcNow);
        }

        return snapshot.Checked.TryGetValue(watch.Id, out var at)
            ? $"{WatchWords.When(at.CheckedUtc, _clock.UtcNow)} · block {WatchWords.Block(at.Height)}"
            : WatchWords.NeverCheckedText;
    }

    /// <summary>Shows the chosen watch's facts and hits.</summary>
    private void ShowDetails()
    {
        if (Selected is not { } row)
        {
            Facts = [];
            Hits = [];
            return;
        }

        var snapshot = _board.Snapshot;
        var watch = row.Watch;
        var checkedText = snapshot.Checked.TryGetValue(watch.Id, out var at) && at.Problem is { } problem
            ? $"{row.LastText} · {problem}"
            : row.LastText;
        Facts =
        [
            new("Kedja", watch.Chain.ToString()),
            new("Riktning", WatchWords.Direction(watch.Filter.Direction)),
            new("Motparter", row.PartiesText),
            new("Tillgång", WatchWords.Asset(watch.Filter)),
            new("Minsta belopp", WatchWords.Minimum(watch.Filter)),
            new("Hur ofta", WatchWords.Pace(watch.Filter.Pace)),
            new("Senast kontrollerad", checkedText),
            new("Startad", $"{watch.CreatedBy.Name} {WatchWords.When(watch.CreatedUtc, _clock.UtcNow)}"),
            new("Räknas från", watch.StartHeight is { } start
                ? $"Efter block {WatchWords.Block(start)}"
                : "Nästa kontroll"),
            new("Källa", "Endpoint i fas"),
        ];
        Hits = [.. snapshot.HitsOf(watch.Id).Select(hit => new WatchHitRow(
            hit,
            WatchWords.Hit(hit, Name),
            WhenText(hit.Match.Block),
            snapshot.IsNew(hit)))];
        OnPropertyChanged(nameof(PauseText));
    }

    /// <summary>Writes a hit's block: when, its height and its hash shortened (principle 1).</summary>
    /// <param name="block">The block.</param>
    /// <returns>For example <c>14:01 · block 921 340 · 0000…3f2a</c>.</returns>
    private string WhenText(WatchBlock block) =>
        $"{WatchWords.When(block.Time, _clock.UtcNow)} · block {WatchWords.Block(block.Height)} · " +
        Graph.SubjectText.Fit(block.Hash, Graph.SubjectText.Shortest + 4);

    /// <summary>Shows the head: the summary and the honest state of the watching.</summary>
    /// <param name="snapshot">The snapshot.</param>
    private void ShowHead(WatchSnapshot snapshot)
    {
        var active = snapshot.Watches.Where(watch => watch.State == WatchState.Active).ToList();
        var unseen = snapshot.Unseen;
        var news = unseen switch
        {
            0 => string.Empty,
            1 => " · 1 ny träff",
            _ => $" · {unseen} nya träffar",
        };
        Summary = $"{snapshot.Watches.Count} bevakningar · {active.Count} aktiva{news}";
        // The gate counts whether or not a watch is active: an honest head never says it watches when it cannot.
        var chains = snapshot.Watches.Select(watch => watch.Chain).DefaultIfEmpty(Chain.Bitcoin).Distinct();
        var gates = chains.Select(chain => (Chain: chain, Gate: _board.Gate(chain))).ToList();
        var notAsked = active.Any(watch => snapshot.Checked.TryGetValue(watch.Id, out var last)
            && last.Status == WatchFeedStatus.NotAsked);
        var failing = active.Any(watch => _board.HeightFailing.Contains(watch.Chain)
            || (snapshot.Checked.TryGetValue(watch.Id, out var last) && last.Status == WatchFeedStatus.Failed));
        if (gates.Any(each => each.Gate == WatchGate.Locked))
        {
            (BackgroundText, BackgroundTip, IsWaiting) = (WatchWords.LockedText, WatchWords.LockedText, true);
        }
        else if (gates.Any(each => each.Gate == WatchGate.NoExit))
        {
            (BackgroundText, BackgroundTip, IsWaiting) = (WatchWords.NoExitText, WatchWords.NoExitTip, true);
        }
        else if (gates.FirstOrDefault(each => each.Gate == WatchGate.ChainOff) is { Gate: WatchGate.ChainOff } off)
        {
            var text = string.Format(CultureInfo.InvariantCulture, WatchWords.ChainOffText, off.Chain);
            (BackgroundText, BackgroundTip, IsWaiting) = (text, text, true);
        }
        else if (failing)
        {
            (BackgroundText, BackgroundTip, IsWaiting) = (WatchWords.FailingText, WatchWords.FailingTip, true);
        }
        else if (notAsked)
        {
            (BackgroundText, BackgroundTip, IsWaiting) = (WatchWords.NoSourceText, WatchWords.NoSourceTip, true);
        }
        else
        {
            ShowBackground();
            return;
        }

        IsOff = false;
    }

    /// <summary>
    /// Shows, when Chainsaw itself watches without trouble, what happens when it is closed (sketch BV1a): the
    /// background goes through Windows, waits for the exit (VPN), is off, or does not run.
    /// </summary>
    private void ShowBackground()
    {
        IsOff = false;
        switch (_background?.Mode)
        {
            case BackgroundMode.Running:
                var inStep = _background.InStepWith is { } name
                    ? string.Format(CultureInfo.InvariantCulture, WatchWords.InStepText, name)
                    : string.Empty;
                (BackgroundText, BackgroundTip, IsWaiting) =
                    (WatchWords.BackgroundRunningText + inStep, WatchWords.BackgroundRunningTip, false);
                break;
            case BackgroundMode.NeedsWindows:
                (BackgroundText, BackgroundTip, IsWaiting) =
                    (WatchWords.BackgroundWaitsText, WatchWords.BackgroundWaitsTip, true);
                break;
            case BackgroundMode.Failing:
                (BackgroundText, BackgroundTip, IsWaiting) =
                    (WatchWords.BackgroundFailingText, WatchWords.BackgroundFailingTip, true);
                break;
            case BackgroundMode.SourcesOff:
                (BackgroundText, BackgroundTip, IsWaiting) =
                    (WatchWords.BackgroundSourcesOffText, WatchWords.BackgroundSourcesOffTip, true);
                break;
            case BackgroundMode.NotRunning:
                (BackgroundText, BackgroundTip, IsWaiting) =
                    (WatchWords.BackgroundStoppedText, WatchWords.BackgroundStoppedTip, true);
                break;
            case BackgroundMode.Off:
                (BackgroundText, BackgroundTip, IsWaiting) =
                    (WatchWords.BackgroundOffText, WatchWords.BackgroundOffTip, false);
                IsOff = true;
                break;
            default:
                (BackgroundText, BackgroundTip, IsWaiting) = (WatchWords.RunningText, WatchWords.RunningTip, false);
                break;
        }
    }
}
