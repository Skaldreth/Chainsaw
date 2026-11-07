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

    /// <summary>The start of a line that revokes an earlier key.</summary>
    public const string RevokedPrefix = "REVOKED ";

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

        var revoked = text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(RevokedPrefix, StringComparison.Ordinal))
            .Select(line => ReadFingerprint(line[RevokedPrefix.Length..]))
            .ToHashSet(StringComparer.Ordinal);
        if (key is not null && revoked.Contains(key.Fingerprint))
        {
            throw new InvalidOperationException($"The resource {ResourceName} revokes its own key.");
        }

        return new RecoveryKeyConfiguration(key, revoked);
    }

    /// <summary>Reads a fingerprint as written by people: upper or lower case, with or without spaces.</summary>
    /// <param name="text">The text after the prefix.</param>
    /// <returns>The fingerprint in lowercase without spaces.</returns>
    /// <exception cref="InvalidOperationException">It is not 64 hexadecimal digits.</exception>
    private static string ReadFingerprint(string text)
    {
        var fingerprint = text.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
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
