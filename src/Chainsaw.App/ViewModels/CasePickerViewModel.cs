using System.Collections.ObjectModel;
using System.Globalization;
using Chainsaw.App.Links;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;
using Chainsaw.SharedKernel.Commands;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// A case file in the case picker (sketch ÄV1a): its number, when it was last saved, whether it is the open case and
/// whether it is the chosen row, which Enter opens.
/// </summary>
public sealed partial class CasePickerRow : ObservableObject
{
    /// <summary>Creates a row.</summary>
    /// <param name="caseNumber">The case number.</param>
    /// <param name="changedText">When it was last saved, in words, for example <c>ändrad i dag 14:02</c>.</param>
    /// <param name="isOpenCase">Whether it is the case open in the window.</param>
    /// <param name="placesLink">Whether Enter lays a link's start point in the case rather than opens it (LN1a).</param>
    public CasePickerRow(string caseNumber, string changedText, bool isOpenCase, bool placesLink = false)
    {
        CaseNumber = caseNumber;
        ChangedText = changedText;
        IsOpenCase = isOpenCase;
        PlacesLink = placesLink;
    }

    /// <summary>Gets whether Enter lays a link's start point in the case rather than opens it (sketch LN1a).</summary>
    public bool PlacesLink { get; }

    /// <summary>Gets what Enter does on the chosen row: <em>Enter öppnar</em>, or <em>Lägg i · Enter</em>.</summary>
    public string EnterHint => PlacesLink ? "Lägg i · Enter" : "Enter öppnar";

    /// <summary>Gets the case number.</summary>
    public string CaseNumber { get; }

    /// <summary>Gets when the case was last saved, in words.</summary>
    public string ChangedText { get; }

    /// <summary>Gets whether this is the case open in the window, marked with the accent and <em>Öppet</em>.</summary>
    public bool IsOpenCase { get; }

    /// <summary>Gets or sets whether this row is chosen, so that Enter or a second click opens it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEnterHint))]
    public partial bool IsChosen { get; set; }

    /// <summary>
    /// Gets whether the row says what Enter does: chosen, and not already open, unless a link is to be laid in it.
    /// </summary>
    public bool ShowsEnterHint => IsChosen && (PlacesLink || !IsOpenCase);
}

/// <summary>
/// The case picker under the case in the title bar (sketch ÄV1a): the open case with <em>Verifiera</em> and
/// <em>Stäng</em>, a search on the case number, the case files newest first, and <em>Nytt ärende …</em>. A click
/// chooses a row and a second click (or Enter) opens it. Only the number and the time of the last save are shown,
/// since the rest of a case lies encrypted in its file. It knows nothing of WPF, so that it is tested.
/// </summary>
public sealed partial class CasePickerViewModel : ObservableObject
{
    /// <summary>What the open case's status says before it has been verified in this opening.</summary>
    public const string NotVerifiedText = "Inte kontrollerat sedan det öppnades";

    /// <summary>What the open case's status says while it is being verified.</summary>
    public const string VerifyingText = "Kontrollerar …";

    /// <summary>What the open case's status says when the verification found nothing changed.</summary>
    public const string VerifiedText = "Hashkedjan är hel";

    /// <summary>What the list says when no case number matches the search.</summary>
    public const string NoMatchText = "Inget ärende har det numret.";

    /// <summary>What the list says when there are no cases in the folder at all.</summary>
    public const string NoCasesText = "Inga ärenden än.";

    /// <summary>The window's open case.</summary>
    private readonly CaseSession _session;

    /// <summary>The cases: their files, and the way to open and close them.</summary>
    private readonly CaseWorkflow _workflow;

    /// <summary>Cases' verification, the same as behind <c>/verify</c>.</summary>
    private readonly ICaseVerifier _verifier;

    /// <summary>The clock, for <em>ändrad i dag 14:02</em>.</summary>
    private readonly IClock _clock;

    /// <summary>The time zone the times are told in.</summary>
    private readonly TimeZoneInfo _zone;

    /// <summary>
    /// Counts the openings of cases, so that a verification that finishes after another case was opened (or the case
    /// was closed) does not speak for it.
    /// </summary>
    private int _opening;

    /// <summary>The case whose opening the verification status speaks for, or <see langword="null"/>.</summary>
    private ICase? _openCase;

    /// <summary>Creates the picker, folded in, and follows the open case and the list of case files.</summary>
    /// <param name="session">The window's open case.</param>
    /// <param name="workflow">The cases.</param>
    /// <param name="verifier">Cases' verification.</param>
    /// <param name="ui">The UI thread, where the listed files are shown.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="zone">The time zone the times are told in; the computer's when not given.</param>
    public CasePickerViewModel(
        CaseSession session,
        CaseWorkflow workflow,
        ICaseVerifier verifier,
        IUiThread ui,
        IClock clock,
        TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _session = session;
        _workflow = workflow;
        _verifier = verifier;
        _clock = clock;
        _zone = zone ?? TimeZoneInfo.Local;
        session.Changed += (_, _) => OnCaseChanged();
        session.Renamed += (_, _) => OnCaseChanged();
        workflow.CasesListed += (_, _) => ui.Post(ShowRows);
        _openCase = session.Current;
        OnCaseChanged();
    }

    /// <summary>Raised when <em>Nytt ärende …</em> is chosen; the shell opens the panel from the right.</summary>
    public event EventHandler? NewCaseWanted;

    /// <summary>
    /// Raised when <em>Nytt ärende med adressen …</em> is chosen for a link (sketch LN1a), with the start point as it
    /// is written; the shell opens Nytt ärende (NC2a) with it, where it is fetched in advance.
    /// </summary>
    public event EventHandler<string>? NewCaseWithLinkWanted;

    /// <summary>
    /// Raised when a link's start point was laid in a case (sketch LN1a), with the answer; the shell says it.
    /// </summary>
    public event EventHandler<CommandReply>? LinkPlaced;

    /// <summary>Raised when <em>Egenskaper</em> is chosen; the shell shows the properties (sketch ÄR1b).</summary>
    public event EventHandler? PropertiesWanted;

    /// <summary>Raised when <em>Öppna en kollegas fil …</em> is chosen; the shell asks for the file.</summary>
    public event EventHandler? OpenFileWanted;

    /// <summary>Gets the case files that match the search, newest first.</summary>
    public ObservableCollection<CasePickerRow> Rows { get; } = [];

    /// <summary>Gets whether the picker is folded out.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    /// <summary>Gets or sets the search on the case number, which filters the list while it is typed.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// Gets the start point of a link that waited while Chainsaw was locked (sketch LN1a), shown at the top until it is
    /// laid in a case, made a new case, or thrown away (Esc or ×); <see langword="null"/> otherwise.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HasLink), nameof(LinkValue), nameof(LinkChainText), nameof(ListHeading), nameof(NewCaseText))]
    public partial ChainStartPoint? Link { get; private set; }

    /// <summary>
    /// Gets when the link came, as its heading says it, for example <c>Länk från ett annat program · 10:41</c>.
    /// </summary>
    [ObservableProperty]
    public partial string LinkArrivedText { get; private set; } = string.Empty;

    /// <summary>Gets whether a link is shown at the top.</summary>
    public bool HasLink => Link is not null;

    /// <summary>Gets the link's whole value, an address or a tx.</summary>
    public string LinkValue => Link?.Value ?? string.Empty;

    /// <summary>Gets the link's chain, by name.</summary>
    public string LinkChainText => Link?.Chain is { } chain ? CaseTexts.ChainName(chain) : string.Empty;

    /// <summary>Gets the list's heading: <em>Lägg i ärende</em> with a link, <em>Senaste</em> otherwise.</summary>
    public string ListHeading => HasLink ? "Lägg i ärende" : "Senaste";

    /// <summary>Gets the last row's text: <em>Nytt ärende med adressen …</em> with a link.</summary>
    public string NewCaseText => HasLink ? "Nytt ärende med adressen …" : "Nytt ärende …";

    /// <summary>Gets whether a case is open, so that <em>Öppet nu</em> is shown.</summary>
    [ObservableProperty]
    public partial bool HasOpenCase { get; private set; }

    /// <summary>Gets the open case's name, as the title bar shows it.</summary>
    [ObservableProperty]
    public partial string OpenName { get; private set; } = string.Empty;

    /// <summary>Gets what the open case's verification has said in this opening.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanVerify))]
    public partial string VerifyText { get; private set; } = NotVerifiedText;

    /// <summary>
    /// Gets the verification's second line when the chain is whole: its rows, when, and its head; empty otherwise.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVerifyDetail))]
    public partial string VerifyDetail { get; private set; } = string.Empty;

    /// <summary>Gets whether <see cref="VerifyDetail"/> has something to say.</summary>
    public bool HasVerifyDetail => VerifyDetail.Length > 0;

    /// <summary>
    /// Gets the replaced anchor the last verification found in the journal (sketch AK1a), with its likely cause, or
    /// <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnchorChange), nameof(AnchorText), nameof(AnchorCauseText))]
    public partial Guide.GuideAnchorChange? AnchorChange { get; private set; }

    /// <summary>Gets whether the yellow line about a replaced anchor is shown.</summary>
    public bool HasAnchorChange => AnchorChange is not null;

    /// <summary>Gets the yellow line's first part, for example <c>Ankaret byttes i dag 09:12</c>.</summary>
    public string AnchorText => AnchorChange is { } change ? change.Replaced : string.Empty;

    /// <summary>Gets the yellow line's second part, for example <c>Troligast: äldre kopia</c>.</summary>
    public string AnchorCauseText => AnchorChange is { } change ? "Troligast: " + change.CauseShort : string.Empty;

    /// <summary>Gets whether the last verification found something changed, or could not be done.</summary>
    [ObservableProperty]
    public partial bool IsVerifyFailed { get; private set; }

    /// <summary>Gets whether a verification runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanVerify))]
    public partial bool IsVerifying { get; private set; }

    /// <summary>Gets what went wrong when a case was opened or closed from the picker, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; private set; } = string.Empty;

    /// <summary>Gets whether <see cref="ErrorText"/> has something to say.</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>Gets whether <em>Verifiera</em> can be pressed.</summary>
    public bool CanVerify => HasOpenCase && !IsVerifying;

    /// <summary>Gets how many cases the list shows, for example <c>12 ärenden</c>.</summary>
    public string CountText =>
        Rows.Count == 1 ? "1 ärende" : $"{Rows.Count.ToString(CultureInfo.InvariantCulture)} ärenden";

    /// <summary>Gets whether the list is empty and says why instead.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>Gets why the list is empty: no case matches, or there are none.</summary>
    public string EmptyText => _workflow.Files.Count == 0 ? NoCasesText : NoMatchText;

    /// <summary>Gets the listing of the case files started when the picker last folded out.</summary>
    public Task Listing { get; private set; } = Task.CompletedTask;

    /// <summary>Gets the chosen row, or <see langword="null"/>.</summary>
    public CasePickerRow? Chosen => Rows.FirstOrDefault(row => row.IsChosen);

    /// <summary>Folds the picker out, or in when it is out.</summary>
    [RelayCommand]
    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    /// <summary>Folds the picker out with an empty search, and lists the case files anew off the UI thread.</summary>
    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        ErrorText = string.Empty;
        SearchText = string.Empty;
        ShowRows();
        IsOpen = true;
        Listing = _workflow.RefreshCasesAsync();
    }

    /// <summary>
    /// Folds the picker out with a link that waited while Chainsaw was locked at the top (sketch LN1a), with the
    /// search and the list under it for choosing the case to lay it in.
    /// </summary>
    /// <param name="startPoint">The link's start point.</param>
    /// <param name="arrivedUtc">When the link came.</param>
    public void OpenForLink(ChainStartPoint startPoint, DateTimeOffset arrivedUtc)
    {
        ArgumentNullException.ThrowIfNull(startPoint);
        Close();
        var local = TimeZoneInfo.ConvertTime(arrivedUtc, _zone);
        LinkArrivedText = "Länk från ett annat program · " + local.ToString("HH:mm", CultureInfo.InvariantCulture);
        Link = startPoint;
        Open();
    }

    /// <summary>Folds the picker in; a link at the top is thrown away (Esc or ×).</summary>
    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
        Link = null;
    }

    /// <summary>Moves the choice up or down the list (the arrow keys), staying within it.</summary>
    /// <param name="step">-1 for up, 1 for down.</param>
    public void Move(int step)
    {
        if (Rows.Count == 0)
        {
            return;
        }

        var index = Chosen is { } chosen ? Rows.IndexOf(chosen) + step : 0;
        Choose(Rows[Math.Clamp(index, 0, Rows.Count - 1)]);
    }

    /// <summary>A click on a row: the first chooses it, a second on the chosen row opens it.</summary>
    /// <param name="row">The row.</param>
    /// <returns>A task that completes when the case is open, or the click only chose the row.</returns>
    [RelayCommand]
    public async Task PressAsync(CasePickerRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.IsChosen)
        {
            Choose(row);
            return;
        }

        await OpenOrPlaceAsync(row);
    }

    /// <summary>Opens the chosen row's case (Enter); nothing happens when no row is chosen.</summary>
    /// <returns>A task that completes when the case is open, or why not is said.</returns>
    [RelayCommand]
    public async Task OpenChosenAsync()
    {
        if (Chosen is { } chosen)
        {
            await OpenOrPlaceAsync(chosen);
        }
    }

    /// <summary>
    /// Verifiera: goes through every sealed chain of the open case, as <c>/verify</c> does, and says first whether the
    /// hash chain is whole, with its rows and its head (sketch AK1a). A replaced anchor is no fault in the file: it is
    /// said under, as a calm yellow line with the likely cause, and <em>Varför?</em> explains it in the Guide's panel.
    /// </summary>
    /// <returns>A task that completes when the status is shown.</returns>
    [RelayCommand]
    public async Task VerifyAsync()
    {
        if (_session.Current is not { } openCase || IsVerifying)
        {
            return;
        }

        var opening = _opening;
        IsVerifying = true;
        IsVerifyFailed = false;
        VerifyText = VerifyingText;
        VerifyDetail = string.Empty;
        AnchorChange = null;
        CaseVerification verification;
        AnchorReplacement? replaced;
        try
        {
            (verification, replaced) = await Task.Run(
                () => (_verifier.Verify(openCase), AnchorReplacement.LatestIn(openCase)));
        }
        catch (CaseException exception)
        {
            if (opening == _opening)
            {
                IsVerifying = false;
                IsVerifyFailed = true;
                VerifyText = CaseTexts.For(exception.Error);
            }

            return;
        }

        if (opening != _opening)
        {
            return;
        }

        IsVerifying = false;
        ShowVerification(verification, replaced);
    }

    /// <summary>Raised when <em>Varför?</em> asks the Guide's panel to explain a replaced anchor (sketch AK1a).</summary>
    public event EventHandler<Guide.GuideRequest>? GuideWanted;

    /// <summary><em>Varför?</em> on the replaced anchor: opens the Guide's panel with the explanation.</summary>
    [RelayCommand]
    public void WhyAnchor()
    {
        if (AnchorChange is { } change)
        {
            Close();
            GuideWanted?.Invoke(this, Guide.GuideRequest.ForAnchors(change));
        }
    }

    /// <summary>
    /// Shows what the verification found: the chain whole with its rows and head, and a replaced anchor under it, or
    /// what is wrong in the file.
    /// </summary>
    /// <param name="verification">The verification.</param>
    /// <param name="replaced">The latest replacement of the anchors, or <see langword="null"/>.</param>
    private void ShowVerification(CaseVerification verification, AnchorReplacement? replaced)
    {
        var whole = verification.Chains.All(chain => chain.Break is null)
            && verification.Problems.All(problem => problem == CaseProblem.AnchorsReplaced);
        if (!whole)
        {
            IsVerifyFailed = true;
            VerifyText = Commands.VerificationText.Describe(verification).Text;
            return;
        }

        var journal = verification.Chains.FirstOrDefault(chain => chain.Chain == CaseChains.Journal);
        VerifyText = VerifiedText;
        VerifyDetail = DetailOf(journal, verification.CheckedUtc);
        if (replaced is not null && verification.Problems.Contains(CaseProblem.AnchorsReplaced))
        {
            AnchorChange = Guide.GuideAnchorChange.Of(replaced, journal, _clock.UtcNow, _zone);
        }
    }

    /// <summary>The rows, the time and the head of the journal's chain, as the verification's second line says it.</summary>
    /// <param name="journal">The journal's verification, or <see langword="null"/>.</param>
    /// <param name="checkedUtc">When it was verified.</param>
    /// <returns>The text, for example <c>431 rader · 10:42 · topp 9c1e47b2…d03a44af</c>.</returns>
    private string DetailOf(ChainVerification? journal, DateTimeOffset checkedUtc)
    {
        var at = TimeZoneInfo.ConvertTime(checkedUtc, _zone).ToString("HH:mm", CultureInfo.InvariantCulture);
        if (journal is null)
        {
            return at;
        }

        var rows = journal.RecordCount.ToString("#,##0", CultureInfo.GetCultureInfo("sv-SE"));
        var head = journal.HeadHash is { Length: > 16 } hash ? $" · topp {hash[..8]}…{hash[^8..]}" : string.Empty;
        return $"{rows} rader · {at}{head}";
    }

    /// <summary>Stäng: saves and closes the open case; the picker stays out, so that another can be chosen.</summary>
    /// <returns>A task that completes when the case is closed, or why not is said.</returns>
    [RelayCommand]
    public async Task CloseCaseAsync()
    {
        ErrorText = string.Empty;
        var reply = await _workflow.CloseAsync();
        if (reply.IsError)
        {
            ErrorText = reply.Text;
        }
    }

    /// <summary>Nytt ärende …: folds the picker in and asks for the panel from the right.</summary>
    [RelayCommand]
    public void NewCase()
    {
        if (Link is { } link)
        {
            Close();
            NewCaseWithLinkWanted?.Invoke(this, StartPointText.Write(link));
            return;
        }

        Close();
        NewCaseWanted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Egenskaper: folds the picker in and asks for the open case's properties.</summary>
    [RelayCommand]
    public void Properties()
    {
        Close();
        PropertiesWanted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Öppna en kollegas fil …: folds the picker in and asks for a case file.</summary>
    [RelayCommand]
    public void OpenFile()
    {
        Close();
        OpenFileWanted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Lists the rows anew when a link comes or goes, so that they say what Enter does.</summary>
    /// <param name="value">The link's start point, or <see langword="null"/>.</param>
    partial void OnLinkChanged(ChainStartPoint? value) => ShowRows();

    /// <summary>Filters the list anew while the search is typed.</summary>
    /// <param name="value">The search.</param>
    partial void OnSearchTextChanged(string value) => ShowRows();

    /// <summary>Opens the row's case, or, with a link at the top, lays the link's start point in it.</summary>
    /// <param name="row">The case's row.</param>
    /// <returns>A task that completes when it is done, or why not is said.</returns>
    private Task OpenOrPlaceAsync(CasePickerRow row) =>
        Link is { } link ? PlaceLinkAsync(row, link) : OpenCaseAsync(row);

    /// <summary>
    /// <em>Lägg i ärende</em> (sketch LN1a): opens the case unless it is the open one, and lays the link's value in it
    /// as a new, neutral start point, with a line in the journal that it came with a link. The picker folds in and the
    /// shell says it; when it cannot be done, the picker says why and the link stays.
    /// </summary>
    /// <param name="row">The case's row.</param>
    /// <param name="link">The link's start point.</param>
    /// <returns>A task that completes when it is laid in the case, or why not is said.</returns>
    private async Task PlaceLinkAsync(CasePickerRow row, ChainStartPoint link)
    {
        ErrorText = string.Empty;
        if (!row.IsOpenCase)
        {
            var opened = await _workflow.OpenAsync(row.CaseNumber);
            if (opened.IsError)
            {
                ErrorText = opened.Text;
                return;
            }
        }

        var reply = await _workflow.AddLinkedStartPointAsync(link);
        if (reply.IsError)
        {
            ErrorText = reply.Text;
            return;
        }

        Close();
        LinkPlaced?.Invoke(this, reply);
    }

    /// <summary>Opens a case, and folds the picker in when it is open; otherwise says why in the picker.</summary>
    /// <param name="row">The case's row.</param>
    /// <returns>A task that completes when the case is open, or why not is said.</returns>
    private async Task OpenCaseAsync(CasePickerRow row)
    {
        ErrorText = string.Empty;
        var reply = await _workflow.OpenAsync(row.CaseNumber);
        if (reply.IsError)
        {
            ErrorText = reply.Text;
            return;
        }

        Close();
    }

    /// <summary>Makes a row the chosen one, and no other.</summary>
    /// <param name="row">The row.</param>
    private void Choose(CasePickerRow row)
    {
        foreach (var other in Rows)
        {
            other.IsChosen = ReferenceEquals(other, row);
        }

        OnPropertyChanged(nameof(Chosen));
    }

    /// <summary>
    /// Shows the open case anew when a case is opened, closed or renamed: a new opening has not been verified, and the
    /// list marks the case that is open now.
    /// </summary>
    private void OnCaseChanged()
    {
        // A new title is the same opening; another case, or none, is not.
        var current = _session.Current;
        if (!ReferenceEquals(current, _openCase))
        {
            _openCase = current;
            ResetVerification();
        }

        HasOpenCase = current is not null;
        OpenName = _session.DisplayName;
        ShowRows();
    }

    /// <summary>Forgets what the last verification said, since it spoke for another opening.</summary>
    private void ResetVerification()
    {
        _opening++;
        IsVerifying = false;
        IsVerifyFailed = false;
        VerifyText = NotVerifiedText;
        VerifyDetail = string.Empty;
        AnchorChange = null;
    }

    /// <summary>
    /// Lists the case files whose number contains the search, newest first, keeping the chosen case chosen when it is
    /// still listed and choosing the first otherwise, so that a number and Enter open it.
    /// </summary>
    private void ShowRows()
    {
        var chosen = Chosen?.CaseNumber;
        var open = _session.Current is { } current ? CaseSession.NumberOf(current) : null;
        var search = SearchText.Trim();
        var now = _clock.UtcNow;
        Rows.Clear();
        foreach (var file in _workflow.Files
            .Where(file => file.CaseNumber.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.LastWrittenUtc)
            .ThenBy(file => file.CaseNumber, StringComparer.OrdinalIgnoreCase))
        {
            Rows.Add(new CasePickerRow(
                file.CaseNumber,
                GraphStartViewModel.ChangedText(file.LastWrittenUtc, now, _zone),
                string.Equals(file.CaseNumber, open, StringComparison.OrdinalIgnoreCase),
                HasLink));
        }

        var keep = Rows.FirstOrDefault(
            row => string.Equals(row.CaseNumber, chosen, StringComparison.OrdinalIgnoreCase));
        if ((keep ?? Rows.FirstOrDefault()) is { } first)
        {
            Choose(first);
        }

        OnPropertyChanged(nameof(Chosen));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }
}
