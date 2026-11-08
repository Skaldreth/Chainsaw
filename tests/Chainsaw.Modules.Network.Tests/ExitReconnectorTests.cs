using Chainsaw.Modules.Network.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace Chainsaw.Modules.Network.Tests;

/// <summary>
/// The tunnel connected again when it falls (TODO, <em>Återanslutning av tunneln</em>): a growing wait, an attempt at
/// once when the network comes back, and the user's own choice never undone. No network and no tunnel: the
/// connection and the clock are test doubles.
/// </summary>
public sealed class ExitReconnectorTests : IDisposable
{
    private static readonly Guid ExitId = Guid.NewGuid();

    private static readonly ExitStatus Lost = new(ExitState.Failed, ExitId, ExitFailure.ConnectionLost);

    private static readonly ExitStatus Connected = new(ExitState.Connected, ExitId, null);

    private readonly FakeTimeProvider _time = new();

    private readonly ScriptedConnection _connection = new();

    private readonly TestPresence _presence = new();

    private readonly ExitReconnector _reconnector;

    public ExitReconnectorTests() => _reconnector = new ExitReconnector(_connection, _presence, _time);

    public void Dispose() => _reconnector.Dispose();

    [Fact]
    public void The_waits_grow_and_then_stay_at_a_minute()
    {
        var waits = Enumerable.Range(0, 8).Select(ExitReconnector.WaitBefore).ToList();

        Assert.Equal(TimeSpan.FromSeconds(2), waits[0]);
        Assert.All(waits.Zip(waits.Skip(1)), pair => Assert.True(pair.First <= pair.Second));
        Assert.Equal(TimeSpan.FromMinutes(1), waits[^1]);
    }

    [Fact]
    public void Only_failures_that_can_pass_are_tried_again()
    {
        Assert.True(ExitReconnector.CanPass(ExitFailure.ConnectionLost));
        Assert.True(ExitReconnector.CanPass(ExitFailure.NoAnswer));
        Assert.True(ExitReconnector.CanPass(ExitFailure.ConnectFailed));
        Assert.False(ExitReconnector.CanPass(ExitFailure.ConfigurationRefused));
        Assert.False(ExitReconnector.CanPass(ExitFailure.TunnelUnavailable));
        Assert.False(ExitReconnector.CanPass(null));
    }

    [Fact]
    public async Task A_lost_tunnel_is_connected_again_after_the_first_wait()
    {
        _connection.Results.Enqueue(Connected);

        _connection.Tell(Lost);
        await Until(() => _reconnector.NextAttempt is not null);

        Assert.Equal(_time.GetUtcNow() + TimeSpan.FromSeconds(2), _reconnector.NextAttempt);
        Assert.Equal(0, _connection.Attempts);

        _time.Advance(TimeSpan.FromSeconds(2));
        await Until(() => _connection.Attempts == 1 && _reconnector.NextAttempt is null);

        Assert.Equal(ExitState.Connected, _connection.Status.State);
        Assert.Equal(0, _reconnector.Attempts);
    }

    [Fact]
    public async Task Each_failed_attempt_waits_longer_and_the_state_is_said()
    {
        _connection.Results.Enqueue(new ExitStatus(ExitState.Failed, ExitId, ExitFailure.NoAnswer));
        _connection.Results.Enqueue(new ExitStatus(ExitState.Failed, ExitId, ExitFailure.NoAnswer));
        _connection.Results.Enqueue(Connected);
        var told = 0;
        _reconnector.Changed += (_, _) => Interlocked.Increment(ref told);

        _connection.Tell(Lost);
        await Until(() => _reconnector.NextAttempt is not null);
        _time.Advance(TimeSpan.FromSeconds(2));
        await Until(() => _connection.Attempts == 1 && _reconnector.NextAttempt is not null);

        Assert.Equal(1, _reconnector.Attempts);
        Assert.Equal(_time.GetUtcNow() + TimeSpan.FromSeconds(5), _reconnector.NextAttempt);

        _time.Advance(TimeSpan.FromSeconds(4));
        await Task.Delay(50);
        Assert.Equal(1, _connection.Attempts);

        _time.Advance(TimeSpan.FromSeconds(1));
        await Until(() => _connection.Attempts == 2 && _reconnector.NextAttempt is not null);
        Assert.Equal(_time.GetUtcNow() + TimeSpan.FromSeconds(15), _reconnector.NextAttempt);

        _time.Advance(TimeSpan.FromSeconds(15));
        await Until(() => _connection.Attempts == 3 && _reconnector.NextAttempt is null);
        Assert.True(told >= 4);
    }

    [Fact]
    public async Task The_network_coming_back_tries_at_once()
    {
        _connection.Results.Enqueue(new ExitStatus(ExitState.Failed, ExitId, ExitFailure.NoAnswer));
        _connection.Results.Enqueue(Connected);
        _connection.Tell(Lost);
        await Until(() => _reconnector.NextAttempt is not null);
        _time.Advance(TimeSpan.FromSeconds(2));
        await Until(() => _connection.Attempts == 1 && _reconnector.NextAttempt is not null);

        _presence.Tell(true);

        await Until(() => _connection.Attempts == 2);
        Assert.Equal(ExitState.Connected, _connection.Status.State);
    }

    [Fact]
    public async Task The_network_going_away_does_not_wake_the_wait()
    {
        _connection.Tell(Lost);
        await Until(() => _reconnector.NextAttempt is not null);

        _presence.Tell(false);
        await Task.Delay(50);

        Assert.Equal(0, _connection.Attempts);
    }

    [Fact]
    public async Task A_disconnect_by_the_user_while_waiting_ends_it()
    {
        _connection.Tell(Lost);
        await Until(() => _reconnector.NextAttempt is not null);

        _connection.Tell(ExitStatus.NotConnected);
        await Until(() => _reconnector.NextAttempt is null);
        _time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);

        Assert.Equal(0, _connection.Attempts);
    }

    [Fact]
    public async Task A_status_changed_meanwhile_is_read_before_the_attempt()
    {
        _connection.Tell(Lost);
        await Until(() => _reconnector.NextAttempt is not null);

        // As if the user had disconnected and the event had not come yet.
        _connection.Status = ExitStatus.NotConnected;
        _time.Advance(TimeSpan.FromSeconds(2));
        await Until(() => _reconnector.NextAttempt is null);

        Assert.Equal(0, _connection.Attempts);
    }

    [Fact]
    public async Task A_refused_configuration_is_not_tried_again()
    {
        _connection.Results.Enqueue(new ExitStatus(ExitState.Failed, ExitId, ExitFailure.ConfigurationRefused));
        _connection.Tell(Lost);
        await Until(() => _reconnector.NextAttempt is not null);

        _time.Advance(TimeSpan.FromSeconds(2));
        await Until(() => _connection.Attempts == 1 && _reconnector.NextAttempt is null);
        _time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);

        Assert.Equal(1, _connection.Attempts);
    }

    [Fact]
    public async Task A_cancelled_attempt_ends_it()
    {
        _connection.Cancels = true;
        _connection.Tell(Lost);
        await Until(() => _reconnector.NextAttempt is not null);

        _time.Advance(TimeSpan.FromSeconds(2));
        await Until(() => _connection.Attempts == 1 && _reconnector.NextAttempt is null);
        _time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);

        Assert.Equal(1, _connection.Attempts);
    }

    [Fact]
    public async Task A_connection_that_failed_to_start_is_never_taken_up()
    {
        _connection.Tell(new ExitStatus(ExitState.Failed, ExitId, ExitFailure.NoAnswer));
        await Task.Delay(50);

        Assert.Null(_reconnector.NextAttempt);
        _time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);
        Assert.Equal(0, _connection.Attempts);
    }

    [Fact]
    public async Task Nothing_is_tried_after_it_is_disposed()
    {
        _connection.Tell(Lost);
        await Until(() => _reconnector.NextAttempt is not null);

        _reconnector.Dispose();
        await Until(() => _reconnector.NextAttempt is null);
        _time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);

        Assert.Equal(0, _connection.Attempts);
    }

    [Fact]
    public async Task The_real_connection_gets_a_new_tunnel_when_the_first_falls()
    {
        var harness = new NetworkHarness();
        using var reconnector = new ExitReconnector(harness.Connection, null, _time);
        var exit = await harness.ConnectedExitAsync();

        harness.Connector.Tunnels[0].Drop();
        await Until(() => reconnector.NextAttempt is not null);
        _time.Advance(TimeSpan.FromSeconds(2));

        await Until(() => harness.Connection.Status.State == ExitState.Connected);
        Assert.Equal(2, harness.Connector.Tunnels.Count);
        Assert.True(harness.Connection.TryLease(out var lease));
        Assert.Equal(exit.Id, lease.ExitId);
    }

    [Fact]
    public async Task A_locked_vault_is_waited_out_and_the_tunnel_comes_back_after_the_unlock()
    {
        var harness = new NetworkHarness();
        using var reconnector = new ExitReconnector(harness.Connection, null, _time);
        await harness.ConnectedExitAsync();
        harness.Secrets.Locked = true;

        harness.Connector.Tunnels[0].Drop();
        await Until(() => reconnector.NextAttempt is not null);
        _time.Advance(TimeSpan.FromSeconds(2));
        await Until(() => reconnector.Attempts == 1 && reconnector.NextAttempt is not null);
        Assert.Equal(ExitFailure.ConnectionLost, harness.Connection.Status.Failure);

        harness.Secrets.Locked = false;
        _time.Advance(TimeSpan.FromSeconds(5));

        await Until(() => harness.Connection.Status.State == ExitState.Connected);
        Assert.Equal(2, harness.Connector.Tunnels.Count);
    }

    [Fact]
    public async Task The_real_connection_disconnected_by_the_user_is_left_alone()
    {
        var harness = new NetworkHarness();
        using var reconnector = new ExitReconnector(harness.Connection, null, _time);
        await harness.ConnectedExitAsync();
        harness.Connector.Tunnels[0].Drop();
        await Until(() => reconnector.NextAttempt is not null);

        await harness.Connection.DisconnectAsync();
        await Until(() => reconnector.NextAttempt is null);
        _time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);

        Assert.Single(harness.Connector.Tunnels);
        Assert.Equal(ExitStatus.NotConnected, harness.Connection.Status);
    }

    /// <summary>Waits a bounded while for something the reconnecting does on the thread pool.</summary>
    private static async Task Until(Func<bool> condition)
    {
        for (var tries = 0; tries < 400 && !condition(); tries++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    /// <summary>A connection whose attempts give what the test queued, and whose events the test tells.</summary>
    private sealed class ScriptedConnection : IExitConnection
    {
        private int _attempts;

        public event EventHandler<ExitStatus>? StatusChanged;

        public ExitStatus Status { get; set; } = ExitStatus.NotConnected;

        public System.Collections.Concurrent.ConcurrentQueue<ExitStatus> Results { get; } = new();

        public bool Cancels { get; set; }

        public int Attempts => Volatile.Read(ref _attempts);

        public void Tell(ExitStatus status)
        {
            Status = status;
            StatusChanged?.Invoke(this, status);
        }

        public Task<ExitStatus> ConnectAsync(CancellationToken cancellationToken = default)
        {
            // The real connection's own events while it connects, which the reconnecting must pass over.
            Tell(ExitStatus.NotConnected);
            Tell(new ExitStatus(ExitState.Connecting, ExitId, null));
            Interlocked.Increment(ref _attempts);
            if (Cancels)
            {
                Tell(ExitStatus.NotConnected);
                throw new OperationCanceledException();
            }

            Tell(Results.TryDequeue(out var result) ? result : Lost);
            return Task.FromResult(Status);
        }

        public Task DisconnectAsync()
        {
            Tell(ExitStatus.NotConnected);
            return Task.CompletedTask;
        }
    }

    /// <summary>A network presence the test tells.</summary>
    private sealed class TestPresence : INetworkPresence
    {
        public event EventHandler<bool>? Changed;

        public bool IsAvailable { get; private set; } = true;

        public void Tell(bool available)
        {
            IsAvailable = available;
            Changed?.Invoke(this, available);
        }
    }
}
