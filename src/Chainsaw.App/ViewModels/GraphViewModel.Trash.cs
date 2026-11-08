using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The graph's trash (sketch SO1a): a node dragged onto the bin, or chosen and Delete, is thrown; what is reached only
/// through it follows, after a warning by the bin with the tree, and a watch among them is never thrown in silence.
/// Throwing is only a filter: the trash's records are sealed in the case and nothing the graph rests on is touched.
/// </summary>
/// <remarks>
/// Every throw and putting back belongs to the case that was open when it began: a throw number means nothing in
/// another case, so nothing is shown, remembered or put back once another case is open.
/// </remarks>
public sealed partial class GraphViewModel
{
    /// <summary>What is said when a throw could not be written.</summary>
    public const string ThrowFailedText = "Det kunde inte slängas: ärendet gick inte att skriva.";

    /// <summary>What is said when what was to be thrown already lies in the trash.</summary>
    public const string AlreadyThrownText = "Det ligger redan i soptunnan.";

    /// <summary>What is said when a putting back could not be written.</summary>
    public const string RestoreFailedText = "Det kunde inte läggas tillbaka: ärendet gick inte att skriva.";

    /// <summary>What is said when Ångra belongs to a case that is no longer open.</summary>
    public const string UndoOtherCaseText = "Ångra gäller ett ärende som inte är öppet längre.";

    /// <summary>What the toast says under a throw that ended watches: Ångra does not bring them back.</summary>
    public const string WatchesStayEndedText = "Ångra lägger tillbaka noderna men inte bevakningarna.";

    /// <summary>What is said when a watch the user chose to end could not be ended after the throw.</summary>
    public const string WatchesNotEndedText =
        "Det slängda ligger i soptunnan, men bevakningen kunde inte avslutas. Avsluta den i vyn Bevakning.";

    /// <summary>What the toast says under a case thrown on the start page: the file is untouched.</summary>
    public const string CaseThrownText = "Filen ligger kvar på datorn; ärendet döljs bara på startsidan.";

    /// <summary>What is said when the cases thrown on the start page could not be saved on this computer.</summary>
    public const string CaseTrashSaveFailedText =
        "Soptunnan på startsidan kunde inte sparas på datorn; den gäller tills Chainsaw stängs.";

    /// <summary>What the graph says when everything in it lies in the trash.</summary>
    public const string AllInTrashText =
        "Allt i grafen ligger i soptunnan. Klicka på soptunnan nere till vänster för att lägga tillbaka.";

    /// <summary>What the warning says when a start point lies in the trash.</summary>
    public const string StartPointThrownText = "Startpunkten ligger i soptunnan";

    /// <summary>The action that puts a thrown start point back.</summary>
    public const string RestoreActionText = "Lägg tillbaka";

    /// <summary>The action on the toast after a throw.</summary>
    public const string UndoText = "Ångra · Ctrl+Z";

    /// <summary>The reason a watch ended with when what it watches was thrown.</summary>
    public const string WatchEndedReason = "Slängdes i soptunnan";

    /// <summary>The trash in the case, or none where it is not wired.</summary>
    private ITrashSource? _trashSource;

    /// <summary>What the trash hides now, in the open case.</summary>
    private TrashFilter _trash = TrashFilter.Empty;

    /// <summary>The latest throw in this session and the case it was made in, which Ctrl+Z puts back.</summary>
    private (ICase Case, long Sequence)? _lastThrow;

    /// <summary>Counts the readings of the trash, so that only the latest is kept.</summary>
    private int _trashReads;

    /// <summary>Whether a throw is being written, so that a second Delete does not throw the same again.</summary>
    private bool _throwing;

    /// <summary>The thrown start points last told, so that the same are not told at every reading.</summary>
    private string _toldThrownStarts = string.Empty;

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

    /// <summary>
    /// Gets whether something in the graph can be thrown now: a case is open, the trash is wired and no throw is being
    /// written.
    /// </summary>
    public bool CanThrow => _trashSource is not null && _session.Current is not null && Scene is not null && !_throwing;

    /// <summary>Gets whether Delete has something to throw: a chosen node, picked actors or chosen cards.</summary>
    public bool HasChosenToThrow =>
        Fan is { HasSelection: true } || Checks.Picked.Count > 0 || SelectedNode is not null;

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
        cases.SaveFailed += (_, _) => Tell(ToastKind.Warning, CaseTrashSaveFailedText);
        Start.UseCaseTrash(cases);
        Start.TrashWanted += (_, _) => TrashView.Toggle();
        Start.CaseThrown += (_, number) => ToastWanted?.Invoke(this, new ToastRequest(
            ToastKind.Information,
            "Ärendet " + number + " ligger i soptunnan",
            CaseThrownText,
            UndoText,
            () => Start.RestoreCase(number)));
    }

    /// <summary>Says whether a case is still the open one.</summary>
    /// <param name="openCase">The case.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private bool IsStillOpen(ICase openCase) => ReferenceEquals(_session.Current, openCase);

    /// <summary>Starts a reading of the trash, which makes every earlier reading's answer stale.</summary>
    /// <returns>The reading's number.</returns>
    private int BeginTrashRead() => ++_trashReads;

    /// <summary>Reads what the trash hides in a case; it is kept only by <see cref="KeepTrash"/>.</summary>
    /// <param name="openCase">The case.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>What it hides; a failure hides nothing, never something wrong.</returns>
    private async Task<TrashFilter> ReadTrashAsync(ICase openCase, CancellationToken cancellationToken)
    {
        if (_trashSource is null)
        {
            return TrashFilter.Empty;
        }

        try
        {
            return new TrashFilter(await _trashSource.ReadAsync(openCase, cancellationToken));
        }
        catch (Exception exception) when (exception is CaseException or InvalidOperationException)
        {
            return TrashFilter.Empty;
        }
    }

    /// <summary>
    /// Keeps a reading of the trash when it is the latest and its case is still open, so that an older answer never
    /// replaces a newer one or another case's.
    /// </summary>
    /// <param name="read">The reading's number.</param>
    /// <param name="openCase">The case it was read from.</param>
    /// <param name="trash">What it read.</param>
    /// <returns><see langword="true"/> when it was kept.</returns>
    private bool KeepTrash(int read, ICase openCase, TrashFilter trash)
    {
        if (read != _trashReads || !IsStillOpen(openCase))
        {
            return false;
        }

        _trash = trash;
        return true;
    }

    /// <summary>Forgets the trash of the case that was open.</summary>
    private void ForgetTrash()
    {
        TrashWarning = null;
        _trash = TrashFilter.Empty;
        _lastThrow = null;
        _toldThrownStarts = string.Empty;
        BeginTrashRead();
    }

    /// <summary>Shows what the trash holds in the bin's dot and the trash's view.</summary>
    private void ShowTrash()
    {
        TrashView.Show(_trash.Throws, Scene?.Trashed ?? 0, WatchesOfMember);
        ShowBinDot();
    }

    /// <summary>Shows the bin's dot when anything lies in the trash.</summary>
    private void ShowBinDot() => Bin.HasThrows = !_trash.IsEmpty || CaseTrash.Cases.Count > 0;

    /// <summary>
    /// Says what the graph says when everything drawn lies in the trash, or an empty text when something is drawn.
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <returns>The text.</returns>
    private static string TrashEmptyText(GraphScene scene) =>
        scene.Nodes.Count == 0 && scene.Trashed > 0 ? AllInTrashText : string.Empty;

    /// <summary>
    /// Tells a start point that lies in the trash (it would otherwise leave the graph silent), once per reading of the
    /// same, with <em>Lägg tillbaka</em>; told again when a start point is added, since it may be the same address.
    /// </summary>
    /// <param name="scene">The graph.</param>
    private void TellThrownStartPoints(GraphScene scene)
    {
        if (_session.Current is not { } openCase)
        {
            return;
        }

        var thrown = scene.Snapshot.Graph.Nodes
            .Where(node => node.IsStartPoint && scene.Node(node.Key) is null && scene.Trash.Hides(node))
            .ToList();
        var told = string.Join("|", thrown.Select(node => node.Key));
        if (told == _toldThrownStarts)
        {
            return;
        }

        _toldThrownStarts = told;
        var holders = thrown.Select(HolderOf).OfType<(long Throw, string Key)>().Distinct().ToList();
        if (holders.Count == 0)
        {
            return;
        }

        ToastWanted?.Invoke(this, new ToastRequest(
            ToastKind.Warning,
            StartPointThrownText,
            string.Empty,
            RestoreActionText,
            () =>
            {
                foreach (var (sequence, key) in holders)
                {
                    _ = RestoreInAsync(openCase, sequence, key);
                }
            }));
    }

    /// <summary>Finds the throw and member that hide a node.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The throw and the member, or <see langword="null"/>.</returns>
    private (long Throw, string Key)? HolderOf(GraphNode node)
    {
        var keys = TrashFilter.KeysOf(node);
        foreach (var each in _trash.Throws)
        {
            foreach (var member in each.Members)
            {
                var lying = member.FoundBy.Append(member.Key).ToHashSet(StringComparer.Ordinal);
                if (TrashMember.IsHidden(node.Key, keys, lying))
                {
                    return (each.Sequence, member.Key);
                }
            }
        }

        return null;
    }

    /// <summary>Notes where a dragged node is, so that the bin grows as it comes near.</summary>
    /// <param name="distanceToBin">The pointer's distance from the bin's centre, in pixels.</param>
    public void DragNear(double distanceToBin)
    {
        if (CanThrow)
        {
            Bin.DragAt(distanceToBin);
        }
    }

    /// <summary>Ends a drag at the bin: a release on it asks to throw the node, or the whole picked group dragged.</summary>
    /// <param name="nodeKey">The dragged node.</param>
    /// <returns><see langword="true"/> when the release was on the bin, so that the node is not moved.</returns>
    public bool DropOnBin(string nodeKey)
    {
        if (!Bin.EndDrag())
        {
            return false;
        }

        if (TakeGroup() is { } group)
        {
            ThrowGroup(group.Actors, group.Notes);
            return true;
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
        if (CanThrow && !HasTrashWarning && members.Count > 0)
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

    /// <summary>Throws what the warning shows, then ends the watches the user chose to end (Enter).</summary>
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

    /// <summary>
    /// Puts the latest throw of this session back (Ctrl+Z), as the toast's <em>Ångra</em> does; only in the case it was
    /// made in.
    /// </summary>
    /// <returns>A task that completes when it is put back.</returns>
    public Task UndoThrowAsync() =>
        _lastThrow is { } last ? RestoreInAsync(last.Case, last.Sequence, null) : Task.CompletedTask;

    /// <summary>Puts a member of a throw in the open case back, or the whole throw (the trash's view).</summary>
    /// <param name="throwSequence">The throw.</param>
    /// <param name="key">The member, or <see langword="null"/> for the whole throw.</param>
    /// <returns>A task that completes when it is put back and shown.</returns>
    public Task RestoreAsync(long throwSequence, string? key) =>
        _session.Current is { } openCase ? RestoreInAsync(openCase, throwSequence, key) : Task.CompletedTask;

    /// <summary>
    /// Puts a member of a throw back with its branch and what it hangs under, or the whole throw, and shows the graph
    /// with it; refused when the throw's case is no longer open, since its number means nothing in another.
    /// </summary>
    /// <param name="openCase">The case the throw was made in.</param>
    /// <param name="throwSequence">The throw.</param>
    /// <param name="key">The member, or <see langword="null"/> for the whole throw.</param>
    /// <returns>A task that completes when it is put back and shown.</returns>
    private async Task RestoreInAsync(ICase openCase, long throwSequence, string? key)
    {
        if (_trashSource is null)
        {
            return;
        }

        if (!IsStillOpen(openCase))
        {
            Tell(ToastKind.Warning, UndoOtherCaseText);
            return;
        }

        try
        {
            var back = await _trashSource.RestoreAsync(openCase, throwSequence, key);
            if (!IsStillOpen(openCase))
            {
                return;
            }

            if (key is null && _lastThrow is { } last && last.Sequence == throwSequence)
            {
                _lastThrow = null;
            }

            await ApplyTrashAsync(openCase);
            if (back.Count > 0 && IsStillOpen(openCase))
            {
                Say("Lade tillbaka " + TrashViewModel.CountText(back.Count) + " i grafen.");
            }
        }
        catch (Exception exception) when (exception is CaseException or InvalidOperationException)
        {
            if (IsStillOpen(openCase))
            {
                Tell(ToastKind.Error, RestoreFailedText);
            }
        }
    }

    /// <summary>
    /// Throws, then ends the watches the user chose to end, and shows the graph without what was thrown. The throw is
    /// written first, so that a throw that fails never ends a watch; a watch that could not be ended is said. Nothing
    /// is shown or remembered once another case is open.
    /// </summary>
    /// <param name="warning">What to throw.</param>
    /// <returns>A task that completes when it is thrown and shown.</returns>
    private async Task ThrowAsync(TrashWarningViewModel warning)
    {
        if (_trashSource is null || _session.Current is not { } openCase || _throwing)
        {
            return;
        }

        _throwing = true;
        try
        {
            if (await WriteThrowAsync(openCase, warning) is not { } thrown)
            {
                return;
            }

            var (ended, failed) = await EndWatchesAsync(openCase, warning.WatchesToEnd);
            if (!IsStillOpen(openCase))
            {
                return;
            }

            if (warning.Keys.Contains(SelectedNode ?? string.Empty))
            {
                Choose(null, null);
            }

            Fan?.Close();
            await ApplyTrashAsync(openCase);
            if (!IsStillOpen(openCase))
            {
                return;
            }

            ToastWanted?.Invoke(this, new ToastRequest(
                ToastKind.Information,
                ThrownText(thrown.Members.Count, ended),
                ended > 0 ? WatchesStayEndedText : string.Empty,
                UndoText,
                () => _ = RestoreInAsync(openCase, thrown.Sequence, null)));
            if (failed > 0)
            {
                Tell(ToastKind.Warning, WatchesNotEndedText);
            }
        }
        finally
        {
            _throwing = false;
        }
    }

    /// <summary>Writes a throw; says what went wrong when it could not be.</summary>
    /// <param name="openCase">The case.</param>
    /// <param name="warning">What to throw.</param>
    /// <returns>The throw, or <see langword="null"/> when nothing was thrown or another case is open now.</returns>
    private async Task<TrashThrow?> WriteThrowAsync(ICase openCase, TrashWarningViewModel warning)
    {
        try
        {
            var thrown = await _trashSource!.ThrowAsync(openCase, warning.Members);
            if (!IsStillOpen(openCase))
            {
                return null;
            }

            _lastThrow = (openCase, thrown.Sequence);
            return thrown;
        }
        catch (InvalidOperationException)
        {
            if (IsStillOpen(openCase))
            {
                Tell(ToastKind.Information, AlreadyThrownText);
                await ApplyTrashAsync(openCase);
            }
        }
        catch (Exception exception) when (exception is CaseException or ArgumentException)
        {
            if (IsStillOpen(openCase))
            {
                Tell(ToastKind.Error, ThrowFailedText);
                Redraw?.Invoke(this, EventArgs.Empty);
            }
        }

        return null;
    }

    /// <summary>Ends watches after their nodes were thrown, each with its own sealed record in Monitoring.</summary>
    /// <param name="openCase">The open case.</param>
    /// <param name="watchIds">The watches.</param>
    /// <returns>How many ended, and how many could not be ended.</returns>
    private async Task<(int Ended, int Failed)> EndWatchesAsync(ICase openCase, IReadOnlyList<string> watchIds)
    {
        if (_watches is not { } board || watchIds.Count == 0)
        {
            return (0, 0);
        }

        var (ended, failed) = (0, 0);
        foreach (var id in watchIds)
        {
            var done = false;
            var wrote = await board.WriteAsync(
                async () => done = await board.Source.RemoveAsync(openCase, id, WatchEndedReason));
            ended += done ? 1 : 0;
            failed += wrote ? 0 : 1;
        }

        return (ended, failed);
    }

    /// <summary>Reads the trash again and shows the graph through it, when the reading is the latest.</summary>
    /// <param name="openCase">The open case.</param>
    /// <returns>A task that completes when it is shown.</returns>
    private async Task ApplyTrashAsync(ICase openCase)
    {
        var read = BeginTrashRead();
        var trash = await ReadTrashAsync(openCase, CancellationToken.None);
        if (!KeepTrash(read, openCase, trash))
        {
            return;
        }

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
    /// Gives the active and paused watches a throw would touch at a node in the graph: those on its addresses (or for a
    /// pile its cards'), and those that have them as a counterparty, whose watch line would go into the trash.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <returns>The watches' ids.</returns>
    private IReadOnlyList<string> WatchesInGraph(TrashMember member)
    {
        var addresses = Scene?.Pile(member.Key) is { } pile
            ? pile.Cards.SelectMany(card => card.Node.Node.Addresses).ToHashSet()
            : (Scene?.Node(member.Key)?.Node.Addresses ?? []).ToHashSet();
        return LiveWatches()
            .Where(watch => addresses.Contains(watch.Subject.Anchor)
                || watch.Filter.Receivers.Concat(watch.Filter.Senders).Any(addresses.Contains))
            .Select(watch => watch.Id)
            .ToList();
    }

    /// <summary>Gives the live watches on a member in the trash, by the keys it is known by.</summary>
    /// <param name="member">The member.</param>
    /// <returns>The watches' ids.</returns>
    private IReadOnlyList<string> WatchesOfMember(TrashMember member)
    {
        var keys = member.FoundBy.Append(member.Key).ToHashSet(StringComparer.Ordinal);
        return LiveWatches()
            .Where(watch => keys.Contains(KeyOfWatched(watch.Subject.Anchor)))
            .Select(watch => watch.Id)
            .ToList();
    }

    /// <summary>Gives the key a watched address is known by in the trash: its address key on Bitcoin, else its node.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The key.</returns>
    private static string KeyOfWatched(ChainAddress address) => address.Chain == Chain.Bitcoin
        ? GraphPile.AddressKeyPrefix + address.Value
        : GraphPile.AccountKey(address);

    /// <summary>Gives the watches that are checked or paused, not those taken back.</summary>
    /// <returns>The watches.</returns>
    private IEnumerable<Watch> LiveWatches() =>
        (_watches?.Snapshot.Watches ?? []).Where(watch => watch.State != WatchState.Removed);
}
