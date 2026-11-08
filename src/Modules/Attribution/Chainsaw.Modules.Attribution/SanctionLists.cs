using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution;

/// <summary>
/// The imported sanction lists in the application data folder. Each list is kept as the exact bytes the user
/// imported, named by its SHA-256, next to a small description; a list is read again, and its hash and date checked,
/// before any hit is taken from it, so that a changed file is noticed rather than trusted.
/// </summary>
/// <param name="options">Where the lists live.</param>
/// <param name="clock">The clock for when a list is imported.</param>
internal sealed class SanctionLists(AttributionOptions options, IClock clock) : ISanctionLists
{
    /// <summary>The largest file that is read (the real list is some tens of megabytes).</summary>
    public const int MaximumLength = 256 * 1024 * 1024;

    /// <summary>The start of a stored list's file names.</summary>
    private const string FilePrefix = "ofac-sdn-";

    /// <summary>The extension of a stored list.</summary>
    private const string ListExtension = ".xml";

    /// <summary>The extension of a stored list's description.</summary>
    private const string DescriptionExtension = ".json";

    /// <summary>Guards the files and the loaded list.</summary>
    private readonly Lock _gate = new();

    /// <summary>The list loaded for lookups, by its hash, or <see langword="null"/>.</summary>
    private LoadedList? _loaded;

    /// <summary>
    /// The stored lists as last read from the folder, or <see langword="null"/> until they are read; only an import
    /// changes them, and every hit is still checked against the file's hash when the list is loaded.
    /// </summary>
    private IReadOnlyList<SanctionListInfo>? _listing;

    /// <inheritdoc/>
    public SanctionListInfo? Current => List().FirstOrDefault();

    /// <inheritdoc/>
    public SanctionListInfo Import(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var bytes = ReadAll(content);
        var parsed = OfacSdnParser.Parse(bytes);
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        lock (_gate)
        {
            if (Describe(sha) is { } existing)
            {
                return existing;
            }

            var info = new SanctionListInfo(
                OfacSdnParser.ListName,
                sha,
                parsed.PublishDate,
                clock.UtcNow,
                Counts(parsed),
                parsed.Skipped);
            Directory.CreateDirectory(options.SanctionDirectory);
            WriteAtomically(PathFor(sha, ListExtension), bytes);
            WriteAtomically(PathFor(sha, DescriptionExtension), JsonSerializer.SerializeToUtf8Bytes(
                new StoredDescription(info.PublishDate, info.ImportedUtc, info.AddressesByCurrency, info.Skipped)));
            _listing = null;
            return info;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<SanctionListInfo> List()
    {
        lock (_gate)
        {
            return _listing ??= ReadListing();
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
            .. Directory.EnumerateFiles(options.SanctionDirectory, FilePrefix + "*" + DescriptionExtension)
                .Select(path => Path.GetFileNameWithoutExtension(path)[FilePrefix.Length..])
                .Select(Describe)
                .OfType<SanctionListInfo>()
                .OrderByDescending(info => info.PublishDate)
                .ThenByDescending(info => info.ImportedUtc)
                .ThenBy(info => info.Sha256, StringComparer.Ordinal),
        ];
    }

    /// <inheritdoc/>
    public SanctionMatch? Find(BitcoinAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return Match(list => list.Bitcoin.GetValueOrDefault(address));
    }

    /// <inheritdoc/>
    public SanctionMatch? Find(EthereumAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return Match(list => list.Ethereum.GetValueOrDefault(address));
    }

    /// <summary>Finds hits on the current list, loading it again when another list has become the current one.</summary>
    /// <param name="hits">Gives the loaded list's entries for the address, or <see langword="null"/>.</param>
    /// <returns>The match, or <see langword="null"/> when no list is imported.</returns>
    private SanctionMatch? Match(Func<LoadedList, IReadOnlyList<SanctionedAddress>?> hits)
    {
        var current = Current;
        if (current is null)
        {
            return null;
        }

        lock (_gate)
        {
            if (_loaded?.Info.Sha256 != current.Sha256)
            {
                _loaded = Load(current);
            }

            return new SanctionMatch(current, hits(_loaded) ?? []);
        }
    }

    /// <summary>Reads a stored list again and checks it against its hash and date.</summary>
    /// <param name="info">The list.</param>
    /// <returns>The list, with its Bitcoin and Ethereum addresses by address.</returns>
    /// <exception cref="SanctionListException"><see cref="SanctionListError.Tampered"/>.</exception>
    private LoadedList Load(SanctionListInfo info)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(PathFor(info.Sha256, ListExtension));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new SanctionListException(SanctionListError.Tampered, exception);
        }

        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != info.Sha256)
        {
            throw new SanctionListException(SanctionListError.Tampered);
        }

        var parsed = OfacSdnParser.Parse(bytes);
        if (parsed.PublishDate != info.PublishDate)
        {
            throw new SanctionListException(SanctionListError.Tampered);
        }

        return new LoadedList(
            info, ByKey(parsed, OfacSdnParser.BitcoinKey), ByKey(parsed, OfacSdnParser.EthereumKey));
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

    /// <summary>Reads a stored list's description.</summary>
    /// <param name="sha">The list's hash.</param>
    /// <returns>The list, or <see langword="null"/> when it is not stored or its description cannot be read.</returns>
    private SanctionListInfo? Describe(string sha)
    {
        var path = PathFor(sha, DescriptionExtension);
        if (!File.Exists(path) || !File.Exists(PathFor(sha, ListExtension)))
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredDescription>(File.ReadAllBytes(path));
            return stored is null
                ? null
                : new SanctionListInfo(
                    OfacSdnParser.ListName,
                    sha,
                    stored.PublishDate,
                    stored.ImportedUtc,
                    stored.AddressesByCurrency,
                    stored.Skipped);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Gives the path of a stored list's file.</summary>
    /// <param name="sha">The list's hash.</param>
    /// <param name="extension">The file's extension.</param>
    /// <returns>The path.</returns>
    private string PathFor(string sha, string extension) =>
        Path.Combine(options.SanctionDirectory, FilePrefix + sha + extension);

    /// <summary>Counts the addresses per currency.</summary>
    /// <param name="parsed">The list.</param>
    /// <returns>The counts, by currency code in ordinal order.</returns>
    private static SortedDictionary<string, int> Counts(ParsedSanctionList parsed) => new(
        parsed.Addresses.GroupBy(address => address.Currency).ToDictionary(group => group.Key, group => group.Count()),
        StringComparer.Ordinal);

    /// <summary>Reads a stream to its end, up to <see cref="MaximumLength"/>.</summary>
    /// <param name="content">The stream.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="SanctionListException"><see cref="SanctionListError.TooLarge"/>.</exception>
    private static byte[] ReadAll(Stream content)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = content.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > MaximumLength)
            {
                throw new SanctionListException(SanctionListError.TooLarge);
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>Writes a file through a temporary file, so that it is either whole or not there.</summary>
    /// <param name="path">The file.</param>
    /// <param name="bytes">The content.</param>
    private static void WriteAtomically(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, path, overwrite: true);
    }

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
    private sealed record StoredDescription(
        DateOnly PublishDate,
        DateTimeOffset ImportedUtc,
        IReadOnlyDictionary<string, int> AddressesByCurrency,
        int Skipped);
}
