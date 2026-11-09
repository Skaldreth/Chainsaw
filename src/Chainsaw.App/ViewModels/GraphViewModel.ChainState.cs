using System.ComponentModel;
using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// What the graph shows of the chains now (sketches IN4a, GR9a and TL2a): the cards turned with Alt + click to show
/// what is left at an address, the marks of transactions that moved to another block, <em>Väntar på källan</em> when
/// every source of a chain asks Chainsaw to wait, and the lens and the newest block in the strip. Turning a card asks
/// for the balance and today's rate through the exit; everything else is read from the case.
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>The graph's way to balances, rates and block changes; none when it is not wired.</summary>
    private IGraphChainState? _chainState;

    /// <summary>The chains' endpoints, whose waits are followed; none when they are not wired.</summary>
    private IChainSources? _chainSources;

    /// <summary>The turned cards, by node, in the order they were turned.</summary>
    private readonly Dictionary<string, HoldingsCard> _cards = new(StringComparer.Ordinal);

    /// <summary>
    /// When each chain's wait was first seen and until when it was then, for the ring that drains; a pause that ends
    /// later than the one seen is a new wait, and its ring starts full again.
    /// </summary>
    private readonly Dictionary<Chain, (DateTimeOffset Since, DateTimeOffset Until)> _waitSince = [];

    /// <summary>Each turned card's turn, so that an answer to an earlier turn of the same node is dropped.</summary>
    private readonly Dictionary<string, int> _turnIds = new(StringComparer.Ordinal);

    /// <summary>The turns made so far, which number them.</summary>
    private int _turns;

    /// <summary>Counts the readings of the marks, so that an answer to an older one is dropped.</summary>
    private int _marksVersion;

    /// <summary>The block changes the case has, as last read.</summary>
    private IReadOnlyList<BlockChange> _changes = [];

    /// <summary>The marks of the lines whose transaction left its block, by line.</summary>
    private IReadOnlyDictionary<string, BlockMark> _blockMarks = new Dictionary<string, BlockMark>();

    /// <summary>The node an expansion runs for, which gets the hourglass while its source makes it wait.</summary>
    private string? _expandingNode;

    /// <summary>The selection view while it is open, so that its window becomes the strip's lens when it closes.</summary>
    private HeldBackViewModel? _openSelection;

    /// <summary>Gets the time zone the cards and the marks write their times in.</summary>
    public TimeZoneInfo Zone { get; init; } = TimeZoneInfo.Local;

    /// <summary>Gets the turned cards' backs, by node (sketch IN4a).</summary>
    public IReadOnlyDictionary<string, HoldingsCard> Cards => _cards;

    /// <summary>Gets the marks of the lines whose transaction left its block (sketch GR9a), by line.</summary>
    public IReadOnlyDictionary<string, BlockMark> BlockMarksByLine
    {
        get => _blockMarks;
        private set
        {
            _blockMarks = value;
            GoneTrail = GoneTrail.Of(Scene, value);
        }
    }

    /// <summary>
    /// Gets the lines and actors a gone transaction leaves uncertain (sketch GR9a), drawn dashed; worked out with the
    /// marks, so that the graph never draws a trail the marks do not show.
    /// </summary>
    public GoneTrail GoneTrail { get; private set; } = GoneTrail.Empty;

    /// <summary>Gets the line whose block mark has its box open, or <see langword="null"/>.</summary>
    public string? OpenBlockMark { get; private set; }

    /// <summary>Gets the wait for a source shown in the graph's head, or <see langword="null"/> (sketch GR9a).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWait), nameof(WaitLeftText), nameof(WaitLeftShare), nameof(WaitRows))]
    public partial SourceWait? Wait { get; private set; }

    /// <summary>Gets whether a chain's sources all ask Chainsaw to wait.</summary>
    public bool HasWait => Wait is not null;

    /// <summary>Gets how long is left of the wait, for example <c>34 s</c>.</summary>
    public string WaitLeftText => Wait?.LeftText(_clock.UtcNow) ?? string.Empty;

    /// <summary>Gets how much of the wait is left, for the ring that drains: 1 at its start and 0 at its end.</summary>
    public double WaitLeftShare => Wait?.LeftShare(_clock.UtcNow) ?? 0;

    /// <summary>Gets the endpoints in the wait's box, each with how long it has left.</summary>
    public IReadOnlyList<SourceWaitLine> WaitRows => Wait is { } wait
        ? [.. wait.Rows.Select(row => new SourceWaitLine(row.Host, row.Why, row.LeftText(_clock.UtcNow), row.IsWaiting))]
        : [];

    /// <summary>Says whether a node stands turned.</summary>
    /// <param name="nodeKey">The node.</param>
    /// <returns><see langword="true"/> when its back is shown.</returns>
    public bool IsTurned(string nodeKey) => _cards.ContainsKey(nodeKey);

    /// <summary>Says whether a node waits for its source: its expansion runs while the chain's sources all wait.</summary>
    /// <param name="nodeKey">The node.</param>
    /// <returns><see langword="true"/> when it gets the hourglass.</returns>
    public bool IsWaitingNode(string nodeKey) =>
        Wait is { } wait && nodeKey == _expandingNode && Scene?.Node(nodeKey)?.Node.Chain == wait.Chain;

    /// <summary>
    /// Turns a node's card (Alt + click, sketch IN4a): its back shows what is left at the address, asked for through
    /// the exit; turned again, the front comes back. Several cards can stand turned at once. A pile or a clue has no
    /// back.
    /// </summary>
    /// <param name="nodeKey">The node.</param>
    public void ToggleTurn(string nodeKey)
    {
        if (_cards.Remove(nodeKey))
        {
            Redraw?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (Scene?.Node(nodeKey) is not { } node || node.Look.Sort is NodeSort.Pile or NodeSort.HeldBack
            || node.Node.Kind is not (GraphNodeKind.Address or GraphNodeKind.Cluster))
        {
            return;
        }

        _cards[nodeKey] = HoldingsCards.Fetching(nodeKey);
        _turnIds[nodeKey] = ++_turns;
        Redraw?.Invoke(this, EventArgs.Empty);
        _ = FillCardAsync(node, _turns);
    }

    /// <summary>Opens or closes a block mark's box (a click on the mark).</summary>
    /// <param name="edgeKey">The line, or <see langword="null"/> to close the box.</param>
    public void ToggleBlockMark(string? edgeKey)
    {
        OpenBlockMark = edgeKey is not null && OpenBlockMark != edgeKey ? edgeKey : null;
        Redraw?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The Guide's heading that explains a reorganisation of the chain.</summary>
    public const string ReorganisationHeading = "Omorganisation av kedjan";

    /// <summary>
    /// Opens the Guide from the <em>?</em> in a block mark's box: on a transaction that is gone, or else on a
    /// reorganisation of the chain.
    /// </summary>
    public void OpenBlockMarkGuide()
    {
        var key = OpenBlockMark ?? string.Empty;
        if (BlockMarksByLine.TryGetValue(key, out var mark) && mark.IsGone)
        {
            OpenGoneGuide();
            return;
        }

        var request = GuideRequest.ForTopic(GuideTopic.ChainCheck);
        GuideWanted?.Invoke(this, request with { Heading = ReorganisationHeading, IsChoice = true });
    }

    /// <summary>
    /// Reads the waits anew and moves the countdown on; the view calls it every second while a wait is shown, and a
    /// wait that is over is read again at once.
    /// </summary>
    public void TickWait()
    {
        if (Wait is { } wait && _clock.UtcNow >= wait.Until)
        {
            ReadWaits();
            return;
        }

        OnPropertyChanged(nameof(WaitLeftText));
        OnPropertyChanged(nameof(WaitLeftShare));
        OnPropertyChanged(nameof(WaitRows));
    }

    /// <summary>Wires the chains' state: the balances, the marks and the waits.</summary>
    /// <param name="state">The graph's way to the chains, or <see langword="null"/>.</param>
    /// <param name="sources">The chains' endpoints, or <see langword="null"/>.</param>
    /// <param name="ui">The UI thread.</param>
    private void UseChainState(IGraphChainState? state, IChainSources? sources, IUiThread ui)
    {
        _chainState = state;
        _chainSources = sources;
        if (sources is not null)
        {
            sources.Changed += (_, _) => ui.Post(ReadWaits);
        }

        PropertyChanged += OnChainStateWatched;

        // Another case, or none: nothing of the former case's chains stays on screen.
        CaseChanged += (_, _) => ForgetChainState();
        ReadWaits();
    }

    /// <summary>Follows the graph, the expansion and the selection view.</summary>
    /// <param name="sender">The view model.</param>
    /// <param name="e">What changed.</param>
    private void OnChainStateWatched(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Scene):
                KeepCards();
                _ = ReadMarksAsync();
                break;
            case nameof(IsExpanding):
                _expandingNode = IsExpanding ? SelectedNode : null;
                Redraw?.Invoke(this, EventArgs.Empty);
                break;
            case nameof(HeldBack):
                KeepLens();
                break;
            default:
                break;
        }
    }

    /// <summary>Forgets everything about the former case's chains, so that nothing of it stays on screen.</summary>
    private void ForgetChainState()
    {
        _cards.Clear();
        _turnIds.Clear();
        _marksVersion++;
        _changes = [];
        _askedReplacements.Clear();
        BlockMarksByLine = new Dictionary<string, BlockMark>();
        OpenBlockMark = null;
        OpenSwap = null;
        Timeline.ClearLens();
        Timeline.ShowNewestBlock(null);
    }

    /// <summary>Drops the cards of nodes that are no longer in the graph, and the marks that went with them.</summary>
    private void KeepCards()
    {
        foreach (var key in _cards.Keys.Where(key => Scene?.Node(key) is null).ToList())
        {
            _cards.Remove(key);
        }

        BlockMarksByLine = BlockMarks.ForLines(Scene, _changes, _clock.UtcNow, Zone);
        if (OpenBlockMark is { } open && !BlockMarksByLine.ContainsKey(open))
        {
            OpenBlockMark = null;
        }
    }

    /// <summary>Reads the block changes and the newest block from the case, off the UI thread, sending nothing.</summary>
    /// <returns>A task that completes when they are shown.</returns>
    private async Task ReadMarksAsync()
    {
        if (_chainState is not { } state || _session.Current is not { } openCase)
        {
            return;
        }

        var version = ++_marksVersion;
        ChainMarksReading reading;
        try
        {
            reading = await state.ReadMarksAsync(openCase, _work.Token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The marks are a help to the eye; the graph stands without them, and the next reading tries again.
            return;
        }

        // A newer reading, or another case, came meanwhile: this answer is dropped.
        if (version != _marksVersion || _session.Current != openCase)
        {
            return;
        }

        _changes = reading.Changes;
        BlockMarksByLine = BlockMarks.ForLines(Scene, _changes, _clock.UtcNow, Zone);
        Timeline.ShowNewestBlock(reading.Tip);
        Redraw?.Invoke(this, EventArgs.Empty);
        await AskReplacementsAsync();
    }

    /// <summary>
    /// Fills a turned card: the balance through the exit (from the case when it cannot be asked), then today's rate,
    /// or offline the case's newest rate with its date.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="turn">The turn it is filled for.</param>
    /// <returns>A task that completes when the card is filled.</returns>
    private async Task FillCardAsync(NodeView node, int turn)
    {
        var key = node.Key;
        if (_chainState is not { } state || _session.Current is not { } openCase)
        {
            Fill(key, turn, HoldingsCards.Build(key, Unwired, Rates(), CardRateKind.None, null, _clock.UtcNow, Zone));
            return;
        }

        try
        {
            var holdings = await state.HoldingsAsync(openCase, node.Node, _work.Token);
            var since = EmptySince(key);
            var fromCase = IsOffline ? CardRateKind.FromCase : CardRateKind.Fetching;
            if (!Fill(key, turn, HoldingsCards.Build(key, holdings, Rates(), fromCase, since, _clock.UtcNow, Zone))
                || IsOffline || holdings.Held.Count == 0)
            {
                return;
            }

            var assets = holdings.Held.Select(amount => amount.Asset).ToList();
            var today = await state.TodayRatesAsync(openCase, assets, _clock.UtcNow, _work.Token);
            var (rates, kind) = today.Count > 0 ? (today, CardRateKind.Today) : (Rates(), CardRateKind.NoNewRate);
            Fill(key, turn, HoldingsCards.Build(key, holdings, rates, kind, since, _clock.UtcNow, Zone));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Fill(key, turn, HoldingsCards.Build(key, Unwired, Rates(), CardRateKind.None, null, _clock.UtcNow, Zone));
        }
    }

    /// <summary>What a card says when its balance could not be had for another reason than the source's.</summary>
    public const string CardFailedText = "Saldot kunde inte hämtas.";

    /// <summary>What a card says when the graph has no way to the chains, or its balance could not be had.</summary>
    private static readonly NodeHoldings Unwired = new([], null, null, false, CardFailedText);

    /// <summary>Puts a card's new back in place, when the card still stands turned in the same turn.</summary>
    /// <param name="key">The node.</param>
    /// <param name="turn">The turn the back is for.</param>
    /// <param name="card">The back.</param>
    /// <returns><see langword="true"/> when it still stood turned in that turn.</returns>
    private bool Fill(string key, int turn, HoldingsCard card)
    {
        if (!_cards.ContainsKey(key) || _turnIds.GetValueOrDefault(key) != turn)
        {
            return false;
        }

        _cards[key] = card;
        Redraw?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Gives the case's newest rate of each asset, as the graph read it.</summary>
    /// <returns>The rates.</returns>
    private IReadOnlyDictionary<Asset, Price> Rates() =>
        Scene?.Snapshot.Rates ?? new Dictionary<Asset, Price>();

    /// <summary>Gives when the money last left a node along the graph's lines, for an empty address.</summary>
    /// <param name="key">The node.</param>
    /// <returns>The newest block time of a payment out, or <see langword="null"/>.</returns>
    private DateTimeOffset? EmptySince(string key) => Scene?.Edges
        .Where(edge => edge.Edge.From == key)
        .SelectMany(edge => edge.Edge.Transfers)
        .Select(transfer => transfer.Block?.Time)
        .OfType<DateTimeOffset>()
        .DefaultIfEmpty()
        .Max() is { } newest && newest != default ? newest : null;

    /// <summary>Reads whether a chain's sources all wait now; Bitcoin first, then the others.</summary>
    private void ReadWaits()
    {
        if (_chainSources is not { } sources)
        {
            return;
        }

        var now = _clock.UtcNow;
        SourceWait? shown = null;
        foreach (var chain in sources.Chains)
        {
            var wait = SourceWait.Read(sources.Get(chain), now, SinceOf(chain, sources.Get(chain), now));
            if (wait is null)
            {
                _waitSince.Remove(chain);
                continue;
            }

            _waitSince[chain] = (wait.Since, wait.Until);
            shown ??= wait;
        }

        Wait = shown;
        Redraw?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gives when a chain's wait began: when it was first seen, unless the pause now ends later than the one seen then,
    /// which is a new wait whose ring starts full (finding M6).
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="view">Its sources now.</param>
    /// <param name="now">The time now.</param>
    /// <returns>The start, or <see langword="null"/> for now.</returns>
    private DateTimeOffset? SinceOf(Chain chain, ChainSourcesView view, DateTimeOffset now)
    {
        if (!_waitSince.TryGetValue(chain, out var seen))
        {
            return null;
        }

        var ends = view.Endpoints.Select(endpoint => endpoint.WaitUntil).Where(until => until > now).Min();
        return ends is { } until && until > seen.Until ? null : seen.Since;
    }

    /// <summary>
    /// Keeps the selection view's window as the strip's lens when the view closes (sketch TL2a), with its held-back
    /// transactions and the traced arrival.
    /// </summary>
    private void KeepLens()
    {
        if (HeldBack is { } open)
        {
            _openSelection = open;
            return;
        }

        if (_openSelection is not { } closed)
        {
            return;
        }

        _openSelection = null;
        var times = closed.Choices.Choices.Select(choice => choice.Transaction.Time).OfType<DateTimeOffset>().ToList();
        Timeline.ShowLens(closed.WindowAt, closed.WindowHours, closed.Choices.Arrival, times);
    }
}

/// <summary>One endpoint in the wait's box as the view shows it.</summary>
/// <param name="Host">The endpoint's host.</param>
/// <param name="Why">Why it is not asked.</param>
/// <param name="Left">How long it has left, or a dash.</param>
/// <param name="IsWaiting">Whether it asked Chainsaw to wait (an amber dot) rather than not answering.</param>
public sealed record SourceWaitLine(string Host, string Why, string Left, bool IsWaiting);
