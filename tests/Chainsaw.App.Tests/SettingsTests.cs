using System.Security.Cryptography;
using Chainsaw.App.Navigation;
using Chainsaw.App.Settings;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>
/// The settings (sketch ST1a, etapp 19): the dialog and its menu, the way out (Windows or VPN) in Network, the title
/// bar, the exits' panel and the lock screen in the mode Windows, the lock, the currency, the profile, the master key
/// and Om Chainsaw, and that every change is applied and saved at once.
/// </summary>
public sealed class SettingsTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private readonly GateRig _rig = new(VaultState.Unlocked);

    private SettingsViewModel Settings => _rig.Settings;

    public void Dispose()
    {
        // The file is written off the UI thread: a write still under way would put a file back into the folder while
        // it is removed, and the removal would fail.
        Assert.True(Settings.Saving.Wait(Wait));
        var folder = Path.GetDirectoryName(_rig.SettingsStore.FilePath)!;
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// Waits until the dialog has read the profile Alex Berg into all three fields. The read writes them one after the
    /// other off the UI thread, so a test that waits for the name alone could write a field the read then overwrites.
    /// </summary>
    private void WaitForTheProfile() => Assert.True(SpinWait.SpinUntil(
        () => Settings is { ProfileName: "Alex Berg", ProfileTitle: "Utredare", ProfileUnit: "Kryptogruppen" }, Wait));

    [Fact]
    public void The_menu_has_the_sketchs_pages_in_groups_with_om_last_on_its_own()
    {
        Assert.Equal(["Den här datorn", "Arbetet", "Du", string.Empty], SettingsMenu.Groups.Select(g => g.Name));
        Assert.Equal(
            ["Nätverk", "Källor", "Låsning", "Utseende", "Länkar", "Kurser", "Profil", "Om Chainsaw"],
            SettingsMenu.Groups.SelectMany(g => g.Entries).Select(e => e.Name));
        Assert.All(Enum.GetValues<SettingsPage>(), page => Assert.Equal(page, SettingsMenu.For(page).Page));
        Assert.Equal("Hur Chainsaw når källorna. Bara https, alltid.", SettingsMenu.For(SettingsPage.Network).Lead);
        Assert.False(SettingsMenu.Groups[^1].HasName);
        Assert.Equal(SettingsMenu.Gesture, ViewTable.For(AppView.Settings).Gesture);
    }

    [Fact]
    public void The_pills_cog_opens_the_dialog_over_the_view_shown_at_nätverk()
    {
        _rig.Shell.Show(AppView.Graph);

        _rig.Shell.Show(AppView.Settings);

        Assert.True(Settings.IsOpen);
        Assert.Equal(AppView.Graph, _rig.Shell.Current);
        Assert.Equal(SettingsPage.Network, Settings.Page);
        Assert.Equal("Nätverk", Settings.Title);
        Assert.True(Settings.Menu[0].Items[0].IsSelected);
        Assert.True(_rig.ExitPanel.IsInSettings);
    }

    [Fact]
    public void Another_page_is_marked_and_titled_and_the_dialog_opens_there_again()
    {
        Settings.Open();
        Settings.ShowItem(Settings.Menu[1].Items[0]);
        Settings.Close();

        Settings.Open();

        Assert.Equal(SettingsPage.Pricing, Settings.Page);
        Assert.True(Settings.IsPricingPage);
        Assert.Equal("Kurser", Settings.Title);
        Assert.True(Settings.Menu[1].Items[0].IsSelected);
        Assert.False(Settings.Menu[0].Items[0].IsSelected);
        Assert.False(Settings.HasLead && Settings.IsAboutPage);
    }

    [Fact]
    public void A_lock_another_view_or_nytt_ärende_closes_the_dialog()
    {
        _rig.Shell.Show(AppView.Settings);
        _rig.Gate.Lock();
        Assert.False(Settings.IsOpen);
        Assert.False(_rig.ExitPanel.IsInSettings);

        _rig.Vault.Set(VaultState.Unlocked);
        _rig.Shell.Show(AppView.Settings);
        _rig.Shell.Show(AppView.Guide);
        Assert.False(Settings.IsOpen);

        _rig.Shell.Show(AppView.Settings);
        _rig.Shell.Show(AppView.New);
        Assert.False(Settings.IsOpen);
        Assert.True(_rig.NewCase.IsOpen);
    }

    [Fact]
    public void Nytt_ärende_open_keeps_the_dialog_from_opening_behind_it()
    {
        _rig.Shell.Show(AppView.New);

        _rig.Shell.Show(AppView.Settings);

        Assert.False(Settings.IsOpen);
    }

    [Fact]
    public void Vpn_is_the_default_way_out_and_its_card_is_marked()
    {
        Assert.Equal(ExitRoute.Vpn, new AppSettings().ExitRoute);
        Assert.True(Settings.IsVpn);
        Assert.False(Settings.IsWindows);
        Assert.True(_rig.ExitPanel.IsVpnMode);
    }

    [Fact]
    public void Windows_is_chosen_in_network_and_saved_and_shown_in_the_title_bar_and_status_line()
    {
        Settings.Open();

        Settings.ChooseRoute(ExitRoute.Windows);

        Assert.Equal(ExitRoute.Windows, _rig.Route.Current);
        Assert.True(Settings.IsWindows);
        Assert.Equal(ShellViewModel.WindowsText, _rig.Shell.ExitText);
        Assert.Equal(ShellViewModel.WindowsSourceText, _rig.Shell.SourceText);
        Assert.True(_rig.Shell.IsExitLive);
        Assert.True(SpinWait.SpinUntil(() => _rig.SettingsStore.Load().ExitRoute == ExitRoute.Windows, Wait));
    }

    [Fact]
    public void Back_to_vpn_without_an_exit_the_title_bar_says_offline_again()
    {
        Settings.ChooseRoute(ExitRoute.Windows);

        Settings.ChooseRoute(ExitRoute.Vpn);

        Assert.Equal(ExitRoute.Vpn, _rig.Route.Current);
        Assert.Equal(ShellViewModel.NoExitText, _rig.Shell.ExitText);
        Assert.Equal(ShellViewModel.OfflineText, _rig.Shell.SourceText);
        Assert.False(_rig.Shell.IsExitLive);
        Assert.True(SpinWait.SpinUntil(() => File.Exists(_rig.SettingsStore.FilePath)
            && _rig.SettingsStore.Load().ExitRoute == ExitRoute.Vpn, Wait));
    }

    [Fact]
    public void With_windows_a_connected_exits_name_is_never_shown_as_the_way()
    {
        var exit = new ExitInfo(Guid.NewGuid(), "wg-sthlm", "Stockholm", ExitKind.WireGuard);
        _rig.Exits.Known.Add(exit);
        Settings.ChooseRoute(ExitRoute.Windows);

        _rig.Exit.Set(new ExitStatus(ExitState.Connected, exit.Id, null));
        Thread.Sleep(50);

        Assert.Equal(ShellViewModel.WindowsText, _rig.Shell.ExitText);
        Assert.Equal(ShellViewModel.WindowsSourceText, _rig.Shell.SourceText);
    }

    [Fact]
    public void An_unknown_way_is_never_chosen()
    {
        Settings.ChooseRoute((ExitRoute)7);

        Assert.Empty(_rig.Route.Chosen);
        Assert.True(Settings.IsVpn);
    }

    [Fact]
    public void With_windows_the_exits_panel_routes_through_windows_and_tones_the_exits()
    {
        Settings.ChooseRoute(ExitRoute.Windows);

        Assert.False(_rig.ExitPanel.IsVpnMode);
        Assert.True(_rig.ExitPanel.IsWindowsMode);
        Assert.True(_rig.ExitPanel.IsLive);
        Assert.Equal(ExitPanelViewModel.WindowsRouteText, _rig.ExitPanel.RouteText);
    }

    [Fact]
    public async Task With_windows_the_exits_panel_neither_connects_nor_adds_a_file()
    {
        _rig.Exits.Known.Add(new ExitInfo(Guid.NewGuid(), "wg-sthlm", "Stockholm", ExitKind.WireGuard));
        _rig.ExitPanel.Toggle();
        await _rig.ExitPanel.RefreshAsync();
        Settings.ChooseRoute(ExitRoute.Windows);

        await _rig.ExitPanel.PowerAsync(_rig.ExitPanel.Rows[0]);
        await _rig.ExitPanel.AddFileAsync();

        Assert.Equal(0, _rig.Picker.AskedForWireGuard);
        Assert.Equal(ExitState.NotConnected, _rig.Exit.Status.State);
    }

    [Fact]
    public void Inställningar_in_the_exits_panel_opens_the_settings_at_nätverk_and_folds_the_panel_in()
    {
        _rig.Shell.Show(AppView.Settings);
        Settings.ShowItem(Settings.Menu[2].Items[0]);
        Settings.Close();
        _rig.ExitPanel.Toggle();

        _rig.ExitPanel.OpenSettings();

        Assert.False(_rig.ExitPanel.IsOpen);
        Assert.True(Settings.IsOpen);
        Assert.Equal(SettingsPage.Network, Settings.Page);
    }

    [Fact]
    public async Task The_exits_are_read_for_the_settings_and_a_file_can_be_added_there()
    {
        _rig.Exits.Known.Add(new ExitInfo(Guid.NewGuid(), "wg-ams", "Amsterdam", ExitKind.WireGuard));
        Settings.Open();
        Assert.True(SpinWait.SpinUntil(() => _rig.ExitPanel.Rows.Count == 1, Wait));

        var path = Path.Combine(Path.GetDirectoryName(_rig.SettingsStore.FilePath)!, "Oslo.conf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "[Interface]\nPrivateKey = x\nAddress = 10.0.0.2/32\nDNS = 10.0.0.1\n"
            + "[Peer]\nPublicKey = y\nEndpoint = vpn.example:51820\nAllowedIPs = 0.0.0.0/0\n");
        _rig.Picker.WireGuardPath = path;

        await _rig.ExitPanel.AddFileAsync();

        Assert.True(_rig.ExitPanel.IsAdding);
        Assert.Equal("Oslo", _rig.ExitPanel.NewName);
        Settings.Close();
        Assert.False(_rig.ExitPanel.IsAdding);
        Assert.False(_rig.ExitPanel.IsInSettings);
    }

    [Fact]
    public void The_lock_screen_says_that_lookups_go_through_windows()
    {
        Settings.ChooseRoute(ExitRoute.Windows);

        Assert.Equal(LockViewModel.WindowsWaitsText, _rig.LockScreen.ExitText);

        Settings.ChooseRoute(ExitRoute.Vpn);
        Assert.Equal(LockViewModel.ExitWaitsText, _rig.LockScreen.ExitText);
    }

    [Fact]
    public void The_lock_time_is_applied_saved_and_shown_on_the_lock_screen()
    {
        Assert.Equal(["5 min", "15 min", "30 min", "1 h"], Settings.LockChoices.Select(c => c.Label));
        Assert.True(Settings.LockChoices[1].IsSelected);

        Settings.ChooseLock(Settings.LockChoices[2]);

        Assert.Equal(TimeSpan.FromMinutes(30), _rig.AutoLock.Timeout);
        Assert.True(Settings.LockChoices[2].IsSelected);
        Assert.False(Settings.LockChoices[1].IsSelected);
        Assert.Equal("Låses automatiskt efter 30 minuter utan aktivitet.", _rig.LockScreen.AutoLockText);
        Assert.True(SpinWait.SpinUntil(() => _rig.SettingsStore.Load().AutoLockMinutes == 30, Wait));
    }

    [Fact]
    public void The_new_lock_time_counts_from_the_last_activity()
    {
        _rig.Gate.Touch();
        Settings.ChooseLock(Settings.LockChoices[0]);

        _rig.Clock.UtcNow += TimeSpan.FromMinutes(6);
        _rig.Gate.CheckIdle();

        Assert.Equal(VaultState.Locked, _rig.Vault.State);
    }

    [Fact]
    public void The_watching_when_closed_and_its_ways_of_telling_are_switched_saved_and_told()
    {
        AppSettings? told = null;
        Settings.SettingsChanged += (_, settings) => told = settings;
        Assert.True(Settings.WatchesInBackground);
        Assert.True(Settings.AlertsInWindows);
        Assert.True(Settings.AlertsWithSound);

        Settings.ToggleWatchInBackground();
        Settings.ToggleAlertInWindows();
        Settings.ToggleAlertWithSound();

        Assert.False(Settings.WatchesInBackground);
        Assert.False(Settings.AlertsInWindows);
        Assert.False(Settings.AlertsWithSound);
        Assert.Equal(Settings.Current, told);
        Assert.Equal(Chainsaw.Modules.Monitoring.Contracts.WatchAlerts.None, told!.Alerts);
        Assert.True(SpinWait.SpinUntil(() => File.Exists(_rig.SettingsStore.FilePath)
            && !_rig.SettingsStore.Load().WatchInBackground && !_rig.SettingsStore.Load().AlertWithSound, Wait));
    }

    [Fact]
    public void Without_the_lock_with_windows_windows_lock_leaves_chainsaw_open_but_sleep_still_locks()
    {
        Assert.True(Settings.LocksWithWindows);

        Settings.ToggleLockWithWindows();
        _rig.Gate.OnWindowsSessionLocked();

        Assert.False(Settings.LocksWithWindows);
        Assert.False(_rig.AutoLock.LocksWithWindows);
        Assert.Equal(VaultState.Unlocked, _rig.Vault.State);
        Assert.True(SpinWait.SpinUntil(() => File.Exists(_rig.SettingsStore.FilePath)
            && !_rig.SettingsStore.Load().LockWithWindows, Wait));

        _rig.Gate.OnWindowsLocked();
        Assert.Equal(VaultState.Locked, _rig.Vault.State);
    }

    [Fact]
    public void With_the_lock_with_windows_windows_lock_locks_chainsaw()
    {
        _rig.Gate.OnWindowsSessionLocked();

        Assert.Equal(VaultState.Locked, _rig.Vault.State);
    }

    [Fact]
    public void The_currency_and_resolution_go_to_pricing_and_are_saved()
    {
        Assert.Equal(["SEK", "USD", "EUR"], Settings.CurrencyChoices.Select(c => c.Label));
        Assert.Equal(["Minut", "Timme", "Dygn"], Settings.ResolutionChoices.Select(c => c.Label));

        Settings.ChooseCurrency(Settings.CurrencyChoices[2]);
        Settings.ChooseResolution(Settings.ResolutionChoices[2]);

        Assert.Equal(FiatCurrency.Eur, _rig.Prices.Currency);
        Assert.Equal(PriceResolution.Day, _rig.Prices.Resolution);
        Assert.True(Settings.CurrencyChoices[2].IsSelected);
        Assert.True(SpinWait.SpinUntil(() => File.Exists(_rig.SettingsStore.FilePath)
            && _rig.SettingsStore.Load() is { Currency: FiatCurrency.Eur, Resolution: PriceResolution.Day }, Wait));
    }

    [Fact]
    public void A_settings_file_that_cannot_be_written_is_said_and_the_choice_still_holds()
    {
        var folder = Path.GetDirectoryName(_rig.SettingsStore.FilePath)!;
        Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
        File.WriteAllText(folder, "a file where the folder should be");
        try
        {
            Settings.ChooseCurrency(Settings.CurrencyChoices[1]);

            Assert.True(SpinWait.SpinUntil(() => Settings.HasError, Wait));
            Assert.Equal(SettingsViewModel.SaveFailedText, Settings.ErrorText);
            Assert.Equal(FiatCurrency.Usd, _rig.Prices.Currency);
        }
        finally
        {
            File.Delete(folder);
        }
    }

    [Fact]
    public async Task The_profile_is_read_when_the_dialog_opens_and_saved_when_changed()
    {
        _rig.Profiles.Save(new OperatorProfile("Alex Berg", "Utredare", "Kryptogruppen"));
        Settings.Open();
        WaitForTheProfile();

        Settings.ProfileTitle = "  Analytiker ";
        await Settings.SaveProfileAsync();

        Assert.Equal(new OperatorProfile("Alex Berg", "Analytiker", "Kryptogruppen"), _rig.Profiles.Saved);
        Assert.False(Settings.HasProfileError);
    }

    [Fact]
    public async Task An_empty_name_is_not_saved()
    {
        _rig.Profiles.Save(new OperatorProfile("Alex Berg", "Utredare", "Kryptogruppen"));
        Settings.Open();
        WaitForTheProfile();

        Settings.ProfileName = "   ";
        await Settings.SaveProfileAsync();

        Assert.Equal(SettingsViewModel.NameNeededText, Settings.ProfileError);
        Assert.Equal("Alex Berg", _rig.Profiles.Saved!.Name);
    }

    [Fact]
    public async Task A_profile_that_cannot_be_saved_is_said_and_closing_forgets_the_fields()
    {
        _rig.Profiles.Save(new OperatorProfile("Alex Berg", "Utredare", "Kryptogruppen"));
        Settings.Open();
        WaitForTheProfile();
        _rig.Profiles.Failure = new VaultException(VaultError.Locked);

        Settings.ProfileUnit = "Annan enhet";
        await Settings.SaveProfileAsync();

        Assert.Equal(SettingsViewModel.ProfileFailedText, Settings.ProfileError);
        Settings.Close();
        Assert.Equal(string.Empty, Settings.ProfileName);
        Assert.False(Settings.HasProfileError);
    }

    [Fact]
    public async Task A_failed_profile_save_is_tried_again()
    {
        _rig.Profiles.Save(new OperatorProfile("Alex Berg", "Utredare", "Kryptogruppen"));
        Settings.Open();
        WaitForTheProfile();
        _rig.Profiles.Failure = new VaultException(VaultError.Locked);
        Settings.ProfileUnit = "Annan enhet";
        await Settings.SaveProfileAsync();
        _rig.Profiles.Failure = null;

        await Settings.SaveProfileAsync();

        Assert.Equal("Annan enhet", _rig.Profiles.Saved!.Organization);
        Assert.False(Settings.HasProfileError);
    }

    [Fact]
    public void Closing_the_dialog_saves_a_field_being_written_in()
    {
        _rig.Profiles.Save(new OperatorProfile("Alex Berg", "Utredare", "Kryptogruppen"));
        Settings.Open();
        WaitForTheProfile();
        Settings.ProfileTitle = "Analytiker";

        Settings.Close();

        Assert.True(SpinWait.SpinUntil(() => _rig.Profiles.Saved!.Title == "Analytiker", Wait));
    }

    [Fact]
    public async Task A_second_press_while_the_key_is_changed_leaves_the_first_busy_and_clears_its_keys()
    {
        var gate = new TaskCompletionSource();
        var vault = new SlowVault(gate.Task);
        var settings = new SettingsViewModel(
            new AppSettings(), _rig.SettingsStore, _rig.Way, _rig.ExitPanel, _rig.AutoLock, _rig.Prices,
            _rig.Profiles, vault, _rig.Cases, _rig.Build);
        settings.Open();
        settings.ToggleChangeKey();
        var first = settings.ChangeMasterKeyAsync(
            new TestEntry("correct horse battery"), new TestEntry("a much longer new key"),
            new TestEntry("a much longer new key"));
        Assert.True(settings.IsKeyBusy);
        var second = new TestEntry("correct horse battery");

        await settings.ChangeMasterKeyAsync(second, new TestEntry("x"), new TestEntry("x"));

        Assert.True(settings.IsKeyBusy);
        Assert.True(second.AllCleared);
        gate.SetResult();
        await first;
        Assert.False(settings.IsKeyBusy);
        Assert.Equal(SettingsViewModel.KeyChangedText, settings.KeyText);
    }

    [Fact]
    public void A_choice_of_vpn_that_cannot_be_written_removes_a_file_that_says_windows()
    {
        var store = _rig.SettingsStore;
        store.Save(new AppSettings { ExitRoute = ExitRoute.Windows });

        store.ForgetIfWindows();
        Assert.False(File.Exists(store.FilePath));
        Assert.Equal(ExitRoute.Vpn, store.Load().ExitRoute);

        store.Save(new AppSettings { ExitRoute = ExitRoute.Vpn, AutoLockMinutes = 30 });
        store.ForgetIfWindows();
        Assert.Equal(30, store.Load().AutoLockMinutes);
    }

    [Fact]
    public void A_choice_of_vpn_whose_write_fails_removes_a_file_that_says_windows()
    {
        var store = _rig.SettingsStore;
        store.Save(new AppSettings { ExitRoute = ExitRoute.Windows });
        Directory.CreateDirectory(store.FilePath + ".tmp");

        Settings.ChooseRoute(ExitRoute.Vpn);

        Assert.True(SpinWait.SpinUntil(() => !File.Exists(store.FilePath) && Settings.HasError, Wait));
        Assert.Equal(ExitRoute.Vpn, store.Load().ExitRoute);
    }

    [Fact]
    public void An_unreadable_file_is_removed_after_a_failed_choice_of_vpn()
    {
        var store = _rig.SettingsStore;
        store.Save(new AppSettings());
        File.WriteAllText(store.FilePath, "{ not json");

        store.ForgetIfWindows();

        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void A_later_write_that_succeeds_clears_the_error_of_an_earlier_one()
    {
        var store = _rig.SettingsStore;
        Directory.CreateDirectory(store.FilePath + ".tmp");
        Settings.ChooseCurrency(Settings.CurrencyChoices[1]);
        Assert.True(SpinWait.SpinUntil(() => Settings.HasError, Wait));
        Directory.Delete(store.FilePath + ".tmp");

        Settings.ChooseCurrency(Settings.CurrencyChoices[2]);

        Assert.True(SpinWait.SpinUntil(
            () => !Settings.HasError && File.Exists(store.FilePath) && store.Load().Currency == FiatCurrency.Eur, Wait));
    }

    [Fact]
    public void The_cog_again_while_the_settings_are_open_keeps_what_is_written()
    {
        _rig.Profiles.Save(new OperatorProfile("Alex Berg", "Utredare", "Kryptogruppen"));
        _rig.Shell.Show(AppView.Settings);
        WaitForTheProfile();
        Settings.ProfileTitle = "Analytiker";

        _rig.Shell.Show(AppView.Settings, SettingsPage.Profile);

        Assert.True(Settings.IsOpen);
        Assert.Equal(SettingsPage.Profile, Settings.Page);
        Assert.Equal("Analytiker", Settings.ProfileTitle);
    }

    [Fact]
    public async Task Nothing_is_saved_before_the_profile_has_been_read()
    {
        await Settings.SaveProfileAsync();

        Assert.Null(_rig.Profiles.Saved);
    }

    [Fact]
    public async Task The_master_key_is_changed_and_every_key_is_cleared()
    {
        Settings.Open();
        Settings.ToggleChangeKey();
        var current = new TestEntry("correct horse battery");
        var next = new TestEntry("a much longer new key");
        var repeat = new TestEntry("a much longer new key");

        await Settings.ChangeMasterKeyAsync(current, next, repeat);

        Assert.Equal("a much longer new key", _rig.Vault.ChangedTo);
        Assert.Equal(SettingsViewModel.KeyChangedText, Settings.KeyText);
        Assert.False(Settings.IsKeyError);
        Assert.False(Settings.IsChangingKey);
        Assert.True(current.AllCleared && next.AllCleared && repeat.AllCleared);
    }

    [Theory]
    [InlineData("correct horse battery", "short", "short", "minst")]
    [InlineData("correct horse battery", "a much longer new key", "a much longer new kez", "inte lika")]
    [InlineData("the wrong current key", "a much longer new key", "a much longer new key", "stämmer inte")]
    public async Task A_master_key_that_cannot_be_changed_says_why_and_clears_the_keys(
        string currentKey, string nextKey, string repeatKey, string said)
    {
        Settings.Open();
        Settings.ToggleChangeKey();
        var current = new TestEntry(currentKey);
        var next = new TestEntry(nextKey);
        var repeat = new TestEntry(repeatKey);

        await Settings.ChangeMasterKeyAsync(current, next, repeat);

        Assert.Null(_rig.Vault.ChangedTo);
        Assert.Contains(said, Settings.KeyText, StringComparison.Ordinal);
        Assert.True(Settings.IsKeyError);
        Assert.True(Settings.IsChangingKey);
        Assert.True(current.AllCleared && next.AllCleared && repeat.AllCleared);
    }

    [Fact]
    public async Task The_master_key_is_not_changed_unless_its_form_is_out()
    {
        Settings.Open();
        var current = new TestEntry("correct horse battery");

        await Settings.ChangeMasterKeyAsync(current, new TestEntry("a much longer new key"),
            new TestEntry("a much longer new key"));

        Assert.Null(_rig.Vault.ChangedTo);
        Assert.True(current.AllCleared);
    }

    [Fact]
    public void Om_chainsaw_has_the_version_no_recovery_key_and_the_licences()
    {
        var about = Settings.About;
        Assert.Equal("0.1.0 · abcdef0", about.VersionText);
        Assert.False(about.HasKey);
        Assert.True(about.HasNoKey);
        Assert.Equal(AboutViewModel.NoKeyHintText, about.KeyHint);
        Assert.Empty(about.Lines);
        Assert.NotNull(about.Licenses);
        Assert.Contains("Tredje part", about.Licenses!.Title, StringComparison.Ordinal);

        about.MarkCopied();
        Assert.False(about.IsCopied);
        about.ToggleLicenses();
        Assert.True(about.IsShowingLicenses);
        Settings.Open();
        Settings.Close();
        Assert.False(about.IsShowingLicenses);
    }

    [Fact]
    public void Om_chainsaw_shows_the_built_in_recovery_keys_fingerprint_in_four_lines_of_groups()
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        var recovery = RecoveryPublicKey.FromPrivateKey(key);
        var cases = new TestCaseStore { RecoveryKey = recovery };

        var settings = new SettingsViewModel(
            new AppSettings(), _rig.SettingsStore, _rig.Way, _rig.ExitPanel, _rig.AutoLock, _rig.Prices,
            _rig.Profiles, _rig.Vault, cases, _rig.Build);

        var about = settings.About;
        Assert.True(about.HasKey);
        Assert.Equal(AboutViewModel.KeyHintText, about.KeyHint);
        Assert.Equal(recovery.DisplayFingerprint, about.Fingerprint);
        Assert.Equal(4, about.Lines.Count);
        Assert.All(about.Lines, line => Assert.Equal(4, line.Groups.Count));
        var digits = string.Concat(about.Lines.SelectMany(line => line.Groups));
        Assert.Equal(recovery.Fingerprint.ToUpperInvariant(), digits);
        Assert.False(about.HasRevoked);

        about.MarkCopied();
        Assert.True(about.IsCopied);
        settings.Open();
        settings.Close();
        Assert.False(about.IsCopied);
    }

    [Fact]
    public void Om_chainsaw_lists_the_revoked_keys_struck_and_short()
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        var revoked = new string('a', 60) + "0f9e";
        var cases = new TestCaseStore
        {
            RecoveryKey = RecoveryPublicKey.FromPrivateKey(key),
            RevokedRecoveryKeys = [revoked],
        };

        var about = new AboutViewModel(cases, _rig.Build);

        Assert.True(about.HasRevoked);
        var shown = Assert.Single(about.Revoked);
        Assert.Equal("AAAA AAAA … 0F9E", shown.ShortText);
        Assert.Equal(string.Join(' ', AboutViewModel.GroupsOf(revoked)), shown.FullText);
        Assert.Equal(16, AboutViewModel.GroupsOf(revoked).Count);
    }

    [Fact]
    public void Revoked_keys_are_not_listed_when_no_key_is_built_in_since_the_warning_says_it_all()
    {
        var cases = new TestCaseStore { RevokedRecoveryKeys = [new string('b', 64)] };

        var about = new AboutViewModel(cases, _rig.Build);

        Assert.False(about.HasRevoked);
        Assert.True(about.HasNoKey);
    }

    [Fact]
    public void The_screen_message_is_a_way_with_its_switch_and_the_page_says_when_none_is_chosen()
    {
        Assert.False(Settings.AlertsOnScreen);
        Assert.False(Settings.HasNoAlertWay);

        Settings.ToggleAlertOnScreen();
        Assert.True(Settings.AlertsOnScreen);
        Assert.True(Settings.Current.Alerts.HasFlag(Chainsaw.Modules.Monitoring.Contracts.WatchAlerts.Screen));

        Settings.ToggleAlertOnScreen();
        Settings.ToggleAlertInWindows();
        Settings.ToggleAlertWithSound();
        Assert.True(Settings.HasNoAlertWay);
        Assert.Equal(Chainsaw.Modules.Monitoring.Contracts.WatchAlerts.None, Settings.Current.Alerts);
    }

    [Fact]
    public void Prova_and_Lyssna_show_the_message_and_play_the_sound_where_they_can()
    {
        Assert.False(Settings.CanPreviewAlerts);
        Settings.TryScreenMessage();
        Settings.PlaySound();

        var alerts = new TestAlerts();
        var settings = new SettingsViewModel(
            new AppSettings(), _rig.SettingsStore, _rig.Way, _rig.ExitPanel, _rig.AutoLock, _rig.Prices,
            _rig.Profiles, _rig.Vault, _rig.Cases, _rig.Build, alerts: alerts);

        Assert.True(settings.CanPreviewAlerts);
        settings.TryScreenMessage();
        settings.PlaySound();
        Assert.Equal((1, 1), (alerts.Messages, alerts.Sounds));
    }

    [Fact]
    public void Checking_an_exit_on_connecting_is_on_from_the_start_and_follows_its_switch()
    {
        Assert.True(Settings.ChecksExitOnConnect);
        Assert.True(_rig.ExitPanel.ChecksOnConnect);

        Settings.ToggleCheckExitOnConnect();

        Assert.False(Settings.ChecksExitOnConnect);
        Assert.False(_rig.ExitPanel.ChecksOnConnect);
        Assert.False(Settings.Current.CheckExitOnConnect);
    }

    [Fact]
    public void The_preview_card_is_closed_when_everything_is_dismissed_as_at_a_lock()
    {
        var alerts = new TestAlerts();
        var settings = new SettingsViewModel(
            new AppSettings(), _rig.SettingsStore, _rig.Way, _rig.ExitPanel, _rig.AutoLock, _rig.Prices,
            _rig.Profiles, _rig.Vault, _rig.Cases, _rig.Build, alerts: alerts);
        var shell = new ShellViewModel(
            _rig.Session, _rig.Commands, _rig.NewCase, _rig.Workflow, _rig.Way, _rig.Exits, _rig.Sanctions,
            _rig.ExitPanel, settings, _rig.Ui, toaster: _rig.Toaster);
        settings.TryScreenMessage();
        var before = alerts.Closed;

        shell.DismissAll();

        Assert.True(alerts.Closed > before);
    }

    [Fact]
    public void The_saved_way_out_is_read_only_when_the_file_names_one()
    {
        var folder = Path.Combine(Path.GetTempPath(), "chainsaw-route-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store = new AppSettingsStore(folder);
            Assert.Null(store.SavedRoute());

            File.WriteAllText(store.FilePath, "{ \"AutoLockMinutes\": 5 }");
            Assert.Null(store.SavedRoute());

            File.WriteAllText(store.FilePath, "{ \"ExitRoute\": \"Teleport\" }");
            Assert.Null(store.SavedRoute());

            File.WriteAllText(store.FilePath, "not json");
            Assert.Null(store.SavedRoute());

            store.Save(new AppSettings { ExitRoute = Modules.Network.Contracts.ExitRoute.Vpn });
            Assert.Equal(Modules.Network.Contracts.ExitRoute.Vpn, store.SavedRoute());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Counts what the settings asked to be shown and played.</summary>
    private sealed class TestAlerts : IAlertPreview
    {
        public int Messages { get; private set; }

        public int Sounds { get; private set; }

        public void ShowMessage() => Messages++;

        public void PlaySound() => Sounds++;

        public int Closed { get; private set; }

        public void CloseMessage() => Closed++;
    }

    [Fact]
    public void The_settings_read_at_the_start_are_marked()
    {
        var settings = new SettingsViewModel(
            new AppSettings { AutoLockMinutes = 60, Currency = FiatCurrency.Usd, LockWithWindows = false },
            _rig.SettingsStore, _rig.Way, _rig.ExitPanel, _rig.AutoLock, _rig.Prices, _rig.Profiles, _rig.Vault,
            _rig.Cases, _rig.Build);

        Assert.True(settings.LockChoices[3].IsSelected);
        Assert.True(settings.CurrencyChoices[1].IsSelected);
        Assert.True(settings.ResolutionChoices[1].IsSelected);
        Assert.False(settings.LocksWithWindows);
    }

    [Fact]
    public void The_settings_file_keeps_the_choices_as_words_and_turns_unknown_values_into_the_defaults()
    {
        var store = _rig.SettingsStore;
        store.Save(new AppSettings { ExitRoute = ExitRoute.Windows, Currency = FiatCurrency.Eur });

        var text = File.ReadAllText(store.FilePath);
        Assert.Contains("\"Windows\"", text, StringComparison.Ordinal);
        Assert.Contains("\"Eur\"", text, StringComparison.Ordinal);
        Assert.Equal(ExitRoute.Windows, store.Load().ExitRoute);

        File.WriteAllText(store.FilePath, """{ "ExitRoute": 7, "Currency": 9, "Resolution": 5 }""");
        var unknown = store.Load();
        Assert.Equal(ExitRoute.Vpn, unknown.ExitRoute);
        Assert.Equal(FiatCurrency.Sek, unknown.Currency);
        Assert.Equal(PriceResolution.Hour, unknown.Resolution);

        File.WriteAllText(store.FilePath, """{ "ExitRoute": "windows" }""");
        Assert.Equal(ExitRoute.Windows, store.Load().ExitRoute);

        File.WriteAllText(store.FilePath, """{ "ExitRoute": "Satellit" }""");
        Assert.Equal(ExitRoute.Vpn, store.Load().ExitRoute);
    }

    [Fact]
    public void The_automatic_lock_refuses_a_time_shorter_than_a_minute()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _rig.AutoLock.Change(TimeSpan.FromSeconds(10)));
        Assert.Equal(TimeSpan.FromMinutes(15), _rig.AutoLock.Timeout);
    }

    [Fact]
    public void The_graph_is_not_offline_through_windows()
    {
        var route = new TestExitRoute();
        var way = TestWays.For(_rig.Exit, route);

        route.Choose(ExitRoute.Windows);

        Assert.True(way.IsLive);
        Assert.True(way.IsWindows);
        route.Choose(ExitRoute.Vpn);
        Assert.False(way.IsLive);
    }

    [Theory]
    [InlineData(5, "5 min")]
    [InlineData(60, "1 h")]
    [InlineData(120, "2 h")]
    public void A_lock_time_is_written_as_the_sketch_writes_it(int minutes, string label)
    {
        Assert.Equal(label, SettingsViewModel.LockLabel(minutes));
    }

    /// <summary>A vault whose change of master key waits until the test lets it go.</summary>
    private sealed class SlowVault(Task released) : IVault
    {
        public VaultState State => VaultState.Unlocked;

        public event EventHandler<VaultState>? StateChanged
        {
            add { }
            remove { }
        }

        public void Create(ReadOnlySpan<char> masterKey) => throw new NotSupportedException();

        public void Unlock(ReadOnlySpan<char> masterKey) => throw new NotSupportedException();

        public void Lock()
        {
        }

        public void ChangeMasterKey(ReadOnlySpan<char> currentMasterKey, ReadOnlySpan<char> newMasterKey) =>
            released.Wait();
    }
}
