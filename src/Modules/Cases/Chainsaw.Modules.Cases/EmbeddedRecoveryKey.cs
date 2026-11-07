using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
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
internal static partial class EmbeddedRecoveryKey
{
    /// <summary>The name of the embedded resource.</summary>
    public const string ResourceName = "Chainsaw.Modules.Cases.organization-recovery-key.pem";

    /// <summary>
    /// The keyword of a line that revokes an earlier key, <c>REVOKED &lt;fingerprint&gt;</c>; every line that
    /// mentions it, in any case, must hold exactly one fingerprint, and no other line may hold one.
    /// </summary>
    public const string RevokedKeyword = "REVOKED";

    /// <summary>What the keyword is replaced with before a fingerprint is looked for: no hexadecimal digit.</summary>
    private const string KeywordStandIn = "|";

    /// <summary>Every kind of line break, so that a file saved with CR, LF or CRLF reads the same.</summary>
    private static readonly char[] LineBreaks = ['\r', '\n'];

    /// <summary>The text that marks a private key, which must never be built in.</summary>
    private const string PrivateKeyMarker = "PRIVATE KEY";

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
        if (text.Contains(PrivateKeyMarker, StringComparison.OrdinalIgnoreCase))
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

        // Every line is read, also those of the key: a block written on one line or never closed must not hide a
        // revocation, and the key's Base64 holding the keyword or a fingerprint is too unlikely to matter (and would
        // stop the build rather than pass unnoticed).
        var lines = text.Split(LineBreaks);
        var revoked = lines
            .Where(line => line.Contains(RevokedKeyword, StringComparison.OrdinalIgnoreCase))
            .Select(ReadFingerprint)
            .ToHashSet(StringComparer.Ordinal);
        if (lines.Any(line => !line.Contains(RevokedKeyword, StringComparison.OrdinalIgnoreCase)
            && LooseFingerprint().IsMatch(Separators().Replace(line, string.Empty))))
        {
            // A fingerprint without the keyword (a typo, or the word in Swedish) is a revocation that would be lost.
            throw new InvalidOperationException(
                $"The resource {ResourceName} has a fingerprint on a line without {RevokedKeyword}.");
        }
        if (key is not null && revoked.Contains(key.Fingerprint))
        {
            throw new InvalidOperationException($"The resource {ResourceName} revokes its own key.");
        }

        return new RecoveryKeyConfiguration(key, revoked);
    }

    /// <summary>
    /// Reads the one fingerprint on a line that mentions the keyword, however the line is written (in a list, in
    /// quotes, after a colon or an equals sign): 64 hexadecimal digits in either case, together or in groups of four.
    /// A line that mentions the keyword without exactly one fingerprint stops the build from running, so that a
    /// revocation is never ignored because of how it was written.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>The fingerprint in lowercase without spaces.</returns>
    /// <exception cref="InvalidOperationException">The line does not hold exactly one fingerprint.</exception>
    private static string ReadFingerprint(string line)
    {
        // The keyword ends in a hexadecimal letter, so it is taken out before the fingerprint is looked for.
        var withoutKeyword = line.Replace(RevokedKeyword, KeywordStandIn, StringComparison.OrdinalIgnoreCase);
        var matches = Fingerprint().Matches(withoutKeyword);
        return matches.Count == 1
            ? new string([.. matches[0].Value.Where(char.IsAsciiHexDigit)]).ToLowerInvariant()
            : throw new InvalidOperationException(
                $"The resource {ResourceName} has a line about a revoked key without exactly one fingerprint.");
    }

    /// <summary>
    /// A fingerprint: 64 hexadecimal digits, together or in groups of four with whitespace between, with no further
    /// hexadecimal digit next to it, not even across whitespace; a hexadecimal word beside a grouped fingerprint would
    /// otherwise be taken as one of its groups and revoke another key than meant.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"(?<![0-9A-Fa-f]\s*)(?:[0-9A-Fa-f]{4}\s*){15}[0-9A-Fa-f]{4}(?!\s*[0-9A-Fa-f])")]
    private static partial Regex Fingerprint();

    /// <summary>64 hexadecimal digits in a row, for a line whose separators have been taken out.</summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex("[0-9A-Fa-f]{64}")]
    private static partial Regex LooseFingerprint();

    /// <summary>The separators people write between a fingerprint's digits: whitespace, colons and dashes.</summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"[\s:\-]")]
    private static partial Regex Separators();
}

/// <summary>The recovery key a build has, and the earlier keys it revokes.</summary>
/// <param name="Key">The public key, or <see langword="null"/> when none is configured.</param>
/// <param name="Revoked">
/// The fingerprints of revoked keys, whose envelopes are removed from cases that are opened.
/// </param>
internal sealed record RecoveryKeyConfiguration(RecoveryPublicKey? Key, IReadOnlySet<string> Revoked);
