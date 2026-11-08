using Chainsaw.App.Links;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Security.Contracts;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// Bevakning's head says since when the hits are new (sketch BV1a: <em>2 nya träffar sedan 09:10</em>), and the time
/// the user looked is noted when the hits are seen.
/// </summary>
public sealed class WatchHeadTests
{
    private readonly TestWatchSource _source = new();

    private readonly TestClock _clock = new();

    [Fact]
    public async Task New_hits_are_said_since_the_user_last_looked()
    {
        var watch = _source.Add(WatchSamples.Subject(V), WatchSamples.Filter(WatchDirection.Out, [WatchSamples.On(E)]));
        _source.Hit(watch, WatchSamples.Out(WatchSamples.On(E), 0.1m));
        var looked = _clock.UtcNow.AddHours(-3);
        _source.SeenAt = looked;
        var (_, board) = WatchSamples.Board(_source);
        var view = new MonitoringViewModel(board, new WatchFormViewModel(board), _clock);
        await board.ReloadAsync();

        Assert.EndsWith(" · 1 ny träff", view.Summary, StringComparison.Ordinal);

        view.Choose(view.Rows[0]);
        await board.ReloadAsync();
        _source.Hit(watch, WatchSamples.Out(WatchSamples.On(E), 0.2m));
        _source.Hit(watch, WatchSamples.Out(WatchSamples.On(E), 0.3m));
        await board.ReloadAsync();

        Assert.Equal(looked, _source.LastSeen);
        Assert.EndsWith(
            $" · 2 nya träffar sedan {WatchWords.When(looked, _clock.UtcNow)}", view.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_new_hits_the_head_says_no_time()
    {
        _source.Add(WatchSamples.Subject(V), WatchSamples.Filter());
        _source.LastSeen = _clock.UtcNow;
        var (_, board) = WatchSamples.Board(_source);
        var view = new MonitoringViewModel(board, new WatchFormViewModel(board), _clock);

        await board.ReloadAsync();

        Assert.DoesNotContain("sedan", view.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("träff", view.Summary, StringComparison.Ordinal);
    }
}

/// <summary>The link <c>chainsaw:</c> registered for the user under Länkar (<em>Öppna med en adress</em>).</summary>
public sealed class LinkRegistrationTests
{
    private const string Program = @"C:\Program Files\Chainsaw\Chainsaw.exe";

    private readonly TestLinkRegistry _registry = new();

    [Fact]
    public void The_command_runs_the_program_with_the_link_as_one_argument()
    {
        Assert.Equal("\"C:\\Program Files\\Chainsaw\\Chainsaw.exe\" \"%1\"", LinkRegistration.CommandFor(Program));
    }

    [Fact]
    public void The_state_says_whether_the_link_opens_this_program()
    {
        var links = new LinkRegistration(_registry, Program);
        Assert.Equal(LinkState.None, links.Read());

        links.Register();
        Assert.Equal(LinkState.ThisProgram, links.Read());
        Assert.Equal(Program, _registry.Icon);

        _registry.Command = "\"D:\\Old\\Chainsaw.exe\" \"%1\"";
        Assert.Equal(LinkState.OtherProgram, links.Read());

        links.Unregister();
        Assert.Equal(LinkState.None, links.Read());
    }

    [Fact]
    public void The_switch_registers_and_takes_the_link_away()
    {
        var settings = Settings(new LinkRegistration(_registry, Program));
        Assert.True(settings.CanRegisterLinks);
        Assert.False(settings.IsLinkRegistered);

        settings.ToggleLinks();

        Assert.True(settings.IsLinkRegistered);
        Assert.Equal(LinkRegistration.CommandFor(Program), _registry.Command);

        settings.ToggleLinks();

        Assert.False(settings.IsLinkRegistered);
        Assert.Null(_registry.Command);
    }

    [Fact]
    public void A_link_to_another_copy_is_said_and_taken_over()
    {
        _registry.Command = "\"D:\\Old\\Chainsaw.exe\" \"%1\"";
        var settings = Settings(new LinkRegistration(_registry, Program));

        Assert.False(settings.IsLinkRegistered);
        Assert.Equal(SettingsViewModel.LinkElsewhereText, settings.LinkHint);

        settings.ToggleLinks();

        Assert.True(settings.IsLinkRegistered);
        Assert.Equal(SettingsViewModel.LinkHintText, settings.LinkHint);
    }

    [Fact]
    public void A_registry_that_refuses_is_said()
    {
        _registry.Refuses = true;
        var settings = Settings(new LinkRegistration(_registry, Program));

        settings.ToggleLinks();

        Assert.False(settings.IsLinkRegistered);
        Assert.Equal(SettingsViewModel.LinkFailedText, settings.ErrorText);
    }

    [Fact]
    public void Without_a_registry_the_row_is_greyed()
    {
        var settings = Settings(null);

        Assert.False(settings.CanRegisterLinks);
        settings.ToggleLinks();
        Assert.False(settings.IsLinkRegistered);
    }

    private static SettingsViewModel Settings(LinkRegistration? links)
    {
        var rig = new GateRig(VaultState.Unlocked);
        return new SettingsViewModel(
            new Chainsaw.App.Settings.AppSettings(),
            rig.SettingsStore,
            rig.Way,
            rig.ExitPanel,
            rig.AutoLock,
            rig.Prices,
            rig.Profiles,
            rig.Vault,
            rig.Cases,
            rig.Build,
            links: links);
    }

    /// <summary>The user's registry in memory.</summary>
    private sealed class TestLinkRegistry : ILinkRegistry
    {
        public string? Command { get; set; }

        public string? Icon { get; private set; }

        public bool Refuses { get; set; }

        public string? ReadCommand() => Command;

        public void Write(string command, string icon)
        {
            if (Refuses)
            {
                throw new UnauthorizedAccessException("refused");
            }

            (Command, Icon) = (command, icon);
        }

        public void Delete() => Command = null;
    }
}
