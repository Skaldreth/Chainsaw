using System.Net;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network.Tests;

/// <summary>Attempts, disconnects, disposal and events that overlap.</summary>
public sealed class ExitConnectionRaceTests
{
    private readonly NetworkHarness _harness = new();

    [Fact]
    public async Task A_connector_that_returns_no_tunnel_leaves_the_exit_failed()
    {
        var exit = await _harness.SelectedExitAsync();
        _harness.Connector.ReturnNull = true;

        var status = await _harness.Connection.ConnectAsync();

        Assert.Equal(new ExitStatus(ExitState.Failed, exit.Id, ExitFailure.ConnectFailed), status);
    }

    [Fact]
    public async Task A_disconnect_cancels_an_attempt_that_is_still_waiting_while_the_old_tunnel_stops()
    {
        await _harness.ConnectedExitAsync();
        var slowStop = new TaskCompletionSource();
        _harness.Connector.Tunnels[0].StopGate = slowStop.Task;
        _harness.Connector.Gate = new TaskCompletionSource();

        var reconnecting = _harness.Connection.ConnectAsync();
        var disconnecting = _harness.Connection.DisconnectAsync();
        slowStop.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reconnecting);
        await disconnecting;
        Assert.Equal(ExitStatus.NotConnected, _harness.Connection.Status);
        Assert.Single(_harness.Connector.Tunnels);
    }

    [Fact]
    public async Task A_dispose_while_the_old_tunnel_stops_starts_no_new_tunnel()
    {
        await _harness.ConnectedExitAsync();
        var slowStop = new TaskCompletionSource();
        _harness.Connector.Tunnels[0].StopGate = slowStop.Task;

        var reconnecting = _harness.Connection.ConnectAsync();
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            slowStop.SetResult();
        });

        // Dispose cancels the waiting attempt and waits for the old tunnel's stop to end.
        _harness.Connection.Dispose();

        Assert.True(_harness.Connector.Tunnels[0].Disposed);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reconnecting);
        Assert.Single(_harness.Connector.Tunnels);
        Assert.Equal(ExitStatus.NotConnected, _harness.Connection.Status);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _harness.Connection.ConnectAsync());
    }

    [Fact]
    public async Task A_tunnel_handed_over_after_dispose_is_stopped_at_once()
    {
        await _harness.SelectedExitAsync();
        var release = new TaskCompletionSource();
        var connector = new HandOverAfterConnector(release.Task);
        var connection = new ExitConnection(_harness.Records, connector, _harness.Handlers);

        var connecting = connection.ConnectAsync();
        connection.Dispose();
        release.SetResult();

        // The connector ignores its token and hands the tunnel over anyway; the publication refuses it.
        await Assert.ThrowsAsync<ObjectDisposedException>(() => connecting);
        Assert.True(connector.Tunnel.Disposed);
        Assert.False(connection.TryLease(out _));
        Assert.Equal(ExitStatus.NotConnected, connection.Status);
    }

    [Fact]
    public async Task A_disconnect_that_cancels_an_attempt_whose_tunnel_comes_anyway_stops_it()
    {
        await _harness.SelectedExitAsync();
        var release = new TaskCompletionSource();
        var connector = new HandOverAfterConnector(release.Task);
        var connection = new ExitConnection(_harness.Records, connector, _harness.Handlers);

        var connecting = connection.ConnectAsync();
        var disconnecting = connection.DisconnectAsync();
        release.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);
        await disconnecting;
        Assert.True(connector.Tunnel.Disposed);
        Assert.Equal(ExitStatus.NotConnected, connection.Status);
    }

    [Fact]
    public async Task Disposing_raises_the_status_not_connected()
    {
        await _harness.ConnectedExitAsync();

        _harness.Connection.Dispose();

        Assert.Equal(ExitStatus.NotConnected, _harness.Events[^1]);
    }

    [Fact]
    public async Task A_handler_that_throws_does_not_stop_a_disconnect_or_a_change()
    {
        await _harness.ConnectedExitAsync();
        _harness.Connection.StatusChanged += (_, _) => throw new InvalidOperationException("display failed");
        var other = _harness.Store.Add("Exit B", "Oslo", ExitKind.WireGuard, NetworkHarness.Configuration);

        await _harness.Store.SelectAsync(other.Id);

        Assert.True(_harness.Connector.Tunnels[0].Disposed);
        Assert.Equal(other, _harness.Store.FindSelected());
        Assert.Equal(ExitStatus.NotConnected, _harness.Events[^1]);
    }

    [Fact]
    public async Task Changes_made_while_a_handler_runs_are_raised_after_it_in_order()
    {
        var exit = await _harness.SelectedExitAsync();
        var inHandler = new TaskCompletionSource();
        using var releaseHandler = new ManualResetEventSlim();
        var raised = new List<ExitStatus>();
        _harness.Connection.StatusChanged += (_, status) =>
        {
            lock (raised)
            {
                raised.Add(status);
            }

            if (status.State == ExitState.Connected && !inHandler.Task.IsCompleted)
            {
                inHandler.SetResult();
                releaseHandler.Wait(TimeSpan.FromSeconds(10));
            }
        };

        // The handler holds the event for Connected while the tunnel drops on another thread.
        var connecting = Task.Run(() => _harness.Connection.ConnectAsync());
        await inHandler.Task;
        _harness.Connector.Tunnels[0].Drop();
        var failed = new ExitStatus(ExitState.Failed, exit.Id, ExitFailure.ConnectionLost);
        Assert.Equal(failed, _harness.Connection.Status);
        releaseHandler.Set();
        await connecting;

        await NetworkHarness.WaitUntilAsync(() =>
        {
            lock (raised)
            {
                return raised[^1] == failed;
            }
        });
        await Task.Delay(50);
        lock (raised)
        {
            Assert.Equal(
                [ExitState.Connecting, ExitState.Connected, ExitState.Failed],
                raised.Select(status => status.State));
        }
    }

    [Fact]
    public async Task A_cancellation_just_after_the_exit_is_published_leaves_it_connected_and_usable()
    {
        await _harness.SelectedExitAsync();
        using var cancel = new CancellationTokenSource();
        _harness.Connection.StatusChanged += (_, status) =>
        {
            if (status.State == ExitState.Connected)
            {
                cancel.Cancel();
            }
        };

        var status = await _harness.Connection.ConnectAsync(cancel.Token);

        Assert.Equal(ExitState.Connected, status.State);
        Assert.True(_harness.Connection.TryLease(out _));
        Assert.False(_harness.Connector.Tunnels[0].Disposed);
        await _harness.Http.SendAsync(ExitRequest.Get(new Uri("https://chain.example/")));
    }

    [Fact]
    public async Task A_proxy_written_as_an_ipv4_mapped_address_is_used_as_ipv4()
    {
        _harness.Connector.Proxy = new IPEndPoint(IPAddress.Parse("::ffff:127.0.0.1"), 49_300);

        await _harness.ConnectedExitAsync();

        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 49_300), Assert.Single(_harness.Handlers.Proxies));
    }

    [Fact]
    public async Task Removing_another_exit_does_not_cancel_an_attempt_to_connect_the_selected_one()
    {
        await _harness.SelectedExitAsync("Exit A");
        var other = _harness.Store.Add("Exit B", "Oslo", ExitKind.WireGuard, NetworkHarness.Configuration);
        _harness.Connector.Gate = new TaskCompletionSource();

        var connecting = _harness.Connection.ConnectAsync();
        var removing = _harness.Store.RemoveAsync(other.Id);
        _harness.Connector.Gate.SetResult();

        Assert.Equal(ExitState.Connected, (await connecting).State);
        Assert.True(await removing);
    }

    [Fact]
    public async Task Disposing_waits_for_a_tunnel_that_is_stopping_after_it_fell()
    {
        await _harness.ConnectedExitAsync();
        var tunnel = _harness.Connector.Tunnels[0];
        var slowStop = new TaskCompletionSource();
        tunnel.StopGate = slowStop.Task;
        tunnel.Drop();
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            slowStop.SetResult();
        });

        _harness.Connection.Dispose();

        Assert.True(tunnel.Disposed);
    }

    [Fact]
    public async Task Disposing_waits_for_a_disconnect_that_is_stopping_its_tunnel()
    {
        await _harness.ConnectedExitAsync();
        var tunnel = _harness.Connector.Tunnels[0];
        var slowStop = new TaskCompletionSource();
        tunnel.StopGate = slowStop.Task;
        var disconnecting = _harness.Connection.DisconnectAsync();
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            slowStop.SetResult();
        });

        _harness.Connection.Dispose();

        Assert.True(tunnel.Disposed);
        await disconnecting;
    }

    [Fact]
    public void A_watch_that_comes_after_the_exit_stopped_is_not_registered()
    {
        var tunnel = new FakeTunnel(FakeConnector.LoopbackProxy, new NetworkCredential("u", "p"));
        var active = new ActiveExit(Guid.NewGuid(), tunnel, new HttpMessageInvoker(new RecordingHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage()))));
        var calls = 0;

        active.Stop();
        active.Watch(tunnel.Stopped, () => calls++);
        tunnel.Drop();

        Assert.Equal(0, calls);
    }

    [Fact]
    public void A_watch_on_a_tunnel_that_has_already_stopped_runs_at_once()
    {
        var tunnel = new FakeTunnel(FakeConnector.LoopbackProxy, new NetworkCredential("u", "p"));
        var active = new ActiveExit(Guid.NewGuid(), tunnel, new HttpMessageInvoker(new RecordingHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage()))));
        tunnel.Drop();
        var calls = 0;

        active.Watch(tunnel.Stopped, () =>
        {
            calls++;
            active.Stop();
        });

        Assert.Equal(1, calls);
    }

    /// <summary>A connector that hands over its tunnel only when told to, whatever its token says.</summary>
    private sealed class HandOverAfterConnector(Task release) : IExitConnector
    {
        public FakeTunnel Tunnel { get; } =
            new(FakeConnector.LoopbackProxy, new NetworkCredential("tunnel-user", "tunnel-password-1"));

        public async Task<IExitTunnel> ConnectAsync(
            ExitInfo exit, string configuration, CancellationToken cancellationToken)
        {
            await release;
            return Tunnel;
        }
    }
}
