using System.Net;

namespace Chainsaw.Modules.Network.Contracts;

/// <summary>A running tunnel to an exit. Disposing it stops the tunnel.</summary>
public interface IExitTunnel : IAsyncDisposable
{
    /// <summary>
    /// Gets the tunnel's SOCKS5 proxy. It must be on a loopback address; a tunnel with any other address is refused,
    /// so that the proxy itself is never reached over the computer's network.
    /// </summary>
    IPEndPoint Socks5Proxy { get; }

    /// <summary>Gets a token that is cancelled when the tunnel stops for any reason other than being disposed.</summary>
    CancellationToken Stopped { get; }
}
