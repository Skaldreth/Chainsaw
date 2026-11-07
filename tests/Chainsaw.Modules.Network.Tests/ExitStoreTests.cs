using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Network.Tests;

public sealed class ExitStoreTests
{
    private readonly NetworkHarness _harness = new();

    [Fact]
    public void A_new_store_has_no_exits_and_none_selected()
    {
        Assert.Empty(_harness.Store.List());
        Assert.Null(_harness.Store.FindSelected());
    }

    [Fact]
    public void Added_exits_are_listed_by_name_without_their_configuration()
    {
        var zulu = _harness.Store.Add("Zulu", "Oslo", ExitKind.WireGuard, NetworkHarness.Configuration);
        var alpha = _harness.Store.Add("  Alpha ", "", ExitKind.WireGuard, NetworkHarness.Configuration);

        Assert.Equal([alpha, zulu], _harness.Store.List());
        Assert.Equal("Alpha", alpha.Name);
    }

    [Fact]
    public void Each_exit_is_one_secret_named_only_by_its_id()
    {
        var exit = _harness.Store.Add("Exit A", "Stockholm", ExitKind.WireGuard, NetworkHarness.Configuration);

        var (name, value) = Assert.Single(_harness.Secrets.All);
        Assert.Equal(ExitRecords.ExitPrefix + exit.Id.ToString("N"), name);
        Assert.Contains("test-private-key-not-real", value, StringComparison.Ordinal);
        Assert.Equal(new ExitWithConfiguration(exit, NetworkHarness.Configuration), _harness.Records.Find(exit.Id));
    }

    [Theory]
    [InlineData("", "Oslo", NetworkHarness.Configuration)]
    [InlineData("   ", "Oslo", NetworkHarness.Configuration)]
    [InlineData("Exit", "Oslo", "")]
    [InlineData("Exit", "Oslo", "  \n ")]
    public void Add_refuses_a_blank_name_or_configuration(string name, string place, string configuration)
    {
        Assert.Throws<ArgumentException>(() => _harness.Store.Add(name, place, ExitKind.WireGuard, configuration));
        Assert.Empty(_harness.Secrets.All);
    }

    [Fact]
    public void Add_refuses_too_long_values_and_an_unknown_kind()
    {
        var store = _harness.Store;
        var longName = new string('n', ExitRules.MaximumNameLength + 1);
        var longPlace = new string('p', ExitRules.MaximumPlaceLength + 1);
        var longConfiguration = new string('c', ExitRules.MaximumConfigurationLength + 1);

        Assert.Throws<ArgumentException>(() => store.Add(longName, "", ExitKind.WireGuard, "x"));
        Assert.Throws<ArgumentException>(() => store.Add("Exit", longPlace, ExitKind.WireGuard, "x"));
        Assert.Throws<ArgumentException>(() => store.Add("Exit", "", ExitKind.WireGuard, longConfiguration));
        Assert.Throws<ArgumentException>(() => store.Add("Exit", "", (ExitKind)42, "x"));
        Assert.Throws<ArgumentNullException>(() => store.Add(null!, "", ExitKind.WireGuard, "x"));
        Assert.Empty(_harness.Secrets.All);
    }

    [Theory]
    [InlineData("[Interface]\nPrivateKey = k\n")]
    [InlineData("[Interface]\nPrivateKey = k\n# DNS = 10.64.0.1\n")]
    [InlineData("[Interface]\nPrivateKey = k\nDNS =\n")]
    [InlineData("[Interface]\nPrivateKey = k\nDNS = example.internal\n")]
    [InlineData("[Interface]\nPrivateKey = k\nDNS = 10\n")]
    [InlineData("[Interface]\nPrivateKey = k\nDNS = 1.2.3\n")]
    [InlineData("[Interface]\nPrivateKey = k\nDNS = 010.0.0.1\n")]
    [InlineData("[Interface]\nPrivateKey = k\nDNS = 0x0a.0.0.1\n")]
    [InlineData("[Interface]\nPrivateKey = k\nDNS = [2606:4700::1111]\n")]
    [InlineData("[Interface]\nPrivateKey = k\nDNS = [2606:4700::1111]:53\n")]
    [InlineData("[Interface]\nPrivateKey = k\n[Peer]\nDNS = 10.64.0.1\n")]
    [InlineData("DNS = 10.64.0.1\n[Interface]\nPrivateKey = k\n")]
    public void A_configuration_without_a_dns_server_is_refused(string configuration)
    {
        Assert.Throws<ArgumentException>(() => _harness.Store.Add("Exit", "", ExitKind.WireGuard, configuration));
        Assert.Empty(_harness.Secrets.All);
    }

    [Theory]
    [InlineData("[Interface]\nPrivateKey = k\nDNS = 10.64.0.1\n")]
    [InlineData("[Interface]\r\nPrivateKey = k\r\n  dns=10.64.0.1, 10.64.0.2\r\n")]
    [InlineData("[interface]\nDNS = example.internal, fd00::1 # the exit's own\n[Peer]\n")]
    public void A_configuration_with_a_dns_server_is_accepted(string configuration)
    {
        _harness.Store.Add("Exit", "", ExitKind.WireGuard, configuration);

        Assert.Single(_harness.Secrets.All);
    }

    [Fact]
    public void The_message_of_a_refused_configuration_does_not_contain_it()
    {
        var secret = "PrivateKey = " + new string('k', ExitRules.MaximumConfigurationLength);

        var exception = Assert.Throws<ArgumentException>(
            () => _harness.Store.Add("Exit", "", ExitKind.WireGuard, secret));

        Assert.DoesNotContain("PrivateKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Selecting_stores_the_selection_in_the_vault()
    {
        var exit = await _harness.SelectedExitAsync();

        Assert.Equal(exit, _harness.Store.FindSelected());
        Assert.Equal(exit.Id.ToString("N"), _harness.Secrets.All[ExitRecords.SelectedName]);

        await _harness.Store.SelectAsync(null);

        Assert.Null(_harness.Store.FindSelected());
        Assert.False(_harness.Secrets.All.ContainsKey(ExitRecords.SelectedName));
    }

    [Fact]
    public async Task Selecting_an_unknown_exit_is_refused_and_disconnects_nothing()
    {
        var exit = await _harness.ConnectedExitAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _harness.Store.SelectAsync(Guid.NewGuid()));

        Assert.Equal(exit, _harness.Store.FindSelected());
        Assert.Equal(ExitState.Connected, _harness.Connection.Status.State);
    }

    [Fact]
    public async Task Selecting_another_exit_disconnects_the_connected_one_first()
    {
        await _harness.ConnectedExitAsync("Exit A");
        var other = _harness.Store.Add("Exit B", "Oslo", ExitKind.WireGuard, NetworkHarness.Configuration);

        await _harness.Store.SelectAsync(other.Id);

        Assert.Equal(ExitStatus.NotConnected, _harness.Connection.Status);
        Assert.True(_harness.Connector.Tunnels[0].Disposed);
        Assert.False(_harness.Connection.TryLease(out _));
    }

    [Fact]
    public async Task Clearing_the_selection_disconnects()
    {
        await _harness.ConnectedExitAsync();

        await _harness.Store.SelectAsync(null);

        Assert.Equal(ExitStatus.NotConnected, _harness.Connection.Status);
    }

    [Fact]
    public async Task Selecting_the_connected_exit_again_keeps_it_connected()
    {
        var exit = await _harness.ConnectedExitAsync();

        await _harness.Store.SelectAsync(exit.Id);

        Assert.Equal(ExitState.Connected, _harness.Connection.Status.State);
        Assert.False(_harness.Connector.Tunnels[0].Disposed);
    }

    [Fact]
    public async Task Removing_the_connected_exit_disconnects_it_and_clears_the_selection()
    {
        var exit = await _harness.ConnectedExitAsync();

        Assert.True(await _harness.Store.RemoveAsync(exit.Id));

        Assert.Equal(ExitStatus.NotConnected, _harness.Connection.Status);
        Assert.Null(_harness.Store.FindSelected());
        Assert.Empty(_harness.Store.List());
        Assert.Empty(_harness.Secrets.All);
        Assert.False(await _harness.Store.RemoveAsync(exit.Id));
    }

    [Fact]
    public async Task Removing_another_exit_keeps_the_connected_one()
    {
        await _harness.ConnectedExitAsync("Exit A");
        var other = _harness.Store.Add("Exit B", "Oslo", ExitKind.WireGuard, NetworkHarness.Configuration);

        Assert.True(await _harness.Store.RemoveAsync(other.Id));

        Assert.Equal(ExitState.Connected, _harness.Connection.Status.State);
    }

    [Fact]
    public void An_unreadable_exit_in_the_vault_is_treated_as_missing()
    {
        var id = Guid.NewGuid();
        _harness.Secrets.Save(ExitRecords.ExitPrefix + id.ToString("N"), "{ not json");
        _harness.Secrets.Save(ExitRecords.ExitPrefix + "not-a-guid", "{}");

        Assert.Empty(_harness.Store.List());
        Assert.Null(_harness.Records.Find(id));
    }

    [Fact]
    public void An_exit_with_its_configuration_never_shows_the_configuration_as_text()
    {
        var exit = _harness.Store.Add("Exit A", "Stockholm", ExitKind.WireGuard, NetworkHarness.Configuration);

        var found = _harness.Records.Find(exit.Id)!;

        Assert.DoesNotContain("test-private-key-not-real", found.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            "PrivateKey",
            new StoredExit("A", "B", ExitKind.WireGuard, NetworkHarness.Configuration).ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_locked_vault_is_reported_as_locked()
    {
        _harness.Secrets.Locked = true;

        var exception = Assert.Throws<VaultException>(() => _harness.Store.List());

        Assert.Equal(VaultError.Locked, exception.Error);
    }
}
