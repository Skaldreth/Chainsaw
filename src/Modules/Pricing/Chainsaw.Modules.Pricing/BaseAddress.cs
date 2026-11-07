namespace Chainsaw.Modules.Pricing;

/// <summary>Checks a source's base address.</summary>
internal static class BaseAddress
{
    /// <summary>Checks that a base address is <c>https</c> of its own and makes it end in <c>/</c>.</summary>
    /// <param name="uri">The base address.</param>
    /// <param name="parameterName">The name of the setting, for the exception.</param>
    /// <returns>The checked address.</returns>
    /// <exception cref="ArgumentException">It is not an absolute <c>https</c> address without user, query or
    /// fragment.</exception>
    public static Uri Check(Uri uri, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(uri, parameterName);
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0
            || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            throw new ArgumentException(
                "A rate source's base address must be https without user, query or fragment.", parameterName);
        }

        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }
}
