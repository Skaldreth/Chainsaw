namespace Chainsaw.Modules.Security;

/// <summary>The output of one AES-256-GCM encryption: a fresh nonce, the ciphertext and the authentication tag.</summary>
/// <param name="Nonce">The 12-byte nonce, random for every encryption so that it is never reused with the same key.</param>
/// <param name="Ciphertext">The encrypted bytes, as long as the plaintext.</param>
/// <param name="Tag">The 16-byte tag that proves the ciphertext and its associated data are unchanged.</param>
internal sealed record SealedBox(byte[] Nonce, byte[] Ciphertext, byte[] Tag);
