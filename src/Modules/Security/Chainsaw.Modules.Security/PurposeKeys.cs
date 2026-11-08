using System.Buffers;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Security;

/// <summary>Derives keys for other modules from the vault's data key, one independent key per purpose.</summary>
/// <param name="vault">The vault that holds the data key.</param>
internal sealed class PurposeKeys(Vault vault) : IPurposeKeys
{
    /// <summary>The shortest key that is handed out (128 bits).</summary>
    public const int MinimumKeyLength = 16;

    /// <summary>The longest key that is handed out (512 bits).</summary>
    public const int MaximumKeyLength = 64;

    /// <summary>The characters a purpose or secret name may contain after its first character.</summary>
    private static readonly SearchValues<char> PurposeCharacters =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789/-._");

    /// <inheritdoc />
    public void DeriveKey(string purpose, Span<byte> destination)
    {
        if (!IsValidName(purpose, IPurposeKeys.MaximumPurposeLength))
        {
            throw new ArgumentException(
                "The purpose must be lower-case ASCII letters, digits and / - . _, starting with a letter or digit.",
                nameof(purpose));
        }

        if (destination.Length is < MinimumKeyLength or > MaximumKeyLength)
        {
            throw new ArgumentException(
                $"The key must be between {MinimumKeyLength} and {MaximumKeyLength} bytes.", nameof(destination));
        }

        vault.DerivePurposeKey(purpose, destination);
    }

    /// <summary>
    /// Checks a purpose name, so that two different purposes can never give the same HKDF info; the vault's named
    /// secrets follow the same rule.
    /// </summary>
    /// <param name="name">The name to check.</param>
    /// <param name="maximumLength">The longest name allowed.</param>
    /// <returns><see langword="true"/> when the name is allowed.</returns>
    internal static bool IsValidName(string? name, int maximumLength) =>
        !string.IsNullOrEmpty(name)
        && name.Length <= maximumLength
        && (char.IsAsciiLetterLower(name[0]) || char.IsAsciiDigit(name[0]))
        && !name.AsSpan().ContainsAnyExcept(PurposeCharacters);
}
