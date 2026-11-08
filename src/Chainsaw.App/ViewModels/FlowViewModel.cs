using System.Globalization;
using System.Numerics;
using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>A row of where the victims' money ended (sketch FL1a, <em>Från offren</em>).</summary>
/// <param name="End">Where.</param>
/// <param name="Share">The share, from 0 to 1.</param>
/// <param name="ToolTip">The amount.</param>
public sealed record FlowEndRow(FlowEnd End, double Share, string ToolTip)
{
    /// <summary>Gets the name.</summary>
    public string Name => FlowReading.NameOf(End);

    /// <summary>Gets the colour.</summary>
    public GraphInk Ink => FlowReading.InkOf(End);

    /// <summary>Gets the share as written.</summary>
    public string Percent => ExposureMap.Percent(Share);
}

/// <summary>A way the chosen money went: the actors as chips, the amount and the transactions behind every step.</summary>
/// <param name="Crumbs">The actors.</param>
/// <param name="Amount">How much went this way.</param>
/// <param name="Transactions">The transactions, shortened.</param>
public sealed record FlowRoute(IReadOnlyList<ExposureCrumb> Crumbs, string Amount, string Transactions);

/// <summary>The chosen band or actor at the bottom of the panel (sketch FL1a).</summary>
/// <param name="Look">The sort of the actor where most of it ended.</param>
/// <param name="Name">Its name.</param>
/// <param name="Line">The amount, for example <c>0,2500 BTC · 263 100 kr</c>.</param>
/// <param name="Percent">The share of the victims' money.</param>
/// <param name="Routes">The largest ways.</param>
/// <param name="More">How many more ways there are, for example <c>+2 vägar</c>, or an empty text.</param>
/// <param name="Basis">What it rests on, as chips.</param>
public sealed record FlowDetail(
    SortLook Look,
    string Name,
    string Line,
    string Percent,
    IReadOnlyList<FlowRoute> Routes,
    string More,
    IReadOnlyList<ExposureBasis> Basis)
{
    /// <summary>Gets whether more ways than shown went there.</summary>
    public bool HasMore => More.Length > 0;
}

/// <summary>
/// The view Flöde (sketch FL1a): the victims to the left and a column per hop, the bands as wide as the money and each
/// split in strands in the colour of where that money ended, what stays as a stub in the accent, the uncertain way
/// striped. The method is chosen in the head and counts the split again; the flow is a judgement (principle 2) with
/// <em>Varför?</em>, counted off the UI thread, and the view writes nothing in the case. No WPF, so that it is tested
/// in Chainsaw.App.Tests.
/// </summary>
public sealed partial class FlowViewModel : ObservableObject
{
    /// <summary>What the view says when none of the victims' money moved in the graph.</summary>
    public const string EmptyText =
        "Inget flöde att visa än. Flödet börjar hos offren: en startpunkt eller en aktör med etiketten offer, som har "
        + "skickat pengar i grafen. Expandera grafen först.";

    /// <summary>What the view says when the flow could not be counted.</summary>
    public const string FailedText = "Flödet kunde inte räknas för den här grafen.";

    /// <summary>How many ways the panel lists.</summary>
    private const int RoutesShown = 3;

    /// <summary>The size the Sankey is laid out for until the view says its own.</summary>
    private static readonly (double Width, double Height) DefaultRoom = (980, 700);

    /// <summary>The UI thread, where a flow counted off it is shown.</summary>
    private readonly IUiThread _ui;

    /// <summary>The graph shown, or <see langword="null"/>.</summary>
    private GraphScene? _scene;

    /// <summary>Whether the graph changed while the view was hidden, so that it is counted when shown.</summary>
    private bool _stale = true;

    /// <summary>Stops the flow being counted, when another is asked for.</summary>
    private CancellationTokenSource? _running;

    /// <summary>The flow counted, or <see langword="null"/>.</summary>
    private FlowResult? _flow;

    /// <summary>The actors by key, as the layout names them.</summary>
    private IReadOnlyDictionary<string, FlowLayout.Actor> _actors = new Dictionary<string, FlowLayout.Actor>();

    /// <summary>The value of a unit per part, in the currency, when every part has one.</summary>
    private IReadOnlyDictionary<FlowLot, decimal>? _values;

    /// <summary>The currency of the values.</summary>
    private string _currency = string.Empty;

    /// <summary>The method chosen in the view, or <see langword="null"/> to follow the graph's.</summary>
    private TracingMethodKind? _method;

    /// <summary>The asset followed.</summary>
    private Asset? _asset;

    /// <summary>Whether the amounts are written in the currency.</summary>
    private bool _inCurrency;

    /// <summary>The size the Sankey is laid out for.</summary>
    private (double Width, double Height) _room = DefaultRoom;

    /// <summary>Creates the view.</summary>
    /// <param name="ui">The UI thread.</param>
    public FlowViewModel(IUiThread ui) => _ui = ui;

    /// <summary>Raised when <em>Visa vägen i grafen</em> asks for the graph with the way lit.</summary>
    public event EventHandler<ExposurePath>? PathWanted;

    /// <summary>Raised when <em>Varför?</em> or the method's <em>?</em> asks for the Guide's panel.</summary>
    public event EventHandler<GuideRequest>? GuideWanted;

    /// <summary>Gets the flow being counted off the UI thread, or the last; complete when none runs.</summary>
    public Task Computed { get; private set; } = Task.CompletedTask;

    /// <summary>Gets or sets whether the view is shown; it counts only while it is (principle 6).</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; }

    /// <summary>Gets why nothing could be counted, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string Problem { get; private set; } = string.Empty;

    /// <summary>Gets whether something could not be counted.</summary>
    public bool HasProblem => Problem.Length > 0;

    /// <summary>Gets whether any of the victims' money is drawn.</summary>
    [ObservableProperty]
    public partial bool HasFlow { get; private set; }

    /// <summary>Gets the methods to choose between: Tracing's own.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ExposureChoice> Methods { get; private set; } = [];

    /// <summary>Gets the assets to follow, shown only when there is more than one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAssets))]
    public partial IReadOnlyList<ExposureChoice> Assets { get; private set; } = [];

    /// <summary>Gets whether more than one asset can be followed.</summary>
    public bool HasAssets => Assets.Count > 1;

    /// <summary>Gets the units to write in: the asset and, when every part has a value, the currency.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnits))]
    public partial IReadOnlyList<ExposureChoice> Units { get; private set; } = [];

    /// <summary>Gets whether the amounts can be written in the currency too.</summary>
    public bool HasUnits => Units.Count > 1;

    /// <summary>Gets the Sankey laid out.</summary>
    [ObservableProperty]
    public partial FlowPicture Picture { get; private set; } = FlowPicture.None;

    /// <summary>Gets the chosen step or actor's key, or <see langword="null"/>.</summary>
    [ObservableProperty]
    public partial string? SelectedKey { get; private set; }

    /// <summary>Gets the parts that run through the choice, drawn clear while the rest dims.</summary>
    [ObservableProperty]
    public partial IReadOnlySet<FlowLot> Lit { get; private set; } = new HashSet<FlowLot>();

    /// <summary>Gets the victims' money in all, for the panel's head.</summary>
    [ObservableProperty]
    public partial string TotalText { get; private set; } = string.Empty;

    /// <summary>Gets the unit of the total.</summary>
    [ObservableProperty]
    public partial string UnitText { get; private set; } = string.Empty;

    /// <summary>Gets where the victims' money ended, per colour.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<FlowEndRow> Ends { get; private set; } = [];

    /// <summary>Gets what the picture leaves out or merges, said honestly, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string Note { get; private set; } = string.Empty;

    /// <summary>Gets whether something is left out or merged.</summary>
    public bool HasNote => Note.Length > 0;

    /// <summary>Gets whether a way rests on something uncertain, so that the striped key stands in the head.</summary>
    [ObservableProperty]
    public partial bool HasGuess { get; private set; }

    /// <summary>Gets the chosen band or actor, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    [NotifyCanExecuteChangedFor(nameof(ShowPathCommand), nameof(AskWhyCommand))]
    public partial FlowDetail? Detail { get; private set; }

    /// <summary>Gets whether something is chosen.</summary>
    public bool HasDetail => Detail is not null;

    /// <summary>Gets the method counted with.</summary>
    private TracingMethodKind Method => _method ?? _scene?.Snapshot.Method ?? TracingMethodKind.Fifo;

    /// <summary>Gets the parts that run through the choice.</summary>
    private List<FlowLot> Chosen => _flow is null || SelectedKey is null ? [] : [.. _flow.Lots.Where(Covers)];

    /// <summary>
    /// Follows the graph shown. While the view is hidden the graph is only kept and the view marked stale.
    /// </summary>
    /// <param name="scene">The graph, or <see langword="null"/>.</param>
    public void Update(GraphScene? scene)
    {
        _scene = scene;
        _stale = true;
        if (IsShown)
        {
            Refresh();
        }
    }

    /// <summary>Lays the Sankey out for the room the view gives it.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    public void Resize(double width, double height)
    {
        if (width > 0 && height > 0 && (Math.Abs(width - _room.Width) >= 1 || Math.Abs(height - _room.Height) >= 1))
        {
            _room = (width, height);
            Lay();
        }
    }

    /// <summary>Counts again with another method (FIFO or haircut).</summary>
    /// <param name="value">The method, as its choice's value.</param>
    [RelayCommand]
    public void SetMethod(string? value)
    {
        if (Enum.TryParse<TracingMethodKind>(value, out var method) && method != Method)
        {
            _method = method;
            Refresh();
        }
    }

    /// <summary>Follows another asset.</summary>
    /// <param name="symbol">The asset's symbol.</param>
    [RelayCommand]
    public void PickAsset(string? symbol)
    {
        var asset = _scene is null ? null : FlowReading.Assets(_scene).FirstOrDefault(each => each.Symbol == symbol);
        if (asset is not null && asset != _asset)
        {
            _asset = asset;
            SelectedKey = null;
            Refresh();
        }
    }

    /// <summary>Writes the amounts in the asset or in the currency.</summary>
    /// <param name="value">The unit's choice value.</param>
    [RelayCommand]
    public void SetUnit(string? value)
    {
        var inCurrency = value == _currency && _values is not null;
        if (inCurrency != _inCurrency)
        {
            _inCurrency = inCurrency;
            Lay();
        }
    }

    /// <summary>Chooses a band (<c>from&gt;to</c>) or an actor.</summary>
    /// <param name="key">The key.</param>
    [RelayCommand]
    public void Pick(string? key)
    {
        if (key is null || _flow is null)
        {
            return;
        }

        SelectedKey = key;
        if (Chosen.Count == 0)
        {
            SelectedKey = null;
        }

        Choose();
    }

    /// <summary><em>Visa vägen i grafen</em> (G): the graph with the largest chosen way lit.</summary>
    [RelayCommand(CanExecute = nameof(HasDetail))]
    public void ShowPath()
    {
        if (_flow is not { } flow || Chosen.FirstOrDefault() is not { } lot)
        {
            return;
        }

        var edges = lot.Path.Skip(1)
            .Select((to, index) => flow.Steps.GetValueOrDefault(FlowResult.StepKey(lot.Path[index], to))?.EdgeKey)
            .OfType<string>()
            .ToList();
        var names = string.Join(" → ", lot.Path.Select(NameOf));
        PathWanted?.Invoke(this, new ExposurePath(lot.Path, edges, $"{names} · {flow.Method.Name}"));
    }

    /// <summary><em>Varför?</em>: the Guide's panel with how the split is counted.</summary>
    [RelayCommand(CanExecute = nameof(HasDetail))]
    public void AskWhy()
    {
        if (_flow is not { } flow || Detail is not { } detail)
        {
            return;
        }

        var lines = new List<WhyLine>
        {
            new(Explain(flow, Chosen), GuideTopic.Flow),
            new($"Spårningsmetoden är {flow.Method}.", GuideTopic.Methods),
        };
        var context = new GuideContext("Varför? · " + detail.Name, lines);
        GuideWanted?.Invoke(this, new GuideRequest(GuideTopic.Flow, null, context, SelectedKey) { IsChoice = true });
    }

    /// <summary>The method's <em>?</em>: the Guide's section on the tracing methods.</summary>
    [RelayCommand]
    public void AskMethods() => GuideWanted?.Invoke(this, new GuideRequest(GuideTopic.Methods, null, null, null));

    /// <summary>Writes why the chosen money is split as it is.</summary>
    /// <param name="flow">The flow.</param>
    /// <param name="chosen">The parts chosen.</param>
    /// <returns>The text.</returns>
    public static string Explain(FlowResult flow, IReadOnlyList<FlowLot> chosen)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(chosen);
        var victims = chosen.Where(lot => lot.IsVictim).Aggregate(BigInteger.Zero, (sum, lot) => sum + lot.Units);
        var amount = AmountText.WithUnit(new AssetAmount(flow.Asset, victims));
        var text = $"{amount} av offrens pengar går här, räknat med {FlowResult.Analysis} och {flow.Method}. "
            + "Varje aktör läses som en plånbok: pengarna lämnar den i tidsordning så som metoden säger, och varje del "
            + "behåller sin väg så att den kan färgas efter var den hamnade. Det är en bedömning, inte ett faktum på "
            + "kedjan; tx:erna i varje steg är fakta.";
        if (chosen.Any(lot => lot.IsGuess))
        {
            text += " En del av vägen bygger på något osäkert, till exempel en godkänd trolig utgång ur en mixer, och "
                + "är randig och streckad.";
        }

        return text;
    }

    /// <summary>Counts a stale view when it is shown.</summary>
    /// <param name="value">Whether it is shown.</param>
    partial void OnIsShownChanged(bool value)
    {
        if (value && _stale)
        {
            Refresh();
        }
    }

    /// <summary>Counts the flow off the UI thread (principle 6), the last asked for winning.</summary>
    private void Refresh()
    {
        _stale = false;
        _running?.Cancel();
        Methods = [.. Enum.GetValues<TracingMethodKind>().Select(MethodChoice)];
        if (_scene is not { } scene || !IsShown)
        {
            _stale = _scene is not null;
            Apply(null, string.Empty);
            return;
        }

        var assets = FlowReading.Assets(scene);
        if (_asset is null || !assets.Contains(_asset))
        {
            _asset = assets.FirstOrDefault();
        }

        Assets = [.. assets.Select(each => new ExposureChoice(each.Symbol, "Följ " + each.Symbol, each == _asset,
            each.Symbol))];
        if (_asset is not { } asset)
        {
            Apply(null, string.Empty);
            return;
        }

        var running = new CancellationTokenSource();
        _running = running;
        Computed = ReadAsync(scene, asset, Method, running.Token);
    }

    /// <summary>Counts the flow off the UI thread and shows it, unless another was asked for.</summary>
    /// <param name="scene">The graph.</param>
    /// <param name="asset">The asset.</param>
    /// <param name="method">The method.</param>
    /// <param name="cancellation">Stops it when another is asked for.</param>
    /// <returns>The counting.</returns>
    private async Task ReadAsync(GraphScene scene, Asset asset, TracingMethodKind method, CancellationToken cancellation)
    {
        try
        {
            var flow = await Task.Run(() => FlowReading.Read(scene, asset, method), cancellation).ConfigureAwait(false);
            if (!cancellation.IsCancellationRequested)
            {
                _ui.Post(() =>
                {
                    if (!cancellation.IsCancellationRequested)
                    {
                        Apply(flow, string.Empty);
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Another flow was asked for; it shows its own.
        }
        catch (InvalidOperationException)
        {
            if (!cancellation.IsCancellationRequested)
            {
                _ui.Post(() => Apply(null, FailedText));
            }
        }
    }

    /// <summary>Shows a flow: the actors' names, the values, and the choice kept where it can be.</summary>
    /// <param name="flow">The flow, or <see langword="null"/>.</param>
    /// <param name="problem">Why nothing could be counted, or an empty text.</param>
    private void Apply(FlowResult? flow, string problem)
    {
        _flow = flow;
        Problem = problem;
        Methods = [.. Enum.GetValues<TracingMethodKind>().Select(MethodChoice)];
        _actors = _scene is { } scene ? ActorsOf(scene) : new Dictionary<string, FlowLayout.Actor>();
        (_values, _currency) = _scene is { } shown && flow is not null ? Values(shown, flow) : (null, string.Empty);
        _inCurrency &= _values is not null;
        Units = _values is null || flow is null
            ? []
            : [UnitChoice(flow.Asset.Symbol, !_inCurrency), UnitChoice(_currency, _inCurrency)];
        if (flow is null || (SelectedKey is not null && !flow.Lots.Any(Covers)))
        {
            SelectedKey = null;
        }

        if (SelectedKey is null && flow is not null)
        {
            SelectedKey = DefaultChoice(flow);
        }

        Lay();
    }

    /// <summary>Lays the Sankey and the panel out for the flow, the unit and the room.</summary>
    private void Lay()
    {
        if (_flow is not { } flow || !flow.HasVictimMoney)
        {
            Picture = FlowPicture.None;
            HasFlow = false;
            Ends = [];
            TotalText = string.Empty;
            UnitText = string.Empty;
            Note = string.Empty;
            HasGuess = false;
            Choose();
            return;
        }

        Picture = FlowLayout.Lay(flow, _actors, _room.Width, _room.Height, Write);
        HasFlow = Picture.HasBars;
        HasGuess = Picture.Strands.Any(strand => strand.IsGuess);
        var total = flow.VictimUnits;
        var victims = flow.Lots.Where(lot => lot.IsVictim).ToList();
        TotalText = Write(victims);
        UnitText = _inCurrency ? string.Empty : flow.Asset.Symbol;
        Ends = [.. EndsOf(flow, total)];
        Note = NoteOf(flow, Picture);
        Choose();
    }

    /// <summary>Fills the panel and lights the strands for the choice.</summary>
    private void Choose()
    {
        var chosen = Chosen;
        Lit = chosen.ToHashSet();
        Detail = _flow is { } flow && chosen.Count > 0 ? DetailOf(flow, chosen) : null;
    }

    /// <summary>Gives whether a part runs through the choice: on the chosen step, or ending at the chosen actor.</summary>
    /// <param name="lot">The part.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private bool Covers(FlowLot lot)
    {
        if (SelectedKey is not { } key)
        {
            return false;
        }

        var arrow = key.IndexOf('>', StringComparison.Ordinal);
        if (arrow > 0 && _flow?.Steps.ContainsKey(key) == true)
        {
            return lot.Passes(key[..arrow], key[(arrow + 1)..]);
        }

        var endsHere = _flow?.Lots.Any(each => each.Last == key) == true;
        return endsHere ? lot.Last == key : lot.Path.Contains(key);
    }

    /// <summary>Gives the choice shown first: the largest cash-out of the victims' money, or the largest part.</summary>
    /// <param name="flow">The flow.</param>
    /// <returns>The actor's key, or <see langword="null"/>.</returns>
    private static string? DefaultChoice(FlowResult flow)
    {
        var victims = flow.Lots.Where(lot => lot.IsVictim).ToList();
        var best = victims.Where(lot => lot.End == FlowEnd.CashOut).GroupBy(lot => lot.Last)
            .MaxBy(group => group.Aggregate(BigInteger.Zero, (sum, lot) => sum + lot.Units));
        return best?.Key ?? victims.FirstOrDefault()?.Last;
    }

    /// <summary>Makes the panel's detail: the actor where most of it ended, the ways and what they rest on.</summary>
    /// <param name="flow">The flow.</param>
    /// <param name="chosen">The parts chosen.</param>
    /// <returns>The detail.</returns>
    private FlowDetail DetailOf(FlowResult flow, IReadOnlyList<FlowLot> chosen)
    {
        var victims = chosen.Where(lot => lot.IsVictim).Aggregate(BigInteger.Zero, (sum, lot) => sum + lot.Units);
        var end = chosen.GroupBy(lot => lot.Last)
            .MaxBy(group => group.Aggregate(BigInteger.Zero, (sum, lot) => sum + lot.Units))!.Key;
        var actor = _actors.GetValueOrDefault(end);
        var amount = AmountText.WithUnit(new AssetAmount(flow.Asset, victims));
        var value = ValueText(chosen.Where(lot => lot.IsVictim));
        var line = value.Length > 0 ? $"{amount} · {value}" : amount;
        var share = flow.VictimUnits.IsZero ? 0 : (double)victims / (double)flow.VictimUnits;
        var ways = chosen.GroupBy(lot => string.Join('>', lot.Path))
            .Select(group => group.ToList())
            .OrderByDescending(way => way.Aggregate(BigInteger.Zero, (sum, lot) => sum + lot.Units))
            .ToList();
        var routes = ways.Take(RoutesShown).Select(way => RouteOf(flow, way)).ToList();
        var more = ways.Count > RoutesShown
            ? "+" + (ways.Count - RoutesShown).ToString(CultureInfo.InvariantCulture) + " vägar"
            : string.Empty;
        return new FlowDetail(
            actor?.Look ?? VisualLanguage.Of(NodeSort.Unknown), actor?.Name ?? end, line, ExposureMap.Percent(share),
            routes, more, BasisOf(flow, chosen));
    }

    /// <summary>Makes a way: its actors as chips, its amount and the transactions of its steps.</summary>
    /// <param name="flow">The flow.</param>
    /// <param name="way">The parts that went the way.</param>
    /// <returns>The way.</returns>
    private FlowRoute RouteOf(FlowResult flow, IReadOnlyList<FlowLot> way)
    {
        var lot = way[0];
        var crumbs = lot.Path.Select((key, index) => new ExposureCrumb(
            NameOf(key), _actors.GetValueOrDefault(key)?.Look.IsDashed == true, index < lot.Path.Count - 1)).ToList();
        var transactions = lot.Path.Skip(1)
            .SelectMany((to, index) => flow.Steps.GetValueOrDefault(FlowResult.StepKey(lot.Path[index], to))
                ?.Transactions ?? [])
            .Select(tx => GraphScene.Shorten(tx.Id))
            .Distinct()
            .ToList();
        return new FlowRoute(crumbs, Write(way), string.Join(" · ", transactions));
    }

    /// <summary>Gives what the chosen money rests on: the method, the analysis, the clusters and any suggestion.</summary>
    /// <param name="flow">The flow.</param>
    /// <param name="chosen">The parts chosen.</param>
    /// <returns>The chips.</returns>
    private List<ExposureBasis> BasisOf(FlowResult flow, IReadOnlyList<FlowLot> chosen)
    {
        var basis = new List<ExposureBasis>
        {
            new(flow.Method.ToString(), "Icon.Layers", "Spårningsmetoden som delar beloppen"),
            new(FlowResult.Analysis, "Icon.Flow", "Analysen som följer pengarna aktör för aktör"),
        };
        if (chosen.Any(lot => lot.Path.Any(key => key.StartsWith("cluster:", StringComparison.Ordinal))))
        {
            basis.Add(new("Klustring", "Icon.Join", "Vägen går genom kluster, som är klustringens slutsats"));
        }

        if (chosen.Any(lot => lot.IsGuess))
        {
            const string Uncertain = "En del av vägen bygger på något osäkert, till exempel en trolig utgång";
            basis.Add(new("Förslag", "Icon.Suggest", Uncertain));
        }

        return basis;
    }

    /// <summary>Gives where the victims' money ended, per colour, in the panel's order.</summary>
    /// <param name="flow">The flow.</param>
    /// <param name="total">The victims' money in all.</param>
    /// <returns>The rows with money.</returns>
    private IEnumerable<FlowEndRow> EndsOf(FlowResult flow, BigInteger total)
    {
        foreach (var end in Enum.GetValues<FlowEnd>())
        {
            var lots = flow.Lots.Where(lot => lot.IsVictim && lot.End == end).ToList();
            var units = lots.Aggregate(BigInteger.Zero, (sum, lot) => sum + lot.Units);
            if (units.Sign > 0)
            {
                var share = (double)units / (double)total;
                yield return new FlowEndRow(end, share, Write(lots) + " " + UnitText);
            }
        }
    }

    /// <summary>Writes what the picture leaves out or merges, or an empty text.</summary>
    /// <param name="flow">The flow.</param>
    /// <param name="picture">The picture.</param>
    /// <returns>The note.</returns>
    private static string NoteOf(FlowResult flow, FlowPicture picture)
    {
        var notes = new List<string>();
        if (picture.Hidden > 0)
        {
            notes.Add($"{picture.Hidden.ToString(CultureInfo.InvariantCulture)} tunna delar är för små att rita");
        }

        if (flow.IsSimplified)
        {
            notes.Add("små delar hos aktörer med mycket många slogs ihop");
        }

        return notes.Count == 0 ? string.Empty : char.ToUpperInvariant(notes[0][0]) + string.Join(" · ", notes)[1..];
    }

    /// <summary>Writes the amount of some parts in the unit shown: the asset, or their value in the currency.</summary>
    /// <param name="lots">The parts.</param>
    /// <returns>The amount.</returns>
    private string Write(IEnumerable<FlowLot> lots)
    {
        if (_flow is not { } flow)
        {
            return string.Empty;
        }

        if (_inCurrency && _values is not null)
        {
            return ValueText(lots);
        }

        var units = lots.Aggregate(BigInteger.Zero, (sum, lot) => sum + lot.Units);
        return AmountText.Number(new AssetAmount(flow.Asset, units));
    }

    /// <summary>Writes the value of parts in the currency, or an empty text when they have none.</summary>
    /// <param name="lots">The parts.</param>
    /// <returns>For example <c>263 100 kr</c>.</returns>
    private string ValueText(IEnumerable<FlowLot> lots)
    {
        if (_values is not { } values)
        {
            return string.Empty;
        }

        var sum = lots.Sum(lot => values.GetValueOrDefault(lot) * (decimal)lot.Units);
        return Currency(sum);
    }

    /// <summary>Writes a sum in the currency.</summary>
    /// <param name="sum">The sum.</param>
    /// <returns>For example <c>263 100 kr</c>.</returns>
    private string Currency(decimal sum) =>
        Math.Round(sum, 0).ToString("#,##0", CultureInfo.GetCultureInfo("sv-SE")) + MoneyText.Unit(_currency);

    /// <summary>
    /// Gives each part's value per unit at the time it left its source, from the value Pricing gave the first step's
    /// line; none at all when a part's first line has no value, so that the currency is never half made up.
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <param name="flow">The flow.</param>
    /// <returns>The values and their currency, or <see langword="null"/>.</returns>
    private static (IReadOnlyDictionary<FlowLot, decimal>? Values, string Currency) Values(
        GraphScene scene, FlowResult flow)
    {
        var values = new Dictionary<FlowLot, decimal>();
        var currency = string.Empty;
        foreach (var lot in flow.Lots)
        {
            var step = flow.Steps.GetValueOrDefault(FlowResult.StepKey(lot.Path[0], lot.Path[1]));
            var edge = step is null ? null : scene.Snapshot.Graph.Edges.FirstOrDefault(e => e.Key == step.EdgeKey);
            if (edge is null || scene.Snapshot.Values.GetValueOrDefault(edge.Key) is not { } value
                || edge.Amount.Units.IsZero || (currency.Length > 0 && currency != value.Currency))
            {
                return (null, string.Empty);
            }

            currency = value.Currency;
            values[lot] = value.Rounded / (decimal)edge.Amount.Units;
        }

        return (values.Count > 0 ? values : null, currency);
    }

    /// <summary>Gives the actors the layout names, with their sorts and the sanction mark.</summary>
    /// <param name="scene">The graph.</param>
    /// <returns>The actors by key.</returns>
    private static Dictionary<string, FlowLayout.Actor> ActorsOf(GraphScene scene) =>
        ExposureMap.Actors(scene).Values.ToDictionary(
            node => node.Key,
            node => new FlowLayout.Actor(node.Name, node.Look, node.Badges.Contains(NodeBadge.Sanctioned)),
            StringComparer.Ordinal);

    /// <summary>Gives an actor's name.</summary>
    /// <param name="key">The actor's key.</param>
    /// <returns>The name.</returns>
    private string NameOf(string key) => _actors.GetValueOrDefault(key)?.Name ?? key;

    /// <summary>Makes a method's choice in the head.</summary>
    /// <param name="method">The method.</param>
    /// <returns>The choice.</returns>
    private ExposureChoice MethodChoice(TracingMethodKind method) => new(
        Capitalised(GraphScene.MethodName(method)),
        method == TracingMethodKind.Fifo
            ? "Först in, först ut"
            : "Varje utbetalning bär sin andel av allt som kom in",
        method == Method,
        method.ToString());

    /// <summary>Writes a name with a capital first letter, as the head's choices are written.</summary>
    /// <param name="name">The name.</param>
    /// <returns>For example <c>Haircut</c>.</returns>
    private static string Capitalised(string name) =>
        name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : name;

    /// <summary>Makes a unit's choice in the head.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="isSelected">Whether it is chosen.</param>
    /// <returns>The choice.</returns>
    private static ExposureChoice UnitChoice(string unit, bool isSelected) => new(unit, "Skriv beloppen i " + unit,
        isSelected, unit);
}
