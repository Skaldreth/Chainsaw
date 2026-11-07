using System.Globalization;

namespace Chainsaw.App.Field;

/// <summary>
/// A filled outline made of straight lines, read from SVG path data, that tells whether a point is inside it. Only
/// the absolute commands M, L, H, V and Z are read, which is all the chain's joint uses; anything else is refused, so
/// that a changed drawing never draws something else silently.
/// </summary>
public sealed class PathPolygons
{
    /// <summary>The closed outlines, each as x and y pairs.</summary>
    private readonly List<double[]> _rings;

    /// <summary>Creates the outline from its rings.</summary>
    /// <param name="rings">The closed outlines.</param>
    private PathPolygons(List<double[]> rings) => _rings = rings;

    /// <summary>Gets how many closed outlines the path has.</summary>
    public int RingCount => _rings.Count;

    /// <summary>Reads SVG path data.</summary>
    /// <param name="data">The path data, for example <c>M0 0 10 0 10 10Z</c>.</param>
    /// <returns>The outline.</returns>
    /// <exception cref="FormatException">The data uses another command or is not well formed.</exception>
    public static PathPolygons Parse(string data)
    {
        var reader = new PathReader(data);
        var rings = new List<double[]>();
        var current = new List<double>();
        var command = ' ';
        while (reader.Next(ref command, out var closed) is { } token)
        {
            if (closed)
            {
                Close(rings, current);
            }

            if (command != 'M' && current.Count == 0)
            {
                throw new FormatException("A path must start with M.");
            }

            switch (command)
            {
                case 'M':
                    Close(rings, current);
                    current.Add(token);
                    current.Add(reader.Number());
                    command = 'L';
                    break;
                case 'L':
                    current.Add(token);
                    current.Add(reader.Number());
                    break;
                case 'H':
                    current.Add(token);
                    current.Add(current[^2]);
                    break;
                case 'V':
                    current.Add(current[^2]);
                    current.Add(token);
                    break;
                default:
                    throw new FormatException($"The path command '{command}' is not supported.");
            }
        }

        Close(rings, current);
        return new PathPolygons(rings);
    }

    /// <summary>Joins two outlines into one, filled together.</summary>
    /// <param name="other">The other outline.</param>
    /// <returns>An outline with the rings of both.</returns>
    public PathPolygons Union(PathPolygons other) => new([.. _rings, .. other._rings]);

    /// <summary>
    /// Tells whether a point is inside, by the non-zero winding rule (the rule a canvas fills by): the rings are
    /// counted with their direction, so a ring that runs against another cuts a hole in it.
    /// </summary>
    /// <param name="x">The point's horizontal position.</param>
    /// <param name="y">The point's vertical position.</param>
    /// <returns><see langword="true"/> when the point is filled.</returns>
    public bool Contains(double x, double y)
    {
        var winding = 0;
        foreach (var ring in _rings)
        {
            var count = ring.Length / 2;
            for (var i = 0; i < count; i++)
            {
                var (x0, y0) = (ring[2 * i], ring[2 * i + 1]);
                var next = (i + 1) % count;
                var (x1, y1) = (ring[2 * next], ring[2 * next + 1]);
                if (y0 <= y && y1 > y && Side(x0, y0, x1, y1, x, y) > 0)
                {
                    winding++;
                }
                else if (y0 > y && y1 <= y && Side(x0, y0, x1, y1, x, y) < 0)
                {
                    winding--;
                }
            }
        }

        return winding != 0;
    }

    /// <summary>Tells on which side of the line from one point to another a third point lies.</summary>
    /// <param name="x0">The line's start, horizontally.</param>
    /// <param name="y0">The line's start, vertically.</param>
    /// <param name="x1">The line's end, horizontally.</param>
    /// <param name="y1">The line's end, vertically.</param>
    /// <param name="x">The point, horizontally.</param>
    /// <param name="y">The point, vertically.</param>
    /// <returns>Positive on the left, negative on the right, zero on the line.</returns>
    private static double Side(double x0, double y0, double x1, double y1, double x, double y) =>
        (x1 - x0) * (y - y0) - (x - x0) * (y1 - y0);

    /// <summary>Ends the outline being read, keeping it when it has at least three corners.</summary>
    /// <param name="rings">The outlines read so far.</param>
    /// <param name="current">The corners of the outline being read; emptied.</param>
    private static void Close(List<double[]> rings, List<double> current)
    {
        if (current.Count >= 6)
        {
            rings.Add([.. current]);
        }

        current.Clear();
    }

    /// <summary>Reads the commands and numbers of path data one at a time.</summary>
    /// <param name="data">The path data.</param>
    private sealed class PathReader(string data)
    {
        /// <summary>Where the reader is in the data.</summary>
        private int _position;

        /// <summary>
        /// Reads the next number, after any command letters. A Z is told through <paramref name="closed"/>; another
        /// command letter changes <paramref name="command"/>.
        /// </summary>
        /// <param name="command">The command in force; changed when a letter is read.</param>
        /// <param name="closed">Whether a Z came before the number, which ends the ring being read.</param>
        /// <returns>The number, or <see langword="null"/> at the end.</returns>
        public double? Next(ref char command, out bool closed)
        {
            closed = false;
            while (true)
            {
                SkipSeparators();
                if (_position >= data.Length)
                {
                    return null;
                }

                var character = data[_position];
                if (!char.IsLetter(character))
                {
                    return Number();
                }

                _position++;
                if (character is 'Z' or 'z')
                {
                    closed = true;
                }
                else
                {
                    command = character;
                }
            }
        }

        /// <summary>Reads a number.</summary>
        /// <returns>The number.</returns>
        /// <exception cref="FormatException">No number stands here.</exception>
        public double Number()
        {
            SkipSeparators();
            var start = _position;
            if (_position < data.Length && data[_position] is '-' or '+')
            {
                _position++;
            }

            while (_position < data.Length && (char.IsDigit(data[_position]) || data[_position] == '.'))
            {
                _position++;
            }

            if (!double.TryParse(
                data.AsSpan(start, _position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException($"No number stands at position {start} of the path.");
            }

            return value;
        }

        /// <summary>Skips spaces and commas.</summary>
        private void SkipSeparators()
        {
            while (_position < data.Length && (char.IsWhiteSpace(data[_position]) || data[_position] == ','))
            {
                _position++;
            }
        }
    }
}
