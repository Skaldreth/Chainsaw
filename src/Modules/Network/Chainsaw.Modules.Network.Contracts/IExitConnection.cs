namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// The program's connection to its selected exit. Requests through <see cref="IExitHttp"/> are only sent while the
/// state is <see cref="ExitState.Connected"/>.
/// </summary>
public interface IExitConnection
{
    /// <summary>Gets the current status.</summary>
    ExitStatus Status { get; }

    /// <summary>
    /// Raised after <see cref="Status"/> has changed, on any thread, one at a time and in order. When changes come
    /// quickly an intermediate status may be skipped, but the latest is always raised. A handler that throws does not
    /// affect the connection. A handler must not wait synchronously for <see cref="ConnectAsync"/> or
    /// <see cref="DisconnectAsync"/>, since the event can be raised while one of them runs.
    /// </summary>
    event EventHandler<ExitStatus>? StatusChanged;

    /// <summary>
    /// Connects the selected exit, disconnecting any exit already connected. A failure to connect is not thrown but
    /// ends in <see cref="ExitState.Failed"/> with its reason.
    /// </summary>
    /// <param name="cancellationToken">Stops the attempt; the state then returns to not connected.</param>
    /// <returns>The status when the attempt ended.</returns>
    /// <exception cref="InvalidOperationException">No exit is selected.</exception>
    /// <exception cref="OperationCanceledException">
    /// The attempt was cancelled, by the token or by a disconnect or a change of the selected exit meanwhile.
    /// </exception>
    Task<ExitStatus> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects the exit, cancelling an attempt to connect that is running. Requests in flight are stopped and
    /// fail. Disconnecting when nothing is connected is harmless.
    /// </summary>
    /// <returns>A task that completes when the tunnel is stopped.</returns>
    Task DisconnectAsync();
}
