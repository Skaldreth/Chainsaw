using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// Describes the envelopes in a case file's header for the user (sketch ÄR1b) without changing any of them: which one
/// this vault opens, the other vaults', the sharing envelope, the recovery keys' and those of unknown kinds. Every
/// file key opened on the way is cleared before it returns.
/// </summary>
internal static class EnvelopeDescriptions
{
    /// <summary>Describes every envelope, in file order.</summary>
    /// <param name="header">The header.</param>
    /// <param name="wrappingKey">This vault's purpose key for envelopes.</param>
    /// <param name="fileKey">
    /// The case's file key when the case is open, so that only an envelope that opens to it is this vault's; empty
    /// when the case is not open, and then any envelope this vault opens is.
    /// </param>
    /// <param name="options">The options, with the built-in and the revoked recovery keys.</param>
    /// <returns>The envelopes.</returns>
    public static IReadOnlyList<CaseEnvelope> Describe(
        CaseHeader header, ReadOnlySpan<byte> wrappingKey, ReadOnlySpan<byte> fileKey, CasesOptions options)
    {
        var opened = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            var described = new List<CaseEnvelope>(header.Envelopes.Count);
            foreach (var envelope in header.Envelopes)
            {
                described.Add(Describe(envelope, header.CaseId, wrappingKey, fileKey, opened, options));
                CryptographicOperations.ZeroMemory(opened);
            }

            return described;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(opened);
        }
    }

    /// <summary>Describes one envelope.</summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="caseId">The case id.</param>
    /// <param name="wrappingKey">This vault's purpose key for envelopes.</param>
    /// <param name="fileKey">The case's file key, or empty.</param>
    /// <param name="opened">A buffer for the key a vault envelope opens to; the caller clears it.</param>
    /// <param name="options">The options.</param>
    /// <returns>The description.</returns>
    private static CaseEnvelope Describe(
        JsonObject envelope,
        Guid caseId,
        ReadOnlySpan<byte> wrappingKey,
        ReadOnlySpan<byte> fileKey,
        Span<byte> opened,
        CasesOptions options)
    {
        if (KeyEnvelopes.IsKind(envelope, KeyEnvelopes.OperatorKind))
        {
            var isYours = KeyEnvelopes.TryOpenOne(envelope, caseId, wrappingKey, opened)
                && (fileKey.IsEmpty || CryptographicOperations.FixedTimeEquals(opened, fileKey));
            return new CaseEnvelope(isYours ? CaseEnvelopeKind.Yours : CaseEnvelopeKind.OtherVault);
        }

        if (KeyEnvelopes.IsKind(envelope, KeyEnvelopes.PasswordKind))
        {
            return new CaseEnvelope(CaseEnvelopeKind.SharingPassword);
        }

        return RecoveryEnvelopes.FingerprintOf(envelope) is { } fingerprint
            ? new CaseEnvelope(CaseEnvelopeKind.Recovery, fingerprint, RecoveryKeyOf(fingerprint, options))
            : new CaseEnvelope(CaseEnvelopeKind.Unknown);
    }

    /// <summary>Tells which recovery key a fingerprint belongs to, as this build sees it.</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <param name="options">The options.</param>
    /// <returns>The key.</returns>
    private static RecoveryEnvelopeKey RecoveryKeyOf(string fingerprint, CasesOptions options)
    {
        if (options.RevokedRecoveryKeys.Contains(fingerprint))
        {
            return RecoveryEnvelopeKey.Revoked;
        }

        return string.Equals(options.RecoveryKey?.Fingerprint, fingerprint, StringComparison.Ordinal)
            ? RecoveryEnvelopeKey.BuiltIn
            : RecoveryEnvelopeKey.Other;
    }
}
