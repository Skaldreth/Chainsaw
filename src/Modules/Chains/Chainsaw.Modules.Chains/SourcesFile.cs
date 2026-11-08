using System.Text.Json;
using System.Text.Json.Serialization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>A chain as it is saved: its mode and its endpoints in the user's order.</summary>
/// <param name="Chain">The chain.</param>
/// <param name="Mode">Its mode.</param>
/// <param name="Endpoints">Its endpoints.</param>
internal sealed record SavedChain(Chain Chain, SourceMode Mode, IReadOnlyList<SourceEndpoint> Endpoints);

/// <summary>
/// The chains' endpoints as <c>sources.json</c> in the program's data: nothing secret (a key is only named) and nothing
/// about a case. A missing file, or a chain the file does not mention, gives the defaults; a value Chainsaw does not
/// know is left out (an address that is not https, a sort of API it cannot speak) rather than used.
/// </summary>
/// <param name="directory">The folder, or <see langword="null"/> to keep everything in memory.</param>
internal sealed class SourcesFile(string? directory)
{
    /// <summary>How the file is written: indented and with the choices as words, readable by hand.</summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Lets one write at a time, so that two quick changes never meet in the temporary file.</summary>
    private readonly SemaphoreSlim _writing = new(1, 1);

    /// <summary>Gets the file's path, or <see langword="null"/> when nothing is written.</summary>
    public string? FilePath => directory is null ? null : Path.Combine(directory, ChainSourcesOptions.FileName);

    /// <summary>Reads the chains the file has, keeping only what can be used.</summary>
    /// <param name="apis">The sorts of API Chainsaw speaks, by code.</param>
    /// <returns>The chains' modes and endpoints; empty when there is no file or it cannot be read.</returns>
    public IReadOnlyDictionary<Chain, SavedChain> Load(IReadOnlyDictionary<string, ISourceApi> apis)
    {
        var result = new Dictionary<Chain, SavedChain>();
        if (FilePath is not { } path || !File.Exists(path))
        {
            return result;
        }

        FileDto? file;
        try
        {
            file = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), Json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return result;
        }

        foreach (var chain in file?.Chains ?? [])
        {
            if (chain is null || !Enum.IsDefined(chain.Chain) || result.ContainsKey(chain.Chain))
            {
                continue;
            }

            var mode = Enum.IsDefined(chain.Mode) ? chain.Mode : SourceMode.Fastest;
            var endpoints = (chain.Endpoints ?? [])
                .Select(endpoint => Known(endpoint, chain.Chain, apis))
                .OfType<SourceEndpoint>()
                .DistinctBy(endpoint => endpoint.Id)
                .ToList();
            result[chain.Chain] = new SavedChain(chain.Chain, mode, endpoints);
        }

        return result;
    }

    /// <summary>
    /// Writes every chain as it is when the write starts, replacing the file in one step, one write at a time, so that
    /// the last write always holds the latest state. Nothing is written without a folder.
    /// </summary>
    /// <param name="current">Gives the chains' modes and endpoints as they are now.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="IOException">The file could not be written.</exception>
    public async Task SaveAsync(Func<IReadOnlyList<SavedChain>> current)
    {
        if (FilePath is not { } path)
        {
            return;
        }

        await _writing.WaitAsync().ConfigureAwait(false);
        try
        {
            var file = new FileDto([.. current().Select(ToDto)]);
            await Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(directory!);
                    var temporary = path + ".tmp";
                    File.WriteAllText(temporary, JsonSerializer.Serialize(file, Json));
                    File.Move(temporary, path, overwrite: true);
                }
                catch (UnauthorizedAccessException exception)
                {
                    throw new IOException(exception.Message, exception);
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            _writing.Release();
        }
    }

    /// <summary>Makes the file's form of a chain.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>The chain in the file.</returns>
    private static ChainDto ToDto(SavedChain chain) => new(chain.Chain, chain.Mode,
    [
        .. chain.Endpoints.Select(endpoint => new EndpointDto(
            endpoint.Id, endpoint.Name, endpoint.Address.AbsoluteUri, endpoint.Api, endpoint.ApiKey,
            endpoint.Enabled, endpoint.Local,
            endpoint.Interval == DefaultPace(endpoint.Local) ? null : (int)endpoint.Interval.TotalMilliseconds)
        {
            Pin = endpoint.Pin,
            FormerPins = endpoint.FormerPins.Count == 0 ? null : endpoint.FormerPins,
        }),
    ]);

    /// <summary>
    /// Makes an endpoint of a saved one, or nothing when it cannot be used. An address on the computer or a private
    /// network is kept only for an endpoint marked local, and a local one only without an IP address outside the user's
    /// own network; a pace that is missing is the default of its kind, and one outside the limits is brought within
    /// them, never faster than <see cref="SourcePace.Public"/> for an endpoint on the internet.
    /// </summary>
    /// <param name="saved">The saved endpoint.</param>
    /// <param name="chain">The chain it is saved under.</param>
    /// <param name="apis">The sorts of API Chainsaw speaks.</param>
    /// <returns>The endpoint, or <see langword="null"/>.</returns>
    private static SourceEndpoint? Known(
        EndpointDto? saved, Chain chain, IReadOnlyDictionary<string, ISourceApi> apis)
    {
        if (saved is null || saved.Id == Guid.Empty
            || saved.Api is null || !apis.TryGetValue(saved.Api, out var api) || api.Info.Chain != chain
            || !SourceAddress.TryRead(saved.Address, saved.Local, out var address, out _))
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(saved.Name) ? SourceAddress.NameOf(address!) : saved.Name.Trim();
        var key = string.IsNullOrWhiteSpace(saved.ApiKey) ? null : saved.ApiKey.Trim();
        // An endpoint on the internet is never asked faster than Chainsaw's own pace, whatever the file says.
        var slowest = saved.Local ? SourcePace.Local : SourcePace.Public;
        var pace = saved.IntervalMilliseconds is { } milliseconds
            ? TimeSpan.FromMilliseconds(Math.Clamp(
                milliseconds, slowest.TotalMilliseconds, SourcePace.Maximum.TotalMilliseconds))
            : DefaultPace(saved.Local);
        return new SourceEndpoint(
            saved.Id, chain, name[..Math.Min(name.Length, SourceAddress.MaximumNameLength)], address!, api.Info.Code,
            key, saved.Enabled)
        {
            Local = saved.Local,
            Interval = pace,

            // Only the own node is pinned; a pin whose fingerprint cannot be read is dropped, never loosened.
            Pin = saved.Local ? KnownPin(saved.Pin) : null,
            FormerPins = saved.Local
                ? [.. (saved.FormerPins ?? []).Select(KnownPin).OfType<EndpointCertificate>()
                    .Take(EndpointCertificate.MaximumFormer)]
                : [],
        };
    }

    /// <summary>Keeps a saved pin only when its fingerprint is one and it says who pinned it.</summary>
    /// <param name="saved">The saved pin.</param>
    /// <returns>The pin, or <see langword="null"/>.</returns>
    private static EndpointCertificate? KnownPin(EndpointCertificate? saved) =>
        saved is not null && CertificatePin.IsValid(saved.Fingerprint) && !string.IsNullOrWhiteSpace(saved.PinnedBy)
            ? saved
            : null;

    /// <summary>
    /// Gives the pace an endpoint has when the file says nothing, so that only another pace is written.
    /// </summary>
    /// <param name="local">Whether it is the user's own node.</param>
    /// <returns>The pace.</returns>
    private static TimeSpan DefaultPace(bool local) => local ? SourcePace.Local : SourcePace.Public;

    /// <summary>The file.</summary>
    /// <param name="Chains">The chains.</param>
    private sealed record FileDto(IReadOnlyList<ChainDto?>? Chains);

    /// <summary>A chain in the file.</summary>
    /// <param name="Chain">The chain.</param>
    /// <param name="Mode">Its mode.</param>
    /// <param name="Endpoints">Its endpoints, in the user's order.</param>
    private sealed record ChainDto(Chain Chain, SourceMode Mode, IReadOnlyList<EndpointDto?>? Endpoints);

    /// <summary>An endpoint in the file.</summary>
    /// <param name="Id">Its id.</param>
    /// <param name="Name">Its name.</param>
    /// <param name="Address">Its address.</param>
    /// <param name="Api">Its sort of API.</param>
    /// <param name="ApiKey">The vault's service for its key, or <see langword="null"/>.</param>
    /// <param name="Enabled">Whether it is on.</param>
    /// <param name="Local">Whether the user marked it as their own node on the computer or a private network.</param>
    /// <param name="IntervalMilliseconds">
    /// Its pace, the shortest time between two requests, or <see langword="null"/> for the default of its kind.
    /// </param>
    private sealed record EndpointDto(
        Guid Id, string? Name, string? Address, string? Api, string? ApiKey, bool Enabled, bool Local = false,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? IntervalMilliseconds = null)
    {
        /// <summary>Gets the certificate pinned for the own node (sketch KL3b), or none.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public EndpointCertificate? Pin { get; init; }

        /// <summary>Gets the certificates pinned before, the latest first, or none.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<EndpointCertificate>? FormerPins { get; init; }
    }
}
