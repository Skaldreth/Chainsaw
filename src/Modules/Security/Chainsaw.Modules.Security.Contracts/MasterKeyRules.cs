namespace Chainsaw.Modules.Security.Contracts;

/// <summary>The rules a master key must follow, so that the setup screen and the vault agree on them.</summary>
public static class MasterKeyRules
{
    /// <summary>The fewest characters a master key may have.</summary>
    public const int MinimumLength = 12;

    /// <summary>Tells whether a master key is long enough.</summary>
    /// <param name="masterKey">The master key, as characters that the caller can clear afterwards.</param>
    /// <returns><see langword="true"/> when the key has at least <see cref="MinimumLength"/> characters.</returns>
    public static bool IsLongEnough(ReadOnlySpan<char> masterKey) => masterKey.Length >= MinimumLength;
}
