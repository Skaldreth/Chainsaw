using System.Runtime.InteropServices;
using System.Windows.Controls;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Controls;

/// <summary>
/// A <see cref="PasswordBox"/> as the view models see a secret field. The secret is read from the box's
/// <see cref="PasswordBox.SecurePassword"/> straight into a pinned array, never through
/// <see cref="PasswordBox.Password"/>, which would make it a string that cannot be cleared.
/// </summary>
/// <param name="box">The box.</param>
public sealed class PasswordEntry(PasswordBox box) : ISecretEntry
{
    /// <inheritdoc />
    public char[] Take()
    {
        using var secure = box.SecurePassword;
        box.Clear();

        // Pinned, so that the garbage collector never leaves a copy behind when it moves memory.
        var characters = GC.AllocateArray<char>(secure.Length, pinned: true);
        if (secure.Length == 0)
        {
            return characters;
        }

        var unmanaged = Marshal.SecureStringToGlobalAllocUnicode(secure);
        try
        {
            Marshal.Copy(unmanaged, characters, 0, secure.Length);
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(unmanaged);
        }

        return characters;
    }
}
