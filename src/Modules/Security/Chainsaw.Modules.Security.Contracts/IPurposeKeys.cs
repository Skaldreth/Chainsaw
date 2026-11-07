namespace Chainsaw.Modules.Security.Contracts;

/// <summary>
/// Key material for other modules (for example the case files), derived from the vault's data key with HKDF and the
/// name of a purpose. The data key itself never leaves the Security module, and keys for different purposes are
/// independent of each other. A purpose key stays the same when the master key is changed.
/// </summary>
public interface IPurposeKeys
{
    /// <summary>The longest purpose name that is accepted.</summary>
    const int MaximumPurposeLength = 128;

    /// <summary>Derives the key for a purpose into a buffer that the caller owns and must clear after use.</summary>
    /// <param name="purpose">
    /// A stable name for the purpose, for example <c>cases/file-key</c>: lower-case ASCII letters, digits and
    /// <c>/ - . _</c>, at most <see cref="MaximumPurposeLength"/> characters.
    /// The same purpose always gives the same key.
    /// </param>
    /// <param name="destination">Receives the key; between 16 and 64 bytes.</param>
    /// <exception cref="VaultException"><see cref="VaultError.Locked"/> when the vault is not unlocked.</exception>
    /// <exception cref="ArgumentException">The purpose or the destination length is not allowed.</exception>
    void DeriveKey(string purpose, Span<byte> destination);
}
