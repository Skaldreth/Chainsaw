using System.Globalization;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>
/// One rule for saying that Chainsaw is offline (TODO, <em>Grafens text när Windows är utan nät</em>, and the
/// reconnecting of a fallen tunnel): Windows without a network is never told as an exit that is not connected, and a
/// tunnel being connected again says so with the next attempt's time, in the title bar, the graph, Nytt ärende and
/// Bevakning alike.
/// </summary>
public sealed class OfflineWordsTests
{
    private static readonly Guid ExitId = Guid.NewGuid();

    private static readonly ExitStatus Lost = new(ExitState.Failed, ExitId, ExitFailure.ConnectionLost);

    private readonly GateRig _rig = new(VaultState.Unlocked);

    [Fact]
    public void Vpn_without_an_exit_says_no_exit()
    {
        Assert.Same(OfflineWords.NoExit, _rig.Way.Offline);
        Assert.Equal(ShellViewModel.OfflineText, _rig.Shell.SourceText);
    }

    [Fact]
    public void Windows_without_a_network_says_no_network_everywhere()
    {
        _rig.Settings.ChooseRoute(ExitRoute.Windows);
        _rig.Network.Set(false);

        Assert.Same(OfflineWords.NoNetwork, _rig.Way.Offline);
        Assert.Equal(OfflineWords.NoNetworkText, _rig.NewCase.ExitChipTip);
        Assert.DoesNotContain("utgång", _rig.NewCase.ExitChipTip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_start_point_waiting_without_a_network_waits_for_the_network()
    {
        _rig.Settings.ChooseRoute(ExitRoute.Windows);
        _rig.Network.Set(false);
        _rig.NewCase.Show();

        _rig.NewCase.Paste(Samples.Address);

        Assert.True(_rig.NewCase.Rows[0].IsWaiting);
        Assert.Equal("Väntar på nätet", _rig.NewCase.Rows[0].LineText);

        _rig.Settings.ChooseRoute(ExitRoute.Vpn);

        Assert.Equal(StartPointRowViewModel.WaitingText, _rig.NewCase.Rows[0].LineText);
    }

    [Fact]
    public void A_fallen_tunnel_being_connected_again_says_so_with_the_next_attempt()
    {
        var next = new DateTimeOffset(2026, 11, 8, 14, 2, 31, TimeSpan.Zero);
        _rig.Exit.Set(Lost);

        _rig.Reconnect.Set(next, 1);

        Assert.True(_rig.Way.IsReconnecting);
        Assert.Equal(ShellViewModel.ReconnectingText, _rig.Shell.ExitText);
        var time = next.ToLocalTime().ToString("HH:mm:ss", CultureInfo.GetCultureInfo("sv-SE"));
        Assert.Contains($"Nästa försök kl. {time}.", _rig.Shell.SourceText, StringComparison.Ordinal);
        Assert.StartsWith(OfflineWords.ReconnectingText, _rig.NewCase.ExitChipTip, StringComparison.Ordinal);
    }

    [Fact]
    public void An_attempt_under_way_is_still_reconnecting_and_connected_ends_it()
    {
        _rig.Exit.Set(Lost);
        _rig.Reconnect.Set(DateTimeOffset.UnixEpoch, 0);
        _rig.Exit.Set(new ExitStatus(ExitState.Connecting, ExitId, null));
        _rig.Reconnect.Set(null, 1);

        Assert.Equal(ShellViewModel.ReconnectingText, _rig.Shell.ExitText);
        Assert.Equal(OfflineWords.ReconnectingText, _rig.Way.Offline.Text);

        _rig.Exit.Set(new ExitStatus(ExitState.Connected, ExitId, null));
        _rig.Reconnect.Set(null, 0);

        Assert.False(_rig.Way.IsReconnecting);
        Assert.True(_rig.Shell.IsExitLive);
    }

    [Fact]
    public void A_failure_without_reconnecting_says_no_exit()
    {
        _rig.Exit.Set(new ExitStatus(ExitState.Failed, ExitId, ExitFailure.ConfigurationRefused));

        Assert.False(_rig.Way.IsReconnecting);
        Assert.Equal("Utgången är inte ansluten", _rig.Shell.ExitText);
        Assert.Equal(ShellViewModel.OfflineText, _rig.Shell.SourceText);
    }

    [Fact]
    public void Windows_is_never_reconnecting_even_while_the_vpn_tunnel_is()
    {
        _rig.Exit.Set(Lost);
        _rig.Reconnect.Set(DateTimeOffset.UnixEpoch, 1);

        _rig.Settings.ChooseRoute(ExitRoute.Windows);

        Assert.False(_rig.Way.IsReconnecting);
        Assert.Equal(ShellViewModel.WindowsText, _rig.Shell.ExitText);
    }

    [Fact]
    public void The_empty_graph_says_why_it_is_offline()
    {
        var start = new GraphStartViewModel(new CaseSession(), _rig.Workflow, new InlineUi(), new TestClock());

        start.ShowExit(isOnline: false, name: null);
        Assert.Equal(GraphStartViewModel.OfflineFootText, start.FootText);
        Assert.Equal(GraphStartViewModel.OfflineSendText, start.SendText);

        start.ShowOffline(OfflineWords.NoNetwork);

        Assert.Equal("Offline: datorn når inte internet", start.FootText);
        Assert.Equal(OfflineWords.NoNetwork.Hint, start.FootTip);
        Assert.EndsWith("förrän datorn når internet igen.", start.SendText, StringComparison.Ordinal);
        Assert.DoesNotContain("utgång", start.SendText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bevakning_without_a_network_waits_for_the_network()
    {
        var source = new TestWatchSource { GateState = WatchGate.NoExit };
        source.Add(WatchSamples.Subject(GraphSamples.V), WatchSamples.Filter());
        var session = new CaseSession();
        session.Open(new TestCase("2026-0026"));
        _rig.Settings.ChooseRoute(ExitRoute.Windows);
        _rig.Network.Set(false);
        var board = new WatchBoard(session, source, new InlineUi(), _rig.Way);
        var view = new MonitoringViewModel(board, new WatchFormViewModel(board), new TestClock());

        await board.ReloadAsync();

        Assert.Equal("Väntar på nätet" + WatchWords.NoLookupsText, view.BackgroundText);
        Assert.Equal(OfflineWords.NoNetwork.Hint, view.BackgroundTip);
        Assert.Equal("Väntar på nätet", view.Rows[0].StateText);
    }
}
