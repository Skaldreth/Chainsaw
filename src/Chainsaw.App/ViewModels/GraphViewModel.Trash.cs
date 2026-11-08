using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The graph's trash (sketch SO1a): a node dragged onto the bin, or chosen and Delete, is thrown; what is reached only
/// through it follows, after a warning by the bin with the tree, and a watch among them is never thrown in silence.
/// Throwing is only a filter: the trash's records are sealed in the case and nothing the graph rests on is touched.
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>What is said when a throw could not be written.</summary>
    public const string ThrowFailedText = "Det kunde inte slängas: ärendet gick inte att skriva.";

    /// <summary>What is said when a putting back could not be written.</summary>
    public const string RestoreFailedText = "Det kunde inte läggas tillbaka: ärendet gick inte att skriva.";

    /// <summary>What the toast says under a case thrown on the start page: the file is untouched.</summary>
    public const string CaseThrownText = "Filen ligger kvar på datorn; ärendet döljs bara på startsidan.";

    /// <summary>The action on the toast after a throw.</summary>
    public const string UndoText = "Ångra · Ctrl+Z";

    /// <summary>The reason a watch ended with when what it watches was thrown.</summary>
    public const string WatchEndedReason = "Slängdes i soptunnan";

    /// <summary>The trash in the case, or none where it is not wired.</summary>
    private ITrashSource? _trashSource;

    /// <summary>What the trash hides now.</summary>
    private TrashFilter _trash = TrashFilter.Empty;

    /// <summary>The latest throw in this session, which Ctrl+Z puts back.</summary>
    private long? _lastThrow;

    /// <summary>Gets the bin in the lower left corner.</summary>
    public TrashBinViewModel Bin { get; } = new();

    /// <summary>Gets the trash's own view.</summary>
    public TrashViewModel TrashView { get; private set; } = null!;

    /// <summary>Gets the cases thrown on the start page, shared with the start page.</summary>
    public CaseTrash CaseTrash { get; private set; } = null!;

    /// <summary>Gets the warning by the bin while a throw waits, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrashWarning))]
    public partial TrashWarningViewModel? TrashWarning { get; private set; }

    /// <summary>Gets whether the warning by the bin is open.</summary>
    public bool HasTrashWarning => TrashWarning is not null;

    /// <summary>Gets whether something in the graph can be thrown now: a case is open and the trash is wired.</summary>
    public bool CanThrow => _trashSource is not null && _session.Current is not null && Scene is not null;

    /// <summary>Connects the trash, the bin and the trash's view.</summary>
    /// <param name="source">The trash in the case, or none.</param>
    /// <param name="cases">The cases thrown on the start page.</param>
    private void UseTrash(ITrashSource? source, CaseTrash cases)
    {
        _trashSource = source;
        CaseTrash = cases;
        TrashView = new TrashViewModel(cases, _clock);
        TrashView.RestoreWanted += (_, wanted) => _ = RestoreAsync(wanted.Throw, wanted.Key);
        TrashView.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TrashViewModel.IsOpen))
            {
                Bin.IsViewOpen = TrashView.IsOpen;
            }
        };
        cases.Changed += (_, _) => ShowBinDot();
        Start.UseCaseTrash(cases);
        Start.TrashWanted += (_, _) => TrashView.Toggle();
        Start.CaseThrown += (_, number) => ToastWanted?.Invoke(this, new ToastRequest(
            ToastKind.Information,
            "Ärendet " + number + " ligger i soptunnan",
            CaseThrownText,
            UndoText,
            () => Start.RestoreCase(number)));
    }

    /// <summary>Reads what the trash hides in the open case, before the graph is shown.</summary>
    /// <param name="openCase">The open case.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>A task that completes when it is read; a failure hides nothing, never something wrong.</returns>
    private async Task ReadTrashAsync(ICase openCase, CancellationToken cancellationToken)
    {
        if (_trashSource is null)
        {
            return;
        }

        try
        {
            _trash = new TrashFilter(await _trashSource.ReadAsync(openCase, cancellationToken));
        }
        catch (Exception exception) when (exception is CaseException or InvalidOperationException)
        {
            _trash = TrashFilter.Empty;
        }
    }

    /// <summary>Shows what the trash holds in the bin's dot and the trash's view.</summary>
    private void ShowTrash()
    {
        TrashView.Show(_trash.Throws, Scene?.Trashed ?? 0, WatchesOfMember);
        ShowBinDot();
    }

    /// <summary>Shows the bin's dot when anything lies in the trash.</summary>
    private void ShowBinDot() => Bin.HasThrows = !_trash.IsEmpty || CaseTrash.Cases.Count > 0;

    /// <summary>Notes where a dragged node is, so that the bin grows as it comes near.</summary>
    /// <param name="distanceToBin">The pointer's distance from the bin's centre, in pixels.</param>
    public void DragNear(double distanceToBin)
    {
        if (CanThrow)
        {
            Bin.DragAt(distanceToBin);
        }
    }

    /// <summary>Ends a drag at the bin: a release on it asks to throw the node.</summary>
    /// <param name="nodeKey">The dragged node.</param>
    /// <returns><see langword="true"/> when the release was on the bin, so that the node is not moved.</returns>
    public bool DropOnBin(string nodeKey)
    {
        if (!Bin.EndDrag())
        {
            return false;
        }

        _moved.Remove((nodeKey, Layout));
        Arrange();
        AskThrow([nodeKey]);
        return true;
    }

    /// <summary>
    /// Throws what is chosen (Delete): the cards chosen in a fanned pile, the actors picked for an area, or the chosen
    /// node.
    /// </summary>
    [RelayCommand]
    public void ThrowChosen()
    {
        if (Fan is { HasSelection: true } fan)
        {
            AskThrowCards(fan);
            return;
        }

        if (Checks.Picked.Count > 0)
        {
            AskThrow([.. Checks.Picked]);
            return;
        }

        if (SelectedNode is { } node)
        {
            AskThrow([node]);
        }
    }

    /// <summary>
    /// Asks to throw nodes: what is reached only through them follows as a tree. A lone node without a watch is thrown
    /// at once, with <em>Ångra</em> on the toast; otherwise the warning by the bin opens.
    /// </summary>
    /// <param name="keys">The drawn nodes.</param>
    public void AskThrow(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (!CanThrow || Scene is not { } scene || HasTrashWarning)
        {
            return;
        }

        var thrown = keys.Where(key => scene.Node(key) is not null).Distinct(StringComparer.Ordinal).ToList();
        if (thrown.Count == 0)
        {
            return;
        }

        var tree = TrashTree.Follow(
            thrown,
            scene.Edges.Select(edge => (edge.Edge.From, edge.Edge.To)),
            scene.Nodes.Where(node => node.IsStartPoint).Select(node => node.Key));
        var members = tree.Select(branch => MemberOf(scene.Node(branch.Key)!, branch.Parent)).ToList();
        Warn(members);
    }

    /// <summary>Asks to throw the cards chosen in a fanned pile, each on its own: a card leads nowhere else.</summary>
    /// <param name="fan">The fanned pile.</param>
    private void AskThrowCards(PileFanViewModel fan)
    {
        var members = fan.Pile.Cards.Where(card => fan.Selected.Contains(card.Key))
            .Select(card => MemberOf(card.Node, null))
            .ToList();
        if (CanThrow && members.Count > 0)
        {
            Warn(members);
        }
    }

    /// <summary>Throws at once when it is one node without a watch; otherwise opens the warning.</summary>
    /// <param name="members">The tree.</param>
    private void Warn(List<TrashMember> members)
    {
        var parents = members.ToDictionary(member => member.Key, member => member.Parent, StringComparer.Ordinal);
        var rows = TrashRowViewModel.Tree(members, member => DepthIn(parents, member.Key), WatchesInGraph);
        var warning = new TrashWarningViewModel(rows);
        if (rows.Count == 1 && !warning.HasWatches)
        {
            _ = ThrowAsync(warning);
            return;
        }

        CloseMenu();
        TrashWarning = warning;
        Redraw?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Throws what the warning shows, after ending the watches the user chose to end (Enter).</summary>
    /// <returns>A task that completes when it is thrown and shown.</returns>
    [RelayCommand]
    public async Task ConfirmThrowAsync()
    {
        if (TrashWarning is not { } warning)
        {
            return;
        }

        TrashWarning = null;
        await ThrowAsync(warning);
    }

    /// <summary>Closes the warning without throwing anything (Esc).</summary>
    [RelayCommand]
    public void CancelThrow()
    {
        TrashWarning = null;
        Redraw?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts the latest throw of this session back (Ctrl+Z), as the toast's <em>Ångra</em> does.</summary>
    /// <returns>A task that completes when it is put back.</returns>
    public Task UndoThrowAsync() => _lastThrow is { } last ? RestoreAsync(last, null) : Task.CompletedTask;

    /// <summary>Puts a member of a throw back with its branch, or the whole throw, and shows the graph with it.</summary>
    /// <param name="throwSequence">The throw.</param>
    /// <param name="key">The member, or <see langword="null"/> for the whole throw.</param>
    /// <returns>A task that completes when it is put back and shown.</returns>
    public async Task RestoreAsync(long throwSequence, string? key)
    {
        if (_trashSource is null || _session.Current is not { } openCase)
        {
            return;
        }

        try
        {
            var back = await _trashSource.RestoreAsync(openCase, throwSequence, key);
            if (key is null && _lastThrow == throwSequence)
            {
                _lastThrow = null;
            }

            await ApplyTrashAsync(openCase);
            if (back.Count > 0)
            {
                Say("Lade tillbaka " + TrashViewModel.CountText(back.Count) + " i grafen.");
            }
        }
        catch (Exception exception) when (exception is CaseException or InvalidOperationException)
        {
            Tell(ToastKind.Error, RestoreFailedText);
        }
    }

    /// <summary>Ends the chosen watches, throws, and shows the graph without what was thrown.</summary>
    /// <param name="warning">What to throw.</param>
    /// <returns>A task that completes when it is thrown and shown.</returns>
    private async Task ThrowAsync(TrashWarningViewModel warning)
    {
        if (_trashSource is null || _session.Current is not { } openCase)
        {
            return;
        }

        try
        {
            var ended = await EndWatchesAsync(openCase, warning.WatchesToEnd);
            var thrown = await _trashSource.ThrowAsync(openCase, warning.Members);
            _lastThrow = thrown.Sequence;
            if (warning.Keys.Contains(SelectedNode ?? string.Empty))
            {
                Choose(null, null);
            }

            Fan?.Close();
            await ApplyTrashAsync(openCase);
            ToastWanted?.Invoke(this, new ToastRequest(
                ToastKind.Information, ThrownText(thrown.Members.Count, ended), string.Empty, UndoText,
                () => _ = RestoreAsync(thrown.Sequence, null)));
        }
        catch (Exception exception) when (exception is CaseException or InvalidOperationException
            or ArgumentException)
        {
            Tell(ToastKind.Error, ThrowFailedText);
            Redraw?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Ends watches before their nodes are thrown, each with its own sealed record in Monitoring.</summary>
    /// <param name="openCase">The open case.</param>
    /// <param name="watchIds">The watches.</param>
    /// <returns>How many ended.</returns>
    private async Task<int> EndWatchesAsync(ICase openCase, IReadOnlyList<string> watchIds)
    {
        if (_watches is not { } board || watchIds.Count == 0)
        {
            return 0;
        }

        var ended = 0;
        foreach (var id in watchIds)
        {
            var done = false;
            await board.WriteAsync(async () => done = await board.Source.RemoveAsync(openCase, id, WatchEndedReason));
            ended += done ? 1 : 0;
        }

        return ended;
    }

    /// <summary>Reads the trash again and shows the graph through it.</summary>
    /// <param name="openCase">The open case.</param>
    /// <returns>A task that completes when it is shown.</returns>
    private async Task ApplyTrashAsync(ICase openCase)
    {
        await ReadTrashAsync(openCase, CancellationToken.None);
        if (Scene is { } scene)
        {
            Show(scene, fit: false, keepNotice: true);
        }
        else
        {
            ShowTrash();
        }
    }

    /// <summary>Writes what a throw's toast says: <c>Slängde 5 aktörer</c>, and how many watches ended.</summary>
    /// <param name="count">How many were thrown.</param>
    /// <param name="ended">How many watches ended.</param>
    /// <returns>The text.</returns>
    public static string ThrownText(int count, int ended)
    {
        var text = "Slängde " + TrashViewModel.CountText(count);
        return ended switch
        {
            0 => text,
            1 => text + " · bevakningen avslutad",
            _ => text + string.Create(CultureInfo.InvariantCulture, $" · {ended} bevakningar avslutade"),
        };
    }

    /// <summary>Makes a member of a drawn node: its key, its name and sort, and the keys it is known by.</summary>
    /// <param name="node">The drawn node.</param>
    /// <param name="parent">The member it follows with, or <see langword="null"/>.</param>
    /// <returns>The member.</returns>
    private static TrashMember MemberOf(NodeView node, string? parent) =>
        new(node.Key, parent, node.Name, node.Look.Sort.ToString()) { Keys = TrashFilter.KeysOf(node.Node) };

    /// <summary>Gives how deep a member hangs in a tree.</summary>
    /// <param name="parents">Each member's parent.</param>
    /// <param name="key">The member.</param>
    /// <returns>The depth: 0 for what is thrown.</returns>
    private static int DepthIn(Dictionary<string, string?> parents, string key)
    {
        var depth = 0;
        for (var parent = parents[key]; parent is not null; parent = parents[parent])
        {
            depth++;
        }

        return depth;
    }

    /// <summary>
    /// Gives the active and paused watches on a node in the graph: on its addresses, or for a pile on its cards'.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <returns>The watches' ids.</returns>
    private IReadOnlyList<string> WatchesInGraph(TrashMember member)
    {
        var addresses = Scene?.Pile(member.Key) is { } pile
            ? pile.Cards.SelectMany(card => card.Node.Node.Addresses).ToHashSet()
            : (Scene?.Node(member.Key)?.Node.Addresses ?? []).ToHashSet();
        return LiveWatches().Where(watch => addresses.Contains(watch.Subject.Anchor)).Select(watch => watch.Id)
            .ToList();
    }

    /// <summary>Gives the live watches on a member in the trash, by the keys it is known by.</summary>
    /// <param name="member">The member.</param>
    /// <returns>The watches' ids.</returns>
    private IReadOnlyList<string> WatchesOfMember(TrashMember member)
    {
        var keys = member.FoundBy.Append(member.Key).ToHashSet(StringComparer.Ordinal);
        return LiveWatches()
            .Where(watch => keys.Contains(GraphPile.AddressKeyPrefix + watch.Subject.Anchor.Value)
                || keys.Contains(
                    GraphPile.AccountKeyPrefix + watch.Subject.Anchor.Chain + ":" + watch.Subject.Anchor.Value))
            .Select(watch => watch.Id)
            .ToList();
    }

    /// <summary>Gives the watches that are checked or paused, not those taken back.</summary>
    /// <returns>The watches.</returns>
    private IEnumerable<Watch> LiveWatches() =>
        (_watches?.Snapshot.Watches ?? []).Where(watch => watch.State != WatchState.Removed);
}
