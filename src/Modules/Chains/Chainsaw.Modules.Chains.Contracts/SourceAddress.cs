namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// The rules for an endpoint's address: only <c>https</c> (CLAUDE.md, <em>Säkerhet</em>), absolute, without user,
/// query or fragment, so that nothing but the lookup's path is ever added to it. A key is never put in the address.
/// </summary>
public static class SourceAddress
{
    /// <summary>The longest address, in characters.</summary>
    public const int MaximumLength = 300;

    /// <summary>The longest name of an endpoint.</summary>
    public const int MaximumNameLength = 80;

    /// <summary>Reads an address as the user wrote it.</summary>
    /// <param name="text">The address.</param>
    /// <param name="address">The address ending in <c>/</c>, or <see langword="null"/>.</param>
    /// <param name="problem">Why it cannot be used, in Swedish, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when it can be used.</returns>
    public static bool TryRead(string? text, out Uri? address, out string? problem)
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
