using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>A chip in the head of the view Tidslinje that hides a kind of event (sketch TL3a).</summary>
/// <param name="Filter">What it hides.</param>
/// <param name="IsOn">Whether its kind is shown.</param>
public sealed record TimelineChip(TimelineFilter Filter, bool IsOn)
{
    /// <summary>Gets its name.</summary>
    public string Name => TimelineReading.NameOf(Filter);

    /// <summary>Gets its colour's resource key.</summary>
    public string Brush => TimelineReading.BrushOf(Filter);

    /// <summary>Gets its tooltip.</summary>
    public string ToolTip => (IsOn ? "Dölj " : "Visa ") + Name.ToLower(CultureInfo.GetCultureInfo("sv-SE"));
}

/// <summary>A fact in the inspector of the view Tidslinje: a label and a value, the hashes in the mono font.</summary>
/// <param name="Label">What it is, for example <c>Blocktid</c>.</param>
/// <param name="Value">What is written.</param>
/// <param name="IsMono">Whether it is a hash or an id, written in the mono font.</param>
/// <param name="Copy">What a click copies, or <see langword="null"/> when it cannot be copied.</param>
public sealed record TimelineFact(string Label, string Value, bool IsMono = false, string? Copy = null)
{
    /// <summary>Gets whether a click copies it.</summary>
    public bool CanCopy => Copy is not null;
}

/// <summary>A row of <em>På banan</em>: an event on the chosen actor's lane.</summary>
/// <param name="Id">The event's id.</param>
/// <param name="When">When, for example <c>15 okt 20:04</c>.</param>
/// <param name="Name">What it is, for example <c>till Börs A</c>.</param>
/// <param name="Amount">The amount with its sign, or an empty text.</param>
/// <param name="Brush">The dot's colour.</param>
/// <param name="IsSelected">Whether it is the chosen event.</param>
public sealed record TimelineRow(string Id, string When, string Name, string Amount, string Brush, bool IsSelected);

/// <summary>The chosen event in the inspector of the view Tidslinje (sketch TL3a).</summary>
/// <param name="IsTransfer">Whether it is a transfer on the chain.</param>
/// <param name="Title">The title, for example <c>Kluster K2 → Börs A</c>.</param>
/// <param name="Line">The line under it: what it is.</param>
/// <param name="Icon">The icon's resource key.</param>
/// <param name="Brush">The frame's colour.</param>
/// <param name="Amount">The amount, large, or an empty text.</param>
/// <param name="Unit">The amount's unit.</param>
/// <param name="Value">The value at the time, or an empty text.</param>
/// <param name="Facts">The facts.</param>
/// <param name="Judgement">What is a judgement, in the dashed box, or an empty text when nothing is.</param>
public sealed record TimelineDetail(
    bool IsTransfer,
    string Title,
    string Line,
    string Icon,
    string Brush,
    string Amount,
    string Unit,
    string Value,
    IReadOnlyList<TimelineFact> Facts,
    string Judgement)
{
    /// <summary>Gets whether an amount stands large.</summary>
    public bool HasAmount => Amount.Length > 0;

    /// <summary>Gets whether a value stands beside the amount.</summary>
    public bool HasValue => Value.Length > 0;

    /// <summary>Gets whether something is a judgement.</summary>
    public bool IsJudged => Judgement.Length > 0;
}

/// <summary>
/// The view Tidslinje (sketch TL3a): a lane per actor, a transfer as a stroke between two lanes in the colour of what
/// the money became, transfers close together bundled with a number that zooms to hours, the case's own events on a
/// lane of their own, and to the right the chosen event with its facts from the chain apart from what is a judgement
/// (principle 2). It reads the graph shown and the case's own events off the UI thread and writes nothing in the case.
/// No WPF, so that it is tested in Chainsaw.App.Tests.
/// </summary>
public sealed partial class TimelineViewModel : ObservableObject
{
    /// <summary>What the view says when the graph has no transfer with a time.</summary>
    public const string EmptyText =
        "Inget att visa än. Tidslinjen visar grafens tx i tidsordning, en bana per aktör. Expandera grafen först.";

    /// <summary>What the view says when the graph could not be read.</summary>
    public const string FailedText = "Tidslinjen kunde inte läsas för den här grafen.";

    /// <summary>The width the lanes are laid out for until the view says its own.</summary>
    private const double DefaultWidth = 800;

    /// <summary>The UI thread, where a reading made off it is shown.</summary>
    private readonly IUiThread _ui;

    /// <summary>Now, for the line <em>nu</em>.</summary>
    private readonly IClock _clock;

    /// <summary>The chips that are off.</summary>
    private readonly HashSet<TimelineFilter> _hidden = [];

    /// <summary>The graph shown, or <see langword="null"/>.</summary>
    private GraphScene? _scene;

    /// <summary>Whether the graph changed while the view was hidden, so that it is read when shown.</summary>
    private bool _stale = true;

    /// <summary>Stops the reading running, when another is asked for.</summary>
    private CancellationTokenSource? _running;

    /// <summary>The lanes and events read.</summary>
    private TimelineData _data = TimelineData.Empty;

    /// <summary>How wide the time is laid out.</summary>
    private double _width = DefaultWidth;

    /// <summary>The moment in the middle of the hours, or <see langword="null"/>.</summary>
    private DateTimeOffset? _hoursAround;

    /// <summary>The graph's actors by key, read when a reading is shown.</summary>
    private IReadOnlyDictionary<string, NodeView> _actors = new Dictionary<string, NodeView>();

    /// <summary>Creates the view.</summary>
    /// <param name="ui">The UI thread.</param>
    /// <param name="clock">Now.</param>
    public TimelineViewModel(IUiThread ui, IClock clock)
    {
        _ui = ui;
        _clock = clock;
        Chips = [.. Enum.GetValues<TimelineFilter>().Select(filter => new TimelineChip(filter, true))];
    }

    /// <summary>Raised when <em>Visa i grafen</em> asks for the graph with the event's actors and line lit.</summary>
    public event EventHandler<ExposurePath>? PathWanted;

    /// <summary>Raised when <em>Varför?</em> asks for the Guide's panel.</summary>
    public event EventHandler<GuideRequest>? GuideWanted;

    /// <summary>Raised when a fact is copied, with the text, so that the view puts it on the clipboard.</summary>
    public event EventHandler<string>? CopyWanted;

    /// <summary>Gets or sets the time zone the days are counted in; Windows' by default.</summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Local;

    /// <summary>
    /// Gets or sets where the case's own events beyond the labels come from: the watches' hits and the decisions,
    /// tied to the graph's actors. Read off the UI thread together with the graph.
    /// </summary>
    public Func<TraceGraph, IReadOnlyList<TimelineMoment>> Moments { get; set; } = _ => [];

    /// <summary>Gets the reading running off the UI thread, or the last; complete when none runs.</summary>
    public Task Computed { get; private set; } = Task.CompletedTask;

    /// <summary>Gets or sets whether the view is shown; it reads only while it is (principle 6).</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; }

    /// <summary>Gets why nothing could be read, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string Problem { get; private set; } = string.Empty;

    /// <summary>Gets whether something could not be read.</summary>
    public bool HasProblem => Problem.Length > 0;

    /// <summary>Gets whether there is anything to show.</summary>
    [ObservableProperty]
    public partial bool HasEvents { get; private set; }

    /// <summary>Gets how far it is zoomed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDays), nameof(IsHours))]
    public partial TimelineZoom Zoom { get; private set; } = TimelineZoom.Days;

    /// <summary>Gets whether the whole case is shown in days.</summary>
    public bool IsDays => Zoom == TimelineZoom.Days;

    /// <summary>Gets whether a window of hours is shown.</summary>
    public bool IsHours => Zoom == TimelineZoom.Hours;

    /// <summary>Gets the chips.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TimelineChip> Chips { get; private set; }

    /// <summary>Gets the lanes laid out.</summary>
    [ObservableProperty]
    public partial TimelinePicture Picture { get; private set; } = TimelinePicture.None;

    /// <summary>Gets the overview strip.</summary>
    [ObservableProperty]
    public partial TimelineStrip Strip { get; private set; } = TimelineStrip.None;

    /// <summary>Gets the chosen event's id, or <see langword="null"/>.</summary>
    [ObservableProperty]
    public partial string? SelectedId { get; private set; }

    /// <summary>Gets the chosen event in the inspector, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    [NotifyCanExecuteChangedFor(nameof(ShowInGraphCommand), nameof(AskWhyCommand))]
    public partial TimelineDetail? Detail { get; private set; }

    /// <summary>Gets whether an event is chosen.</summary>
    public bool HasDetail => Detail is not null;

    /// <summary>Gets the heading of the chosen actor's lane, for example <c>PÅ BANAN · BÖRS A</c>.</summary>
    [ObservableProperty]
    public partial string SequenceTitle { get; private set; } = string.Empty;

    /// <summary>Gets the events on the chosen actor's lane.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TimelineRow> Sequence { get; private set; } = [];

    /// <summary>Gets the stretch shown, for example <c>13 okt – 8 nov · 7 dygn med rörelse</c>.</summary>
    [ObservableProperty]
    public partial string RangeText { get; private set; } = string.Empty;

    /// <summary>Gets how many events are shown, for example <c>12 av 14 händelser</c>.</summary>
    [ObservableProperty]
    public partial string CountText { get; private set; } = string.Empty;

    /// <summary>Gets the number of earlier events outside the hours, for example <c>‹ 3</c>, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBefore))]
    public partial string BeforeText { get; private set; } = string.Empty;

    /// <summary>Gets the number of later events outside the hours, for example <c>4 ›</c>, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAfter))]
    public partial string AfterText { get; private set; } = string.Empty;

    /// <summary>Gets whether earlier events lie outside the hours.</summary>
    public bool HasBefore => BeforeText.Length > 0;

    /// <summary>Gets whether later events lie outside the hours.</summary>
    public bool HasAfter => AfterText.Length > 0;

    /// <summary>Gets the method the colours rest on, for the dashed box in the head.</summary>
    [ObservableProperty]
    public partial string MethodText { get; private set; } = string.Empty;

    /// <summary>Gets the chosen event.</summary>
    private TimelineEvent? Selected => _data.Events.FirstOrDefault(each => each.Id == SelectedId);

    /// <summary>
    /// Follows the graph shown. While the view is hidden the graph is only kept and the view marked stale.
    /// </summary>
    /// <param name="scene">The graph, or <see langword="null"/>.</param>
    public void Update(GraphScene? scene)
    {
        _scene = scene;
        _stale = true;
        if (IsShown)
        {
            Refresh();
        }
    }

    /// <summary>Reads again when the case's own events changed (a watch's hit, a decision).</summary>
    public void MomentsChanged() => Update(_scene);

    /// <summary>Lays the lanes out for the width the view gives the time.</summary>
    /// <param name="width">The width.</param>
    public void Resize(double width)
    {
        if (width > 0 && Math.Abs(width - _width) >= 1)
        {
            _width = width;
            Lay();
        }
    }

    /// <summary>Chooses an event on the lanes or in <em>På banan</em>.</summary>
    /// <param name="id">The event's id.</param>
    [RelayCommand]
    public void Pick(string? id)
    {
        if (id is not null && _data.Events.Any(each => each.Id == id))
        {
            SelectedId = id;
            Lay();
        }
    }

    /// <summary>A click on a bundle: the hours around it, its first transfer chosen.</summary>
    /// <param name="bundle">The bundle.</param>
    public void Open(TimelineBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        SelectedId = bundle.Items[0].Id;
        var times = bundle.Items.Select(each => each.Time).OfType<DateTimeOffset>().ToList();
        _hoursAround = times.Count > 0 ? times.Min() + ((times.Max() - times.Min()) / 2) : null;
        Zoom = TimelineZoom.Hours;
        Lay();
    }

    /// <summary>Chooses the next event (→) or the previous (←) in time.</summary>
    /// <param name="forward">Whether to step forward.</param>
    public void Step(bool forward)
    {
        var shown = Picture.Events.Where(each => !each.IsHidden).Select(each => each.Event).ToList();
        if (shown.Count == 0)
        {
            return;
        }

        var index = shown.FindIndex(each => each.Id == SelectedId);
        var next = index < 0 ? 0 : Math.Clamp(index + (forward ? 1 : -1), 0, shown.Count - 1);
        Pick(shown[next].Id);
    }

    /// <summary>Shows the whole case in days (Ctrl+−).</summary>
    [RelayCommand]
    public void ShowDays()
    {
        Zoom = TimelineZoom.Days;
        Lay();
    }

    /// <summary>Shows the hours around the chosen event (Ctrl++).</summary>
    [RelayCommand]
    public void ShowHours()
    {
        _hoursAround = Selected?.Time ?? _data.Events.LastOrDefault(each => each.Time is not null)?.Time;
        if (_hoursAround is null)
        {
            return;
        }

        Zoom = TimelineZoom.Hours;
        Lay();
    }

    /// <summary>Turns a chip off or on.</summary>
    /// <param name="filter">The chip.</param>
    [RelayCommand]
    public void Toggle(TimelineFilter filter)
    {
        if (!_hidden.Remove(filter))
        {
            _hidden.Add(filter);
        }

        Chips = [.. Chips.Select(chip => chip with { IsOn = !_hidden.Contains(chip.Filter) })];
        Lay();
    }

    /// <summary>Copies a fact.</summary>
    /// <param name="fact">The fact.</param>
    [RelayCommand]
    public void Copy(TimelineFact? fact)
    {
        if (fact?.Copy is { } text)
        {
            CopyWanted?.Invoke(this, text);
        }
    }

    /// <summary><em>Visa i grafen</em> (G): the graph with the event's actors and line lit.</summary>
    [RelayCommand(CanExecute = nameof(HasDetail))]
    public void ShowInGraph()
    {
        if (Selected is not { } chosen || Detail is not { } detail)
        {
            return;
        }

        string[] nodes = chosen.IsTransfer ? [chosen.From!, chosen.To!] : [chosen.On!];
        string[] edges = chosen.EdgeKey is { } edge ? [edge] : [];
        var when = chosen.Time is { } time ? " · " + TimelineScale.Short(time, TimeZone) : string.Empty;
        PathWanted?.Invoke(this, new ExposurePath(nodes, edges, detail.Title + when));
    }

    /// <summary><em>Varför?</em>: the Guide's panel with what the colour and the event rest on.</summary>
    [RelayCommand(CanExecute = nameof(HasDetail))]
    public void AskWhy()
    {
        if (Selected is not { } chosen || Detail is not { } detail)
        {
            return;
        }

        var lines = new List<WhyLine>
        {
            new(WhyText(chosen), GuideTopic.TimelineView),
        };
        if (chosen.IsTransfer && chosen.Kind != TimelineKind.Unknown)
        {
            lines.Add(new($"Spårningsmetoden är {GraphScene.MethodName(_data.Method)}.", GuideTopic.Methods));
        }

        var context = new GuideContext("Varför? · " + detail.Title, lines);
        GuideWanted?.Invoke(this, new GuideRequest(GuideTopic.TimelineView, null, context, chosen.Id)
        {
            IsChoice = true,
        });
    }

    /// <summary>Reads a stale view when it is shown.</summary>
    /// <param name="value">Whether it is shown.</param>
    partial void OnIsShownChanged(bool value)
    {
        if (value && _stale)
        {
            Refresh();
        }
    }

    /// <summary>
    /// Reads the graph and the case's own events off the UI thread (principle 6), the last asked winning.
    /// </summary>
    private void Refresh()
    {
        _stale = false;
        _running?.Cancel();
        if (_scene is not { } scene)
        {
            Apply(TimelineData.Empty, string.Empty);
            return;
        }

        var running = new CancellationTokenSource();
        _running = running;
        var moments = Moments;
        Computed = ReadAsync(scene, moments, running.Token);
    }

    /// <summary>Reads the timeline off the UI thread and shows it, unless another reading was asked for.</summary>
    /// <param name="scene">The graph.</param>
    /// <param name="moments">The case's own events beyond the labels.</param>
    /// <param name="cancellation">Stops it when another is asked for.</param>
    /// <returns>The reading.</returns>
    private async Task ReadAsync(
        GraphScene scene, Func<TraceGraph, IReadOnlyList<TimelineMoment>> moments, CancellationToken cancellation)
    {
        try
        {
            var data = await Task.Run(
                () => TimelineReading.Read(
                    scene, [.. TimelineReading.Labels(scene), .. moments(scene.Snapshot.Graph)]),
                cancellation).ConfigureAwait(false);
            if (!cancellation.IsCancellationRequested)
            {
                _ui.Post(() =>
                {
                    if (!cancellation.IsCancellationRequested)
                    {
                        Apply(data, string.Empty);
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Another reading was asked for; it shows its own.
        }
        catch (InvalidOperationException)
        {
            if (!cancellation.IsCancellationRequested)
            {
                _ui.Post(() => Apply(TimelineData.Empty, FailedText));
            }
        }
    }

    /// <summary>Shows a reading: the chosen event kept where it can be, otherwise the first cash-out.</summary>
    /// <param name="data">The reading.</param>
    /// <param name="problem">Why nothing could be read, or an empty text.</param>
    private void Apply(TimelineData data, string problem)
    {
        _data = data;
        _actors = _scene is { } scene ? ExposureMap.Actors(scene) : new Dictionary<string, NodeView>();
        Problem = problem;
        HasEvents = data.HasEvents;
        MethodText = data.HasEvents ? GraphScene.MethodName(data.Method) : string.Empty;
        if (!data.Events.Any(each => each.Id == SelectedId))
        {
            SelectedId = (data.Events.FirstOrDefault(each => each.Kind == TimelineKind.CashOut)
                ?? data.Events.FirstOrDefault(each => each.IsTransfer)
                ?? data.Events.FirstOrDefault())?.Id;
        }

        Lay();
    }

    /// <summary>Lays the lanes, the strip and the inspector out for the zoom, the chips and the choice.</summary>
    private void Lay()
    {
        var scale = Zoom == TimelineZoom.Hours && _hoursAround is { } around
            ? TimelineScale.Hours(around, TimeZone, _width)
            : TimelineScale.Days(Times(), TimeZone, _width);
        if (Zoom == TimelineZoom.Hours && _hoursAround is null)
        {
            Zoom = TimelineZoom.Days;
        }

        Picture = TimelineLayout.Lay(_data, scale, _hidden, SelectedId, _clock.UtcNow, ValueOf);
        Strip = TimelineLayout.Strip(_data, scale, _hidden, _width);
        var shown = Picture.Events.Count(each => !each.IsHidden);
        CountText = $"{shown.ToString(CultureInfo.InvariantCulture)} av "
            + $"{_data.Events.Count.ToString(CultureInfo.InvariantCulture)} händelser";
        RangeText = Range(scale);
        var outside = Zoom == TimelineZoom.Hours;
        var before = outside ? _data.Events.Count(each => each.Time < scale.From) : 0;
        var after = outside ? _data.Events.Count(each => each.Time > scale.To) : 0;
        BeforeText = before > 0 ? "‹ " + before.ToString(CultureInfo.InvariantCulture) : string.Empty;
        AfterText = after > 0 ? after.ToString(CultureInfo.InvariantCulture) + " ›" : string.Empty;
        Inspect();
    }

    /// <summary>Gives the times the days are laid out around: every event's.</summary>
    /// <returns>The times.</returns>
    private IEnumerable<DateTimeOffset> Times() => _data.Events.Select(each => each.Time).OfType<DateTimeOffset>();

    /// <summary>Writes the stretch shown.</summary>
    /// <param name="scale">The time along the x axis.</param>
    /// <returns>The text.</returns>
    private string Range(TimelineScale scale)
    {
        if (scale.Pieces.Count == 0)
        {
            return string.Empty;
        }

        if (scale.Zoom == TimelineZoom.Hours)
        {
            return TimelineScale.DateHour(scale.From, TimeZone) + " – " + TimelineScale.DateHour(scale.To, TimeZone);
        }

        var days = Times().Select(time => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, TimeZone).DateTime))
            .Distinct().Count();
        var last = scale.To.AddSeconds(-1);
        return $"{TimelineScale.Date(scale.From, TimeZone)} – {TimelineScale.Date(last, TimeZone)} · "
            + $"{days.ToString(CultureInfo.InvariantCulture)} dygn med rörelse";
    }

    /// <summary>Fills the inspector and <em>På banan</em> for the chosen event.</summary>
    private void Inspect()
    {
        if (Selected is not { } chosen || _scene is not { } scene)
        {
            Detail = null;
            Sequence = [];
            SequenceTitle = string.Empty;
            return;
        }

        Detail = chosen.IsTransfer ? TransferDetail(chosen, scene) : MomentDetail(chosen, scene);
        var focus = chosen.Focus;
        var lane = _data.Lanes.FirstOrDefault(each => each.Key == focus);
        var swedish = CultureInfo.GetCultureInfo("sv-SE");
        SequenceTitle = lane is null ? string.Empty : "PÅ BANAN · " + lane.Name.ToUpper(swedish);
        Sequence =
        [
            .. _data.Events
                .Where(each => each.From == focus || each.To == focus || each.On == focus)
                .Select(each => RowOf(each, focus, scene)),
        ];
    }

    /// <summary>Makes the inspector of a transfer: the facts from the chain and, dashed, what is a judgement.</summary>
    /// <param name="chosen">The transfer.</param>
    /// <param name="scene">The graph.</param>
    /// <returns>The detail.</returns>
    private TimelineDetail TransferDetail(TimelineEvent chosen, GraphScene scene)
    {
        var facts = new List<TimelineFact>();
        if (chosen.Time is { } time)
        {
            facts.Add(new("Blocktid", TimelineScale.Long(time, TimeZone)));
            facts.Add(new("UTC", TimelineScale.Utc(time)));
        }
        else
        {
            facts.Add(new("Blocktid", "Obekräftad: inte i ett block än"));
        }

        AddChainFacts(facts, chosen);
        var title = $"{NameOf(chosen.From)} → {NameOf(chosen.To)}";
        var amount = chosen.Amount;
        return new TimelineDetail(
            true, title, TimelineReading.NameOf(chosen.Kind), "Icon.ArrowRight", TimelineReading.BrushOf(chosen.Kind),
            amount is { } a ? AmountText.Number(a) : string.Empty, amount?.Asset.Symbol ?? string.Empty,
            ValueOf(chosen), facts, JudgementOf(chosen));
    }

    /// <summary>Makes the inspector of a case event: when, who and why, and the journal entry that seals it.</summary>
    /// <param name="chosen">The case event.</param>
    /// <param name="scene">The graph.</param>
    /// <returns>The detail.</returns>
    private TimelineDetail MomentDetail(TimelineEvent chosen, GraphScene scene)
    {
        var facts = new List<TimelineFact>();
        if (chosen.Time is { } time)
        {
            facts.Add(new("Tid", TimelineScale.Long(time, TimeZone)));
            facts.Add(new("UTC", TimelineScale.Utc(time)));
        }

        AddChainFacts(facts, chosen);
        if (chosen.Who is { Length: > 0 } who)
        {
            facts.Add(new("Av", who));
        }

        if (chosen.Why is { Length: > 0 } why)
        {
            facts.Add(new("Skäl", why));
        }

        if (chosen.Journal is { } journal)
        {
            facts.Add(new("Förseglad", "journalpost " + journal.ToString(CultureInfo.InvariantCulture)));
        }

        var node = _actors.GetValueOrDefault(chosen.On ?? string.Empty);
        var line = node is null ? TimelineReading.NameOf(chosen.Kind) : $"{node.Name} · {TimelineReading.LineOf(node)}";
        var icon = chosen.Kind switch
        {
            TimelineKind.Label => "Icon.Tag",
            TimelineKind.Decision => "Icon.Check",
            _ => "Icon.Eye",
        };
        var amount = chosen.Amount;
        return new TimelineDetail(
            false, chosen.Title, line, icon, TimelineReading.BrushOf(chosen.Kind),
            amount is { } a ? AmountText.Number(a) : string.Empty, amount?.Asset.Symbol ?? string.Empty,
            string.Empty, facts, JudgementOf(chosen));
    }

    /// <summary>Adds the block, its hash and the transaction, which every step can be checked against.</summary>
    /// <param name="facts">The facts.</param>
    /// <param name="chosen">The event.</param>
    private static void AddChainFacts(List<TimelineFact> facts, TimelineEvent chosen)
    {
        if (chosen.Block is { } block)
        {
            facts.Add(new("Block", block.Height.ToString("N0", CultureInfo.GetCultureInfo("sv-SE"))));
            facts.Add(new("Blockhash", GraphScene.Shorten(block.Hash), true, block.Hash));
        }

        if (chosen.Transaction is { } transaction)
        {
            facts.Add(new("txid", GraphScene.Shorten(transaction.Id), true, transaction.Id));
        }
    }

    /// <summary>Writes what about an event is a judgement, or an empty text when nothing is.</summary>
    /// <param name="chosen">The event.</param>
    /// <returns>The text in the dashed box.</returns>
    private string JudgementOf(TimelineEvent chosen)
    {
        if (!TimelineReading.IsJudged(chosen.Kind))
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (chosen.Kind == TimelineKind.Guess)
        {
            parts.Add("Bygger på något osäkert");
        }

        if (chosen.Kind == TimelineKind.Watch)
        {
            parts.Add("Bevakningen: " + chosen.Why);
        }

        if (chosen.Kind == TimelineKind.Decision)
        {
            parts.Add("Ett förslag är en bedömning");
        }

        if (chosen.IsTransfer)
        {
            parts.Add(GraphScene.MethodName(_data.Method));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Writes why an event has its colour, for <em>Varför?</em>.</summary>
    /// <param name="chosen">The event.</param>
    /// <returns>The text.</returns>
    private string WhyText(TimelineEvent chosen)
    {
        var name = TimelineReading.NameOf(chosen.Kind).ToLower(CultureInfo.GetCultureInfo("sv-SE"));
        return chosen.Kind switch
        {
            TimelineKind.Unknown =>
                "Spåret följer inte de här pengarna med " + GraphScene.MethodName(_data.Method) + ", så strecket är grått. "
                + "Tx:en, beloppet och blocket är fakta ur kedjan.",
            TimelineKind.Label or TimelineKind.Decision or TimelineKind.Watch =>
                $"Ärendets egen händelse: {chosen.Title}. Den är förseglad i journalen, men det den säger är en bedömning.",
            _ =>
                $"Strecket har färgen för {name}: mottagarens sort enligt etiketterna, och att spåret följer pengarna med "
                + $"{GraphScene.MethodName(_data.Method)}. Det är en bedömning; tx:en, beloppet och blocket är fakta.",
        };
    }

    /// <summary>Makes a row of <em>På banan</em>.</summary>
    /// <param name="each">The event.</param>
    /// <param name="focus">The lane's actor.</param>
    /// <param name="scene">The graph.</param>
    /// <returns>The row.</returns>
    private TimelineRow RowOf(TimelineEvent each, string focus, GraphScene scene)
    {
        var when = each.Time is { } time ? TimelineScale.Short(time, TimeZone) : "Obekräftad";
        var outwards = each.From == focus || each.Kind == TimelineKind.Watch;
        var name = each.IsTransfer
            ? (outwards ? "till " : "från ") + NameOf(outwards ? each.To : each.From)
            : each.Title;
        var sign = outwards ? "−" : "+";
        var amount = each.Amount is { } a && each.Kind != TimelineKind.Label ? sign + AmountText.Number(a) : string.Empty;
        return new TimelineRow(each.Id, when, name, amount, TimelineReading.BrushOf(each.Kind), each.Id == SelectedId);
    }

    /// <summary>
    /// Gives a transfer's value at the time, when Pricing has it, as the graph writes it under the amount.
    /// </summary>
    /// <param name="each">The event.</param>
    /// <returns>For example <c>≈ 263 100 kr</c>, or an empty text.</returns>
    private string ValueOf(TimelineEvent each)
    {
        if (each.EdgeKey is not { } key || _scene?.Snapshot.Values.GetValueOrDefault(key) is not { } value)
        {
            return string.Empty;
        }

        var part = value.Parts.FirstOrDefault(p => p.Transfer.Transaction == each.Transaction
            && p.Transfer.Amount == each.Amount);
        if (part is null)
        {
            return value.Parts.Count == 0 ? value.Text : string.Empty;
        }

        return "≈ " + part.Valuation.Rounded.ToString("#,##0", CultureInfo.GetCultureInfo("sv-SE"))
            + MoneyText.Unit(value.Currency);
    }

    /// <summary>Gives an actor's name as the graph writes it.</summary>
    /// <param name="key">The actor's key.</param>
    /// <returns>The name.</returns>
    private string NameOf(string? key) =>
        key is null ? string.Empty : _actors.GetValueOrDefault(key)?.Name ?? key;
}
