using System.Net;
using System.Text;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.App.Exits;

/// <summary>
/// A quick check of a WireGuard file when it is chosen, so that the user sees at once whether it can be used (sketch
/// UT1a: <em>Giltig</em> beside the file's name). It reads the file as the tunnel's helper does
/// (<c>tools/Chainsaw.Tunnel/config.go</c>): lines split at <c>\n</c>, comments after <c>#</c>, the known sections and
/// keys, and the sections and keys every tunnel needs. The values (keys, addresses, ports) are left to the helper,
/// which checks them when it starts, except the DNS server, which the exits' store also demands. The file holds a
/// private key, so it is read as characters and no part of it ever becomes a string here.
/// </summary>
public static class WireGuardCheck
{
    /// <summary>The words shown beside a file that passed.</summary>
    public const string ValidText = "Giltig";

    /// <summary>The longest line the helper reads, in UTF-8 bytes (its scanner's buffer), not counted inclusive.</summary>
    private const int MaximumLineBytes = 64 * 1024;

    /// <summary>The keys of <c>[Interface]</c> the helper reads, as <c>config.go</c> names them.</summary>
    private static readonly string[] InterfaceKeys = ["PrivateKey", "Address", "DNS", "MTU"];

    /// <summary>The keys of <c>[Interface]</c> the helper accepts and ignores (they only concern wg-quick).</summary>
    private static readonly string[] IgnoredInterfaceKeys =
        ["ListenPort", "FwMark", "Table", "SaveConfig", "PreUp", "PostUp", "PreDown", "PostDown"];

    /// <summary>The keys of <c>[Peer]</c> the helper reads.</summary>
    private static readonly string[] PeerKeys =
        ["PublicKey", "PresharedKey", "Endpoint", "AllowedIPs", "PersistentKeepalive"];

    /// <summary>The keys that may be given more than once in their section.</summary>
    private static readonly string[] RepeatableKeys = ["Address", "DNS", "AllowedIPs"];

    /// <summary>Checks a WireGuard file's text.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>What the check found; <see cref="WireGuardVerdict.Valid"/> when nothing is wrong.</returns>
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

        var reader = new Reader();
        var rest = text;
        while (!rest.IsEmpty)
        {
            var end = rest.IndexOf('\n');
            var line = end < 0 ? rest : rest[..end];
            rest = end < 0 ? [] : rest[(end + 1)..];
            if (Encoding.UTF8.GetByteCount(line) >= MaximumLineBytes)
            {
                return WireGuardVerdict.TooLarge;
            }

            if (reader.Read(line) is { } refused)
            {
                return refused;
            }
        }

        return reader.Verdict();
    }

    /// <summary>Gives the short Swedish words for a verdict, shown beside the file's name.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <returns>The words.</returns>
    public static string TextFor(WireGuardVerdict verdict) => verdict switch
    {
        WireGuardVerdict.Valid => ValidText,
        WireGuardVerdict.Empty => "Filen är tom",
        WireGuardVerdict.TooLarge => "För stor för en WireGuard-fil",
        WireGuardVerdict.BrokenHeader => "En rubrik saknar ]",
        WireGuardVerdict.UnknownSection => "Okänd rubrik",
        WireGuardVerdict.LineWithoutEquals => "En rad saknar =",
        WireGuardVerdict.KeyBeforeSection => "En nyckel före [Interface]",
        WireGuardVerdict.UnknownKey => "Okänd nyckel",
        WireGuardVerdict.RepeatedKey => "En nyckel står två gånger",
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

    /// <summary>Finds a key in a list, without regard to case, as wg-quick compares them.</summary>
    /// <param name="keys">The list.</param>
    /// <param name="key">The key in the file.</param>
    /// <returns>The key's place in the list, or -1.</returns>
    private static int IndexOf(string[] keys, ReadOnlySpan<char> key)
    {
        for (var i = 0; i < keys.Length; i++)
        {
            if (key.Equals(keys[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Tells whether a key is the one asked for, without regard to case.</summary>
    /// <param name="key">The key in the file.</param>
    /// <param name="name">The key asked for.</param>
    /// <returns><see langword="true"/> when they are the same.</returns>
    private static bool Is(ReadOnlySpan<char> key, string name) =>
        key.Equals(name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Tells whether a DNS value names a server by its address (a name is a search domain).</summary>
    /// <param name="value">The value, a list separated by commas.</param>
    /// <returns><see langword="true"/> when an item is an address.</returns>
    private static bool NamesAddress(ReadOnlySpan<char> value)
    {
        foreach (var range in value.Split(','))
        {
            // .NET reads a bracketed IPv6 address, which the helper and the store refuse.
            var item = value[range].Trim();
            if (!item.Contains('[') && IPAddress.TryParse(item, out var address) && IsPlain(address, item))
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

    /// <summary>The section a line is in.</summary>
    private enum Section
    {
        /// <summary>Before the first section.</summary>
        None,

        /// <summary><c>[Interface]</c>.</summary>
        Interface,

        /// <summary><c>[Peer]</c>.</summary>
        Peer,
    }

    /// <summary>Reads the file line by line, as the helper does, and remembers what it has found.</summary>
    private struct Reader
    {
        /// <summary>The section the next line is in.</summary>
        private Section _section;

        /// <summary>The keys seen in the current section, one bit per key of its list.</summary>
        private int _seen;

        /// <summary>The number of <c>[Interface]</c> sections.</summary>
        private int _interfaces;

        /// <summary>The number of <c>[Peer]</c> sections.</summary>
        private int _peers;

        /// <summary>Whether <c>[Interface]</c> has a <c>PrivateKey</c>.</summary>
        private bool _privateKey;

        /// <summary>Whether <c>[Interface]</c> has an <c>Address</c>.</summary>
        private bool _address;

        /// <summary>Whether <c>[Interface]</c> names a DNS server by its IP address.</summary>
        private bool _dnsServer;

        /// <summary>Whether <c>[Peer]</c> has a <c>PublicKey</c>.</summary>
        private bool _publicKey;

        /// <summary>Whether <c>[Peer]</c> has an <c>Endpoint</c>.</summary>
        private bool _endpoint;

        /// <summary>Whether <c>[Peer]</c> has <c>AllowedIPs</c>.</summary>
        private bool _allowedIps;

        /// <summary>Reads one line: a header, a key with its value, a comment or nothing.</summary>
        /// <param name="raw">The line, without its <c>\n</c>.</param>
        /// <returns>Why the line is refused, or <see langword="null"/> when it is read.</returns>
        public WireGuardVerdict? Read(ReadOnlySpan<char> raw)
        {
            var hash = raw.IndexOf('#');
            var line = (hash < 0 ? raw : raw[..hash]).Trim();
            if (line.IsEmpty)
            {
                return null;
            }

            if (line[0] == '[')
            {
                return Enter(line);
            }

            var equals = line.IndexOf('=');
            if (equals < 0)
            {
                return WireGuardVerdict.LineWithoutEquals;
            }

            if (_section == Section.None)
            {
                return WireGuardVerdict.KeyBeforeSection;
            }

            // Like the helper, keys in a second [Interface] or [Peer] are not read; the count refuses the file.
            return _interfaces > 1 || _peers > 1
                ? null
                : Note(line[..equals].Trim(), line[(equals + 1)..].Trim());
        }

        /// <summary>Gives the verdict for what was found, in the order the helper checks it.</summary>
        /// <returns>The verdict.</returns>
        public readonly WireGuardVerdict Verdict() => this switch
        {
            { _interfaces: not 1 } => WireGuardVerdict.NotOneInterface,
            { _peers: not 1 } => WireGuardVerdict.NotOnePeer,
            { _privateKey: false } => WireGuardVerdict.NoPrivateKey,
            { _address: false } => WireGuardVerdict.NoAddress,
            { _dnsServer: false } => WireGuardVerdict.NoDnsServer,
            { _publicKey: false } => WireGuardVerdict.NoPublicKey,
            { _endpoint: false } => WireGuardVerdict.NoEndpoint,
            { _allowedIps: false } => WireGuardVerdict.NoAllowedIps,
            _ => WireGuardVerdict.Valid,
        };

        /// <summary>Enters a section and counts it.</summary>
        /// <param name="line">The header's line, such as <c>[Peer]</c>.</param>
        /// <returns>Why the header is refused, or <see langword="null"/>.</returns>
        private WireGuardVerdict? Enter(ReadOnlySpan<char> line)
        {
            if (line[^1] != ']')
            {
                return WireGuardVerdict.BrokenHeader;
            }

            var name = line[1..^1].Trim();
            _seen = 0;
            if (Is(name, "Interface"))
            {
                _section = Section.Interface;
                _interfaces++;
                return null;
            }

            if (Is(name, "Peer"))
            {
                _section = Section.Peer;
                _peers++;
                return null;
            }

            return WireGuardVerdict.UnknownSection;
        }

        /// <summary>Notes a key with its value. Only the DNS value is looked at: it must be an address.</summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value; a key's value is never copied.</param>
        /// <returns>Why the key is refused, or <see langword="null"/>.</returns>
        private WireGuardVerdict? Note(ReadOnlySpan<char> key, ReadOnlySpan<char> value)
        {
            var keys = _section == Section.Interface ? InterfaceKeys : PeerKeys;
            var index = IndexOf(keys, key);
            if (index < 0)
            {
                return _section == Section.Interface && IndexOf(IgnoredInterfaceKeys, key) >= 0
                    ? null
                    : WireGuardVerdict.UnknownKey;
            }

            var bit = 1 << index;
            if ((_seen & bit) != 0 && IndexOf(RepeatableKeys, key) < 0)
            {
                return WireGuardVerdict.RepeatedKey;
            }

            _seen |= bit;
            if (value.IsEmpty)
            {
                return null;
            }

            if (_section == Section.Interface)
            {
                _privateKey |= Is(key, "PrivateKey");
                _address |= Is(key, "Address");
                _dnsServer |= Is(key, "DNS") && NamesAddress(value);
            }
            else
            {
                _publicKey |= Is(key, "PublicKey");
                _endpoint |= Is(key, "Endpoint");
                _allowedIps |= Is(key, "AllowedIPs");
            }

            return null;
        }
    }
}
