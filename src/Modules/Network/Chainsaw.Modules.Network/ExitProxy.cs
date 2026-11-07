using System.Net;

namespace Chainsaw.Modules.Network;

/// <summary>
/// The proxy setting of an exit's pipeline: every address goes to the tunnel's SOCKS5 proxy, none is bypassed, not
/// even the loopback address or a local name. SOCKS5 sends the host name to the proxy, so the name is resolved at the
/// exit and never by the computer's own DNS.
/// </summary>
/// <param name="socks5Proxy">The tunnel's proxy.</param>
internal sealed class ExitProxy(IPEndPoint socks5Proxy) : IWebProxy
{
    /// <summary>The proxy's address as a URI, built once.</summary>
    private readonly Uri _address = new($"socks5://{socks5Proxy}");

    /// <summary>Gets or sets the proxy's credentials; always none, the tunnel's proxy needs none.</summary>
    public ICredentials? Credentials
    {
        get => null;
        set => throw new NotSupportedException("The exit's proxy takes no credentials.");
    }

    /// <summary>Gives the proxy for an address: always the tunnel's.</summary>
    /// <param name="destination">The address asked for.</param>
    /// <returns>The tunnel's SOCKS5 proxy.</returns>
    public Uri GetProxy(Uri destination) => _address;

    /// <summary>Tells whether an address goes around the proxy: never.</summary>
    /// <param name="host">The address asked for.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public bool IsBypassed(Uri host) => false;
}
