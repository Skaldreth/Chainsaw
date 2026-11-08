using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The graph's piles and clues (sketches EX1, EX2 and EX2b) and the hidden outputs that can never be spent: a click on
/// a clue opens the selection view of what was held back, where the chosen are brought into the graph; a click on a
/// pile fans its tiles out over the dimmed graph, where a tile dragged out becomes a node of its own; and a lingering
/// toast offers to show the hidden outputs.
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>The action on the toast that shows the hidden outputs.</summary>
    public const string ShowUnspendableText = "Visa dem";

    /// <summary>The action on the toast that hides them again.</summary>
    public const string HideUnspendableText = "Dölj dem";

    /// <summary>What the graph says when the outputs that can never be spent are shown again.</summary>
    public const string UnspendableShownText = "Utgångar utan mottagare visas i grafen.";

    /// <summary>What the graph says when a pick could not be stored.</summary>
    public const string PickFailedText = "Urvalet kunde inte sparas i ärendet.";

    /// <summary>What the graph says when what was held back could not be read.</summary>
    public const string HeldBackFailedText = "Det som hölls tillbaka kunde inte läsas.";

    /// <summary>What the graph says when the address's history holds nothing more to choose.</summary>
    public const string NothingHeldBackText = "Det finns inget mer att välja vid adressen.";

    /// <summary>
    /// How far apart the cards picked out together are put where they were dropped, one under the other.
    /// </summary>
    private const double PickedSpacing = GraphSizes.RowSpacing;

    /// <summary>
    /// Whether the outputs that can never be spent and carry nothing are drawn; stored with the graph's view.
    /// </summary>
    private bool _showUnspendable;

    /// <summary>Gets the pile fanned out over the dimmed graph, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFan))]
    public partial PileFanViewModel? Fan { get; private set; }

    /// <summary>Gets whether a pile is fanned out.</summary>
    public bool HasFan => Fan is not null;

    /// <summary>What the toast says when Esc stopped a read of the history.</summary>
    public const string HeldBackStoppedText = "Läsningen avbröts. Det som redan hämtats ligger kvar i ärendet.";

    /// <summary>Stops the read of the history under way, or <see langword="null"/> when none is.</summary>
    private CancellationTokenSource? _reading;

    /// <summary>
    /// Gets the clue whose held-back transactions are being read, so that the graph is sent out in the circular blast
    /// at once (sketch EX1) while they come; <see langword="null"/> otherwise.
    /// </summary>
    [ObservableProperty]
    public partial string? OpeningClue { get; private set; }

    /// <summary>
    /// Gets the selection view of what was held back (sketches EX1 and EX2), or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeldBack), nameof(ShowsCorner))]
    public partial HeldBackViewModel? HeldBack { get; private set; }

    /// <summary>Gets whether the selection view is open.</summary>
    public bool HasHeldBack => HeldBack is not null;

    /// <summary>
    /// Gets whether the graph's corner and its ? are shown: with a case, and not while the selection view stands in
    /// the graph's place with its own tools (sketch EX2).
    /// </summary>
    public bool ShowsCorner => HasCase && HeldBack is null;

    /// <summary>Gets whether the outputs that can never be spent are drawn.</summary>
    public bool ShowsUnspendable => _showUnspendable;

    /// <summary>
    /// Says the toast's text for the hidden outputs: <c>49 utgångar utan mottagare döljs</c>.
    /// </summary>
    /// <param name="count">How many.</param>
    /// <returns>The text.</returns>
    public static string HiddenUnspendableText(int count) => count == 1
        ? "1 utgång utan mottagare döljs."
        : string.Create(CultureInfo.InvariantCulture, $"{count} utgångar utan mottagare döljs.");

    /// <summary>
    /// Gets how the graph is read now: by its method, with the outputs that can never be spent drawn or hidden and
    /// the watched kept out of the piles, so that the source builds the scene the way it is shown, off the UI thread
    /// (principle 6).
    /// </summary>
    private GraphReading Reading => new(Method, _showUnspendable) { KeptOutOfPiles = _keptOutOfPiles };

    /// <summary>
    /// Shows or hides the outputs that can never be spent and carry nothing, and stores the choice with the view; the
    /// graph is built again off the UI thread (<see cref="ShowUnspendableAsync"/>).
    /// </summary>
    /// <param name="show">Whether they are drawn.</param>
    public void ShowUnspendable(bool show) => _ = ShowUnspendableAsync(show);

    /// <summary>
    /// Shows or hides the outputs that can never be spent and carry nothing, stores the choice with the view, and
    /// builds the graph again from what it already holds, off the UI thread (principle 6).
    /// </summary>
    /// <param name="show">Whether they are drawn.</param>
    /// <returns>A task that completes when the graph is shown the new way, or another reading took its place.</returns>
    public async Task ShowUnspendableAsync(bool show)
    {
        if (_showUnspendable == show)
        {
            return;
        }

        _showUnspendable = show;
        OnPropertyChanged(nameof(ShowsUnspendable));
        OnPropertyChanged(nameof(UnspendableToolTip));
        SaveView();
        if (show)
        {
            ToastWanted?.Invoke(this, new ToastRequest(
                ToastKind.Information, UnspendableShownText, ActionText: HideUnspendableText,
                Action: () => ShowUnspendable(false))
            {
                Lingers = true,
            });
        }

        await FollowReadingAsync();
    }

    /// <summary>
    /// Builds the scene shown again from what it holds, off the UI thread, when it is not drawn the way the graph is
    /// read now (the outputs that can never be spent, the addresses kept out of the piles), and shows it unless a
    /// newer reading, or another choice, came meanwhile.
    /// </summary>
    /// <returns>A task that completes when the graph is drawn the chosen way, or nothing was needed.</returns>
    private async Task FollowReadingAsync()
    {
        var reading = Reading;
        if (Scene is not { } scene || scene.IsDrawnAs(reading))
        {
            return;
        }

        var version = _version;
        var built = await Task.Run(() => scene.DrawnAs(reading));
        if (version == _version && ReferenceEquals(Scene, scene) && built.IsDrawnAs(Reading))
        {
            Show(built, fit: false, keepNotice: true);
        }
    }

    /// <summary>
    /// Fans a pile out over the dimmed graph (sketch EX2b), around the pile's node; a click on the pile that is fanned
    /// out folds it again.
    /// </summary>
    /// <param name="pileKey">The pile's key.</param>
    public void OpenFan(string pileKey)
    {
        if (Fan?.Pile.Key == pileKey)
        {
            CloseFan();
            return;
        }

        if (Scene?.Pile(pileKey) is not { } pile || !Places.TryGetValue(pileKey, out var place))
        {
            return;
        }

        CloseFan();
        var frame = GraphSizes.NodeFrame(place);
        var fan = new PileFanViewModel(pile, frame.Centre);
        fan.Changed += (_, _) => Redraw?.Invoke(this, EventArgs.Empty);
        fan.PickWanted += (_, keys) => _ = PickFromPileAsync(keys, null);
        fan.CloseWanted += (_, _) => CloseFan();
        Fan = fan;
        Redraw?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Folds the fan; the tiles lie in the pile again. The selection view closes too.</summary>
    public void CloseFan()
    {
        CloseHeldBack();
        if (Fan is null)
        {
            return;
        }

        Fan = null;
        Redraw?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens the selection view of what an address's expansion held back (sketch EX1): the graph is sent out at once,
    /// the history is read off the UI thread (every page sealed in the case), and the view opens with it. A clue that
    /// has no address's history (a transaction's spenders, an Ethereum address) brings in the next part instead.
    /// </summary>
    /// <param name="clue">The clue.</param>
    /// <returns>A task that completes when the view is open, or could not be.</returns>
    public async Task OpenHeldBackAsync(HeldBackClue clue)
    {
        ArgumentNullException.ThrowIfNull(clue);

        // A second click while a read is under way must not take Esc's hold on the first read.
        if (_reading is not null)
        {
            return;
        }

        using var reading = CancellationTokenSource.CreateLinkedTokenSource(_work.Token);
        _reading = reading;
        try
        {
            await OpenHeldBackCoreAsync(clue, reading.Token);
        }
        finally
        {
            _reading = null;
        }
    }

    /// <summary>
    /// Esc while the history is read (the user's decision 2026-11-08): stops the read, brings the graph back and says
    /// that what was already fetched stays sealed in the case.
    /// </summary>
    /// <returns><see langword="true"/> when a read was under way and is stopped.</returns>
    public bool StopHeldBackRead()
    {
        if (OpeningClue is null || _reading is not { } reading)
        {
            return false;
        }

        reading.Cancel();
        Tell(ToastKind.Information, HeldBackStoppedText);
        return true;
    }

    /// <summary>Opens the selection view, reading the history with a token that Esc can stop.</summary>
    /// <param name="clue">The clue.</param>
    /// <param name="token">Stops the read.</param>
    /// <returns>A task that completes when the view is open, or could not be.</returns>
    private async Task OpenHeldBackCoreAsync(HeldBackClue clue, CancellationToken token)
    {
        if (clue.Address is null)
        {
            await ExpandClueAsync(clue);
            return;
        }

        if (IsExpanding || OpeningClue is not null || _session.Current is not { } openCase || Scene is not { } scene)
        {
            return;
        }

        CloseFan();
        Choose(null, null);
        var version = _version;
        OpeningClue = clue.Key;
        HeldBackChoices choices;
        try
        {
            choices = await _source.ListHeldBackAsync(openCase, clue, scene, token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            OpeningClue = null;

            // A read cancelled, or one for a case that was closed or reread meanwhile, is no fault to say.
            if (exception is OperationCanceledException || version != _version || _session.Current != openCase)
            {
                return;
            }

            Tell(ToastKind.Error, HeldBackFailedText, exception.Message, () => _ = OpenHeldBackAsync(clue));
            return;
        }

        OpeningClue = null;
        if (version != _version || _session.Current != openCase)
        {
            return;
        }

        if (choices.Choices.Count == 0)
        {
            Tell(ToastKind.Information, NothingHeldBackText, choices.Problem ?? string.Empty);
            return;
        }

        if (choices.Problem is { } problem)
        {
            Tell(ToastKind.Warning, problem);
        }

        var view = new HeldBackViewModel(choices, now: _clock.UtcNow);
        view.Changed += (_, _) => Redraw?.Invoke(this, EventArgs.Empty);
        view.TakeWanted += (_, keys) => _ = TakeHeldBackAsync(choices, keys);
        view.CloseWanted += (_, _) => CloseHeldBack();
        HeldBack = view;
    }

    /// <summary>Closes the selection view; the graph comes back.</summary>
    public void CloseHeldBack()
    {
        if (HeldBack is not null)
        {
            HeldBack = null;
            Redraw?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Brings the chosen held-back transactions into the graph (<em>Plocka ut</em>): the view closes, the graph comes
    /// back, and the new counterparties lie in the address's pile when they are many.
    /// </summary>
    /// <param name="choices">What the view was opened with, which knows the address each was read at.</param>
    /// <param name="txids">The chosen transactions' ids.</param>
    /// <returns>A task that completes when the graph is shown with them.</returns>
    public async Task TakeHeldBackAsync(HeldBackChoices choices, IReadOnlyList<string> txids)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(txids);
        CloseHeldBack();
        var picks = choices.PicksOf(txids);
        if (IsExpanding || _session.Current is not { } openCase || picks.Count == 0)
        {
            return;
        }

        await ExpandWithAsync(
            token => _source.TakeHeldBackAsync(openCase, picks, Reading, token),
            () => _ = TakeHeldBackAsync(choices, txids));
    }

    /// <summary>
    /// Picks cards out of the fanned pile: each becomes a node of its own, put where it was dropped (the others picked
    /// with it under it), and the case remembers it, so that the graph looks the same when it is opened again.
    /// </summary>
    /// <param name="nodeKeys">The cards' node keys.</param>
    /// <param name="droppedAt">
    /// Where the first was dropped on the surface, or <see langword="null"/> for its own place.
    /// </param>
    /// <returns>A task that completes when the graph is shown with them picked out.</returns>
    public async Task PickFromPileAsync(IReadOnlyList<string> nodeKeys, GraphPoint? droppedAt)
    {
        ArgumentNullException.ThrowIfNull(nodeKeys);
        if (_session.Current is not { } openCase || nodeKeys.Count == 0)
        {
            return;
        }

        try
        {
            if (droppedAt is { } at)
            {
                for (var index = 0; index < nodeKeys.Count; index++)
                {
                    var place = new GraphPoint(at.X, at.Y + (index * PickedSpacing));
                    _moved[(nodeKeys[index], GraphLayout.Free)] = place;
                    await _source.SavePositionAsync(openCase, nodeKeys[index], GraphLayout.Free, place);
                }
            }

            await _source.PickFromPileAsync(openCase, nodeKeys);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Tell(ToastKind.Warning, PickFailedText);
            return;
        }

        await RefreshAsync();
    }

    /// <summary>Gets whether the chosen node was picked out of a pile and can be put back.</summary>
    public bool CanReturnToPile => SelectedNode is { } key
        && Scene?.Snapshot.Graph.Piles.Any(pile => pile.PickedOut.Contains(key)) == true;

    /// <summary>
    /// Puts the chosen node back in its pile, and the case forgets that it was picked out, under every address the
    /// node has, so that a cluster whose representative changed since is put back too.
    /// </summary>
    /// <returns>A task that completes when the graph is shown with it back in the pile.</returns>
    public async Task ReturnToPileAsync()
    {
        if (_session.Current is not { } openCase || !CanReturnToPile || SelectedNode is not { } key)
        {
            return;
        }

        var members = Scene?.Node(key)?.Node.GraphAddresses.Select(GraphPile.MemberKey) ?? [];
        try
        {
            await _source.ReturnToPileAsync(openCase, [key, .. members]);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Tell(ToastKind.Warning, PickFailedText);
            return;
        }

        Choose(null, null);
        await RefreshAsync();
    }

    /// <summary>
    /// Takes a click on a pile or a clue (or a clue's line): a pile fans out, a clue opens the selection view. Neither
    /// is chosen, since neither is an actor to inspect.
    /// </summary>
    /// <param name="nodeKey">The node clicked, or <see langword="null"/>.</param>
    /// <param name="edgeKey">The line clicked, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the click was a pile's or a clue's.</returns>
    private bool ChooseFolded(string? nodeKey, string? edgeKey)
    {
        if (Scene is not { } scene)
        {
            return false;
        }

        if (scene.Pile(nodeKey) is { } pile)
        {
            OpenFan(pile.Key);
            return true;
        }

        var clue = scene.Clue(nodeKey) ?? ClueOfLine(scene, edgeKey);
        if (clue is null)
        {
            return false;
        }

        _ = OpenHeldBackAsync(clue);
        return true;
    }

    /// <summary>Gives the clue a clue's thin line leads to.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="edgeKey">The line, or <see langword="null"/>.</param>
    /// <returns>The clue, or <see langword="null"/> when the line is not a clue's.</returns>
    private static HeldBackClue? ClueOfLine(GraphScene scene, string? edgeKey) =>
        scene.Edge(edgeKey) is { Edge.Kind: GraphEdgeKind.HeldBack } line ? scene.Clue(line.Edge.To) : null;

    /// <summary>
    /// Brings in the next part of what an expansion held back, for a clue without an address's history to choose
    /// from (sketch EX1).
    /// </summary>
    /// <param name="clue">The clue.</param>
    /// <returns>A task that completes when the graph is shown after it.</returns>
    public async Task ExpandClueAsync(HeldBackClue clue)
    {
        ArgumentNullException.ThrowIfNull(clue);
        if (IsExpanding || _session.Current is not { } openCase)
        {
            return;
        }

        await ExpandWithAsync(openCase, clue.Next, () => _ = ExpandClueAsync(clue));
    }

    /// <summary>
    /// Says, in a toast that lingers, how many outputs that can never be spent are hidden, when an expansion brought
    /// new ones and they are hidden; its action shows them.
    /// </summary>
    /// <param name="before">How many the graph had before the expansion.</param>
    private void SayHiddenUnspendable(int before)
    {
        if (_showUnspendable || Scene is not { } scene || scene.Unspendable <= before)
        {
            return;
        }

        ToastWanted?.Invoke(this, new ToastRequest(
            ToastKind.Information,
            HiddenUnspendableText(scene.Unspendable),
            ActionText: ShowUnspendableText,
            Action: () => ShowUnspendable(true))
        {
            Lingers = true,
        });
    }

    /// <summary>
    /// Follows a new graph with the fan: the same pile fanned out with what is left in it and what was chosen still
    /// chosen, or folded when it is gone.
    /// </summary>
    private void FollowFan()
    {
        if (Fan is not { } fan)
        {
            return;
        }

        if (Scene?.Pile(fan.Pile.Key) is null)
        {
            CloseFan();
            return;
        }

        var chosen = fan.Selected.ToList();
        Fan = null;
        OpenFan(fan.Pile.Key);
        foreach (var key in chosen)
        {
            Fan?.Click(key, add: true);
        }
    }
}
