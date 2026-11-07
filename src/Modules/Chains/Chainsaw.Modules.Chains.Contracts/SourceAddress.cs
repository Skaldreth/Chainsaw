using System.Net;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// The rules for an endpoint's address: only <c>https</c> (CLAUDE.md, <em>Säkerhet</em>), absolute, without user,
/// query or fragment, so that nothing but the lookup's path is ever added to it. A key is never put in the address.
/// An address on the computer or a private network is allowed only for an endpoint the user marks as their own node
/// (<em>lokal</em>), and such an endpoint must not have a global IP address (decided 2026-11-07).
/// </summary>
public static class SourceAddress
{
    /// <summary>The longest address, in characters.</summary>
    public const int MaximumLength = 300;

    /// <summary>The longest name of an endpoint.</summary>
    public const int MaximumNameLength = 80;

    /// <summary>What an address on the computer or a private network says when the endpoint is not marked
    /// local.</summary>
    public const string LocalNeedsMarkText = "En adress på datorn eller ett privat nät kräver att den märks Lokal.";

    /// <summary>What a global IP address says when the endpoint is marked local.</summary>
    public const string LocalMustBeLocalText = "En lokal endpoint ska ha en adress på datorn eller ett privat nät.";

    /// <summary>Reads the address of an endpoint on the internet, as the user wrote it.</summary>
    /// <param name="text">The address.</param>
    /// <param name="address">The address ending in <c>/</c>, or <see langword="null"/>.</param>
    /// <param name="problem">Why it cannot be used, in Swedish, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when it can be used.</returns>
    public static bool TryRead(string? text, out Uri? address, out string? problem) =>
        TryRead(text, local: false, out address, out problem);

    /// <summary>Reads an address as the user wrote it.</summary>
    /// <param name="text">The address.</param>
    /// <param name="local">
    /// Whether the endpoint is marked as the user's own node: then the address must not be a global IP address; else
    /// it must not be on the computer or a private network. A name (<c>esplora.lan</c>) is checked when it is
    /// resolved: through the exit only global addresses are reached, and to an own node only local ones.
    /// </param>
    /// <param name="address">The address ending in <c>/</c>, or <see langword="null"/>.</param>
    /// <param name="problem">Why it cannot be used, in Swedish, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when it can be used.</returns>
    public static bool TryRead(string? text, bool local, out Uri? address, out string? problem)
    {
        address = null;
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            problem = "Skriv adressen.";
            return false;
        }

        if (trimmed.Length > MaximumLength
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || uri.HostNameType == UriHostNameType.Unknown)
        {
            problem = "Adressen går inte att läsa.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            problem = "Bara https.";
            return false;
        }

        if (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            problem = "Adressen får inte ha användare, frågor (?) eller #.";
            return false;
        }

        var literal = IPAddress.TryParse(uri.IdnHost.Trim('[', ']'), out var ip) ? ip : null;
        if (!local && LocalAddresses.IsLocal(uri))
        {
            problem = LocalNeedsMarkText;
            return false;
        }

        if (local && literal is not null && !LocalAddresses.IsLocal(literal))
        {
            problem = LocalMustBeLocalText;
            return false;
        }

        address = uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
        problem = null;
        return true;
    }

    /// <summary>Gives the name an endpoint gets from its address: the host.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The host, for example <c>mempool.space</c>.</returns>
    public static string NameOf(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.IdnHost;
    }
}
