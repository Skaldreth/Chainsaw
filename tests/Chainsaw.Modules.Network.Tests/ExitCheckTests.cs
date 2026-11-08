using System.Text;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network.Tests;

/// <summary>The check of the way out (sketch UT3b) and an exit's new name and place.</summary>
public sealed class ExitCheckTests
{
    private const string Trace =
        "fl=123\nh=1.1.1.1\nip=185.213.154.68\nts=1762600000.1\nvisit_scheme=https\nloc=SE\ntls=TLSv1.3\n";

    private static readonly DateTimeOffset Answered = new(2026, 11, 8, 10, 42, 0, TimeSpan.Zero);

    private readonly NetworkHarness _harness = new();

    [Fact]
    public void The_trace_gives_the_address_and_the_country()
    {
        Assert.Equal(("185.213.154.68", "SE"), ExitCheck.Read(Trace));
        Assert.Equal(("2a03:1b20:3::f", "NL"), ExitCheck.Read("ip=2a03:1b20:3::f\r\nloc=nl\r\n"));
    }

    [Theory]
    [InlineData("loc=SE\n")]
    [InlineData("ip=not-an-address\nloc=SE\n")]
    [InlineData("<html>Captive portal</html>")]
    public void An_answer_without_an_address_is_none(string text) => Assert.Null(ExitCheck.Read(text));

    [Theory]
    [InlineData("ip=10.0.0.1\nloc=XX1\n")]
    [InlineData("ip=10.0.0.1\nloc=\n")]
    [InlineData("ip=10.0.0.1\n")]
    public void A_country_that_is_not_two_letters_is_left_empty(string text)
    {
        var answer = ExitCheck.Read(text);
        Assert.NotNull(answer);
        Assert.Equal(string.Empty, answer.Value.Country);
    }

    [Fact]
    public async Task A_check_through_an_exit_asks_the_trace_and_is_kept_with_the_exit()
    {
        var exit = _harness.Store.Add("Stockholm", "Sverige", ExitKind.WireGuard, NetworkHarness.Configuration);
        var http = new TraceHttp(Trace, exit.Id, ExitRoute.Vpn);
        var check = new ExitCheck(http, _harness.Records, _harness.Connection);

        var sighting = await check.CheckAsync();

        Assert.Equal(IExitCheck.TraceUri, Assert.Single(http.Asked).Uri);
        Assert.Equal(new ExitSighting(exit.Id, ExitRoute.Vpn, "185.213.154.68", "SE", Answered), sighting);
        Assert.Equal(sighting, check.FindLast(exit.Id));
        Assert.Contains(_harness.Secrets.All.Keys, name => name == ExitRecords.SightingPrefix + exit.Id.ToString("N"));
    }

    [Fact]
    public async Task A_check_through_Windows_is_not_kept()
    {
        var http = new TraceHttp(Trace, Guid.Empty, ExitRoute.Windows);

        var sighting = await new ExitCheck(http, _harness.Records, _harness.Connection).CheckAsync();

        Assert.Equal(ExitRoute.Windows, sighting.Route);
        Assert.Empty(_harness.Secrets.All);
    }

    [Fact]
    public async Task An_answer_without_an_address_or_a_failed_status_is_a_failure_and_keeps_nothing()
    {
        var exit = _harness.Store.Add("Stockholm", "", ExitKind.WireGuard, NetworkHarness.Configuration);

        var none = new ExitCheck(new TraceHttp("hello", exit.Id, ExitRoute.Vpn), _harness.Records, _harness.Connection);
        var failed = new ExitCheck(
            new TraceHttp(Trace, exit.Id, ExitRoute.Vpn, status: 503), _harness.Records, _harness.Connection);

        var empty = await Assert.ThrowsAsync<ExitException>(() => none.CheckAsync());
        Assert.Equal(ExitError.RequestFailed, empty.Error);
        var refused = await Assert.ThrowsAsync<ExitException>(() => failed.CheckAsync());
        Assert.Equal(ExitError.RequestFailed, refused.Error);
        Assert.Null(none.FindLast(exit.Id));
    }

    [Fact]
    public async Task A_removed_exit_takes_its_check_with_it_and_a_locked_vault_finds_none()
    {
        var exit = _harness.Store.Add("Stockholm", "", ExitKind.WireGuard, NetworkHarness.Configuration);
        var check = new ExitCheck(new TraceHttp(Trace, exit.Id, ExitRoute.Vpn), _harness.Records, _harness.Connection);
        await check.CheckAsync();

        _harness.Secrets.Locked = true;
        Assert.Null(check.FindLast(exit.Id));
        _harness.Secrets.Locked = false;

        Assert.True(await _harness.Store.RemoveAsync(exit.Id));
        Assert.Null(check.FindLast(exit.Id));
        Assert.Empty(_harness.Secrets.All);
    }

    [Fact]
    public async Task Describe_renames_and_moves_an_exit_and_keeps_its_configuration()
    {
        var exit = _harness.Store.Add("se-sto-wg-004", "", ExitKind.WireGuard, NetworkHarness.Configuration);

        var described = await _harness.Store.DescribeAsync(exit.Id, " Stockholm ", " Sverige · Stockholm ");

        Assert.Equal(exit with { Name = "Stockholm", Place = "Sverige · Stockholm" }, described);
        var stored = _harness.Records.Find(exit.Id);
        Assert.Equal(new ExitWithConfiguration(described, NetworkHarness.Configuration), stored);
    }

    [Fact]
    public async Task Describe_refuses_an_unknown_exit_a_blank_name_and_a_long_place()
    {
        var exit = _harness.Store.Add("Exit", "", ExitKind.WireGuard, NetworkHarness.Configuration);
        var longPlace = new string('p', ExitRules.MaximumPlaceLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(() => _harness.Store.DescribeAsync(Guid.NewGuid(), "Exit", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => _harness.Store.DescribeAsync(exit.Id, "  ", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => _harness.Store.DescribeAsync(exit.Id, "Exit", longPlace));
        Assert.Equal(exit, Assert.Single(_harness.Store.List()));
    }

    [Fact]
    public async Task A_removed_exit_is_never_written_back_by_a_new_name_or_a_late_answer()
    {
        var exit = _harness.Store.Add("Stockholm", "", ExitKind.WireGuard, NetworkHarness.Configuration);
        var check = new ExitCheck(new TraceHttp(Trace, exit.Id, ExitRoute.Vpn), _harness.Records, _harness.Connection);
        Assert.True(await _harness.Store.RemoveAsync(exit.Id));

        await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Store.DescribeAsync(exit.Id, "Stockholm", "Sverige"));
        var sighting = await check.CheckAsync();

        Assert.Equal(exit.Id, sighting.ExitId);
        Assert.Empty(_harness.Secrets.All);
    }

    [Fact]
    public async Task Through_VPN_without_a_connected_exit_the_check_sends_nothing()
    {
        var check = new ExitCheck(_harness.Http, _harness.Records, _harness.Connection);

        await Assert.ThrowsAsync<ExitNotConnectedException>(() => check.CheckAsync());

        Assert.Equal(ExitRoute.Vpn, _harness.Route.Current);
        Assert.Empty(_harness.Handlers.Handlers);
        Assert.Empty(_harness.WindowsHandlers.Handlers);
        Assert.Empty(_harness.Secrets.All);
    }

    /// <summary>A way out that answers every question with the trace given.</summary>
    private sealed class TraceHttp(string body, Guid exitId, ExitRoute route, int status = 200) : IExitHttp
    {
        public List<ExitRequest> Asked { get; } = [];

        public Task<ExitResponse> SendAsync(ExitRequest request, CancellationToken cancellationToken = default)
        {
            Asked.Add(request);
            return Task.FromResult(new ExitResponse(
                request.Uri, exitId, status, "text/plain", Encoding.UTF8.GetBytes(body), Answered, Answered, route));
        }
    }
}
