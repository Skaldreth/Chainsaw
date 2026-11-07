namespace Chainsaw.App.Exits;

/// <summary>What the quick check of a WireGuard file found, before it is stored in the vault.</summary>
public enum WireGuardVerdict
{
    /// <summary>The file has every section and key a tunnel needs.</summary>
    Valid,

    /// <summary>The file is empty.</summary>
    Empty,

    /// <summary>The file is larger than a configuration may be.</summary>
    TooLarge,

    /// <summary>The file has no <c>[Interface]</c>, or more than one.</summary>
    NotOneInterface,

    /// <summary>The file has no <c>[Peer]</c>, or more than one (the tunnel takes exactly one).</summary>
    NotOnePeer,

    /// <summary><c>[Interface]</c> has no <c>PrivateKey</c>.</summary>
    NoPrivateKey,

    /// <summary><c>[Interface]</c> has no <c>Address</c>.</summary>
    NoAddress,

    /// <summary><c>[Interface]</c> names no DNS server by its IP address.</summary>
    NoDnsServer,

    /// <summary><c>[Peer]</c> has no <c>PublicKey</c>.</summary>
    NoPublicKey,

    /// <summary><c>[Peer]</c> has no <c>Endpoint</c>.</summary>
    NoEndpoint,

    /// <summary><c>[Peer]</c> has no <c>AllowedIPs</c>.</summary>
    NoAllowedIps,
}
