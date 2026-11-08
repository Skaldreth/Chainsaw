using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>A cluster or address in the subject menu (sketch EP1b, <em>I grafen</em>).</summary>
/// <param name="Subject">The subject.</param>
/// <param name="IsSelected">Whether it is the one shown.</param>
public sealed record ExposureSubjectRow(ExposureSubject Subject, bool IsSelected)
{
    /// <summary>Gets the name.</summary>
    public string Name => Subject.Name;

    /// <summary>Gets the second line.</summary>
    public string Line => Subject.Line;

    /// <summary>Gets the sort, drawn as in the graph.</summary>
    public SortLook Look => Subject.Node.Look;
}

/// <summary>A choice in a small segmented control: a depth, or an asset.</summary>
/// <param name="Text">What it says.</param>
/// <param name="ToolTip">Its tooltip.</param>
/// <param name="IsSelected">Whether it is chosen.</param>
/// <param name="Value">What choosing it sets: the depth as text, or the asset's symbol.</param>
public sealed record ExposureChoice(string Text, string ToolTip, bool IsSelected, string Value);

/// <summary>A row of a side's shares per colour (sketch EP1b): a dot, the name, a bar and the share.</summary>
/// <param name="Group">What the colour means.</param>
/// <param name="Share">The share, from 0 to 1.</param>
/// <param name="ToolTip">The amount, or why it is unknown.</param>
public sealed record ExposureGroupRow(ExposureGroup Group, double Share, string ToolTip)
{
    /// <summary>Gets the name.</summary>
    public string Name => ExposureMap.NameOf(Group);

    /// <summary>Gets the colour.</summary>
    public GraphInk Ink => ExposureMap.InkOf(Group);

    /// <summary>Gets the share as written.</summary>
    public string Percent => ExposureMap.Percent(Share);

    /// <summary>Gets whether it is the grey <em>Okänt</em>, whose dot is a dashed frame.</summary>
    public bool IsUnknown => Group == ExposureGroup.Unknown;
}

/// <summary>A side of the panel: <em>Betalat till</em> or <em>Fått från</em>, its total and its shares.</summary>
/// <param name="Title">The heading, in capitals.</param>
/// <param name="Total">The total, without unit.</param>
/// <param name="Unit">The unit.</param>
/// <param name="Rows">The shares per colour.</param>
public sealed record ExposureSideRows(string Title, string Total, string Unit, IReadOnlyList<ExposureGroupRow> Rows);

/// <summary>A service in the panel's list: its sort, name, hops and share.</summary>
/// <param name="Id">Its place in the rings.</param>
/// <param name="Look">Its sort.</param>
/// <param name="Name">Its name.</param>
/// <param name="Hops">How many hops, for example <c>2 hopp</c>.</param>
/// <param name="Percent">Its share.</param>
/// <param name="IsSelected">Whether it is chosen.</param>
public sealed record ExposureServiceRow(
    string Id, SortLook Look, string Name, string Hops, string Percent, bool IsSelected);

/// <summary>A step of the chosen way, as a chip (sketch EP1b).</summary>
/// <param name="Text">The actor's name.</param>
/// <param name="IsDashed">Whether its sort's frame is dashed.</param>
/// <param name="HasNext">Whether a <c>›</c> follows.</param>
public sealed record ExposureCrumb(string Text, bool IsDashed, bool HasNext);

/// <summary>What the chosen share rests on, as a chip with an icon (sketch EP1b).</summary>
/// <param name="Text">What it says.</param>
/// <param name="Icon">The icon's resource key.</param>
/// <param name="ToolTip">Its tooltip.</param>
public sealed record ExposureBasis(string Text, string Icon, string ToolTip);

/// <summary>The chosen service at the bottom of the panel (sketch EP1b).</summary>
/// <param name="Look">Its sort.</param>
/// <param name="Name">Its name.</param>
/// <param name="Amount">How much, with unit.</param>
/// <param name="Percent">Its share.</param>
/// <param name="Direction">Of what: <em>av det som gått ut</em> or <em>av det som kommit in</em>.</param>
/// <param name="Crumbs">The way, from the subject outwards when paid, from the service when got.</param>
/// <param name="Transactions">The transactions on the way.</param>
/// <param name="Basis">What it rests on.</param>
public sealed record ExposureDetail(
    SortLook Look,
    string Name,
    string Amount,
    string Percent,
    string Direction,
    IReadOnlyList<ExposureCrumb> Crumbs,
    string Transactions,
    IReadOnlyList<ExposureBasis> Basis);

/// <summary>The way to show in the graph: the actors and lines of the chosen service's way.</summary>
/// <param name="Nodes">The actors, the service last.</param>
/// <param name="Edges">The lines.</param>
/// <param name="Note">What the graph's note says, for example <c>K1 → Börs A · 0,2500 BTC · FIFO v1</c>.</param>
public sealed record ExposurePath(IReadOnlyList<string> Nodes, IReadOnlyList<string> Edges, string Note);

/// <summary>
/// The view Exponering (sketch EP1b): a cluster or address in the centre with a ring per hop, what it paid out to the
/// right and what it got to the left, the shares per colour of both sides, the services and the chosen one's way with
/// what it rests on. The numbers are Tracing's analysis <em>exposure</em> with the graph's tracing method, a judgement
/// (principle 2) with <em>Varför?</em>; what reached no service is said as <em>Okänt</em>. The view follows the graph
/// shown. No WPF, so that it is tested in Chainsaw.App.Tests.
/// </summary>
public sealed partial class ExposureViewModel : ObservableObject
{
    /// <summary>What the view says when the graph has no actor that paid or got anything.</summary>
    public const string EmptyText =
        "Inget att visa än. Exponeringen räknas för ett kluster eller en adress i grafen som har betalat eller fått "
        + "pengar. Expandera grafen först.";

    /// <summary>What the view says when the analysis could not be run for the graph.</summary>
    public const string FailedText = "Exponeringen kunde inte räknas för den här grafen.";

    /// <summary>The analysis.</summary>
    private readonly IExposureAnalysis _analysis;

    /// <summary>The graph shown, or <see langword="null"/>.</summary>
    private GraphScene? _scene;

    /// <summary>The UI thread, where an analysis read off it is shown.</summary>
    private readonly IUiThread _ui;

    /// <summary>The graph's actors and sorts, by key.</summary>
    private ExposureNames _names = ExposureNames.None;

    /// <summary>Whether the graph changed while the view was hidden, so that it is read when shown.</summary>
    private bool _stale = true;

    /// <summary>Stops the analysis running, when another is asked for.</summary>
    private CancellationTokenSource? _running;

    /// <summary>The subject's key, kept when the graph is read anew.</summary>
    private string? _subjectKey;

    /// <summary>The asset followed, kept while the subject has it.</summary>
    private Asset? _asset;

    /// <summary>The chosen place's id, kept while it is there.</summary>
    private string? _selectedId;

    /// <summary>What the subject paid out, or <see langword="null"/>.</summary>
    private ExposureResult? _paid;

    /// <summary>What the subject got, or <see langword="null"/>.</summary>
    private ExposureResult? _got;

    /// <summary>The approved likely ways out of the graph's mixers, drawn beyond them (sketch KF2b).</summary>
    private IReadOnlyList<ApprovedWayOut> _waysOut = [];

    /// <summary>Creates the view.</summary>
    /// <param name="analysis">Tracing's analysis.</param>
    /// <param name="ui">The UI thread.</param>
    public ExposureViewModel(IExposureAnalysis analysis, IUiThread ui)
    {
        _analysis = analysis;
        _ui = ui;
    }

    /// <summary>
    /// Gets the analysis running off the UI thread, or the last; complete when none runs. The view's numbers are shown
    /// when it ends.
    /// </summary>
    public Task Computed { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Gets or sets whether the view is shown. The analysis runs only then (principle 6): a graph read while the view
    /// is hidden marks it stale, and showing it reads it.
    /// </summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; }

    /// <summary>Gets why the exposure could not be read, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string Problem { get; private set; } = string.Empty;

    /// <summary>Gets whether the exposure could not be read.</summary>
    public bool HasProblem => Problem.Length > 0;

    /// <summary>Raised when the user wants the chosen way shown in the graph.</summary>
    public event EventHandler<ExposurePath>? PathWanted;

    /// <summary>Raised when the user asks <em>Varför?</em> about the chosen share.</summary>
    public event EventHandler<GuideRequest>? GuideWanted;

    /// <summary>Gets the subjects the menu offers.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ExposureSubjectRow> Subjects { get; private set; } = [];

    /// <summary>Gets the subject shown, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubject))]
    public partial ExposureSubject? Subject { get; private set; }

    /// <summary>Gets whether a subject is shown; otherwise the view says why not.</summary>
    public bool HasSubject => Subject is not null;

    /// <summary>Gets or sets whether the subject menu is out.</summary>
    [ObservableProperty]
    public partial bool IsMenuOpen { get; set; }

    /// <summary>Gets how many hops are followed (1 to 3).</summary>
    [ObservableProperty]
    public partial int Depth { get; private set; } = ExposureRequest.MaxDepth;

    /// <summary>Gets the depth's choices.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ExposureChoice> Depths { get; private set; } = [];

    /// <summary>Gets the assets to choose between, empty when the subject has only one (principle 7).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAssets))]
    public partial IReadOnlyList<ExposureChoice> Assets { get; private set; } = [];

    /// <summary>Gets whether there are assets to choose between.</summary>
    public bool HasAssets => Assets.Count > 1;

    /// <summary>Gets the tracing method's name, for example <c>FIFO</c>, always shown with the shares.</summary>
    [ObservableProperty]
    public partial string MethodText { get; private set; } = string.Empty;

    /// <summary>Gets the method's and the analysis's versions, small beside the method.</summary>
    [ObservableProperty]
    public partial string VersionText { get; private set; } = string.Empty;

    /// <summary>Gets the picture of the rings.</summary>
    [ObservableProperty]
    public partial ExposurePicture Picture { get; private set; } = ExposurePicture.None;

    /// <summary>Gets the chosen place's id, which the picture draws lit, or <see langword="null"/>.</summary>
    [ObservableProperty]
    public partial string? SelectedId { get; private set; }

    /// <summary>Gets the chosen way's place ids and line keys, which the picture keeps clear while the rest dims.</summary>
    [ObservableProperty]
    public partial (IReadOnlySet<string> Places, IReadOnlySet<string> Lines) Lit { get; private set; } =
        (new HashSet<string>(), new HashSet<string>());

    /// <summary>Gets the two sides' shares.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ExposureSideRows> Sides { get; private set; } = [];

    /// <summary>Gets the services the subject paid out to, the largest first.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ExposureServiceRow> Services { get; private set; } = [];

    /// <summary>Gets the chosen service, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    [NotifyCanExecuteChangedFor(nameof(ShowPathCommand), nameof(AskWhyCommand))]
    public partial ExposureDetail? Detail { get; private set; }

    /// <summary>Gets whether a service is chosen.</summary>
    public bool HasDetail => Detail is not null;

    /// <summary>Gets the chosen service's row in the analysis, or <see langword="null"/>.</summary>
    private (ExposureResult Result, ExposureReach Reach)? Chosen =>
        Find(_paid, ExposureDirection.Out) ?? Find(_got, ExposureDirection.In);

    /// <summary>
    /// Follows the graph shown. While the view is hidden the graph is only kept and the view marked stale; shown, the
    /// subjects are found again and the subject, the asset and the choice stay where they can.
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

    /// <summary>
    /// Follows the approved likely ways out of the graph's mixers, so that the picture draws them beyond the mixers as
    /// suggestions (TODO, <em>Exponering, mixerns troliga utgångar</em>); they change no number.
    /// </summary>
    /// <param name="waysOut">The approved ways out.</param>
    public void UpdateWaysOut(IReadOnlyList<ApprovedWayOut> waysOut)
    {
        ArgumentNullException.ThrowIfNull(waysOut);
        if (waysOut.SequenceEqual(_waysOut))
        {
            return;
        }

        _waysOut = waysOut;
        if (_paid is not null && _got is not null && Subject is { } subject)
        {
            Picture = ExposureLayout.Build(subject.Node, _paid, _got, _names, _waysOut);
        }
    }

    /// <summary>Reads a stale view when it is shown.</summary>
    /// <param name="value">Whether it is shown.</param>
    partial void OnIsShownChanged(bool value)
    {
        if (value && _stale)
        {
            Refresh();
        }
    }

    /// <summary>Finds the subjects in the graph kept and reads the exposure.</summary>
    private void Refresh()
    {
        _stale = false;
        var scene = _scene;
        _names = scene is null ? ExposureNames.None : ExposureNames.Of(scene);
        var subjects = scene is null ? [] : ExposureMap.Subjects(scene);
        var subject = subjects.FirstOrDefault(each => each.Key == _subjectKey) ?? subjects.FirstOrDefault();
        Show(subjects, subject);
    }

    /// <summary>Folds the subject menu out or in.</summary>
    [RelayCommand]
    public void ToggleMenu() => IsMenuOpen = !IsMenuOpen && Subjects.Count > 0;

    /// <summary>Shows another subject from the menu.</summary>
    /// <param name="row">The subject.</param>
    [RelayCommand]
    public void PickSubject(ExposureSubjectRow? row)
    {
        IsMenuOpen = false;
        if (row is not null)
        {
            _selectedId = null;
            Show([.. Subjects.Select(each => each.Subject)], row.Subject);
        }
    }

    /// <summary>Shows a subject when it is in the graph, as the graph's node menu may ask.</summary>
    /// <param name="nodeKey">The node.</param>
    /// <returns><see langword="true"/> when it is a subject.</returns>
    public bool Open(string nodeKey)
    {
        var subject = Subjects.FirstOrDefault(row => row.Subject.Key == nodeKey)?.Subject;
        if (subject is null)
        {
            return false;
        }

        _selectedId = null;
        Show([.. Subjects.Select(each => each.Subject)], subject);
        return true;
    }

    /// <summary>Sets how many hops are followed.</summary>
    /// <param name="value">The depth, as its choice's value.</param>
    [RelayCommand]
    public void SetDepth(string? value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var depth)
            && depth is >= 1 and <= ExposureRequest.MaxDepth && depth != Depth)
        {
            Depth = depth;
            Compute();
        }
    }

    /// <summary>Follows another asset of the subject.</summary>
    /// <param name="symbol">The asset's symbol.</param>
    [RelayCommand]
    public void PickAsset(string? symbol)
    {
        var asset = Subject?.Assets.FirstOrDefault(each => each.Symbol == symbol);
        if (asset is not null && asset != _asset)
        {
            _asset = asset;
            _selectedId = null;
            Compute();
        }
    }

    /// <summary>Chooses a service or an actor on the way, in the rings or the list.</summary>
    /// <param name="id">Its place's id.</param>
    [RelayCommand]
    public void Pick(string? id)
    {
        IsMenuOpen = false;
        if (id is null || !Picture.Services.Any(place => place.Id == id))
        {
            return;
        }

        _selectedId = id;
        Choose();
    }

    /// <summary><em>Visa vägen i grafen</em> (G): the graph with the chosen way lit.</summary>
    [RelayCommand(CanExecute = nameof(HasDetail))]
    public void ShowPath()
    {
        if (Chosen is not { } chosen)
        {
            return;
        }

        var (result, reach) = chosen;
        var subject = _names.NameOf(result.Subject);
        var service = _names.NameOf(reach.NodeKey);
        var arrow = result.Direction == ExposureDirection.Out ? $"{subject} → {service}" : $"{service} → {subject}";
        var note = $"{arrow} · {AmountText.WithUnit(reach.Amount)} · {result.Method}";
        PathWanted?.Invoke(this, new ExposurePath(reach.Path, reach.PathEdges, note));
    }

    /// <summary><em>Varför?</em>: the Guide's panel with the share explained and the section on exposure.</summary>
    [RelayCommand(CanExecute = nameof(HasDetail))]
    public void AskWhy()
    {
        if (Chosen is not { } chosen || Detail is not { } detail)
        {
            return;
        }

        var (result, reach) = chosen;
        WhyLine[] lines =
        [
            new(result.Explain(reach), GuideTopic.Exposure),
            new($"Spårningsmetoden är {result.Method}.", GuideTopic.Methods),
        ];
        var context = new GuideContext("Varför? · " + detail.Name, lines);
        GuideWanted?.Invoke(this, new GuideRequest(GuideTopic.Exposure, null, context, ExposureLayout.IdOf(
            result.Direction, reach.NodeKey)) { IsChoice = true });
    }

    /// <summary>Shows a subject: its assets, the analysis on both sides and the choice.</summary>
    /// <param name="subjects">Every subject.</param>
    /// <param name="subject">The one shown, or <see langword="null"/>.</param>
    private void Show(IReadOnlyList<ExposureSubject> subjects, ExposureSubject? subject)
    {
        _subjectKey = subject?.Key;
        Subject = subject;
        Subjects = [.. subjects.Select(each => new ExposureSubjectRow(each, each.Key == subject?.Key))];
        if (subject is not null && (_asset is null || !subject.Assets.Contains(_asset)))
        {
            _asset = subject.Assets[0];
        }

        Compute();
    }

    /// <summary>
    /// Reads the exposure on both sides off the UI thread (principle 6), the last asked for winning, and then lays out
    /// the picture and the panel. While the view is hidden it is only marked stale.
    /// </summary>
    private void Compute()
    {
        Depths = [.. Enumerable.Range(1, ExposureRequest.MaxDepth).Select(DepthChoice)];
        _running?.Cancel();
        if (!IsShown)
        {
            _stale = true;
            return;
        }

        if (_scene is not { } scene || Subject is not { } subject || _asset is not { } asset)
        {
            Apply(null, null, string.Empty);
            return;
        }

        Assets = [.. subject.Assets.Select(each => new ExposureChoice(
            each.Symbol, "Följ " + each.Symbol, each == asset, each.Symbol))];
        var paid = ExposureMap.Request(scene, subject.Key, asset, ExposureDirection.Out, Depth);
        var got = ExposureMap.Request(scene, subject.Key, asset, ExposureDirection.In, Depth);
        var running = new CancellationTokenSource();
        _running = running;
        Computed = ReadAsync(paid, got, running.Token);
    }

    /// <summary>Runs the analysis on both sides off the UI thread and shows it, unless another was asked for.</summary>
    /// <param name="paid">What the subject paid out.</param>
    /// <param name="got">What it got.</param>
    /// <param name="cancellation">Stops it when another is asked for.</param>
    /// <returns>The reading.</returns>
    private async Task ReadAsync(ExposureRequest paid, ExposureRequest got, CancellationToken cancellation)
    {
        try
        {
            var results = await Task.Run(
                () => (_analysis.Analyse(paid), _analysis.Analyse(got)), cancellation).ConfigureAwait(false);
            if (!cancellation.IsCancellationRequested)
            {
                _ui.Post(() => ApplyUnlessCancelled(results.Item1, results.Item2, cancellation));
            }
        }
        catch (OperationCanceledException)
        {
            // Another reading was asked for; it shows its own.
        }
        catch (InvalidOperationException)
        {
            // The analysis' own message is in English and is never shown; a reading asked for since shows its own.
            if (!cancellation.IsCancellationRequested)
            {
                _ui.Post(() => Apply(null, null, FailedText));
            }
        }
    }

    /// <summary>Shows a reading, unless another was asked for since.</summary>
    /// <param name="paid">What the subject paid out.</param>
    /// <param name="got">What it got.</param>
    /// <param name="cancellation">Whether another was asked for.</param>
    private void ApplyUnlessCancelled(ExposureResult paid, ExposureResult got, CancellationToken cancellation)
    {
        if (!cancellation.IsCancellationRequested)
        {
            Apply(paid, got, string.Empty);
        }
    }

    /// <summary>Lays out the picture and the panel for a reading, or empties them.</summary>
    /// <param name="paid">What the subject paid out, or <see langword="null"/>.</param>
    /// <param name="got">What it got, or <see langword="null"/>.</param>
    /// <param name="problem">Why nothing could be read, or an empty text.</param>
    private void Apply(ExposureResult? paid, ExposureResult? got, string problem)
    {
        (_paid, _got) = (paid, got);
        Problem = problem;
        if (paid is null || got is null || Subject is not { } subject)
        {
            Assets = Subject is null ? [] : Assets;
            Picture = ExposurePicture.None;
            Sides = [];
            MethodText = string.Empty;
            VersionText = string.Empty;
            Choose();
            return;
        }

        MethodText = paid.Method.Name;
        VersionText = $"{paid.Method} · {ExposureResult.Analysis}";
        Picture = ExposureLayout.Build(subject.Node, paid, got, _names, _waysOut);
        var word = SubjectWord(subject);
        Sides = [SideRows("BETALAT TILL", paid, word), SideRows("FÅTT FRÅN", got, word)];
        if (_selectedId is null || !Picture.Services.Any(place => place.Id == _selectedId))
        {
            _selectedId = Largest(paid, ExposureDirection.Out) ?? Largest(got, ExposureDirection.In);
        }

        Choose();
    }

    /// <summary>Gives the word for the subject in the rest's reasons: <em>klustret</em> or <em>adressen</em>.</summary>
    /// <param name="subject">The subject.</param>
    /// <returns>The word.</returns>
    private static string SubjectWord(ExposureSubject subject) =>
        subject.Node.Node.Kind == GraphNodeKind.Cluster ? "klustret" : "adressen";

    /// <summary>Makes a depth's choice.</summary>
    /// <param name="depth">The depth.</param>
    /// <returns>The choice.</returns>
    private ExposureChoice DepthChoice(int depth) => new(
        depth.ToString(CultureInfo.InvariantCulture),
        depth == 1 ? "Bara direkt" : $"Upp till {depth} hopp",
        depth == Depth,
        depth.ToString(CultureInfo.InvariantCulture));

    /// <summary>Gives the id of a side's largest service, or <see langword="null"/>.</summary>
    /// <param name="result">The side.</param>
    /// <param name="side">Which side.</param>
    /// <returns>The id.</returns>
    private static string? Largest(ExposureResult? result, ExposureDirection side) =>
        result?.Reached.FirstOrDefault() is { } reach ? ExposureLayout.IdOf(side, reach.NodeKey) : null;

    /// <summary>Makes a side's rows: a share per colour that has any, <em>Okänt</em> last.</summary>
    /// <param name="title">The heading.</param>
    /// <param name="result">The side.</param>
    /// <param name="subject">The word for the subject.</param>
    /// <returns>The rows.</returns>
    private ExposureSideRows SideRows(string title, ExposureResult result, string subject)
    {
        var groups = result.Reached
            .GroupBy(reach => ExposureMap.GroupOf(_names.LookOf(reach.NodeKey)))
            .ToDictionary(group => group.Key, group => group.ToList());
        var sums = groups.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Aggregate(AssetAmount.Zero(result.Total.Asset), (sum, reach) => sum + reach.Amount));
        sums[ExposureGroup.Unknown] = sums.GetValueOrDefault(ExposureGroup.Unknown, AssetAmount.Zero(result.Total.Asset))
            + result.Unknown;
        var rows = ExposureMap.Order
            .Where(group => sums.TryGetValue(group, out var sum) && sum.Units.Sign > 0)
            .Select(group => new ExposureGroupRow(
                group, result.ShareOf(sums[group]), GroupTip(group, sums[group], result, subject, groups)))
            .ToList();
        return new ExposureSideRows(title, AmountText.Number(result.Total), result.Total.Asset.Symbol, rows);
    }

    /// <summary>
    /// Writes a share's tooltip: the amount and what it was worth at the time, and for <em>Okänt</em> where it went.
    /// </summary>
    /// <param name="group">The colour.</param>
    /// <param name="sum">Its amount.</param>
    /// <param name="result">The side.</param>
    /// <param name="subject">The word for the subject.</param>
    /// <param name="groups">The services per colour.</param>
    /// <returns>The tooltip.</returns>
    private string GroupTip(
        ExposureGroup group,
        AssetAmount sum,
        ExposureResult result,
        string subject,
        IReadOnlyDictionary<ExposureGroup, List<ExposureReach>> groups)
    {
        if (group != ExposureGroup.Unknown)
        {
            return ExposureValues.After(AmountText.WithUnit(sum), ValueOf(result, groups[group]));
        }

        var parts = result.Rest.Select(
            pair => $"{RestText(pair.Key, result.Depth, subject)}: {AmountText.WithUnit(pair.Value)}");
        return string.Join("\n", parts.Prepend("Har inte nått en tjänst · " + AmountText.WithUnit(sum)));
    }

    /// <summary>Says why a part reached no service.</summary>
    /// <param name="rest">The reason.</param>
    /// <param name="depth">How many hops were followed.</param>
    /// <param name="subject">The word for the subject, <em>klustret</em> or <em>adressen</em>.</param>
    /// <returns>The words.</returns>
    public static string RestText(ExposureRest rest, int depth, string subject) => rest switch
    {
        ExposureRest.Remains => "Ligger kvar hos en aktör på vägen",
        ExposureRest.NotExpanded => "Aktören är inte expanderad",
        ExposureRest.Fees => "Gick till avgifter",
        ExposureRest.StillMoving => "Gick runt fler varv än analysen följer",
        ExposureRest.Returned => "Gick tillbaka till " + subject,
        ExposureRest.BeforeHistory => "Kom före historiken i grafen",
        ExposureRest.TrailEnds => "Spåret tar slut i en tx, till exempel en CoinJoin",
        ExposureRest.Conflicting => "Uppgifterna om vart pengarna gick går inte ihop",
        ExposureRest.OtherChain => "Fortsätter på annan kedja",
        _ => depth == 1 ? "Gick vidare till en aktör som inte följs" : $"Nådde ingen tjänst inom {depth} hopp",
    };

    /// <summary>Lays out the panel's list and the chosen service for the choice.</summary>
    private void Choose()
    {
        SelectedId = _selectedId;
        Services = _paid is null
            ? []
            : [.. _paid.Reached.Select(reach => ServiceRow(_paid, reach))];
        if (Chosen is not { } chosen)
        {
            Detail = null;
            Lit = (new HashSet<string>(), new HashSet<string>());
            return;
        }

        var (result, reach) = chosen;
        Detail = DetailOf(result, reach);
        Lit = (
            reach.Path.Skip(1).Select(key => ExposureLayout.IdOf(result.Direction, key)).ToHashSet(StringComparer.Ordinal),
            reach.PathEdges.ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>Finds the chosen service on a side.</summary>
    /// <param name="result">The side.</param>
    /// <param name="side">Which side.</param>
    /// <returns>The side and the row, or <see langword="null"/>.</returns>
    private (ExposureResult, ExposureReach)? Find(ExposureResult? result, ExposureDirection side) =>
        result?.Reached.FirstOrDefault(reach => ExposureLayout.IdOf(side, reach.NodeKey) == _selectedId) is { } found
            ? (result, found)
            : null;

    /// <summary>Makes a service's row in the list.</summary>
    /// <param name="result">The side.</param>
    /// <param name="reach">The service.</param>
    /// <returns>The row.</returns>
    private ExposureServiceRow ServiceRow(ExposureResult result, ExposureReach reach)
    {
        var id = ExposureLayout.IdOf(result.Direction, reach.NodeKey);
        return new ExposureServiceRow(
            id,
            _names.LookOf(reach.NodeKey),
            _names.NameOf(reach.NodeKey),
            reach.Hops.ToString(CultureInfo.InvariantCulture) + " hopp",
            ExposureMap.Percent(result.ShareOf(reach.Amount)),
            id == _selectedId);
    }

    /// <summary>Makes the chosen service's detail: its share, its way, the transactions and what it rests on.</summary>
    /// <param name="result">The side.</param>
    /// <param name="reach">The service.</param>
    /// <returns>The detail.</returns>
    private ExposureDetail DetailOf(ExposureResult result, ExposureReach reach)
    {
        var way = result.Direction == ExposureDirection.Out ? reach.Path : [.. reach.Path.Reverse()];
        var crumbs = way.Select((key, index) => new ExposureCrumb(
                _names.NameOf(key), _names.LookOf(key).IsDashed, index < way.Count - 1))
            .ToList();
        return new ExposureDetail(
            _names.LookOf(reach.NodeKey),
            _names.NameOf(reach.NodeKey),
            ExposureValues.After(AmountText.WithUnit(reach.Amount), ValueOf(result, [reach])),
            ExposureMap.Percent(result.ShareOf(reach.Amount)),
            result.Direction == ExposureDirection.Out ? "av det som gått ut" : "av det som kommit in",
            crumbs,
            TransactionsText(reach),
            BasisOf(result, reach.NodeKey));
    }

    /// <summary>
    /// Gives what the money that reached some services was worth at the time (sketch KU1a), or an empty text when the
    /// graph has no value for a line it moved on.
    /// </summary>
    /// <param name="result">The side.</param>
    /// <param name="reaches">The services.</param>
    /// <returns>For example <c>≈ 23 400 kr</c>, or an empty text.</returns>
    private string ValueOf(ExposureResult result, IReadOnlyList<ExposureReach> reaches) => _scene is { } scene
        ? ExposureValues.OfServices(
            scene.Snapshot, result, reaches.Select(reach => reach.NodeKey).ToHashSet(StringComparer.Ordinal))
        : string.Empty;

    /// <summary>Writes the transactions on the way, each shortened.</summary>
    /// <param name="reach">The service.</param>
    /// <returns>For example <c>tx 8b7d…11fe</c> or <c>2 tx: 8b7d…11fe · e03a…77c1</c>.</returns>
    private static string TransactionsText(ExposureReach reach)
    {
        var ids = string.Join(" · ", reach.Transactions.Select(tx => GraphScene.Shorten(tx.Id)));
        return reach.Transactions.Count == 1
            ? "tx " + ids
            : reach.Transactions.Count.ToString(CultureInfo.InvariantCulture) + " tx: " + ids;
    }

    /// <summary>
    /// Gives what the share rests on: the method, the analysis, the clustering of a cluster subject, and the service's
    /// labels with where they came from.
    /// </summary>
    /// <param name="result">The side.</param>
    /// <param name="service">The service's node.</param>
    /// <returns>The chips.</returns>
    private List<ExposureBasis> BasisOf(ExposureResult result, string service)
    {
        var basis = new List<ExposureBasis>
        {
            new(result.Method.ToString(), "Icon.Layers", "Spårningsmetoden, som är vald för grafen"),
            new(ExposureResult.Analysis, "Icon.Exposure", "Analysen som räknar andelarna"),
        };
        if (Subject?.Node is { Node.Kind: GraphNodeKind.Cluster } cluster)
        {
            basis.Add(new ExposureBasis(
                "Klustring", "Icon.Kind.Wallet", $"{cluster.Name}: {cluster.Count}, enligt heuristikerna"));
        }

        foreach (var label in _scene?.Snapshot.Labels.GetValueOrDefault(service) ?? [])
        {
            var tip = string.Join("\n", label.Origins.Select(origin => origin.Describe()));
            basis.Add(new ExposureBasis(label.Name ?? VisualLanguage.Of(VisualLanguage.SortOf(label.Kind)).Name,
                label.Kind == Modules.Attribution.Contracts.LabelKind.Sanctioned ? "Icon.Kind.Ban" : "Icon.Tag", tip));
        }

        return basis;
    }
}
