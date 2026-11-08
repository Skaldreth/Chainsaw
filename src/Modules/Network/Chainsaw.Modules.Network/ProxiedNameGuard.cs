using System.Net;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Refuses, on the way out through Windows, a request that the system's proxy would carry to a name or an address that
/// points into the computer or a private network. Without a proxy the pipeline resolves the name itself and refuses such
/// addresses (<see cref="WindowsHandlerFactory.ConnectAsync"/>); through a proxy the proxy resolves it, so the name is
/// resolved here first with the computer's resolver, as Windows would, and the request is refused when any address it
/// gives is local, since the proxy may pick that one (a source on the internet never has one). A name the computer
/// cannot resolve is left to the proxy, which may know more.
/// </summary>
/// <param name="inner">The pipeline that sends the request.</param>
/// <param name="proxyFor">Gives the proxy the system uses for an address, or <see langword="null"/> for none.</param>
/// <param name="resolve">Resolves a name with the computer's resolver.</param>
internal sealed class ProxiedNameGuard(
    HttpMessageHandler inner,
    Func<Uri, Uri?> proxyFor,
    Func<string, CancellationToken, Task<IPAddress[]>> resolve) : DelegatingHandler(inner)
{
    /// <summary>Gives the system's proxy for an address, as <see cref="SocketsHttpHandler"/> will use it.</summary>
    /// <param name="address">The address asked for.</param>
    /// <returns>The proxy, or <see langword="null"/> when the request goes straight to the source.</returns>
    public static Uri? SystemProxyFor(Uri address)
    {
        var proxy = HttpClient.DefaultProxy;
        return proxy.IsBypassed(address) ? null : proxy.GetProxy(address);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestUri is { } address && proxyFor(address) is not null)
        {
            await RequireGlobalAsync(address.IdnHost.Trim('[', ']'), cancellationToken).ConfigureAwait(false);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Refuses an address, or a name that has a local address on this computer.</summary>
    /// <param name="host">The name or the address.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <exception cref="HttpRequestException">The name points into the computer or a private network.</exception>
    private async Task RequireGlobalAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            Refuse(literal);
            return;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await resolve(host, cancellationToken).ConfigureAwait(false);
        }
        catch (System.Net.Sockets.SocketException)
        {
            // Not known here; the proxy may resolve it on a network the computer does not see.
            return;
        }

        foreach (var item in addresses)
        {
            Refuse(item);
        }
    }

    /// <summary>Refuses an address on the computer or a private network.</summary>
    /// <param name="address">The address.</param>
    /// <exception cref="HttpRequestException">The address is local.</exception>
    private static void Refuse(IPAddress address)
    {
        if (LocalAddresses.IsLocal(address))
        {
            throw new HttpRequestException("The source points to the computer or a private network.");
        }
    }
}
