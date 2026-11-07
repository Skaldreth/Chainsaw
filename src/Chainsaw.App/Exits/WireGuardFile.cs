using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.App.Exits;

/// <summary>
/// Reads a WireGuard file as a secret: into pinned characters that the caller clears with
/// <see cref="ViewModels.Secret.Clear"/>, never into a string, and the raw bytes are cleared before the method returns.
/// The text becomes a string only at the moment it is handed to <see cref="IExitStore.Add"/>, which takes one.
/// </summary>
public static class WireGuardFile
{
    /// <summary>The largest file read, in bytes: the longest configuration in four-byte characters.</summary>
    public const long MaximumBytes = ExitRules.MaximumConfigurationLength * 4L;

    /// <summary>Reads a file's text as UTF-8, without a byte order mark.</summary>
    /// <param name="path">The file.</param>
    /// <returns>
    /// The text in a pinned array the caller must clear, or <see langword="null"/> when the file is larger than
    /// <see cref="MaximumBytes"/>.
    /// </returns>
    /// <exception cref="IOException">The file could not be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file may not be read.</exception>
    public static char[]? Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.None);
        if (stream.Length > MaximumBytes)
        {
            return null;
        }

        var bytes = GC.AllocateUninitializedArray<byte>((int)stream.Length, pinned: true);
        try
        {
            stream.ReadExactly(bytes);
            var body = bytes.AsSpan();
            if (body.StartsWith(Encoding.UTF8.Preamble))
            {
                body = body[Encoding.UTF8.Preamble.Length..];
            }

            var text = GC.AllocateUninitializedArray<char>(Encoding.UTF8.GetCharCount(body), pinned: true);
            Encoding.UTF8.GetChars(body, text);
            return text;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
