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

    /// <summary>The length of a fingerprint in hexadecimal digits.</summary>
    private const int FingerprintLength = 64;

    /// <summary>
    /// The characters besides whitespace that may decorate a revocation: list markers, quotes, separators and
    /// invisible marks that editors insert.
    /// </summary>
    private const string Decoration = "-–—*•`'\":=\u200B\uFEFF";

    /// <summary>Every kind of line break, so that a file saved with CR, LF or CRLF reads the same.</summary>
    private static readonly char[] LineBreaks = ['\r', '\n', '\u0085', '\u2028', '\u2029', '\f', '\v'];

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
        // stop the application at startup rather than pass unnoticed).
        var lines = text.Split(LineBreaks);
        var revoked = lines
            .Select((line, index) => (Line: line, Number: index + 1))
            .Where(item => item.Line.Contains(RevokedKeyword, StringComparison.OrdinalIgnoreCase))
            .Select(item => ReadFingerprint(item.Line, item.Number))
            .ToHashSet(StringComparer.Ordinal);
        if (lines.Any(line => !line.Contains(RevokedKeyword, StringComparison.OrdinalIgnoreCase)
            && HoldsFingerprint(line)))
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
    /// Reads the fingerprint on a line that mentions the keyword. The line may hold nothing but the keyword, one
    /// fingerprint of 64 hexadecimal digits in either case, and decoration (whitespace, list markers, backticks, a
    /// colon or an equals sign, dashes between groups); anything else, a second fingerprint or a comment included,
    /// stops the build from running, so that a revocation is never lost or turned into another one by how it was
    /// written.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <param name="number">The line's number, for the message (never its text).</param>
    /// <returns>The fingerprint in lowercase.</returns>
    /// <exception cref="InvalidOperationException">The line is not exactly one revocation.</exception>
    private static string ReadFingerprint(string line, int number)
    {
        var rest = line.Replace(RevokedKeyword, string.Empty, StringComparison.OrdinalIgnoreCase);
        var digits = new string([.. rest.Where(character => !IsDecoration(character))]);
        return digits.Length == FingerprintLength && digits.All(char.IsAsciiHexDigit)
            ? digits.ToLowerInvariant()
            : throw new InvalidOperationException(
                $"The resource {ResourceName}, line {number}: a revocation must hold only the keyword and one "
                + "fingerprint.");
    }

    /// <summary>Checks whether a character is decoration around a revocation rather than content.</summary>
    /// <param name="character">The character.</param>
    /// <returns><see langword="true"/> for decoration.</returns>
    private static bool IsDecoration(char character) =>
        char.IsWhiteSpace(character) || Decoration.Contains(character);

    /// <summary>
    /// Checks whether a line without the keyword holds a fingerprint in any notation: 64 hexadecimal digits in a row
    /// once everything but letters and digits is taken out.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool HoldsFingerprint(string line) =>
        LooseFingerprint().IsMatch(new string([.. line.Where(char.IsLetterOrDigit)]));

    /// <summary>64 hexadecimal digits in a row.</summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex("[0-9A-Fa-f]{64}")]
    private static partial Regex LooseFingerprint();
}

/// <summary>The recovery key a build has, and the earlier keys it revokes.</summary>
/// <param name="Key">The public key, or <see langword="null"/> when none is configured.</param>
/// <param name="Revoked">
/// The fingerprints of revoked keys, whose envelopes are removed from cases that are opened.
/// </param>
internal sealed record RecoveryKeyConfiguration(RecoveryPublicKey? Key, IReadOnlySet<string> Revoked);
