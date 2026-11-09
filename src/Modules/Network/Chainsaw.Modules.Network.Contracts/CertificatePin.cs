using System.Security.Cryptography;

namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// The fingerprint a local endpoint's certificate is pinned with (sketch KL3b): the SHA-256 of the <em>whole
/// certificate</em> in DER form, as 64 upper-case hex digits.
/// </summary>
/// <remarks>
/// The whole certificate rather than its public key: it is the fingerprint Windows' certificate dialog, the browsers
/// and <c>openssl x509 -fingerprint -sha256</c> show, so the user can compare it with what the node shows, and it pins
/// exactly the certificate the user looked at, dates and name included. A node that renews its certificate with the
/// same key is then a change the user sees and confirms, never one that passes in silence.
/// </remarks>
public static class CertificatePin
{
    /// <summary>The number of hex digits in a fingerprint.</summary>
    public const int Length = 64;

    /// <summary>Gives a certificate's fingerprint.</summary>
    /// <param name="certificate">The certificate in DER form.</param>
    /// <returns>The fingerprint.</returns>
    public static string Of(ReadOnlySpan<byte> certificate) => Convert.ToHexString(SHA256.HashData(certificate));

    /// <summary>Tells whether a text is a fingerprint: 64 hex digits in upper case.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when it is one.</returns>
    public static bool IsValid(string? text) =>
        text is { Length: Length } && text.All(digit => char.IsAsciiDigit(digit) || char.IsAsciiHexDigitUpper(digit));

    /// <summary>Gives the fingerprint's bytes, for the picture of it as a grid of one cell per byte.</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <returns>The 32 bytes.</returns>
    /// <exception cref="ArgumentException">It is not a fingerprint.</exception>
    public static byte[] Bytes(string fingerprint) => IsValid(fingerprint)
        ? Convert.FromHexString(fingerprint)
        : throw new ArgumentException("Not a certificate's fingerprint.", nameof(fingerprint));

    /// <summary>Gives the fingerprint in pairs split by colons, as Windows and the browsers show it.</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <returns>For example <c>4F:9A:1C:…</c>; empty when it is not a fingerprint.</returns>
    public static string Grouped(string fingerprint) => IsValid(fingerprint)
        ? string.Join(':', Enumerable.Range(0, Length / 2).Select(index => fingerprint.Substring(index * 2, 2)))
        : string.Empty;

    /// <summary>Gives the short form shown under the picture: the first and the last two bytes.</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <returns>For example <c>4F:9A … 6D:12</c>; empty when it is not a fingerprint.</returns>
    public static string Short(string fingerprint) => IsValid(fingerprint)
        ? $"{fingerprint[..2]}:{fingerprint[2..4]} … {fingerprint[^4..^2]}:{fingerprint[^2..]}"
        : string.Empty;
}
