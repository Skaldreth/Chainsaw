namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// Which units of one coin are traced (satoshi on Bitcoin): ranges of positions inside the coin, from 0 to its
/// amount. FIFO needs the positions, so that money that is followed keeps its place through the next transaction;
/// haircut only uses the amount and always gives a range from 0. It belongs to the UTXO model.
/// </summary>
/// <remarks>The ranges are sorted, do not overlap and do not touch; an empty set traces nothing.</remarks>
public sealed class TaintSet : IEquatable<TaintSet>
{
    /// <summary>The ranges, sorted and apart.</summary>
    private readonly TaintRange[] _ranges;

    /// <summary>Creates a set from ranges that are already sorted and apart.</summary>
    /// <param name="ranges">The ranges.</param>
    private TaintSet(TaintRange[] ranges)
    {
        _ranges = ranges;
        Units = ranges.Aggregate(0L, (sum, range) => checked(sum + range.Length));
    }

    /// <summary>Gets a set that traces nothing.</summary>
    public static TaintSet None { get; } = new([]);

    /// <summary>Gets the ranges, sorted and apart.</summary>
    public IReadOnlyList<TaintRange> Ranges => _ranges;

    /// <summary>Gets how many units are traced.</summary>
    public long Units { get; }

    /// <summary>Gets whether nothing is traced.</summary>
    public bool IsEmpty => _ranges.Length == 0;

    /// <summary>Gets the first position after the last traced unit; 0 when nothing is traced.</summary>
    public long End => _ranges.Length == 0 ? 0 : _ranges[^1].End;

    /// <summary>Gives a set that traces the first units of a coin.</summary>
    /// <param name="units">How many; zero gives <see cref="None"/>.</param>
    /// <returns>The range from 0 to the amount.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The amount is negative.</exception>
    public static TaintSet FirstPart(long units)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(units);
        return units == 0 ? None : new TaintSet([new TaintRange(0, units)]);
    }

    /// <summary>Gives a set of the given ranges, sorted and joined where they overlap or touch.</summary>
    /// <param name="ranges">The ranges, in any order.</param>
    /// <returns>The set.</returns>
    public static TaintSet Of(IEnumerable<TaintRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        var sorted = ranges.Where(range => range.Length > 0).OrderBy(range => range.Start).ToList();
        var joined = new List<TaintRange>(sorted.Count);
        foreach (var range in sorted)
        {
            if (joined.Count > 0 && range.Start <= joined[^1].End)
            {
                joined[^1] = new TaintRange(joined[^1].Start, Math.Max(joined[^1].End, range.End));
            }
            else
            {
                joined.Add(range);
            }
        }

        return joined.Count == 0 ? None : new TaintSet([.. joined]);
    }

    /// <summary>Moves every range by an offset, as when a coin is laid after others in a transaction.</summary>
    /// <param name="offset">How far; not negative.</param>
    /// <returns>The moved set.</returns>
    public TaintSet Shift(long offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        return offset == 0
            ? this
            : new TaintSet([.. _ranges.Select(range => new TaintRange(range.Start + offset, range.End + offset))]);
    }

    /// <summary>
    /// Takes the part of the set between two positions and moves it to start at 0, as when a transaction's laid-out
    /// inputs are cut into an output.
    /// </summary>
    /// <param name="start">The first position.</param>
    /// <param name="end">The position after the last.</param>
    /// <returns>The part, starting at 0.</returns>
    public TaintSet Cut(long start, long end)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
        var part = _ranges
            .Where(range => range.End > start && range.Start < end)
            .Select(range => new TaintRange(Math.Max(range.Start, start) - start, Math.Min(range.End, end) - start))
            .ToArray();
        return part.Length == 0 ? None : new TaintSet(part);
    }

    /// <inheritdoc/>
    public bool Equals(TaintSet? other) => other is not null && _ranges.SequenceEqual(other._ranges);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as TaintSet);

    /// <inheritdoc/>
    public override int GetHashCode() => _ranges.Aggregate(0, (hash, range) => HashCode.Combine(hash, range));

    /// <summary>Writes the ranges, for example <c>[0, 500) [800, 1000)</c>.</summary>
    /// <returns>The ranges, or <c>none</c>.</returns>
    public override string ToString() =>
        _ranges.Length == 0 ? "none" : string.Join(' ', _ranges.Select(range => range.ToString()));
}

/// <summary>
/// A range of positions inside a coin, from <see cref="Start"/> up to but not including <see cref="End"/>.
/// </summary>
/// <param name="Start">The first position.</param>
/// <param name="End">The position after the last.</param>
public readonly record struct TaintRange(long Start, long End)
{
    /// <summary>Gets the first position; not negative.</summary>
    public long Start { get; } = Start >= 0
        ? Start
        : throw new ArgumentOutOfRangeException(nameof(Start), "A position is not negative.");

    /// <summary>Gets the position after the last; not before the start.</summary>
    public long End { get; } = End >= Start
        ? End
        : throw new ArgumentOutOfRangeException(nameof(End), "A range ends after it starts.");

    /// <summary>Gets how many units the range holds.</summary>
    public long Length => End - Start;

    /// <summary>Writes the range as <c>[start, end)</c>.</summary>
    /// <returns>The range.</returns>
    public override string ToString() => FormattableString.Invariant($"[{Start}, {End})");
}
