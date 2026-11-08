using System.Threading.Channels;
using Chainsaw.Modules.Network.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace Chainsaw.Modules.Network.Tests;

/// <summary>
/// The presence is green only while the computer has a network and the last probe reached the internet: a Wi-Fi with a
/// login page is offline, the probe runs only with a network, at once on every change, every 60 s while it answers and
/// every 10 s while it does not, and each real change is told once. The probe leaves outside any tunnel, so it is sent
/// only while Windows is the way chosen: never with VPN, and one in flight is stopped when VPN is chosen.
/// </summary>
public sealed class PresenceProbingTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly FakeSignals _signals = new();
    private readonly ScriptedProbe _probe = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ExitRouteChoice _route =
        new(new NetworkOptions { Route = ExitRoute.Windows }, new RecordingWindowsHandlerFactory());
    private readonly Channel<TimeSpan?> _waits = Channel.CreateUnbounded<TimeSpan?>();
    private readonly List<bool> _told = [];
    private NetworkPresence? _presence;

    private NetworkPresence Presence => _presence ?? throw new InvalidOperationException("Not started.");

    public void Dispose()
    {
        _presence?.Dispose();
        _route.Dispose();
    }

    [Fact]
    public async Task A_probe_that_reaches_the_internet_keeps_it_green_and_waits_a_minute()
    {
        Start();
        Assert.True(Presence.IsAvailable);

        _probe.Answer(true);

        Assert.Equal(NetworkPresence.SettledInterval, await NextWaitAsync());
        Assert.True(Presence.IsAvailable);
        Assert.Empty(_told);
    }

    [Fact]
    public async Task A_login_page_turns_it_offline_and_it_retries_every_ten_seconds()
    {
        Start();

        _probe.Answer(false);

        Assert.Equal(NetworkPresence.RetryInterval, await NextWaitAsync());
        Assert.False(Presence.IsAvailable);
        Assert.Equal([false], _told);
    }

    [Fact]
    public async Task After_the_login_it_turns_green_at_the_next_retry()
    {
        Start();
        _probe.Answer(false);
        await NextWaitAsync();

        _time.Advance(NetworkPresence.RetryInterval);
        _probe.Answer(true);

        Assert.Equal(NetworkPresence.SettledInterval, await NextWaitAsync());
        Assert.True(Presence.IsAvailable);
        Assert.Equal([false, true], _told);
        Assert.Equal(2, _probe.Calls);
    }

    [Fact]
    public async Task A_probe_that_throws_counts_as_not_reached()
    {
        Start();

        _probe.Throw();

        Assert.Equal(NetworkPresence.RetryInterval, await NextWaitAsync());
        Assert.False(Presence.IsAvailable);
    }

    [Fact]
    public async Task Without_a_network_no_probe_is_sent_until_the_network_comes()
    {
        _signals.HasNetwork = false;
        Start();

        Assert.Null(await NextWaitAsync());
        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(0, _probe.Calls);
        Assert.False(Presence.IsAvailable);

        _signals.Set(true);
        _probe.Answer(true);

        Assert.Equal(NetworkPresence.SettledInterval, await NextWaitAsync());
        Assert.Equal(1, _probe.Calls);
        Assert.Equal([true], _told);
    }

    [Fact]
    public async Task Losing_the_network_turns_it_offline_at_once_without_a_probe()
    {
        Start();
        _probe.Answer(true);
        await NextWaitAsync();

        _signals.Set(false);

        Assert.False(Presence.IsAvailable);
        Assert.Null(await NextWaitAsync());
        Assert.Equal(1, _probe.Calls);
        Assert.Equal([false], _told);
    }

    [Fact]
    public async Task A_new_address_probes_at_once_without_waiting_for_the_minute()
    {
        Start();
        _probe.Answer(true);
        await NextWaitAsync();

        _signals.Set(true);
        _probe.Answer(false);

        Assert.Equal(NetworkPresence.RetryInterval, await NextWaitAsync());
        Assert.Equal(2, _probe.Calls);
        Assert.Equal([false], _told);
    }

    [Fact]
    public async Task Each_real_change_is_told_once()
    {
        Start();
        _probe.Answer(false);
        await NextWaitAsync();
        _time.Advance(NetworkPresence.RetryInterval);
        _probe.Answer(false);
        await NextWaitAsync();

        _signals.Set(false);
        await NextWaitAsync();
        _signals.Set(true);
        _probe.Answer(true);
        await NextWaitAsync();
        _signals.Set(false);
        await NextWaitAsync();

        Assert.Equal([false, true, false], _told);
    }

    [Fact]
    public async Task A_listener_that_throws_never_keeps_the_change_from_the_others()
    {
        Start();
        var told = false;
        Presence.Changed += (_, _) => throw new InvalidOperationException("listener");
        Presence.Changed += (_, _) => told = true;

        _probe.Answer(false);
        await NextWaitAsync();

        Assert.True(told);
    }

    [Fact]
    public async Task After_dispose_nothing_is_told_or_probed()
    {
        Start();
        _probe.Answer(true);
        await NextWaitAsync();

        Presence.Dispose();
        _signals.Set(false);
        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.Empty(_told);
        Assert.Equal(1, _probe.Calls);
    }

    [Fact]
    public async Task With_vpn_chosen_no_probe_is_sent_and_only_the_network_is_told()
    {
        _route.Choose(ExitRoute.Vpn);
        Start();

        Assert.Null(await NextWaitAsync());
        _signals.Set(true);
        Assert.Null(await NextWaitAsync());
        _time.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(0, _probe.Calls);
        Assert.True(Presence.IsAvailable);
        _signals.Set(false);
        Assert.False(Presence.IsAvailable);
    }

    [Fact]
    public async Task Choosing_vpn_stops_the_probe_in_flight_and_forgets_its_last_answer()
    {
        Start();
        _probe.Answer(false);
        await NextWaitAsync();
        _time.Advance(NetworkPresence.RetryInterval);
        await Until(() => _probe.Calls == 2);

        _route.Choose(ExitRoute.Vpn);

        Assert.Null(await NextWaitAsync());
        Assert.True(_probe.Cancelled);
        Assert.True(Presence.IsAvailable);
        Assert.Equal([false, true], _told);
        _probe.Answer(false);
        _time.Advance(TimeSpan.FromMinutes(10));
        _signals.Set(true);
        Assert.Null(await NextWaitAsync());
        Assert.Equal(2, _probe.Calls);
    }

    [Fact]
    public async Task Choosing_windows_probes_at_once()
    {
        _route.Choose(ExitRoute.Vpn);
        Start();
        Assert.Null(await NextWaitAsync());

        _route.Choose(ExitRoute.Windows);
        _probe.Answer(false);

        Assert.Equal(NetworkPresence.RetryInterval, await NextWaitAsync());
        Assert.Equal(1, _probe.Calls);
        Assert.False(Presence.IsAvailable);
    }

    /// <summary>Waits until a condition holds, which the presence's own thread makes true.</summary>
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition never held.");
            await Task.Delay(10);
        }
    }

    /// <summary>Starts the presence and listens to its changes before the first probe can answer.</summary>
    private void Start()
    {
        _presence = new NetworkPresence(_signals, _probe, _route, _time, wait => _waits.Writer.TryWrite(wait));
        _presence.Changed += (_, available) =>
        {
            lock (_told)
            {
                _told.Add(available);
            }
        };
    }

    /// <summary>Waits until the presence's loop has kept its answer and started the next wait.</summary>
    private async Task<TimeSpan?> NextWaitAsync() => await _waits.Reader.ReadAsync().AsTask().WaitAsync(Patience);
}
