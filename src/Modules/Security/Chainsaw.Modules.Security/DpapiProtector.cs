using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Security;

/// <summary>
/// Protects data with Windows' DPAPI for the signed-in user (<c>CryptProtectData</c> without
/// <c>CRYPTPROTECT_LOCAL_MACHINE</c>), with the purpose as extra entropy and no prompt. Called straight into
/// <c>crypt32.dll</c>, so that no package is needed.
/// </summary>
internal sealed partial class DpapiProtector : IUserDataProtector
{
    /// <summary>Never shows a prompt: the background process has no one to ask.</summary>
    private const int UiForbidden = 0x1;

    /// <inheritdoc/>
    public byte[] Protect(ReadOnlySpan<byte> data, string purpose) => Run(data, purpose, protect: true);

    /// <inheritdoc/>
    public byte[] Unprotect(ReadOnlySpan<byte> protectedData, string purpose) =>
        Run(protectedData, purpose, protect: false);

    /// <summary>Protects or reads data through DPAPI; every buffer Windows gives back is wiped and freed.</summary>
    /// <param name="input">The bytes in.</param>
    /// <param name="purpose">The purpose, as entropy.</param>
    /// <param name="protect">Whether to protect (or read).</param>
    /// <returns>The bytes out.</returns>
    private static byte[] Run(ReadOnlySpan<byte> input, string purpose, bool protect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI is only on Windows.");
        }

        var entropy = Encoding.UTF8.GetBytes(purpose);
        var inHandle = GCHandle.Alloc(input.ToArray(), GCHandleType.Pinned);
        var entropyHandle = GCHandle.Alloc(entropy, GCHandleType.Pinned);
        var output = default(DataBlob);
        try
        {
            var inBlob = new DataBlob(input.Length, inHandle.AddrOfPinnedObject());
            var entropyBlob = new DataBlob(entropy.Length, entropyHandle.AddrOfPinnedObject());
            var done = protect
                ? CryptProtectData(ref inBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output)
                : CryptUnprotectData(
                    ref inBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
            if (!done)
            {
                throw new CryptographicException(Marshal.GetLastPInvokeError());
            }

            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(((byte[])inHandle.Target!).AsSpan());
            inHandle.Free();
            entropyHandle.Free();
            if (output.Data != IntPtr.Zero)
            {
                Marshal.Copy(new byte[output.Size], 0, output.Data, output.Size);
                LocalFree(output.Data);
            }
        }
    }

    /// <summary>Protects data (<c>CryptProtectData</c>).</summary>
    /// <param name="dataIn">The data.</param>
    /// <param name="description">A description; none.</param>
    /// <param name="entropy">Extra entropy: the purpose.</param>
    /// <param name="reserved">Reserved.</param>
    /// <param name="prompt">No prompt.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="dataOut">The protected data, freed with <see cref="LocalFree"/>.</param>
    /// <returns>Whether it worked.</returns>
    [LibraryImport("crypt32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(
        ref DataBlob dataIn, string? description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags,
        out DataBlob dataOut);

    /// <summary>Reads protected data (<c>CryptUnprotectData</c>).</summary>
    /// <param name="dataIn">The protected data.</param>
    /// <param name="description">The description; not wanted.</param>
    /// <param name="entropy">The purpose it was protected with.</param>
    /// <param name="reserved">Reserved.</param>
    /// <param name="prompt">No prompt.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="dataOut">The data, freed with <see cref="LocalFree"/>.</param>
    /// <returns>Whether it worked.</returns>
    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(
        ref DataBlob dataIn, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags,
        out DataBlob dataOut);

    /// <summary>Frees memory Windows gave back.</summary>
    /// <param name="memory">The memory.</param>
    /// <returns><see cref="IntPtr.Zero"/> when it was freed.</returns>
    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);

    /// <summary>Windows' <c>DATA_BLOB</c>: a length and a pointer.</summary>
    /// <param name="Size">The number of bytes.</param>
    /// <param name="Data">Where they are.</param>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct DataBlob(int Size, IntPtr Data);
}
