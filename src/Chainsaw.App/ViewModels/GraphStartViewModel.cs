using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>What the empty graph guides to (sketch GR6c).</summary>
public enum GraphStartMode
{
    /// <summary>A graph is shown, or it is being read: nothing to guide.</summary>
    None,

    /// <summary>No case is open: Nytt ärende in the middle and the latest cases around it.</summary>
    NoCase,

    /// <summary>The open case has no start point yet: the empty start point with a field in it.</summary>
    NoStartPoint,
}

/// <summary>A recent case around Nytt ärende: its number and when it was last saved.</summary>
/// <param name="CaseNumber">The case number.</param>
/// <param name="ChangedText">When it was last saved, for example <c>ändrad i går 16:02</c>.</param>
/// <remarks>The title and the start points lie in the encrypted file, so they are not shown before it is opened.</remarks>
public sealed record RecentCaseItem(string CaseNumber, string ChangedText) : IStartRingItem
{
    /// <inheritdoc />
    public string Key => CaseNumber;
}

/// <summary>A form of start point that is recognised, lit while the text in the field is of that form.</summary>
/// <param name="Name">The form's name, for example <c>Bitcoin-adress</c>.</param>
/// <param name="Form">What it looks like, short, for example <c>bc1… 1… 3…</c>.</param>
/// <param name="Tip">What it is, in full, shown on hover.</param>
/// <param name="IsLit">Whether the text in the field is of this form.</param>
public sealed record StartPointForm(string Name, string Form, string Tip, bool IsLit);

/// <summary>
/// The empty graph that guides (sketch GR6c): with no case, Nytt ärende in the middle and the latest cases around it;
/// in a case without a start point, the empty start point with a field in it that says what a start point is, lights
/// the form that the text matches, takes Ctrl+V anywhere on the surface and says that nothing is fetched until the
/// start point is expanded, and through which exit. The steps after it stand faint. No WPF, so that it is tested.
/// </summary>
public sealed partial class GraphStartViewModel : ObservableObject
{
    /// <summary>How many recent cases stand around Nytt ärende.</summary>
    public const int RecentCount = 4;

    /// <summary>The most characters the start point's field takes: room for a chain's name in front of any value.</summary>
    public const int MaximumInput = ChainStartPoint.MaximumLength * 2;

    /// <summary>What the start point's field says while it is empty.</summary>
    public const string Placeholder = "Klistra in en adress eller tx";

    /// <summary>What is said when the text is not an address or a tx.</summary>
    public const string NotRecognisedText = "Det känns inte igen som en adress eller en tx.";

    /// <summary>What the foot says while no exit is connected.</summary>
    public const string OfflineFootText = "Offline: ingen utgång ansluten";

    /// <summary>What the start point says while no exit is connected: it can be added, but nothing fetched.</summary>
    public const string OfflineSendText =
        "Offline: startpunkten kan läggas till, men inget hämtas förrän en utgång är ansluten i titelraden.";

    /// <summary>The steps of a case, shown as dots the first time (sketch GR6c).</summary>
    public static readonly IReadOnlyList<string> Course = ["Ärende", "Startpunkt", "Expandera", "Uttaget", "Bevaka"];

    /// <summary>The month names, short, so that the text does not depend on the computer's culture.</summary>
    private static readonly string[] Months =
        ["jan", "feb", "mars", "apr", "maj", "juni", "juli", "aug", "sep", "okt", "nov", "dec"];

    /// <summary>The forms that are recognised: the chain, the kind, the name, the short form and the tip.</summary>
    private static readonly (Chain Chain, StartPointKind Kind, string Name, string Form, string Tip)[] Forms =
    [
        (Chain.Bitcoin, StartPointKind.Address, "Bitcoin-adress", "bc1… 1… 3…",
            "Alla tre sorternas adresser: bc1 (SegWit och Taproot), 1 och 3"),
        (Chain.Ethereum, StartPointKind.Address, "Ethereum-adress", "0x + 40",
            "0x följt av 40 hexadecimala tecken; kontrollsumman (EIP-55) prövas"),
        (Chain.Bitcoin, StartPointKind.Transaction, "Bitcoin-tx", "64 tecken",
            "En txid: 64 hexadecimala tecken"),
        (Chain.Ethereum, StartPointKind.Transaction, "Ethereum-tx", "0x + 64",
            "En tx-hash: 0x följt av 64 hexadecimala tecken"),
    ];

    /// <summary>The window's open case.</summary>
    private readonly CaseSession _session;

    /// <summary>Opens a case and adds a start point.</summary>
    private readonly CaseWorkflow _workflow;

    /// <summary>The time zone the times are told in.</summary>
    private readonly TimeZoneInfo _zone;

    /// <summary>The clock, for <em>i dag</em> and <em>i går</em>.</summary>
    private readonly IClock _clock;

    /// <summary>The UI thread, which the saved piles are shown on once they are read.</summary>
    private readonly IUiThread _ui;

    /// <summary>The exit's name while it is connected, for example <c>Stockholm</c> or <c>Windows</c>.</summary>
    private string? _exitName;

    /// <summary>Creates the empty graph.</summary>
    /// <param name="session">The window's open case.</param>
    /// <param name="workflow">Opens a case and adds a start point.</param>
    /// <param name="ui">The UI thread.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="zone">The time zone the times are told in; the computer's own when none is given.</param>
    public GraphStartViewModel(
        CaseSession session, CaseWorkflow workflow, IUiThread ui, IClock clock, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(ui);
        _session = session;
        _workflow = workflow;
        _clock = clock;
        _ui = ui;
        _zone = zone ?? TimeZoneInfo.Local;
        workflow.CasesListed += (_, _) => ui.Post(ShowRing);
        ShowRing();
        ShowSend();
    }

    /// <summary>Raised when Nytt ärende is chosen, so that the panel opens (sketch NC1a).</summary>
    public event EventHandler? NewCaseWanted;

    /// <summary>Raised when <em>Alla ärenden …</em> is chosen, so that the case picker folds out.</summary>
    public event EventHandler? AllCasesWanted;

    /// <summary>Raised when <em>Så spårar man i Chainsaw</em> is chosen, so that the Guide opens beside the work.</summary>
    public event EventHandler<GuideRequest>? GuideWanted;

    /// <summary>Gets what the empty graph guides to.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNoCase), nameof(IsNoStartPoint), nameof(IsShown))]
    public partial GraphStartMode Mode { get; private set; }

    /// <summary>Gets whether no case is open.</summary>
    public bool IsNoCase => Mode == GraphStartMode.NoCase;

    /// <summary>Gets whether the open case has no start point.</summary>
    public bool IsNoStartPoint => Mode == GraphStartMode.NoStartPoint;

    /// <summary>Gets whether the empty graph is shown instead of a text.</summary>
    public bool IsShown => Mode != GraphStartMode.None;

    /// <summary>
    /// Gets the loose cases on the ring, the most recently saved first: the latest <see cref="RecentCount"/> that lie
    /// in no pile, and those taken out of a pile while Chainsaw runs (sketch GR8a).
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<RecentCaseItem> Recent { get; private set; } = [];

    /// <summary>
    /// Gets whether there are no cases at all, the first time: then a little more is said, and the course is shown.
    /// </summary>
    public bool IsFirstTime => Ring.Count == 0;

    /// <summary>Gets whether there are cases or piles to stand around Nytt ärende.</summary>
    public bool HasRecent => Ring.Count > 0;

    /// <summary>Gets the text in the start point's field.</summary>
    [ObservableProperty]
    public partial string StartPointText { get; set; } = string.Empty;

    /// <summary>Gets the forms of start point, the one the text matches lit.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<StartPointForm> FormsShown { get; private set; } = Light(null);

    /// <summary>Gets whether the text is a start point that can be added.</summary>
    [ObservableProperty]
    public partial bool CanAdd { get; private set; }

    /// <summary>Gets whether the text has been recognised, so that the field's edge and flag go quiet.</summary>
    [ObservableProperty]
    public partial bool IsRecognised { get; private set; }

    /// <summary>Gets what went wrong when the start point was added, or an empty text.</summary>
    [ObservableProperty]
    public partial string ErrorText { get; private set; } = string.Empty;

    /// <summary>Gets whether a start point is being added or a case opened.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>Gets whether an exit is connected (or Windows is the way out), so that lookups can be made.</summary>
    [ObservableProperty]
    public partial bool IsOnline { get; private set; }

    /// <summary>Gets what the start point says about fetching, and through which exit.</summary>
    [ObservableProperty]
    public partial string SendText { get; private set; } = OfflineSendText;

    /// <summary>
    /// Gets whether the foot says that Chainsaw is offline. A connected exit is not repeated there, since the title bar
    /// already shows it (the user's decision 2026-11-07).
    /// </summary>
    [ObservableProperty]
    public partial bool IsFootOffline { get; private set; } = true;

    /// <summary>Shows what the graph has: no case, a case without a start point, or a graph.</summary>
    /// <param name="mode">What to guide to.</param>
    public void Show(GraphStartMode mode)
    {
        if (mode != Mode)
        {
            StartPointText = string.Empty;
            ErrorText = string.Empty;
        }

        if (mode != GraphStartMode.NoCase)
        {
            ClosePile();
        }

        Mode = mode;
    }

    /// <summary>Shows the exit: its name while it is connected, or offline.</summary>
    /// <param name="isOnline">Whether lookups can be made.</param>
    /// <param name="name">
    /// The exit's name, for example <c>Stockholm</c> or <c>Windows</c>; <see langword="null"/> while it is not known.
    /// </param>
    public void ShowExit(bool isOnline, string? name)
    {
        IsOnline = isOnline;
        _exitName = isOnline ? name : null;
        ShowSend();
    }

    /// <summary>Puts pasted text in the start point's field, when the case has no start point.</summary>
    /// <param name="text">The text from the clipboard.</param>
    /// <returns><see langword="true"/> when it was taken.</returns>
    public bool Paste(string? text)
    {
        if (!IsNoStartPoint || string.IsNullOrWhiteSpace(text) || text.Length > MaximumInput)
        {
            return false;
        }

        StartPointText = text.Trim();
        return true;
    }

    /// <summary>Gives when a case was last saved, in words, for example <c>ändrad i går 16:02</c>.</summary>
    /// <param name="savedUtc">When it was saved.</param>
    /// <param name="nowUtc">Now.</param>
    /// <param name="zone">The time zone to tell it in.</param>
    /// <returns>The text.</returns>
    public static string ChangedText(DateTimeOffset savedUtc, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var saved = TimeZoneInfo.ConvertTime(savedUtc, zone);
        var today = TimeZoneInfo.ConvertTime(nowUtc, zone).Date;
        var time = saved.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var month = Months[saved.Month - 1];
        if (saved.Date == today)
        {
            return $"ändrad i dag {time}";
        }

        if (saved.Date == today.AddDays(-1))
        {
            return $"ändrad i går {time}";
        }

        return saved.Year == today.Year ? $"ändrad {saved.Day} {month}" : $"ändrad {saved.Day} {month} {saved.Year}";
    }

    /// <summary>Opens Nytt ärende.</summary>
    [RelayCommand]
    private void NewCase() => NewCaseWanted?.Invoke(this, EventArgs.Empty);

    /// <summary>Asks for every case in the folder, through the case picker.</summary>
    [RelayCommand]
    private void AllCases() => AllCasesWanted?.Invoke(this, EventArgs.Empty);

    /// <summary>Opens <em>Så spårar man i Chainsaw</em> in the Guide beside the work.</summary>
    [RelayCommand]
    private void Guide() => GuideWanted?.Invoke(this, GuideRequest.ForTopic(GuideTopic.HowToTrace));

    /// <summary>Opens a recent case, after saving and closing none (no case is open here).</summary>
    /// <param name="item">The case.</param>
    /// <returns>A task that completes when the case is open or why not is shown.</returns>
    [RelayCommand]
    private async Task OpenCaseAsync(RecentCaseItem? item)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var reply = await _workflow.OpenAsync(item.CaseNumber);
            ErrorText = reply.IsError ? reply.Text : string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Adds the start point in the field to the open case (Enter).</summary>
    /// <returns>A task that completes when it is added or why not is shown.</returns>
    [RelayCommand]
    private async Task AddAsync()
    {
        if (IsBusy || _session.Current is null)
        {
            return;
        }

        var startPoint = StartPointTextParser(StartPointText);
        if (!startPoint.IsRecognised)
        {
            ErrorText = NotRecognisedText;
            return;
        }

        if (!startPoint.IsSupported)
        {
            ErrorText = CaseWorkflow.NotBuiltText(startPoint);
            return;
        }

        IsBusy = true;
        try
        {
            var reply = await _workflow.AddStartPointAsync(startPoint);
            ErrorText = reply.IsError ? reply.Text : string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Lights the form the text matches, and says whether it can be added, as the text changes.</summary>
    /// <param name="value">The text.</param>
    partial void OnStartPointTextChanged(string value)
    {
        ErrorText = string.Empty;
        var startPoint = StartPointTextParser(value);
        IsRecognised = startPoint.IsRecognised;
        CanAdd = startPoint.IsSupported;
        FormsShown = Light(startPoint);
    }

    /// <summary>Recognises the field's text, with its chain in front when one is written.</summary>
    /// <param name="text">The text.</param>
    /// <returns>What it is.</returns>
    private static ChainStartPoint StartPointTextParser(string text) => Links.StartPointText.Parse(text);

    /// <summary>Gives the forms with the one the start point is of lit.</summary>
    /// <param name="startPoint">The recognised start point, or <see langword="null"/> for none.</param>
    /// <returns>The forms.</returns>
    private static IReadOnlyList<StartPointForm> Light(ChainStartPoint? startPoint) =>
    [
        .. Forms.Select(form => new StartPointForm(
            form.Name, form.Form, form.Tip,
            startPoint is not null && startPoint.Chain == form.Chain && startPoint.Kind == form.Kind)),
    ];

    /// <summary>Says what is fetched, and through which exit, or that nothing can be fetched now.</summary>
    private void ShowSend()
    {
        if (!IsOnline)
        {
            SendText = OfflineSendText;
            IsFootOffline = true;
            return;
        }

        var through = _exitName is { } name ? name : "utgången";
        SendText = $"Enter lägger till startpunkten. Inget hämtas förrän du expanderar den, och då genom {through}.";
        IsFootOffline = false;
    }
}
