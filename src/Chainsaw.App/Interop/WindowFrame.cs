using System.Runtime.InteropServices;

namespace Chainsaw.App.Interop;

/// <summary>
/// Asks the Desktop Window Manager to draw the main window's frame as Gawk's is drawn: rounded corners, a dark frame
/// and a thin border in the theme's strong border colour (decided 2026-11-07).
/// </summary>
/// <remarks>
/// The window draws its own title bar through <c>WindowChrome</c>, so Windows would otherwise give it square corners.
/// Rounded corners and the border colour need Windows 11; on older versions the calls fail harmlessly and the window
/// keeps square corners.
/// </remarks>
internal static partial class WindowFrame
{
    /// <summary>The attribute that asks for a dark frame (DWMWA_USE_IMMERSIVE_DARK_MODE).</summary>
    private const int UseImmersiveDarkMode = 20;

    /// <summary>The attribute for the corner preference (DWMWA_WINDOW_CORNER_PREFERENCE).</summary>
    private const int WindowCornerPreference = 33;

    /// <summary>The attribute for the border colour (DWMWA_BORDER_COLOR).</summary>
    private const int BorderColor = 34;

    /// <summary>The corner preference for Windows 11's normal rounding (DWMWCP_ROUND).</summary>
    private const int CornerRound = 2;

    /// <summary>The border colour as a COLORREF (0x00BBGGRR): <c>Brush.BorderStrong</c>, #45474A.</summary>
    private const int BorderStrong = 0x004A4745;

    /// <summary>Asks for rounded corners, a dark frame and the strong border colour on the given window.</summary>
    /// <param name="hwnd">The window's handle, known once the window's source is initialised.</param>
    public static void ApplyRoundedDarkFrame(nint hwnd)
    {
        var enabled = 1;
        _ = DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref enabled, sizeof(int));

        var corner = CornerRound;
        _ = DwmSetWindowAttribute(hwnd, WindowCornerPreference, ref corner, sizeof(int));

        var border = BorderStrong;
        _ = DwmSetWindowAttribute(hwnd, BorderColor, ref border, sizeof(int));
    }

    /// <summary>Sets one attribute of a window's frame; the result is ignored, since a failure only keeps the default.</summary>
    /// <param name="hwnd">The window's handle.</param>
    /// <param name="attribute">The attribute to set.</param>
    /// <param name="value">The attribute's value.</param>
    /// <param name="size">The value's size in bytes.</param>
    /// <returns>An HRESULT.</returns>
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
