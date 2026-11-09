using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Controls;

/// <summary>
/// Shows a written file in Windows' Explorer with the file chosen. Explorer is started with the path as one argument,
/// never through the shell, so that a path cannot be read as a command.
/// </summary>
public sealed class WpfFolderView : IFolderView
{
    /// <inheritdoc />
    public void ShowInFolder(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            return;
        }

        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        start.ArgumentList.Add("/select," + path);
        try
        {
            using var _ = Process.Start(start);
        }
        catch (Win32Exception)
        {
            // Explorer could not be started; the toast's text still names the file.
        }
    }
}
