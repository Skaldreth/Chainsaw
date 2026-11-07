using System.Net;

namespace Chainsaw.Modules.Network;

/// <summary>Builds the HTTP pipeline for a connected exit. The tests replace it to see what would be sent.</summary>
internal interface IExitHandlerFactory
{
    /// <summary>Builds a pipeline that sends every request through a SOCKS5 proxy and nowhere else.</summary>
    /// <param name="socks5Proxy">The tunnel's proxy on the loopback address.</param>
    /// <param name="credentials">The user name and password the proxy demands.</param>
    /// <param name="proxyIsTunnel">
    /// Tells, after each connection to the proxy, whether the port still belongs to the tunnel; when it does not, the
    /// connection is closed before anything is sent (see <see cref="Contracts.IExitTunnel.IsRunning"/>).
    /// </param>
    /// <returns>The pipeline's handler, owned by the caller.</returns>
    HttpMessageHandler Create(IPEndPoint socks5Proxy, NetworkCredential credentials, Func<bool> proxyIsTunnel);
}
