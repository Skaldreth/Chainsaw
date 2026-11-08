using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Requests.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>A row in the list of the view Begäran (sketch BG1b): a cash-out, or an address to freeze.</summary>
public sealed partial class RequestExitRowViewModel : ObservableObject
{
    /// <summary>Creates the row.</summary>
    /// <param name="exit">The place.</param>
    public RequestExitRowViewModel(RequestExit exit)
    {
        ArgumentNullException.ThrowIfNull(exit);
        Exit = exit;
    }

    /// <summary>Gets the place.</summary>
    public RequestExit Exit { get; }

    /// <summary>Gets the row's key.</summary>
    public string Key => Exit.Key;

    /// <summary>Gets the name.</summary>
    public string Name => Exit.Name;

    /// <summary>Gets the second line: the chain, the deposits and their sum.</summary>
    public string Summary => Exit.Summary;

    /// <summary>Gets the sort's icon, drawn as in the graph.</summary>
    public string Icon => Exit.Kind == RequestKind.Freeze ? "Icon.Snow" : Exit.Look.Icon;

    /// <summary>Gets whether the frame is dashed, as the sort's is in the graph (an exchange without KYC).</summary>
    public bool IsDashed => Exit.Look.IsDashed;

    /// <summary>Gets whether the row is a freeze, drawn with the snowflake's ice blue.</summary>
    public bool IsFreeze => Exit.Kind == RequestKind.Freeze;

    /// <summary>Gets or sets whether the row is chosen.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Gets when a request to it was last exported, for the green mark's tooltip; empty when never.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExported))]
    public partial string ExportedText { get; set; } = string.Empty;

    /// <summary>Gets whether a request to it has been exported (the green mark, sketch BG1b).</summary>
    public bool IsExported => ExportedText.Length > 0;
}

/// <summary>
/// A chip under <em>Ta med</em> (sketch BG1b): a deposit, taken along or not. A whole edge is traced money, a dashed
/// one another deposit to the same address.
/// </summary>
public sealed partial class DepositChipViewModel : ObservableObject
{
    /// <summary>Creates the chip.</summary>
    /// <param name="deposit">The deposit.</param>
    /// <param name="text">What the chip says.</param>
    /// <param name="isOn">Whether it is taken along.</param>
    public DepositChipViewModel(RequestDeposit deposit, string text, bool isOn)
    {
        Deposit = deposit;
        Text = text;
        IsOn = isOn;
    }

    /// <summary>Gets the deposit.</summary>
    public RequestDeposit Deposit { get; }

    /// <summary>Gets what the chip says: when and how much.</summary>
    public string Text { get; }

    /// <summary>Gets whether traced money came in with it (a conclusion by the method).</summary>
    public bool IsTraced => Deposit.IsTraced;

    /// <summary>Gets the chip's tooltip.</summary>
    public string ToolTip => (IsTraced ? "Spårade pengar" : "Inte spårade pengar: en annan insättning till adressen")
        + " · tx " + Deposit.Transaction;

    /// <summary>Gets or sets whether it is taken along.</summary>
    [ObservableProperty]
    public partial bool IsOn { get; set; }
}

/// <summary>A row under <em>Exporterade</em>: the file, when, and its SHA-256.</summary>
/// <param name="Export">The export as sealed.</param>
/// <param name="When">When, in local time.</param>
public sealed record ExportRow(RequestExport Export, string When)
{
    /// <summary>Gets the file's name.</summary>
    public string FileName => Export.FileName;

    /// <summary>Gets the hash shortened to its ends, as the sketch writes it.</summary>
    public string ShortSha => Shorten(Export.Sha256);

    /// <summary>Gets the tooltip: the whole hash and where the file is.</summary>
    public string ToolTip => $"SHA-256 {Export.Sha256}\n{Export.Path}";

    /// <summary>Shortens a hash to its ends, as the sketch writes it.</summary>
    /// <param name="sha">The hash.</param>
    /// <returns>For example <c>3b7e09c1…f2a4</c>.</returns>
    public static string Shorten(string sha)
    {
        ArgumentNullException.ThrowIfNull(sha);
        return sha.Length > 12 ? $"{sha[..8]}…{sha[^4..]}" : sha;
    }
}

/// <summary>
/// The view Begäran (sketch BG1b): every cash-out traced money reached in the case, the chosen request as a large
/// preview of its Markdown, chips for the deposits and the trail, <em>Exportera .md …</em>, and <em>Exporterade</em>
/// with SHA-256. The list follows the graph shown, so it is what the user sees there; nothing is written until the
/// export, which the Requests module seals and journals. No WPF, so that it is tested in Chainsaw.App.Tests.
/// </summary>
public sealed partial class RequestsViewModel : ObservableObject
{
    /// <summary>What the view says while no cash-out is reached.</summary>
    public const string EmptyText =
        "Inga uttag än. En aktör hamnar här när spårade pengar når ett uttag, blått i grafen: en börs, en automat, "
        + "OTC eller ett kort.";

    /// <summary>What the list says under the rows.</summary>
    public const string HintText = "Blått i grafen är uttag. En aktör kommer hit när spårade pengar når den.";

    /// <summary>What the line under the preview says when Chainsaw knows no verified way to the recipient.</summary>
    public const string NoContactText =
        "Ingen verifierad kontaktväg för {0} finns i Chainsaw. Använd mottagarens egen väg för myndigheter.";

    /// <summary>The toast's title after an export.</summary>
    public const string ExportedTitle = "Begäran exporterad och journalförd";

    /// <summary>The toast's title when an export failed.</summary>
    public const string ExportFailedTitle = "Begäran kunde inte exporteras";

    /// <summary>The requests: templates, exports and contacts.</summary>
    private readonly IRequestService _requests;

    /// <summary>The open case.</summary>
    private readonly CaseSession _session;

    /// <summary>Where the user saves a request.</summary>
    private readonly ISaveFilePicker _picker;

    /// <summary>The UI thread, where the exports read off it are shown.</summary>
    private readonly IUiThread _ui;

    /// <summary>The toaster, which says what an export did; or <see langword="null"/>.</summary>
    private readonly Toaster? _toaster;

    /// <summary>The zone local times are written in.</summary>
    private readonly TimeZoneInfo _zone;

    /// <summary>
    /// The deposits the user chose, per row, by deposit key; a row the user has not touched takes its traced ones.
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _chosen = new(StringComparer.Ordinal);

    /// <summary>The rows whose trail the user left out.</summary>
    private readonly HashSet<string> _withoutTrail = new(StringComparer.Ordinal);

    /// <summary>The exports of the open case, oldest first.</summary>
    private IReadOnlyList<RequestExport> _exports = [];

    /// <summary>
    /// Gets the reading of the open case's exports, which runs off the UI thread when a case is opened; complete when
    /// none is running.
    /// </summary>
    public Task ExportsRead { get; private set; } = Task.CompletedTask;

    /// <summary>Creates the view.</summary>
    /// <param name="requests">The requests.</param>
    /// <param name="session">The open case.</param>
    /// <param name="picker">Where the user saves a request.</param>
    /// <param name="ui">The UI thread.</param>
    /// <param name="toaster">The toaster, or <see langword="null"/>.</param>
    /// <param name="zone">The zone local times are written in; the computer's own when <see langword="null"/>.</param>
    public RequestsViewModel(
        IRequestService requests,
        CaseSession session,
        ISaveFilePicker picker,
        IUiThread ui,
        Toaster? toaster = null,
        TimeZoneInfo? zone = null)
    {
        _requests = requests;
        _session = session;
        _picker = picker;
        _ui = ui;
        _toaster = toaster;
        _zone = zone ?? TimeZoneInfo.Local;
        session.Changed += (_, _) => OnCaseChanged();
        OnCaseChanged();
    }

    /// <summary>Gets the rows: the cash-outs first, then the addresses that can be frozen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExits), nameof(CountText))]
    public partial IReadOnlyList<RequestExitRowViewModel> Exits { get; private set; } = [];

    /// <summary>Gets whether any request can be made.</summary>
    public bool HasExits => Exits.Count > 0;

    /// <summary>Gets the list's heading (sketch BG1b), for example <c>Uttag som nåtts i ärendet · 3</c>.</summary>
    public string CountText => string.Create(
        CultureInfo.InvariantCulture, $"Uttag som nåtts i ärendet · {Exits.Count(row => !row.IsFreeze)}");

    /// <summary>Gets the chosen row, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrail))]
    public partial RequestExitRowViewModel? Selected { get; private set; }

    /// <summary>Gets the chosen row's deposits as chips.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<DepositChipViewModel> Chips { get; private set; } = [];

    /// <summary>Gets whether the chosen row has a trail to take along, so that its chip is shown.</summary>
    public bool HasTrail => Selected?.Exit.Trail is not null;

    /// <summary>Gets whether the trail is taken along.</summary>
    [ObservableProperty]
    public partial bool IsTrailOn { get; private set; }

    /// <summary>Gets the language the request is written in.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LanguageText))]
    public partial RequestLanguage Language { get; private set; } = RequestLanguage.Swedish;

    /// <summary>Gets the language's name on its button.</summary>
    public string LanguageText => Language == RequestLanguage.English ? "English" : "Svenska";

    /// <summary>Gets the request as it will be exported, or <see langword="null"/> when none is chosen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocument))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial RequestDocument? Document { get; private set; }

    /// <summary>Gets whether a request is shown.</summary>
    public bool HasDocument => Document is not null;

    /// <summary>Gets why no request is shown for the chosen row, for example that no deposit is taken along.</summary>
    [ObservableProperty]
    public partial string Problem { get; private set; } = string.Empty;

    /// <summary>Gets what the line under the preview says about how the recipient is reached.</summary>
    [ObservableProperty]
    public partial string ContactText { get; private set; } = string.Empty;

    /// <summary>Gets the exported requests, newest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExports))]
    public partial IReadOnlyList<ExportRow> Exports { get; private set; } = [];

    /// <summary>Gets whether any request has been exported.</summary>
    public bool HasExports => Exports.Count > 0;

    /// <summary>Gets whether an export is being written.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial bool IsExporting { get; private set; }

    /// <summary>Follows the graph shown: the list is found again; the chosen row and chips stay where they can.
    /// </summary>
    /// <param name="scene">The graph, or <see langword="null"/> when none is shown.</param>
    public void Update(GraphScene? scene)
    {
        var found = scene is null ? [] : RequestExits.Find(scene);
        var key = Selected?.Key;
        Exits = [.. found.Select(exit => new RequestExitRowViewModel(exit))];
        MarkExported();
        Choose(Exits.FirstOrDefault(row => row.Key == key) ?? Exits.FirstOrDefault());
    }

    /// <summary>
    /// Opens the request for a node, as the graph's <em>Begäran …</em> and the inspector's
    /// <em>Begäran om frysning …</em> ask (sketch TK1a).
    /// </summary>
    /// <param name="nodeKey">The node.</param>
    /// <param name="kind">What the request asks for.</param>
    /// <returns><see langword="true"/> when the node has such a request.</returns>
    public bool Open(string nodeKey, RequestKind kind)
    {
        var row = Exits.FirstOrDefault(other => other.Exit.NodeKey == nodeKey && other.Exit.Kind == kind);
        if (row is null)
        {
            return false;
        }

        Choose(row);
        return true;
    }

    /// <summary>Chooses a row in the list.</summary>
    /// <param name="row">The row.</param>
    [RelayCommand]
    public void Pick(RequestExitRowViewModel? row)
    {
        if (row is not null)
        {
            Choose(row);
        }
    }

    /// <summary>Takes a deposit along or leaves it out.</summary>
    /// <param name="chip">The deposit's chip.</param>
    [RelayCommand]
    public void ToggleDeposit(DepositChipViewModel? chip)
    {
        if (chip is null || Selected is not { } row)
        {
            return;
        }

        chip.IsOn = !chip.IsOn;
        _chosen[row.Key] = [.. Chips.Where(other => other.IsOn).Select(other => DepositKey(other.Deposit))];
        Compose();
    }

    /// <summary>Takes the trail along or leaves it out.</summary>
    [RelayCommand]
    public void ToggleTrail()
    {
        if (Selected is not { } row || !HasTrail)
        {
            return;
        }

        if (!_withoutTrail.Remove(row.Key))
        {
            _withoutTrail.Add(row.Key);
        }

        IsTrailOn = !_withoutTrail.Contains(row.Key);
        Compose();
    }

    /// <summary>Writes the request in the other language.</summary>
    [RelayCommand]
    public void ToggleLanguage()
    {
        Language = Language == RequestLanguage.Swedish ? RequestLanguage.English : RequestLanguage.Swedish;
        Compose();
    }

    /// <summary>
    /// <em>Exportera .md …</em> (Ctrl+S): asks where, writes the file off the UI thread, never over another, and says
    /// in a toast what was written; the Requests module seals it with a line in the journal.
    /// </summary>
    /// <returns>A task that completes when the export is done or has failed.</returns>
    [RelayCommand(CanExecute = nameof(CanExport))]
    public async Task ExportAsync()
    {
        if (_session.Current is not { } open || Draft() is not { } draft || Document is not { } document)
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
            var made = draft with { MadeUtc = document.MadeUtc };
            var export = await Task.Run(() => _requests.Export(open, made, path));
            var told = $"{export.FileName} · SHA-256 {ExportRow.Shorten(export.Sha256)}";
            _toaster?.Show(ToastKind.Success, ExportedTitle, told);
            await LoadExportsAsync(open);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or CaseException)
        {
            _toaster?.Show(ToastKind.Error, ExportFailedTitle, ReportViewModel.ExportFailure(exception));
        }
        finally
        {
            IsExporting = false;
        }
    }

    /// <summary>Says whether the request can be exported now.</summary>
    /// <returns><see langword="true"/> when a request is shown and none is being written.</returns>
    private bool CanExport() => Document is not null && !IsExporting && _session.Current is not null;

    /// <summary>Chooses a row: its chips, its trail and its request.</summary>
    /// <param name="row">The row, or <see langword="null"/> for none.</param>
    private void Choose(RequestExitRowViewModel? row)
    {
        foreach (var other in Exits)
        {
            other.IsSelected = ReferenceEquals(other, row);
        }

        Selected = row;
        if (row is null)
        {
            Chips = [];
            IsTrailOn = false;
            ContactText = string.Empty;
            Compose();
            return;
        }

        var chosen = _chosen.GetValueOrDefault(row.Key);
        Chips = [.. row.Exit.Deposits.Select(deposit => new DepositChipViewModel(
            deposit,
            $"{RequestExits.When(deposit, _zone)} · {AmountText.WithUnit(deposit.Amount)}",
            chosen?.Contains(DepositKey(deposit)) ?? deposit.IsTraced))];
        IsTrailOn = row.Exit.Trail is not null && !_withoutTrail.Contains(row.Key);
        ContactText = Contact(row.Exit.Target.Name);
        Compose();
    }

    /// <summary>Composes the chosen request again, or says why none can be.</summary>
    private void Compose()
    {
        Problem = string.Empty;
        if (_session.Current is not { } open || Draft() is not { } draft)
        {
            Document = null;
            return;
        }

        if (draft.Kind == RequestKind.Information && draft.Deposits.Count == 0)
        {
            Document = null;
            Problem = "Välj minst en insättning att ta med.";
            return;
        }

        Document = _requests.Compose(open, draft);
    }

    /// <summary>Makes the draft from the chosen row, the chips and the trail.</summary>
    /// <returns>The draft, or <see langword="null"/> when no row is chosen.</returns>
    private RequestDraft? Draft()
    {
        if (Selected?.Exit is not { } exit)
        {
            return null;
        }

        var deposits = Chips.Where(chip => chip.IsOn).Select(chip => chip.Deposit).ToList();
        return new RequestDraft(
            exit.Kind, Language, TargetOf(exit, deposits), deposits, IsTrailOn ? exit.Trail : null, _zone)
        {
            Holding = exit.Holding,
        };
    }

    /// <summary>
    /// Gives who the request concerns: for a request for information only the addresses the chosen deposits went to,
    /// so that it never asks about customers who are not in the case (CLAUDE.md, BG1b); for a freeze the address
    /// that holds the token.
    /// </summary>
    /// <param name="exit">The place.</param>
    /// <param name="deposits">The deposits taken along.</param>
    /// <returns>The recipient.</returns>
    private static RequestTarget TargetOf(RequestExit exit, IReadOnlyList<RequestDeposit> deposits)
    {
        var addresses = deposits.Select(deposit => deposit.To).Distinct().ToList();
        return exit.Kind == RequestKind.Freeze || addresses.Count == 0
            ? exit.Target
            : new RequestTarget(exit.Target.Name, exit.Target.Kind, exit.Target.Chain, addresses);
    }

    /// <summary>Says how the recipient is reached: the verified ways with source and date, or honestly none.</summary>
    /// <param name="name">The recipient.</param>
    /// <returns>The line.</returns>
    private string Contact(string name)
    {
        var ways = _requests.ContactsFor(name);
        if (ways.Count == 0)
        {
            return string.Format(CultureInfo.InvariantCulture, NoContactText, name);
        }

        return "Kontaktväg: " + string.Join("; ", ways.Select(way => string.Create(
            CultureInfo.InvariantCulture,
            $"{way.Way} (enligt {way.Source.Host}, kontrollerad {way.Checked:yyyy-MM-dd})")));
    }

    /// <summary>Reads the open case's exports anew, or forgets them without a case.</summary>
    private void OnCaseChanged()
    {
        _chosen.Clear();
        _withoutTrail.Clear();
        if (_session.Current is { } open)
        {
            ExportsRead = LoadExportsAsync(open);
        }
        else
        {
            ShowExports([]);
            Update(null);
        }
    }

    /// <summary>Reads the exports off the UI thread and shows them, if the case is still the one open.</summary>
    /// <param name="open">The case.</param>
    /// <returns>A task that completes when they are read.</returns>
    private async Task LoadExportsAsync(ICase open)
    {
        IReadOnlyList<RequestExport> exports;
        try
        {
            exports = await Task.Run(() => _requests.ListExports(open));
        }
        catch (CaseException)
        {
            // The case closed meanwhile; the next case reads its own.
            return;
        }

        _ui.Post(() =>
        {
            if (ReferenceEquals(_session.Current, open))
            {
                ShowExports(exports);
            }
        });
    }

    /// <summary>Shows the exports, newest first, and marks the rows that have one.</summary>
    /// <param name="exports">The exports, oldest first.</param>
    private void ShowExports(IReadOnlyList<RequestExport> exports)
    {
        _exports = exports;
        Exports = [.. exports.Reverse().Select(export => new ExportRow(export, LocalTime(export.Utc)))];
        MarkExported();
    }

    /// <summary>Puts the green mark on every row a request has been exported to.</summary>
    private void MarkExported()
    {
        foreach (var row in Exits)
        {
            var last = _exports.LastOrDefault(row.Exit.IsExportedBy);
            row.ExportedText = last is null ? string.Empty : "Exporterad " + LocalTime(last.Utc);
        }
    }

    /// <summary>Writes a time in the local zone, as the list does.</summary>
    /// <param name="utc">The time.</param>
    /// <returns>For example <c>2026-11-07 14:20</c>.</returns>
    private string LocalTime(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, _zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Gives a deposit's key: its tx, receiver and amount, since a tx can pay an address twice.</summary>
    /// <param name="deposit">The deposit.</param>
    /// <returns>The key.</returns>
    private static string DepositKey(RequestDeposit deposit) =>
        $"{deposit.Transaction}|{deposit.To}|{deposit.Amount.Units}";
}
