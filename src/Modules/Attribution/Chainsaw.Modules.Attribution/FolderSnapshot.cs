namespace Chainsaw.Modules.Attribution;

/// <summary>
/// What a folder of lists holds, read again only when the folder has changed (its time of last change), so that a
/// lookup per address costs one look at the folder rather than a reading of every description, and a list added or
/// removed outside Chainsaw is still noticed without a restart (the review of etapp 8, M5).
/// </summary>
/// <typeparam name="T">What is read from the folder.</typeparam>
/// <param name="directory">The folder.</param>
/// <param name="read">Reads the folder.</param>
/// <remarks>Not thread-safe on its own; the caller holds its lock.</remarks>
internal sealed class FolderSnapshot<T>(string directory, Func<IReadOnlyList<T>> read)
{
    /// <summary>What was read, with the folder's time of last change then, or <see langword="null"/>.</summary>
    private (DateTime Stamp, IReadOnlyList<T> Items)? _current;

    /// <summary>Gives what the folder holds, reading it again when it has changed.</summary>
    /// <returns>The items.</returns>
    public IReadOnlyList<T> Get()
    {
        var stamp = Directory.Exists(directory) ? Directory.GetLastWriteTimeUtc(directory) : DateTime.MinValue;
        if (_current is { } current && current.Stamp == stamp)
        {
            return current.Items;
        }

        var items = read();
        _current = (stamp, items);
        return items;
    }

    /// <summary>Forgets what was read, after Chainsaw itself changed the folder.</summary>
    public void Invalidate() => _current = null;
}
