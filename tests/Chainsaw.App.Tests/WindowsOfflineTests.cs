using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>
/// Windows as the way out without a network (CLAUDE.md, <em>Windows som utgång</em>): green only while the computer
/// has a network, and <em>Offline</em> in red in the title bar and the exits' panel otherwise, as for a VPN exit that
/// is not connected; everything follows when the network comes back. VPN is unaffected.
/// </summary>
public sealed class WindowsOfflineTests
{
    private readonly GateRig _rig = new(VaultState.Unlocked);

    [Fact]
    public void Windows_without_a_network_is_offline_in_the_title_bar()
    {
        _rig.Settings.ChooseRoute(ExitRoute.Windows);

        _rig.Network.Set(false);

        Assert.False(_rig.Way.IsLive);
        Assert.True(_rig.Way.IsWindowsOffline);
        Assert.False(_rig.Shell.IsExitLive);
        Assert.Equal(ShellViewModel.WindowsOfflineText, _rig.Shell.ExitText);
        Assert.Equal(ShellViewModel.WindowsOfflineSourceText, _rig.Shell.SourceText);
    }

    [Fact]
    public void Windows_without_a_network_turns_the_exits_panel_red_and_offline()
    {
        _rig.Settings.ChooseRoute(ExitRoute.Windows);

        _rig.Network.Set(false);

        Assert.True(_rig.ExitPanel.IsWindowsMode);
        Assert.False(_rig.ExitPanel.IsLive);
        Assert.Equal(ExitPanelViewModel.NoRouteText, _rig.ExitPanel.RouteText);
    }

    [Fact]
    public void Everything_turns_green_again_when_the_network_is_back()
    {
        _rig.Settings.ChooseRoute(ExitRoute.Windows);
        _rig.Network.Set(false);
        var changes = 0;
        _rig.Way.Changed += (_, _) => changes++;

        _rig.Network.Set(true);

        Assert.Equal(1, changes);
        Assert.True(_rig.Way.IsLive);
        Assert.True(_rig.Shell.IsExitLive);
        Assert.Equal(ShellViewModel.WindowsText, _rig.Shell.ExitText);
        Assert.Equal(ShellViewModel.WindowsSourceText, _rig.Shell.SourceText);
        Assert.True(_rig.ExitPanel.IsLive);
        Assert.Equal(ExitPanelViewModel.WindowsRouteText, _rig.ExitPanel.RouteText);
    }

    [Fact]
    public void Choosing_windows_while_the_computer_has_no_network_shows_offline_at_once()
    {
        _rig.Network.Set(false);

        _rig.Settings.ChooseRoute(ExitRoute.Windows);

        Assert.False(_rig.Shell.IsExitLive);
        Assert.Equal(ShellViewModel.WindowsOfflineText, _rig.Shell.ExitText);
        Assert.False(_rig.ExitPanel.IsLive);
    }

    [Fact]
    public void Vpn_with_a_connected_exit_is_not_turned_offline_by_the_network()
    {
        _rig.Exit.Set(new ExitStatus(ExitState.Connected, Guid.NewGuid(), null));

        _rig.Network.Set(false);

        Assert.True(_rig.Way.IsLive);
        Assert.False(_rig.Way.IsWindowsOffline);
        Assert.True(_rig.Shell.IsExitLive);
    }

    [Fact]
    public void The_same_value_again_tells_no_change()
    {
        var changes = 0;
        _rig.Way.Changed += (_, _) => changes++;

        _rig.Network.Set(true);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void A_change_is_told_on_the_ui_thread()
    {
        var ui = new QueuedUi();
        var network = new TestNetwork();
        var way = new ExitWay(new TestExitRoute(), new TestExitConnection(), ui, network: network);
        way.Choose(ExitRoute.Windows);

        network.Set(false);

        Assert.True(way.IsLive);
        ui.RunAll();
        Assert.False(way.IsLive);
    }

    /// <summary>Keeps posted work until the test runs it, as a UI thread that is busy would.</summary>
    private sealed class QueuedUi : IUiThread
    {
        private readonly Queue<Action> _work = new();

        public void Post(Action work) => _work.Enqueue(work);

        public void RunAll()
        {
            while (_work.TryDequeue(out var work))
            {
                work();
            }
        }
    }
}
