using System.Text.Json;

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

    /// <summary>Reads a JSON file; a missing or unreadable file gives nothing, so that it never stops Chainsaw.</summary>
    /// <typeparam name="T">What the file holds.</typeparam>
    /// <param name="path">The file.</param>
    /// <param name="options">How the file is written.</param>
    /// <returns>What it holds, or <see langword="null"/> when it is missing or cannot be read.</returns>
    public static T? ReadJson<T>(string path, JsonSerializerOptions options)
        where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(ReadShared(path), options) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes a JSON file in one step (<see cref="WriteReplacing"/>), saying whether it was written.</summary>
    /// <typeparam name="T">What the file holds.</typeparam>
    /// <param name="path">The file.</param>
    /// <param name="value">What it holds.</param>
    /// <param name="options">How it is written.</param>
    /// <returns><see langword="true"/> when it was written.</returns>
    /// <remarks>The caller lets one write at a time, as for <see cref="WriteReplacing"/>.</remarks>
    public static bool TryWriteJson<T>(string path, T value, JsonSerializerOptions options)
    {
        try
        {
            WriteReplacing(path, JsonSerializer.Serialize(value, options));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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
