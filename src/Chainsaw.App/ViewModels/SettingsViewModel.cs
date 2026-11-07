using Chainsaw.App.Guide;
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
/// through the same view model as the exits' panel), Låsning, Kurser, Profil and Om Chainsaw; Utseende and Länkar say
/// that they are not built yet. Changes apply and are saved at once. It knows nothing of WPF.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>What a row that is not built says (CLAUDE.md: never show what does not exist).</summary>
    public const string NotBuiltText = "Inte byggt än";

    /// <summary>What the dialog says when the settings' file could not be written.</summary>
    public const string SaveFailedText = "Inställningarna gick inte att spara. De gäller tills Chainsaw stängs.";

    /// <summary>What the About page says when Chainsaw has no recovery key built in.</summary>
    public const string NoRecoveryKeyText = "Ingen inbyggd nyckel";

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
    /// <param name="sources">The page Källor (sketch KL1a), or <see langword="null"/> where there is none.</param>
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
        SourcesViewModel? sources = null)
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
        Exits = exits;
        Sources = sources;
        Menu = SettingsMenu.Groups
            .Select(group => new SettingsMenuGroup(group.Name, [.. group.Entries.Select(e => new SettingsMenuItem(e))]))
            .ToList();
        LockChoices = [.. LockMinutes.Select(minutes => new SettingsChoice(LockLabel(minutes), minutes))];
        CurrencyChoices =
            [.. Enum.GetValues<FiatCurrency>().Select(currency => new SettingsChoice(currency.Code(), currency))];
        ResolutionChoices =
        [
            new("Minut", PriceResolution.Minute), new("Timme", PriceResolution.Hour), new("Dag", PriceResolution.Day),
        ];
        VersionText = $"{build.Version} · {build.ShortCommit}";
        RecoveryKeyText = cases.RecoveryKey?.DisplayFingerprint ?? NoRecoveryKeyText;
        HasRecoveryKey = cases.RecoveryKey is not null;
        way.Changed += (_, _) => ShowWay();
        ShowPage(SettingsMenu.Start);
        ShowChoices();
        ShowWay();
    }

    /// <summary>Gets the menu's groups and pages.</summary>
    public IReadOnlyList<SettingsMenuGroup> Menu { get; }

    /// <summary>Gets the exits under VPN, the same view model as the exits' panel in the title bar.</summary>
    public ExitPanelViewModel Exits { get; }

    /// <summary>Gets the page Källor, or <see langword="null"/> where there is none.</summary>
    public SourcesViewModel? Sources { get; }

    /// <summary>Gets the automatic lock's times.</summary>
    public IReadOnlyList<SettingsChoice> LockChoices { get; }

    /// <summary>Gets the currencies.</summary>
    public IReadOnlyList<SettingsChoice> CurrencyChoices { get; }

    /// <summary>Gets the rate's resolutions.</summary>
    public IReadOnlyList<SettingsChoice> ResolutionChoices { get; }

    /// <summary>Gets the version and the commit, under Om Chainsaw.</summary>
    public string VersionText { get; }

    /// <summary>Gets the recovery key's fingerprint, or that none is built in.</summary>
    public string RecoveryKeyText { get; }

    /// <summary>Gets whether a recovery key is built in, so that its fingerprint is written as a hash.</summary>
    public bool HasRecoveryKey { get; }

    /// <summary>Gets the licences, built in from <c>THIRD_PARTY.md</c>, or <see langword="null"/>.</summary>
    public GuideSection? Licenses { get; } = Settings.Licenses.Load();

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
    public partial bool IsWindows { get; private set; }

    /// <summary>Gets whether VPN is the way out (its card is marked, and the exits are shown under it).</summary>
    public bool IsVpn => !IsWindows;

    /// <summary>Gets whether Chainsaw locks when Windows is locked.</summary>
    [ObservableProperty]
    public partial bool LocksWithWindows { get; private set; }

    /// <summary>Gets what went wrong with the settings' file, or an empty text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; private set; } = string.Empty;

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

    /// <summary>Gets whether the licences are folded out under Om Chainsaw.</summary>
    [ObservableProperty]
    public partial bool IsShowingLicenses { get; private set; }

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
        IsShowingLicenses = false;
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

    /// <summary>Folds the licences out under Om Chainsaw, or in.</summary>
    [RelayCommand]
    public void ToggleLicenses() => IsShowingLicenses = !IsShowingLicenses;

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
        foreach (var item in Menu.SelectMany(group => group.Items))
        {
            item.IsSelected = item.Entry.Page == page;
        }
    }

    /// <summary>Marks the options chosen in the settings.</summary>
    private void ShowChoices()
    {
        SettingsChoice.Mark(LockChoices, _settings.AutoLockMinutes);
        SettingsChoice.Mark(CurrencyChoices, _settings.Currency);
        SettingsChoice.Mark(ResolutionChoices, _settings.Resolution);
        LocksWithWindows = _settings.LockWithWindows;
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
