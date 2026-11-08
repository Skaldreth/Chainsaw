namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>What kind of key opens one of the envelopes in a case file's header.</summary>
public enum CaseEnvelopeKind
{
    /// <summary>This operator's vault: the case opens with the master key.</summary>
    Yours,

    /// <summary>
    /// Another vault, for example a colleague's. Its name (<see cref="CaseEnvelope.Name"/>) is encrypted under the
    /// file key, so that the file does not show whose it is to anyone who cannot open it.
    /// </summary>
    OtherVault,

    /// <summary>The sharing envelope, which opens with the case password.</summary>
    SharingPassword,

    /// <summary>An organisation's recovery key; see <see cref="CaseEnvelope.Recovery"/>.</summary>
    Recovery,

    /// <summary>A kind or format this build does not know. It is kept as it is and never removed.</summary>
    Unknown,
}

/// <summary>Which recovery key an organisation's envelope is for, as this build sees it.</summary>
public enum RecoveryEnvelopeKey
{
    /// <summary>The key built into this Chainsaw.</summary>
    BuiltIn,

    /// <summary>A key this build revokes; its envelope is removed when the case is opened.</summary>
    Revoked,

    /// <summary>Another key (an older or newer one, or another organisation's); its envelope is kept.</summary>
    Other,
}

/// <summary>
/// One envelope in a case file's header, described without its secret (sketch ÄR1b). The file key itself is never
/// part of it.
/// </summary>
/// <param name="Kind">What kind of key opens it.</param>
/// <param name="Fingerprint">
/// For <see cref="CaseEnvelopeKind.Recovery"/>, the key's fingerprint (see
/// <see cref="RecoveryPublicKey.Fingerprint"/>); otherwise <see langword="null"/>.
/// </param>
/// <param name="Recovery">
/// For <see cref="CaseEnvelopeKind.Recovery"/>, which key it is; otherwise <see langword="null"/>.
/// </param>
public sealed record CaseEnvelope(
    CaseEnvelopeKind Kind, string? Fingerprint = null, RecoveryEnvelopeKey? Recovery = null)
{
    /// <summary>
    /// Gets, for a vault's envelope, the name of the operator it was made for, when the case is open and the envelope
    /// has one; otherwise <see langword="null"/>. Envelopes made before names were added have none.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets, for <see cref="CaseEnvelopeKind.Unknown"/>, the kind the envelope says it is (text from the file,
    /// shortened and without control characters), or <see langword="null"/>.
    /// </summary>
    public string? UnknownKind { get; init; }
}
