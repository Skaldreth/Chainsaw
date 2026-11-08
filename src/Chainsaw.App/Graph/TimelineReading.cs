using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>
/// What an event on the view Tidslinje is (sketch TL3a): for a transaction, what the money became, in the colours of
/// the visual language (GR7a); for the case's own events, a label, a decision or a watch's hit.
/// </summary>
public enum TimelineKind
{
    /// <summary>The victims' money, traced by the method (accent).</summary>
    Victim,

    /// <summary>The victims' money on a line that rests on something uncertain (accent, dashed).</summary>
    Guess,

    /// <summary>To a cash-out (blue).</summary>
    CashOut,

    /// <summary>To where money is spent (yellow).</summary>
    Spent,

    /// <summary>To risk or the end of the trail (red).</summary>
    Risk,

    /// <summary>On to another chain (purple, dashed).</summary>
    OtherChain,

    /// <summary>Money the trace does not follow (grey).</summary>
    Unknown,

    /// <summary>A label the user set in the case.</summary>
    Label,

    /// <summary>A decision about a suggestion.</summary>
    Decision,

    /// <summary>A watch's hit.</summary>
    Watch,
}

/// <summary>The chips in the view's head that hide kinds of events (sketch TL3a).</summary>
public enum TimelineFilter
{
    /// <summary><em>Offrens pengar</em>, also those that rest on a suggestion.</summary>
    Victim,

    /// <summary><em>Uttag</em>.</summary>
    CashOut,

    /// <summary><em>Spenderas</em>.</summary>
    Spent,

    /// <summary><em>Risk och slut</em>.</summary>
    Risk,

    /// <summary><em>Annan kedja</em>.</summary>
    OtherChain,

    /// <summary><em>Okänt</em>.</summary>
    Unknown,

    /// <summary><em>Ärendet</em>: the labels and decisions.</summary>
    Case,

    /// <summary><em>Bevakning</em>: the watches' hits.</summary>
    Watch,
}

/// <summary>A lane of the view Tidslinje: an actor of the graph, or the case's own lane last.</summary>
/// <param name="Key">The node's key, or <see cref="TimelineReading.CaseLane"/>.</param>
/// <param name="Name">What the graph calls it.</param>
/// <param name="Line">The faint line under the name.</param>
/// <param name="Look">How its sort is drawn.</param>
public sealed record TimelineLane(string Key, string Name, string Line, SortLook Look)
{
    /// <summary>Gets whether it is the case's own lane.</summary>
    public bool IsCase => Key == TimelineReading.CaseLane;
}

/// <summary>A block as the inspector writes it: the facts every step can be checked against (principle 1).</summary>
/// <param name="Height">Its height.</param>
/// <param name="Hash">Its hash.</param>
public sealed record TimelineBlock(long Height, string Hash);

/// <summary>
/// An event on the view Tidslinje. A transfer is a fact from the chain (its transaction, amount and block); what it
/// became, the colour, is the method's and the attribution's judgement (principle 2). The case's own events are the
/// user's or the watch's, with who and the journal entry that seals them.
/// </summary>
/// <param name="Id">A stable id within the reading.</param>
/// <param name="Kind">What it is.</param>
/// <param name="Time">When: the block's time, or when the user did it; <see langword="null"/> while unconfirmed.</param>
/// <param name="From">The sending actor's key, for a transfer.</param>
/// <param name="To">The receiving actor's key, for a transfer.</param>
/// <param name="On">The actor a case event concerns.</param>
/// <param name="Amount">The amount, for a transfer or a watch's hit.</param>
/// <param name="Transaction">The transaction, for a transfer or a watch's hit.</param>
/// <param name="Block">The block, when confirmed.</param>
/// <param name="EdgeKey">The graph's line it lies on, for a transfer.</param>
/// <param name="Title">What a case event says, for example <c>Etikett: Börs A</c>.</param>
/// <param name="Who">Who did it, for a case event.</param>
/// <param name="Why">The reason or what it rests on, or <see langword="null"/>.</param>
/// <param name="Journal">The journal entry that seals a case event, or <see langword="null"/>.</param>
public sealed record TimelineEvent(
    string Id,
    TimelineKind Kind,
    DateTimeOffset? Time,
    string? From,
    string? To,
    string? On,
    AssetAmount? Amount,
    ChainTransactionRef? Transaction,
    TimelineBlock? Block,
    string? EdgeKey,
    string Title,
    string? Who,
    string? Why,
    long? Journal)
{
    /// <summary>Gets whether it is a transfer on the chain, drawn as a stroke between two lanes.</summary>
    public bool IsTransfer => From is not null && To is not null;

    /// <summary>
    /// Gets the actor whose lane the inspector's sequence follows: the receiver, or the actor concerned.
    /// </summary>
    public string Focus => To ?? On ?? string.Empty;

    /// <summary>Gets the filter chip that hides it.</summary>
    public TimelineFilter Filter => TimelineReading.FilterOf(Kind);
}

/// <summary>A case event before it is placed: a label, a decision or a watch's hit, with the actor it concerns.</summary>
/// <param name="Kind">Label, decision or watch.</param>
/// <param name="NodeKey">The actor it concerns.</param>
/// <param name="Time">When.</param>
/// <param name="Title">What it says.</param>
/// <param name="Who">Who did it.</param>
/// <param name="Why">The reason, or <see langword="null"/>.</param>
/// <param name="Journal">The journal entry that seals it.</param>
public sealed record TimelineMoment(
    TimelineKind Kind, string NodeKey, DateTimeOffset Time, string Title, string Who, string? Why, long Journal)
{
    /// <summary>Gets the watch hit's amount, or <see langword="null"/>.</summary>
    public AssetAmount? Amount { get; init; }

    /// <summary>Gets the watch hit's transaction, or <see langword="null"/>.</summary>
    public ChainTransactionRef? Transaction { get; init; }

    /// <summary>Gets the watch hit's block, or <see langword="null"/>.</summary>
    public TimelineBlock? Block { get; init; }
}

/// <summary>Everything the view Tidslinje shows: the lanes, top down, and the events in time order.</summary>
/// <param name="Lanes">The lanes, the case's own last.</param>
/// <param name="Events">The events, oldest first; unconfirmed transfers last.</param>
/// <param name="Method">The tracing method the colours rest on.</param>
public sealed record TimelineData(
    IReadOnlyList<TimelineLane> Lanes, IReadOnlyList<TimelineEvent> Events, TracingMethodKind Method)
{
    /// <summary>Gets the reading of an empty graph.</summary>
    public static TimelineData Empty { get; } = new([], [], TracingMethodKind.Fifo);

    /// <summary>Gets whether there is anything to show.</summary>
    public bool HasEvents => Events.Count > 0;
}

/// <summary>
/// Reads the view Tidslinje (sketch TL3a) out of the graph shown and the case's own events, without WPF: a lane per
/// actor in an order where the money mostly runs downwards (by hop, then by first event), the transfers as events in
/// time order, and the case's own lane last. It writes nothing in the case.
/// </summary>
public static class TimelineReading
{
    /// <summary>The key of the case's own lane.</summary>
    public const string CaseLane = "timeline:case";

    /// <summary>What the case's own lane is called.</summary>
    public const string CaseLaneName = "Ärendet";

    /// <summary>The faint line under the case's own lane.</summary>
    public const string CaseLaneLine = "egna händelser";

    /// <summary>The case lane's look: the file icon in the quiet grey of the case's own events.</summary>
    private static readonly SortLook CaseLook = new(NodeSort.Unknown, CaseLaneName, "Icon.File", GraphInk.Unknown,
        NodeShape.Square, false);

    /// <summary>Gives the filter chip a kind falls under.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The chip.</returns>
    public static TimelineFilter FilterOf(TimelineKind kind) => kind switch
    {
        TimelineKind.Victim or TimelineKind.Guess => TimelineFilter.Victim,
        TimelineKind.CashOut => TimelineFilter.CashOut,
        TimelineKind.Spent => TimelineFilter.Spent,
        TimelineKind.Risk => TimelineFilter.Risk,
        TimelineKind.OtherChain => TimelineFilter.OtherChain,
        TimelineKind.Label or TimelineKind.Decision => TimelineFilter.Case,
        TimelineKind.Watch => TimelineFilter.Watch,
        _ => TimelineFilter.Unknown,
    };

    /// <summary>Gives a kind's name, as the inspector and the chips write it.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The name, in Swedish.</returns>
    public static string NameOf(TimelineKind kind) => kind switch
    {
        TimelineKind.Victim => "Offrens pengar",
        TimelineKind.Guess => "Offrens pengar, förslag",
        TimelineKind.CashOut => "Uttag",
        TimelineKind.Spent => "Spenderas",
        TimelineKind.Risk => "Risk och slut",
        TimelineKind.OtherChain => "Annan kedja",
        TimelineKind.Label => "Etikett",
        TimelineKind.Decision => "Beslut",
        TimelineKind.Watch => "Bevakning",
        _ => "Okänt",
    };

    /// <summary>Gives a chip's name.</summary>
    /// <param name="filter">The chip.</param>
    /// <returns>The name, in Swedish.</returns>
    public static string NameOf(TimelineFilter filter) => filter switch
    {
        TimelineFilter.Victim => "Offrens pengar",
        TimelineFilter.CashOut => "Uttag",
        TimelineFilter.Spent => "Spenderas",
        TimelineFilter.Risk => "Risk och slut",
        TimelineFilter.OtherChain => "Annan kedja",
        TimelineFilter.Case => CaseLaneName,
        TimelineFilter.Watch => "Bevakning",
        _ => "Okänt",
    };

    /// <summary>
    /// Gives the colour of a kind; the case's own events are quiet grey, a watch's hit its own dull tone.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The resource key of its brush.</returns>
    public static string BrushOf(TimelineKind kind) => kind switch
    {
        TimelineKind.Victim or TimelineKind.Guess => GraphInkKeys.Of(GraphInk.Accent),
        TimelineKind.CashOut => GraphInkKeys.Of(GraphInk.CashOut),
        TimelineKind.Spent => GraphInkKeys.Of(GraphInk.Spent),
        TimelineKind.Risk => GraphInkKeys.Of(GraphInk.Risk),
        TimelineKind.OtherChain => GraphInkKeys.Of(GraphInk.OtherChain),
        TimelineKind.Watch => "Brush.Watch",
        TimelineKind.Label or TimelineKind.Decision => "Brush.TextMuted",
        _ => GraphInkKeys.Of(GraphInk.Unknown),
    };

    /// <summary>Gives a chip's colour.</summary>
    /// <param name="filter">The chip.</param>
    /// <returns>The resource key of its brush.</returns>
    public static string BrushOf(TimelineFilter filter) => filter switch
    {
        TimelineFilter.Victim => BrushOf(TimelineKind.Victim),
        TimelineFilter.CashOut => BrushOf(TimelineKind.CashOut),
        TimelineFilter.Spent => BrushOf(TimelineKind.Spent),
        TimelineFilter.Risk => BrushOf(TimelineKind.Risk),
        TimelineFilter.OtherChain => BrushOf(TimelineKind.OtherChain),
        TimelineFilter.Case => BrushOf(TimelineKind.Label),
        TimelineFilter.Watch => BrushOf(TimelineKind.Watch),
        _ => BrushOf(TimelineKind.Unknown),
    };

    /// <summary>Gives whether a kind is drawn dashed, since dashed always means uncertain (GR3a).</summary>
    /// <param name="kind">The kind.</param>
    /// <returns><see langword="true"/> for a suggestion and another chain.</returns>
    public static bool IsDashed(TimelineKind kind) => kind is TimelineKind.Guess or TimelineKind.OtherChain;

    /// <summary>Gives whether a kind is a judgement shown in a dashed box in the inspector (principle 2).</summary>
    /// <param name="kind">The kind.</param>
    /// <returns><see langword="false"/> only for money the trace does not follow, and the user's own labels.</returns>
    public static bool IsJudged(TimelineKind kind) => kind is not (TimelineKind.Unknown or TimelineKind.Label);

    /// <summary>Reads the timeline out of the graph and the case's own events.</summary>
    /// <param name="scene">The graph shown.</param>
    /// <param name="moments">The case's own events, already tied to the graph's actors.</param>
    /// <returns>The lanes and events.</returns>
    public static TimelineData Read(GraphScene scene, IReadOnlyList<TimelineMoment> moments)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(moments);
        var actors = ExposureMap.Actors(scene);
        var looks = ExposureMap.Looks(scene);
        var traced = Traced(scene);
        var events = new List<TimelineEvent>();
        foreach (var edge in scene.Snapshot.Graph.Edges)
        {
            if (edge.Kind == GraphEdgeKind.HeldBack || edge.From == edge.To
                || !actors.ContainsKey(edge.From) || !actors.ContainsKey(edge.To))
            {
                continue;
            }

            var kind = KindOf(edge, looks.GetValueOrDefault(edge.To), traced.Contains(edge.Key), IsUncertain(scene, edge));
            events.AddRange(edge.Transfers.Select((transfer, index) => Transfer(edge, transfer, index, kind)));
        }

        var placed = moments.Where(moment => actors.ContainsKey(moment.NodeKey)).ToList();
        events.AddRange(placed.Select((moment, index) => Moment(moment, index)));
        var ordered = events
            .OrderBy(each => each.Time is null)
            .ThenBy(each => each.Time ?? DateTimeOffset.MaxValue)
            .ThenBy(each => each.Id, StringComparer.Ordinal)
            .ToList();
        var lanes = Lanes(actors, ordered);
        return new TimelineData(lanes, ordered, scene.Snapshot.Method);
    }

    /// <summary>Reads the user's own labels set in this case, with who, when and why.</summary>
    /// <param name="scene">The graph, whose snapshot carries the labels by node.</param>
    /// <returns>A moment per own label entry.</returns>
    public static IReadOnlyList<TimelineMoment> Labels(GraphScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var moments = new List<TimelineMoment>();
        foreach (var (key, labels) in scene.Snapshot.Labels)
        {
            foreach (var label in labels)
            {
                var name = label.Name ?? label.Kind.Describe();
                moments.AddRange(label.Entries
                    .Where(entry => entry.Origin.Source == LabelSource.Own)
                    .Select(entry => new TimelineMoment(
                        TimelineKind.Label, key, entry.Origin.SetUtc, "Etikett: " + name, entry.Origin.SetBy.Name,
                        entry.Origin.Reason, entry.JournalSequence)));
            }
        }

        return moments;
    }

    /// <summary>Reads the watches' hits, on the lane of the actor watched.</summary>
    /// <param name="graph">The graph, which finds the actor an address belongs to.</param>
    /// <param name="watches">The case's watches.</param>
    /// <param name="hits">Their hits.</param>
    /// <returns>A moment per hit on an actor in the graph.</returns>
    public static IReadOnlyList<TimelineMoment> Hits(
        TraceGraph graph, IReadOnlyList<Watch> watches, IReadOnlyList<WatchHit> hits)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(watches);
        ArgumentNullException.ThrowIfNull(hits);
        var byId = watches.ToDictionary(watch => watch.Id, StringComparer.Ordinal);
        var moments = new List<TimelineMoment>();
        foreach (var hit in hits)
        {
            if (!byId.TryGetValue(hit.WatchId, out var watch) || graph.NodeOf(watch.Subject.Anchor) is not { } node)
            {
                continue;
            }

            var match = hit.Match;
            moments.Add(new TimelineMoment(
                TimelineKind.Watch, node.Key, match.Block.Time, "Bevakningen slog till", "Bakgrunden",
                watch.Subject.Name, hit.JournalSequence)
            {
                Amount = match.Amount,
                Transaction = Reference(hit.Chain, match.Transaction),
                Block = new TimelineBlock(match.Block.Height, match.Block.Hash),
            });
        }

        return moments;
    }

    /// <summary>Reads the user's decisions about suggestions, on the lane of the candidate's or finding's actor.</summary>
    /// <param name="graph">The graph, which finds the actor an address belongs to.</param>
    /// <param name="checks">The sealed checks with their decisions.</param>
    /// <returns>A moment per approving or dismissing decision on an actor in the graph.</returns>
    public static IReadOnlyList<TimelineMoment> Decisions(TraceGraph graph, IReadOnlyList<StoredCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(checks);
        var moments = new List<TimelineMoment>();
        foreach (var check in checks)
        {
            foreach (var decision in check.Decisions.Where(d => d.Verdict != CheckVerdict.Withdrawn))
            {
                var candidate = check.Finding.Candidates.FirstOrDefault(c => c.Key == decision.CandidateKey);
                var address = candidate?.Address ?? check.Finding.Addresses.FirstOrDefault();
                if (address is null || graph.NodeOf(address) is not { } node)
                {
                    continue;
                }

                var verdict = decision.Verdict == CheckVerdict.Approved ? "godkänd" : "avfärdad";
                var why = decision.Reason is { Length: > 0 } reason
                    ? reason
                    : $"{check.AnalysisName} {check.Analysis.Version}";
                moments.Add(new TimelineMoment(
                    TimelineKind.Decision, node.Key, decision.DecidedUtc, $"{check.Finding.Title} {verdict}",
                    decision.DecidedBy.Name, why, decision.JournalSequence));
            }
        }

        return moments;
    }

    /// <summary>Gives what a line's money became, from the receiver's sort and whether the trace follows it.</summary>
    /// <param name="edge">The line.</param>
    /// <param name="receiver">How the receiver is drawn, or <see langword="null"/>.</param>
    /// <param name="isTraced">Whether the line carries traced money.</param>
    /// <param name="isUncertain">Whether the line rests on something uncertain.</param>
    /// <returns>The kind.</returns>
    public static TimelineKind KindOf(GraphEdge edge, SortLook? receiver, bool isTraced, bool isUncertain)
    {
        ArgumentNullException.ThrowIfNull(edge);
        if (edge.Kind == GraphEdgeKind.ContinuesOnOtherChain)
        {
            return TimelineKind.OtherChain;
        }

        var group = receiver is null ? ExposureGroup.Unknown : ExposureMap.GroupOf(receiver);
        return group switch
        {
            ExposureGroup.CashOut => TimelineKind.CashOut,
            ExposureGroup.Spent => TimelineKind.Spent,
            ExposureGroup.Risk => TimelineKind.Risk,
            ExposureGroup.OtherChain => TimelineKind.OtherChain,
            _ when isTraced && isUncertain => TimelineKind.Guess,
            _ when isTraced => TimelineKind.Victim,
            _ => TimelineKind.Unknown,
        };
    }

    /// <summary>Gives the lines that carry traced money, by the method (conclusion).</summary>
    /// <param name="scene">The graph.</param>
    /// <returns>The lines' keys.</returns>
    private static HashSet<string> Traced(GraphScene scene)
    {
        var keys = scene.Edges.Where(edge => edge.IsTraced).Select(edge => edge.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var edge in scene.Snapshot.Trace?.Edges ?? [])
        {
            if (edge.Amount.Amount.Units.Sign > 0)
            {
                keys.Add(edge.EdgeKey);
            }
        }

        return keys;
    }

    /// <summary>Gives whether a line is drawn dashed in the graph, as uncertain.</summary>
    /// <param name="scene">The graph.</param>
    /// <param name="edge">The line.</param>
    /// <returns><see langword="true"/> when dashed.</returns>
    private static bool IsUncertain(GraphScene scene, GraphEdge edge) =>
        scene.Edge(edge.Key) is { Dash: not EdgeDash.None };

    /// <summary>Makes the event of one transfer on a line.</summary>
    /// <param name="edge">The line.</param>
    /// <param name="transfer">The transfer.</param>
    /// <param name="index">Its place on the line.</param>
    /// <param name="kind">What the line's money became.</param>
    /// <returns>The event.</returns>
    private static TimelineEvent Transfer(GraphEdge edge, AssetTransfer transfer, int index, TimelineKind kind)
    {
        var block = transfer.Block is { } b ? new TimelineBlock(b.Height, b.Hash.Value) : null;
        // An unconfirmed transfer may still change, so the victims' money on it is dashed as uncertain.
        var shown = transfer.Block is null && kind == TimelineKind.Victim ? TimelineKind.Guess : kind;
        return new TimelineEvent(
            $"tx:{edge.Key}:{index}", shown, transfer.Block?.Time,
            edge.From, edge.To, null, transfer.Amount, transfer.Transaction, block, edge.Key, string.Empty, null, null,
            null);
    }

    /// <summary>Makes the event of a case moment.</summary>
    /// <param name="moment">The moment.</param>
    /// <param name="index">Its place among the moments.</param>
    /// <returns>The event.</returns>
    private static TimelineEvent Moment(TimelineMoment moment, int index) => new(
        $"case:{index:D6}", moment.Kind, moment.Time, null, null, moment.NodeKey, moment.Amount, moment.Transaction,
        moment.Block, null, moment.Title, moment.Who, moment.Why, moment.Journal);

    /// <summary>
    /// Gives the lanes: every actor with an event, by hop (the start first, those before it above, those without a
    /// hop last) and then by its first event, and the case's own lane last when it has an event.
    /// </summary>
    /// <param name="actors">The actors drawn.</param>
    /// <param name="events">The events in time order.</param>
    /// <returns>The lanes.</returns>
    private static List<TimelineLane> Lanes(
        IReadOnlyDictionary<string, NodeView> actors, IReadOnlyList<TimelineEvent> events)
    {
        var first = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < events.Count; index++)
        {
            foreach (var key in new[] { events[index].From, events[index].To, events[index].On })
            {
                if (key is not null)
                {
                    first.TryAdd(key, index);
                }
            }
        }

        var lanes = first.Keys
            .Select(key => actors[key])
            .OrderBy(node => node.Node.Hop is null)
            .ThenBy(node => node.Node.Hop ?? 0)
            .ThenBy(node => first[node.Key])
            .Select(node => new TimelineLane(node.Key, node.Name, LineOf(node), node.Look))
            .ToList();
        if (events.Any(each => !each.IsTransfer))
        {
            lanes.Add(new TimelineLane(CaseLane, CaseLaneName, CaseLaneLine, CaseLook));
        }

        return lanes;
    }

    /// <summary>Gives the faint line under an actor's name: the cluster's size, or the address under a name.</summary>
    /// <param name="node">The actor.</param>
    /// <returns>The line.</returns>
    public static string LineOf(NodeView node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Count.Length > 0)
        {
            return node.Count;
        }

        return node.IsNamed ? GraphScene.Shorten(node.Subject) : node.Look.Name;
    }

    /// <summary>Makes a transaction's reference, or nothing when the id is not in the chain's form.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="id">The id.</param>
    /// <returns>The reference, or <see langword="null"/>.</returns>
    private static ChainTransactionRef? Reference(Chain chain, string id)
    {
        try
        {
            return new ChainTransactionRef(chain, id);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
