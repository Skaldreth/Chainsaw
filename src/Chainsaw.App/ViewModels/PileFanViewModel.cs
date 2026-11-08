using Chainsaw.App.Graph;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// A pile fanned out in the graph (sketch EX2b): its cards as tiles in a ring around it, the rest of the graph dimmed.
/// A click chooses a tile, Shift-click chooses more, and a tile dragged out onto the dimmed graph becomes a node of
/// its own where it is dropped, with the other chosen under it; the rest stay in the pile. A new click on the pile
/// folds it again. Without WPF, so that it is tested in Chainsaw.App.Tests.
/// </summary>
public sealed partial class PileFanViewModel : ObservableObject
{
    /// <summary>The chosen tiles by key.</summary>
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);

    /// <summary>Creates the fan for a pile.</summary>
    /// <param name="pile">The pile.</param>
    /// <param name="centre">The pile node's centre on the surface.</param>
    public PileFanViewModel(PileView pile, GraphPoint centre)
    {
        ArgumentNullException.ThrowIfNull(pile);
        Pile = pile;
        Centre = centre;
        Shown = [.. pile.Cards.OrderByDescending(card => card.Amount.Units)
            .ThenBy(card => card.Key, StringComparer.Ordinal)];
        Places = PileFanLayout.PlaceRing(Shown, centre);
    }

    /// <summary>Raised when what is chosen changes, so that the surface draws again.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised when the chosen tiles are to be picked out where they lie; the graph stores it in the case.
    /// </summary>
    public event EventHandler<IReadOnlyList<string>>? PickWanted;

    /// <summary>Raised when the fan is to fold.</summary>
    public event EventHandler? CloseWanted;

    /// <summary>Gets the pile.</summary>
    public PileView Pile { get; }

    /// <summary>Gets the pile node's centre on the surface, around which the tiles lie.</summary>
    public GraphPoint Centre { get; }

    /// <summary>Gets the tiles, the largest amount first, which take the ring's places from the top.</summary>
    public IReadOnlyList<PileCard> Shown { get; }

    /// <summary>Gets each tile's centre on the surface.</summary>
    public IReadOnlyDictionary<string, GraphPoint> Places { get; }

    /// <summary>Gets the chosen tiles' keys.</summary>
    public IReadOnlySet<string> Selected => _selected;

    /// <summary>Gets whether any tile is chosen.</summary>
    public bool HasSelection => _selected.Count > 0;

    /// <summary>Gives the tile under a point on the surface, the one drawn last on top.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The tile's key, or <see langword="null"/>.</returns>
    public string? CardAt(GraphPoint point) => Shown
        .Select(card => card.Key)
        .LastOrDefault(key => PileFanLayout.TileFrame(Places[key]).Contains(point));

    /// <summary>
    /// Chooses a tile: alone, or, with Shift, besides those chosen (a second Shift-click lets it go).
    /// </summary>
    /// <param name="key">The tile.</param>
    /// <param name="add">Whether Shift is held.</param>
    public void Click(string key, bool add)
    {
        if (!Places.ContainsKey(key))
        {
            return;
        }

        if (!add)
        {
            var only = _selected.Count == 1 && _selected.Contains(key);
            _selected.Clear();
            if (!only)
            {
                _selected.Add(key);
            }
        }
        else if (!_selected.Remove(key))
        {
            _selected.Add(key);
        }

        OnPropertyChanged(nameof(HasSelection));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gives the tiles that go when one is dragged out: all the chosen when it is one of them, otherwise only it.
    /// </summary>
    /// <param name="key">The tile dragged.</param>
    /// <returns>The keys, the dragged first.</returns>
    public IReadOnlyList<string> Taking(string key) =>
        _selected.Contains(key) ? [key, .. _selected.Where(other => other != key).Order(StringComparer.Ordinal)] : [key];

    /// <summary>Picks the chosen tiles out of the pile where they lie (Enter).</summary>
    [RelayCommand]
    public void PickSelected()
    {
        if (_selected.Count > 0)
        {
            PickWanted?.Invoke(this, [.. _selected.Order(StringComparer.Ordinal)]);
        }
    }

    /// <summary>Folds the pile again (Esc, a click on the pile, or a click on the dimmed graph).</summary>
    [RelayCommand]
    public void Close() => CloseWanted?.Invoke(this, EventArgs.Empty);
}
