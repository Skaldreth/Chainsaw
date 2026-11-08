namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// A certificate the user pinned for their own node (sketch KL3b), kept in the program's data with the endpoint, never
/// in a case: the fingerprint, when and by whom it was pinned, and, once it has been replaced or removed, when and by
/// whom, so that the old one is kept with the change.
/// </summary>
/// <param name="Fingerprint">
/// The SHA-256 of the whole certificate in DER form, as 64 upper-case hex digits (<c>CertificatePin</c>).
/// </param>
/// <param name="PinnedAt">When it was pinned, in UTC.</param>
/// <param name="PinnedBy">Who pinned it: the operator's name.</param>
public sealed record EndpointCertificate(string Fingerprint, DateTimeOffset PinnedAt, string PinnedBy)
{
    /// <summary>The most former pins kept per endpoint; older ones fall away.</summary>
    public const int MaximumFormer = 10;

    /// <summary>Gets when it was replaced or removed, in UTC, or <see langword="null"/> while it is the pin.</summary>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>Gets who replaced or removed it, or <see langword="null"/> while it is the pin.</summary>
    public string? EndedBy { get; init; }
}
