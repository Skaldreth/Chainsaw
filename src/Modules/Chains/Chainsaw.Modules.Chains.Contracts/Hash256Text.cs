namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>Checks and normalises the 64 hex characters of a txid or a block hash.</summary>
internal static class Hash256Text
{
    /// <summary>The number of hex characters in a 32-byte hash.</summary>
    public const int Length = 64;

    /// <summary>Checks a text and gives it in lower case.</summary>
    /// <param name="text">The text.</param>
    /// <param name="normalised">The text in lower case, when it is valid.</param>
    /// <returns><see langword="true"/> when the text is exactly 64 hex characters.</returns>
    public static bool TryNormalise(string? text, out string normalised)
    {
        normalised = string.Empty;
        if (text is null || text.Length != Length || !text.All(char.IsAsciiHexDigit))
        {
            return false;
        }

        normalised = text.ToLowerInvariant();
        return true;
    }
}
