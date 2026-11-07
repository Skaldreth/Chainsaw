namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// Whether the computer reaches the internet: it has a network, and an <c>https</c> probe through the computer's own
/// proxy answered (not a ping, which an organisation's network often blocks). With <see cref="ExitRoute.Windows"/>
/// chosen, lookups can go only while it does, so the way out is shown green only then and <em>Offline</em> otherwise
/// (decided 2026-11-07); a Wi-Fi with a login page counts as offline. It says nothing of whether a given source answers.
/// </summary>
public interface INetworkPresence
{
    /// <summary>Gets whether the computer reaches the internet now.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Raised with the new value after <see cref="IsAvailable"/> has changed, on a thread of the network's choosing,
    /// never the UI thread as such. A handler that throws does not affect the others.
    /// </summary>
    event EventHandler<bool>? Changed;
}
