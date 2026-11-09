using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>
/// The switch under Nätverk that lets the background go out through the selected exit (decided 2026-11-08): it
/// allows or forbids the selected exit, follows the file rather than what it showed, and is unusable while the
/// background is off.
/// </summary>
public sealed class SettingsExitInBackgroundTests
{
    private readonly BackgroundFakes.MemoryExits _exits = new();

    private readonly SelectedStore _store = new();

    [Fact]
    public async Task The_switch_allows_and_forbids_the_selected_exit_and_says_the_settings_changed()
    {
        var settings = Settings(new Settings.AppSettings());
        var changes = 0;
        settings.SettingsChanged += (_, _) => changes++;

        settings.Open(Chainsaw.App.Settings.SettingsPage.Network);
        await WaitAsync(() => settings.CanExitInBackground);
        await settings.ToggleExitInBackgroundAsync();

        Assert.Contains(_store.Exit.Id, _exits.Allowed);
        Assert.True(settings.ExitInBackground);
        await settings.ToggleExitInBackgroundAsync();
        Assert.Empty(_exits.Allowed);
        Assert.False(settings.ExitInBackground);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task With_the_background_off_the_switch_cannot_be_used()
    {
        var settings = Settings(new Settings.AppSettings { WatchInBackground = false });

        settings.Open(Chainsaw.App.Settings.SettingsPage.Network);
        await Task.Delay(50);
        await settings.ToggleExitInBackgroundAsync();

        Assert.False(settings.CanExitInBackground);
        Assert.Empty(_exits.Allowed);
    }

    /// <summary>Makes the settings with the exits and a store whose selected exit is known.</summary>
    private SettingsViewModel Settings(Settings.AppSettings values)
    {
        var rig = new GateRig(VaultState.Unlocked);
        return new SettingsViewModel(
            values,
            rig.SettingsStore,
            rig.Way,
            rig.ExitPanel,
            rig.AutoLock,
            rig.Prices,
            rig.Profiles,
            rig.Vault,
            rig.Cases,
            rig.Build,
            backgroundExits: _exits,
            exitStore: _store);
    }

    /// <summary>Waits until a condition holds, at most two seconds.</summary>
    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var round = 0; round < 200 && !condition(); round++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    /// <summary>An exit store with one selected exit.</summary>
    private sealed class SelectedStore : IExitStore
    {
        public ExitInfo Exit { get; } = new(Guid.NewGuid(), "Exit A", "Oslo", ExitKind.WireGuard);

        public IReadOnlyList<ExitInfo> List() => [Exit];

        public ExitInfo Add(string name, string place, ExitKind kind, string configuration) =>
            throw new NotSupportedException();

        public Task<ExitInfo> DescribeAsync(Guid id, string name, string place) => throw new NotSupportedException();

        public Task<bool> RemoveAsync(Guid id) => Task.FromResult(false);

        public ExitInfo? FindSelected() => Exit;

        public Task SelectAsync(Guid? id) => Task.CompletedTask;
    }
}
