using System.Collections.ObjectModel;
using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// A bar in a filter's histogram (sketch EX2): its height on the track and whether it lies inside the range.
/// </summary>
/// <param name="Height">The bar's height in pixels; 0 for an empty slice.</param>
/// <param name="Inside">Whether the slice lies inside what is let through.</param>
public sealed record HistogramBar(double Height, bool Inside);

/// <summary>A counterparty group's chip in the filter (sketch EX2, <em>Motpart</em>), on or off.</summary>
public sealed partial class GroupChip : ObservableObject
{
    /// <summary>What happens when it is pressed.</summary>
    private readonly Action<GroupChip> _toggle;

    /// <summary>Creates a chip.</summary>
    /// <param name="group">The group.</param>
    /// <param name="toggle">What a press does.</param>
    public GroupChip(CounterpartyGroup group, Action<GroupChip> toggle)
    {
        Group = group;
        _toggle = toggle;
    }

    /// <summary>Gets the group.</summary>
    public CounterpartyGroup Group { get; }

    /// <summary>Gets the group's name.</summary>
    public string Name => HeldBackChoice.NameOf(Group);

    /// <summary>Gets the group's colour.</summary>
    public GraphInk Ink => HeldBackChoice.InkOf(Group);

    /// <summary>Gets or sets whether the group is shown.</summary>
    [ObservableProperty]
    public partial bool IsOn { get; set; } = true;

    /// <summary>Turns the group on or off; the last one on stays on.</summary>
    [RelayCommand]
    private void Toggle() => _toggle(this);
}

/// <summary>A line in a dot's tip (sketch EX2): why it is marked, in its colour.</summary>
/// <param name="Text">The line.</param>
/// <param name="Ink">Its colour.</param>
public sealed record TipLine(string Text, GraphInk Ink);

/// <summary>
/// The selection view of what an expansion held back (sketches EX1a, EX1b and EX2): the transactions as dots around the
/// address, on a spiral or a time arc, with the filters (amount with its distribution, direction, a time window
/// around the traced arrival, the counterparty's group, dust, those already in the graph, grouping per
/// counterparty), the marks (sonar for a negative label and for an amount near the traced one, a ring for one
/// already in the graph, a diamond for a label from another case) and the choosing (click, Shift-click a run, lasso,
/// the ten largest, the pulsing). <em>Plocka ut</em> brings the chosen into the graph. Without WPF, so that it is
/// tested in Chainsaw.App.Tests.
/// </summary>
public sealed partial class HeldBackViewModel : ObservableObject
{
    /// <summary>The lowest amount on the slider's logarithmic scale: 10⁻⁶ BTC.</summary>
    public const double LowestPower = -6;

    /// <summary>The highest amount on the slider's logarithmic scale: 10 BTC.</summary>
    public const double HighestPower = 1;

    /// <summary>What counts as dust (<em>Dölj damm</em>): under 0,0001 BTC, often address poisoning.</summary>
    public const double DustBtc = 0.0001;

    /// <summary>The window's hours around its centre in the default choice, ±48 h.</summary>
    public const int TwoDays = 48;

    /// <summary>The window's hours around its centre in the week's choice, ±7 d.</summary>
    public const int OneWeek = 168;

    /// <summary>How many slices the amount's histogram has.</summary>
    private const int AmountSlices = 26;

    /// <summary>How many slices the time's histogram has.</summary>
    private const int TimeSlices = 44;

    /// <summary>The tallest bar of the amount's histogram, and the shortest of one that is not empty.</summary>
    private const double AmountBarTallest = 22;

    /// <summary>The tallest bar of the time's histogram.</summary>
    private const double TimeBarTallest = 26;

    /// <summary>The narrowest gap between the slider's two ends.</summary>
    private const double SliderGap = 0.04;

    /// <summary>How many the largest choose.</summary>
    private const int Largest = 10;

    /// <summary>The chosen transactions' ids.</summary>
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);

    /// <summary>The time zone dates are written in.</summary>
    private readonly TimeZoneInfo _zone;

    /// <summary>The whole span of the history, padded, which the time slider covers.</summary>
    private readonly (DateTimeOffset From, DateTimeOffset To) _span;

    /// <summary>When the view was opened: where a transaction not confirmed yet lies in time.</summary>
    private readonly DateTimeOffset _now;

    /// <summary>The transactions the filters let through, newest first.</summary>
    private List<HeldBackChoice> _visible = [];

    /// <summary>The dot a Shift-click runs from, or −1.</summary>
    private int _runFrom = -1;

    /// <summary>
    /// Whether the filters are being set from code, so that each change does not lay the dots out again.
    /// </summary>
    private bool _resetting;

    /// <summary>Creates the view for what was held back.</summary>
    /// <param name="choices">The choices and the traced arrival.</param>
    /// <param name="zone">The time zone dates are written in; the computer's when not given.</param>
    /// <param name="now">
    /// When it is now, where a transaction not confirmed yet lies (inside every window that reaches the present);
    /// the computer's clock when not given.
    /// </param>
    public HeldBackViewModel(HeldBackChoices choices, TimeZoneInfo? zone = null, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(choices);
        Choices = choices;
        _zone = zone ?? TimeZoneInfo.Local;
        _now = now ?? DateTimeOffset.UtcNow;
        var times = choices.Choices.Select(TimeOf).ToList();
        var newest = times.Count == 0 ? DateTimeOffset.UnixEpoch : times.Max();
        var oldest = times.Count == 0 ? newest : times.Min();
        _span = (oldest - TimeSpan.FromDays(4), newest + TimeSpan.FromDays(2));
        Groups = new ObservableCollection<GroupChip>(Enum.GetValues<CounterpartyGroup>()
            .Select(group => new GroupChip(group, ToggleGroup)));
        foreach (var chip in Groups)
        {
            chip.PropertyChanged += (_, _) => Refilter();
        }

        ResetFilters();
    }

    /// <summary>
    /// Raised when the dots, their places, marks or what is chosen change, so that the surface draws again.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the chosen transactions are to be brought into the graph.</summary>
    public event EventHandler<IReadOnlyList<string>>? TakeWanted;

    /// <summary>Raised when the view is to close without bringing anything in.</summary>
    public event EventHandler? CloseWanted;

    /// <summary>Gets the choices and the traced arrival.</summary>
    public HeldBackChoices Choices { get; }

    /// <summary>Gets the counterparty groups' chips.</summary>
    public ObservableCollection<GroupChip> Groups { get; }

    /// <summary>Gets or sets the mode: the spiral or the time arc.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSpiral), nameof(IsTimeArc))]
    public partial HeldBackMode Mode { get; set; }

    /// <summary>Gets or sets the spiral's order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsByTime), nameof(IsByAmount))]
    public partial HeldBackOrder Order { get; set; }

    /// <summary>Gets or sets how a press chooses.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClick), nameof(IsLasso))]
    public partial HeldBackTool Tool { get; set; }

    /// <summary>Gets or sets which side's transactions are shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIn), nameof(IsOut), nameof(IsAll))]
    public partial HeldBackDirection Direction { get; set; }

    /// <summary>Gets or sets the slider's low end, from 0 to 1 on the logarithmic scale.</summary>
    [ObservableProperty]
    public partial double Low { get; set; }

    /// <summary>Gets or sets the slider's high end, from 0 to 1 on the logarithmic scale.</summary>
    [ObservableProperty]
    public partial double High { get; set; } = 1;

    /// <summary>Gets or sets the time window's hours on each side of its centre; 0 for the whole history.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTwoDays), nameof(IsOneWeek), nameof(IsWholeTime))]
    public partial int WindowHours { get; set; }

    /// <summary>Gets or sets the time window's centre.</summary>
    [ObservableProperty]
    public partial DateTimeOffset WindowAt { get; set; }

    /// <summary>Gets or sets whether dust is hidden.</summary>
    [ObservableProperty]
    public partial bool HideDust { get; set; }

    /// <summary>Gets or sets whether only counterparties not yet in the graph are shown.</summary>
    [ObservableProperty]
    public partial bool OnlyNotInGraph { get; set; }

    /// <summary>Gets or sets whether a counterparty's transactions are one dot.</summary>
    [ObservableProperty]
    public partial bool GroupByCounterparty { get; set; }

    /// <summary>Gets the dot under the pointer, or −1.</summary>
    [ObservableProperty]
    public partial int Hovered { get; private set; } = -1;

    /// <summary>Gets whether the dots lie on the spiral.</summary>
    public bool IsSpiral => Mode == HeldBackMode.Spiral;

    /// <summary>Gets whether the dots lie on the time arc.</summary>
    public bool IsTimeArc => Mode == HeldBackMode.TimeArc;

    /// <summary>Gets whether the spiral is in time order.</summary>
    public bool IsByTime => Order == HeldBackOrder.Time;

    /// <summary>Gets whether the spiral is in amount order.</summary>
    public bool IsByAmount => Order == HeldBackOrder.Amount;

    /// <summary>Gets whether a click chooses.</summary>
    public bool IsClick => Tool == HeldBackTool.Click;

    /// <summary>Gets whether a drawn ring chooses.</summary>
    public bool IsLasso => Tool == HeldBackTool.Lasso;

    /// <summary>Gets whether only what came in is shown.</summary>
    public bool IsIn => Direction == HeldBackDirection.In;

    /// <summary>Gets whether only what went out is shown.</summary>
    public bool IsOut => Direction == HeldBackDirection.Out;

    /// <summary>Gets whether both sides are shown.</summary>
    public bool IsAll => Direction == HeldBackDirection.All;

    /// <summary>Gets whether the window is ±48 h.</summary>
    public bool IsTwoDays => WindowHours == TwoDays;

    /// <summary>Gets whether the window is ±7 d.</summary>
    public bool IsOneWeek => WindowHours == OneWeek;

    /// <summary>Gets whether the whole history is shown.</summary>
    public bool IsWholeTime => WindowHours == 0;

    /// <summary>Gets the dots shown, in the layout's order.</summary>
    public IReadOnlyList<HeldBackDot> Dots { get; private set; } = [];

    /// <summary>Gets the guides the dots lie on.</summary>
    public HeldBackGuides Guides { get; private set; } = HeldBackGuides.None;

    /// <summary>Gets the chosen transactions' ids.</summary>
    public IReadOnlySet<string> Selected => _selected;

    /// <summary>Gets whether any transaction is chosen.</summary>
    public bool HasSelection => _selected.Count > 0;

    /// <summary>Gets whether any dot pulses, so that <em>Välj pulserande</em> has something to choose.</summary>
    public bool HasPulsing => Dots.Any(dot => dot.Pulses);

    /// <summary>Gets the amount's histogram.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<HistogramBar> AmountBars { get; private set; } = [];

    /// <summary>Gets the time's histogram.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<HistogramBar> TimeBars { get; private set; } = [];

    /// <summary>Gets the window's start on the time slider, from 0 to 1.</summary>
    [ObservableProperty]
    public partial double WindowStart { get; private set; }

    /// <summary>Gets the window's end on the time slider, from 0 to 1.</summary>
    [ObservableProperty]
    public partial double WindowEnd { get; private set; } = 1;

    /// <summary>Gets the traced arrival on the time slider, from 0 to 1, or −1 when there is none.</summary>
    public double ArrivalShare => Choices.Arrival is { } arrival ? SpanShare(arrival) : -1;

    /// <summary>Gets the traced arrival's tip.</summary>
    public string ArrivalTip => Choices.Traced is { } traced
        ? "Den spårade ankomsten, " + HeldBackLayout.Btc(traced.Value / 1e8) + " BTC"
        : string.Empty;

    /// <summary>Gets the line at the top: the address, how many were held back and how many are shown.</summary>
    [ObservableProperty]
    public partial string HeaderText { get; private set; } = string.Empty;

    /// <summary>Gets the filter's counter, for example <c>42 av 243</c>.</summary>
    [ObservableProperty]
    public partial string FilterCountText { get; private set; } = string.Empty;

    /// <summary>Gets the amount's words, <c>Alla belopp</c> or the range.</summary>
    [ObservableProperty]
    public partial string AmountText { get; private set; } = string.Empty;

    /// <summary>Gets the window's words, <c>Hela tiden</c> or its ends.</summary>
    [ObservableProperty]
    public partial string TimeText { get; private set; } = string.Empty;

    /// <summary>Gets the bottom bar's words: what is shown, or what is chosen.</summary>
    [ObservableProperty]
    public partial string CountText { get; private set; } = string.Empty;

    /// <summary>Gets the main button's words, <c>Plocka ut 12 · Enter</c>.</summary>
    [ObservableProperty]
    public partial string TakeText { get; private set; } = string.Empty;

    /// <summary>Gets the address card's second line: how many were held back.</summary>
    public string CentreText => string.Create(
        CultureInfo.InvariantCulture, $"{Choices.Choices.Count} tx hölls tillbaka");

    /// <summary>Gives the dot within reach of a point, the nearest.</summary>
    /// <param name="point">The point, relative to the address's centre.</param>
    /// <param name="reach">How far past a dot's edge still counts.</param>
    /// <returns>The dot's index in <see cref="Dots"/>, or −1.</returns>
    public int DotAt(GraphPoint point, double reach = 5)
    {
        var best = -1;
        var bestDistance = double.MaxValue;
        for (var index = 0; index < Dots.Count; index++)
        {
            var distance = Dots[index].At.DistanceTo(point);
            if (distance < (Dots[index].Size / 2) + reach && distance < bestDistance)
            {
                best = index;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Says the dot under the pointer, so that it grows and shows its tip.</summary>
    /// <param name="index">The dot, or −1.</param>
    public void Hover(int index)
    {
        if (index != Hovered)
        {
            Hovered = index;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Says whether all of a dot's transactions are chosen.</summary>
    /// <param name="dot">The dot.</param>
    /// <returns><see langword="true"/> when they are.</returns>
    public bool IsChosen(HeldBackDot dot)
    {
        ArgumentNullException.ThrowIfNull(dot);
        return dot.Choices.All(choice => _selected.Contains(choice.Key));
    }

    /// <summary>
    /// Takes a click on a dot: it is chosen or let go, or, with Shift, every dot from the last clicked to it is chosen.
    /// </summary>
    /// <param name="index">The dot.</param>
    /// <param name="run">Whether Shift is held.</param>
    public void Click(int index, bool run)
    {
        if (index < 0 || index >= Dots.Count)
        {
            return;
        }

        if (run && _runFrom >= 0 && _runFrom < Dots.Count)
        {
            var (from, to) = (Math.Min(_runFrom, index), Math.Max(_runFrom, index));
            Choose(Dots.Skip(from).Take(to - from + 1));
        }
        else if (IsChosen(Dots[index]))
        {
            _selected.ExceptWith(Dots[index].Choices.Select(choice => choice.Key));
        }
        else
        {
            Choose([Dots[index]]);
        }

        _runFrom = index;
        SelectionChanged();
    }

    /// <summary>Chooses the dots inside a drawn ring.</summary>
    /// <param name="ring">The ring's points, relative to the address's centre.</param>
    public void Lasso(IReadOnlyList<GraphPoint> ring)
    {
        ArgumentNullException.ThrowIfNull(ring);
        if (ring.Count < 3)
        {
            return;
        }

        Choose(Dots.Where(dot => Inside(dot.At, ring)));
        SelectionChanged();
    }

    /// <summary>Gives a dot's tip: what moved, when, and why it is marked.</summary>
    /// <param name="dot">The dot.</param>
    /// <returns>The head, the second line and the marks' lines.</returns>
    public (string Head, string Sub, IReadOnlyList<TipLine> Lines) TipOf(HeldBackDot dot)
    {
        ArgumentNullException.ThrowIfNull(dot);
        var first = dot.First;
        var head = string.Create(
            CultureInfo.InvariantCulture, $"{HeadWords(dot)} · {HeldBackLayout.Btc(dot.Btc)} BTC");
        var when = dot.Time is { } time
            ? time.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"
            : "obekräftad";
        var sub = dot.Count > 1
            ? string.Create(CultureInfo.InvariantCulture, $"{dot.Count} tx · senast {when}")
            : SubjectText.Fit(first.Key, 0) + " · " + when;
        var lines = new List<TipLine>();
        if (dot.Choices.FirstOrDefault(choice => choice.IsNegative)?.NegativeName is { } negative)
        {
            lines.Add(new TipLine(negative + ": negativ etikett", GraphInk.Risk));
        }

        if (dot.IsUncertain)
        {
            lines.Add(new TipLine(SharedText, GraphInk.Unknown));
        }

        if (dot.IsNear && Choices.Traced is { } traced)
        {
            lines.Add(new TipLine(NearWords(dot, traced), GraphInk.Accent));
        }

        if (first.InGraph)
        {
            lines.Add(new TipLine("Motparten finns redan i grafen", GraphInk.Cluster));
        }

        if (first.FromOtherCase)
        {
            lines.Add(new TipLine("Etikett från ett annat ärende", GraphInk.Cluster));
        }

        return (head, sub, lines);
    }

    /// <summary>
    /// The tip's line for an amount that is the address's share of a transaction others paid into too (principle 2).
    /// </summary>
    public const string SharedText = "Flera betalade in: adressens andel, osäkert";

    /// <summary>
    /// Writes the tip's head before the amount: a transfer to itself, the one counterparty, or how many there are
    /// when there are several, never one of them as if it were all.
    /// </summary>
    /// <param name="dot">The dot.</param>
    /// <returns>For example <c>Ut till bc1q…x0wl</c>, <c>Ut till 3 mottagare</c> or <c>Till sig själv</c>.</returns>
    private static string HeadWords(HeldBackDot dot)
    {
        var first = dot.First;
        if (first.IsSelf)
        {
            return "Till sig själv";
        }

        var direction = dot.IsOut ? "Ut till" : "In från";
        var parties = dot.Count == 1 ? first.Transaction.Counterparties : 1;
        if (parties <= 1)
        {
            return direction + " " + first.Name;
        }

        var many = dot.IsOut ? "mottagare" : "avsändare";
        return string.Create(CultureInfo.InvariantCulture, $"{direction} {parties} {many}");
    }

    /// <summary>
    /// Writes why a dot is near the traced amount: the two amounts and the difference between them, and, for one
    /// transaction, its fee as the chain has it (the difference is worked out, the fee is a fact).
    /// </summary>
    /// <param name="dot">The dot.</param>
    /// <param name="traced">What was traced in.</param>
    /// <returns>The line.</returns>
    private static string NearWords(HeldBackDot dot, Satoshi traced)
    {
        var tracedBtc = traced.Value / (double)Satoshi.PerBitcoin;
        var line = $"Nära det spårade: {HeldBackLayout.Btc(dot.Btc)} av {HeldBackLayout.Btc(tracedBtc)} BTC, "
            + "skillnad " + HeldBackLayout.Btc(Math.Max(0, tracedBtc - dot.Btc));
        if (dot.Count > 1)
        {
            return line;
        }

        var fee = dot.First.Transaction.Fee.Value / (double)Satoshi.PerBitcoin;
        return line + " · avgift " + HeldBackLayout.Btc(fee);
    }

    /// <summary>Shows the spiral.</summary>
    [RelayCommand]
    private void ShowSpiral() => Mode = HeldBackMode.Spiral;

    /// <summary>Shows the time arc.</summary>
    [RelayCommand]
    private void ShowTimeArc() => Mode = HeldBackMode.TimeArc;

    /// <summary>Orders the spiral by time, the newest innermost.</summary>
    [RelayCommand]
    private void ByTime() => Order = HeldBackOrder.Time;

    /// <summary>Orders the spiral by amount, the largest innermost.</summary>
    [RelayCommand]
    private void ByAmount() => Order = HeldBackOrder.Amount;

    /// <summary>Chooses by click.</summary>
    [RelayCommand]
    private void UseClick() => Tool = HeldBackTool.Click;

    /// <summary>Chooses by lasso (L).</summary>
    [RelayCommand]
    public void UseLasso() => Tool = HeldBackTool.Lasso;

    /// <summary>Shows what came in.</summary>
    [RelayCommand]
    private void ShowIn() => Direction = HeldBackDirection.In;

    /// <summary>Shows what went out.</summary>
    [RelayCommand]
    private void ShowOut() => Direction = HeldBackDirection.Out;

    /// <summary>Shows both sides.</summary>
    [RelayCommand]
    private void ShowAll() => Direction = HeldBackDirection.All;

    /// <summary>Sets the window to ±48 h, around the traced arrival when it was the whole history before.</summary>
    [RelayCommand]
    private void TwoDaysWindow() => SetWindow(TwoDays);

    /// <summary>Sets the window to ±7 d.</summary>
    [RelayCommand]
    private void OneWeekWindow() => SetWindow(OneWeek);

    /// <summary>Shows the whole history.</summary>
    [RelayCommand]
    private void WholeTime() => SetWindow(0);

    /// <summary>Chooses the ten largest shown, besides those chosen.</summary>
    [RelayCommand]
    private void ChooseLargest()
    {
        Choose(Dots.OrderByDescending(dot => dot.Btc).Take(Largest));
        SelectionChanged();
    }

    /// <summary>Chooses every dot that pulses, besides those chosen.</summary>
    [RelayCommand]
    private void ChoosePulsing()
    {
        Choose(Dots.Where(dot => dot.Pulses));
        SelectionChanged();
    }

    /// <summary>Lets every chosen go.</summary>
    [RelayCommand]
    private void ClearChoice()
    {
        _selected.Clear();
        _runFrom = -1;
        SelectionChanged();
    }

    /// <summary>Sets the filters back to their defaults; the mode, the order and the tool stay.</summary>
    [RelayCommand]
    private void Reset() => ResetFilters();

    /// <summary>Brings the chosen into the graph (<em>Plocka ut</em>, Enter).</summary>
    [RelayCommand]
    public void Take()
    {
        if (_selected.Count > 0)
        {
            TakeWanted?.Invoke(this, [.. _selected.Order(StringComparer.Ordinal)]);
        }
    }

    /// <summary>Closes the view without bringing anything in (<em>Avbryt</em>, Esc).</summary>
    [RelayCommand]
    public void Cancel() => CloseWanted?.Invoke(this, EventArgs.Empty);

    /// <summary>Lays the dots out again when the mode changes.</summary>
    /// <param name="value">The mode.</param>
    partial void OnModeChanged(HeldBackMode value) => Relayout();

    /// <summary>Lays the dots out again when the order changes.</summary>
    /// <param name="value">The order.</param>
    partial void OnOrderChanged(HeldBackOrder value) => Relayout();

    /// <summary>Filters again when the direction changes.</summary>
    /// <param name="value">The direction.</param>
    partial void OnDirectionChanged(HeldBackDirection value) => Refilter();

    /// <summary>Keeps the low end under the high, and filters again.</summary>
    /// <param name="value">The low end.</param>
    partial void OnLowChanged(double value)
    {
        if (value > High - SliderGap)
        {
            Low = Math.Max(0, High - SliderGap);
            return;
        }

        Refilter();
    }

    /// <summary>Keeps the high end over the low, and filters again.</summary>
    /// <param name="value">The high end.</param>
    partial void OnHighChanged(double value)
    {
        if (value < Low + SliderGap)
        {
            High = Math.Min(1, Low + SliderGap);
            return;
        }

        Refilter();
    }

    /// <summary>Filters again when the window's size changes.</summary>
    /// <param name="value">The hours.</param>
    partial void OnWindowHoursChanged(int value) => Refilter();

    /// <summary>Filters again when the window moves.</summary>
    /// <param name="value">The centre.</param>
    partial void OnWindowAtChanged(DateTimeOffset value) => Refilter();

    /// <summary>Filters again when dust is hidden or shown.</summary>
    /// <param name="value">Whether it is hidden.</param>
    partial void OnHideDustChanged(bool value) => Refilter();

    /// <summary>Filters again when those in the graph are hidden or shown.</summary>
    /// <param name="value">Whether they are hidden.</param>
    partial void OnOnlyNotInGraphChanged(bool value) => Refilter();

    /// <summary>Lays the dots out again when they are grouped or not.</summary>
    /// <param name="value">Whether they are grouped.</param>
    partial void OnGroupByCounterpartyChanged(bool value) => Refilter();

    /// <summary>
    /// Moves the time window so that its centre lies at a share of the slider (sketch EX2: dragged along the time);
    /// from the whole history it becomes ±48 h.
    /// </summary>
    /// <param name="share">The share, from 0 to 1.</param>
    public void MoveWindowTo(double share)
    {
        var at = _span.From + ((_span.To - _span.From) * Math.Clamp(share, 0, 1));
        _resetting = true;
        if (WindowHours == 0)
        {
            WindowHours = TwoDays;
        }

        _resetting = false;
        WindowAt = at;
    }

    /// <summary>Gives the window's ends.</summary>
    /// <returns>The window, or the padded history when it is the whole.</returns>
    public (DateTimeOffset From, DateTimeOffset To) Window() => WindowHours == 0
        ? _span
        : (WindowAt - TimeSpan.FromHours(WindowHours), WindowAt + TimeSpan.FromHours(WindowHours));

    /// <summary>Gives an amount's place on the slider's logarithmic scale.</summary>
    /// <param name="btc">The amount in bitcoin.</param>
    /// <returns>The share, from 0 to 1.</returns>
    public static double AmountShare(double btc) =>
        Math.Clamp((Math.Log10(Math.Max(btc, 1e-12)) - LowestPower) / (HighestPower - LowestPower), 0, 1);

    /// <summary>Sets the window's size, centred on the traced arrival (or the newest) when it had none.</summary>
    /// <param name="hours">The hours on each side; 0 for the whole history.</param>
    private void SetWindow(int hours)
    {
        _resetting = true;
        if (WindowHours == 0 && hours > 0)
        {
            WindowAt = DefaultCentre();
        }

        _resetting = false;
        WindowHours = hours;
        Refilter();
    }

    /// <summary>Gives where a window centres by default: the traced arrival, else the newest transaction.</summary>
    /// <returns>The time.</returns>
    private DateTimeOffset DefaultCentre() => Choices.Arrival ?? (_span.To - TimeSpan.FromDays(2));

    /// <summary>
    /// Sets every filter to its default (sketch EX2): all amounts and sides, ±48 h around the traced arrival (the
    /// whole history without one), every group, dust hidden, nothing else.
    /// </summary>
    private void ResetFilters()
    {
        _resetting = true;
        Low = 0;
        High = 1;
        Direction = HeldBackDirection.All;
        WindowAt = DefaultCentre();
        WindowHours = Choices.Arrival is null ? 0 : TwoDays;
        foreach (var chip in Groups)
        {
            chip.IsOn = true;
        }

        HideDust = true;
        OnlyNotInGraph = false;
        GroupByCounterparty = false;
        _resetting = false;
        Refilter();
    }

    /// <summary>Turns a group on or off, never the last one on.</summary>
    /// <param name="chip">The group's chip.</param>
    private void ToggleGroup(GroupChip chip)
    {
        if (chip.IsOn && Groups.Count(other => other.IsOn) == 1)
        {
            return;
        }

        chip.IsOn = !chip.IsOn;
    }

    /// <summary>Says whether a transaction passes the filters.</summary>
    /// <param name="choice">The transaction.</param>
    /// <param name="window">The time window.</param>
    /// <returns><see langword="true"/> when it is shown.</returns>
    private bool Passes(HeldBackChoice choice, (DateTimeOffset From, DateTimeOffset To) window)
    {
        var sideShown = Direction == HeldBackDirection.All || (Direction == HeldBackDirection.Out) == choice.IsOut;
        var share = AmountShare(choice.Btc);
        var time = TimeOf(choice);
        var inWindow = WindowHours == 0 || (time >= window.From && time <= window.To);
        return sideShown
            && Groups.First(chip => chip.Group == choice.Group).IsOn
            && share >= Low - 1e-9 && share <= High + 1e-9
            && inWindow
            && !(HideDust && choice.Btc < DustBtc)
            && !(OnlyNotInGraph && choice.InGraph);
    }

    /// <summary>Works out what is shown, the histograms and the words, and lays the dots out.</summary>
    private void Refilter()
    {
        if (_resetting)
        {
            return;
        }

        var window = Window();
        var shown = Choices.Choices.Where(choice => Passes(choice, window)).ToList();
        AmountBars = Histogram(
            Choices.Choices.Select(choice => AmountShare(choice.Btc)), AmountSlices, Low, High, AmountBarTallest, 3);
        var (start, end) = (SpanShare(window.From), SpanShare(window.To));
        WindowStart = WindowHours == 0 ? 0 : start;
        WindowEnd = WindowHours == 0 ? 1 : end;
        TimeBars = Histogram(
            Choices.Choices.Select(TimeOf).Select(SpanShare),
            TimeSlices, WindowStart, WindowEnd, TimeBarTallest, 2);
        AmountText = AmountWords();
        TimeText = WindowHours == 0
            ? "Hela tiden"
            : HeldBackLayout.Day(window.From, _zone, true) + " – " + HeldBackLayout.Day(window.To, _zone, true);
        _visible = shown;
        Relayout();
    }

    /// <summary>Lays the shown transactions out as dots, with their guides, and says the counts.</summary>
    private void Relayout()
    {
        if (_resetting)
        {
            return;
        }

        var items = GroupByCounterparty
            ? Grouped(_visible)
            : [.. _visible.Select(choice => (IReadOnlyList<HeldBackChoice>)[choice])];
        Dots = Mode == HeldBackMode.Spiral ? OnSpiral(items) : OnArc(items);
        var window = Window();
        Guides = Mode == HeldBackMode.Spiral
            ? HeldBackLayout.SpiralGuides(Dots, Order, window.To - window.From < TimeSpan.FromDays(12), _zone)
            : HeldBackLayout.ArcGuides(window.From, window.To, Choices.Arrival, ArrivalWords(), _zone);
        var total = Choices.Choices.Count;
        var grouped = GroupByCounterparty
            ? string.Create(CultureInfo.InvariantCulture, $" · {Dots.Count} motparter")
            : string.Empty;
        HeaderText = string.Create(
            CultureInfo.InvariantCulture,
            $"{Choices.AnchorName} · {total} tx som hölls tillbaka · visas {_visible.Count} av {total}{grouped}");
        FilterCountText = string.Create(CultureInfo.InvariantCulture, $"{_visible.Count} av {total}");
        Hovered = -1;
        _runFrom = -1;
        SelectionChanged();
    }

    /// <summary>Groups transactions per counterparty and side, the newest group first.</summary>
    /// <param name="shown">The transactions.</param>
    /// <returns>The groups.</returns>
    private static List<IReadOnlyList<HeldBackChoice>> Grouped(List<HeldBackChoice> shown) =>
    [
        .. shown.GroupBy(choice => choice.CounterpartyKey, StringComparer.Ordinal)
            .Select(group => (IReadOnlyList<HeldBackChoice>)[.. group])
            .OrderByDescending(group => group.Max(choice => choice.Transaction.Time ?? DateTimeOffset.MaxValue)),
    ];

    /// <summary>Lays the items on the spiral, in time or amount order.</summary>
    /// <param name="items">The items, newest first.</param>
    /// <returns>The dots.</returns>
    private List<HeldBackDot> OnSpiral(List<IReadOnlyList<HeldBackChoice>> items)
    {
        var ordered = Order == HeldBackOrder.Amount
            ? items.OrderByDescending(Sum).ToList()
            : items;
        return [.. ordered.Select((item, index) => Dot(item, HeldBackLayout.SpiralAt(index)))];
    }

    /// <summary>Lays the items on the time arc: the angle their time, the distance their amount.</summary>
    /// <param name="items">The items.</param>
    /// <returns>The dots, the earliest first.</returns>
    private List<HeldBackDot> OnArc(List<IReadOnlyList<HeldBackChoice>> items)
    {
        var (from, to) = Window();
        return
        [
            .. items.OrderBy(Newest).Select(item => Dot(item, HeldBackLayout.OnArc(
                HeldBackLayout.ArcAngle(Newest(item), from, to), HeldBackLayout.ArcRadius(Sum(item))))),
        ];
    }

    /// <summary>Makes a dot of an item at a place.</summary>
    /// <param name="item">The transactions.</param>
    /// <param name="at">The place.</param>
    /// <returns>The dot.</returns>
    private HeldBackDot Dot(IReadOnlyList<HeldBackChoice> item, GraphPoint at)
    {
        var btc = Sum(item);
        return new HeldBackDot(
            item,
            at,
            HeldBackLayout.DotSize(btc, item.Count),
            btc,
            item.Select(choice => choice.Transaction.Time).Max(),
            item.Any(Choices.IsNear));
    }

    /// <summary>Adds an item's amounts.</summary>
    /// <param name="item">The transactions.</param>
    /// <returns>The sum in bitcoin.</returns>
    private static double Sum(IReadOnlyList<HeldBackChoice> item) => item.Sum(choice => choice.Btc);

    /// <summary>Gives an item's newest time; an unconfirmed one lies at now.</summary>
    /// <param name="item">The transactions.</param>
    /// <returns>The time.</returns>
    private DateTimeOffset Newest(IReadOnlyList<HeldBackChoice> item) => item.Max(TimeOf);

    /// <summary>
    /// Gives when a transaction happened: its block's time, or now while it is not confirmed (principle 9: it is
    /// shown, not lost outside every window).
    /// </summary>
    /// <param name="choice">The transaction.</param>
    /// <returns>The time.</returns>
    private DateTimeOffset TimeOf(HeldBackChoice choice) => choice.Transaction.Time ?? _now;

    /// <summary>Chooses every transaction of some dots.</summary>
    /// <param name="dots">The dots.</param>
    private void Choose(IEnumerable<HeldBackDot> dots) =>
        _selected.UnionWith(dots.SelectMany(dot => dot.Choices).Select(choice => choice.Key));

    /// <summary>Says the bottom bar's words, and that what is chosen changed.</summary>
    private void SelectionChanged()
    {
        _selected.IntersectWith(_visible.Select(choice => choice.Key));
        var chosen = Choices.Choices.Where(choice => _selected.Contains(choice.Key)).ToList();
        var total = Choices.Choices.Count;
        if (chosen.Count == 0)
        {
            CountText = string.Create(CultureInfo.InvariantCulture, $"Visas {_visible.Count} av {total} · inga valda");
            TakeText = "Plocka ut · Enter";
        }
        else
        {
            var parties = chosen.Select(choice => choice.CounterpartyKey).Distinct(StringComparer.Ordinal).Count();
            var sum = HeldBackLayout.Btc(chosen.Sum(choice => choice.Btc));
            CountText = string.Create(
                CultureInfo.InvariantCulture, $"{chosen.Count} valda · {sum} BTC · {parties} motparter");
            TakeText = string.Create(CultureInfo.InvariantCulture, $"Plocka ut {chosen.Count} · Enter");
        }

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasPulsing));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Writes the amount filter's words.</summary>
    /// <returns><c>Alla belopp</c>, or the range with <c>allt</c> at an open end.</returns>
    private string AmountWords()
    {
        if (Low <= 0.001 && High >= 0.999)
        {
            return "Alla belopp";
        }

        var low = Low <= 0.001 ? "allt" : HeldBackLayout.ShortBtc(BtcAt(Low));
        var high = High >= 0.999 ? "allt" : HeldBackLayout.ShortBtc(BtcAt(High));
        return $"{low} – {high} BTC";
    }

    /// <summary>Writes the traced arrival's word on the time arc.</summary>
    /// <returns>For example <c>spårad ankomst · 0,92 BTC</c>.</returns>
    private string ArrivalWords() => Choices.Traced is { } traced
        ? "spårad ankomst · " + HeldBackLayout.ShortBtc(traced.Value / 1e8) + " BTC"
        : "spårad ankomst";

    /// <summary>Gives the amount at a share of the slider.</summary>
    /// <param name="share">The share.</param>
    /// <returns>The amount in bitcoin.</returns>
    private static double BtcAt(double share) => Math.Pow(10, LowestPower + (share * (HighestPower - LowestPower)));

    /// <summary>Gives a time's place on the time slider.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The share, from 0 to 1.</returns>
    private double SpanShare(DateTimeOffset time)
    {
        var span = (_span.To - _span.From).TotalSeconds;
        return span <= 0 ? 0.5 : Math.Clamp((time - _span.From).TotalSeconds / span, 0, 1);
    }

    /// <summary>
    /// Counts values in slices and gives a bar per slice (sketch EX2: the distribution behind the slider).
    /// </summary>
    /// <param name="shares">The values, each from 0 to 1.</param>
    /// <param name="slices">How many slices.</param>
    /// <param name="low">Where what is let through starts.</param>
    /// <param name="high">Where it ends.</param>
    /// <param name="tallest">The tallest bar.</param>
    /// <param name="shortest">The shortest bar that is not empty.</param>
    /// <returns>The bars.</returns>
    private static List<HistogramBar> Histogram(
        IEnumerable<double> shares, int slices, double low, double high, double tallest, double shortest)
    {
        var counts = new int[slices];
        foreach (var share in shares)
        {
            counts[Math.Min(slices - 1, (int)Math.Floor(share * slices))]++;
        }

        var most = Math.Max(1, counts.Max());
        return
        [
            .. counts.Select((count, index) =>
            {
                var middle = (index + 0.5) / slices;
                var height = count == 0 ? 0 : shortest + (tallest * Math.Sqrt(count / (double)most));
                return new HistogramBar(height, middle >= low && middle <= high);
            }),
        ];
    }

    /// <summary>Says whether a point lies inside a ring (even-odd).</summary>
    /// <param name="point">The point.</param>
    /// <param name="ring">The ring.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool Inside(GraphPoint point, IReadOnlyList<GraphPoint> ring)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var (a, b) = (ring[i], ring[j]);
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < ((b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y)) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
