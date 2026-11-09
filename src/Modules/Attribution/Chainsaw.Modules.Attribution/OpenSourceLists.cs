using System.Security.Cryptography;
using System.Text.Json;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution;

/// <summary>
/// The imported open sources in the application data folder, kept as the sanction lists are: the exact bytes the user
/// imported, named by the source and the SHA-256, next to a small description; a source is read again, and its hash
/// checked, before any label is taken from it.
/// </summary>
/// <param name="options">Where the sources live.</param>
/// <param name="clock">The clock for when a source is imported.</param>
internal sealed class OpenSourceLists(AttributionOptions options, IClock clock) : IOpenSourceLists
{
    /// <summary>The largest file that is read (Ransomwhere's data set is some megabytes).</summary>
    public const int MaximumLength = 64 * 1024 * 1024;

    /// <summary>The extension of a stored source.</summary>
    private const string DataExtension = ".data";

    /// <summary>The extension of a stored source's description.</summary>
    private const string DescriptionExtension = ".json";

    /// <summary>The sources Chainsaw reads, each with the start of its files' names and its reader.</summary>
    private static readonly IReadOnlyList<SourceKind> Kinds =
    [
        new(RansomwhereParser.ListName, "ransomwhere-", RansomwhereParser.Recognises, RansomwhereParser.Parse),
    ];

    /// <summary>Guards the files and the loaded sources.</summary>
    private readonly Lock _gate = new();

    /// <summary>The stored sources, read again when the folder has changed; created on first use.</summary>
    private FolderSnapshot<OpenSourceListInfo>? _listing;

    /// <summary>The sources whose file has lost its hash, not read again until imported or removed.</summary>
    private readonly HashSet<string> _broken = new(StringComparer.Ordinal);

    /// <summary>The sources loaded for lookups, by their hash.</summary>
    private readonly Dictionary<string, ILookup<ChainAddress, OpenSourceEntry>> _loaded = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public IReadOnlyList<OpenSourceListInfo> CurrentLists =>
    [
        .. List().GroupBy(list => list.Name, StringComparer.Ordinal).Select(group => group.First()),
    ];

    /// <inheritdoc/>
    public OpenSourceListInfo Import(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var bytes = ListFiles.ReadAll(
            content, MaximumLength, () => new OpenSourceListException(OpenSourceListError.TooLarge));
        var (kind, parsed) = Parse(bytes);

        // The newest data of a source is the one used, so a date after today (a day's slack for time zones) would
        // shadow every later file of the same source.
        if (parsed.DataDate > DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddDays(1))
        {
            throw new OpenSourceListException(OpenSourceListError.FutureDate);
        }

        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        lock (_gate)
        {
            if (Describe(kind, sha, repair: false) is { } existing && !_broken.Contains(sha))
            {
                return existing;
            }

            _broken.Remove(sha);
            _loaded.Remove(sha);
            var info = new OpenSourceListInfo(
                parsed.Name, parsed.License, sha, parsed.DataDate, clock.UtcNow, Counts(parsed), parsed.Skipped);
            Directory.CreateDirectory(options.OpenSourceDirectory);
            ListFiles.WriteAtomically(PathFor(kind, sha, DataExtension), bytes);
            WriteDescription(kind, info);
            _listing?.Invalidate();
            return info;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<OpenSourceListInfo> List()
    {
        lock (_gate)
        {
            _listing ??= new FolderSnapshot<OpenSourceListInfo>(options.OpenSourceDirectory, ReadListing);
            return _listing.Get();
        }
    }

    /// <summary>Reads the stored sources from the folder.</summary>
    /// <returns>The sources, the newest data first.</returns>
    private IReadOnlyList<OpenSourceListInfo> ReadListing()
    {
        if (!Directory.Exists(options.OpenSourceDirectory))
        {
            return [];
        }

        return
        [
        .. Kinds.SelectMany(kind => Directory
                .EnumerateFiles(options.OpenSourceDirectory, kind.Prefix + "*" + DataExtension)
                .Select(path => Path.GetFileNameWithoutExtension(path)[kind.Prefix.Length..])
                .Where(IsHash)
                .Select(sha => Describe(kind, sha, repair: true)))
            .OfType<OpenSourceListInfo>()
            .OrderByDescending(info => info.DataDate)
            .ThenByDescending(info => info.ImportedUtc)
            .ThenBy(info => info.Sha256, StringComparer.Ordinal),
        ];
    }

    /// <inheritdoc/>
    public OpenSourceLookup Find(IReadOnlyCollection<ChainAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var usable = new List<(OpenSourceListInfo Info, ILookup<ChainAddress, OpenSourceEntry> Entries)>();
        var unusable = new List<OpenSourceListInfo>();
        var missing = new List<OpenSourceListInfo>();
        foreach (var list in CurrentLists)
        {
            switch (TryLoaded(list))
            {
                case { Entries: { } entries }:
                    usable.Add((list, entries));
                    break;
                case { IsMissing: true }:
                    missing.Add(list);
                    break;
                default:
                    unusable.Add(list);
                    break;
            }
        }

        var matches = new Dictionary<ChainAddress, IReadOnlyList<OpenSourceMatch>>();
        foreach (var address in usable.Count == 0 ? [] : addresses.Distinct())
        {
            List<OpenSourceMatch> found =
            [
                .. usable.Where(list => list.Entries.Contains(address))
                    .Select(list => new OpenSourceMatch(list.Info, [.. list.Entries[address]])),
            ];
            if (found.Count > 0)
            {
                matches[address] = found;
            }
        }

        return new OpenSourceLookup([.. usable.Select(list => list.Info)], unusable, matches) { Missing = missing };
    }

    /// <summary>
    /// Loads a source for lookups, or gives none when its file has lost its hash; such a source is remembered as
    /// broken, so that it is not read and hashed again at every lookup, until it is imported again or removed.
    /// </summary>
    /// <param name="info">The source.</param>
    /// <returns>Its entries by address, or whether it is missing rather than broken.</returns>
    private (ILookup<ChainAddress, OpenSourceEntry>? Entries, bool IsMissing) TryLoaded(OpenSourceListInfo info)
    {
        lock (_gate)
        {
            if (_broken.Contains(info.Sha256))
            {
                return (null, false);
            }

            try
            {
                return (Loaded(info), false);
            }
            catch (OpenSourceListException exception) when (exception.Error == OpenSourceListError.Tampered)
            {
                _broken.Add(info.Sha256);
                return (null, false);
            }
            catch (OpenSourceListException exception) when (exception.Error == OpenSourceListError.Missing)
            {
                // Not remembered as broken: the file may be back, and the listing follows the folder.
                return (null, true);
            }
        }
    }

    /// <inheritdoc/>
    public bool Remove(string sha256)
    {
        ArgumentNullException.ThrowIfNull(sha256);
        if (!IsHash(sha256))
        {
            return false;
        }

        lock (_gate)
        {
            var removed = false;
            foreach (var kind in Kinds.Where(kind => File.Exists(PathFor(kind, sha256, DataExtension))))
            {
                File.Delete(PathFor(kind, sha256, DataExtension));
                File.Delete(PathFor(kind, sha256, DescriptionExtension));
                removed = true;
            }

            _loaded.Remove(sha256);
            _broken.Remove(sha256);
            _listing?.Invalidate();
            return removed;
        }
    }

    /// <summary>Tells which source a file is by its content, and reads it.</summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <returns>The source and what it holds.</returns>
    /// <exception cref="OpenSourceListException">The file is not a source Chainsaw reads.</exception>
    private static (SourceKind Kind, ParsedOpenSource Parsed) Parse(byte[] bytes)
    {
        var kind = Kinds.FirstOrDefault(each => each.Recognises(bytes))
            ?? throw new OpenSourceListException(OpenSourceListError.NotKnown);
        return (kind, kind.Parse(bytes));
    }

    /// <summary>Gives a source's entries by address, reading it again and checking its hash the first time.</summary>
    /// <param name="info">The source.</param>
    /// <returns>The entries by address.</returns>
    /// <exception cref="OpenSourceListException"><see cref="OpenSourceListError.Tampered"/>.</exception>
    private ILookup<ChainAddress, OpenSourceEntry> Loaded(OpenSourceListInfo info)
    {
        lock (_gate)
        {
            if (_loaded.TryGetValue(info.Sha256, out var loaded))
            {
                return loaded;
            }

            var kind = Kinds.First(each => each.Name == info.Name);
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(PathFor(kind, info.Sha256, DataExtension));
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                // Gone since the folder was read (removed meanwhile): not changed, and the next listing shows it.
                throw new OpenSourceListException(OpenSourceListError.Missing, exception);
            }

            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != info.Sha256)
            {
                throw new OpenSourceListException(OpenSourceListError.Tampered);
            }

            foreach (var stale in _loaded.Keys.ToList())
            {
                _loaded.Remove(stale);
            }

            loaded = kind.Parse(bytes).Entries.ToLookup(entry => entry.Address);
            _loaded[info.Sha256] = loaded;
            return loaded;
        }
    }

    /// <summary>
    /// Reads a stored source's description. A source whose description is missing or broken is not dropped in
    /// silence: when <paramref name="repair"/> is set, its file is read again, checked against its hash, and the
    /// description written anew with the file's own time as when it was imported (as for the sanction lists).
    /// </summary>
    /// <param name="kind">The source.</param>
    /// <param name="sha">The file's hash.</param>
    /// <param name="repair">Whether a missing or broken description is written anew from the file.</param>
    /// <returns>The source, or <see langword="null"/> when it is not stored or cannot be described.</returns>
    private OpenSourceListInfo? Describe(SourceKind kind, string sha, bool repair)
    {
        if (!File.Exists(PathFor(kind, sha, DataExtension)))
        {
            return null;
        }

        var path = PathFor(kind, sha, DescriptionExtension);
        try
        {
            var stored = File.Exists(path)
                ? JsonSerializer.Deserialize<StoredDescription>(File.ReadAllBytes(path))
                : null;
            if (stored is not null)
            {
                return new OpenSourceListInfo(kind.Name, LicenseOf(kind), sha, stored.DataDate, stored.ImportedUtc,
                    stored.AddressesByChain, stored.Skipped);
            }
        }
        catch (JsonException)
        {
            // Written anew below, from the source itself.
        }

        return repair ? Repair(kind, sha) : null;
    }

    /// <summary>Writes a stored source's description anew from its file.</summary>
    /// <param name="kind">The source.</param>
    /// <param name="sha">The file's hash.</param>
    /// <returns>The source, or <see langword="null"/> when its file no longer has its hash or cannot be read.</returns>
    private OpenSourceListInfo? Repair(SourceKind kind, string sha)
    {
        try
        {
            var bytes = File.ReadAllBytes(PathFor(kind, sha, DataExtension));
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != sha)
            {
                return null;
            }

            var parsed = kind.Parse(bytes);
            var imported = new DateTimeOffset(File.GetLastWriteTimeUtc(PathFor(kind, sha, DataExtension)));
            var info = new OpenSourceListInfo(
                kind.Name, LicenseOf(kind), sha, parsed.DataDate, imported, Counts(parsed), parsed.Skipped);
            WriteDescription(kind, info);
            return info;
        }
        catch (Exception exception) when (exception is OpenSourceListException or IOException
            or UnauthorizedAccessException)
        {
            // A source that cannot be read is never listed: a label from it could not be followed to its file.
            return null;
        }
    }

    /// <summary>Writes a source's description next to it.</summary>
    /// <param name="kind">The source.</param>
    /// <param name="info">The source's description.</param>
    private void WriteDescription(SourceKind kind, OpenSourceListInfo info) =>
        ListFiles.WriteAtomically(PathFor(kind, info.Sha256, DescriptionExtension), JsonSerializer.SerializeToUtf8Bytes(
            new StoredDescription(info.DataDate, info.ImportedUtc, info.AddressesByChain, info.Skipped)));

    /// <summary>Gives a source's licence, which is Chainsaw's own knowledge of it, never read from the file.</summary>
    /// <param name="kind">The source.</param>
    /// <returns>The licence and its attribution.</returns>
    private static string LicenseOf(SourceKind kind) =>
        kind.Name == RansomwhereParser.ListName ? RansomwhereParser.License : string.Empty;

    /// <summary>Says whether a text is a SHA-256 in lower-case hex, so that no other file name is ever used.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool IsHash(string text) =>
        text.Length == 64 && text.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');

    /// <summary>Gives the path of a stored source's file.</summary>
    /// <param name="kind">The source.</param>
    /// <param name="sha">The file's hash.</param>
    /// <param name="extension">The file's extension.</param>
    /// <returns>The path.</returns>
    private string PathFor(SourceKind kind, string sha, string extension) =>
        Path.Combine(options.OpenSourceDirectory, kind.Prefix + sha + extension);

    /// <summary>Counts the addresses per chain.</summary>
    /// <param name="parsed">The source.</param>
    /// <returns>The counts, by the chain's name in ordinal order.</returns>
    private static SortedDictionary<string, int> Counts(ParsedOpenSource parsed) => new(
        parsed.Entries.Select(entry => entry.Address).Distinct()
            .GroupBy(address => address.Chain.ToString())
            .ToDictionary(group => group.Key, group => group.Count()),
        StringComparer.Ordinal);

    /// <summary>An open source Chainsaw reads.</summary>
    /// <param name="Name">The source's name, as origins state it.</param>
    /// <param name="Prefix">The start of its stored files' names.</param>
    /// <param name="Recognises">Says whether a file is the source's.</param>
    /// <param name="Parse">Reads it.</param>
    private sealed record SourceKind(
        string Name, string Prefix, Func<byte[], bool> Recognises, Func<byte[], ParsedOpenSource> Parse);

    /// <summary>A stored source's description.</summary>
    /// <param name="DataDate">The date of its newest record.</param>
    /// <param name="ImportedUtc">When it was imported.</param>
    /// <param name="AddressesByChain">The addresses per chain.</param>
    /// <param name="Skipped">Records that name no address Chainsaw can read.</param>
    private sealed record StoredDescription(
        DateOnly DataDate,
        DateTimeOffset ImportedUtc,
        IReadOnlyDictionary<string, int> AddressesByChain,
        int Skipped);
}
