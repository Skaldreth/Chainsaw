using System.Runtime.InteropServices;
using System.Windows;
using Chainsaw.App.ViewModels;
using Chainsaw.App.Views;

namespace Chainsaw.App.Interop;

/// <summary>
/// Shows the ways a hit is said from the settings (sketch BV4a): the message in the middle of the screen, as the
/// background process shows it, and the coin swoosh, played from memory on a thread of its own. One message at a
/// time.
/// </summary>
/// <param name="show">What the preview's <em>Visa</em> does: opens Bevakning.</param>
public sealed partial class WpfAlertPreview(Action show) : IAlertPreview
{
    /// <summary><c>SND_NODEFAULT</c>: no system sound in its place.</summary>
    private const uint NoDefault = 0x2;

    /// <summary><c>SND_MEMORY</c>: the sound is in memory.</summary>
    private const uint FromMemory = 0x4;

    /// <summary>The sound, read once from the program.</summary>
    private static readonly Lazy<byte[]> Wave = new(Load);

    /// <summary>The message shown now, or <see langword="null"/>.</summary>
    private AlertCardWindow? _shown;

    /// <inheritdoc/>
    public void ShowMessage()
    {
        if (_shown is not null)
        {
            _shown.Activate();
            return;
        }

        var card = new AlertCard(AlertCardSource.Preview, DateTimeOffset.Now);
        _shown = new AlertCardWindow(card, show) { Owner = Application.Current.MainWindow };
        _shown.Closed += (_, _) => _shown = null;
        _shown.Show();
    }

    /// <inheritdoc/>
    public void PlaySound()
    {
        if (Wave.Value.Length > 0)
        {
            _ = Task.Run(() => PlaySoundW(Wave.Value, 0, FromMemory | NoDefault));
        }
    }

    /// <inheritdoc/>
    public void CloseMessage() => _shown?.Close();

    /// <summary>Reads the sound from the program's resources.</summary>
    /// <returns>The WAV file's bytes, or none.</returns>
    private static byte[] Load()
    {
        using var stream = typeof(WpfAlertPreview).Assembly.GetManifestResourceStream("Chainsaw.App.coin-swoosh.wav");
        if (stream is null)
        {
            return [];
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>Plays a sound to its end.</summary>
    /// <param name="sound">The WAV file in memory.</param>
    /// <param name="module">None.</param>
    /// <param name="flags">How.</param>
    /// <returns>Whether it played.</returns>
    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySoundW(byte[] sound, nint module, uint flags);
}
