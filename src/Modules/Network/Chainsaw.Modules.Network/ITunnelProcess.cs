namespace Chainsaw.Modules.Network;

/// <summary>
/// A running copy of Chainsaw's WireGuard helper (<c>tools/Chainsaw.Tunnel</c>), seen through its standard streams.
/// The tests replace it with a process in memory.
/// </summary>
internal interface ITunnelProcess : IDisposable
{
    /// <summary>
    /// Gets the helper's stdin. The first line gives it the configuration and the credentials; closing it tells the
    /// helper to stop.
    /// </summary>
    Stream Input { get; }

    /// <summary>Gets the helper's stdout, on which it answers with JSON lines.</summary>
    Stream Output { get; }

    /// <summary>Gets a task that completes when the helper has exited.</summary>
    Task Exited { get; }

    /// <summary>
    /// Gets whether the helper has exited, asked of the operating system now (not through <see cref="Exited"/>,
    /// which may complete a little later).
    /// </summary>
    bool HasExited { get; }

    /// <summary>Ends the helper at once. Does nothing when it has already exited.</summary>
    void Kill();
}
