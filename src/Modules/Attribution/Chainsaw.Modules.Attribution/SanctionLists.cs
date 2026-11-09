using System.Security.Cryptography;
using System.Text.Json;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution;

/// <summary>
/// The imported sanction lists in the application data folder. Each list is kept as the exact bytes the user
/// imported, named by its kind and SHA-256, next to a small description; a list is read again, and its hash and date
/// checked, before any hit is taken from it, so that a changed file is noticed rather than trusted.
/// </summary>
/// <param name="options">Where the lists live.</param>
/// <param name="clock">The clock for when a list is imported.</param>
internal sealed class SanctionLists(AttributionOptions options, IClock clock) : ISanctionLists
{
    /// <summary>The largest file that is read (the real lists are some tens of megabytes).</summary>
    public const int MaximumLength = 256 * 1024 * 1024;

    /// <summary>The extension of a stored list.</summary>
    private const string ListExtension = ".xml";

    /// <summary>The extension of a stored list's description.</summary>
    private const string DescriptionExtension = ".json";

    /// <summary>The kinds of list Chainsaw reads, each with its name, its files' prefix and its reader.</summary>
    private static readonly IReadOnlyList<ListKind> Kinds =
    [
        new(OfacSdnParser.ListName, "ofac-sdn-", "sdnList", OfacSdnParser.Parse),
        new(EuFsfParser.ListName, "eu-fsf-", EuFsfParser.RootName, EuFsfParser.Parse),
    ];

    /// <summary>Guards the files and the loaded lists.</summary>
    private readonly Lock _gate = new();

    /// <summary>The lists whose file has lost its hash, not read again until imported or removed.</summary>
    private readonly HashSet<string> _broken = new(StringComparer.Ordinal);

    /// <summary>The lists loaded for lookups, by their hash.</summary>
    private readonly Dictionary<string, LoadedList> _loaded = new(StringComparer.Ordinal);

    /// <summary>The stored lists, read again when the folder has changed; created on first use.</summary>
    private FolderSnapshot<SanctionListInfo>? _listing;

    /// <inheritdoc/>
    public SanctionListInfo? Current => CurrentLists.FirstOrDefault();

    /// <inheritdoc/>
    public IReadOnlyList<SanctionListInfo> CurrentLists =>
    [
        .. List().GroupBy(list => list.Name, StringComparer.Ordinal).Select(group => group.First())
            .OrderByDescending(list => list.PublishDate).ThenByDescending(list => list.ImportedUtc),
    ];

    /// <inheritdoc/>
    public SanctionListInfo Import(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var bytes = ListFiles.ReadAll(
            content, MaximumLength, () => new SanctionListException(SanctionListError.TooLarge));
        var (kind, parsed) = Parse(bytes);

        // The newest list of a kind is the one used, so a date after today (a day's slack for time zones) would
        // shadow every list of that kind imported later.
        if (parsed.PublishDate > DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddDays(1))
        {
            throw new SanctionListException(SanctionListError.FutureDate);
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

            var info = new SanctionListInfo(
                parsed.Name, sha, parsed.PublishDate, clock.UtcNow, Counts(parsed), parsed.Skipped)
            {
                Uncertain = UncertainOf(parsed),
            };
            Directory.CreateDirectory(options.SanctionDirectory);
            ListFiles.WriteAtomically(PathFor(kind, sha, ListExtension), bytes);
            WriteDescription(kind, info);
            _listing?.Invalidate();
            return info;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<SanctionListInfo> List()
    {
        lock (_gate)
        {
            _listing ??= new FolderSnapshot<SanctionListInfo>(options.SanctionDirectory, ReadListing);
            return _listing.Get();
        }
    }

    /// <inheritdoc/>
    public bool Remove(string sha256)
    {
        ArgumentNullException.ThrowIfNull(sha256);
        lock (_gate)
        {
            var removed = false;
            foreach (var kind in Kinds)
            {
                var list = PathFor(kind, sha256, ListExtension);
                if (!IsStoredName(kind, Path.GetFileName(list)) || !File.Exists(list))
                {
                    continue;
                }

                File.Delete(list);
                File.Delete(PathFor(kind, sha256, DescriptionExtension));
                removed = true;
            }

            _loaded.Remove(sha256);
            _broken.Remove(sha256);
            _listing?.Invalidate();
            return removed;
        }
    }

    /// <inheritdoc/>
    public SanctionMatch? Find(BitcoinAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return Current is { } current ? Match(current, list => list.Bitcoin.GetValueOrDefault(address)) : null;
    }

    /// <inheritdoc/>
    public SanctionMatch? Find(EthereumAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return Current is { } current ? Match(current, list => list.Ethereum.GetValueOrDefault(address)) : null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<SanctionMatch> FindAll(BitcoinAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var key = address.ToChainAddress();
        return FindMany([key]).Matches.GetValueOrDefault(key) ?? [];
    }

    /// <inheritdoc/>
    public IReadOnlyList<SanctionMatch> FindAll(EthereumAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var key = address.ToChainAddress();
        return FindMany([key]).Matches.GetValueOrDefault(key) ?? [];
    }

    /// <inheritdoc/>
    public SanctionLookup FindMany(IReadOnlyCollection<ChainAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var usable = new List<LoadedList>();
        var unusable = new List<SanctionListInfo>();
        var missing = new List<SanctionListInfo>();
        foreach (var list in CurrentLists)
        {
            switch (TryLoaded(list))
            {
                case { Loaded: { } loaded }:
                    usable.Add(loaded);
                    break;
                case { IsMissing: true }:
                    missing.Add(list);
                    break;
                default:
                    unusable.Add(list);
                    break;
            }
        }

        var matches = new Dictionary<ChainAddress, IReadOnlyList<SanctionMatch>>();
        foreach (var address in addresses.Distinct())
        {
            List<SanctionMatch> found =
            [
                .. usable.Select(list => (list.Info, Hits: HitsOn(list, address)))
                    .Where(pair => pair.Hits is { Count: > 0 })
                    .Select(pair => new SanctionMatch(pair.Info, pair.Hits!)),
            ];
            if (found.Count > 0)
            {
                matches[address] = found;
            }
        }

        return new SanctionLookup([.. usable.Select(list => list.Info)], unusable, matches) { Missing = missing };
    }

    /// <summary>Gives a loaded list's entries for an address on its own chain.</summary>
    /// <param name="list">The list.</param>
    /// <param name="address">The address with its chain.</param>
    /// <returns>The entries, or <see langword="null"/> when the list does not name it.</returns>
    private static IReadOnlyList<SanctionedAddress>? HitsOn(LoadedList list, ChainAddress address) =>
        address.Chain switch
        {
            Chain.Bitcoin when BitcoinAddress.TryParse(address.Value, out var bitcoin) =>
                list.Bitcoin.GetValueOrDefault(bitcoin),
            Chain.Ethereum when EthereumAddress.TryParse(address.Value.ToLowerInvariant(), out var account) =>
                list.Ethereum.GetValueOrDefault(account),
            _ => null,
        };

    /// <summary>
    /// Loads a list for lookups, or says why it cannot be. A list whose file has lost its hash is remembered as broken,
    /// so that it is never read and hashed again at every lookup (up to 256 MB), until it is imported again or
    /// removed; one whose file is gone is not, since the next listing follows the folder. The other lists are still
    /// looked on (the review of round 5, I4 and M-d).
    /// </summary>
    /// <param name="info">The list.</param>
    /// <returns>The loaded list, or whether it is missing rather than broken.</returns>
    private (LoadedList? Loaded, bool IsMissing) TryLoaded(SanctionListInfo info)
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
            catch (SanctionListException exception) when (exception.Error == SanctionListError.Tampered)
            {
                _broken.Add(info.Sha256);
                return (null, false);
            }
            catch (SanctionListException exception) when (exception.Error == SanctionListError.Missing)
            {
                return (null, true);
            }
        }
    }

    /// <summary>Tells the kind of list a file is by its root element, and reads it.</summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <returns>The kind and what the list holds.</returns>
    /// <exception cref="SanctionListException">The file is not a list Chainsaw reads.</exception>
    private static (ListKind Kind, ParsedSanctionList Parsed) Parse(byte[] bytes)
    {
        var root = SanctionXml.RootName(bytes);
        var kind = Kinds.FirstOrDefault(each => each.Root == root)
            ?? throw new SanctionListException(SanctionListError.NotSdnList);
        return (kind, kind.Parse(bytes));
    }

    /// <summary>Finds hits on a list, loading it when it is not loaded.</summary>
    /// <param name="info">The list.</param>
    /// <param name="hits">Gives the loaded list's entries for the address, or <see langword="null"/>.</param>
    /// <returns>The match, empty when the list does not name the address.</returns>
    private SanctionMatch Match(SanctionListInfo info, Func<LoadedList, IReadOnlyList<SanctionedAddress>?> hits) =>
        new(info, hits(Loaded(info)) ?? []);

    /// <summary>Gives a list loaded for lookups, loading it when it is not loaded.</summary>
    /// <param name="info">The list.</param>
    /// <returns>The loaded list.</returns>
    /// <exception cref="SanctionListException"><see cref="SanctionListError.Tampered"/>.</exception>
    private LoadedList Loaded(SanctionListInfo info)
    {
        lock (_gate)
        {
            if (!_loaded.TryGetValue(info.Sha256, out var loaded))
            {
                // Only the current list of each kind is kept in memory.
                foreach (var stale in _loaded.Values.Where(list => list.Info.Name == info.Name).ToList())
                {
                    _loaded.Remove(stale.Info.Sha256);
                }

                loaded = Load(info);
                _loaded[info.Sha256] = loaded;
            }

            return loaded;
        }
    }

    /// <summary>Reads the stored lists from the folder.</summary>
    /// <returns>The lists, the newest publish date first.</returns>
    private List<SanctionListInfo> ReadListing()
    {
        if (!Directory.Exists(options.SanctionDirectory))
        {
            return [];
        }

        return
        [
            .. Kinds.SelectMany(kind => Directory
                    .EnumerateFiles(options.SanctionDirectory, kind.Prefix + "*" + ListExtension)
                    .Where(path => IsStoredName(kind, Path.GetFileName(path)))
                    .Select(path => Describe(kind, Path.GetFileNameWithoutExtension(path)[kind.Prefix.Length..], true)))
                .OfType<SanctionListInfo>()
                .OrderByDescending(info => info.PublishDate)
                .ThenByDescending(info => info.ImportedUtc)
                .ThenBy(info => info.Sha256, StringComparer.Ordinal),
        ];
    }

    /// <summary>Reads a stored list again and checks it against its hash, kind and date.</summary>
    /// <param name="info">The list.</param>
    /// <returns>The list, with its Bitcoin and Ethereum addresses by address.</returns>
    /// <exception cref="SanctionListException"><see cref="SanctionListError.Tampered"/>.</exception>
    private LoadedList Load(SanctionListInfo info)
    {
        var kind = Kinds.First(each => each.Name == info.Name);
        var parsed = ReadStored(kind, info.Sha256);
        if (parsed.PublishDate != info.PublishDate)
        {
            throw new SanctionListException(SanctionListError.Tampered);
        }

        return new LoadedList(
            info, ByKey(parsed, OfacSdnParser.BitcoinKey), ByKey(parsed, OfacSdnParser.EthereumKey));
    }

    /// <summary>Reads a stored list's file and checks that it still has its hash and is of its kind.</summary>
    /// <param name="kind">The kind of list.</param>
    /// <param name="sha">The list's hash.</param>
    /// <returns>What the list holds.</returns>
    /// <exception cref="SanctionListException"><see cref="SanctionListError.Tampered"/>.</exception>
    private ParsedSanctionList ReadStored(ListKind kind, string sha)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(PathFor(kind, sha, ListExtension));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // Gone since the folder was read (removed meanwhile): not changed, and the next listing shows it.
            throw new SanctionListException(SanctionListError.Missing, exception);
        }

        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != sha)
        {
            throw new SanctionListException(SanctionListError.Tampered);
        }

        var (read, parsed) = Parse(bytes);
        return read == kind ? parsed : throw new SanctionListException(SanctionListError.Tampered);
    }

    /// <summary>Gathers a list's addresses by the key they are looked up by on one chain.</summary>
    /// <typeparam name="TKey">The chain's address.</typeparam>
    /// <param name="parsed">The list.</param>
    /// <param name="key">Gives an entry's key on the chain, or <see langword="null"/> when it is not for it.</param>
    /// <returns>The entries by address.</returns>
    private static Dictionary<TKey, IReadOnlyList<SanctionedAddress>> ByKey<TKey>(
        ParsedSanctionList parsed, Func<SanctionedAddress, TKey?> key)
        where TKey : class =>
        parsed.Addresses
            .Select(address => (Key: key(address), Address: address))
            .Where(pair => pair.Key is not null)
            .GroupBy(pair => pair.Key!)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SanctionedAddress>)[.. group.Select(pair => pair.Address)]);

    /// <summary>
    /// Reads a stored list's description. A list whose description is missing or broken is no longer dropped from
    /// the listing in silence (the review of etapp 8, M5): when <paramref name="repair"/> is set, its file is read
    /// again, checked against its hash, and the description written anew, with the file's own time as when it was
    /// imported.
    /// </summary>
    /// <param name="kind">The kind of list.</param>
    /// <param name="sha">The list's hash.</param>
    /// <param name="repair">Whether a missing or broken description is written anew from the file.</param>
    /// <returns>The list, or <see langword="null"/> when it is not stored or cannot be read.</returns>
    private SanctionListInfo? Describe(ListKind kind, string sha, bool repair)
    {
        if (!File.Exists(PathFor(kind, sha, ListExtension)))
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
                return new SanctionListInfo(
                    kind.Name, sha, stored.PublishDate, stored.ImportedUtc, stored.AddressesByCurrency, stored.Skipped)
                {
                    Uncertain = stored.Uncertain,
                };
            }
        }
        catch (JsonException)
        {
            // Written anew below, from the list itself.
        }

        return repair ? Repair(kind, sha) : null;
    }

    /// <summary>Writes a stored list's description anew from the list's file.</summary>
    /// <param name="kind">The kind of list.</param>
    /// <param name="sha">The list's hash.</param>
    /// <returns>The list, or <see langword="null"/> when its file no longer has its hash or cannot be read.</returns>
    private SanctionListInfo? Repair(ListKind kind, string sha)
    {
        try
        {
            var parsed = ReadStored(kind, sha);
            var imported = new DateTimeOffset(File.GetLastWriteTimeUtc(PathFor(kind, sha, ListExtension)));
            var info = new SanctionListInfo(
                kind.Name, sha, parsed.PublishDate, imported, Counts(parsed), parsed.Skipped)
            {
                Uncertain = UncertainOf(parsed),
            };
            WriteDescription(kind, info);
            return info;
        }
        catch (Exception exception) when (exception is SanctionListException or IOException
            or UnauthorizedAccessException)
        {
            // A list that has lost its hash is never listed: a hit from it could not be followed back to its file.
            return null;
        }
    }

    /// <summary>Writes a list's description next to it.</summary>
    /// <param name="kind">The kind of list.</param>
    /// <param name="info">The list.</param>
    private void WriteDescription(ListKind kind, SanctionListInfo info) =>
        ListFiles.WriteAtomically(PathFor(kind, info.Sha256, DescriptionExtension), JsonSerializer.SerializeToUtf8Bytes(
            new StoredDescription(
                info.PublishDate, info.ImportedUtc, info.AddressesByCurrency, info.Skipped, info.Uncertain)));

    /// <summary>Says whether a file name is a stored list's: the kind's prefix and a lower-case SHA-256.</summary>
    /// <param name="kind">The kind of list.</param>
    /// <param name="fileName">The file's name with its extension.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool IsStoredName(ListKind kind, string fileName)
    {
        var sha = Path.GetFileNameWithoutExtension(fileName);
        return sha.StartsWith(kind.Prefix, StringComparison.Ordinal)
            && sha.Length == kind.Prefix.Length + 64
            && sha[kind.Prefix.Length..].All(Uri.IsHexDigit)
            && sha[kind.Prefix.Length..] == sha[kind.Prefix.Length..].ToLowerInvariant();
    }

    /// <summary>Gives the path of a stored list's file.</summary>
    /// <param name="kind">The kind of list.</param>
    /// <param name="sha">The list's hash.</param>
    /// <param name="extension">The file's extension.</param>
    /// <returns>The path.</returns>
    private string PathFor(ListKind kind, string sha, string extension) =>
        Path.Combine(options.SanctionDirectory, kind.Prefix + sha + extension);

    /// <summary>Counts the addresses whose chain the list does not say for certain.</summary>
    /// <param name="parsed">The list.</param>
    /// <returns>The count.</returns>
    private static int UncertainOf(ParsedSanctionList parsed) => parsed.Addresses.Count(address => address.IsUncertain);

    /// <summary>Counts the addresses per currency.</summary>
    /// <param name="parsed">The list.</param>
    /// <returns>The counts, by currency code in ordinal order.</returns>
    private static SortedDictionary<string, int> Counts(ParsedSanctionList parsed) => new(
        parsed.Addresses.GroupBy(address => address.Currency).ToDictionary(group => group.Key, group => group.Count()),
        StringComparer.Ordinal);

    /// <summary>A kind of sanction list.</summary>
    /// <param name="Name">The list's name, as origins state it.</param>
    /// <param name="Prefix">The start of its stored files' names.</param>
    /// <param name="Root">The local name of its root element, which tells it apart.</param>
    /// <param name="Parse">Reads it.</param>
    private sealed record ListKind(string Name, string Prefix, string Root, Func<byte[], ParsedSanctionList> Parse);

    /// <summary>A list loaded for lookups.</summary>
    /// <param name="Info">The list.</param>
    /// <param name="Bitcoin">Its Bitcoin addresses, by address.</param>
    /// <param name="Ethereum">Its Ethereum addresses (ether, USDT and USDC), by address.</param>
    private sealed record LoadedList(
        SanctionListInfo Info,
        IReadOnlyDictionary<BitcoinAddress, IReadOnlyList<SanctionedAddress>> Bitcoin,
        IReadOnlyDictionary<EthereumAddress, IReadOnlyList<SanctionedAddress>> Ethereum);

    /// <summary>A stored list's description.</summary>
    /// <param name="PublishDate">The date the list says it was published.</param>
    /// <param name="ImportedUtc">When it was imported.</param>
    /// <param name="AddressesByCurrency">The addresses per currency.</param>
    /// <param name="Skipped">Addresses that can never match.</param>
    /// <param name="Uncertain">Addresses whose chain the list does not say for certain.</param>
    private sealed record StoredDescription(
        DateOnly PublishDate,
        DateTimeOffset ImportedUtc,
        IReadOnlyDictionary<string, int> AddressesByCurrency,
        int Skipped,
        int Uncertain = 0);
}
