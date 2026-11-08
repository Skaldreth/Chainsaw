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
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        lock (_gate)
        {
            if (Describe(kind, sha) is { } existing)
            {
                return existing;
            }

            var info = new OpenSourceListInfo(
                parsed.Name, parsed.License, sha, parsed.DataDate, clock.UtcNow, Counts(parsed), parsed.Skipped);
            Directory.CreateDirectory(options.OpenSourceDirectory);
            ListFiles.WriteAtomically(PathFor(kind, sha, DataExtension), bytes);
            ListFiles.WriteAtomically(PathFor(kind, sha, DescriptionExtension), JsonSerializer.SerializeToUtf8Bytes(
                new StoredDescription(info.DataDate, info.ImportedUtc, info.AddressesByChain, info.Skipped)));
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
                .Select(sha => Describe(kind, sha)))
            .OfType<OpenSourceListInfo>()
            .OrderByDescending(info => info.DataDate)
            .ThenByDescending(info => info.ImportedUtc)
            .ThenBy(info => info.Sha256, StringComparer.Ordinal),
        ];
    }

    /// <inheritdoc/>
    public IReadOnlyList<OpenSourceMatch> Find(ChainAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var matches = new List<OpenSourceMatch>();
        foreach (var list in CurrentLists)
        {
            var hits = Loaded(list)[address].ToList();
            if (hits.Count > 0)
            {
                matches.Add(new OpenSourceMatch(list, hits));
            }
        }

        return matches;
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
                throw new OpenSourceListException(OpenSourceListError.Tampered, exception);
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

    /// <summary>Reads a stored source's description.</summary>
    /// <param name="kind">The source.</param>
    /// <param name="sha">The file's hash.</param>
    /// <returns>The source, or <see langword="null"/> when it is not stored or cannot be described.</returns>
    private OpenSourceListInfo? Describe(SourceKind kind, string sha)
    {
        var path = PathFor(kind, sha, DescriptionExtension);
        if (!File.Exists(path) || !File.Exists(PathFor(kind, sha, DataExtension)))
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredDescription>(File.ReadAllBytes(path));
            return stored is null
                ? null
                : new OpenSourceListInfo(kind.Name, LicenseOf(kind), sha, stored.DataDate, stored.ImportedUtc,
                    stored.AddressesByChain, stored.Skipped);
        }
        catch (JsonException)
        {
            return null;
        }
    }

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
