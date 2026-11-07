using System.Text;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Reads the helper's stdout line by line. A line is bounded, so a helper that goes wrong cannot make Chainsaw hold
/// an endless line in memory.
/// </summary>
/// <param name="stream">The helper's stdout.</param>
internal sealed class TunnelOutput(Stream stream)
{
    /// <summary>The longest line that is accepted, in bytes; the helper's lines are a few dozen.</summary>
    internal const int MaximumLineBytes = 4096;

    /// <summary>The bytes read but not yet given as a line.</summary>
    private readonly byte[] _buffer = new byte[MaximumLineBytes + 1];

    /// <summary>How many bytes of <see cref="_buffer"/> are filled.</summary>
    private int _filled;

    /// <summary>Reads the next line, without its line break.</summary>
    /// <param name="cancellationToken">Stops the read where the stream allows it.</param>
    /// <returns>The line, or <see langword="null"/> when the stream has ended.</returns>
    /// <exception cref="InvalidDataException">The line is longer than <see cref="MaximumLineBytes"/>.</exception>
    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var end = Array.IndexOf(_buffer, (byte)'\n', 0, _filled);
            if (end >= 0)
            {
                var line = Encoding.UTF8.GetString(_buffer, 0, end).TrimEnd('\r');
                _filled -= end + 1;
                Array.Copy(_buffer, end + 1, _buffer, 0, _filled);
                return line;
            }

            if (_filled > MaximumLineBytes)
            {
                throw new InvalidDataException("The helper wrote a line that is too long.");
            }

            var read = await stream.ReadAsync(_buffer.AsMemory(_filled), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            _filled += read;
        }
    }
}
