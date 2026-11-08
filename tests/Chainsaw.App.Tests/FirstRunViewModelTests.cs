using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

public sealed class FirstRunViewModelTests
{
    private const string Key = "correct horse battery";

    [Fact]
    public void The_setup_starts_at_welcome_with_the_first_dash_filled()
    {
        var setup = new GateRig(VaultState.NotCreated).FirstRun;

        Assert.Equal(FirstRunStep.Welcome, setup.Step);
        Assert.Equal("Steg 1 av 4 · Välkommen", setup.StepLabel);
        Assert.Equal([true, false, false, false], setup.Dots.Select(d => d.IsReached));
        Assert.False(setup.CanGoBack);
        Assert.Equal("Nästa", setup.NextLabel);
    }

    [Fact]
    public async Task Welcome_needs_a_name()
    {
        var setup = new GateRig(VaultState.NotCreated).FirstRun;

        await setup.NextAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.Equal(FirstRunStep.Welcome, setup.Step);
        Assert.True(setup.HasError);
    }

    [Fact]
    public async Task A_short_master_key_creates_no_vault_and_is_cleared()
    {
        var rig = await AtMasterKey();
        var key = new TestEntry("kort");
        var repeat = new TestEntry("kort");

        await rig.FirstRun.NextAsync(key, repeat);

        Assert.Equal(FirstRunStep.MasterKey, rig.FirstRun.Step);
        Assert.Equal($"Huvudnyckeln ska ha minst {MasterKeyRules.MinimumLength} tecken.", rig.FirstRun.ErrorText);
        Assert.Null(rig.Vault.CreatedWith);
        Assert.True(key.AllCleared && repeat.AllCleared);
    }

    [Fact]
    public async Task Two_different_master_keys_create_no_vault()
    {
        var rig = await AtMasterKey();

        await rig.FirstRun.NextAsync(new TestEntry(Key), new TestEntry(Key + "!"));

        Assert.Equal(FirstRunStep.MasterKey, rig.FirstRun.Step);
        Assert.Null(rig.Vault.CreatedWith);
    }

    [Fact]
    public async Task The_master_key_creates_the_vault_stores_the_profile_and_is_cleared()
    {
        var rig = await AtMasterKey();
        var key = new TestEntry(Key);
        var repeat = new TestEntry(Key);

        await rig.FirstRun.NextAsync(key, repeat);

        Assert.Equal(FirstRunStep.Exit, rig.FirstRun.Step);
        Assert.Equal(Key, rig.Vault.CreatedWith);
        Assert.Equal(new OperatorProfile("Anna Andersson", "Utredare", "Enheten"), rig.Profiles.Saved);
        Assert.True(key.AllCleared && repeat.AllCleared);
        Assert.True(rig.FirstRun.IsMasterKeyChosen);
    }

    [Fact]
    public async Task Going_back_after_the_vault_is_made_only_says_the_key_is_chosen()
    {
        var rig = await AtExit();

        rig.FirstRun.Back();
        await rig.FirstRun.NextAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.Equal(FirstRunStep.Exit, rig.FirstRun.Step);
        Assert.Equal(Key, rig.Vault.CreatedWith);
    }

    [Fact]
    public async Task A_new_name_after_the_vault_is_made_is_stored_again()
    {
        var rig = await AtExit();
        rig.FirstRun.Back();
        rig.FirstRun.Back();
        rig.FirstRun.Name = "Anna Berg";

        await rig.FirstRun.NextAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.Equal("Anna Berg", rig.Profiles.Saved?.Name);
    }

    [Fact]
    public async Task A_wireguard_file_is_stored_through_network_and_selected()
    {
        var rig = await AtExit();
        var file = Path.Combine(Path.GetTempPath(), $"Stockholm-{Guid.NewGuid():N}.conf");
        await File.WriteAllTextAsync(file, WireGuardCheckTests.Valid);
        try
        {
            await rig.FirstRun.AddExitAsync(file);
        }
        finally
        {
            File.Delete(file);
        }

        var added = Assert.Single(rig.Exits.Added);
        Assert.StartsWith("Stockholm-", added.Name, StringComparison.Ordinal);
        Assert.Equal(WireGuardCheckTests.Valid, added.Configuration);
        Assert.NotNull(rig.Exits.Selected);
        Assert.True(rig.FirstRun.HasExit);
        Assert.Equal(FirstRunStep.Exit, rig.FirstRun.Step);
        Assert.StartsWith("Stockholm-", rig.FirstRun.ExitFileName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_step_Utgang_has_Windows_chosen_from_the_start_with_a_live_route()
    {
        var rig = await AtExit();
        var setup = rig.FirstRun;

        Assert.True(setup.IsWindowsChosen);
        Assert.False(setup.NeedsExitFile);
        Assert.True(setup.IsRouteLive);
        Assert.Equal("Windows", setup.RouteName);
        Assert.Equal("Datorn når internet.", setup.RouteVerdict);
        Assert.Equal("Fortsätt", setup.NextLabel);

        await setup.NextAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.Equal(FirstRunStep.FirstCase, setup.Step);
        Assert.Equal(Modules.Network.Contracts.ExitRoute.Windows, rig.Route.Current);
        await rig.Settings.Saving;
        Assert.Equal(Modules.Network.Contracts.ExitRoute.Windows, rig.SettingsStore.Load().ExitRoute);
    }

    [Fact]
    public void The_step_Utgang_starts_from_the_way_out_the_settings_file_names()
    {
        var rig = new GateRig(VaultState.NotCreated);
        rig.SettingsStore.Save(new Settings.AppSettings { ExitRoute = Modules.Network.Contracts.ExitRoute.Vpn });
        var settings = new SettingsViewModel(
            new Settings.AppSettings(), rig.SettingsStore, rig.Way, rig.ExitPanel, rig.AutoLock, rig.Prices,
            rig.Profiles, rig.Vault, rig.Cases, rig.Build);

        var setup = new FirstRunViewModel(
            rig.Vault, rig.Profiles, rig.Exits, rig.Workflow, rig.Way, settings, rig.Exit);

        Assert.True(setup.IsVpnChosen);
        Assert.True(new GateRig(VaultState.NotCreated).FirstRun.IsWindowsChosen);
    }

    [Fact]
    public async Task Windows_without_a_network_says_so_and_goes_on_offline()
    {
        var rig = await AtExit();
        rig.Network.Set(false);

        Assert.False(rig.FirstRun.IsRouteLive);
        Assert.Equal("Offline", rig.FirstRun.RouteName);
        Assert.Equal("Datorn når inte internet.", rig.FirstRun.RouteVerdict);
        Assert.Equal("Fortsätt offline", rig.FirstRun.NextLabel);
    }

    [Fact]
    public async Task VPN_asks_for_a_file_and_goes_on_honestly_without_an_exit()
    {
        var rig = await AtExit();
        var setup = rig.FirstRun;

        setup.ChooseRoute(Modules.Network.Contracts.ExitRoute.Vpn);

        Assert.True(setup.IsVpnChosen);
        Assert.True(setup.NeedsExitFile);
        Assert.False(setup.IsRouteLive);
        Assert.Equal("Ingen utgång", setup.RouteName);
        Assert.Equal("Inga uppslag.", setup.RouteVerdict);
        Assert.Equal("Fortsätt utan utgång", setup.NextLabel);
        Assert.Equal(Modules.Network.Contracts.ExitRoute.Vpn, rig.Route.Current);
    }

    [Fact]
    public async Task A_stored_exit_that_connects_makes_the_route_live_with_its_name()
    {
        var rig = await AtExit();
        rig.FirstRun.ChooseRoute(Modules.Network.Contracts.ExitRoute.Vpn);
        var file = Path.Combine(Path.GetTempPath(), $"Stockholm-{Guid.NewGuid():N}.conf");
        await File.WriteAllTextAsync(file, WireGuardCheckTests.Valid);
        try
        {
            await rig.FirstRun.AddExitAsync(file);
        }
        finally
        {
            File.Delete(file);
        }

        Assert.False(rig.FirstRun.NeedsExitFile);
        Assert.Equal(ExitRowViewModel.NotConnectedText, rig.FirstRun.ExitStateText);
        Assert.Equal(rig.FirstRun.ExitName, rig.FirstRun.RouteName);

        rig.Exit.Set(new Modules.Network.Contracts.ExitStatus(
            Modules.Network.Contracts.ExitState.Connected, rig.Exits.Selected, null));

        Assert.True(rig.FirstRun.IsRouteLive);
        Assert.True(rig.FirstRun.IsExitOn);
        Assert.Equal(ExitRowViewModel.ConnectedText, rig.FirstRun.ExitStateText);
        Assert.Equal("Ansluten.", rig.FirstRun.RouteVerdict);
        Assert.Equal("Fortsätt", rig.FirstRun.NextLabel);
    }

    [Fact]
    public async Task A_file_network_refuses_is_said_in_swedish_and_the_step_stays()
    {
        var rig = await AtExit();
        rig.Exits.Failure = new ArgumentException("The configuration must name a DNS server.", "configuration");
        var file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, WireGuardCheckTests.Valid);
        try
        {
            await rig.FirstRun.AddExitAsync(file);
        }
        finally
        {
            File.Delete(file);
        }

        Assert.Equal(FirstRunStep.Exit, rig.FirstRun.Step);
        Assert.Contains("DNS =", rig.FirstRun.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("configuration", rig.FirstRun.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_that_fails_the_check_says_what_is_missing_and_is_not_stored()
    {
        var rig = await AtExit();
        var file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, WireGuardCheckTests.Valid.Replace("Address", "# Address"));
        try
        {
            await rig.FirstRun.AddExitAsync(file);
        }
        finally
        {
            File.Delete(file);
        }

        Assert.Empty(rig.Exits.Added);
        Assert.Equal(FirstRunStep.Exit, rig.FirstRun.Step);
        Assert.Contains("Saknar Address", rig.FirstRun.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_file_is_said()
    {
        var rig = await AtExit();

        await rig.FirstRun.AddExitAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.Equal("Filen gick inte att läsa.", rig.FirstRun.ErrorText);
    }

    [Fact]
    public async Task Going_on_without_a_file_goes_to_the_first_case_without_an_exit()
    {
        var rig = await AtExit();

        await rig.FirstRun.NextAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.Equal(FirstRunStep.FirstCase, rig.FirstRun.Step);
        Assert.Empty(rig.Exits.Added);
        Assert.Equal("Skapa ärendet", rig.FirstRun.NextLabel);
    }

    [Theory]
    [InlineData("", "Skriv ett ärendenummer, eller hoppa över.")]
    [InlineData("a/b", "Ärendenumret får ha bokstäver")]
    public async Task A_case_number_that_cannot_be_used_creates_nothing(string number, string text)
    {
        var rig = await AtFirstCase();
        rig.FirstRun.CaseNumber = number;

        await rig.FirstRun.NextAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.StartsWith(text, rig.FirstRun.ErrorText, StringComparison.Ordinal);
        Assert.Empty(rig.Cases.Created);
    }

    [Fact]
    public async Task A_case_that_exists_is_said()
    {
        var rig = await AtFirstCase();
        rig.Cases.Failure = new CaseException(CaseError.AlreadyExists);
        rig.FirstRun.CaseNumber = "5000-K123-26";

        await rig.FirstRun.NextAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.Equal("Det finns redan ett ärende med det numret.", rig.FirstRun.ErrorText);
        Assert.Null(rig.Session.Current);
    }

    [Fact]
    public async Task The_first_case_is_created_through_cases_opened_and_ends_the_setup()
    {
        var rig = await AtFirstCase();
        var completed = false;
        rig.FirstRun.Completed += (_, _) => completed = true;
        rig.FirstRun.CaseNumber = " 5000-K123-26 ";

        await rig.FirstRun.NextAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.Equal(["5000-K123-26"], rig.Cases.Created);
        Assert.Equal("5000-K123-26", rig.Session.Current?.Info.CaseNumber);
        Assert.Equal("5000-K123-26", rig.Shell.CaseTitle);
        Assert.True(completed);
        Assert.Equal(GateMode.Unlocked, rig.Gate.Mode);
    }

    [Fact]
    public async Task Skipping_the_first_case_ends_the_setup_without_a_case()
    {
        var rig = await AtFirstCase();

        rig.FirstRun.Skip();

        Assert.Empty(rig.Cases.Created);
        Assert.Equal(GateMode.Unlocked, rig.Gate.Mode);
        Assert.Equal(ShellViewModel.NoCaseText, rig.Shell.CaseTitle);
    }

    [Fact]
    public void The_view_model_holds_no_property_for_the_key()
    {
        var names = typeof(FirstRunViewModel).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(names, n => n.Contains("Password", StringComparison.OrdinalIgnoreCase));
        var keyNames = names.Where(n => n.Contains("Key", StringComparison.Ordinal)).Order();
        Assert.Equal(["IsMasterKey", "IsMasterKeyChosen"], keyNames);
    }

    [Fact]
    public async Task The_first_case_gets_its_title_and_start_point()
    {
        var rig = await AtFirstCase();
        rig.FirstRun.CaseNumber = "2026-0117";
        rig.FirstRun.CaseTitle = "Investeringsbedrägeri via chatt";
        rig.FirstRun.StartPointText = Samples.Address;

        Assert.True(rig.FirstRun.HasStartPoint);
        Assert.Equal("Bitcoin-adress", rig.FirstRun.StartPoint.KindText);
        await rig.FirstRun.NextAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.Equal("2026-0117 · Investeringsbedrägeri via chatt", rig.Shell.CaseTitle);
        Assert.Equal(Samples.Address, rig.Tracing.Added.Single().StartPoint.ToString());
    }

    [Fact]
    public async Task A_start_point_that_fails_in_the_first_case_is_said_in_the_shell()
    {
        var rig = await AtFirstCase();
        rig.FirstRun.CaseNumber = "2026-0117";
        rig.FirstRun.StartPointText = Samples.Address;
        rig.Tracing.Failure = new CaseException(CaseError.NoOperatorProfile);

        await rig.FirstRun.NextAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.True(rig.Gate.IsShellVisible);
        Assert.True(rig.Shell.IsShowingError);
        Assert.Contains("lades inte till", rig.Shell.CommandAnswer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_first_case_cut_short_by_a_lock_is_said_in_the_shell_after_the_unlock()
    {
        var rig = await AtFirstCase();
        rig.FirstRun.CaseNumber = "2026-0117";
        rig.Cases.DuringCreate = () => rig.Vault.Lock();
        await rig.FirstRun.NextAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));
        rig.Cases.DuringCreate = null;

        await rig.LockScreen.UnlockAsync(new TestEntry("correct horse battery"));

        Assert.True(rig.Gate.IsShellVisible);
        Assert.Contains("/open 2026-0117", rig.Shell.CommandAnswer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_link_that_waited_through_the_setup_explains_itself_after_the_first_case()
    {
        var rig = await AtFirstCase();
        rig.Links.Receive("chainsaw:bitcoin/" + Samples.Address);
        rig.FirstRun.CaseNumber = "2026-0117";

        await rig.FirstRun.NextAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.Equal(Samples.Address, rig.Shell.CommandText);
        Assert.Contains("Från en länk", rig.Shell.CommandAnswer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_start_point_creates_no_first_case()
    {
        var rig = await AtFirstCase();
        rig.FirstRun.CaseNumber = "2026-0117";
        rig.FirstRun.StartPointText = "hej";

        await rig.FirstRun.NextAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.Empty(rig.Cases.Created);
        Assert.True(rig.FirstRun.HasError);
    }

    private static async Task<GateRig> AtMasterKey()
    {
        var rig = new GateRig(VaultState.NotCreated);
        rig.FirstRun.Name = "Anna Andersson";
        rig.FirstRun.Title = "Utredare";
        rig.FirstRun.Organization = "Enheten";
        await rig.FirstRun.NextAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));
        Assert.Equal(FirstRunStep.MasterKey, rig.FirstRun.Step);
        return rig;
    }

    private static async Task<GateRig> AtExit()
    {
        var rig = await AtMasterKey();
        await rig.FirstRun.NextAsync(new TestEntry(Key), new TestEntry(Key));
        Assert.Equal(FirstRunStep.Exit, rig.FirstRun.Step);
        return rig;
    }

    private static async Task<GateRig> AtFirstCase()
    {
        var rig = await AtExit();
        await rig.FirstRun.NextAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));
        Assert.Equal(FirstRunStep.FirstCase, rig.FirstRun.Step);
        return rig;
    }
}
