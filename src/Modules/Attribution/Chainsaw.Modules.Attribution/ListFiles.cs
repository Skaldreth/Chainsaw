using System.Globalization;

namespace Chainsaw.Modules.Attribution;

/// <summary>
/// Reading and writing the lists kept in the application data folder (sanction lists and open sources), so that every
/// kind of list is read with a ceiling and written whole or not at all.
/// </summary>
internal static class ListFiles
{
    /// <summary>Reads a stream to its end, up to a ceiling.</summary>
    /// <param name="content">The stream.</param>
    /// <param name="maximumLength">The most bytes read.</param>
    /// <param name="tooLarge">Gives the exception thrown when the stream is longer.</param>
    /// <returns>The bytes.</returns>
    public static byte[] ReadAll(Stream content, int maximumLength, Func<Exception> tooLarge)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = content.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > maximumLength)
            {
                throw tooLarge();
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>Writes a file through a temporary file, so that it is either whole or not there.</summary>
    /// <param name="path">The file.</param>
    /// <param name="bytes">The content.</param>
    public static void WriteAtomically(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, path, overwrite: true);
    }
}
