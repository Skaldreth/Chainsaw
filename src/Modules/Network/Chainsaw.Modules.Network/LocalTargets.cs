using System.Net;
using System.Net.Sockets;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Recognises addresses on the computer or on a private network. A lookup never needs them, and a tunnel could reach
/// them on its own side, so they are refused before anything is sent.
/// </summary>
internal static class LocalTargets
{
    /// <summary>Tells whether an address names the computer itself or a private, link-local or shared network.</summary>
    /// <param name="uri">The absolute address.</param>
    /// <returns><see langword="true"/> when the host is local.</returns>
    public static bool IsLocal(Uri uri)
    {
        var host = uri.IdnHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host.Trim('[', ']'), out var address) && IsLocal(address);
    }

    /// <summary>Tells whether an IP address is loopback, unspecified, private, link-local or shared.</summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> when the address is local.</returns>
    private static bool IsLocal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork ? IsLocalV4(bytes) : IsLocalV6(address, bytes);
    }

    /// <summary>Checks the IPv4 ranges: 0/8, 10/8, 100.64/10, 169.254/16, 172.16/12 and 192.168/16.</summary>
    /// <param name="bytes">The four bytes.</param>
    /// <returns><see langword="true"/> when the address is in one of them.</returns>
    private static bool IsLocalV4(byte[] bytes) => bytes switch
    {
        [0, ..] or [10, ..] => true,
        [100, >= 64 and <= 127, ..] => true,
        [169, 254, ..] => true,
        [172, >= 16 and <= 31, ..] => true,
        [192, 168, ..] => true,
        _ => false,
    };

    /// <summary>Checks the IPv6 ranges: unspecified, link-local (fe80::/10) and unique local (fc00::/7).</summary>
    /// <param name="address">The address.</param>
    /// <param name="bytes">Its sixteen bytes.</param>
    /// <returns><see langword="true"/> when the address is in one of them.</returns>
    private static bool IsLocalV6(IPAddress address, byte[] bytes) =>
        address.Equals(IPAddress.IPv6None) || address.IsIPv6LinkLocal || (bytes[0] & 0xFE) == 0xFC;
}
