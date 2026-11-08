using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Requests.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>A part's row under <em>Delar</em> (sketch RP1b): its icon, name, what it holds and its switch.</summary>
public sealed partial class ReportPartRowViewModel : ObservableObject
{
    /// <summary>Creates the row.</summary>
    /// <param name="part">The part.</param>
    /// <param name="name">Its name.</param>
    /// <param name="sub">What it holds, in a few words.</param>
    /// <param name="icon">Its icon's resource key.</param>
    /// <param name="isOn">Whether it is on.</param>
    public ReportPartRowViewModel(ReportPart part, string name, string sub, string icon, bool isOn)
    {
        Part = part;
        Name = name;
        Sub = sub;
        Icon = icon;
        IsOn = isOn;
    }

    /// <summary>Gets the part.</summary>
    public ReportPart Part { get; }

    /// <summary>Gets its name.</summary>
    public string Name { get; }

    /// <summary>Gets or sets what it holds; the part <em>Belopp i SEK</em> says honestly when no rate is known.</summary>
    [ObservableProperty]
    public partial string Sub { get; set; }

    /// <summary>Gets its icon's resource key.</summary>
    public string Icon { get; }

    /// <summary>Gets or sets whether it is in the report.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    public partial bool IsOn { get; set; }

    /// <summary>Gets the row's tooltip: what a click does.</summary>
    public string ToolTip => IsOn ? "Ta bort ur rapporten" : "Ta med i rapporten";
}

/// <summary>How a line of the raw Markdown is coloured, only to tell headings, tables and text apart.</summary>
public enum RawLineKind
{
    /// <summary>Running text.</summary>
    Text,

    /// <summary>A heading (<c>#</c>, <c>##</c>).</summary>
    Heading,

    /// <summary>A table's row.</summary>
    Table,

    /// <summary>A quiet line: a note or a reason.</summary>
    Faint,
}

/// <summary>A line of the report's Markdown as the view <em>Markdown</em> shows it.</summary>
/// <param name="Text">The line.</param>
/// <param name="Kind">How it is coloured.</param>
public sealed record RawLine(string Text, RawLineKind Kind)
{
    /// <summary>Reads the lines of a Markdown text.</summary>
    /// <param name="markdown">The text.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<RawLine> Of(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return [.. markdown.Split('\n').Select(line => new RawLine(line, KindOf(line)))];
    }

    /// <summary>Tells what a line is by how it starts.</summary>
    /// <param name="line">The line.</param>
    /// <returns>Its kind.</returns>
    private static RawLineKind KindOf(string line)
    {
        if (line.StartsWith('#'))
        {
            return RawLineKind.Heading;
        }

        if (line.StartsWith('|'))
        {
            return RawLineKind.Table;
        }

        return line.StartsWith('*') || line.StartsWith("  >", StringComparison.Ordinal)
            ? RawLineKind.Faint
            : RawLineKind.Text;
    }
}

/// <summary>A file under the report, exported and sealed: its name, and its SHA-256 in the tooltip.</summary>
/// <param name="Export">The export as sealed.</param>
/// <param name="When">When, in local time.</param>
public sealed record ReportExportRow(ReportExport Export, string When)
{
    /// <summary>Gets the file's name.</summary>
    public string FileName => Export.FileName;

    /// <summary>Gets the tooltip: when, the whole hash and where the file is.</summary>
    public string ToolTip => $"Exporterad {When}\nSHA-256 {Export.Sha256}\n{Export.Path}";
}

/// <summary>
/// The view Rapport (sketch RP1b): the report is built from what is marked in the graph, with a small picture of the
/// marking and its actors as chips, the parts as switches, a preview that can be switched to the file's Markdown, and
/// <em>Exportera .md …</em>, which the Requests module writes without overwriting, journals and seals with SHA-256.
/// The material is found and the report composed off the UI thread. No WPF, so that it is tested in
/// Chainsaw.App.Tests.
/// </summary>
public sealed partial class ReportViewModel : ObservableObject
{
    /// <summary>What the view says while nothing is marked.</summary>
    public const string EmptyText =
        "Markera aktörerna i grafen med Ctrl+klick eller en ruta. Spåren mellan dem blir rapportens spår.";

    /// <summary>The line under the preview: what never comes along, and that the export is journalled.</summary>
    public const string PrivacyText =
        "Offrens namn och anteckningar kommer aldrig med. Exporten journalförs med SHA-256.";

    /// <summary>The toast's title after an export.</summary>
    public const string ExportedTitle = "Exporterad och journalförd";

    /// <summary>The toast's text after an export, after the file's name.</summary>
    public const string ExportedText = " · en ny fil, inget skrevs över";

    /// <summary>The toast's title when an export failed.</summary>
    public const string ExportFailedTitle = "Rapporten kunde inte exporteras";

    /// <summary>What the part <em>Belopp i SEK</em> says when no trail has a rate in the case.</summary>
    public const string NoRateText = "Ingen kurs i ärendet för spåren än";

    /// <summary>What the part <em>Belopp i SEK</em> says when a rate is known.</summary>
    public const string RateText = "Kursen vid tx-tiden, med källa";

    /// <summary>The parts in the file's order, with their names, what they hold, icons and whether they start on.</summary>
    private static readonly (ReportPart Part, string Name, string Sub, string Icon, bool IsOn)[] PartTable =
    [
        (ReportPart.Summary, "Sammanfattning", "Två meningar om vart pengarna gick", "Icon.Pen", true),
        (ReportPart.Trail, "Spåret steg för steg", "Tid, UTC, belopp och tx för varje hopp", "Icon.Flow", true),
        (ReportPart.Chain, "Blockhöjd och blockhash", "Så att varje steg kan kontrolleras mot kedjan", "Icon.Hash", true),
        (ReportPart.CashOut, "Uttag", "Där pengarna lämnar kedjan, och om en begäran går", "Icon.Kind.Bank", true),
        (ReportPart.Conclusions, "Slutsatser med Varför", "Heuristik, version och tx bakom varje slutsats",
            "Icon.Question", true),
        (ReportPart.Labels, "Etiketter och källor", "Vilken källa, version och dag", "Icon.Tag", false),
        (ReportPart.Sek, "Belopp i SEK", RateText, "Icon.Coin", true),
        (ReportPart.Ledger, "Hashkedjans topphash", "Kontrollerad före exporten", "Icon.Shield", true),
    ];

    /// <summary>The reports: compose, check, export.</summary>
    private readonly IReportService _reports;

    /// <summary>The open case.</summary>
    private readonly CaseSession _session;

    /// <summary>Where the user saves the report.</summary>
    private readonly ISaveFilePicker _picker;

    /// <summary>The UI thread, where what is found off it is shown.</summary>
    private readonly IUiThread _ui;

    /// <summary>The clusters, whose heuristics a cluster's conclusion names; or <see langword="null"/>.</summary>
    private readonly IClusteringService? _clustering;

    /// <summary>The toaster, which says what an export did; or <see langword="null"/>.</summary>
    private readonly Toaster? _toaster;

    /// <summary>The zone local times are written in.</summary>
    private readonly TimeZoneInfo _zone;

    /// <summary>The graph and the marking the report was last asked to follow.</summary>
    private (GraphScene? Scene, IReadOnlyList<string> Picked) _source = (null, []);

    /// <summary>The material found from the marking.</summary>
    private ReportMaterial _material = ReportMaterial.Empty;

    /// <summary>The top of the hash chain as last checked, or <see langword="null"/>.</summary>
    private ReportLedger? _ledger;

    /// <summary>Counts the rebuilds, so that one overtaken by a newer is never shown.</summary>
    private int _version;

    /// <summary>Creates the view.</summary>
    /// <param name="reports">The reports.</param>
    /// <param name="session">The open case.</param>
    /// <param name="picker">Where the user saves the report.</param>
    /// <param name="ui">The UI thread.</param>
    /// <param name="clustering">The clusters, or <see langword="null"/>.</param>
    /// <param name="toaster">The toaster, or <see langword="null"/>.</param>
    /// <param name="zone">The zone local times are written in; the computer's own when <see langword="null"/>.</param>
    public ReportViewModel(
        IReportService reports,
        CaseSession session,
        ISaveFilePicker picker,
        IUiThread ui,
        IClusteringService? clustering = null,
        Toaster? toaster = null,
        TimeZoneInfo? zone = null)
    {
        _reports = reports;
        _session = session;
        _picker = picker;
        _ui = ui;
        _clustering = clustering;
        _toaster = toaster;
        _zone = zone ?? TimeZoneInfo.Local;
        Parts = [.. PartTable.Select(row => new ReportPartRowViewModel(row.Part, row.Name, row.Sub, row.Icon, row.IsOn))];
        session.Changed += (_, _) => OnCaseChanged();
        OnCaseChanged();
    }

    /// <summary>Raised when the user wants the graph, to change what is marked.</summary>
    public event EventHandler? GraphWanted;

    /// <summary>Gets the parts, in the file's order.</summary>
    public IReadOnlyList<ReportPartRowViewModel> Parts { get; }

    /// <summary>Gets how many parts are on, for example <c>6 av 8</c>.</summary>
    public string OnCountText => string.Create(
        CultureInfo.InvariantCulture, $"{Parts.Count(row => row.IsOn)} av {Parts.Count}");

    /// <summary>Gets the actors marked, as chips.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActors))]
    public partial IReadOnlyList<ReportActor> Actors { get; private set; } = [];

    /// <summary>Gets whether anything is marked, so that the report is shown rather than the hint.</summary>
    public bool HasActors => Actors.Count > 0;

    /// <summary>Gets the small picture of the marking (sketch RP1b).</summary>
    [ObservableProperty]
    public partial ReportPicture Picture { get; private set; } = ReportPicture.Empty;

    /// <summary>Gets the line beside the switch: actors, transactions and ends.</summary>
    [ObservableProperty]
    public partial string StatsText { get; private set; } = string.Empty;

    /// <summary>Gets the report as it will be exported, or <see langword="null"/> while none is composed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocument))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial RequestDocument? Document { get; private set; }

    /// <summary>Gets whether a report is composed.</summary>
    public bool HasDocument => Document is not null;

    /// <summary>Gets the report's Markdown line by line, for the view <em>Markdown</em>.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<RawLine> RawLines { get; private set; } = [];

    /// <summary>Gets whether the file's Markdown is shown rather than the preview.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreview))]
    public partial bool IsRaw { get; private set; }

    /// <summary>Gets whether the preview is shown.</summary>
    public bool IsPreview => !IsRaw;

    /// <summary>Gets the exported reports, newest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExports))]
    public partial IReadOnlyList<ReportExportRow> Exports { get; private set; } = [];

    /// <summary>Gets whether any report has been exported.</summary>
    public bool HasExports => Exports.Count > 0;

    /// <summary>Gets whether an export is being written.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial bool IsExporting { get; private set; }

    /// <summary>
    /// Gets the last rebuild, which finds the material and composes off the UI thread; complete when none is running.
    /// </summary>
    public Task Built { get; private set; } = Task.CompletedTask;

    /// <summary>Follows the graph shown and what is marked in it; the report is built again off the UI thread.</summary>
    /// <param name="scene">The graph, or <see langword="null"/> when none is shown.</param>
    /// <param name="picked">The actors marked.</param>
    /// <param name="places">Where each node stands, for the small picture.</param>
    public void Update(
        GraphScene? scene, IReadOnlyList<string> picked, IReadOnlyDictionary<string, GraphPoint> places)
    {
        ArgumentNullException.ThrowIfNull(picked);
        ArgumentNullException.ThrowIfNull(places);
        _source = (scene, [.. picked]);
        Picture = scene is null ? ReportPicture.Empty : ReportPicture.Of(scene, picked, places);
        Built = RebuildAsync();
    }

    /// <summary>
    /// Checks the hash chain again, when the view is shown, so that the preview's top hash is the case's as it stands.
    /// </summary>
    /// <returns>A task that completes when it is checked and the report composed again.</returns>
    public Task RefreshAsync() => Built = CheckAndRebuildAsync();

    /// <summary>Switches a part on or off.</summary>
    /// <param name="row">The part's row.</param>
    [RelayCommand]
    public void TogglePart(ReportPartRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        row.IsOn = !row.IsOn;
        OnPropertyChanged(nameof(OnCountText));
        Built = ComposeAsync(Interlocked.Increment(ref _version));
    }

    /// <summary>Shows the preview.</summary>
    [RelayCommand]
    public void ShowPreview() => IsRaw = false;

    /// <summary>Shows the file's Markdown.</summary>
    [RelayCommand]
    public void ShowRaw() => IsRaw = true;

    /// <summary><em>Ändra i grafen</em>: the graph, where the marking is changed.</summary>
    [RelayCommand]
    public void GoToGraph() => GraphWanted?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// <em>Exportera .md …</em> (Ctrl+S): asks where, writes the file off the UI thread, never over another, and says
    /// in a toast what was written; the Requests module checks the hash chain anew and seals the export in the journal.
    /// </summary>
    /// <returns>A task that completes when the export is done or has failed.</returns>
    [RelayCommand(CanExecute = nameof(CanExport))]
    public async Task ExportAsync()
    {
        if (_session.Current is not { } open || Document is not { } document)
        {
            return;
        }

        var path = _picker.PickRequestFile(document.FileName);
        if (path is null)
        {
            return;
        }

        IsExporting = true;
        try
        {
            var draft = Draft() with { MadeUtc = document.MadeUtc };
            var export = await Task.Run(() => _reports.Export(open, draft, path));
            _toaster?.Show(ToastKind.Success, ExportedTitle, export.FileName + ExportedText);
            await LoadExportsAsync(open);
            await CheckAndRebuildAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or CaseException)
        {
            _toaster?.Show(ToastKind.Error, ExportFailedTitle, exception.Message);
        }
        finally
        {
            IsExporting = false;
        }
    }

    /// <summary>Says whether the report can be exported now.</summary>
    /// <returns><see langword="true"/> when one is composed, none is being written and a case is open.</returns>
    private bool CanExport() => Document is not null && !IsExporting && _session.Current is not null;

    /// <summary>Makes the draft from the material shown and the parts switched on.</summary>
    /// <returns>The draft.</returns>
    private ReportDraft Draft() => DraftOf(_material);

    /// <summary>Finds the material off the UI thread and composes the report, unless a newer rebuild overtook it.
    /// </summary>
    /// <returns>A task that completes when it is shown or dropped.</returns>
    private async Task RebuildAsync()
    {
        var version = Interlocked.Increment(ref _version);
        var (scene, picked) = _source;
        if (_session.Current is not { } open || scene is null || picked.Count == 0)
        {
            Show(version, ReportMaterial.Empty, null);
            return;
        }

        try
        {
            var (material, document) = await Task.Run(() =>
            {
                var found = ReportSelection.Build(scene, picked, Clusters(open, scene, picked));
                return (found, found.Actors.Count == 0 ? null : _reports.Compose(open, DraftOf(found)));
            });
            _ui.Post(() => Show(version, material, document));
        }
        catch (CaseException)
        {
            // The case closed meanwhile; the next case builds its own.
        }
    }

    /// <summary>Composes the report again from the material found, after a part was switched.</summary>
    /// <param name="version">The rebuild's number.</param>
    /// <returns>A task that completes when it is shown or dropped.</returns>
    private async Task ComposeAsync(int version)
    {
        if (_session.Current is not { } open || _material.Actors.Count == 0)
        {
            return;
        }

        var draft = Draft();
        try
        {
            var document = await Task.Run(() => _reports.Compose(open, draft));
            _ui.Post(() => Show(version, _material, document));
        }
        catch (CaseException)
        {
            // The case closed meanwhile.
        }
    }

    /// <summary>Checks the hash chain off the UI thread, then builds the report again.</summary>
    /// <returns>A task that completes when it is done.</returns>
    private async Task CheckAndRebuildAsync()
    {
        if (_session.Current is { } open)
        {
            try
            {
                var ledger = await Task.Run(() => _reports.Check(open));
                if (ReferenceEquals(_session.Current, open))
                {
                    _ledger = ledger;
                }
            }
            catch (CaseException)
            {
                // The case closed meanwhile.
            }
        }

        await RebuildAsync();
    }

    /// <summary>Makes a draft of material, with the parts switched on now.</summary>
    /// <param name="material">The material.</param>
    /// <returns>The draft.</returns>
    private ReportDraft DraftOf(ReportMaterial material) => new(
        material.Trails,
        material.Conclusions,
        material.Labels,
        material.Method,
        Parts.Where(row => row.IsOn).Select(row => row.Part).ToHashSet(),
        _zone)
    {
        Ledger = _ledger,
    };

    /// <summary>Reads the clusters of the marked cluster nodes; none when clustering is not at hand.</summary>
    /// <param name="open">The case.</param>
    /// <param name="scene">The graph.</param>
    /// <param name="picked">The actors marked.</param>
    /// <returns>The clusters by node key.</returns>
    private Dictionary<string, Cluster> Clusters(ICase open, GraphScene scene, IReadOnlyList<string> picked)
    {
        var clusters = new Dictionary<string, Cluster>(StringComparer.Ordinal);
        if (_clustering is null)
        {
            return clusters;
        }

        foreach (var (key, address) in ReportSelection.ClustersWanted(scene, picked))
        {
            clusters[key] = _clustering.GetCluster(open, address);
        }

        return clusters;
    }

    /// <summary>Shows a rebuild, unless a newer one has begun since.</summary>
    /// <param name="version">The rebuild's number.</param>
    /// <param name="material">The material.</param>
    /// <param name="document">The report, or <see langword="null"/>.</param>
    private void Show(int version, ReportMaterial material, RequestDocument? document)
    {
        if (version != Volatile.Read(ref _version))
        {
            return;
        }

        _material = material;
        Actors = material.Actors;
        Document = document;
        RawLines = document is null ? [] : RawLine.Of(document.Markdown);
        StatsText = material.Actors.Count == 0 ? string.Empty : Stats(material);
        var sek = Parts.First(row => row.Part == ReportPart.Sek);
        sek.Sub = material.Trails.Any(trail => trail.Value is not null) ? RateText : NoRateText;
    }

    /// <summary>Writes the line beside the switch.</summary>
    /// <param name="material">The material.</param>
    /// <returns>For example <c>4 aktörer · 3 tx · 1 där pengarna lämnar eller spåret slutar</c>.</returns>
    private static string Stats(ReportMaterial material) => string.Create(
        CultureInfo.InvariantCulture,
        $"{material.Actors.Count} {(material.Actors.Count == 1 ? "aktör" : "aktörer")} · {material.TransactionCount} tx"
        + $" · {material.EndCount} där pengarna lämnar eller spåret slutar");

    /// <summary>Reads the open case's exports and checks its hash chain, or forgets them without a case.</summary>
    private void OnCaseChanged()
    {
        _ledger = null;
        if (_session.Current is { } open)
        {
            _ = LoadExportsAsync(open);
            Built = CheckAndRebuildAsync();
        }
        else
        {
            Exports = [];
            _source = (null, []);
            Picture = ReportPicture.Empty;
            Built = RebuildAsync();
        }
    }

    /// <summary>Reads the exports off the UI thread and shows them, if the case is still the one open.</summary>
    /// <param name="open">The case.</param>
    /// <returns>A task that completes when they are read.</returns>
    private async Task LoadExportsAsync(ICase open)
    {
        IReadOnlyList<ReportExport> exports;
        try
        {
            exports = await Task.Run(() => _reports.ListExports(open));
        }
        catch (CaseException)
        {
            return;
        }

        _ui.Post(() =>
        {
            if (ReferenceEquals(_session.Current, open))
            {
                Exports = [.. exports.Reverse().Select(export => new ReportExportRow(export, LocalTime(export.Utc)))];
            }
        });
    }

    /// <summary>Writes a time in the local zone.</summary>
    /// <param name="utc">The time.</param>
    /// <returns>For example <c>2026-11-08 14:40</c>.</returns>
    private string LocalTime(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, _zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
