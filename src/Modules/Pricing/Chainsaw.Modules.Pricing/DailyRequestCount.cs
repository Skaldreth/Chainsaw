using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Chainsaw.Modules.Pricing;

/// <summary>
/// How many requests went to a source on one day, kept in a small file in the application data folder when one is
/// given, so that the Riksbank's daily limit is counted across restarts of Chainsaw on this computer (the review of
/// etapp 14: the count was only since Chainsaw started). Computers that share an exit still share the limit without
/// knowing it, which the answer that the limit is reached says.
/// </summary>
/// <remarks>Not thread-safe on its own; the caller holds the fiat gate.</remarks>
internal sealed class DailyRequestCount
{
    /// <summary>The file the count is kept in, or <see langword="null"/> to count in memory only.</summary>
    private readonly string? _path;

    /// <summary>The day counted.</summary>
    private DateOnly _day;

    /// <summary>The requests on <see cref="_day"/>.</summary>
    private int _count;

    /// <summary>Creates the count, reading the file when there is one.</summary>
    /// <param name="path">The file, or <see langword="null"/> to count in memory only.</param>
    public DailyRequestCount(string? path)
    {
        _path = path;
        if (path is null)
        {
            return;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllBytes(path));
            if (stored is not null && stored.Count >= 0 && DateOnly.TryParseExact(
                stored.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                (_day, _count) = (day, stored.Count);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // No file yet, or one that cannot be read: the count starts at zero, as before it was kept.
        }
    }

    /// <summary>Gives how many requests went out on a day.</summary>
    /// <param name="today">The day.</param>
    /// <returns>
    /// The count; zero for a later day than the one counted. A day before it (the clock was set back) counts as the
    /// day counted, so that setting the clock back never resets the limit (the review of round 5, M8).
    /// </returns>
    public int On(DateOnly today)
    {
        Check(today);
        return today <= _day ? _count : 0;
    }

    /// <summary>Counts one request on a day, and keeps the count in the file.</summary>
    /// <param name="today">The day.</param>
    public void Add(DateOnly today)
    {
        Check(today);
        _count = On(today) + 1;
        _day = today > _day ? today : _day;
        if (_path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(
                new Stored(_day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), _count)));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The count in memory still holds this session to the limit.
        }
    }

    /// <summary>
    /// Forgets a counted day more than a day after today, which would keep the Riksbank closed until that date: the
    /// clock was wrong when it was counted, or the file was changed (the review of round 5, I-b). Said in the log.
    /// </summary>
    /// <param name="today">The day now.</param>
    private void Check(DateOnly today)
    {
        if (_day <= today.AddDays(1))
        {
            return;
        }

        Trace.TraceWarning(
            "The Riksbank's daily count was for {0:yyyy-MM-dd}, after today {1:yyyy-MM-dd}; it starts again.",
            _day,
            today);
        (_day, _count) = (today, 0);
    }

    /// <summary>The count as stored.</summary>
    /// <param name="Day">The day, as <c>yyyy-MM-dd</c> in the source's calendar.</param>
    /// <param name="Count">The requests that day.</param>
    private sealed record Stored(string Day, int Count);
}
