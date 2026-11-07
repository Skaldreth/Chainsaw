namespace Chainsaw.Modules.Security.Contracts;

/// <summary>
/// Protects data for the Windows user who is signed in (DPAPI, scope CurrentUser), without the master key: what the
/// background watching needs (CLAUDE.md, <em>Bevaka</em>) is kept this way, so that it can work while the vault is
/// locked and Chainsaw is closed. Nothing protected this way may open a case or the vault.
/// </summary>
/// <remarks>
/// The protection binds the data to the Windows account on this computer: another account, or the same file on
/// another computer, cannot read it. It is not a protection against programs that run as the same user.
/// </remarks>
public interface IUserDataProtector
{
    /// <summary>Protects data for the signed-in user.</summary>
    /// <param name="data">The data.</param>
    /// <param name="purpose">
    /// What the data is for, bound to the protection, so that data protected for one purpose is not read as another.
    /// </param>
    /// <returns>The protected bytes.</returns>
    /// <exception cref="PlatformNotSupportedException">Not on Windows.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The protection failed.</exception>
    byte[] Protect(ReadOnlySpan<byte> data, string purpose);

    /// <summary>Reads data protected for the signed-in user and the same purpose.</summary>
    /// <param name="protectedData">The protected bytes.</param>
    /// <param name="purpose">The purpose it was protected for.</param>
    /// <returns>The data.</returns>
    /// <exception cref="PlatformNotSupportedException">Not on Windows.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The bytes were changed, protected for another user or purpose, or are not protected data.
    /// </exception>
    byte[] Unprotect(ReadOnlySpan<byte> protectedData, string purpose);
}
