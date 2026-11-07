using System.Net;

namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// A running tunnel to an exit. Disposing it stops the tunnel. The tunnel's proxy must resolve every name inside the
/// tunnel (with the DNS server of the exit's configuration), never with the computer's own resolver, and must refuse
/// to connect to an address on the computer or a private network, also when a name resolves to one.
/// </summary>
public interface IExitTunnel : IAsyncDisposable
{
    /// <summary>
    /// Gets the tunnel's SOCKS5 proxy. It must be on a loopback address; a tunnel with any other address is refused,
    /// so that the proxy itself is never reached over the computer's network.
    /// </summary>
    IPEndPoint Socks5Proxy { get; }

    /// <summary>
    /// Gets the user name and password the proxy demands, new and random for each tunnel, so that no other program on
    /// the computer can use the tunnel. A tunnel without them (or with an empty one) is refused.
    /// </summary>
    NetworkCredential Socks5Credentials { get; }

    /// <summary>
    /// Gets a token that is cancelled when the tunnel stops for any reason other than being disposed. Chainsaw reacts
    /// inside the cancellation and disposes the tunnel afterwards on another thread.
    /// </summary>
    CancellationToken Stopped { get; }

    /// <summary>
    /// Gets whether the tunnel still holds its proxy's port. It is read after each connection to the proxy: while it
    /// is <see langword="true"/> the port has been the tunnel's own all along (it is held exclusively), so the
    /// connection reached the tunnel and not a program that took the port after the tunnel ended. A tunnel that has
    /// stopped, or cannot say, gives <see langword="false"/>, and the connection is closed unused.
    /// </summary>
    bool IsRunning { get; }
}
