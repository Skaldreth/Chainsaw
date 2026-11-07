namespace Chainsaw.Modules.Network;

/// <summary>
/// What Windows says about the computer's own network: whether it has one, and when that or its addresses change. It is
/// the cheap, local half of <see cref="NetworkPresence"/>; whether the internet actually answers is the probe's half.
/// The tests replace it to turn the network on and off.
/// </summary>
internal interface INetworkSignals
{
    /// <summary>Gets whether any interface other than loopback and tunnels is up.</summary>
    bool HasNetwork { get; }

    /// <summary>
    /// Raised when the network comes or goes, or when an address changes (a new Wi-Fi, a cable, a VPN of the whole
    /// computer), on a thread of the network's choosing. <see cref="HasNetwork"/> is read again after it.
    /// </summary>
    event EventHandler? Changed;
}
