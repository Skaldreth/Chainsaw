namespace Chainsaw.App.Settings;

/// <summary>
/// Reads and writes the small files Chainsaw keeps next to the vault (<c>settings.json</c>, <c>piles.json</c>) the
/// same way: a read never makes a write fail, and a write replaces the file in one step, so that a crash or a second
/// reader never sees half a file.
/// </summary>
public static class SettingsFile
{
    /// <summary>How many times the finished file is put in place before the write fails.</summary>
    private const int ReplaceAttempts = 5;

    /// <summary>The ending of the temporary file the text is written to before it is put in place.</summary>
    private const string TemporaryEnding = ".tmp";

    /// <summary>How long to wait before the file is put in place again.</summary>
    private static readonly TimeSpan ReplacePause = TimeSpan.FromMilliseconds(40);

    /// <summary>
    /// Reads a file while letting a write replace it at the same time, so that reading never makes a write fail.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The file's text.</returns>
    public static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Writes a file in one step: the text goes to a temporary file, which replaces the old one.</summary>
    /// <param name="path">The file.</param>
    /// <param name="text">The text.</param>
    /// <remarks>The caller lets one write at a time, so that two writes never meet in the temporary file.</remarks>
    public static void WriteReplacing(string path, string text)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + TemporaryEnding;
        File.WriteAllText(temporary, text);
        Replace(temporary, path);
    }

    /// <summary>
    /// Puts the written file in place of the old one. On Windows the old file cannot be replaced while someone else
    /// has it open (another read, an antivirus scan), so a short wait and a new attempt are tried before the write
    /// fails.
    /// </summary>
    /// <param name="temporary">The written file.</param>
    /// <param name="path">The file it replaces.</param>
    private static void Replace(string temporary, string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, path, overwrite: true);
                return;
            }
            catch (Exception exception) when (attempt < ReplaceAttempts
                && exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(ReplacePause);
            }
        }
    }
}
