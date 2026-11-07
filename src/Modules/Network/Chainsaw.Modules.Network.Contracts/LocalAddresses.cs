using System.Net;
using System.Net.Sockets;

namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// Recognises addresses on the computer or on a private network. A lookup through the exit never needs them, and a
/// tunnel could reach them on its own side, so they are refused there before anything is sent; only a request to the
/// user's own node (<see cref="ILocalHttp"/>) may go to them, and it may go nowhere else.
/// </summary>
public static class LocalAddresses
{
    /// <summary>Tells whether an address names the computer itself or any address that is not global.</summary>
    /// <param name="uri">The absolute address.</param>
    /// <returns><see langword="true"/> when the host is local.</returns>
    public static bool IsLocal(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
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
    /// <exception cref="ArgumentNullException">The address is null.</exception>
    public static bool IsLocal(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return IsLocalV4(bytes);
        }

        if (EmbeddedIPv4(bytes) is { } embedded)
        {
            return IsLocalV4(embedded);
        }

        // 2001::/32 (Teredo, which hides an IPv4 address) and 2001:db8::/32 (documentation) are never a source.
        if (bytes is [0x20, 0x01, 0x00, 0x00, ..] or [0x20, 0x01, 0x0D, 0xB8, ..])
        {
            return true;
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

    /// <summary>
    /// Tells whether an address is in the user's own network: the computer itself (loopback), a private IPv4 network
    /// (10/8, 172.16/12, 192.168/16), link-local (169.254/16, <c>fe80::/10</c>) or an IPv6 unique local address
    /// (<c>fc00::/7</c>), also as an IPv4-mapped IPv6 address. This is the allow-list for the user's own node: unlike
    /// <see cref="IsLocal(IPAddress)"/>, which refuses everything that is not global, it leaves out what is not global
    /// but still reached over the internet (Teredo, 6to4, NAT64, the provider's shared 100.64/10, relays and reserved
    /// ranges).
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> when it is on the computer or a private network.</returns>
    /// <exception cref="ArgumentNullException">The address is null.</exception>
    public static bool IsOwnNetwork(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes switch
            {
                [127, ..] or [10, ..] => true,
                [172, >= 16 and <= 31, ..] or [192, 168, ..] or [169, 254, ..] => true,
                _ => false,
            };
        }

        return IPAddress.IPv6Loopback.Equals(address)
            || (bytes[0] & 0xFE) == 0xFC
            || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80);
    }

    /// <summary>
    /// Tells whether an address's host may be the user's own node: <c>localhost</c>, or an IP address in the user's own
    /// network (<see cref="IsOwnNetwork(IPAddress)"/>). A name is decided when it is resolved.
    /// </summary>
    /// <param name="uri">The absolute address.</param>
    /// <returns>
    /// <see langword="true"/> for <c>localhost</c> or an own-network IP address, <see langword="false"/> for any
    /// other IP address, and <see langword="null"/> for a name.
    /// </returns>
    /// <exception cref="ArgumentNullException">The address is null.</exception>
    public static bool? IsOwnNetwork(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var host = uri.IdnHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host.Trim('[', ']'), out var address) ? IsOwnNetwork(address) : null;
    }
}
