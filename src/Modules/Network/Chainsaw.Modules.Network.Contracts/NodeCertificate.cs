namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// The certificate the user's own node shows, read without trusting it so that the user can compare and pin it (sketch
/// KL3b). Only what the user needs to decide is kept, never the key.
/// </summary>
/// <param name="Fingerprint">
/// The SHA-256 of the whole certificate in DER form, as 64 upper-case hex digits (<see cref="CertificatePin"/>).
/// </param>
/// <param name="Subject">Who it is for, as the certificate says.</param>
/// <param name="Issuer">Who issued it, as the certificate says.</param>
/// <param name="NotBefore">From when it is valid, in UTC.</param>
/// <param name="NotAfter">Until when it is valid, in UTC.</param>
/// <param name="TrustedByWindows">
/// Whether it chains to a root installed on the computer and names the node, checked without fetching anything, as a
/// request without a pin checks it.
/// </param>
public sealed record NodeCertificate(
    string Fingerprint,
    string Subject,
    string Issuer,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    bool TrustedByWindows)
{
    /// <summary>Gets whether it is signed by itself: issued by the same name it is for.</summary>
    public bool SelfSigned => string.Equals(Subject, Issuer, StringComparison.Ordinal);
}
