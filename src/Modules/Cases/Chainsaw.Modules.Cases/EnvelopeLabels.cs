using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The name on a vault's envelope (sketch ÄR1b): the operator who was given the envelope, encrypted with AES-256-GCM
/// under a key derived from the file key, so that only someone who can open the case reads it. To everyone else the
/// file still does not show whose it is, which is why the envelopes carried no name before; someone who can open the
/// case can read the journal, which names the operators anyway.
/// </summary>
/// <remarks>
/// The label is bound to its case and to its envelope's wrapped key, so that it cannot be moved to another envelope.
/// An envelope without a label (or with one that does not open) is described without a name.
/// </remarks>
internal static class EnvelopeLabels
{
    /// <summary>The field with the encrypted name.</summary>
    public const string LabelField = "label";

    /// <summary>The longest name that is written; a profile's name is far shorter.</summary>
    public const int MaximumLength = 200;

    /// <summary>The HKDF info for the key of the labels.</summary>
    private const string KeyInfo = "chainsaw/case/envelope-label/v1";

    /// <summary>The largest label that is read: the nonce, the longest name in UTF-8 and the tag.</summary>
    private const int MaximumLabelBytes = CaseCrypto.NonceSize + (MaximumLength * 4) + CaseCrypto.TagSize;

    /// <summary>Adds a name to a vault's envelope, encrypted under the file key.</summary>
    /// <param name="envelope">The new envelope; its wrapped key must be set.</param>
    /// <param name="caseId">The case id.</param>
    /// <param name="fileKey">The file key.</param>
    /// <param name="name">The operator's name; nothing is added when it is empty.</param>
    public static void Add(JsonObject envelope, Guid caseId, ReadOnlySpan<byte> fileKey, string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return;
        }

        if (trimmed.Length > MaximumLength)
        {
            trimmed = trimmed[..MaximumLength];
        }

        var key = DeriveKey(fileKey, caseId);
        try
        {
            var plaintext = Encoding.UTF8.GetBytes(trimmed);
            var (nonce, ciphertext, tag) = CaseCrypto.Wrap(key, plaintext, BindingOf(envelope, caseId));
            envelope[LabelField] = Convert.ToBase64String([.. nonce, .. ciphertext, .. tag]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Reads the name on a vault's envelope.</summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="caseId">The case id.</param>
    /// <param name="fileKey">The file key.</param>
    /// <returns>The name, or <see langword="null"/> when there is none or it does not open.</returns>
    public static string? Read(JsonObject envelope, Guid caseId, ReadOnlySpan<byte> fileKey)
    {
        if (fileKey.IsEmpty || envelope[LabelField] is not JsonValue value
            || !value.TryGetValue<string>(out var text))
        {
            return null;
        }

        byte[] label;
        try
        {
            label = Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            return null;
        }

        if (label.Length <= CaseCrypto.NonceSize + CaseCrypto.TagSize || label.Length > MaximumLabelBytes)
        {
            return null;
        }

        var key = DeriveKey(fileKey, caseId);
        try
        {
            var nonce = label.AsSpan(0, CaseCrypto.NonceSize);
            var tag = label.AsSpan(label.Length - CaseCrypto.TagSize);
            var ciphertext = label.AsSpan(CaseCrypto.NonceSize, label.Length - CaseCrypto.NonceSize - CaseCrypto.TagSize);
            var plaintext = new byte[ciphertext.Length];
            return CaseCrypto.TryUnwrap(key, nonce, ciphertext, tag, BindingOf(envelope, caseId), plaintext)
                ? Encoding.UTF8.GetString(plaintext)
                : null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Derives the key of the labels from the file key.</summary>
    /// <param name="fileKey">The file key.</param>
    /// <param name="caseId">The case id.</param>
    /// <returns>The key in a pinned buffer that the caller clears.</returns>
    private static byte[] DeriveKey(ReadOnlySpan<byte> fileKey, Guid caseId)
    {
        var key = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        HKDF.DeriveKey(HashAlgorithmName.SHA256, fileKey, key, caseId.ToByteArray(), Encoding.ASCII.GetBytes(KeyInfo));
        return key;
    }

    /// <summary>The associated data: the case and the envelope's wrapped key, which no other envelope has.</summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="caseId">The case id.</param>
    /// <returns>The bytes.</returns>
    private static byte[] BindingOf(JsonObject envelope, Guid caseId)
    {
        var wrapped = envelope[KeyEnvelopes.KeyField] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : string.Empty;
        return Encoding.UTF8.GetBytes($"{KeyInfo}\n{caseId:D}\n{wrapped}");
    }
}
