using Chainsaw.App.Links;
using Chainsaw.App.Settings;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Security.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The settings (sketch ST1a): a dialog in the middle of the window over the darkened work, opened with Ctrl+, or the
/// pill's cog and closed with Esc, × or a lock. The menu's pages are Nätverk (Windows or VPN, and under VPN the exits
/// through the same view model as the exits' panel), Låsning, Länkar (the link <c>chainsaw:</c> registered for the
/// user), Kurser, Profil and Om Chainsaw; Utseende says that it is not built yet. Changes apply and are saved at once.
/// It knows nothing of WPF.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>What a row that is not built says (CLAUDE.md: never show what does not exist).</summary>
    public const string NotBuiltText = "Inte byggt än";

    /// <summary>What the row Länkar says under its title while the link opens this Chainsaw or nothing.</summary>
    public const string LinkHintText = "Registreras för dig i Windows, inte för datorn.";

    /// <summary>What the row Länkar says while the link opens another copy of Chainsaw.</summary>
    public const string LinkElsewhereText =
        "Öppnar en annan Chainsaw på datorn. Slå på för att länken ska öppna den här.";

    /// <summary>What the dialog says when the link could not be changed in Windows.</summary>
    public const string LinkFailedText = "Länken gick inte att ändra i Windows.";

    /// <summary>What the dialog says when the settings' file could not be written.</summary>
    public const string SaveFailedText = "Inställningarna gick inte att spara. De gäller tills Chainsaw stängs.";

    /// <summary>The error when the exit's copy for the background could not be kept or removed.</summary>
    public const string ExitInBackgroundFailedText =
        "Utgången gick inte att spara för bakgrunden. Försök igen om en stund.";

    /// <summary>What the profile says when the name is left empty.</summary>
    public const string NameNeededText = "Namnet behövs: det står med varje uppslag i ärendets journal.";

    /// <summary>What the profile says when it could not be read or written, such as when the vault locked.</summary>
    public const string ProfileFailedText = "Profilen gick inte att läsa eller spara. Lås upp och försök igen.";

    /// <summary>What the master key's form says when the new key was not repeated the same.</summary>
    public const string KeysDifferText = "Den nya huvudnyckeln och upprepningen är inte lika.";

    /// <summary>What the master key's form says when the current key is wrong.</summary>
    public const string WrongKeyText = "Den nuvarande huvudnyckeln stämmer inte.";

    /// <summary>What the master key's form says when the key could not be changed for another reason.</summary>
    public const string KeyFailedText = "Huvudnyckeln gick inte att byta.";

    /// <summary>What the master key's form says when the key is changed.</summary>
    public const string KeyChangedText = "Huvudnyckeln är bytt. Ärendena påverkas inte.";

    /// <summary>The automatic lock's times the page offers (sketch ST1a), in minutes.</summary>
    private static readonly int[] LockMinutes = [5, 15, 30, 60];

    /// <summary>The way out and the exit.</summary>
    private readonly ExitWay _way;

    /// <summary>The automatic lock and the lock with Windows.</summary>
    private readonly AutoLock _autoLock;

    /// <summary>The settings' file.</summary>
    private readonly AppSettingsStore _store;

    /// <summary>The currency and resolution in Pricing.</summary>
    private readonly IPriceDefaults _prices;

    /// <summary>The operator profile in the vault.</summary>
    private readonly IOperatorProfileStore _profiles;

    /// <summary>The vault, whose master key can be changed.</summary>
    private readonly IVault _vault;

    /// <summary>The link <c>chainsaw:</c> in Windows, or <see langword="null"/> where it cannot be.</summary>
    private readonly LinkRegistration? _links;

    /// <summary>Shows the message and plays the sound when asked, or <see langword="null"/> where it cannot.</summary>
    private readonly IAlertPreview? _alerts;

    /// <summary>The exits the background may use, or <see langword="null"/>.</summary>
    private readonly IBackgroundExits? _backgroundExits;

    /// <summary>The exits in the vault, for the selected exit, or <see langword="null"/>.</summary>
    private readonly IExitStore? _exitStore;

    /// <summary>The settings as they are now.</summary>
    private AppSettings _settings;

    /// <summary>The writes of the settings' file, one after the other so that the latest is the one kept.</summary>
    private Task _saving = Task.CompletedTask;

    /// <summary>Counts the writes, so that only the latest one's result is shown.</summary>
    private int _writes;

    /// <summary>The profile as it was read or last saved, so that an unchanged profile is not written again.</summary>
    private OperatorProfile? _savedProfile;

    /// <summary>Counts the times the dialog opened or closed, so that a late answer is not shown after one.</summary>
    private int _shown;

    /// <summary>Creates the settings with what was read from the file when Chainsaw started.</summary>
    /// <param name="settings">The settings read at the start.</param>
    /// <param name="store">The settings' file.</param>
    /// <param name="way">The way out and the exit.</param>
    /// <param name="exits">The exits, the same as in the exits' panel (sketch UT1a).</param>
    /// <param name="autoLock">The automatic lock.</param>
    /// <param name="prices">The currency and resolution in Pricing.</param>
    /// <param name="profiles">The operator profile.</param>
    /// <param name="vault">The vault.</param>
    /// <param name="cases">The cases, for the recovery key built in.</param>
    /// <param name="build">The build, for the version.</param>
    /// <param name="sources">The page Källor (sketch KL2b), or <see langword="null"/> where there is none.</param>
    /// <param name="suppliers">
    /// The switches of the suppliers of attribution, whose names are written to the file when they change, or
    /// <see langword="null"/> where there are none.
    /// </param>
    /// <param name="links">
    /// The link <c>chainsaw:</c> in Windows, or <see langword="null"/> where it cannot be registered (the row is then
    /// greyed).
    /// </param>
    /// <param name="about">
    /// <em>Om Chainsaw</em>, shared with the lock screen; made from the cases and the build when
    /// <see langword="null"/>.
    /// </param>
    /// <param name="alerts">
    /// Shows the message in the middle of the screen and plays the sound (<em>Prova</em>, <em>Lyssna</em>), or
    /// <see langword="null"/> where they cannot be shown.
    /// </param>
    /// <param name="rates">
    /// How many rates the open case keeps, on the page Kurser (sketch KU1a), or <see langword="null"/> where there is
    /// no case to count.
    /// </param>
    /// <param name="backgroundExits">
    /// The exits the background may use (decided 2026-11-08), or <see langword="null"/> where there are none (the row
    /// is then not shown).
    /// </param>
    /// <param name="exitStore">The exits in the vault, for the selected exit, or <see langword="null"/>.</param>
    public SettingsViewModel(
        AppSettings settings,
        AppSettingsStore store,
        ExitWay way,
        ExitPanelViewModel exits,
        AutoLock autoLock,
        IPriceDefaults prices,
        IOperatorProfileStore profiles,
        IVault vault,
        ICaseStore cases,
        BuildInfo build,
        SourcesViewModel? sources = null,
        AttributionSwitches? suppliers = null,
        LinkRegistration? links = null,
        AboutViewModel? about = null,
        IAlertPreview? alerts = null,
        RatesSummaryViewModel? rates = null,
        IBackgroundExits? backgroundExits = null,
        IExitStore? exitStore = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(build);
        _settings = settings;
        _store = store;
        _way = way;
        _autoLock = autoLock;
        _prices = prices;
        _profiles = profiles;
        _vault = vault;
        _links = links;
        _alerts = alerts;
        _backgroundExits = backgroundExits;
        _exitStore = exitStore;
        Exits = exits;
        Sources = sources;
        Rates = rates;
        Menu = SettingsMenu.Groups
            .Select(group => new SettingsMenuGroup(group.Name, [.. group.Entries.Select(e => new SettingsMenuItem(e))]))
            .ToList();
        LockChoices = [.. LockMinutes.Select(minutes => new SettingsChoice(LockLabel(minutes), minutes))];
        CurrencyChoices =
            [.. Enum.GetValues<FiatCurrency>().Select(currency => new SettingsChoice(currency.Code(), currency))];
        ResolutionChoices =
        [
            new("Minut", PriceResolution.Minute), new("Timme", PriceResolution.Hour), new("Dygn", PriceResolution.Day),
        ];
        About = about ?? new AboutViewModel(cases, build);
        SavedRoute = store.SavedRoute();
        way.Changed += (_, _) => ShowWay();
        if (suppliers is not null)
        {
            suppliers.Changed += (_, _) => Save(_settings with { AttributionProviders = suppliers.Enabled });
        }

        ShowPage(SettingsMenu.Start);
        ShowChoices();
        ShowWay();
        ShowLink();
    }

    /// <summary>Gets the menu's groups and pages.</summary>
    public IReadOnlyList<SettingsMenuGroup> Menu { get; }

    /// <summary>Gets the exits under VPN, the same view model as the exits' panel in the title bar.</summary>
    public ExitPanelViewModel Exits { get; }

    /// <summary>Gets how many rates the open case keeps (page Kurser), or <see langword="null"/>.</summary>
    public RatesSummaryViewModel? Rates { get; }

    /// <summary>Gets the page Källor, or <see langword="null"/> where there is none.</summary>
    public SourcesViewModel? Sources { get; }

    /// <summary>Gets the automatic lock's times.</summary>
    public IReadOnlyList<SettingsChoice> LockChoices { get; }

    /// <summary>Gets the currencies.</summary>
    public IReadOnlyList<SettingsChoice> CurrencyChoices { get; }

    /// <summary>Gets the rate's resolutions.</summary>
    public IReadOnlyList<SettingsChoice> ResolutionChoices { get; }

    /// <summary>Gets <em>Om Chainsaw</em>: the version, the recovery key and the licences (sketch ST2a).</summary>
    public AboutViewModel About { get; }

    /// <summary>
    /// Gets the way out the settings' file named when Chainsaw started, or <see langword="null"/> when it named none,
    /// so that the first-run setup starts from it rather than from Windows.
    /// </summary>
    public ExitRoute? SavedRoute { get; }

    /// <summary>Gets whether the message and the sound can be tried here (<em>Prova</em>, <em>Lyssna</em>).</summary>
    public bool CanPreviewAlerts => _alerts is not null;

    /// <summary>Gets whether the dialog is shown.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    /// <summary>Gets the page shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Lead), nameof(HasLead), nameof(IsNetworkPage), nameof(IsSourcesPage),
        nameof(IsLockPage), nameof(IsLookPage), nameof(IsLinksPage), nameof(IsPricingPage), nameof(IsProfilePage),
        nameof(IsAboutPage))]
    public partial SettingsPage Page { get; private set; }

    /// <summary>Gets the page's title.</summary>
    public string Title => SettingsMenu.For(Page).Name;

    /// <summary>Gets the line under the title.</summary>
    public string Lead => SettingsMenu.For(Page).Lead;

    /// <summary>Gets whether the page has a line under its title.</summary>
    public bool HasLead => Lead.Length > 0;

    /// <summary>Gets whether Nätverk is shown.</summary>
    public bool IsNetworkPage => Page == SettingsPage.Network;

    /// <summary>Gets whether Källor is shown.</summary>
    public bool IsSourcesPage => Page == SettingsPage.Sources;

    /// <summary>Gets whether Låsning is shown.</summary>
    public bool IsLockPage => Page == SettingsPage.Lock;

    /// <summary>Gets whether Utseende is shown.</summary>
    public bool IsLookPage => Page == SettingsPage.Look;

    /// <summary>Gets whether Länkar is shown.</summary>
    public bool IsLinksPage => Page == SettingsPage.Links;

    /// <summary>Gets whether Kurser is shown.</summary>
    public bool IsPricingPage => Page == SettingsPage.Pricing;

    /// <summary>Gets whether Profil is shown.</summary>
    public bool IsProfilePage => Page == SettingsPage.Profile;

    /// <summary>Gets whether Om Chainsaw is shown.</summary>
    public bool IsAboutPage => Page == SettingsPage.About;

    /// <summary>Gets whether Windows is the way out (its card is marked).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVpn))]
    [NotifyPropertyChangedFor(nameof(ShowsExitInBackground))]
    public partial bool IsWindows { get; private set; }

    /// <summary>Gets whether VPN is the way out (its card is marked, and the exits are shown under it).</summary>
    public bool IsVpn => !IsWindows;

    /// <summary>Gets whether Chainsaw locks when Windows is locked.</summary>
    [ObservableProperty]
    public partial bool LocksWithWindows { get; private set; }

    /// <summary>Gets whether the watching goes on when Chainsaw is closed.</summary>
    [ObservableProperty]
    public partial bool WatchesInBackground { get; private set; }

    /// <summary>Gets whether a hit is said with a notification in Windows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoAlertWay))]
    public partial bool AlertsInWindows { get; private set; }

    /// <summary>Gets whether a hit is said with the coin swoosh.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoAlertWay))]
    public partial bool AlertsWithSound { get; private set; }

    /// <summary>Gets whether a hit is said with the message in the middle of the screen (sketch BV4a).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoAlertWay))]
    public partial bool AlertsOnScreen { get; private set; }

    /// <summary>
    /// Gets whether no way is chosen, so that the page says honestly that a hit is then seen only in Bevakning while
    /// Chainsaw is open.
    /// </summary>
    public bool HasNoAlertWay => !AlertsInWindows && !AlertsWithSound && !AlertsOnScreen;

    /// <summary>
    /// Gets whether the selected exit is allowed in the background (decided 2026-11-08): its WireGuard file is then
    /// kept protected for the Windows user outside the vault, so that the watching goes through VPN also when Chainsaw
    /// is closed.
    /// </summary>
    [ObservableProperty]
    public partial bool ExitInBackground { get; private set; }

    /// <summary>
    /// Gets whether the switch can be used: copies can be kept, an exit is selected and the background is on.
    /// </summary>
    [ObservableProperty]
    public partial bool CanExitInBackground { get; private set; }

    /// <summary>Gets whether the row is shown: with VPN chosen, and only where copies can be kept.</summary>
    public bool ShowsExitInBackground => IsVpn && _backgroundExits?.IsAvailable == true;

    /// <summary>Gets whether an exit is checked once each time it connects (sketch UT3b).</summary>
    [ObservableProperty]
    public partial bool ChecksExitOnConnect { get; private set; }

    /// <summary>Gets what went wrong with the settings' file, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; private set; } = string.Empty;

    /// <summary>Gets whether the link <c>chainsaw:</c> can be registered here (the row is live).</summary>
    public bool CanRegisterLinks => _links is not null;

    /// <summary>Gets whether the link <c>chainsaw:</c> opens this Chainsaw (the switch).</summary>
    [ObservableProperty]
    public partial bool IsLinkRegistered { get; private set; }

    /// <summary>Gets what the row Länkar says under its title.</summary>
    [ObservableProperty]
    public partial string LinkHint { get; private set; } = LinkHintText;

    /// <summary>Gets whether something went wrong with the settings' file.</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>Gets or sets the operator's name in the profile's field.</summary>
    [ObservableProperty]
    public partial string ProfileName { get; set; } = string.Empty;

    /// <summary>Gets or sets the operator's title in the profile's field.</summary>
    [ObservableProperty]
    public partial string ProfileTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets the operator's unit in the profile's field.</summary>
    [ObservableProperty]
    public partial string ProfileUnit { get; set; } = string.Empty;

    /// <summary>Gets what went wrong with the profile, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfileError))]
    public partial string ProfileError { get; private set; } = string.Empty;

    /// <summary>Gets whether something went wrong with the profile.</summary>
    public bool HasProfileError => ProfileError.Length > 0;

    /// <summary>Gets whether the master key's form is folded out under its row.</summary>
    [ObservableProperty]
    public partial bool IsChangingKey { get; private set; }

    /// <summary>Gets whether the master key is being changed.</summary>
    [ObservableProperty]
    public partial bool IsKeyBusy { get; private set; }

    /// <summary>Gets what the master key's form says: an error, or that the key is changed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeyText))]
    public partial string KeyText { get; private set; } = string.Empty;

    /// <summary>Gets whether the master key's text is an error (in the error colour).</summary>
    [ObservableProperty]
    public partial bool IsKeyError { get; private set; }

    /// <summary>Gets whether the master key's form has something to say.</summary>
    public bool HasKeyText => KeyText.Length > 0;

    /// <summary>Opens the dialog at a page, or at the page shown last (Ctrl+, or the pill's cog).</summary>
    /// <param name="page">The page, or <see langword="null"/> for the page shown last.</param>
    public void Open(SettingsPage? page = null)
    {
        if (IsOpen)
        {
            ShowPage(page ?? Page);
            return;
        }

        _shown++;
        ShowPage(page ?? Page);
        ErrorText = string.Empty;
        ShowLink();
        IsOpen = true;
        _ = ReadProfileAsync();
        if (IsVpn)
        {
            _ = Exits.ShowInSettingsAsync();
        }
    }

    /// <summary>
    /// Closes the dialog (Esc, ×, a click on the dark, another view or a lock) and forgets what it held.
    /// </summary>
    [RelayCommand]
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        // A field being written in is saved as it stands, as leaving it would; its fields are read before this returns.
        _ = SaveProfileAsync();
        _shown++;
        IsOpen = false;
        IsChangingKey = false;
        About.Reset();
        KeyText = string.Empty;
        ProfileError = string.Empty;
        ProfileName = ProfileTitle = ProfileUnit = string.Empty;
        _savedProfile = null;
        Exits.LeaveSettings();
        Sources?.CancelAdd();
    }

    /// <summary>Shows a page of the menu.</summary>
    /// <param name="item">The page's menu item.</param>
    [RelayCommand]
    public void ShowItem(SettingsMenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ShowPage(item.Entry.Page);
    }

    /// <summary>Chooses the way out (the cards Windows and VPN); it applies to the next lookup and is saved.</summary>
    /// <param name="route">The way out.</param>
    [RelayCommand]
    public void ChooseRoute(ExitRoute route)
    {
        if (!Enum.IsDefined(route))
        {
            return;
        }

        _way.Choose(route);
        Save(_settings with { ExitRoute = route });
        if (route == ExitRoute.Vpn && IsOpen)
        {
            _ = Exits.ShowInSettingsAsync();
        }
    }

    /// <summary>Chooses the automatic lock's time.</summary>
    /// <param name="choice">The time's option.</param>
    [RelayCommand]
    public void ChooseLock(SettingsChoice choice)
    {
        if (choice?.Value is not int minutes)
        {
            return;
        }

        var settings = _settings with { AutoLockMinutes = minutes };
        _autoLock.Change(settings.AutoLockTimeout);
        Save(settings);
    }

    /// <summary>Raised on the UI thread when the settings change, with them.</summary>
    public event EventHandler<AppSettings>? SettingsChanged;

    /// <summary>Gets the settings in force.</summary>
    public AppSettings Current => _settings;

    /// <summary>
    /// Gets a task that completes when every change made so far is written to the settings' file, or its failure is
    /// shown, since the file is written off the UI thread.
    /// </summary>
    public Task Saving => _saving;

    /// <summary>Turns the watching when Chainsaw is closed on or off (the switch).</summary>
    [RelayCommand]
    public void ToggleWatchInBackground()
    {
        Save(_settings with { WatchInBackground = !_settings.WatchInBackground });

        // Turning it off removes the exits' copies (BackgroundLink); the switch under Nätverk follows.
        _ = ShowExitInBackgroundAsync();
    }

    /// <summary>Turns the notification in Windows on or off (the switch).</summary>
    [RelayCommand]
    public void ToggleAlertInWindows() => Save(_settings with { AlertInWindows = !_settings.AlertInWindows });

    /// <summary>
    /// Registers the link <c>chainsaw:</c> for the user so that it opens this Chainsaw, or takes it away (the switch).
    /// A link that opens another copy is taken over, not taken away.
    /// </summary>
    [RelayCommand]
    public void ToggleLinks()
    {
        if (_links is null)
        {
            return;
        }

        try
        {
            if (IsLinkRegistered)
            {
                _links.Unregister();
            }
            else
            {
                _links.Register();
            }

            ErrorText = string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            ErrorText = LinkFailedText;
        }

        ShowLink();
    }

    /// <summary>Turns the coin swoosh on or off (the switch).</summary>
    [RelayCommand]
    public void ToggleAlertWithSound() => Save(_settings with { AlertWithSound = !_settings.AlertWithSound });

    /// <summary>Turns the message in the middle of the screen on or off (the switch, sketch BV4a).</summary>
    [RelayCommand]
    public void ToggleAlertOnScreen() => Save(_settings with { AlertOnScreen = !_settings.AlertOnScreen });

    /// <summary><em>Prova</em>: shows the message in the middle of the screen as a hit would.</summary>
    [RelayCommand]
    public void TryScreenMessage() => _alerts?.ShowMessage();

    /// <summary>Closes the message shown by <em>Prova</em>, as when Chainsaw locks.</summary>
    public void DismissPreview() => _alerts?.CloseMessage();

    /// <summary><em>Lyssna</em>: plays the coin swoosh.</summary>
    [RelayCommand]
    public void PlaySound() => _alerts?.PlaySound();

    /// <summary>Turns the check of an exit each time it connects on or off (the switch, sketch UT3b).</summary>
    [RelayCommand]
    public void ToggleCheckExitOnConnect() =>
        Save(_settings with { CheckExitOnConnect = !_settings.CheckExitOnConnect });

    /// <summary>
    /// Lets the background use the selected exit, or takes the permission back (the switch): the exit's WireGuard file
    /// is copied under the Windows user's protection, or the copy is removed, off the UI thread; then the background
    /// is started or stopped by the settings' change.
    /// </summary>
    /// <returns>A task that completes when it is done.</returns>
    [RelayCommand]
    public async Task ToggleExitInBackgroundAsync()
    {
        if (_backgroundExits is not { } exits || _exitStore is not { } store || !CanExitInBackground)
        {
            return;
        }

        var allow = !ExitInBackground;
        try
        {
            await Task.Run(() =>
            {
                if (store.FindSelected() is not { } selected)
                {
                    return;
                }

                if (allow)
                {
                    exits.Allow(selected.Id);
                }
                else
                {
                    exits.Forbid(selected.Id);
                }
            });
            ErrorText = string.Empty;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ErrorText = ExitInBackgroundFailedText;
        }

        SettingsChanged?.Invoke(this, _settings);
        await ShowExitInBackgroundAsync();
    }

    /// <summary>Turns the lock with Windows on or off (the switch).</summary>
    [RelayCommand]
    public void ToggleLockWithWindows()
    {
        var locks = !_settings.LockWithWindows;
        _autoLock.LockWithWindows(locks);
        Save(_settings with { LockWithWindows = locks });
    }

    /// <summary>Chooses the currency amounts are counted in.</summary>
    /// <param name="choice">The currency's option.</param>
    [RelayCommand]
    public void ChooseCurrency(SettingsChoice choice)
    {
        if (choice?.Value is FiatCurrency currency)
        {
            ChoosePricing(_settings with { Currency = currency });
        }
    }

    /// <summary>Chooses the rate's resolution.</summary>
    /// <param name="choice">The resolution's option.</param>
    [RelayCommand]
    public void ChooseResolution(SettingsChoice choice)
    {
        if (choice?.Value is PriceResolution resolution)
        {
            ChoosePricing(_settings with { Resolution = resolution });
        }
    }

    /// <summary>
    /// Saves the profile as written, when it has changed (a field is left, or Enter). An empty name is not saved,
    /// since every lookup names who fetched it.
    /// </summary>
    /// <returns>A task that completes when the profile is saved, or why not is shown.</returns>
    [RelayCommand]
    public async Task SaveProfileAsync()
    {
        if (!IsOpen || _savedProfile is null)
        {
            return;
        }

        var profile = new OperatorProfile(ProfileName.Trim(), ProfileTitle.Trim(), ProfileUnit.Trim());
        if (profile.Name.Length == 0)
        {
            ProfileError = NameNeededText;
            return;
        }

        ProfileError = string.Empty;
        if (profile == _savedProfile)
        {
            return;
        }

        var shown = _shown;
        var before = _savedProfile;
        _savedProfile = profile;
        try
        {
            await Task.Run(() => _profiles.Save(profile));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (shown == _shown)
            {
                // Not saved: the next Enter or leaving a field tries again.
                _savedProfile = before;
                ProfileError = ProfileFailedText;
            }
        }
    }

    /// <summary>Folds the master key's form out under its row (<em>Byt …</em>), or in.</summary>
    [RelayCommand]
    public void ToggleChangeKey()
    {
        IsChangingKey = !IsChangingKey;
        KeyText = string.Empty;
    }

    /// <summary>
    /// Changes the master key: the current key, the new key and the new key again are taken from their fields as
    /// characters, given to the vault off the UI thread, and cleared. Only the vault's key is wrapped again; the
    /// cases are not touched.
    /// </summary>
    /// <param name="current">The field with the current master key.</param>
    /// <param name="next">The field with the new master key.</param>
    /// <param name="repeat">The field with the new master key again.</param>
    /// <returns>A task that completes when the vault has answered.</returns>
    public async Task ChangeMasterKeyAsync(ISecretEntry current, ISecretEntry next, ISecretEntry repeat)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(repeat);
        var currentKey = current.Take();
        var nextKey = next.Take();
        var repeatKey = repeat.Take();
        if (IsKeyBusy || !IsChangingKey)
        {
            // A second press while the first is running, or no form: only the keys taken are cleared.
            Secret.Clear(currentKey);
            Secret.Clear(nextKey);
            Secret.Clear(repeatKey);
            return;
        }

        try
        {
            if (KeyProblem(nextKey, repeatKey) is { } problem)
            {
                ShowKeyText(problem, isError: true);
                return;
            }

            var shown = _shown;
            IsKeyBusy = true;
            KeyText = string.Empty;
            var (text, isError) = await Task.Run(() => ChangeKey(currentKey, nextKey));
            if (shown != _shown)
            {
                return;
            }

            ShowKeyText(text, isError);
            if (!isError)
            {
                IsChangingKey = false;
            }
        }
        finally
        {
            Secret.Clear(currentKey);
            Secret.Clear(nextKey);
            Secret.Clear(repeatKey);
            IsKeyBusy = false;
        }
    }

    /// <summary>Gives the words for an automatic lock's time, as the page shows it.</summary>
    /// <param name="minutes">The time in minutes.</param>
    /// <returns>For example <em>15 min</em> or <em>1 h</em>.</returns>
    public static string LockLabel(int minutes) => minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes} min";

    /// <summary>Says what is wrong with a new master key before it is tried, or nothing.</summary>
    /// <param name="next">The new key.</param>
    /// <param name="repeat">The new key again.</param>
    /// <returns>The problem, or <see langword="null"/>.</returns>
    private static string? KeyProblem(char[] next, char[] repeat)
    {
        if (!MasterKeyRules.IsLongEnough(next))
        {
            return $"Den nya huvudnyckeln ska ha minst {MasterKeyRules.MinimumLength} tecken.";
        }

        return next.AsSpan().SequenceEqual(repeat) ? null : KeysDifferText;
    }

    /// <summary>Changes the master key in the vault and gives the words for the answer.</summary>
    /// <param name="current">The current key.</param>
    /// <param name="next">The new key.</param>
    /// <returns>The words and whether they are an error.</returns>
    private (string Text, bool IsError) ChangeKey(char[] current, char[] next)
    {
        try
        {
            _vault.ChangeMasterKey(current, next);
            return (KeyChangedText, false);
        }
        catch (VaultException exception) when (exception.Error == VaultError.WrongMasterKey)
        {
            return (WrongKeyText, true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The message is never shown: it could name a path.
            return (KeyFailedText, true);
        }
    }

    /// <summary>Shows what the master key's form says.</summary>
    /// <param name="text">The words.</param>
    /// <param name="isError">Whether they are an error.</param>
    private void ShowKeyText(string text, bool isError)
    {
        KeyText = text;
        IsKeyError = isError;
    }

    /// <summary>Applies a currency or resolution in Pricing and saves it.</summary>
    /// <param name="settings">The settings with the choice.</param>
    private void ChoosePricing(AppSettings settings)
    {
        _prices.Choose(settings.Currency, settings.Resolution);
        Save(settings);
    }

    /// <summary>Shows a page and marks it in the menu.</summary>
    /// <param name="page">The page.</param>
    private void ShowPage(SettingsPage page)
    {
        Page = page;
        if (page == SettingsPage.Network)
        {
            _ = ShowExitInBackgroundAsync();
        }

        if (page == SettingsPage.Pricing)
        {
            _ = Rates?.RefreshAsync();
        }

        foreach (var item in Menu.SelectMany(group => group.Items))
        {
            item.IsSelected = item.Entry.Page == page;
        }
    }

    /// <summary>
    /// Reads, off the UI thread, whether the selected exit is allowed in the background and whether the switch can be
    /// used; a vault or file that cannot be read leaves the switch off and unusable.
    /// </summary>
    /// <returns>A task that completes when it is shown.</returns>
    private async Task ShowExitInBackgroundAsync()
    {
        if (_backgroundExits is not { IsAvailable: true } exits || _exitStore is not { } store)
        {
            (ExitInBackground, CanExitInBackground) = (false, false);
            return;
        }

        var on = _settings.WatchInBackground;
        (bool Allowed, bool Usable) state;
        try
        {
            state = await Task.Run(() => store.FindSelected() is { } selected
                ? (exits.IsAllowed(selected.Id), on)
                : (false, false));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            state = (false, false);
        }

        (ExitInBackground, CanExitInBackground) = state;
    }

    /// <summary>Reads where the link <c>chainsaw:</c> points, for the switch and the row's line.</summary>
    private void ShowLink()
    {
        var state = ReadLink();
        IsLinkRegistered = state == LinkState.ThisProgram;
        LinkHint = state == LinkState.OtherProgram ? LinkElsewhereText : LinkHintText;
    }

    /// <summary>Reads where the link points; a registry that cannot be read counts as not registered.</summary>
    /// <returns>Where it points.</returns>
    private LinkState ReadLink()
    {
        try
        {
            return _links?.Read() ?? LinkState.None;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            return LinkState.None;
        }
    }

    /// <summary>Marks the options chosen in the settings.</summary>
    private void ShowChoices()
    {
        SettingsChoice.Mark(LockChoices, _settings.AutoLockMinutes);
        SettingsChoice.Mark(CurrencyChoices, _settings.Currency);
        SettingsChoice.Mark(ResolutionChoices, _settings.Resolution);
        LocksWithWindows = _settings.LockWithWindows;
        WatchesInBackground = _settings.WatchInBackground;
        AlertsInWindows = _settings.AlertInWindows;
        AlertsWithSound = _settings.AlertWithSound;
        AlertsOnScreen = _settings.AlertOnScreen;
        ChecksExitOnConnect = _settings.CheckExitOnConnect;
        Exits.ChecksOnConnect = _settings.CheckExitOnConnect;
    }

    /// <summary>Marks the card of the way out chosen now.</summary>
    private void ShowWay() => IsWindows = _way.IsWindows;

    /// <summary>
    /// Keeps the settings, marks them and writes the file off the UI thread, after any write before it, so that the
    /// file always ends with the latest. A failed write is said, and the choice still holds until Chainsaw closes.
    /// </summary>
    /// <param name="settings">The settings to keep.</param>
    private void Save(AppSettings settings)
    {
        _settings = settings;
        ShowChoices();
        ErrorText = string.Empty;
        SettingsChanged?.Invoke(this, settings);
        var before = _saving;
        _saving = SaveAfterAsync(before, settings, ++_writes);
    }

    /// <summary>Writes the settings once the write before has ended.</summary>
    /// <param name="before">The write before.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="write">The write's number; only the latest write's result is shown.</param>
    /// <returns>A task that completes when the file is written or the failure is shown.</returns>
    private async Task SaveAfterAsync(Task before, AppSettings settings, int write)
    {
        await before;
        string result;
        try
        {
            await Task.Run(() => SaveOrForget(settings));
            result = string.Empty;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            result = SaveFailedText;
        }

        if (write == _writes)
        {
            ErrorText = result;
        }
    }

    /// <summary>
    /// Writes the settings. When they cannot be written while VPN is chosen, the file is removed if it can be, so that
    /// the next start falls back to the defaults, VPN, and never to a Windows that an older file still says.
    /// </summary>
    /// <param name="settings">The settings.</param>
    private void SaveOrForget(AppSettings settings)
    {
        try
        {
            _store.Save(settings);
        }
        catch (Exception) when (settings.ExitRoute == ExitRoute.Vpn)
        {
            _store.ForgetIfWindows();
            throw;
        }
    }

    /// <summary>Reads the profile into its fields off the UI thread when the dialog opens.</summary>
    /// <returns>A task that completes when the fields are filled, or why not is shown.</returns>
    private async Task ReadProfileAsync()
    {
        var shown = _shown;
        try
        {
            var profile = await Task.Run(_profiles.Get)
                ?? new OperatorProfile(string.Empty, string.Empty, string.Empty);
            if (shown != _shown)
            {
                return;
            }

            _savedProfile = profile;
            (ProfileName, ProfileTitle, ProfileUnit) = (profile.Name, profile.Title, profile.Organization);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (shown == _shown)
            {
                ProfileError = ProfileFailedText;
            }
        }
    }
}
