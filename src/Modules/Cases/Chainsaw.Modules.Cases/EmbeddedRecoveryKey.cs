using System.Reflection;
using System.Security.Cryptography;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The organisation's recovery key as built into this assembly, from the resource
/// <c>organization-recovery-key.pem</c>: an explanation, the public key once the administrator has made a key pair,
/// and one line <c>REVOKED &lt;fingerprint&gt;</c> for every earlier key that no longer may open cases.
/// </summary>
/// <remarks>
/// Envelopes are only ever removed for keys revoked here, never because a key is merely unknown to this build: an
/// older build, or another organisation's, must not strip the envelope of a newer key from a file it opens.
/// </remarks>
internal static class EmbeddedRecoveryKey
{
    /// <summary>The name of the embedded resource.</summary>
    public const string ResourceName = "Chainsaw.Modules.Cases.organization-recovery-key.pem";

    /// <summary>The keyword that starts a line revoking an earlier key: <c>REVOKED &lt;fingerprint&gt;</c>.</summary>
    public const string RevokedKeyword = "REVOKED";

    /// <summary>The characters that may come before the keyword when the line is written as a list.</summary>
    private static readonly char[] ListMarkers = ['-', '*', '•'];

    /// <summary>Every kind of line break, so that a file saved with CR, LF or CRLF reads the same.</summary>
    private static readonly char[] LineBreaks = ['\r', '\n'];

    /// <summary>The text that marks a private key, which must never be built in.</summary>
    private const string PrivateKeyMarker = "PRIVATE KEY";

    /// <summary>The length of a fingerprint in hexadecimal digits.</summary>
    private const int FingerprintLength = 64;

    /// <summary>Reads the built-in configuration.</summary>
    /// <returns>The key (or none) and the revoked fingerprints.</returns>
    /// <exception cref="InvalidOperationException">
    /// The resource is missing or does not hold (see <see cref="Parse"/>); a build like that must not run.
    /// </exception>
    public static RecoveryKeyConfiguration Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The resource {ResourceName} is missing from the build.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parses the resource's text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The key (or none) and the revoked fingerprints.</returns>
    /// <exception cref="InvalidOperationException">
    /// The text holds a private key, a public key that is not P-384, a malformed revoked fingerprint, or revokes its
    /// own key; any of them would make cases that the administrator cannot open, or ship a secret.
    /// </exception>
    public static RecoveryKeyConfiguration Parse(string text)
    {
        if (text.Contains(PrivateKeyMarker, StringComparison.Ordinal))
        {
            // A private key pasted by mistake must never ship, nor pass for "no key configured".
            throw new InvalidOperationException($"The resource {ResourceName} holds a private key; remove it.");
        }

        RecoveryPublicKey? key;
        try
        {
            key = RecoveryPublicKey.FromPem(text);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException
            or ArgumentException)
        {
            throw new InvalidOperationException(
                $"The resource {ResourceName} does not hold a P-384 public key.", exception);
        }

        var revoked = text.Split(LineBreaks)
            .Select(RevokedFingerprintText)
            .OfType<string>()
            .Select(ReadFingerprint)
            .ToHashSet(StringComparer.Ordinal);
        if (key is not null && revoked.Contains(key.Fingerprint))
        {
            throw new InvalidOperationException($"The resource {ResourceName} revokes its own key.");
        }

        return new RecoveryKeyConfiguration(key, revoked);
    }

    /// <summary>
    /// Gives the text after the keyword when a line is meant as a revocation: its first word is <c>REVOKED</c> in any
    /// case, after any whitespace or list marker, and followed by any whitespace or a colon. Every such line must then
    /// hold a fingerprint, so that a revocation can never be ignored because of its spelling.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>The text after the keyword, or <see langword="null"/> when the line is no revocation.</returns>
    private static string? RevokedFingerprintText(string line)
    {
        var rest = line.TrimStart().TrimStart(ListMarkers).TrimStart();
        if (!rest.StartsWith(RevokedKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var after = rest[RevokedKeyword.Length..];
        return after.Length == 0 || char.IsWhiteSpace(after[0]) || after[0] == ':' ? after.TrimStart(':') : null;
    }

    /// <summary>Reads a fingerprint as written by people: upper or lower case, with or without spaces.</summary>
    /// <param name="text">The text after the prefix.</param>
    /// <returns>The fingerprint in lowercase without spaces.</returns>
    /// <exception cref="InvalidOperationException">It is not 64 hexadecimal digits.</exception>
    private static string ReadFingerprint(string text)
    {
        var fingerprint = new string([.. text.Where(character => !char.IsWhiteSpace(character))]).ToLowerInvariant();
        return fingerprint.Length == FingerprintLength && fingerprint.All(char.IsAsciiHexDigitLower)
            ? fingerprint
            : throw new InvalidOperationException($"The resource {ResourceName} has a malformed revoked key.");
    }
}

/// <summary>The recovery key a build has, and the earlier keys it revokes.</summary>
/// <param name="Key">The public key, or <see langword="null"/> when none is configured.</param>
/// <param name="Revoked">
/// The fingerprints of revoked keys, whose envelopes are removed from cases that are opened.
/// </param>
internal sealed record RecoveryKeyConfiguration(RecoveryPublicKey? Key, IReadOnlySet<string> Revoked);
