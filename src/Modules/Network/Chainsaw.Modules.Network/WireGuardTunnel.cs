using System.Net;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>
/// A running tunnel in Chainsaw's WireGuard helper. The helper's proxy is on 127.0.0.1 and demands this tunnel's own
/// user name and password. When the helper reports that the tunnel fell, closes its stdout or exits, the tunnel counts
/// as stopped; disposing it closes the helper's stdin, which makes the helper stop, and ends it if it does not.
/// </summary>
internal sealed class WireGuardTunnel : IExitTunnel
{
    /// <summary>How long disposal waits for the helper to stop on its own before it is ended.</summary>
    internal static readonly TimeSpan StopWait = TimeSpan.FromSeconds(3);

    /// <summary>The helper.</summary>
    private readonly ITunnelProcess _process;

    /// <summary>The helper's stdout, after its first line.</summary>
    private readonly TunnelOutput _output;

    /// <summary>Cancelled when the tunnel stops on its own.</summary>
    private readonly CancellationTokenSource _stopped = new();

    /// <summary>Guards <see cref="_disposed"/>.</summary>
    private readonly Lock _gate = new();

    /// <summary>Watches the helper until it stops.</summary>
    private readonly Task _watch;

    /// <summary>Whether disposal has begun; a stop after it is not reported.</summary>
    private bool _disposed;

    /// <summary>Wraps a helper that has reported its proxy's port, and starts watching it.</summary>
    /// <param name="process">The helper.</param>
    /// <param name="output">The helper's stdout, after its first line.</param>
    /// <param name="port">The proxy's port on 127.0.0.1.</param>
    /// <param name="credentials">The proxy's user name and password.</param>
    public WireGuardTunnel(ITunnelProcess process, TunnelOutput output, int port, NetworkCredential credentials)
    {
        _process = process;
        _output = output;
        Socks5Proxy = new IPEndPoint(IPAddress.Loopback, port);
        Socks5Credentials = credentials;
        _watch = WatchAsync();
    }

    /// <inheritdoc />
    public IPEndPoint Socks5Proxy { get; }

    /// <inheritdoc />
    public NetworkCredential Socks5Credentials { get; }

    /// <inheritdoc />
    public CancellationToken Stopped => _stopped.Token;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        try
        {
            // Closed stdin is the helper's signal to stop.
            await _process.Input.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The helper has already gone; it is ended below in any case.
        }

        await Task.WhenAny(_process.Exited, Task.Delay(StopWait)).ConfigureAwait(false);
        if (!_process.Exited.IsCompleted)
        {
            _process.Kill();
        }

        await Task.WhenAny(_watch, Task.Delay(StopWait)).ConfigureAwait(false);
        _process.Dispose();
    }

    /// <summary>
    /// Waits for the helper to stop: a later line on stdout (the helper only writes one when the tunnel fell), the end
    /// of stdout or the helper's exit. Then the tunnel is stopped, unless it is being disposed.
    /// </summary>
    /// <returns>A task that completes when the helper has stopped.</returns>
    private async Task WatchAsync()
    {
        await Task.WhenAny(ReadNextLineAsync(), _process.Exited).ConfigureAwait(false);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        // Outside the lock: Chainsaw reacts inside the cancellation (see IExitTunnel.Stopped).
        _stopped.Cancel();
    }

    /// <summary>Waits for the helper's next line or the end of its stdout.</summary>
    /// <returns>A task that completes when either comes, or when the pipe breaks.</returns>
    private async Task ReadNextLineAsync()
    {
        try
        {
            await _output.ReadLineAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ObjectDisposedException)
        {
            // A broken pipe is a stopped helper too.
        }
    }
}
