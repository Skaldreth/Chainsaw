namespace Chainsaw.Modules.Network.Contracts;

/// <summary>Why a request through the exit failed.</summary>
public enum ExitError
{
    /// <summary>No exit is connected, so nothing was sent.</summary>
    NotConnected,

    /// <summary>The response did not arrive within the time limit.</summary>
    Timeout,

    /// <summary>The response was larger than the size limit.</summary>
    ResponseTooLarge,

    /// <summary>The request failed on the way (the exit, the proxy or the server could not be reached).</summary>
    RequestFailed,

    /// <summary>The exit was disconnected while the request was in flight.</summary>
    ConnectionLost,

    /// <summary>
    /// A request to the user's own node was refused: its address is not on the computer or a private network, so it
    /// would have left the computer directly. Nothing was sent.
    /// </summary>
    NotLocal,

    /// <summary>
    /// Windows is the way out and the computer does not reach the internet (<see cref="INetworkPresence"/>), so nothing
    /// was sent.
    /// </summary>
    Offline,

    /// <summary>
    /// Windows is the way out and its proxy asked for a login (<c>407</c>) that Chainsaw does not send or that the
    /// proxy did not accept (sketch PX1a, <see cref="IWindowsProxy"/>). The request did not reach the source.
    /// </summary>
    ProxyLoginRequired,

    /// <summary>
    /// The user's own node showed another certificate than the one pinned for it (sketch KL3b), so the connection was
    /// refused before anything was sent; the node is refused until the user trusts the new certificate.
    /// </summary>
    CertificateChanged,
}
