using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace Chainsaw.App.Interop;

/// <summary>
/// Puts text on the clipboard so that Windows keeps it out of the clipboard history (Win+V) and never uploads it to
/// the cloud clipboard, which would send case data (addresses, txids, notes) to the cloud (principle 4). Every copy
/// Chainsaw makes goes through here, and <see cref="Guard"/> marks the copies the text boxes make themselves.
/// </summary>
public static class CaseClipboard
{
    /// <summary>The format whose DWORD 0 keeps a copy out of the clipboard history.</summary>
    private const string HistoryFormat = "CanIncludeInClipboardHistory";

    /// <summary>The format whose DWORD 0 keeps a copy from being synced to the cloud clipboard.</summary>
    private const string CloudFormat = "CanUploadToCloudClipboard";

    /// <summary>
    /// Marks every copy a text box or the note editor makes (they raise <see cref="DataObject.CopyingEvent"/>), once
    /// at start.
    /// </summary>
    public static void Guard() =>
        EventManager.RegisterClassHandler(
            typeof(UIElement), DataObject.CopyingEvent, new DataObjectCopyingEventHandler(OnCopying));

    /// <summary>Puts text on the clipboard, kept out of the history and the cloud.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="false"/> when another program holds the clipboard and nothing was copied.</returns>
    public static bool TrySetText(string text)
    {
        var data = new DataObject(DataFormats.UnicodeText, text);
        MarkPrivate(data);
        try
        {
            Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch (ExternalException)
        {
            // Another program holds the clipboard; nothing is lost but the copy.
            return false;
        }
    }

    /// <summary>Marks a copy (not a drag) made by a WPF control as private.</summary>
    /// <param name="sender">The control.</param>
    /// <param name="e">The copy.</param>
    private static void OnCopying(object sender, DataObjectCopyingEventArgs e)
    {
        if (!e.IsDragDrop)
        {
            MarkPrivate(e.DataObject);
        }
    }

    /// <summary>Adds the two formats that keep a copy out of the history and the cloud clipboard.</summary>
    /// <param name="data">The data on its way to the clipboard.</param>
    private static void MarkPrivate(IDataObject data)
    {
        data.SetData(HistoryFormat, Zero());
        data.SetData(CloudFormat, Zero());
    }

    /// <summary>Gives a DWORD 0, as Windows reads these formats.</summary>
    /// <returns>The value as a stream, which the clipboard takes as global memory.</returns>
    private static MemoryStream Zero() => new(BitConverter.GetBytes(0));
}
