using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>A connected exit: its tunnel and the one HTTP pipeline that goes through the tunnel's proxy.</summary>
/// <param name="exitId">The exit.</param>
/// <param name="tunnel">The running tunnel.</param>
/// <param name="invoker">Sends requests through the tunnel's SOCKS5 proxy, and only there.</param>
internal sealed class ActiveExit(Guid exitId, IExitTunnel tunnel, HttpMessageInvoker invoker)
{
    /// <summary>Cancelled when the exit is disconnected, which stops every request in flight through it.</summary>
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>Guards <see cref="_stoppedRegistration"/> and <see cref="_stopped"/> between the two threads.</summary>
    private readonly Lock _watchGate = new();

    /// <summary>The callback on the tunnel's <see cref="IExitTunnel.Stopped"/>, removed when the exit stops.</summary>
    private CancellationTokenRegistration _stoppedRegistration;

    /// <summary>Whether <see cref="Stop"/> has run; a watch that comes later is not registered.</summary>
    private bool _stopped;

    /// <summary>Gets the exit.</summary>
    public Guid ExitId => exitId;

    /// <summary>Gets the pipeline through the tunnel's proxy.</summary>
    public HttpMessageInvoker Invoker => invoker;

    /// <summary>Gets a token that is cancelled when the exit stops.</summary>
    public CancellationToken Stopping => _stopping.Token;

    /// <summary>Watches the tunnel, so that a tunnel that stops on its own stops the exit.</summary>
    /// <param name="stopped">The tunnel's stop token, read before the exit was published.</param>
    /// <param name="onStopped">Called once if the tunnel stops; at once if it has already stopped.</param>
    public void Watch(CancellationToken stopped, Action onStopped)
    {
        // The lock is reentrant, so a callback that runs at once and calls Stop on this thread does not deadlock.
        lock (_watchGate)
        {
            if (!_stopped)
            {
                _stoppedRegistration =
                    stopped.UnsafeRegister(static state => ((Action)state!).Invoke(), onStopped);
            }
        }
    }

    /// <summary>
    /// Stops the exit in memory: requests in flight are cancelled and the pipeline is disposed, so no request can use
    /// it again. The tunnel is stopped separately (<see cref="StopTunnelAsync"/>).
    /// </summary>
    public void Stop()
    {
        // Unregister, not Dispose: Dispose waits for a running callback, and Stop is called from that callback.
        lock (_watchGate)
        {
            _stopped = true;
            _stoppedRegistration.Unregister();
        }

        try
        {
            _stopping.Cancel();
        }
        finally
        {
            // Always, so that no request can use the pipeline again even if a cancellation callback failed.
            invoker.Dispose();
        }
    }

    /// <summary>
    /// Stops the tunnel. A tunnel that fails to stop is left to itself: the pipeline through it is already disposed,
    /// so nothing of Chainsaw's can use it.
    /// </summary>
    /// <returns>A task that completes when the tunnel has stopped or failed to.</returns>
    public Task StopTunnelAsync() => StopQuietlyAsync(tunnel);

    /// <summary>Stops a tunnel and ignores how it fails to stop (see <see cref="StopTunnelAsync"/>).</summary>
    /// <param name="tunnel">The tunnel.</param>
    /// <returns>A task that completes when the tunnel has stopped or failed to.</returns>
    public static async Task StopQuietlyAsync(IExitTunnel tunnel)
    {
        try
        {
            await tunnel.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The tunnel is a plug-in; how it fails to stop must not stop the caller.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Nothing more to do; see the summary.
        }
    }
}
