using System.Globalization;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Reads Windows' proxy setting into the kind and the address https lookups go through, with one rule for the address
/// so that the setting, the user's choice and the proxy a request is sent to are always compared the same way:
/// <c>host:port</c> in lower case.
/// </summary>
internal static class WindowsProxyText
{
    /// <summary>The port Windows uses for a proxy written without one.</summary>
    private const int DefaultPort = 80;

    /// <summary>What splits the entries in Windows' list of proxies.</summary>
    private static readonly char[] EntrySeparators = [';', ' ', '\t'];

    /// <summary>Gives the setting from what Windows says.</summary>
    /// <param name="autoDetect">Whether automatic detection (WPAD) is on.</param>
    /// <param name="autoConfigUrl">The script's address (PAC), if any.</param>
    /// <param name="proxyList">The proxies set by hand, if any.</param>
    /// <returns>
    /// <see cref="WindowsProxyKind.Automatic"/> whenever Windows looks for the proxy itself; else
    /// <see cref="WindowsProxyKind.Manual"/> with the address when a proxy is set by hand for https; else none.
    /// </returns>
    public static WindowsProxySetting Setting(bool autoDetect, string? autoConfigUrl, string? proxyList)
    {
        if (autoDetect || !string.IsNullOrWhiteSpace(autoConfigUrl))
        {
            return new WindowsProxySetting(WindowsProxyKind.Automatic, null);
        }

        return HttpsProxyOf(proxyList) is { } address
            ? new WindowsProxySetting(WindowsProxyKind.Manual, address)
            : WindowsProxySetting.None;
    }

    /// <summary>
    /// Gives the proxy an https request goes through from Windows' list: the entry for <c>https=</c>, else the one
    /// entry without a scheme, which serves every scheme. A list with entries for other schemes only has none for
    /// https, and Windows then sends https straight.
    /// </summary>
    /// <param name="proxyList">The list, such as <c>proxy:8080</c> or <c>http=a:80;https=b:8443</c>.</param>
    /// <returns>The address as <c>host:port</c>, or <see langword="null"/>.</returns>
    public static string? HttpsProxyOf(string? proxyList)
    {
        if (string.IsNullOrWhiteSpace(proxyList))
        {
            return null;
        }

        string? any = null;
        foreach (var entry in proxyList.Split(EntrySeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = entry.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                any ??= AddressOf(entry);
            }
            else if (string.Equals(entry[..equals], "https", StringComparison.OrdinalIgnoreCase))
            {
                return AddressOf(entry[(equals + 1)..]);
            }
        }

        return any;
    }

    /// <summary>Gives a proxy's address as <c>host:port</c>, from what Windows wrote or from the proxy's URI.</summary>
    /// <param name="server">The server, such as <c>proxy.lan.corp:8080</c> or <c>http://proxy:8080</c>.</param>
    /// <returns>The address, or <see langword="null"/> when it cannot be read.</returns>
    public static string? AddressOf(string server)
    {
        var text = server.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var withScheme = text.Contains("://", StringComparison.Ordinal) ? text : "http://" + text;
        return Uri.TryCreate(withScheme, UriKind.Absolute, out var uri) ? AddressOf(uri) : null;
    }

    /// <summary>Gives a proxy's address as <c>host:port</c> from its URI.</summary>
    /// <param name="proxy">The proxy, as a request is sent to it.</param>
    /// <returns>The address, or <see langword="null"/> when the URI has no host.</returns>
    public static string? AddressOf(Uri proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        if (!proxy.IsAbsoluteUri || proxy.IdnHost.Length == 0)
        {
            return null;
        }

        var port = proxy.Port < 0 ? DefaultPort : proxy.Port;
        var host = proxy.HostNameType == UriHostNameType.IPv6 ? $"[{proxy.IdnHost.Trim('[', ']')}]" : proxy.IdnHost;
        return string.Create(CultureInfo.InvariantCulture, $"{host.ToLowerInvariant()}:{port}");
    }
}
