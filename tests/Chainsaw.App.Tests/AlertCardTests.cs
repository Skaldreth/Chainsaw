using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Monitoring.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>The message in the middle of the screen (sketch BV4a) and Om Chainsaw from the lock screen (ST2a).</summary>
public sealed class AlertCardTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 8, 14, 32, 0, TimeSpan.FromHours(1));

    [Fact]
    public void Chainsaw_started_with_the_argument_is_only_the_card()
    {
        Assert.Null(AlertCard.FromArguments([], isRunning: false, At));
        Assert.Null(AlertCard.FromArguments(["chainsaw:btc/bc1qexample"], isRunning: false, At));

        var closed = AlertCard.FromArguments([BackgroundNames.ScreenMessageArgument], isRunning: false, At);
        var running = AlertCard.FromArguments([BackgroundNames.ScreenMessageArgument], isRunning: true, At);

        Assert.Equal(AlertCardSource.Closed, closed?.Source);
        Assert.Equal("Öppna Chainsaw", closed?.ActionText);
        Assert.Equal(AlertCardSource.Running, running?.Source);
        Assert.True(running?.OpensChainsaw);
        Assert.Equal("14:32", closed?.TimeText);
    }

    [Theory]
    [InlineData(AlertCardSource.Preview)]
    [InlineData(AlertCardSource.Closed)]
    [InlineData(AlertCardSource.Running)]
    public void Every_card_says_only_that_a_watch_has_hit(AlertCardSource source)
    {
        var card = new AlertCard(source, At);

        Assert.Equal(source != AlertCardSource.Preview, card.ShowsFrom);
        Assert.Equal(source != AlertCardSource.Preview, card.OpensChainsaw);
        foreach (var text in new[] { AlertCard.Title, card.Text, card.ActionText })
        {
            Assert.DoesNotMatch("bc1|0x|BTC|ETH|ärende ", text);
        }
    }

    [Fact]
    public void The_preview_shows_how_it_looks_and_opens_Bevakning()
    {
        var card = new AlertCard(AlertCardSource.Preview, At);

        Assert.StartsWith("Så här ser det ut", card.Text, StringComparison.Ordinal);
        Assert.Equal("Visa", card.ActionText);
    }

    [Fact]
    public void Om_Chainsaw_opens_over_the_lock_screen_and_closes_again()
    {
        var rig = new GateRig(Modules.Security.Contracts.VaultState.Locked);
        var about = new AboutViewModel(rig.Cases, rig.Build);
        var locked = new LockViewModel(rig.Vault, rig.Way, rig.AutoLock, rig.Build, about: about);

        about.ToggleLicenses();
        locked.OpenAbout();

        Assert.True(locked.IsAboutOpen);
        Assert.Same(about, locked.About);
        Assert.False(about.IsShowingLicenses);

        locked.CloseAbout();
        Assert.False(locked.IsAboutOpen);
    }

    [Fact]
    public void Without_the_page_the_lock_screen_only_tells_the_version()
    {
        var rig = new GateRig(Modules.Security.Contracts.VaultState.Locked);

        rig.LockScreen.OpenAbout();

        Assert.False(rig.LockScreen.IsAboutOpen);
        Assert.Contains("0.1.0", rig.LockScreen.AboutDetails, StringComparison.Ordinal);
    }
}
