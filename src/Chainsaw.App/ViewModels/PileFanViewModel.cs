using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// A pile fanned out (sketches EX1a, EX1b and EX2): its cards around it over a dimmed graph, in a spiral or in time
/// arcs, with the filters of EX2 (the amount between a lowest and a highest, <em>Dölj damm</em>) and a counter. A
/// click chooses a card, Shift-click chooses more, and the chosen are picked out with Enter or by dragging one of them
/// out; the rest stay in the pile. Without WPF, so that it is tested in Chainsaw.App.Tests.
/// </summary>
public sealed partial class PileFanViewModel : ObservableObject
{
    /// <summary>
    /// What counts as dust (<em>Dölj damm</em>): less than 10 000 satoshi moved between the card and the actor, a sum
    /// too small to be worth following in a pile of many.
    /// </summary>
    public static readonly AssetAmount DustLimit = new Satoshi(10_000).ToAmount();

    /// <summary>The cards, chosen ones by key.</summary>
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);

    /// <summary>The smallest amount above zero among the cards, the slider's low end; one satoshi when none.</summary>
    private readonly decimal _lowest;

    /// <summary>The largest amount among the cards, the slider's high end.</summary>
    private readonly decimal _highest;

    /// <summary>Creates the fan for a pile.</summary>
    /// <param name="pile">The pile.</param>
    /// <param name="centre">The pile node's centre on the surface.</param>
    /// <param name="mode">The mode to open in.</param>
    public PileFanViewModel(PileView pile, GraphPoint centre, PileFanMode mode = PileFanMode.Spiral)
    {
        ArgumentNullException.ThrowIfNull(pile);
        Pile = pile;
        Centre = centre;
        var amounts = pile.Cards.Select(card => ToDecimal(card.Amount)).Where(amount => amount > 0).ToList();
        _lowest = amounts.Count == 0 ? ToDecimal(new Satoshi(1).ToAmount()) : amounts.Min();
        _highest = amounts.Count == 0 ? _lowest : Math.Max(amounts.Max(), _lowest);
        Mode = mode;
        Refilter();
    }

    /// <summary>Raised when the cards shown, their places or what is chosen change, so that the surface draws again.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the chosen cards are to be picked out; the graph stores it in the case.</summary>
    public event EventHandler<IReadOnlyList<string>>? PickWanted;

    /// <summary>Raised when the fan is to close.</summary>
    public event EventHandler? CloseWanted;

    /// <summary>Gets the pile.</summary>
    public PileView Pile { get; }

    /// <summary>Gets the pile node's centre on the surface, around which the cards lie.</summary>
    public GraphPoint Centre { get; }

    /// <summary>Gets the mode: a spiral (EX1a) or time arcs (EX1b).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSpiral), nameof(IsTimeArc))]
    public partial PileFanMode Mode { get; set; }

    /// <summary>Gets whether the cards lie in a spiral.</summary>
    public bool IsSpiral => Mode == PileFanMode.Spiral;

    /// <summary>Gets whether the cards lie in time arcs.</summary>
    public bool IsTimeArc => Mode == PileFanMode.TimeArc;

    /// <summary>Gets or sets whether dust is hidden.</summary>
    [ObservableProperty]
    public partial bool HideDust { get; set; }

    /// <summary>Gets or sets the lowest amount shown, as a share of the slider from 0 to 1 on a logarithmic scale.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LowestText))]
    public partial double LowestShare { get; set; }

    /// <summary>Gets or sets the highest amount shown, as a share of the slider from 0 to 1 on a logarithmic scale.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HighestText))]
    public partial double HighestShare { get; set; } = 1;

    /// <summary>Gets the lowest amount shown, written out.</summary>
    public string LowestText => Written(AmountAt(LowestShare));

    /// <summary>Gets the highest amount shown, written out.</summary>
    public string HighestText => Written(AmountAt(HighestShare));

    /// <summary>Gets the cards the filters let through, in the mode's order.</summary>
    public IReadOnlyList<PileCard> Shown { get; private set; } = [];

    /// <summary>Gets each shown card's top-left corner on the surface.</summary>
    public IReadOnlyDictionary<string, GraphPoint> Places { get; private set; } = new Dictionary<string, GraphPoint>();

    /// <summary>Gets the chosen cards' keys.</summary>
    public IReadOnlySet<string> Selected => _selected;

    /// <summary>Gets the counter, for example <c>12 av 40</c>.</summary>
    [ObservableProperty]
    public partial string CountText { get; private set; } = string.Empty;

    /// <summary>Gets whether any card is chosen, so that <em>Plocka ut</em> can be used.</summary>
    public bool HasSelection => _selected.Count > 0;

    /// <summary>Gives the card under a point on the surface.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The card's key, or <see langword="null"/>.</returns>
    public string? CardAt(GraphPoint point) => Shown
        .Select(card => card.Key)
        .LastOrDefault(key => PileFanLayout.CardFrame(Places[key]).Contains(point));

    /// <summary>Chooses a card: alone, or, with Shift, besides those chosen (a second Shift-click lets it go).</summary>
    /// <param name="key">The card.</param>
    /// <param name="add">Whether Shift is held.</param>
    public void Click(string key, bool add)
    {
        if (!Places.ContainsKey(key))
        {
            return;
        }

        if (!add)
        {
            _selected.Clear();
            _selected.Add(key);
        }
        else if (!_selected.Remove(key))
        {
            _selected.Add(key);
        }

        SelectionChanged();
    }

    /// <summary>
    /// Gives the cards that go when one is dragged out: all the chosen when it is one of them, otherwise only it.
    /// </summary>
    /// <param name="key">The card dragged.</param>
    /// <returns>The keys.</returns>
    public IReadOnlyList<string> Taking(string key) =>
        _selected.Contains(key) ? [key, .. _selected.Where(other => other != key).Order(StringComparer.Ordinal)] : [key];

    /// <summary>Picks the chosen cards out of the pile (Enter, or <em>Plocka ut</em>).</summary>
    [RelayCommand]
    public void PickSelected()
    {
        if (_selected.Count > 0)
        {
            PickWanted?.Invoke(this, [.. _selected.Order(StringComparer.Ordinal)]);
        }
    }

    /// <summary>Steps the mode between the spiral and the time arcs.</summary>
    [RelayCommand]
    public void ToggleMode() => Mode = Mode == PileFanMode.Spiral ? PileFanMode.TimeArc : PileFanMode.Spiral;

    /// <summary>Shows the cards in a mode.</summary>
    /// <param name="mode">The mode.</param>
    [RelayCommand]
    public void ShowMode(PileFanMode mode) => Mode = mode;

    /// <summary>Hides or shows dust (<em>Dölj damm</em>).</summary>
    [RelayCommand]
    public void ToggleDust() => HideDust = !HideDust;

    /// <summary>Closes the fan (Esc, or a click on the dimmed graph).</summary>
    [RelayCommand]
    public void Close() => CloseWanted?.Invoke(this, EventArgs.Empty);

    /// <summary>Lays the cards out again in the new mode.</summary>
    /// <param name="value">The mode.</param>
    partial void OnModeChanged(PileFanMode value) => Refilter();

    /// <summary>Filters again when dust is hidden or shown.</summary>
    /// <param name="value">Whether it is hidden.</param>
    partial void OnHideDustChanged(bool value) => Refilter();

    /// <summary>Filters again, keeping the low end under the high.</summary>
    /// <param name="value">The share.</param>
    partial void OnLowestShareChanged(double value)
    {
        if (value > HighestShare)
        {
            HighestShare = value;
        }

        Refilter();
    }

    /// <summary>Filters again, keeping the high end over the low.</summary>
    /// <param name="value">The share.</param>
    partial void OnHighestShareChanged(double value)
    {
        if (value < LowestShare)
        {
            LowestShare = value;
        }

        Refilter();
    }

    /// <summary>
    /// Works out the cards shown and their places: through the filters, ordered by the largest amount for the spiral
    /// and by time for the arcs. A chosen card that is filtered away is let go.
    /// </summary>
    private void Refilter()
    {
        var lowest = AmountAt(LowestShare);
        var highest = AmountAt(HighestShare);
        var shown = Pile.Cards.Where(card => Passes(card, lowest, highest));
        Shown = Mode == PileFanMode.Spiral
            ? [.. shown.OrderByDescending(card => card.Amount.Units).ThenBy(card => card.Key, StringComparer.Ordinal)]
            : [.. shown.OrderBy(card => card.Time ?? DateTimeOffset.MaxValue)
                .ThenBy(card => card.Key, StringComparer.Ordinal)];
        Places = PileFanLayout.Place(Shown, Centre, Mode, Pile.Pile.Side);
        _selected.IntersectWith(Places.Keys);
        CountText = string.Create(CultureInfo.InvariantCulture, $"{Shown.Count} av {Pile.Cards.Count}");
        SelectionChanged();
    }

    /// <summary>Says whether a card passes the filters.</summary>
    /// <param name="card">The card.</param>
    /// <param name="lowest">The lowest amount shown.</param>
    /// <param name="highest">The highest amount shown.</param>
    /// <returns><see langword="true"/> when it is shown.</returns>
    private bool Passes(PileCard card, decimal lowest, decimal highest)
    {
        if (HideDust && card.Amount < DustLimit)
        {
            return false;
        }

        // A card with nothing moved straight to the actor lies at the low end, under any amount.
        var amount = Math.Max(ToDecimal(card.Amount), _lowest);
        return amount >= lowest && amount <= highest;
    }

    /// <summary>Gives the amount at a share of the slider, on a logarithmic scale from the lowest to the highest.</summary>
    /// <param name="share">The share, from 0 to 1.</param>
    /// <returns>The amount in whole units of the asset.</returns>
    private decimal AmountAt(double share)
    {
        if (share <= 0 || _highest <= _lowest)
        {
            return share >= 1 ? _highest : _lowest;
        }

        if (share >= 1)
        {
            return _highest;
        }

        var ratio = Math.Pow((double)(_highest / _lowest), Math.Clamp(share, 0, 1));
        return Math.Min(_highest, _lowest * (decimal)ratio);
    }

    /// <summary>Writes an amount in whole units with the shown asset's symbol.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>For example <c>0,0123 BTC</c>.</returns>
    private static string Written(decimal amount) => AmountText.WithUnit(new AssetAmount(
        GraphScene.Shown,
        new System.Numerics.BigInteger(decimal.Round(
            amount * (decimal)System.Numerics.BigInteger.Pow(10, GraphScene.Shown.Decimals)))));

    /// <summary>Gives an amount in whole units of its asset.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>The number.</returns>
    private static decimal ToDecimal(AssetAmount amount) =>
        (decimal)amount.Units / (decimal)System.Numerics.BigInteger.Pow(10, amount.Asset.Decimals);

    /// <summary>Says that what is chosen changed.</summary>
    private void SelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
