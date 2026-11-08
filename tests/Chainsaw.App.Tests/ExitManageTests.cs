using Chainsaw.App.Exits;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>The exits in Inställningar › Nätverk (sketch UT3b): name and place, removing, and the check.</summary>
public sealed class ExitManageTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 10, 42, 0, TimeSpan.Zero);

    private static readonly ExitInfo Stockholm =
        new(Guid.NewGuid(), "Stockholm", "Sverige · Stockholm", ExitKind.WireGuard);

    private static readonly ExitInfo Zurich = new(Guid.NewGuid(), "Zürich", string.Empty, ExitKind.WireGuard);

    [Fact]
    public void A_place_is_a_country_from_the_list_and_an_optional_city()
    {
        var sweden = ExitPlace.ByCode("se");

        Assert.Equal("Sverige", sweden?.Name);
        Assert.Equal("Sverige · Stockholm", ExitPlace.Compose(sweden, " Stockholm "));
        Assert.Equal("Sverige", ExitPlace.Compose(sweden, " "));
        Assert.Equal("Göteborg", ExitPlace.Compose(null, "Göteborg"));
        Assert.Equal(sweden, ExitPlace.CountryOf("Sverige · Stockholm"));
        Assert.Equal("Stockholm", ExitPlace.CityOf("Sverige · Stockholm"));
        Assert.Null(ExitPlace.CountryOf("Stockholm"));
        Assert.Equal("Stockholm", ExitPlace.CityOf("Stockholm"));
        Assert.Equal("Nederländerna", ExitPlace.NameOf("NL"));
        Assert.Equal("XK", ExitPlace.NameOf("xk"));
    }

    [Fact]
    public void The_countries_are_in_Swedish_order_with_unique_codes()
    {
        var names = ExitPlace.Countries.Select(country => country.Name).ToList();

        Assert.Equal("Albanien", names[0]);
        Assert.True(names.IndexOf("Österrike") is -1 or > 0);
        Assert.Equal(names.Count, ExitPlace.Countries.Select(country => country.Code).Distinct().Count());
        Assert.All(ExitPlace.Countries, country => Assert.Matches("^[A-Z]{2}$", country.Code));
    }

    [Fact]
    public void An_answer_in_the_place_country_is_seen_and_another_country_is_a_mismatch()
    {
        var seen = new ExitSighting(Stockholm.Id, ExitRoute.Vpn, "185.213.154.68", "SE", Now);
        var elsewhere = seen with { Address = "89.45.90.12", CountryCode = "NL" };

        Assert.Equal(ExitCheckState.Seen, ExitCheckWords.StateOf(seen, Stockholm.Place));
        Assert.Equal(ExitCheckState.Mismatch, ExitCheckWords.StateOf(elsewhere, Stockholm.Place));
        Assert.Equal(ExitCheckState.Seen, ExitCheckWords.StateOf(elsewhere, string.Empty));
        Assert.Equal("Nederländerna · angivet Sverige", ExitCheckWords.CountryText(elsewhere, Stockholm.Place));
        Assert.Equal(
            "Stockholm syns som 89.45.90.12 i Nederländerna, angivet Sverige.",
            ExitCheckWords.MismatchText("Stockholm", elsewhere, Stockholm.Place));

        var facts = ExitCheckWords.FactsFor(elsewhere, Stockholm.Place, Now);
        Assert.Equal(["Syns som", "Land", "Källa"], facts.Select(fact => fact.Key));
        Assert.True(facts[0].IsAddress);
        Assert.Equal(ExitFactTone.Warning, facts[1].Tone);
        Assert.StartsWith("1.1.1.1 · i dag ", facts[2].Value, StringComparison.Ordinal);
        Assert.Equal("Sverige · som angivet", ExitCheckWords.FactsFor(seen, Stockholm.Place, Now)[1].Value);
    }

    [Fact]
    public void An_earlier_day_is_written_short_in_Swedish()
    {
        var earlier = Now.AddDays(-2);

        Assert.DoesNotContain("i dag", ExitCheckWords.When(earlier, Now), StringComparison.Ordinal);
        Assert.Equal("i dag", ExitCheckWords.Day(Now, Now));
        Assert.Matches(@"^\d{1,2} nov$", ExitCheckWords.Day(earlier, Now));
    }

    [Fact]
    public async Task The_rows_show_the_place_and_the_kept_check_and_say_honestly_when_none_is()
    {
        var rig = new Rig();
        rig.Check.Kept[Stockholm.Id] = new ExitSighting(Stockholm.Id, ExitRoute.Vpn, "185.213.154.68", "SE", Now);

        await rig.ShowAsync();

        var stockholm = rig.Row(Stockholm);
        Assert.Equal("Sverige · Stockholm", stockholm.PlaceText);
        Assert.Equal(ExitCheckState.Seen, stockholm.Check);
        Assert.Equal("185.213.154.68", stockholm.SeenText);
        Assert.Equal("Sverige · i dag", stockholm.SeenSubText);
        var zurich = rig.Row(Zurich);
        Assert.False(zurich.HasPlace);
        Assert.Equal(ExitPlace.NoPlaceText, zurich.PlaceText);
        Assert.Equal(ExitCheckState.NotChecked, zurich.Check);
        Assert.Equal(ExitCheckWords.NotCheckedText, zurich.SeenText);
        Assert.Equal(ExitCheckWords.NotCheckedTip, zurich.SeenTip);
    }

    [Fact]
    public async Task A_row_folds_out_with_its_name_and_place_and_another_folds_it_in()
    {
        var rig = new Rig();
        await rig.ShowAsync();
        var stockholm = rig.Row(Stockholm);

        rig.Panel.ToggleExpand(stockholm);

        Assert.True(stockholm.IsExpanded);
        Assert.Equal("Stockholm", stockholm.EditName);
        Assert.Equal("SE", stockholm.EditCountry?.Code);
        Assert.Equal("Stockholm", stockholm.EditCity);

        rig.Panel.ToggleExpand(rig.Row(Zurich));

        Assert.False(stockholm.IsExpanded);
        Assert.True(rig.Row(Zurich).IsExpanded);

        await rig.Panel.RefreshAsync();
        Assert.True(rig.Row(Zurich).IsExpanded);

        rig.Panel.LeaveSettings();
        Assert.False(rig.Row(Zurich).IsExpanded);
    }

    [Fact]
    public async Task A_new_name_and_place_are_stored_and_shown_and_an_empty_name_is_not()
    {
        var rig = new Rig();
        await rig.ShowAsync();
        var zurich = rig.Row(Zurich);
        rig.Panel.ToggleExpand(zurich);

        zurich.EditName = " Zürich 2 ";
        zurich.EditCountry = ExitPlace.ByCode("CH");
        zurich.EditCity = "Zürich";
        await rig.Panel.DescribeAsync(zurich);

        Assert.Equal(("Zürich 2", "Schweiz · Zürich"), (zurich.Name, zurich.Place));
        Assert.Equal(("Zürich 2", "Schweiz · Zürich"), (rig.Store.Known[1].Name, rig.Store.Known[1].Place));
        Assert.Equal(1, rig.Store.Describes);

        await rig.Panel.DescribeAsync(zurich);
        Assert.Equal(1, rig.Store.Describes);

        zurich.EditName = "  ";
        await rig.Panel.DescribeAsync(zurich);
        Assert.Equal(ExitPanelViewModel.NameNeededText, rig.Panel.ErrorText);
        Assert.Equal(1, rig.Store.Describes);
    }

    [Fact]
    public async Task A_locked_vault_while_describing_is_said()
    {
        var rig = new Rig();
        await rig.ShowAsync();
        var zurich = rig.Row(Zurich);
        rig.Panel.ToggleExpand(zurich);
        rig.Store.Failure = new VaultException(VaultError.Locked);

        zurich.EditCity = "Bern";
        await rig.Panel.DescribeAsync(zurich);

        Assert.Equal(ExitPanelViewModel.LockedText, rig.Panel.ErrorText);
        Assert.Equal(string.Empty, zurich.Place);
    }

    [Fact]
    public async Task An_exit_is_removed_only_after_the_question()
    {
        var rig = new Rig();
        await rig.ShowAsync();
        var zurich = rig.Row(Zurich);

        await rig.Panel.RemoveAsync(zurich);
        Assert.Equal(2, rig.Store.Known.Count);

        rig.Panel.AskRemove(zurich);
        Assert.False(zurich.IsRemoving);
        rig.Panel.ToggleExpand(zurich);
        rig.Panel.AskRemove(zurich);
        Assert.True(zurich.IsRemoving);
        Assert.Contains("WireGuard-filen tas bort ur valvet", zurich.RemoveText, StringComparison.Ordinal);
        rig.Panel.CancelRemove(zurich);
        Assert.False(zurich.IsRemoving);

        rig.Panel.AskRemove(zurich);
        await rig.Panel.RemoveAsync(zurich);

        Assert.Equal([Stockholm.Id], rig.Store.Known.Select(exit => exit.Id));
        Assert.Equal(["Stockholm"], rig.Panel.Rows.Select(row => row.Name));
    }

    [Fact]
    public async Task Only_a_connected_exit_is_checked_and_its_answer_is_shown()
    {
        var rig = new Rig();
        await rig.ShowAsync();
        var stockholm = rig.Row(Stockholm);
        rig.Panel.ChecksOnConnect = false;

        await rig.Panel.CheckNowAsync(stockholm);
        Assert.Equal(0, rig.Check.Asked);
        Assert.False(stockholm.CanCheck);

        rig.Connection.Set(new ExitStatus(ExitState.Connected, Stockholm.Id, null));
        Assert.True(stockholm.CanCheck);
        rig.Check.Answer = new ExitSighting(Stockholm.Id, ExitRoute.Vpn, "185.213.154.68", "SE", Now);
        await rig.Panel.CheckNowAsync(stockholm);

        Assert.Equal(1, rig.Check.Asked);
        Assert.Equal(ExitCheckState.Seen, stockholm.Check);
        Assert.Equal("185.213.154.68", stockholm.SeenText);
        Assert.Equal("Sverige", stockholm.SeenSubText);
        Assert.Equal(ExitRowViewModel.ConnectedText, stockholm.ConnectionText);
    }

    [Fact]
    public async Task A_check_that_gets_no_answer_says_so_and_never_shows_an_old_address_as_now()
    {
        var rig = new Rig();
        rig.Check.Kept[Stockholm.Id] = new ExitSighting(Stockholm.Id, ExitRoute.Vpn, "185.213.154.68", "SE", Now);
        await rig.ShowAsync();
        rig.Panel.ChecksOnConnect = false;
        rig.Connection.Set(new ExitStatus(ExitState.Connected, Stockholm.Id, null));
        var stockholm = rig.Row(Stockholm);
        rig.Check.Failure = new ExitException(ExitError.Timeout);

        await rig.Panel.CheckNowAsync(stockholm);

        Assert.Equal(ExitCheckState.Failed, stockholm.Check);
        Assert.Equal(ExitCheckWords.FailedText, stockholm.SeenText);
        Assert.Equal(ExitCheckWords.FailedFacts(), stockholm.Facts);
    }

    [Fact]
    public async Task An_answer_from_another_exit_is_not_given_to_the_row()
    {
        var rig = new Rig();
        await rig.ShowAsync();
        rig.Panel.ChecksOnConnect = false;
        rig.Connection.Set(new ExitStatus(ExitState.Connected, Stockholm.Id, null));
        rig.Check.Answer = new ExitSighting(Zurich.Id, ExitRoute.Vpn, "10.0.0.1", "CH", Now);

        await rig.Panel.CheckNowAsync(rig.Row(Stockholm));

        Assert.Equal(ExitCheckState.Failed, rig.Row(Stockholm).Check);
    }

    [Fact]
    public async Task Connecting_checks_once_and_a_country_other_than_the_place_is_told_in_the_toaster()
    {
        var rig = new Rig();
        await rig.ShowAsync();
        rig.Check.Answer = new ExitSighting(Stockholm.Id, ExitRoute.Vpn, "89.45.90.12", "NL", Now);

        rig.Connection.Set(new ExitStatus(ExitState.Connected, Stockholm.Id, null));
        Assert.True(SpinWait.SpinUntil(() => rig.Toaster.Toasts.Count == 1, TimeSpan.FromSeconds(5)));
        rig.Connection.Set(new ExitStatus(ExitState.Connected, Stockholm.Id, null));

        Assert.Equal(1, rig.Check.Asked);
        Assert.Equal(ExitCheckState.Mismatch, rig.Row(Stockholm).Check);
        var toast = Assert.Single(rig.Toaster.Toasts);
        Assert.Equal(ToastKind.Warning, toast.Kind);
        Assert.Equal(ExitCheckWords.MismatchTitle, toast.Title);
        Assert.DoesNotContain("ärende", toast.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Without_the_setting_connecting_asks_nothing()
    {
        var rig = new Rig();
        await rig.ShowAsync();
        rig.Panel.ChecksOnConnect = false;

        rig.Connection.Set(new ExitStatus(ExitState.Connected, Stockholm.Id, null));

        Assert.Equal(0, rig.Check.Asked);
        Assert.Empty(rig.Toaster.Toasts);
    }

    [Fact]
    public async Task With_Windows_chosen_connecting_asks_nothing()
    {
        var rig = new Rig();
        await rig.ShowAsync();
        rig.Route.Choose(ExitRoute.Windows);

        // Windows itself is checked once as it comes online; connecting an exit then asks nothing more.
        await rig.Panel.WindowsChecked;
        var asked = rig.Check.Asked;
        rig.Connection.Set(new ExitStatus(ExitState.Connected, Stockholm.Id, null));
        await rig.Panel.WindowsChecked;

        Assert.Equal(asked, rig.Check.Asked);
        Assert.Equal(ExitCheckState.NotChecked, rig.Row(Stockholm).Check);
    }

    [Fact]
    public async Task With_the_vault_locked_connecting_asks_nothing()
    {
        var rig = new Rig(VaultState.Locked);
        await rig.ShowAsync();

        rig.Connection.Set(new ExitStatus(ExitState.Connected, Stockholm.Id, null));

        Assert.Equal(0, rig.Check.Asked);
        Assert.Empty(rig.Toaster.Toasts);
    }

    [Fact]
    public void The_title_bars_tip_says_the_address_the_country_and_an_earlier_day()
    {
        var seen = new ExitSighting(Stockholm.Id, ExitRoute.Vpn, "185.213.154.68", "SE", Now);

        Assert.Equal("syns som 185.213.154.68, Sverige", ExitCheckWords.TipText(seen, Now));
        Assert.Equal(
            "syns som 185.213.154.68 · " + ExitCheckWords.Day(Now, Now.AddDays(2)),
            ExitCheckWords.TipText(seen with { CountryCode = string.Empty }, Now.AddDays(2)));
    }

    [Fact]
    public async Task A_connected_exit_is_said_in_the_tip_as_its_last_check_saw_it()
    {
        var rig = new Rig();
        rig.Check.Kept[Stockholm.Id] = new ExitSighting(Stockholm.Id, ExitRoute.Vpn, "185.213.154.68", "SE", Now);
        rig.Check.Answer = rig.Check.Kept[Stockholm.Id];
        await rig.ShowAsync();
        Assert.Equal(string.Empty, rig.Panel.SeenTip);

        rig.Connection.Set(new ExitStatus(ExitState.Connected, Stockholm.Id, null));

        Assert.Equal("syns som 185.213.154.68, Sverige", rig.Panel.SeenTip);
        rig.Connection.Set(ExitStatus.NotConnected);
        Assert.Equal(string.Empty, rig.Panel.SeenTip);
    }

    [Fact]
    public async Task Windows_as_the_way_out_is_checked_once_while_online_and_said_in_the_tip()
    {
        var rig = new Rig();
        rig.Check.Answer = new ExitSighting(Guid.Empty, ExitRoute.Windows, "81.226.17.40", "SE", Now);

        rig.Route.Choose(ExitRoute.Windows);
        await rig.Panel.WindowsChecked;
        rig.Route.Choose(ExitRoute.Windows);

        Assert.Equal(1, rig.Check.Asked);
        Assert.Equal("syns som 81.226.17.40, Sverige", rig.Panel.SeenTip);
        Assert.Empty(rig.Check.Kept);
    }

    [Fact]
    public async Task Windows_is_not_checked_while_the_vault_is_locked_or_checks_are_off()
    {
        var locked = new Rig(VaultState.Locked);
        var off = new Rig();
        off.Panel.ChecksOnConnect = false;

        locked.Route.Choose(ExitRoute.Windows);
        off.Route.Choose(ExitRoute.Windows);
        await Task.WhenAll(locked.Panel.WindowsChecked, off.Panel.WindowsChecked);

        Assert.Equal(0, locked.Check.Asked);
        Assert.Equal(0, off.Check.Asked);
        Assert.Equal(string.Empty, off.Panel.SeenTip);
    }

    [Fact]
    public async Task A_failed_check_of_Windows_leaves_the_tip_without_an_address()
    {
        var rig = new Rig();
        rig.Check.Failure = new ExitException(ExitError.RequestFailed);

        rig.Route.Choose(ExitRoute.Windows);
        await rig.Panel.WindowsChecked;

        Assert.Equal(1, rig.Check.Asked);
        Assert.Equal(string.Empty, rig.Panel.SeenTip);
    }

    /// <summary>The panel on the settings' page with test doubles.</summary>
    private sealed class Rig
    {
        public Rig(VaultState vault = VaultState.Unlocked)
        {
            Store.Known.AddRange([Stockholm, Zurich]);
            var ui = new InlineUi();
            Toaster = new Toaster(ui);
            Panel = new ExitPanelViewModel(
                Connection, TestWays.For(Connection, Route), Store, new TestFilePicker(), ui, Check, Toaster,
                new TestClock { UtcNow = Now }, new TestVault(vault));
        }

        public ManageStore Store { get; } = new();

        public TestExitRoute Route { get; } = new();

        public TestExitConnection Connection { get; } = new();

        public FakeCheck Check { get; } = new();

        public Toaster Toaster { get; }

        public ExitPanelViewModel Panel { get; }

        public ExitRowViewModel Row(ExitInfo exit) => Panel.Rows.Single(row => row.Id == exit.Id);

        public Task ShowAsync() => Panel.ShowInSettingsAsync();
    }

    /// <summary>Exits in a list that can be described and removed.</summary>
    private sealed class ManageStore : IExitStore
    {
        public List<ExitInfo> Known { get; } = [];

        public Exception? Failure { get; set; }

        public int Describes { get; private set; }

        public IReadOnlyList<ExitInfo> List() => [.. Known];

        public ExitInfo Add(string name, string place, ExitKind kind, string configuration) =>
            throw new NotSupportedException();

        public Task<ExitInfo> DescribeAsync(Guid id, string name, string place)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Describes++;
            var index = Known.FindIndex(exit => exit.Id == id);
            Known[index] = Known[index] with { Name = name, Place = place };
            return Task.FromResult(Known[index]);
        }

        public Task<bool> RemoveAsync(Guid id) => Task.FromResult(Known.RemoveAll(exit => exit.Id == id) > 0);

        public ExitInfo? FindSelected() => null;

        public Task SelectAsync(Guid? id) => Task.CompletedTask;
    }

    /// <summary>A check that answers as the test says, and keeps answers as the vault would.</summary>
    private sealed class FakeCheck : IExitCheck
    {
        public Dictionary<Guid, ExitSighting> Kept { get; } = [];

        public ExitSighting? Answer { get; set; }

        public Exception? Failure { get; set; }

        public int Asked { get; private set; }

        public Task<ExitSighting> CheckAsync(CancellationToken cancellationToken = default)
        {
            Asked++;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(Answer ?? throw new ExitException(ExitError.RequestFailed));
        }

        public ExitSighting? FindLast(Guid exitId) => Kept.GetValueOrDefault(exitId);
    }
}
