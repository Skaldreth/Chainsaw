using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The graph's piles and clues (sketches EX1, EX2 and EX2b) and the hidden outputs that can never be spent: a click on
/// a pile fans its cards out over the dimmed graph, the chosen cards are picked out and kept in the case, a click on a
/// clue brings in the next part, and a lingering toast offers to show the hidden outputs.
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
    /// Shows or hides the outputs that can never be spent and carry nothing, and stores the choice with the view.
    /// </summary>
    /// <param name="show">Whether they are drawn.</param>
    public void ShowUnspendable(bool show)
    {
        if (_showUnspendable == show)
        {
            return;
        }

        _showUnspendable = show;
        if (Scene is { } scene)
        {
            Show(scene, fit: false, keepNotice: true);
        }

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
    }

    /// <summary>Fans a pile out over the dimmed graph (sketch EX2b), around the pile's node.</summary>
    /// <param name="pileKey">The pile's key.</param>
    public void OpenFan(string pileKey)
    {
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

    /// <summary>Closes the fan; the cards lie in the pile again.</summary>
    public void CloseFan()
    {
        if (Fan is null)
        {
            return;
        }

        Fan = null;
        Redraw?.Invoke(this, EventArgs.Empty);
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

    /// <summary>Puts the chosen node back in its pile, and the case forgets that it was picked out.</summary>
    /// <returns>A task that completes when the graph is shown with it back in the pile.</returns>
    public async Task ReturnToPileAsync()
    {
        if (_session.Current is not { } openCase || !CanReturnToPile || SelectedNode is not { } key)
        {
            return;
        }

        try
        {
            await _source.ReturnToPileAsync(openCase, [key]);
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
    /// Takes a click on a pile or a clue (or a clue's line): a pile fans out, a clue brings in the next part. Neither is
    /// chosen, since neither is an actor to inspect.
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

        _ = ExpandClueAsync(clue);
        return true;
    }

    /// <summary>Gives the clue a clue's thin line leads to.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="edgeKey">The line, or <see langword="null"/>.</param>
    /// <returns>The clue, or <see langword="null"/> when the line is not a clue's.</returns>
    private static HeldBackClue? ClueOfLine(GraphScene scene, string? edgeKey)
    {
        if (edgeKey is null || !edgeKey.StartsWith(GraphFold.ClueEdgePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return scene.Edge(edgeKey) is { } line ? scene.Clue(line.Edge.To) : null;
    }

    /// <summary>Brings in the next part of what an expansion held back (sketch EX1).</summary>
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
    /// Follows a new graph with the fan: the same pile fanned out with what is left in it, or closed when it is gone.
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

        var mode = fan.Mode;
        OpenFan(fan.Pile.Key);
        if (Fan is { } opened)
        {
            opened.Mode = mode;
        }
    }
}
