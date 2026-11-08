using System.Collections.ObjectModel;
using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>The tabs of the case's properties (sketch ÄR1b).</summary>
public enum CasePropertiesTab
{
    /// <summary>Egenskaper: the number and the title.</summary>
    Properties,

    /// <summary>Kuvert: who can open the file.</summary>
    Envelopes,

    /// <summary>Information: the file and the case's id.</summary>
    Information,
}

/// <summary>What an envelope card in the tab Kuvert stands for.</summary>
public enum EnvelopeCardKind
{
    /// <summary>This vault's envelope.</summary>
    Yours,

    /// <summary>No envelope for this vault: the case opened with its password.</summary>
    YoursMissing,

    /// <summary>Another vault's envelope.</summary>
    OtherVault,

    /// <summary>The sharing envelope, the case password.</summary>
    SharingPassword,

    /// <summary>No sharing envelope.</summary>
    NoSharing,

    /// <summary>An organisation's recovery key.</summary>
    Recovery,

    /// <summary>No envelope for any recovery key.</summary>
    NoRecovery,

    /// <summary>An envelope of a kind this build does not know.</summary>
    Unknown,
}

/// <summary>What happens to an envelope when the file key is replaced (sketch VT1a).</summary>
public enum EnvelopeFate
{
    /// <summary>Nothing is being decided: the card is shown as it is.</summary>
    None,

    /// <summary>The envelope is wrapped anew with the new file key (<em>Lindas om</em>).</summary>
    Rewrapped,

    /// <summary>The envelope no longer opens the file after the replacement (<em>Faller bort</em>).</summary>
    FallsAway,
}

/// <summary>One card in the tab Kuvert (sketch ÄR1b): who, what kind, what it means and what can be done.</summary>
public sealed partial class EnvelopeCard : ObservableObject
{
    /// <summary>Creates a card.</summary>
    /// <param name="kind">What it stands for.</param>
    /// <param name="title">Its title.</param>
    /// <param name="subtitle">What kind of key it is.</param>
    /// <param name="text">What it means.</param>
    public EnvelopeCard(EnvelopeCardKind kind, string title, string subtitle, string text)
    {
        Kind = kind;
        Title = title;
        Subtitle = subtitle;
        Text = text;
    }

    /// <summary>Gets what the card stands for, which picks its icon and its marks.</summary>
    public EnvelopeCardKind Kind { get; }

    /// <summary>Gets the envelope the card stands for, or <see langword="null"/> for a card of what is missing.</summary>
    public CaseEnvelope? Envelope { get; init; }

    /// <summary>Gets or sets what a replacement of the file key does to the envelope, while it is being decided.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRewrapped), nameof(FallsAway), nameof(CanChange), nameof(CanCreate))]
    public partial EnvelopeFate Fate { get; set; }

    /// <summary>Gets whether the card carries the green <em>Lindas om</em>.</summary>
    public bool IsRewrapped => Fate == EnvelopeFate.Rewrapped;

    /// <summary>Gets whether the card is struck through with <em>Faller bort</em>.</summary>
    public bool FallsAway => Fate == EnvelopeFate.FallsAway;

    /// <summary>Gets the title.</summary>
    public string Title { get; }

    /// <summary>Gets what kind of key it is.</summary>
    public string Subtitle { get; }

    /// <summary>Gets what it means; a fingerprint for a recovery key.</summary>
    public string Text { get; }

    /// <summary>Gets whether the text is a fingerprint, shown in the mono font.</summary>
    public bool IsFingerprint => Kind == EnvelopeCardKind.Recovery;

    /// <summary>Gets whether the card is dashed: something that is not there.</summary>
    public bool IsDashed => Kind is EnvelopeCardKind.YoursMissing or EnvelopeCardKind.NoSharing
        or EnvelopeCardKind.NoRecovery;

    /// <summary>Gets whether the card carries the green <em>Du</em>.</summary>
    public bool IsYou => Kind == EnvelopeCardKind.Yours;

    /// <summary>Gets whether the card carries the lock: the organisation's, which cannot be removed here.</summary>
    public bool IsOrganisation => Kind == EnvelopeCardKind.Recovery;

    /// <summary>Gets whether <em>Byt …</em> and <em>Ta bort</em> are offered.</summary>
    public bool CanChange => Kind == EnvelopeCardKind.SharingPassword && !IsEditing && Fate == EnvelopeFate.None;

    /// <summary>Gets whether <em>Skapa lösenord …</em> is offered.</summary>
    public bool CanCreate => Kind == EnvelopeCardKind.NoSharing && !IsEditing && Fate == EnvelopeFate.None;

    /// <summary>Gets whether <em>Lägg till mitt kuvert</em> is offered.</summary>
    public bool CanAddOwn => Kind == EnvelopeCardKind.YoursMissing;

    /// <summary>Gets or sets whether the case password is being written in the card.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChange), nameof(CanCreate), nameof(ShowsText))]
    public partial bool IsEditing { get; set; }

    /// <summary>Gets whether the text is shown, which the password's fields take the place of.</summary>
    public bool ShowsText => !IsEditing;
}

/// <summary>
/// The case's properties (sketch ÄR1b): a dialog with the number and the title at the top and the tabs Egenskaper,
/// Kuvert and Information. Kuvert shows every envelope as a card; the case password is created, changed and removed
/// in its card, this vault gets an envelope of its own when it has none, and every change is journalled by the case.
/// The passwords are read from their fields only when the change is made and cleared at once. It knows nothing of
/// WPF, so that it is tested.
/// </summary>
public sealed partial class CasePropertiesViewModel : ObservableObject
{
    /// <summary>What is said when the two passwords differ.</summary>
    public const string MismatchText = "Lösenorden är inte lika.";

    /// <summary>What is said when the case could not be read or changed for another reason.</summary>
    public const string FailedText = "Det gick inte att ändra ärendet. Inget ändrades.";

    /// <summary>The window's open case.</summary>
    private readonly CaseSession _session;

    /// <summary>The clock, for when the case was created.</summary>
    private readonly IClock _clock;

    /// <summary>The time zone times are told in.</summary>
    private readonly TimeZoneInfo _zone;

    /// <summary>The toaster that says the file key was replaced, or <see langword="null"/>.</summary>
    private readonly Toaster? _toaster;

    /// <summary>The case the dialog shows, or <see langword="null"/>.</summary>
    private ICase? _case;

    /// <summary>Creates the dialog, closed, and closes it when the case it shows is closed.</summary>
    /// <param name="session">The window's open case.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="zone">The time zone times are told in; the computer's when not given.</param>
    /// <param name="toaster">The toaster that says the file key was replaced; none in some tests.</param>
    public CasePropertiesViewModel(
        CaseSession session, IClock clock, TimeZoneInfo? zone = null, Toaster? toaster = null)
    {
        _session = session;
        _clock = clock;
        _zone = zone ?? TimeZoneInfo.Local;
        _toaster = toaster;
        session.Changed += (_, _) => OnCaseChanged();
    }

    /// <summary>Gets the envelopes as cards.</summary>
    public ObservableCollection<EnvelopeCard> Cards { get; } = [];

    /// <summary>Gets whether the dialog is shown.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    /// <summary>Gets the tab shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPropertiesTab), nameof(IsEnvelopesTab), nameof(IsInformationTab))]
    public partial CasePropertiesTab Tab { get; private set; } = CasePropertiesTab.Envelopes;

    /// <summary>Gets the case number.</summary>
    [ObservableProperty]
    public partial string CaseNumber { get; private set; } = string.Empty;

    /// <summary>Gets or sets the title as it is written; it is saved with <see cref="SaveTitleAsync"/>.</summary>
    [ObservableProperty]
    public partial string TitleText { get; set; } = string.Empty;

    /// <summary>Gets when the case was created, in words.</summary>
    [ObservableProperty]
    public partial string CreatedText { get; private set; } = string.Empty;

    /// <summary>Gets the case file's path.</summary>
    [ObservableProperty]
    public partial string PathText { get; private set; } = string.Empty;

    /// <summary>Gets the case's id.</summary>
    [ObservableProperty]
    public partial string CaseIdText { get; private set; } = string.Empty;

    /// <summary>Gets whether a change runs.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>Gets what went wrong, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; private set; } = string.Empty;

    /// <summary>Gets whether <see cref="ErrorText"/> has something to say.</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>Gets whether the confirmation of <em>Byt filnyckel …</em> is folded out under the cards (VT1a).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReplaceFileKey))]
    public partial bool IsReplacingFileKey { get; private set; }

    /// <summary>Gets or sets whether a new case password is made in the same step as the new file key.</summary>
    [ObservableProperty]
    public partial bool CreatesSharingPassword { get; set; }

    /// <summary>
    /// Gets the dashed line under the cards after a replacement: when, which envelopes fell away and the journal line;
    /// empty when the file key has never been replaced.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReplacement))]
    public partial string ReplacementText { get; private set; } = string.Empty;

    /// <summary>Gets the confirmation's heading: who can open the file after the replacement.</summary>
    [ObservableProperty]
    public partial string ReplaceHeading { get; private set; } = string.Empty;

    /// <summary>Gets the confirmation's honest word about the copies already handed out.</summary>
    [ObservableProperty]
    public partial string CopiesText { get; private set; } = string.Empty;

    /// <summary>Gets whether <see cref="ReplacementText"/> has something to say.</summary>
    public bool HasReplacement => ReplacementText.Length > 0;

    /// <summary>
    /// Gets whether <em>Byt filnyckel …</em> is offered: only with an envelope of this vault's own, since the
    /// replacement keeps only that one (and the organisation's), and not while it is being confirmed.
    /// </summary>
    public bool CanReplaceFileKey => !IsReplacingFileKey && Cards.Any(card => card.Kind == EnvelopeCardKind.Yours);

    /// <summary>Gets whether the tab Egenskaper is shown.</summary>
    public bool IsPropertiesTab => Tab == CasePropertiesTab.Properties;

    /// <summary>Gets whether the tab Kuvert is shown.</summary>
    public bool IsEnvelopesTab => Tab == CasePropertiesTab.Envelopes;

    /// <summary>Gets whether the tab Information is shown.</summary>
    public bool IsInformationTab => Tab == CasePropertiesTab.Information;

    /// <summary>Gets the card of the case password, while it is written, or <see langword="null"/>.</summary>
    public EnvelopeCard? EditingCard => Cards.FirstOrDefault(card => card.IsEditing);

    /// <summary>Shows the open case's properties on a tab, and reads its envelopes off the UI thread.</summary>
    /// <param name="tab">The tab.</param>
    /// <returns>A task that completes when the envelopes are shown.</returns>
    public async Task ShowAsync(CasePropertiesTab tab = CasePropertiesTab.Envelopes)
    {
        if (_session.Current is not { } openCase)
        {
            return;
        }

        _case = openCase;
        CaseNumber = CaseSession.NumberOf(openCase);
        TitleText = openCase.Title;
        CreatedText = EnvelopeWords.When(openCase.Info.CreatedUtc, _clock.UtcNow, _zone);
        PathText = openCase.Path;
        CaseIdText = openCase.Info.Id.ToString("D", CultureInfo.InvariantCulture);
        ErrorText = string.Empty;
        Tab = tab;
        Cards.Clear();
        IsReplacingFileKey = false;
        ReplacementText = string.Empty;
        IsOpen = true;
        await ReloadAsync();
    }

    /// <summary>Shows a tab.</summary>
    /// <param name="tab">The tab.</param>
    [RelayCommand]
    public void ShowTab(CasePropertiesTab tab) => Tab = tab;

    /// <summary>Closes the dialog (Esc or ×); a change that runs is let finish first.</summary>
    [RelayCommand]
    public void Close()
    {
        if (IsBusy)
        {
            return;
        }

        IsOpen = false;
        _case = null;
        Cards.Clear();
        IsReplacingFileKey = false;
    }

    /// <summary>Saves the title as it is written, sealed in the case with a line in the journal.</summary>
    /// <returns>A task that completes when it is saved, or why not is said.</returns>
    [RelayCommand]
    public async Task SaveTitleAsync()
    {
        if (_case is not { } openCase || !CaseTitleRules.IsValid(TitleText))
        {
            ErrorText = _case is null ? string.Empty : CaseTexts.InvalidTitle;
            return;
        }

        var title = TitleText;
        if (await RunAsync(() => openCase.SetTitle(title)))
        {
            TitleText = openCase.Title;
            _session.Refresh();
        }
    }

    /// <summary><em>Skapa lösenord …</em> or <em>Byt …</em>: folds the two fields out in the card.</summary>
    /// <param name="card">The card of the case password.</param>
    [RelayCommand]
    public void EditPassword(EnvelopeCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        ErrorText = string.Empty;
        card.IsEditing = true;
        OnPropertyChanged(nameof(EditingCard));
    }

    /// <summary>Avbryt in the card: folds the fields in again.</summary>
    [RelayCommand]
    public void CancelPassword()
    {
        foreach (var card in Cards)
        {
            card.IsEditing = false;
        }

        ErrorText = string.Empty;
        OnPropertyChanged(nameof(EditingCard));
    }

    /// <summary>
    /// Byt or Skapa: takes the two passwords from their fields and, when they are alike and long enough, gives the
    /// file a new sharing envelope in place of the old one. The envelopes with keys of their own are not touched.
    /// </summary>
    /// <param name="password">The new password's field.</param>
    /// <param name="repeat">The field where it is written again.</param>
    /// <returns>A task that completes when it is changed, or why not is said.</returns>
    public async Task SetPasswordAsync(ISecretEntry password, ISecretEntry repeat)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(repeat);
        var first = password.Take();
        var second = repeat.Take();
        try
        {
            if (_case is not { } openCase || IsBusy)
            {
                return;
            }

            if (!first.AsSpan().SequenceEqual(second))
            {
                ErrorText = MismatchText;
                return;
            }

            if (first.Length < SharingPasswordRules.MinimumLength)
            {
                ErrorText = CaseTexts.For(CaseError.PasswordTooShort);
                return;
            }

            if (await RunAsync(() => openCase.SetSharingPassword(first)))
            {
                await ReloadAsync();
            }
        }
        finally
        {
            Secret.Clear(first);
            Secret.Clear(second);
        }
    }

    /// <summary>
    /// Ta bort: removes the sharing envelope. Refused when this vault has no envelope of its own, so that the case is
    /// never lost here.
    /// </summary>
    /// <returns>A task that completes when it is removed, or why not is said.</returns>
    [RelayCommand]
    public async Task RemovePasswordAsync()
    {
        if (_case is { } openCase && await RunAsync(openCase.RemoveSharingPassword))
        {
            await ReloadAsync();
        }
    }

    /// <summary><em>Lägg till mitt kuvert</em>: gives this vault an envelope of its own in the file.</summary>
    /// <returns>A task that completes when it is added, or why not is said.</returns>
    [RelayCommand]
    public async Task AddOwnEnvelopeAsync()
    {
        if (_case is { } openCase && await RunAsync(openCase.AddOperatorAccess))
        {
            await ReloadAsync();
        }
    }

    /// <summary>
    /// <em>Byt filnyckel …</em>: folds the confirmation out under the cards, and marks each card <em>Lindas om</em> or
    /// <em>Faller bort</em> (sketch VT1a).
    /// </summary>
    [RelayCommand]
    public void BeginReplaceFileKey()
    {
        if (!CanReplaceFileKey || IsBusy)
        {
            return;
        }

        CancelPassword();
        var dropped = new List<string>();
        foreach (var card in Cards)
        {
            card.Fate = FateOf(card);
            if (card.FallsAway)
            {
                dropped.Add(DroppedWords(card));
            }
        }

        var organisation = Cards.Any(card => card.Kind == EnvelopeCardKind.Recovery && card.IsRewrapped);
        ReplaceHeading = organisation
            ? "Bara du och organisationen kan öppna filen efter bytet"
            : "Bara du kan öppna filen efter bytet";
        CopiesText = CopiesWords(dropped);
        CreatesSharingPassword = false;
        ErrorText = string.Empty;
        IsReplacingFileKey = true;
    }

    /// <summary>Avbryt (Esc): folds the confirmation in, and the cards go back to how they are.</summary>
    [RelayCommand]
    public void CancelReplaceFileKey()
    {
        if (IsBusy)
        {
            return;
        }

        foreach (var card in Cards)
        {
            card.Fate = EnvelopeFate.None;
        }

        IsReplacingFileKey = false;
        CreatesSharingPassword = false;
        ErrorText = string.Empty;
    }

    /// <summary>
    /// <em>Byt filnyckel</em> (Enter): takes the master key, and the new case password when one is to be made, from
    /// their fields and replaces the file key. The fields are read only now and cleared at once.
    /// </summary>
    /// <param name="masterKey">The master key's field.</param>
    /// <param name="password">The new case password's field.</param>
    /// <param name="repeat">The field where it is written again.</param>
    /// <returns>A task that completes when the key is replaced, or why not is said.</returns>
    public async Task ReplaceFileKeyAsync(ISecretEntry masterKey, ISecretEntry password, ISecretEntry repeat)
    {
        ArgumentNullException.ThrowIfNull(masterKey);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(repeat);
        var key = masterKey.Take();
        var first = password.Take();
        var second = repeat.Take();
        try
        {
            if (_case is not { } openCase || !IsReplacingFileKey || IsBusy || key.Length == 0)
            {
                return;
            }

            if (CreatesSharingPassword && NewPasswordProblem(first, second) is { } problem)
            {
                ErrorText = problem;
                return;
            }

            var newPassword = CreatesSharingPassword ? first : [];
            FileKeyReplacement? replaced = null;
            if (!await RunAsync(() => replaced = openCase.ReplaceFileKey(key, newPassword)) || replaced is null)
            {
                return;
            }

            IsReplacingFileKey = false;
            CreatesSharingPassword = false;
            await ReloadAsync();
            _toaster?.Show(ToastKind.Success, "Filnyckeln är bytt", ToastWords(replaced));
        }
        finally
        {
            Secret.Clear(key);
            Secret.Clear(first);
            Secret.Clear(second);
        }
    }

    /// <summary>
    /// Gives what a replacement of the file key does to a card's envelope: this vault's and the built-in recovery
    /// key's are wrapped anew, every other falls away, and a card of what is missing is left as it is.
    /// </summary>
    /// <param name="card">The card.</param>
    /// <returns>The fate.</returns>
    public static EnvelopeFate FateOf(EnvelopeCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card.Envelope is not { } envelope)
        {
            return EnvelopeFate.None;
        }

        return FileKeyReplacement.Keeps(envelope) ? EnvelopeFate.Rewrapped : EnvelopeFate.FallsAway;
    }

    /// <summary>The honest word about the copies already handed out, naming what still opens them.</summary>
    /// <param name="dropped">What falls away, in words.</param>
    /// <returns>The text.</returns>
    public static string CopiesWords(IReadOnlyList<string> dropped)
    {
        ArgumentNullException.ThrowIfNull(dropped);
        const string Copies = "Kopior av filen som redan har lämnats ut kan inte dras tillbaka.";
        const string Here = "Bytet gäller filen här och allt som sparas i den efteråt.";
        return dropped.Count == 0
            ? $"{Copies} {Here}"
            : $"{Copies} De öppnas fortfarande med {Join(dropped)}. {Here}";
    }

    /// <summary>The dashed line under the cards for the latest replacement.</summary>
    /// <param name="replaced">The replacement.</param>
    /// <param name="now">The time now.</param>
    /// <param name="zone">The time zone times are told in.</param>
    /// <returns>The text.</returns>
    public static string ReplacementWords(FileKeyReplacement replaced, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(replaced);
        var when = EnvelopeWords.When(replaced.Utc, now, zone);
        var what = replaced.Dropped.Count == 0 ? "inga kuvert" : string.Join(" · ", replaced.Dropped);
        var line = replaced.Sequence.ToString(CultureInfo.InvariantCulture);
        return $"Filnyckeln byttes {when}. Dragna tillbaka: {what}. Journalrad {line}. Ska någon in igen läggs ett "
            + "nytt kuvert till; det gamla kommer inte tillbaka.";
    }

    /// <summary>Says what is wrong with a new case password written twice, or nothing.</summary>
    /// <param name="first">The password.</param>
    /// <param name="second">The password written again.</param>
    /// <returns>The problem, or <see langword="null"/>.</returns>
    private static string? NewPasswordProblem(char[] first, char[] second)
    {
        if (!first.AsSpan().SequenceEqual(second))
        {
            return MismatchText;
        }

        return first.Length < SharingPasswordRules.MinimumLength ? CaseTexts.For(CaseError.PasswordTooShort) : null;
    }

    /// <summary>Names a card that falls away, as the confirmation says it.</summary>
    /// <param name="card">The card.</param>
    /// <returns>The words.</returns>
    private static string DroppedWords(EnvelopeCard card) => card.Kind switch
    {
        EnvelopeCardKind.OtherVault => card.Envelope?.Name is { } name ? $"{name}s huvudnyckel" : "ett annat valv",
        EnvelopeCardKind.SharingPassword => "det gamla ärendelösenordet",
        EnvelopeCardKind.Recovery => "en annan återställningsnyckel",
        _ => "ett okänt kuvert",
    };

    /// <summary>The toast's text after a replacement.</summary>
    /// <param name="replaced">The replacement.</param>
    /// <returns>The text.</returns>
    private static string ToastWords(FileKeyReplacement replaced)
    {
        var count = replaced.Dropped.Count;
        var removed = count switch
        {
            0 => "Inga kuvert togs bort.",
            1 => "Ett kuvert togs bort.",
            _ => string.Create(CultureInfo.InvariantCulture, $"{count} kuvert togs bort."),
        };
        return removed + " Kopior som redan lämnats ut påverkas inte.";
    }

    /// <summary>Joins words with commas and a last <em>och</em>.</summary>
    /// <param name="words">The words.</param>
    /// <returns>The text.</returns>
    private static string Join(IReadOnlyList<string> words) => words.Count == 1
        ? words[0]
        : string.Join(", ", words.Take(words.Count - 1)) + " och " + words[^1];

    /// <summary>Builds the cards for a file's envelopes in the sketch's order: yours, others, sharing, keys.</summary>
    /// <param name="envelopes">The envelopes as the case described them.</param>
    /// <returns>The cards.</returns>
    public static IReadOnlyList<EnvelopeCard> CardsFor(IReadOnlyList<CaseEnvelope> envelopes)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        var cards = new List<EnvelopeCard>();
        var yours = envelopes.FirstOrDefault(item => item.Kind == CaseEnvelopeKind.Yours);
        var sharing = envelopes.FirstOrDefault(item => item.Kind == CaseEnvelopeKind.SharingPassword);
        cards.Add(yours is not null
            ? new EnvelopeCard(
                EnvelopeCardKind.Yours,
                "Ditt valv",
                "Huvudnyckel",
                "Ditt kuvert. Filen öppnas när Chainsaw är upplåst.") { Envelope = yours }
            : new EnvelopeCard(
                EnvelopeCardKind.YoursMissing,
                "Inget eget kuvert",
                "Huvudnyckel",
                "Ärendet öppnades med lösenordet. Med ett eget kuvert öppnas det med din huvudnyckel, också om "
                + "lösenordet byts."));
        cards.AddRange(envelopes.Where(item => item.Kind == CaseEnvelopeKind.OtherVault).Select(OtherVaultCard));
        cards.Add(sharing is not null
            ? new EnvelopeCard(
                EnvelopeCardKind.SharingPassword,
                "Ärendelösenord",
                "Delning",
                "För en kollega utan eget kuvert. När kollegan har lagt till sitt eget kan lösenordet tas bort.")
            {
                Envelope = sharing,
            }
            : new EnvelopeCard(
                EnvelopeCardKind.NoSharing,
                "Ingen delning",
                "Ärendelösenord",
                "Bara de med eget kuvert kan öppna filen. Ett lösenord lämnas till en kollega, aldrig i samma kanal "
                + "som filen."));
        var recovery = envelopes.Where(item => item.Kind == CaseEnvelopeKind.Recovery).ToList();
        cards.AddRange(recovery.Select(RecoveryCard));
        if (recovery.Count == 0)
        {
            cards.Add(new EnvelopeCard(
                EnvelopeCardKind.NoRecovery,
                "Organisationen",
                "Ingen återställningsnyckel",
                "Filen har inget kuvert för organisationens återställningsnyckel. Glöms huvudnyckeln går ärendet inte "
                + "att öppna."));
        }

        cards.AddRange(envelopes.Where(item => item.Kind == CaseEnvelopeKind.Unknown).Select(item => new EnvelopeCard(
            EnvelopeCardKind.Unknown,
            "Okänt kuvert",
            item.UnknownKind is { } kind ? $"Sort: {kind}" : "Okänd sort",
            EnvelopeWords.Tip(CaseEnvelopeKind.Unknown)) { Envelope = item }));
        return cards;
    }

    /// <summary>
    /// Builds the card of another vault's envelope: with the colleague's name when the envelope has one, which only
    /// someone who can open the case reads.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <returns>The card.</returns>
    private static EnvelopeCard OtherVaultCard(CaseEnvelope envelope) => envelope.Name is { } name
        ? new EnvelopeCard(
            EnvelopeCardKind.OtherVault,
            name,
            "Huvudnyckel",
            "En kollegas kuvert, med kollegans valv. Namnet är krypterat med ärendets nyckel, så bara den som kan "
            + "öppna filen ser vems det är.") { Envelope = envelope }
        : new EnvelopeCard(
            EnvelopeCardKind.OtherVault,
            "Ett annat valv",
            "Huvudnyckel",
            "En kollegas kuvert, med kollegans valv. Kuvertet bär inget namn, så filen visar inte vems det är.")
        {
            Envelope = envelope,
        };

    /// <summary>Builds the card of a recovery key's envelope, with its whole fingerprint.</summary>
    /// <param name="envelope">The envelope.</param>
    /// <returns>The card.</returns>
    private static EnvelopeCard RecoveryCard(CaseEnvelope envelope)
    {
        var subtitle = envelope.Recovery switch
        {
            RecoveryEnvelopeKey.BuiltIn => "Återställningsnyckel · inbyggd i Chainsaw",
            RecoveryEnvelopeKey.Revoked => "Återkallad nyckel · tas bort nästa gång ärendet öppnas",
            _ => "En annan återställningsnyckel · ligger kvar",
        };
        var fingerprint = EnvelopeWords.GroupFingerprint(envelope.Fingerprint ?? string.Empty);
        return new EnvelopeCard(EnvelopeCardKind.Recovery, "Organisationen", subtitle, $"Fingeravtryck {fingerprint}")
        {
            Envelope = envelope,
        };
    }

    /// <summary>Reads the case's envelopes anew off the UI thread and shows them as cards.</summary>
    /// <returns>A task that completes when they are shown.</returns>
    private async Task ReloadAsync()
    {
        if (_case is not { } openCase)
        {
            return;
        }

        IReadOnlyList<CaseEnvelope> envelopes;
        FileKeyReplacement? replaced;
        try
        {
            (envelopes, replaced) = await Task.Run(() => (openCase.DescribeEnvelopes(), LatestReplacement(openCase)));
        }
        catch (CaseException exception)
        {
            ErrorText = CaseTexts.For(exception.Error);
            return;
        }

        if (!ReferenceEquals(_case, openCase))
        {
            return;
        }

        Cards.Clear();
        foreach (var card in CardsFor(envelopes))
        {
            Cards.Add(card);
        }

        ReplacementText = replaced is null ? string.Empty : ReplacementWords(replaced, _clock.UtcNow, _zone);
        OnPropertyChanged(nameof(EditingCard));
        OnPropertyChanged(nameof(CanReplaceFileKey));
    }

    /// <summary>Reads the latest replacement of the file key from the journal; off the UI thread.</summary>
    /// <param name="openCase">The case.</param>
    /// <returns>The replacement, or <see langword="null"/> when the key has never been replaced.</returns>
    private static FileKeyReplacement? LatestReplacement(ICase openCase) => openCase.ReadJournal()
        .LastOrDefault(entry => entry.Kind == CaseJournalKinds.FileKeyReplaced) is { } entry
        ? FileKeyReplacement.FromJournal(entry)
        : null;

    /// <summary>Runs a change of the case off the UI thread, and says why when it fails.</summary>
    /// <param name="change">The change.</param>
    /// <returns><see langword="true"/> when it was made.</returns>
    private async Task<bool> RunAsync(Action change)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            await Task.Run(change);
            return true;
        }
        catch (CaseException exception)
        {
            ErrorText = CaseTexts.For(exception.Error);
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException)
        {
            ErrorText = FailedText;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Closes the dialog when the case it shows is no longer the open one (closed, or locked away).</summary>
    private void OnCaseChanged()
    {
        if (_case is not null && !ReferenceEquals(_session.Current, _case))
        {
            IsBusy = false;
            Close();
        }
    }
}
