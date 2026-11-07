namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// Whether the computer has a network at all, as Windows tells it. With <see cref="ExitRoute.Windows"/> chosen,
/// lookups can go only while it has one, so the way out is shown green only then and <em>Offline</em> otherwise
/// (decided 2026-11-07). It says nothing of whether a given source answers.
/// </summary>
public interface INetworkPresence
{
    /// <summary>Gets whether the computer has a network now.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Raised with the new value after <see cref="IsAvailable"/> has changed, on a thread of the network's choosing,
    /// never the UI thread as such. A handler that throws does not affect the others.
    /// </summary>
    event EventHandler<bool>? Changed;
}
