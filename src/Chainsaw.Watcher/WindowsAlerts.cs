using System.Runtime.InteropServices;
using Chainsaw.Modules.Monitoring.Contracts;

namespace Chainsaw.Watcher;

/// <summary>
/// The notification in Windows: a notification from a tray icon (<c>Shell_NotifyIcon</c> with <c>NIF_INFO</c>), which
/// Windows 10 and 11 show as a toast without the program being installed as an app. The icon is there only while the
/// notification shows, on a thread of its own.
/// </summary>
internal sealed partial class WindowsNotification : IAlertChannel
{
    /// <summary>How long the icon stays, so that the notification can be read.</summary>
    private static readonly TimeSpan Shown = TimeSpan.FromSeconds(10);

    /// <inheritdoc/>
    public WatchAlerts Way => WatchAlerts.Windows;

    /// <inheritdoc/>
    public void Show()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var thread = new Thread(Run) { IsBackground = true, Name = "Chainsaw notification" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <summary>Makes a hidden window, shows the notification from its icon, waits, and takes it away.</summary>
    private static void Run()
    {
        var window = CreateWindowExW(0, "STATIC", "Chainsaw", 0, 0, 0, 0, 0, MessageOnly, 0, 0, 0);
        if (window == 0)
        {
            return;
        }

        var icon = ExtractIconW(0, Environment.ProcessPath ?? string.Empty, 0);
        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            Window = window,
            Id = 1,
            Flags = IconFlag | TipFlag | InfoFlag,
            Icon = icon,
            InfoFlags = InfoIcon,
        };
        data.SetTexts("Chainsaw", BackgroundNames.AlertDetail, BackgroundNames.AlertTitle);
        try
        {
            if (!ShellNotifyIconW(AddMessage, ref data))
            {
                return;
            }

            var until = DateTime.UtcNow + Shown;
            while (DateTime.UtcNow < until)
            {
                while (PeekMessageW(out var message, 0, 0, 0, RemoveMessage))
                {
                    TranslateMessage(ref message);
                    DispatchMessageW(ref message);
                }

                Thread.Sleep(100);
            }

            ShellNotifyIconW(DeleteMessage, ref data);
        }
        finally
        {
            if (icon > 1)
            {
                DestroyIcon(icon);
            }

            DestroyWindow(window);
        }
    }

    /// <summary>The parent of a window that only takes messages.</summary>
    private const nint MessageOnly = -3;

    /// <summary><c>NIM_ADD</c>.</summary>
    private const uint AddMessage = 0;

    /// <summary><c>NIM_DELETE</c>.</summary>
    private const uint DeleteMessage = 2;

    /// <summary><c>NIF_ICON</c>.</summary>
    private const uint IconFlag = 0x2;

    /// <summary><c>NIF_TIP</c>.</summary>
    private const uint TipFlag = 0x4;

    /// <summary><c>NIF_INFO</c>.</summary>
    private const uint InfoFlag = 0x10;

    /// <summary><c>NIIF_INFO</c>.</summary>
    private const uint InfoIcon = 0x1;

    /// <summary><c>PM_REMOVE</c>.</summary>
    private const uint RemoveMessage = 0x1;

    /// <summary>Adds, changes or removes a tray icon.</summary>
    /// <param name="message">What to do.</param>
    /// <param name="data">The icon.</param>
    /// <returns>Whether it worked.</returns>
    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShellNotifyIconW(uint message, ref NotifyIconData data);

    /// <summary>Makes a window.</summary>
    /// <param name="exStyle">Extended style.</param>
    /// <param name="className">The class.</param>
    /// <param name="name">The name.</param>
    /// <param name="style">The style.</param>
    /// <param name="x">Left.</param>
    /// <param name="y">Top.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="parent">The parent.</param>
    /// <param name="menu">The menu.</param>
    /// <param name="instance">The instance.</param>
    /// <param name="parameter">The parameter.</param>
    /// <returns>The window, or zero.</returns>
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(
        uint exStyle, string className, string name, uint style, int x, int y, int width, int height, nint parent,
        nint menu, nint instance, nint parameter);

    /// <summary>Removes a window.</summary>
    /// <param name="window">The window.</param>
    /// <returns>Whether it worked.</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    /// <summary>Takes a waiting message.</summary>
    /// <param name="message">The message.</param>
    /// <param name="window">Any window.</param>
    /// <param name="first">Any message.</param>
    /// <param name="last">Any message.</param>
    /// <param name="remove">Remove it.</param>
    /// <returns>Whether there was one.</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out WindowMessage message, nint window, uint first, uint last,
        uint remove);

    /// <summary>Translates keys.</summary>
    /// <param name="message">The message.</param>
    /// <returns>Whether it was.</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref WindowMessage message);

    /// <summary>Hands a message to its window.</summary>
    /// <param name="message">The message.</param>
    /// <returns>What the window gave.</returns>
    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(ref WindowMessage message);

    /// <summary>Takes the program's icon.</summary>
    /// <param name="instance">None.</param>
    /// <param name="file">The program.</param>
    /// <param name="index">The first icon.</param>
    /// <returns>The icon, or zero or one when there is none.</returns>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint ExtractIconW(nint instance, string file, uint index);

    /// <summary>Frees an icon.</summary>
    /// <param name="icon">The icon.</param>
    /// <returns>Whether it worked.</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    /// <summary>Windows' <c>MSG</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowMessage
    {
        /// <summary>The window.</summary>
        public nint Window;

        /// <summary>The message.</summary>
        public uint Message;

        /// <summary>The first parameter.</summary>
        public nint First;

        /// <summary>The second parameter.</summary>
        public nint Second;

        /// <summary>When.</summary>
        public uint Time;

        /// <summary>The cursor's x.</summary>
        public int X;

        /// <summary>The cursor's y.</summary>
        public int Y;

        /// <summary>Private.</summary>
        public uint Private;
    }

    /// <summary>Windows' <c>NOTIFYICONDATAW</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct NotifyIconData
    {
        /// <summary>The structure's size.</summary>
        public uint Size;

        /// <summary>The window.</summary>
        public nint Window;

        /// <summary>The icon's id.</summary>
        public uint Id;

        /// <summary>What is set.</summary>
        public uint Flags;

        /// <summary>The callback message; none.</summary>
        public uint CallbackMessage;

        /// <summary>The icon.</summary>
        public nint Icon;

        /// <summary>The tip.</summary>
        public fixed char Tip[128];

        /// <summary>The state.</summary>
        public uint State;

        /// <summary>The state's mask.</summary>
        public uint StateMask;

        /// <summary>The notification's text.</summary>
        public fixed char Info[256];

        /// <summary>The time-out (ignored by Windows since Vista).</summary>
        public uint Timeout;

        /// <summary>The notification's title.</summary>
        public fixed char InfoTitle[64];

        /// <summary>The notification's icon.</summary>
        public uint InfoFlags;

        /// <summary>The icon's id as a GUID; none.</summary>
        public Guid Item;

        /// <summary>The notification's own icon; none.</summary>
        public nint BalloonIcon;

        /// <summary>Sets the texts, each cut to its field.</summary>
        /// <param name="tip">The tip.</param>
        /// <param name="info">The notification's text.</param>
        /// <param name="title">The notification's title.</param>
        public void SetTexts(string tip, string info, string title)
        {
            fixed (char* field = Tip)
            {
                Copy(tip, field, 128);
            }

            fixed (char* field = Info)
            {
                Copy(info, field, 256);
            }

            fixed (char* field = InfoTitle)
            {
                Copy(title, field, 64);
            }
        }

        /// <summary>Copies a text into a field, ended by a zero.</summary>
        /// <param name="text">The text.</param>
        /// <param name="field">The field.</param>
        /// <param name="length">The field's length.</param>
        private static void Copy(string text, char* field, int length)
        {
            var count = Math.Min(text.Length, length - 1);
            for (var index = 0; index < count; index++)
            {
                field[index] = text[index];
            }

            field[count] = '\0';
        }
    }
}

/// <summary>
/// The sound: the coin swoosh with a rattle (<c>Sounds/coin-swoosh.wav</c>, made by
/// <c>tools/sounds/coin-swoosh.py</c>), played from memory on a thread of its own.
/// </summary>
internal sealed partial class CoinSound : IAlertChannel
{
    /// <summary><c>SND_NODEFAULT</c>: no system sound in its place.</summary>
    private const uint NoDefault = 0x2;

    /// <summary><c>SND_MEMORY</c>: the sound is in memory.</summary>
    private const uint FromMemory = 0x4;

    /// <summary>Gets the sound, read once from the program.</summary>
    public static byte[] Wave { get; } = Load();

    /// <inheritdoc/>
    public WatchAlerts Way => WatchAlerts.Sound;

    /// <inheritdoc/>
    public void Show()
    {
        if (!OperatingSystem.IsWindows() || Wave.Length == 0)
        {
            return;
        }

        var thread = new Thread(Play) { IsBackground = true, Name = "Chainsaw sound" };
        thread.Start();
    }

    /// <summary>Plays the sound to its end.</summary>
    private static unsafe void Play()
    {
        fixed (byte* sound = Wave)
        {
            PlaySoundW((nint)sound, 0, FromMemory | NoDefault);
        }
    }

    /// <summary>Reads the sound from the program's resources.</summary>
    /// <returns>The WAV file's bytes, or none.</returns>
    private static byte[] Load()
    {
        using var stream = typeof(CoinSound).Assembly.GetManifestResourceStream("Chainsaw.Watcher.coin-swoosh.wav");
        if (stream is null)
        {
            return [];
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>Plays a sound.</summary>
    /// <param name="sound">The WAV file in memory.</param>
    /// <param name="module">None.</param>
    /// <param name="flags">How.</param>
    /// <returns>Whether it played.</returns>
    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySoundW(nint sound, nint module, uint flags);
}

/// <summary>
/// The message in the middle of the screen, on top until it is closed (decided 2026-11-07). Its form is not sketched
/// yet, so it is a plain Windows message box and the settings do not offer it until the sketch is approved (TODO).
/// One shows at a time.
/// </summary>
internal sealed partial class ScreenMessage : IAlertChannel
{
    /// <summary><c>MB_ICONINFORMATION | MB_SETFOREGROUND | MB_TOPMOST</c>.</summary>
    private const uint Style = 0x40 | 0x10000 | 0x40000;

    /// <summary>Whether one shows now: 1, else 0.</summary>
    private int _showing;

    /// <inheritdoc/>
    public WatchAlerts Way => WatchAlerts.Screen;

    /// <inheritdoc/>
    public void Show()
    {
        if (!OperatingSystem.IsWindows() || Interlocked.Exchange(ref _showing, 1) == 1)
        {
            return;
        }

        var thread = new Thread(() =>
        {
            try
            {
                MessageBoxW(0, $"{BackgroundNames.AlertTitle}\n{BackgroundNames.AlertDetail}", "Chainsaw", Style);
            }
            finally
            {
                Volatile.Write(ref _showing, 0);
            }
        })
        {
            IsBackground = true,
            Name = "Chainsaw message",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <summary>Shows a message box.</summary>
    /// <param name="owner">None.</param>
    /// <param name="text">The text.</param>
    /// <param name="caption">The caption.</param>
    /// <param name="style">The style.</param>
    /// <returns>The button pressed.</returns>
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint owner, string text, string caption, uint style);
}
