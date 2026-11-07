using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Chainsaw.App.ViewModels;

/// <summary>Helpers for secrets held as characters, which, unlike strings, can be cleared.</summary>
public static class Secret
{
    /// <summary>Overwrites a secret with zeros, in a way the compiler does not remove.</summary>
    /// <param name="secret">The characters to clear.</param>
    public static void Clear(char[] secret) =>
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(secret.AsSpan()));
}
