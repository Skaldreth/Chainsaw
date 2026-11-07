using System.Net;

namespace Chainsaw.Modules.Network;

/// <summary>Builds the HTTP pipeline for a connected exit. The tests replace it to see what would be sent.</summary>
internal interface IExitHandlerFactory
{
    /// <summary>Builds a pipeline that sends every request through a SOCKS5 proxy and nowhere else.</summary>
    /// <param name="socks5Proxy">The tunnel's proxy on the loopback address.</param>
    /// <returns>The pipeline's handler, owned by the caller.</returns>
    HttpMessageHandler Create(IPEndPoint socks5Proxy);
}
