using System.Globalization;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>What happened to a node, as a row in the inspector's <em>Historik</em> (sketch AN1b).</summary>
public enum HistoryKind
{
    /// <summary>Money first came to the node on the chain: a fact.</summary>
    FirstReceived,

    /// <summary>A heuristic merged the node's addresses: an automatic conclusion.</summary>
    Clustered,

    /// <summary>A label was set on the node.</summary>
    Label,

    /// <summary>The user broke addresses out of the node's cluster.</summary>
    Split,

    /// <summary>The user linked money that came to the node to a transaction on another chain.</summary>
    Link,

    /// <summary>A label or a link was taken back, or a split undone: a sealed record of its own.</summary>
    Withdrawal,

    /// <summary>The user merged addresses with the node's, for example by approving a wallet (user v1).</summary>
    Merge,

    /// <summary>The user made, changed, paused or resumed a watch on the node (sketch BV1a).</summary>
    Watch,

    /// <summary>A watch on the node found money moving: a fact from the chain that the watch picked out.</summary>
    WatchHit,

    /// <summary>A comment was written, changed, tied or thrown in the trash and put back (sketch KN1a).</summary>
    Comment,

    /// <summary>The user chose that the traced value does not follow through a swap of the node's (sketch SW1a).</summary>
    SwapStop,

    /// <summary>A version of an analysis was drawn back (sketch KV1a).</summary>
    VersionWithdrawn,

    /// <summary>A conclusion the latest run held back, and the user's decision about it (sketch HB1a).</summary>
    HeldBack,
}

/// <summary>What the row's button does, when the row is the user's own and still counts.</summary>
public enum HistoryUndo
{
    /// <summary>Nothing: a fact, an automatic conclusion, someone else's judgement, or already taken back.</summary>
    None,

    /// <summary>Takes a label back (<em>Ta tillbaka</em>), and the label library follows.</summary>
    RetractLabel,

    /// <summary>Undoes a breaking out (<em>Ångra</em>), after a confirmation of what merges again.</summary>
    UndoSplit,

    /// <summary>Takes a link to another chain back (<em>Ta tillbaka</em>).</summary>
    Unlink,

    /// <summary>Takes a watch back (<em>Ta tillbaka</em>): it stops, and stays in the case struck through.</summary>
    RemoveWatch,

    /// <summary>
    /// Undoes a merge of the user's own (<em>Ångra</em>); when an approved wallet made it, the approval is taken back.
    /// </summary>
    UndoMerge,

    /// <summary>
    /// Takes back a choice that the value does not follow through a swap (<em>Ta tillbaka</em>): it follows again.
    /// </summary>
    FollowSwap,

    /// <summary>Takes a version's withdrawal back (<em>Ta tillbaka</em>): the version counts again (sketch KV1a).</summary>
    TakeBackWithdrawal,

    /// <summary>
    /// Decides about a conclusion that is held back: <em>Släpp slutsatsen</em>, or <em>Låt stå</em> (sketch HB1a).
    /// </summary>
    DecideHeldBack,

    /// <summary>Takes a decision about a held back conclusion back (<em>Ångra</em>), as a new sealed decision.</summary>
    ReopenHeldBack,
}

/// <summary>One row in the node's history (sketch AN1b).</summary>
/// <param name="Key">The row's stable key, so that an open row stays open when the history is read again.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Utc">When, in UTC.</param>
/// <param name="Text">The row's words, for example <c>Etikett · Börs A</c>.</param>
/// <param name="Sub">The quiet line under: who, and what it rests on.</param>
/// <param name="Icon">The icon's resource key.</param>
/// <param name="Ink">The icon's colour.</param>
/// <param name="IsDashed">Whether the icon's frame is dashed: an automatic conclusion or a link, never a fact.</param>
/// <param name="IsFact">Whether it is a fact from the chain, drawn the quietest.</param>
/// <param name="IsWithdrawn">Whether it is taken back or undone: struck through and toned, but never removed.</param>
/// <param name="Undo">What the row's button does.</param>
public sealed record HistoryEvent(
    string Key,
    HistoryKind Kind,
    DateTimeOffset Utc,
    string Text,
    string Sub,
    string Icon,
    GraphInk Ink,
    bool IsDashed,
    bool IsFact,
    bool IsWithdrawn,
    HistoryUndo Undo)
{
    /// <summary>
    /// Gets whether the row's journal line was added on another computer (<c>ICase.WasAddedElsewhere</c>): a small
    /// muted mark with <see cref="NodeHistory.ElsewhereText"/> on hovering.
    /// </summary>
    public bool AddedElsewhere { get; init; }

    /// <summary>Gets the label's sequence, for <see cref="HistoryUndo.RetractLabel"/>.</summary>
    public long LabelSequence { get; init; }

    /// <summary>
    /// Gets whether the label may have reached the label library, which then also learns that it is taken back.
    /// </summary>
    public bool ReachesLibrary { get; init; }

    /// <summary>Gets the splits of one breaking out, for <see cref="HistoryUndo.UndoSplit"/>.</summary>
    public IReadOnlyList<long> SplitSequences { get; init; } = [];

    /// <summary>Gets the link, for <see cref="HistoryUndo.Unlink"/>.</summary>
    public CrossChainLink? Link { get; init; }

    /// <summary>Gets the watch, for <see cref="HistoryUndo.RemoveWatch"/>.</summary>
    public string? WatchId { get; init; }

    /// <summary>Gets the merges stored together, for <see cref="HistoryUndo.UndoMerge"/>.</summary>
    public IReadOnlyList<long> MergeSequences { get; init; } = [];

    /// <summary>Gets the choice not to follow a swap, for <see cref="HistoryUndo.FollowSwap"/>.</summary>
    public SwapStop? SwapStop { get; init; }

    /// <summary>Gets the withdrawal, for <see cref="HistoryUndo.TakeBackWithdrawal"/>.</summary>
    public long WithdrawalSequence { get; init; }

    /// <summary>Gets the decision to take back, for <see cref="HistoryUndo.ReopenHeldBack"/>.</summary>
    public long DecisionSequence { get; init; }

    /// <summary>
    /// Gets the transaction whose conclusions are held back, for <see cref="HistoryUndo.DecideHeldBack"/>.
    /// </summary>
    public Txid? HeldTxid { get; init; }

    /// <summary>Gets whether the row has a button: it is the user's own and still counts.</summary>
    public bool CanUndo => Undo != HistoryUndo.None && !IsWithdrawn;

    /// <summary>
    /// Gets the button's words: <em>Ångra</em> for a breaking out or a merge, <em>Ta tillbaka</em> otherwise.
    /// </summary>
    public string UndoText => Undo switch
    {
        HistoryUndo.UndoSplit or HistoryUndo.UndoMerge or HistoryUndo.ReopenHeldBack => NodeHistory.UndoText,
        HistoryUndo.DecideHeldBack => NodeHistory.ReleaseText,
        _ => NodeHistory.RetractText,
    };

    /// <summary>Gets the time as the row writes it: the day, the month and the time, in local time.</summary>
    public string TimeText => NodeHistory.Short(Utc);

    /// <summary>Gets the row's tooltip: the time in local time and UTC (principle 1).</summary>
    public string ToolTip => InspectorContent.Time(Utc);
}

/// <summary>What the inspector reads from the modules to write a node's history.</summary>
/// <param name="Labels">The labels on the node's addresses, also those taken back.</param>
/// <param name="Splits">The splits that involve the node's addresses, also those undone.</param>
/// <param name="Links">Every link to another chain in the case, also those taken back.</param>
/// <param name="Cluster">The cluster, for a cluster; <see langword="null"/> otherwise.</param>
/// <param name="Operators">Who wrote each journal entry the history names, by the entry's sequence.</param>
public sealed record NodeHistoryData(
    IReadOnlyList<LabelHistoryEntry> Labels,
    IReadOnlyList<SplitHistoryEntry> Splits,
    IReadOnlyList<CrossChainLinkHistoryEntry> Links,
    Cluster? Cluster,
    IReadOnlyDictionary<long, string> Operators)
{
    /// <summary>
    /// Gets whether a journal line, by its sequence, was added on another computer (<c>ICase.WasAddedElsewhere</c>);
    /// none is, unless the reader says so.
    /// </summary>
    public Func<long, bool> IsElsewhere { get; init; } = _ => false;

    /// <summary>Gets every record of the case's watches, also of those taken back (Monitoring).</summary>
    public IReadOnlyList<WatchRecord> Watches { get; init; } = [];

    /// <summary>Gets every hit of the case's watches.</summary>
    public IReadOnlyList<WatchHit> WatchHits { get; init; } = [];

    /// <summary>Gets the user's merges that hold one of the node's addresses, also those undone (Clustering).</summary>
    public IReadOnlyList<MergeHistoryEntry> Merges { get; init; } = [];

    /// <summary>
    /// Gets the user's choices not to follow a swap of the node's (sketch SW1a), also those taken back (Tracing).
    /// </summary>
    public IReadOnlyList<SwapStopHistoryEntry> SwapStops { get; init; } = [];

    /// <summary>Gets the journal entries whose writer the history names for the choices not to follow a swap.</summary>
    /// <param name="stops">The choices.</param>
    /// <returns>The journal sequences of each choice and its taking back.</returns>
    public static IReadOnlySet<long> JournalNeeds(IEnumerable<SwapStopHistoryEntry> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        var needs = new HashSet<long>();
        foreach (var entry in stops)
        {
            needs.Add(entry.Stop.JournalSequence);
            if (entry.Undo is { } undo)
            {
                needs.Add(undo.JournalSequence);
            }
        }

        return needs;
    }

    /// <summary>Gets the versions drawn back of the analyses that concern the cluster (sketch KV1a).</summary>
    public IReadOnlyList<VersionWithdrawal> Withdrawals { get; init; } = [];

    /// <summary>Gets the conclusions about the node's addresses that the latest run held back (sketch HB1a).</summary>
    public IReadOnlyList<HeldBackConclusion> HeldBack { get; init; } = [];

    /// <summary>Gets the user's decisions about held back conclusions on the node's addresses.</summary>
    public IReadOnlyList<HeldBackDecision> HeldBackDecisions { get; init; } = [];

    /// <summary>Gets the journal entries whose writer the history names for the user's merges.</summary>
    /// <param name="merges">The merges.</param>
    /// <returns>The journal sequences of each merge and its undoing.</returns>
    public static IReadOnlySet<long> JournalNeeds(IEnumerable<MergeHistoryEntry> merges)
    {
        ArgumentNullException.ThrowIfNull(merges);
        var needs = new HashSet<long>();
        foreach (var entry in merges)
        {
            needs.Add(entry.Merge.JournalSequence);
            if (entry.Undo is { } undo)
            {
                needs.Add(undo.JournalSequence);
            }
        }

        return needs;
    }

    /// <summary>Gets the journal entries whose writer the history names.</summary>
    /// <param name="labels">The labels.</param>
    /// <param name="splits">The splits.</param>
    /// <param name="links">The links.</param>
    /// <returns>The journal sequences.</returns>
    public static IReadOnlySet<long> JournalNeeds(
        IEnumerable<LabelHistoryEntry> labels,
        IEnumerable<SplitHistoryEntry> splits,
        IEnumerable<CrossChainLinkHistoryEntry> links)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(splits);
        ArgumentNullException.ThrowIfNull(links);
        var needs = new HashSet<long>();
        foreach (var label in labels)
        {
            if (label.Retraction is { By: null } retraction)
            {
                needs.Add(retraction.JournalSequence);
            }
        }

        foreach (var split in splits)
        {
            needs.Add(split.Split.JournalSequence);
            if (split.Undo is { } undo)
            {
                needs.Add(undo.JournalSequence);
            }
        }

        foreach (var link in links)
        {
            needs.Add(link.Link.JournalSequence);
            if (link.Unlink is { } unlink)
            {
                needs.Add(unlink.JournalSequence);
            }
        }

        return needs;
    }
}

/// <summary>
/// Writes a node's history (sketch AN1b) from what the modules read: the facts and the automatic conclusions without a
/// button, the user's own judgements with <em>Ta tillbaka</em> or <em>Ångra</em>, and every taking back as a row of its
/// own while the row it concerns stays, struck through. Knows nothing of WPF.
/// </summary>
public static class NodeHistory
{
    /// <summary>The button of a label or a link.</summary>
    public const string RetractText = "Ta tillbaka";

    /// <summary>The button of a breaking out.</summary>
    public const string UndoText = "Ångra";

    /// <summary>The button that lets a held back conclusion go (sketch HB1a).</summary>
    public const string ReleaseText = "Släpp slutsatsen";

    /// <summary>The button that lets a held back conclusion stand (sketch HB1a).</summary>
    public const string KeepText = "Låt stå";

    /// <summary>The row of a merge of the user's own, for example an approved wallet.</summary>
    public const string MergeText = "Sammanslagen av användaren";

    /// <summary>The row of a choice that the traced value does not follow through a swap (sketch SW1a).</summary>
    public const string SwapStopText = "Följer inte genom swappen";

    /// <summary>What a choice not to follow a swap says when its transaction no longer reads as a swap.</summary>
    public const string NoSwapText = "ingen swap längre";

    /// <summary>What the mark on a row added on another computer says on hovering.</summary>
    public const string ElsewhereText = "Tillagt på en annan dator";

    /// <summary>Who wrote a journal entry the case could not name.</summary>
    public const string UnknownOperator = "Okänd";

    /// <summary>The months as the rows write them, in Swedish, whatever the computer's language.</summary>
    private static readonly string[] Months =
        ["jan", "feb", "mar", "apr", "maj", "jun", "jul", "aug", "sep", "okt", "nov", "dec"];

    /// <summary>Writes a time as a row does, in local time, for example <c>7 nov 09:12</c>.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The text.</returns>
    public static string Short(DateTimeOffset time)
    {
        var local = time.ToLocalTime();
        return string.Create(
            CultureInfo.InvariantCulture, $"{local.Day} {Months[local.Month - 1]} {local:HH:mm}");
    }

    /// <summary>Says which nodes can have a history: an address, a cluster, or another chain's transaction.</summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> when it can.</returns>
    public static bool HasHistory(NodeView node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Node.Kind is GraphNodeKind.Address or GraphNodeKind.Cluster or GraphNodeKind.OtherChain
            || node.Node.Coin is not null;
    }

    /// <summary>Writes the node's history, oldest first.</summary>
    /// <param name="scene">The scene the node is in.</param>
    /// <param name="node">The node.</param>
    /// <param name="data">What the modules read.</param>
    /// <returns>The rows.</returns>
    public static IReadOnlyList<HistoryEvent> Build(GraphScene scene, NodeView node, NodeHistoryData data)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(data);
        var rows = new List<HistoryEvent>();
        AddFirstReceived(scene, node, rows);
        AddClustered(data, rows);
        AddLabels(data, rows);
        AddSplits(data, rows);
        AddMerges(data, rows);
        AddLinks(scene, node, data, rows);
        AddSwapStops(scene, data, rows);
        AddWatches(node, data, rows);
        AddWithdrawals(data, rows);
        AddHeldBack(scene, node, data, rows);

        // In time order; what happened at the same moment keeps the order above, a taking back after what it concerns.
        return [.. rows.Select((row, index) => (row, index)).OrderBy(pair => pair.row.Utc).ThenBy(pair => pair.index)
            .Select(pair => pair.row)];
    }

    /// <summary>Adds when money first came to the node, with its block: a fact from the chain.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="node">The node.</param>
    /// <param name="rows">The rows.</param>
    private static void AddFirstReceived(GraphScene scene, NodeView node, List<HistoryEvent> rows)
    {
        if (node.Node.FirstReceived is not { } first)
        {
            return;
        }

        var block = scene.Snapshot.Graph.Edges
            .Where(edge => edge.To == node.Key && edge.Kind == GraphEdgeKind.Transfer)
            .SelectMany(edge => edge.Transfers)
            .Select(transfer => transfer.Block)
            .OfType<BlockReference>()
            .OrderBy(reference => reference.Time)
            .FirstOrDefault();
        var sub = block is null
            ? "På kedjan"
            : string.Create(CultureInfo.InvariantCulture, $"Block {block.Height:N0} · på kedjan").Replace(',', ' ');
        rows.Add(new HistoryEvent(
            "first", HistoryKind.FirstReceived, first, "Först in", sub, "Icon.Download", GraphInk.Unknown,
            IsDashed: false, IsFact: true, IsWithdrawn: false, HistoryUndo.None));
    }

    /// <summary>Adds each heuristic that merged the cluster's addresses: an automatic conclusion.</summary>
    /// <param name="data">What the modules read.</param>
    /// <param name="rows">The rows.</param>
    private static void AddClustered(NodeHistoryData data, List<HistoryEvent> rows)
    {
        if (data.Cluster is not { } cluster)
        {
            return;
        }

        var members = cluster.Addresses.ToHashSet();

        // The user's own merges have rows of their own (AddMerges), with who and Ångra.
        foreach (var group in cluster.Links
            .Where(link => link.Evidence.Heuristic.Name != UserMerges.Name)
            .GroupBy(link => link.Evidence.Heuristic))
        {
            var addresses = group.SelectMany(link => link.Evidence.Addresses)
                .Where(members.Contains)
                .Distinct()
                .Count();
            var sub = string.Create(
                CultureInfo.InvariantCulture, $"{InspectorForms.ShortName(group.Key)} · {addresses} adresser");
            rows.Add(new HistoryEvent(
                "clustered/" + group.Key, HistoryKind.Clustered, group.Min(link => link.Evidence.RecordedUtc),
                "Klustrad", sub, "Icon.Kind.Wallet", GraphInk.Cluster, IsDashed: true, IsFact: false,
                IsWithdrawn: false, HistoryUndo.None)
            {
                AddedElsewhere = group.Any(link => data.IsElsewhere(link.Evidence.JournalSequence)),
            });
        }
    }

    /// <summary>
    /// Adds the watches on the node: each record of them, the taking back as a row of its own, and each hit as a fact.
    /// The row that made a watch has <em>Ta tillbaka</em> while the watch is in force.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="data">What the modules read.</param>
    /// <param name="rows">The rows.</param>
    private static void AddWatches(NodeView node, NodeHistoryData data, List<HistoryEvent> rows)
    {
        var addresses = node.Node.Addresses.ToHashSet();
        var mine = data.Watches.Where(record => addresses.Contains(record.Watch.Subject.Anchor)).ToList();
        var removed = mine.Where(record => record.Action == WatchAction.Removed).Select(record => record.Watch.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var record in mine.Where(record => record.Action != WatchAction.Started))
        {
            var watch = record.Watch;
            var key = "watch/" + watch.Id;
            var what = WatchText(watch.Filter);
            switch (record.Action)
            {
                case WatchAction.Removed:
                    rows.Add(Withdrawal(key, record.Utc, "Tillbakatagen: bevakningen", record.By.Name, record.Reason));
                    break;
                case WatchAction.Added:
                    rows.Add(new HistoryEvent(
                        key, HistoryKind.Watch, record.Utc, "Bevakning · " + what, record.By.Name, "Icon.Eye",
                        GraphInk.Cluster, IsDashed: false, IsFact: false, removed.Contains(watch.Id),
                        HistoryUndo.RemoveWatch)
                    {
                        WatchId = watch.Id,
                    });
                    break;
                default:
                    var verb = record.Action switch
                    {
                        WatchAction.Paused => "Bevakning pausad",
                        WatchAction.Resumed => "Bevakning återupptagen",
                        _ => "Bevakning ändrad · " + what,
                    };
                    rows.Add(new HistoryEvent(
                        key + "/" + record.Sequence.ToString(CultureInfo.InvariantCulture), HistoryKind.Watch,
                        record.Utc, verb, record.By.Name, "Icon.Eye", GraphInk.Cluster, IsDashed: false,
                        IsFact: false, IsWithdrawn: false, HistoryUndo.None));
                    break;
            }
        }

        var ids = mine.Select(record => record.Watch.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var hit in data.WatchHits.Where(hit => ids.Contains(hit.WatchId)))
        {
            var match = hit.Match;
            var direction = match.Direction == WatchDirection.Out ? "Ut" : "In";
            var hash = SubjectText.Fit(match.Block.Hash, SubjectText.Shortest + 4);
            var sub = string.Create(CultureInfo.InvariantCulture, $"Block {match.Block.Height:N0} · {hash} · på kedjan")
                .Replace(',', ' ');
            rows.Add(new HistoryEvent(
                "watch-hit/" + hit.Sequence.ToString(CultureInfo.InvariantCulture), HistoryKind.WatchHit,
                match.Block.Time, $"Träff · {direction} {AmountText.WithUnit(match.Amount)}", sub, "Icon.Eye",
                GraphInk.Accent, IsDashed: false, IsFact: true, IsWithdrawn: false, HistoryUndo.None)
            {
                AddedElsewhere = data.IsElsewhere(hit.JournalSequence),
            });
        }
    }

    /// <summary>Writes what a watch looks for in a few words.</summary>
    /// <param name="filter">The filter.</param>
    /// <returns>For example <c>ut · 2 mottagare</c>.</returns>
    private static string WatchText(WatchFilter filter)
    {
        var direction = filter.Direction switch
        {
            WatchDirection.In => "in",
            WatchDirection.Out => "ut",
            _ => "in och ut",
        };
        var parties = new List<string> { direction };
        if (filter.Receivers.Count > 0)
        {
            parties.Add(string.Create(CultureInfo.InvariantCulture, $"{filter.Receivers.Count} mottagare"));
        }

        if (filter.Senders.Count > 0)
        {
            parties.Add(string.Create(CultureInfo.InvariantCulture, $"{filter.Senders.Count} avsändare"));
        }

        return string.Join(" · ", parties);
    }

    /// <summary>Adds every label and every taking back of one.</summary>
    /// <param name="data">What the modules read.</param>
    /// <param name="rows">The rows.</param>
    private static void AddLabels(NodeHistoryData data, List<HistoryEvent> rows)
    {
        foreach (var entry in data.Labels)
        {
            var label = entry.Label;
            var look = VisualLanguage.Of(VisualLanguage.SortOf(label.Kind));
            var what = label.Name ?? label.Kind.Describe();
            var origin = label.Origin;
            var source = origin.Source switch
            {
                LabelSource.OtherCase => "från ett annat ärende",
                LabelSource.SanctionList => origin.SourceName,
                LabelSource.OpenSource => origin.SourceName,
                LabelSource.Behaviour => origin.SourceName,
                LabelSource.Provider => origin.SourceName,
                _ => null,
            };
            var key = "label/" + label.Sequence.ToString(CultureInfo.InvariantCulture);
            rows.Add(new HistoryEvent(
                key, HistoryKind.Label, origin.SetUtc, "Etikett · " + what,
                source is null ? origin.SetBy.Name : $"{origin.SetBy.Name} · {source}", look.Icon, look.Ink,
                IsDashed: false, IsFact: false, entry.IsRetracted,
                entry.CanRetract || entry.IsUsersOwn ? HistoryUndo.RetractLabel : HistoryUndo.None)
            {
                LabelSequence = label.Sequence,
                AddedElsewhere = data.IsElsewhere(label.JournalSequence),
                ReachesLibrary = !label.DoNotSpread && label.Kind != LabelKind.Victim,
            });
            if (entry.Retraction is { } retraction)
            {
                var who = retraction.By?.Name ?? Who(data, retraction.JournalSequence);
                rows.Add(Withdrawal(key, retraction.Utc, "Tillbakatagen: " + what, who, retraction.Reason)
                    with { AddedElsewhere = data.IsElsewhere(retraction.JournalSequence) });
            }
        }
    }

    /// <summary>
    /// Adds every breaking out, the splits stored together in one row, and every undoing of one.
    /// </summary>
    /// <param name="data">What the modules read.</param>
    /// <param name="rows">The rows.</param>
    private static void AddSplits(NodeHistoryData data, List<HistoryEvent> rows)
    {
        foreach (var group in BreakingsOut(data.Splits))
        {
            var first = group[0];
            var key = "split/" + first.Split.Sequence.ToString(CultureInfo.InvariantCulture);
            var undone = group.All(entry => entry.IsUndone);

            // The first address of each split is the one that went out (InspectorForms.BreakOut), so the count is
            // the same from any node and before and after an undoing.
            var count = group.Select(entry => entry.Split.First).Distinct().Count();
            var heuristics = string.Join(", ", group.Select(entry => ShortName(entry.Split.Heuristic)).Distinct());
            rows.Add(new HistoryEvent(
                key, HistoryKind.Split, first.Split.RecordedUtc,
                string.Create(CultureInfo.InvariantCulture, $"Uppbruten · {count} ut"),
                $"{Who(data, first.Split.JournalSequence)} · {heuristics}", "Icon.Split", GraphInk.Cluster,
                IsDashed: false, IsFact: false, undone, HistoryUndo.UndoSplit)
            {
                AddedElsewhere = data.IsElsewhere(first.Split.JournalSequence),
                SplitSequences = [.. group.Where(entry => !entry.IsUndone).Select(entry => entry.Split.Sequence)],
            });
            foreach (var undo in group.Select(entry => entry.Undo).OfType<ClusterSplitUndo>()
                .DistinctBy(undo => undo.JournalSequence))
            {
                rows.Add(Withdrawal(
                    key, undo.RecordedUtc, "Ångrad: uppbrytningen", Who(data, undo.JournalSequence), undo.Reason)
                    with { AddedElsewhere = data.IsElsewhere(undo.JournalSequence) });
            }
        }
    }

    /// <summary>
    /// Adds every merge of the user's own, the merges stored together in one row, and every undoing of one: the
    /// user's judgement, dashed, with <em>Ångra</em> while it holds.
    /// </summary>
    /// <param name="data">What the modules read.</param>
    /// <param name="rows">The rows.</param>
    private static void AddMerges(NodeHistoryData data, List<HistoryEvent> rows)
    {
        foreach (var group in data.Merges.OrderBy(entry => entry.Merge.Sequence)
            .GroupBy(entry => entry.Merge.JournalSequence)
            .Select(group => group.ToList()))
        {
            var first = group[0].Merge;
            var key = "merge/" + first.Sequence.ToString(CultureInfo.InvariantCulture);
            var count = group.SelectMany(entry => entry.Merge.Addresses).Distinct().Count();
            var sub = string.Create(
                CultureInfo.InvariantCulture,
                $"{Who(data, first.JournalSequence)} · {count} adresser · {first.Proposal}");
            rows.Add(new HistoryEvent(
                key, HistoryKind.Merge, first.RecordedUtc, MergeText, sub, "Icon.Kind.Wallet", GraphInk.Cluster,
                IsDashed: true, IsFact: false, group.All(entry => entry.IsUndone), HistoryUndo.UndoMerge)
            {
                AddedElsewhere = data.IsElsewhere(first.JournalSequence),
                MergeSequences = [.. group.Where(entry => !entry.IsUndone).Select(entry => entry.Merge.Sequence)],
            });
            foreach (var undo in group.Select(entry => entry.Undo).OfType<ClusterMergeUndo>()
                .DistinctBy(undo => undo.JournalSequence))
            {
                rows.Add(Withdrawal(
                    key, undo.RecordedUtc, "Ångrad: sammanslagningen", Who(data, undo.JournalSequence), undo.Reason)
                    with { AddedElsewhere = data.IsElsewhere(undo.JournalSequence) });
            }
        }
    }

    /// <summary>
    /// Groups the splits by breaking out: the splits stored under one journal entry are one decision
    /// (<see cref="IClusteringService.Split(Modules.Cases.Contracts.ICase, IReadOnlyList{SplitRequest})"/>).
    /// </summary>
    /// <param name="splits">The splits.</param>
    /// <returns>The groups, oldest first, each in sequence order.</returns>
    public static IReadOnlyList<IReadOnlyList<SplitHistoryEntry>> BreakingsOut(IReadOnlyList<SplitHistoryEntry> splits)
    {
        ArgumentNullException.ThrowIfNull(splits);
        return
        [
            .. splits.OrderBy(entry => entry.Split.Sequence)
                .GroupBy(entry => entry.Split.JournalSequence)
                .Select(group => (IReadOnlyList<SplitHistoryEntry>)[.. group]),
        ];
    }

    /// <summary>Gives a heuristic's short name without its version, as the history writes it.</summary>
    /// <param name="name">The heuristic's name.</param>
    /// <returns>For example <c>CIO</c> or <c>Växel</c>.</returns>
    private static string ShortName(string name)
    {
        var shortName = InspectorForms.ShortName(new HeuristicId(name, 1));
        return shortName[..shortName.LastIndexOf(" v", StringComparison.Ordinal)];
    }

    /// <summary>
    /// Adds every link to another chain from money that came to the node (or to the other chain's transaction it
    /// is), and every taking back of one.
    /// </summary>
    /// <param name="scene">The scene.</param>
    /// <param name="node">The node.</param>
    /// <param name="data">What the modules read.</param>
    /// <param name="rows">The rows.</param>
    private static void AddLinks(GraphScene scene, NodeView node, NodeHistoryData data, List<HistoryEvent> rows)
    {
        var coins = scene.Snapshot.Graph.Edges
            .Where(edge => edge.To == node.Key && edge.Kind == GraphEdgeKind.Transfer)
            .SelectMany(edge => edge.Transfers)
            .Select(transfer => transfer.Coin)
            .ToHashSet();
        if (node.Node.Coin is { } own)
        {
            coins.Add(own);
        }

        var movements = scene.Snapshot.Graph.Edges
            .Where(edge => edge.To == node.Key && edge.Kind == GraphEdgeKind.Transfer)
            .SelectMany(edge => edge.Transfers)
            .Where(transfer => transfer.Movement is not null)
            .Select(InspectorForms.MovementOf)
            .ToHashSet();
        foreach (var entry in data.Links.Where(entry =>
            (entry.Link.From is { } coin && coins.Contains(coin))
            || (entry.Link.FromTransfer is { } movement && movements.Contains(movement))
            || (node.Node.OtherChain is { } other && entry.Link.To == other)))
        {
            var link = entry.Link;
            var chain = GraphScene.ChainShortName(link.To.Chain);
            var key = "link/" + link.Sequence.ToString(CultureInfo.InvariantCulture);
            rows.Add(new HistoryEvent(
                key, HistoryKind.Link, link.RecordedUtc, "Länk till annan kedja · " + chain,
                $"{Who(data, link.JournalSequence)} · tx {GraphScene.Shorten(link.To.Id)}", "Icon.Link",
                GraphInk.OtherChain, IsDashed: true, IsFact: false, entry.IsTakenBack, HistoryUndo.Unlink)
            {
                Link = link,
                AddedElsewhere = data.IsElsewhere(link.JournalSequence),
            });
            if (entry.Unlink is { } unlink)
            {
                rows.Add(Withdrawal(
                    key, unlink.RecordedUtc, "Tillbakatagen: länken till " + chain,
                    Who(data, unlink.JournalSequence), unlink.Reason)
                    with { AddedElsewhere = data.IsElsewhere(unlink.JournalSequence) });
            }
        }
    }

    /// <summary>
    /// Adds every choice that the traced value does not follow through one of the node's swaps (sketch SW1a), with the
    /// swap's pill in words, and every taking back of one; the reading already holds only the node's own.
    /// </summary>
    /// <param name="scene">The scene, for the swap's assets.</param>
    /// <param name="data">What the modules read.</param>
    /// <param name="rows">The rows.</param>
    private static void AddSwapStops(GraphScene scene, NodeHistoryData data, List<HistoryEvent> rows)
    {
        foreach (var entry in data.SwapStops)
        {
            var stop = entry.Stop;
            var pill = " · " + StopPill(scene.Snapshot.Graph.Account, stop);
            var key = "swap-stop/" + stop.Sequence.ToString(CultureInfo.InvariantCulture);
            var who = Who(data, stop.JournalSequence);
            var sub = stop.Reason is null ? who : $"{who} · {stop.Reason}";
            rows.Add(new HistoryEvent(
                key, HistoryKind.SwapStop, stop.RecordedUtc, SwapStopText + pill,
                $"{sub} · tx {GraphScene.Shorten(stop.Transaction.Value)}", "Icon.Kind.Swap", GraphInk.Unknown,
                IsDashed: false, IsFact: false, entry.IsTakenBack, HistoryUndo.FollowSwap)
            {
                SwapStop = stop,
                AddedElsewhere = data.IsElsewhere(stop.JournalSequence),
            });
            if (entry.Undo is { } undo)
            {
                rows.Add(Withdrawal(
                    key, undo.RecordedUtc, "Tillbakataget: värdet följer med igen",
                    Who(data, undo.JournalSequence), undo.Reason)
                    with { AddedElsewhere = data.IsElsewhere(undo.JournalSequence) });
            }
        }
    }

    /// <summary>
    /// Adds the versions drawn back of the analyses in the cluster: the withdrawal with <em>Ta tillbaka</em> while it
    /// is in force, and its taking back as a row of its own (sketches KV1a and AN1b).
    /// </summary>
    /// <param name="data">What the modules read.</param>
    /// <param name="rows">The rows.</param>
    private static void AddWithdrawals(NodeHistoryData data, List<HistoryEvent> rows)
    {
        foreach (var withdrawal in data.Withdrawals)
        {
            var key = "withdrawn/" + withdrawal.Sequence.ToString(CultureInfo.InvariantCulture);
            var who = Who(data, withdrawal.JournalSequence);
            rows.Add(new HistoryEvent(
                key, HistoryKind.VersionWithdrawn, withdrawal.RecordedUtc,
                "Tillbakadragen · " + withdrawal.Heuristic, Because(who, withdrawal.Reason), "Icon.Rollback",
                GraphInk.Cluster, IsDashed: false, IsFact: false, IsWithdrawn: !withdrawal.IsInForce,
                HistoryUndo.TakeBackWithdrawal)
            {
                WithdrawalSequence = withdrawal.Sequence,
                AddedElsewhere = data.IsElsewhere(withdrawal.JournalSequence),
            });
            if (withdrawal.Undo is { } undo)
            {
                rows.Add(Withdrawal(
                    key, undo.RecordedUtc, "Tillbakatagen: " + withdrawal.Heuristic + " räknas igen",
                    Who(data, undo.JournalSequence), undo.Reason)
                    with { AddedElsewhere = data.IsElsewhere(undo.JournalSequence) });
            }
        }
    }

    /// <summary>
    /// Writes what a choice not to follow a swap concerns: the swap's pill, or, when its transaction no longer reads as
    /// a swap (a label set later, or a stricter reading), that there is no swap any more and why; the choice stays and
    /// says so instead of hanging, since the money goes on as a payment and the choice no longer matters.
    /// </summary>
    /// <param name="account">The graph's account part.</param>
    /// <param name="stop">The choice.</param>
    /// <returns>For example <c>₮ → Ξ</c>, or <c>ingen swap längre: motparten har …</c>.</returns>
    private static string StopPill(AccountGraph account, SwapStop stop)
    {
        if (account.Swaps.FirstOrDefault(stop.Is) is { } swap)
        {
            return SwapMarks.Of(swap).Text;
        }

        return account.RefusedSwaps.FirstOrDefault(refusal => stop.Is(refusal.Swap)) is { } refused
            ? $"{NoSwapText}: motparten har {refused.Why}"
            : NoSwapText;
    }

    /// <summary>
    /// Adds the conclusions held back that concern the node (sketch HB1a): one that waits as a dashed card with
    /// <em>Låt stå</em> and <em>Släpp slutsatsen</em>, one let stand with who and when, and one let go struck through
    /// with who and when; a decision can be taken back with <em>Ångra</em> while it is the latest (AN1b).
    /// </summary>
    /// <param name="scene">The scene, for the lines through the node.</param>
    /// <param name="node">The node.</param>
    /// <param name="data">What the modules read.</param>
    /// <param name="rows">The rows.</param>
    private static void AddHeldBack(GraphScene scene, NodeView node, NodeHistoryData data, List<HistoryEvent> rows)
    {
        foreach (var held in data.HeldBack.Where(held => HeldBackMatch.Touches(scene, node, held.Addresses, held.Txid)))
        {
            rows.Add(HeldRow(data, held));
        }

        var concerned = data.HeldBackDecisions
            .Where(decision => HeldBackMatch.Touches(scene, node, decision.Addresses, decision.Txid))
            .ToList();
        var latest = data.HeldBackDecisions.GroupBy(decision => decision.Txid)
            .Select(group => group.MaxBy(decision => decision.Sequence)!.Sequence)
            .ToHashSet();
        foreach (var released in concerned.Where(decision => decision.Choice == ConclusionChoice.Release))
        {
            var key = "held/" + released.Txid.Value + "/" + released.Sequence.ToString(CultureInfo.InvariantCulture);
            rows.Add(new HistoryEvent(
                key, HistoryKind.HeldBack, released.RecordedUtc,
                "Hölls tillbaka · tx " + GraphScene.Shorten(released.Txid.Value),
                "Byggde på " + HeldBackMatch.Basis(released.Txid, released.Missing), "Icon.Held", GraphInk.Spent,
                IsDashed: true, IsFact: false, IsWithdrawn: true, HistoryUndo.None)
            {
                AddedElsewhere = data.IsElsewhere(released.JournalSequence),
            });
            rows.Add(new HistoryEvent(
                key + "/released", HistoryKind.HeldBack, released.RecordedUtc, "Slutsatsen släppt, också dess fynd",
                Because(Who(data, released.JournalSequence), released.Reason), "Icon.Undo", GraphInk.Unknown,
                IsDashed: false, IsFact: false, IsWithdrawn: !latest.Contains(released.Sequence),
                HistoryUndo.ReopenHeldBack)
            {
                DecisionSequence = released.Sequence,
                AddedElsewhere = data.IsElsewhere(released.JournalSequence),
            });
        }

        foreach (var reopened in concerned.Where(decision => decision.Choice == ConclusionChoice.Reopen))
        {
            rows.Add(Withdrawal(
                "held/" + reopened.Txid.Value + "/" + reopened.Undoes?.ToString(CultureInfo.InvariantCulture),
                reopened.RecordedUtc, "Beslutet om slutsatsen taget tillbaka", Who(data, reopened.JournalSequence),
                reopened.Reason) with { AddedElsewhere = data.IsElsewhere(reopened.JournalSequence) });
        }
    }

    /// <summary>
    /// Gives a held back conclusion's card: one that waits with <em>Låt stå</em> and <em>Släpp slutsatsen</em>, and
    /// one let stand with who, which <em>Ångra</em> takes back.
    /// </summary>
    /// <param name="data">What the modules read.</param>
    /// <param name="held">The conclusion.</param>
    /// <returns>The row.</returns>
    private static HistoryEvent HeldRow(NodeHistoryData data, HeldBackConclusion held)
    {
        var heuristics = string.Join(
            ", ",
            held.Evidence.Select(evidence => evidence.Heuristic)
                .Concat(held.Findings.Select(found => found.Heuristic))
                .Select(InspectorForms.ShortName)
                .Distinct());
        var since = held.Decision?.RecordedUtc
            ?? held.Evidence.Select(evidence => evidence.RecordedUtc)
                .Concat(held.Findings.Select(found => found.RecordedUtc))
                .Max();
        var findings = held.Findings.Count > 0 ? "; ett släpp tar också fynden" : string.Empty;
        var sub = held.IsWaiting
            ? "Byggde på " + HeldBackMatch.Basis(held.Txid, held.Missing) + ", som inte fanns i senaste körningen"
                + findings
            : Because("Står kvar · " + Who(data, held.Decision!.JournalSequence), held.Decision.Reason);
        var waiting = held.IsWaiting ? HistoryUndo.DecideHeldBack : HistoryUndo.ReopenHeldBack;
        return new HistoryEvent(
            "held/" + held.Txid.Value, HistoryKind.HeldBack, since, "Hålls tillbaka · " + heuristics, sub,
            "Icon.Held", GraphInk.Spent, IsDashed: true, IsFact: false, IsWithdrawn: false,
            held.Decision is { Choice: ConclusionChoice.Keep } || held.IsWaiting ? waiting : HistoryUndo.None)
        {
            HeldTxid = held.Txid,
            DecisionSequence = held.Decision?.Sequence ?? 0,
            AddedElsewhere = held.Decision is { } decision && data.IsElsewhere(decision.JournalSequence),
        };
    }

    /// <summary>Writes who, and why when there is a reason.</summary>
    /// <param name="who">Who.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <returns>The words.</returns>
    private static string Because(string who, string? reason) => reason is null ? who : $"{who} · {reason}";

    /// <summary>Gives the row of a taking back.</summary>
    /// <param name="of">The key of the row it concerns.</param>
    /// <param name="utc">When.</param>
    /// <param name="text">The row's words.</param>
    /// <param name="who">Who.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <returns>The row.</returns>
    private static HistoryEvent Withdrawal(string of, DateTimeOffset utc, string text, string who, string? reason) =>
        new(
            of + "/withdrawn", HistoryKind.Withdrawal, utc, text, reason is null ? who : $"{who} · {reason}",
            "Icon.Undo", GraphInk.Unknown, IsDashed: false, IsFact: false, IsWithdrawn: false, HistoryUndo.None);

    /// <summary>Gives who wrote a journal entry.</summary>
    /// <param name="data">What the modules read.</param>
    /// <param name="journal">The entry's sequence.</param>
    /// <returns>The operator's name, or <see cref="UnknownOperator"/>.</returns>
    private static string Who(NodeHistoryData data, long journal) =>
        data.Operators.TryGetValue(journal, out var name) ? name : UnknownOperator;
}
