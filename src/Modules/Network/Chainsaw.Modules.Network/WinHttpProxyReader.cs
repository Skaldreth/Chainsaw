using System.Runtime.InteropServices;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Reads the current user's proxy setting the way .NET's own system proxy does on Windows
/// (<c>WinHttpGetIEProxyConfigForCurrentUser</c>): automatic detection (WPAD), a script (PAC) and a proxy set by hand.
/// Automatic detection or a script makes the setting <see cref="WindowsProxyKind.Automatic"/> even when a proxy is
/// also set by hand, since Windows then asks the network first and anyone on it can answer. Off Windows there is no
/// setting.
/// </summary>
internal sealed partial class WinHttpProxyReader : IWindowsProxyReader
{
    /// <inheritdoc />
    public WindowsProxySetting Read()
    {
        if (!OperatingSystem.IsWindows())
        {
            return WindowsProxySetting.None;
        }

        var config = default(CurrentUserProxyConfig);
        if (!WinHttpGetIEProxyConfigForCurrentUser(ref config))
        {
            return WindowsProxySetting.None;
        }

        try
        {
            return WindowsProxyText.Setting(
                config.AutoDetect != 0, Text(config.AutoConfigUrl), Text(config.Proxy));
        }
        finally
        {
            Free(config.AutoConfigUrl);
            Free(config.Proxy);
            Free(config.ProxyBypass);
        }
    }

    /// <summary>Reads a string Windows gave back.</summary>
    /// <param name="pointer">The string, or zero.</param>
    /// <returns>The text, or <see langword="null"/>.</returns>
    private static string? Text(IntPtr pointer) => pointer == IntPtr.Zero ? null : Marshal.PtrToStringUni(pointer);

    /// <summary>Frees a string Windows gave back.</summary>
    /// <param name="pointer">The string, or zero.</param>
    private static void Free(IntPtr pointer)
    {
        if (pointer != IntPtr.Zero)
        {
            GlobalFree(pointer);
        }
    }

    /// <summary>Reads the current user's proxy setting (<c>WinHttpGetIEProxyConfigForCurrentUser</c>).</summary>
    /// <param name="config">The setting; its strings are freed with <see cref="GlobalFree"/>.</param>
    /// <returns>Whether it could be read.</returns>
    [LibraryImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinHttpGetIEProxyConfigForCurrentUser(ref CurrentUserProxyConfig config);

    /// <summary>Frees memory Windows gave back.</summary>
    /// <param name="memory">The memory.</param>
    /// <returns><see cref="IntPtr.Zero"/> when it was freed.</returns>
    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GlobalFree(IntPtr memory);

    /// <summary>Windows' <c>WINHTTP_CURRENT_USER_IE_PROXY_CONFIG</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CurrentUserProxyConfig
    {
        /// <summary>Whether automatic detection (WPAD) is on (a Windows <c>BOOL</c>).</summary>
        public int AutoDetect;

        /// <summary>The script's address (PAC), or zero.</summary>
        public IntPtr AutoConfigUrl;

        /// <summary>The proxies set by hand, or zero.</summary>
        public IntPtr Proxy;

        /// <summary>The addresses that go around the proxy, or zero.</summary>
        public IntPtr ProxyBypass;
    }
}
