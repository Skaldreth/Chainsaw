using System.Net;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.App.Exits;

/// <summary>
/// A quick check of a WireGuard file when it is chosen, so that the user sees at once whether it can be used (sketch
/// UT1a: <em>Giltig</em> beside the file's name). It only asks for the sections and keys the tunnel demands; the
/// tunnel's helper checks every value when it starts, and the exits' store checks the DNS server again when the file
/// is stored. The file holds a private key, so it is read as characters and no part of it ever becomes a string here.
/// </summary>
public static class WireGuardCheck
{
    /// <summary>The words shown beside a file that passed.</summary>
    public const string ValidText = "Giltig";

    /// <summary>Checks a WireGuard file's text.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>What the check found; <see cref="WireGuardVerdict.Valid"/> when nothing is missing.</returns>
    public static WireGuardVerdict Check(ReadOnlySpan<char> text)
    {
        if (text.IsWhiteSpace())
        {
            return WireGuardVerdict.Empty;
        }

        if (text.Length > ExitRules.MaximumConfigurationLength)
        {
            return WireGuardVerdict.TooLarge;
        }

        var found = new Found();
        var section = Section.None;
        foreach (var raw in text.EnumerateLines())
        {
            var line = WithoutComment(raw).Trim();
            if (line.StartsWith("["))
            {
                section = Enter(line, ref found);
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals > 0)
            {
                Note(section, line[..equals].Trim(), line[(equals + 1)..].Trim(), ref found);
            }
        }

        return Verdict(found);
    }

    /// <summary>Gives the short Swedish words for a verdict, shown beside the file's name.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <returns>The words.</returns>
    public static string TextFor(WireGuardVerdict verdict) => verdict switch
    {
        WireGuardVerdict.Valid => ValidText,
        WireGuardVerdict.Empty => "Filen är tom",
        WireGuardVerdict.TooLarge => "För stor för en WireGuard-fil",
        WireGuardVerdict.NotOneInterface => "Ska ha en [Interface]",
        WireGuardVerdict.NotOnePeer => "Ska ha en [Peer]",
        WireGuardVerdict.NoPrivateKey => "Saknar PrivateKey",
        WireGuardVerdict.NoAddress => "Saknar Address",
        WireGuardVerdict.NoDnsServer => "Saknar DNS = med en IP-adress",
        WireGuardVerdict.NoPublicKey => "Saknar PublicKey",
        WireGuardVerdict.NoEndpoint => "Saknar Endpoint",
        WireGuardVerdict.NoAllowedIps => "Saknar AllowedIPs",
        _ => "Godtas inte",
    };

    /// <summary>Cuts a line at its comment, as wg-quick reads it.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The part before <c>#</c>.</returns>
    private static ReadOnlySpan<char> WithoutComment(ReadOnlySpan<char> line)
    {
        var hash = line.IndexOf('#');
        return hash < 0 ? line : line[..hash];
    }

    /// <summary>Enters a section and counts it.</summary>
    /// <param name="line">The section's line, such as <c>[Peer]</c>.</param>
    /// <param name="found">What has been found so far.</param>
    /// <returns>The section entered; <see cref="Section.Other"/> for one the tunnel does not know.</returns>
    private static Section Enter(ReadOnlySpan<char> line, ref Found found)
    {
        var name = line.Trim("[] \t");
        if (name.Equals("Interface", StringComparison.OrdinalIgnoreCase))
        {
            found.Interfaces++;
            return Section.Interface;
        }

        if (name.Equals("Peer", StringComparison.OrdinalIgnoreCase))
        {
            found.Peers++;
            return Section.Peer;
        }

        return Section.Other;
    }

    /// <summary>Notes a key with a value. Only the DNS value is looked at, since it must be an IP address.</summary>
    /// <param name="section">The section the key is in.</param>
    /// <param name="key">The key.</param>
    /// <param name="value">The value; a key's value is never copied.</param>
    /// <param name="found">What has been found so far.</param>
    private static void Note(Section section, ReadOnlySpan<char> key, ReadOnlySpan<char> value, ref Found found)
    {
        if (value.IsEmpty)
        {
            return;
        }

        if (section == Section.Interface)
        {
            found.PrivateKey |= Is(key, "PrivateKey");
            found.Address |= Is(key, "Address");
            found.DnsServer |= Is(key, "DNS") && NamesAddress(value);
        }
        else if (section == Section.Peer)
        {
            found.PublicKey |= Is(key, "PublicKey");
            found.Endpoint |= Is(key, "Endpoint");
            found.AllowedIps |= Is(key, "AllowedIPs");
        }
    }

    /// <summary>Tells whether a key is the one asked for, as wg-quick compares them (without regard to case).</summary>
    /// <param name="key">The key in the file.</param>
    /// <param name="name">The key asked for.</param>
    /// <returns><see langword="true"/> when they are the same.</returns>
    private static bool Is(ReadOnlySpan<char> key, string name) =>
        key.Equals(name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Tells whether a DNS value names a server by its address; a name is a search domain in wg-quick.</summary>
    /// <param name="value">The value, a list separated by commas.</param>
    /// <returns><see langword="true"/> when an item is an address.</returns>
    private static bool NamesAddress(ReadOnlySpan<char> value)
    {
        foreach (var range in value.Split(','))
        {
            var item = value[range].Trim();
            if (IPAddress.TryParse(item, out var address) && IsPlain(address, item))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tells whether an address is written in the form the helper accepts: IPv4 as four plain decimal numbers (not the
    /// old short, octal or hex forms such as <c>10</c> or <c>0x0a.0.0.1</c>), or IPv6. The same rule as the exits'
    /// store, which refuses the file otherwise.
    /// </summary>
    /// <param name="address">The parsed address.</param>
    /// <param name="item">The text it was parsed from (a DNS server's address, not a secret).</param>
    /// <returns><see langword="true"/> when the form is accepted.</returns>
    private static bool IsPlain(IPAddress address, ReadOnlySpan<char> item) =>
        address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
        || item.SequenceEqual(address.ToString());

    /// <summary>Gives the verdict for what was found, in the order the tunnel's helper checks.</summary>
    /// <param name="found">What was found.</param>
    /// <returns>The verdict.</returns>
    private static WireGuardVerdict Verdict(Found found) => found switch
    {
        { Interfaces: not 1 } => WireGuardVerdict.NotOneInterface,
        { Peers: not 1 } => WireGuardVerdict.NotOnePeer,
        { PrivateKey: false } => WireGuardVerdict.NoPrivateKey,
        { Address: false } => WireGuardVerdict.NoAddress,
        { DnsServer: false } => WireGuardVerdict.NoDnsServer,
        { PublicKey: false } => WireGuardVerdict.NoPublicKey,
        { Endpoint: false } => WireGuardVerdict.NoEndpoint,
        { AllowedIps: false } => WireGuardVerdict.NoAllowedIps,
        _ => WireGuardVerdict.Valid,
    };

    /// <summary>The section a line is in.</summary>
    private enum Section
    {
        /// <summary>Before the first section.</summary>
        None,

        /// <summary><c>[Interface]</c>.</summary>
        Interface,

        /// <summary><c>[Peer]</c>.</summary>
        Peer,

        /// <summary>A section the tunnel does not know.</summary>
        Other,
    }

    /// <summary>What has been found in the file: how many sections, and which keys.</summary>
    private struct Found
    {
        /// <summary>The number of <c>[Interface]</c> sections.</summary>
        public int Interfaces;

        /// <summary>The number of <c>[Peer]</c> sections.</summary>
        public int Peers;

        /// <summary>Whether <c>[Interface]</c> has a <c>PrivateKey</c>.</summary>
        public bool PrivateKey;

        /// <summary>Whether <c>[Interface]</c> has an <c>Address</c>.</summary>
        public bool Address;

        /// <summary>Whether <c>[Interface]</c> names a DNS server by its IP address.</summary>
        public bool DnsServer;

        /// <summary>Whether <c>[Peer]</c> has a <c>PublicKey</c>.</summary>
        public bool PublicKey;

        /// <summary>Whether <c>[Peer]</c> has an <c>Endpoint</c>.</summary>
        public bool Endpoint;

        /// <summary>Whether <c>[Peer]</c> has <c>AllowedIPs</c>.</summary>
        public bool AllowedIps;
    }
}
