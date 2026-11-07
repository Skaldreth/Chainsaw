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
    /// Windows is the way out and the computer has no network (<see cref="INetworkPresence"/>), so nothing was sent.
    /// </summary>
    Offline,
}
