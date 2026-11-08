using System.Net;
using System.Net.Sockets;

namespace Chainsaw.Modules.Network.Contracts;

/// <summary>The limits on what an exit is described with.</summary>
public static class ExitRules
{
    /// <summary>The longest name of an exit.</summary>
    public const int MaximumNameLength = 100;

    /// <summary>The longest place of an exit.</summary>
    public const int MaximumPlaceLength = 100;

    /// <summary>The longest configuration of an exit, in characters (a WireGuard file is far shorter).</summary>
    public const int MaximumConfigurationLength = 64 * 1024;

    /// <summary>
    /// Tells whether one item of a WireGuard file's <c>DNS =</c> list names a server by its IP address, in the forms
    /// the tunnel's helper accepts: four plain decimal numbers for IPv4 (not the old short, octal or hex forms such as
    /// <c>10</c>, <c>010.0.0.1</c> or <c>0x0a.0.0.1</c>, which the helper ignores), or IPv6 without brackets. Any
    /// other item is a search domain in wg-quick and names no server. The one rule for the exits' store and the check
    /// shown when a file is chosen, so that the two never disagree.
    /// </summary>
    /// <param name="item">The item, already trimmed (a DNS server's address, not a secret).</param>
    /// <returns><see langword="true"/> when it is an address in an accepted form.</returns>
    public static bool IsDnsServerAddress(ReadOnlySpan<char> item)
    {
        // Brackets (and a port after them) are accepted by .NET but not by the helper.
        if (item.Contains('[') || !IPAddress.TryParse(item, out var address))
        {
            return false;
        }

        return address.AddressFamily != AddressFamily.InterNetwork || item.SequenceEqual(address.ToString());
    }
}
