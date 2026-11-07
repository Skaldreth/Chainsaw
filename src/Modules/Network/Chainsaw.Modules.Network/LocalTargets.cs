using System.Net;
using System.Net.Sockets;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Recognises addresses on the computer or on a private network. A lookup never needs them, and a tunnel could reach
/// them on its own side, so they are refused before anything is sent.
/// </summary>
internal static class LocalTargets
{
    /// <summary>Tells whether an address names the computer itself or any address that is not global.</summary>
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

    /// <summary>
    /// Tells whether an IP address is anything but a global unicast address: loopback, unspecified, private, shared,
    /// link-local, multicast, reserved, or an IPv6 address that carries such an IPv4 address inside it.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> when the address is not global.</returns>
    private static bool IsLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return IsLocalV4(bytes);
        }

        if (EmbeddedIPv4(bytes) is { } embedded)
        {
            return IsLocalV4(embedded);
        }

        // Only 2000::/3 is global unicast; everything else (fe80::/10, fc00::/7, fec0::/10, ff00::/8 and the rest)
        // is refused. The forms that carry IPv4 (also :: and ::1, as 0.0.0.0 and 0.0.0.1) were judged by it above.
        return (bytes[0] & 0xE0) != 0x20;
    }

    /// <summary>
    /// Checks the IPv4 ranges that are not global: 0/8, 10/8, 100.64/10, 127/8, 169.254/16, 172.16/12, 192.0.0/24,
    /// 192.0.2/24, 192.88.99/24, 192.168/16, 198.18/15, 198.51.100/24, 203.0.113/24 and everything from 224/4 up
    /// (multicast, reserved and broadcast).
    /// </summary>
    /// <param name="bytes">The four bytes.</param>
    /// <returns><see langword="true"/> when the address is in one of them.</returns>
    private static bool IsLocalV4(byte[] bytes) => bytes switch
    {
        [0 or 10 or 127, ..] => true,
        [100, >= 64 and <= 127, ..] => true,
        [169, 254, ..] => true,
        [172, >= 16 and <= 31, ..] => true,
        [192, 0, 0 or 2, _] or [192, 88, 99, _] or [192, 168, ..] => true,
        [198, 18 or 19, ..] or [198, 51, 100, _] or [203, 0, 113, _] => true,
        [>= 224, ..] => true,
        _ => false,
    };

    /// <summary>
    /// Gives the IPv4 address inside an IPv6 address that carries one: IPv4-mapped (<c>::ffff:0:0/96</c>),
    /// IPv4-compatible (<c>::/96</c>), translated (<c>::ffff:0:0:0/96</c>), NAT64 (<c>64:ff9b::/96</c>) and 6to4
    /// (<c>2002::/16</c>).
    /// </summary>
    /// <param name="bytes">The sixteen bytes.</param>
    /// <returns>The four bytes of the IPv4 address, or <see langword="null"/> when there is none.</returns>
    private static byte[]? EmbeddedIPv4(byte[] bytes)
    {
        if (bytes[0] == 0x20 && bytes[1] == 0x02)
        {
            return bytes[2..6];
        }

        ReadOnlySpan<byte> nat64 = [0x00, 0x64, 0xFF, 0x9B, 0, 0, 0, 0, 0, 0, 0, 0];
        var prefix = bytes.AsSpan(0, 12);
        var zeroesTo = prefix[..10].IndexOfAnyExcept((byte)0);
        var carriesIPv4 = prefix.SequenceEqual(nat64)
            || (zeroesTo == -1 && prefix[10] is 0 or 0xFF && prefix[11] == prefix[10])
            || (prefix[..8].IndexOfAnyExcept((byte)0) == -1 && prefix[8] == 0xFF && prefix[9] == 0xFF
                && prefix[10] == 0 && prefix[11] == 0);
        return carriesIPv4 ? bytes[12..16] : null;
    }
}
