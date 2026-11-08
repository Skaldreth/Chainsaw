using System.Collections.ObjectModel;
using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>A row in the inspector's <em>Historik</em> (sketch AN1b), and whether its form is folded out.</summary>
/// <param name="event">What happened.</param>
public sealed partial class HistoryRowViewModel(HistoryEvent @event) : ObservableObject
{
    /// <summary>Gets what happened.</summary>
    public HistoryEvent Event { get; } = @event;

    /// <summary>Gets whether the row's form (the optional reason, and for a split what merges again) is open.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>Gets whether the row is drawn after another, so that the time line runs up to it.</summary>
    public bool IsLast { get; init; }
}

/// <summary>
/// An address that undoing a breaking out would merge into the cluster again, with the link that does it.
/// </summary>
/// <param name="ShortAddress">The address, shortened for the row.</param>
/// <param name="Heuristic">The heuristic's short name, for example <c>CIO v1</c>.</param>
/// <param name="ToolTip">The address in full, the heuristic and the transaction.</param>
public sealed record RejoinRow(string ShortAddress, string Heuristic, string ToolTip);

/// <summary>
/// The inspector's tabs <em>Fakta</em> and <em>Historik</em> and the history itself (sketch AN1b): the chosen node's
/// events in time order, read off the UI thread from Attribution, Clustering and Tracing. The user's own label, breaking
/// out and link to another chain have <em>Ta tillbaka</em> or <em>Ångra</em>, which folds out an optional reason;
/// Enter stores and Esc cancels. Undoing a breaking out first shows which addresses merge again and by which heuristic.
/// What is stored is a new sealed record; the row stays, struck through. Knows nothing of WPF.
/// </summary>
public sealed partial class InspectorHistoryViewModel : ObservableObject
{
    /// <summary>What the tab says while the history is read.</summary>
    public const string ReadingText = "Läser historiken …";

    /// <summary>What the tab says when the history could not be read.</summary>
    public const string ReadFailedText = "Historiken kunde inte läsas ur ärendet.";

    /// <summary>What the tab says when the node has no history to show.</summary>
    public const string EmptyText = "Inget har hänt med den här noden än.";

    /// <summary>What the confirmation says while it reads what merges again.</summary>
    public const string ReadingRejoinText = "Läser vad som slås ihop igen …";

    /// <summary>What the confirmation says when it could not read what merges again.</summary>
    public const string RejoinFailedText = "Det gick inte att läsa vad som slås ihop igen; försök igen.";

    /// <summary>What the confirmation says when nothing merges again.</summary>
    public const string NothingRejoinsText =
        "Inget slås ihop igen just nu: ingen heuristik binder dem, eller en annan uppbrytning håller dem isär.";

    /// <summary>
    /// The most addresses the confirmation lists; the rest are counted, so that a large cluster does not fill the
    /// inspector (principle 7).
    /// </summary>
    public const int MostRejoinRows = 12;

    /// <summary>What the form says when the row was already taken back, for example in another window.</summary>
    public const string AlreadyText = "Det var redan tillbakataget; historiken läses om.";

    /// <summary>The open case.</summary>
    private readonly CaseSession _session;

    /// <summary>The modules' readers and writers.</summary>
    private readonly IGraphEditor _editor;

    /// <summary>The scene the chosen node is in.</summary>
    private GraphScene? _scene;

    /// <summary>The chosen node, or <see langword="null"/>.</summary>
    private NodeView? _node;

    /// <summary>The chosen comment, or <see langword="null"/>; never at the same time as a node.</summary>
    private GraphComment? _comment;

    /// <summary>Counts the readings of the history, so that only the latest is shown.</summary>
    private int _reads;

    /// <summary>Counts the forms opened, so that an answer for an earlier one is dropped.</summary>
    private int _opened;

    /// <summary>
    /// Counts the readings of a confirmation, so that only the latest is shown; separate from the form's number, so
    /// that a confirmation read again after a refresh never mistakes a store under way for another form's.
    /// </summary>
    private int _previews;

    /// <summary>Whether the confirmation has read what merges again, so that a breaking out can be undone.</summary>
    private bool _previewRead;

    /// <summary>Creates the history.</summary>
    /// <param name="session">The open case.</param>
    /// <param name="editor">The modules' readers and writers.</param>
    public InspectorHistoryViewModel(CaseSession session, IGraphEditor editor)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
    }

    /// <summary>
    /// Raised when something was taken back or undone, so that the graph is read again from the case, with what the
    /// graph's notice should say once it is read (that the label library could not follow), or <see langword="null"/>.
    /// </summary>
    public event EventHandler<string?>? Saved;

    /// <summary>Raised with what went wrong when a store failed after its form was closed or replaced.</summary>
    public event EventHandler<string>? Failed;

    /// <summary>Gets whether the history tab is shown instead of the facts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFactsShown))]
    public partial bool IsShown { get; private set; }

    /// <summary>Gets whether the facts tab is shown.</summary>
    public bool IsFactsShown => !IsShown;

    /// <summary>Gets whether the chosen node or comment has a history, so that the tabs show.</summary>
    [ObservableProperty]
    public partial bool HasNode { get; private set; }

    /// <summary>
    /// Gets or sets where a chosen comment's history is read (sketch KN1a); none when <see langword="null"/>, and a
    /// comment then has no tabs.
    /// </summary>
    public ICommentHistorySource? Comments { get; set; }

    /// <summary>Gets the rows, oldest first.</summary>
    public ObservableCollection<HistoryRowViewModel> Rows { get; } = [];

    /// <summary>Gets the number on the tab: how many rows there are, or empty before they are read.</summary>
    [ObservableProperty]
    public partial string CountText { get; private set; } = string.Empty;

    /// <summary>Gets what the tab says while reading, after a failure or when empty; empty otherwise.</summary>
    [ObservableProperty]
    public partial string Notice { get; private set; } = string.Empty;

    /// <summary>Gets the row whose form is open, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen), nameof(IsLabelOpen), nameof(IsSplitOpen), nameof(IsLinkOpen))]
    [NotifyPropertyChangedFor(nameof(IsMergeOpen), nameof(IsConfirmOpen))]
    [NotifyPropertyChangedFor(nameof(SubmitText), nameof(CanSubmit), nameof(ReachesLibrary))]
    public partial HistoryRowViewModel? OpenRow { get; private set; }

    /// <summary>Gets whether a row's form is open, so that Esc closes it first.</summary>
    public bool IsOpen => OpenRow is not null;

    /// <summary>Gets whether the open form takes a label back.</summary>
    public bool IsLabelOpen => OpenRow?.Event.Undo == HistoryUndo.RetractLabel;

    /// <summary>Gets whether the open form undoes a breaking out, with its confirmation.</summary>
    public bool IsSplitOpen => OpenRow?.Event.Undo == HistoryUndo.UndoSplit;

    /// <summary>Gets whether the open form undoes a merge of the user's own, with its confirmation.</summary>
    public bool IsMergeOpen => OpenRow?.Event.Undo == HistoryUndo.UndoMerge;

    /// <summary>
    /// Gets whether the open form shows what merges again before it is stored: undoing a breaking out, or undoing a
    /// merge of the user's that held a heuristic's link back (AN1b, the user's decision 2026-11-07).
    /// </summary>
    public bool IsConfirmOpen => IsSplitOpen || IsMergeOpen;

    /// <summary>Gets whether the open form takes a link to another chain back.</summary>
    public bool IsLinkOpen => OpenRow?.Event.Undo == HistoryUndo.Unlink;

    /// <summary>
    /// Gets whether the label in the open form may have reached the label library, which then learns it too.
    /// </summary>
    public bool ReachesLibrary => IsLabelOpen && OpenRow!.Event.ReachesLibrary;

    /// <summary>Gets or sets the reason; it is optional (the user's decision 2026-11-07).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial string Reason { get; set; } = string.Empty;

    /// <summary>Gets the addresses that undoing the breaking out merges again (the confirmation).</summary>
    public ObservableCollection<RejoinRow> Rejoined { get; } = [];

    /// <summary>Gets what the confirmation says while it reads, when it failed or when nothing merges again.</summary>
    [ObservableProperty]
    public partial string RejoinNotice { get; private set; } = string.Empty;

    /// <summary>Gets how many more addresses merge again than are listed (<c>+40 till</c>), or empty.</summary>
    [ObservableProperty]
    public partial string RejoinMore { get; private set; } = string.Empty;

    /// <summary>Gets the heuristics that merge again, as the confirmation's heading writes them.</summary>
    [ObservableProperty]
    public partial string RejoinHeuristics { get; private set; } = string.Empty;

    /// <summary>Gets whether something is being stored.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial bool IsSaving { get; private set; }

    /// <summary>Gets what went wrong, in Swedish; empty when nothing did.</summary>
    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    /// <summary>Gets the store button's words: <em>Ta tillbaka</em> or <em>Ångra</em>.</summary>
    public string SubmitText => OpenRow?.Event.UndoText ?? string.Empty;

    /// <summary>
    /// Gets whether the open form can be stored: a reason within its length, and for a breaking out a confirmation
    /// that has been read.
    /// </summary>
    public bool CanSubmit => OpenRow is { Event.CanUndo: true } && !IsSaving && _session.Current is not null
        && Reason.Trim().Length <= InspectorForms.MostReasonLength
        && (!IsConfirmOpen || _previewRead);

    /// <summary>Shows the facts (the tab <em>Fakta</em>), closing an open form.</summary>
    [RelayCommand]
    public void ShowFacts()
    {
        Close();
        IsShown = false;
    }

    /// <summary>Shows the history (the tab <em>Historik</em>).</summary>
    [RelayCommand]
    public void ShowHistory()
    {
        if (HasNode)
        {
            IsShown = true;
        }
    }

    /// <summary>
    /// Follows what the inspector shows: a new node closes the open form and reads its history; the same node read
    /// again (after the graph changed) reads its history again and keeps the open form while its row can still be
    /// undone.
    /// </summary>
    /// <param name="scene">The scene, or <see langword="null"/>.</param>
    /// <param name="node">The chosen node, or <see langword="null"/>.</param>
    public void Follow(GraphScene? scene, NodeView? node)
    {
        var same = node is not null && _comment is null && _node?.Key == node.Key;
        (_scene, _node, _comment) = (scene, node, null);
        HasNode = node is not null && NodeHistory.HasHistory(node);
        if (!same)
        {
            Close();
            Rows.Clear();
            CountText = string.Empty;
            Notice = string.Empty;
        }

        if (!HasNode)
        {
            IsShown = false;
            _reads++;
            return;
        }

        _ = ReadAsync();
    }

    /// <summary>
    /// Follows a chosen comment (sketch KN1a): its history is read, its sealed records and its throws and puttings back,
    /// in place of a node's. The same comment read again (after it was written in) keeps the tab shown.
    /// </summary>
    /// <param name="scene">The scene, whose nodes name the comment's threads, or <see langword="null"/>.</param>
    /// <param name="comment">The chosen comment, or <see langword="null"/>.</param>
    public void FollowComment(GraphScene? scene, GraphComment? comment)
    {
        var same = comment is not null && _node is null && _comment?.Id == comment.Id;
        (_scene, _node, _comment) = (scene, null, comment);
        HasNode = comment is not null && Comments is not null;
        if (!same)
        {
            Close();
            Rows.Clear();
            CountText = string.Empty;
            Notice = string.Empty;
        }

        if (!HasNode)
        {
            IsShown = false;
            _reads++;
            return;
        }

        _ = ReadCommentAsync();
    }

    /// <summary>Opens a row's form, or closes it when it is already open.</summary>
    /// <param name="row">The row.</param>
    [RelayCommand]
    public void Toggle(HistoryRowViewModel? row)
    {
        if (row is null || !row.Event.CanUndo || IsSaving)
        {
            return;
        }

        if (row == OpenRow)
        {
            Close();
            return;
        }

        Close();
        _opened++;
        row.IsOpen = true;
        OpenRow = row;
        if (IsConfirmOpen)
        {
            _ = PreviewAsync(row.Event);
        }
    }

    /// <summary>Closes the open form without storing anything (Esc).</summary>
    [RelayCommand]
    public void Cancel() => Close();

    /// <summary>
    /// Takes back or undoes what the open row says (Enter), through the module that owns it, off the UI thread; then
    /// closes the form and asks for the graph to be read again. What the case refuses is said in the form.
    /// </summary>
    /// <returns>A task that completes when it is stored or could not be.</returns>
    [RelayCommand]
    public async Task SubmitAsync()
    {
        if (!CanSubmit || _session.Current is not { } openCase || OpenRow is not { } row)
        {
            return;
        }

        var opened = _opened;
        var reason = Reason.Trim().Length == 0 ? null : Reason.Trim();
        IsSaving = true;
        Error = string.Empty;
        try
        {
            if (row.Event.Undo == HistoryUndo.RemoveWatch)
            {
                await RemoveWatchAsync(openCase, row.Event.WatchId!, reason, opened);
                return;
            }

            var outcome = row.Event.Undo switch
            {
                HistoryUndo.RetractLabel => await _editor.RetractLabelAsync(openCase, row.Event.LabelSequence, reason),
                HistoryUndo.UndoSplit => await _editor.UndoSplitsAsync(openCase, row.Event.SplitSequences, reason),
                HistoryUndo.UndoMerge => await _editor.UndoMergesAsync(openCase, row.Event.MergeSequences, reason),
                HistoryUndo.FollowSwap => await _editor.FollowSwapAsync(openCase, row.Event.SwapStop!, reason),
                _ => await _editor.UnlinkAsync(openCase, row.Event.Link!, reason),
            };
            if (!outcome.Done)
            {
                Say(AlreadyText, opened);
            }
            else if (opened == _opened)
            {
                Close();
            }

            Saved?.Invoke(this, outcome.LibraryProblem);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Say(InspectorFormsViewModel.Failure(exception), opened);
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Gets or sets the watches, whose records and hits join the history (sketch BV1a); none when
    /// <see langword="null"/>.
    /// </summary>
    public WatchBoard? Watches { get; set; }

    /// <summary>Takes a watch back through the Monitoring module, as a new sealed record.</summary>
    /// <param name="openCase">The open case.</param>
    /// <param name="watchId">The watch.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <param name="opened">The form's number when the store began.</param>
    /// <returns>A task that completes when it is stored or could not be.</returns>
    private async Task RemoveWatchAsync(
        Modules.Cases.Contracts.ICase openCase, string watchId, string? reason, int opened)
    {
        if (Watches is not { } watches)
        {
            return;
        }

        var done = false;
        var written = await watches.WriteAsync(async () =>
            done = await watches.Source.RemoveAsync(openCase, watchId, reason));
        if (!written)
        {
            Say(WatchBoard.FailedText, opened);
            return;
        }

        if (!done)
        {
            Say(AlreadyText, opened);
        }
        else if (opened == _opened)
        {
            Close();
        }

        Saved?.Invoke(this, null);
    }

    /// <summary>Says what went wrong: in the form while it is the same one, otherwise in the graph's notice.</summary>
    /// <param name="text">What to say.</param>
    /// <param name="opened">The form's number when the store began.</param>
    private void Say(string text, int opened)
    {
        if (opened == _opened)
        {
            Error = text;
        }
        else
        {
            Failed?.Invoke(this, text);
        }
    }

    /// <summary>Closes the open form and forgets the reason and the confirmation.</summary>
    private void Close()
    {
        _opened++;
        _previews++;
        if (OpenRow is { } row)
        {
            row.IsOpen = false;
        }

        OpenRow = null;
        Reason = string.Empty;
        Rejoined.Clear();
        RejoinNotice = string.Empty;
        RejoinHeuristics = string.Empty;
        RejoinMore = string.Empty;
        _previewRead = false;
        Error = string.Empty;
    }

    /// <summary>
    /// Reads the chosen node's history off the UI thread and shows it, if it is still the latest reading.
    /// </summary>
    /// <returns>A task that completes when it is shown or could not be read.</returns>
    private async Task ReadAsync()
    {
        if (_session.Current is not { } openCase || _scene is not { } scene || _node is not { } node)
        {
            return;
        }

        var read = ++_reads;
        if (Rows.Count == 0)
        {
            Notice = ReadingText;
        }

        try
        {
            var data = await _editor.ReadHistoryAsync(openCase, node.Node, CancellationToken.None);
            if (Watches is { } watches)
            {
                var snapshot = watches.Snapshot;
                data = data with { Watches = snapshot.Records, WatchHits = snapshot.Hits };
            }

            if (read != _reads)
            {
                return;
            }

            Show(NodeHistory.Build(scene, node, data));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (read == _reads)
            {
                Notice = ReadFailedText;
            }
        }
    }

    /// <summary>Reads the chosen comment's history off the UI thread and shows it, if it is still the latest reading.</summary>
    /// <returns>A task that completes when it is shown or could not be read.</returns>
    private async Task ReadCommentAsync()
    {
        if (_session.Current is not { } openCase || _comment is not { } comment || Comments is not { } source)
        {
            return;
        }

        var read = ++_reads;
        if (Rows.Count == 0)
        {
            Notice = ReadingText;
        }

        var scene = _scene;
        try
        {
            var data = await source.ReadAsync(openCase, comment.Id, CancellationToken.None);
            if (read == _reads)
            {
                Show(CommentHistory.Build(data, anchor => ThreadName(scene, anchor)));
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (read == _reads)
            {
                Notice = ReadFailedText;
            }
        }
    }

    /// <summary>Gives the name of the node a thread's key goes to in the graph shown.</summary>
    /// <param name="scene">The graph, or <see langword="null"/>.</param>
    /// <param name="anchor">The thread's key.</param>
    /// <returns>The node's name, or <see langword="null"/> when it is not shown.</returns>
    private static string? ThreadName(GraphScene? scene, string anchor) =>
        scene?.Node(CommentGeometry.Resolve(scene, anchor))?.Name;

    /// <summary>Shows the rows, keeping the open form when its row is still there and can be undone.</summary>
    /// <param name="events">The rows, oldest first.</param>
    private void Show(IReadOnlyList<HistoryEvent> events)
    {
        var openKey = OpenRow?.Event.Key;
        var reopen = events.FirstOrDefault(found => found.Key == openKey && found.CanUndo);
        if (openKey is not null && reopen is null)
        {
            Close();
        }

        Rows.Clear();
        for (var index = 0; index < events.Count; index++)
        {
            var row = new HistoryRowViewModel(events[index]) { IsLast = index == events.Count - 1 };
            if (OpenRow is not null && events[index].Key == openKey)
            {
                row.IsOpen = true;
                OpenRow = row;
            }

            Rows.Add(row);
        }

        CountText = events.Count.ToString(CultureInfo.InvariantCulture);
        Notice = events.Count == 0 ? EmptyText : string.Empty;

        // A breaking out read again may have other splits in force: its confirmation is read again, the reason kept.
        if (OpenRow is { Event.Undo: HistoryUndo.UndoSplit or HistoryUndo.UndoMerge } open)
        {
            _previewRead = false;
            OnPropertyChanged(nameof(CanSubmit));
            _ = PreviewAsync(open.Event);
        }
    }

    /// <summary>Reads what undoing a breaking out merges again, for the confirmation.</summary>
    /// <param name="event">The breaking out.</param>
    /// <returns>A task that completes when it is shown or could not be read.</returns>
    private async Task PreviewAsync(HistoryEvent @event)
    {
        if (_session.Current is not { } openCase)
        {
            return;
        }

        // What an earlier reading said is cleared, so that it is never shown as this one's answer.
        var opened = ++_previews;
        Rejoined.Clear();
        RejoinMore = string.Empty;
        RejoinHeuristics = string.Empty;
        RejoinNotice = ReadingRejoinText;
        try
        {
            var preview = await ReadPreviewAsync(openCase, @event);
            if (opened != _previews)
            {
                return;
            }

            ShowPreview(preview);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (opened == _previews)
            {
                RejoinNotice = RejoinFailedText;
            }
        }
    }

    /// <summary>Reads what undoing a row merges again, through the module that owns it.</summary>
    /// <param name="openCase">The open case.</param>
    /// <param name="event">A breaking out, or a merge of the user's.</param>
    /// <returns>The addresses and heuristics, or <see langword="null"/> when nothing of it holds now.</returns>
    private async Task<Rejoining?> ReadPreviewAsync(ICase openCase, HistoryEvent @event)
    {
        if (@event.Undo == HistoryUndo.UndoMerge)
        {
            var merges = await _editor.PreviewUndoMergesAsync(openCase, @event.MergeSequences, CancellationToken.None);
            return merges is { IsInForce: true } ? new Rejoining(merges.Rejoined, merges.Heuristics) : null;
        }

        var from = _node?.Node.GraphAddresses.FirstOrDefault();
        var splits = await _editor.PreviewUndoSplitsAsync(
            openCase, @event.SplitSequences, from, CancellationToken.None);
        return splits is { IsInForce: true } ? new Rejoining(splits.Rejoined, splits.Heuristics) : null;
    }

    /// <summary>Shows the confirmation: each address that merges again with its heuristic.</summary>
    /// <param name="preview">What undoing merges again, or <see langword="null"/> when nothing of it holds now.</param>
    private void ShowPreview(Rejoining? preview)
    {
        Rejoined.Clear();
        RejoinMore = string.Empty;
        if (preview is null)
        {
            RejoinNotice = AlreadyText;
            return;
        }

        foreach (var member in preview.Rejoined.Take(MostRejoinRows))
        {
            Rejoined.Add(RowOf(member));
        }

        var more = preview.Rejoined.Count - MostRejoinRows;
        RejoinMore = more > 0 ? string.Create(CultureInfo.InvariantCulture, $"+{more} till") : string.Empty;

        RejoinHeuristics = string.Join(", ", preview.Heuristics.Select(InspectorForms.ShortName));
        RejoinNotice = preview.Rejoined.Count == 0 ? NothingRejoinsText : string.Empty;
        _previewRead = true;
        OnPropertyChanged(nameof(CanSubmit));
    }

    /// <summary>Gives the confirmation's row for an address that merges again.</summary>
    /// <param name="member">The address and the link that brings it in.</param>
    /// <returns>The row.</returns>
    private static RejoinRow RowOf(ClusterMember member)
    {
        var shortAddress = SubjectText.Fit(member.Address.Value, 22);
        if (member.JoinedBy?.Evidence is not { } evidence)
        {
            return new RejoinRow(shortAddress, string.Empty, member.Address.Value);
        }

        var tip = $"{member.Address.Value} · {evidence.Heuristic} i tx {GraphScene.Shorten(evidence.Txid.Value)}";
        return new RejoinRow(shortAddress, InspectorForms.ShortName(evidence.Heuristic), tip);
    }
}

/// <summary>What undoing a row merges again, for the confirmation (sketch AN1b).</summary>
/// <param name="Rejoined">The addresses that merge again, each with the link that brings it in.</param>
/// <param name="Heuristics">The heuristics that merge them again.</param>
internal sealed record Rejoining(IReadOnlyList<ClusterMember> Rejoined, IReadOnlyList<HeuristicId> Heuristics);
