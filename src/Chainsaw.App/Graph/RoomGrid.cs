namespace Chainsaw.App.Graph;

/// <summary>
/// The room taken on the surface, in a grid of cells, so that a rectangle is only compared with what is near it:
/// placing the amounts (<see cref="AmountLayout"/>) and drawing them stay quick on a large graph while a node is
/// dragged (principle 6), instead of comparing every amount with every other.
/// </summary>
public sealed class RoomGrid
{
    /// <summary>The side of a cell when none is given, in the surface's units.</summary>
    public const double DefaultCell = 160;

    /// <summary>The rectangles in each cell they reach into.</summary>
    private readonly Dictionary<(int Column, int Row), List<GraphRect>> _cells = [];

    /// <summary>The side of a cell.</summary>
    private readonly double _cell;

    /// <summary>Creates an empty grid.</summary>
    /// <param name="cell">The side of a cell, larger than zero.</param>
    public RoomGrid(double cell = DefaultCell)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cell);
        _cell = cell;
    }

    /// <summary>Gets how many rectangles have taken room.</summary>
    public int Count { get; private set; }

    /// <summary>Takes a rectangle's room.</summary>
    /// <param name="rect">The rectangle.</param>
    public void Add(GraphRect rect)
    {
        foreach (var cell in CellsOf(rect))
        {
            if (!_cells.TryGetValue(cell, out var list))
            {
                list = [];
                _cells[cell] = list;
            }

            list.Add(rect);
        }

        Count++;
    }

    /// <summary>Says whether a rectangle overlaps or touches any room taken.</summary>
    /// <param name="rect">The rectangle.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool Touches(GraphRect rect) =>
        CellsOf(rect).Any(cell => _cells.TryGetValue(cell, out var list) && list.Any(rect.Intersects));

    /// <summary>
    /// Says whether a point lies in or on any room taken, looking in its one cell only: the lines' routing asks this
    /// forty times for every curve it tries (<see cref="LineRouting.Route"/>).
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool Holds(GraphPoint point)
    {
        if (!_cells.TryGetValue(CellOf(point), out var list))
        {
            return false;
        }

        foreach (var rect in list)
        {
            if (rect.Contains(point))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gives back the room a rectangle took, for one that moved; a rectangle never taken is ignored.</summary>
    /// <param name="rect">The rectangle, equal to the one taken.</param>
    public void Remove(GraphRect rect)
    {
        var found = false;
        foreach (var cell in CellsOf(rect))
        {
            if (_cells.TryGetValue(cell, out var list) && list.Remove(rect))
            {
                found = true;
                if (list.Count == 0)
                {
                    _cells.Remove(cell);
                }
            }
        }

        if (found)
        {
            Count--;
        }
    }

    /// <summary>Takes a rectangle's room when it touches no room taken.</summary>
    /// <param name="rect">The rectangle.</param>
    /// <returns><see langword="true"/> when it was free and is now taken.</returns>
    public bool TryAdd(GraphRect rect)
    {
        if (Touches(rect))
        {
            return false;
        }

        Add(rect);
        return true;
    }

    /// <summary>Gives the cell a point falls in.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The cell.</returns>
    public (int Column, int Row) CellOf(GraphPoint point) => (Index(point.X), Index(point.Y));

    /// <summary>Gives the cells a rectangle reaches into.</summary>
    /// <param name="rect">The rectangle.</param>
    /// <returns>The cells.</returns>
    public IEnumerable<(int Column, int Row)> CellsOf(GraphRect rect)
    {
        var (left, right) = (Index(rect.X), Index(rect.Right));
        var (top, bottom) = (Index(rect.Y), Index(rect.Bottom));
        for (var column = left; column <= right; column++)
        {
            for (var row = top; row <= bottom; row++)
            {
                yield return (column, row);
            }
        }
    }

    /// <summary>Gives the cell a coordinate falls in.</summary>
    /// <param name="value">The coordinate.</param>
    /// <returns>The cell's index.</returns>
    private int Index(double value) => (int)Math.Floor(value / _cell);
}
