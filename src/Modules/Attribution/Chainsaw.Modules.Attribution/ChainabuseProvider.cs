using System.Globalization;
using System.Text;
using System.Text.Json;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution;

/// <summary>
/// Chainabuse (TRM Labs), where the public and partners report addresses used for fraud and other abuse; it works
/// with law enforcement (the user's decision 2026-11-08). One request, <c>GET /reports?address=…&amp;chain=…</c>,
/// gives up to 50 reports about the address, with Basic authentication where the key is both user name and password.
/// The reports are grouped by what the address was reported as, so that each category becomes one label.
/// </summary>
/// <param name="baseUri">The API's base address, normally <c>https://api.chainabuse.com/v0/</c>.</param>
internal sealed class ChainabuseProvider(Uri baseUri) : IAttributionProvider
{
    /// <summary>The supplier's name in the case and the settings.</summary>
    public const string ProviderName = "chainabuse";

    /// <summary>The service the key is stored under in the vault.</summary>
    public const string KeyService = "Chainabuse";

    /// <summary>How many reports one request asks for, the API's largest page.</summary>
    private const int PageSize = 50;

    /// <summary>How many report ids a label's reason names, so that the reason stays short.</summary>
    private const int NamedReports = 5;

    /// <summary>The API's codes for the chains Chainsaw asks about.</summary>
    private static readonly Dictionary<Chain, string> ChainCodes = new()
    {
        [Chain.Bitcoin] = "BTC",
        [Chain.Ethereum] = "ETH",
    };

    /// <summary>The categories the API documents, in Swedish; others are shown as the API writes them.</summary>
    private static readonly Dictionary<string, string> Categories = new(StringComparer.Ordinal)
    {
        ["PHISHING"] = "Nätfiske",
        ["RANSOMWARE"] = "Ransomware",
        ["SEXTORTION"] = "Sextortion",
        ["RUG_PULL"] = "Rug pull",
        ["PIGBUTCHERING"] = "Pig butchering",
        ["PIG_BUTCHERING"] = "Pig butchering",
        ["IMPERSONATION"] = "Identitetsbedrägeri",
        ["FAKE_PROJECT"] = "Falskt projekt",
        ["FAKE_RETURNS"] = "Falsk avkastning",
        ["INVESTMENT_SCAM"] = "Investeringsbedrägeri",
        ["ROMANCE"] = "Romansbedrägeri",
        ["BLACKMAIL"] = "Utpressning",
        ["DONATION"] = "Falsk insamling",
        ["AIRDROP"] = "Falsk airdrop",
        ["CONTRACT_EXPLOIT"] = "Utnyttjat kontrakt",
        ["HACK"] = "Intrång",
        ["SIM_SWAP"] = "SIM-kapning",
        ["UKRAINE_SCAM"] = "Falsk insamling för Ukraina",
        ["OTHER"] = "Annat",
    };

    /// <summary>The checked base address.</summary>
    private readonly Uri _baseUri = CheckBase(baseUri);

    /// <inheritdoc/>
    public AttributionProviderInfo Info { get; } =
        new(ProviderName, "Chainabuse", KeyService, [.. ChainCodes.Keys]);

    /// <inheritdoc/>
    public ExitRequest Request(ChainAddress address, string key)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrEmpty(key);
        var chain = ChainCodes[address.Chain];
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"reports?address={Uri.EscapeDataString(address.Value)}&chain={chain}&perPage={PageSize}");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{key}:{key}"));
        return ExitRequest.Get(new Uri(_baseUri, query)) with
        {
            Accept = "application/json",
            Credential = new ExitCredential("Authorization", "Basic " + basic),
        };
    }

    /// <inheritdoc/>
    public ProviderAnswer Read(ReadOnlySpan<byte> body, ChainAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            if (!document.RootElement.TryGetProperty("reports", out var reports)
                || reports.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("Chainabuse's answer has no reports.");
            }

            var about = reports.EnumerateArray().Select(Report).Where(report => Names(report, address)).ToList();
            var findings = about
                .GroupBy(report => report.Category ?? "OTHER", StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => Finding(address, group.Key, [.. group.OrderBy(report => report.Created)]))
                .ToList();
            return new ProviderAnswer(about.Count, findings);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or KeyNotFoundException)
        {
            throw new FormatException("Chainabuse's answer could not be read.", exception);
        }
    }

    /// <summary>Checks that the base address is https and ends with a slash, so that paths are added to it.</summary>
    /// <param name="uri">The base address.</param>
    /// <returns>The base address.</returns>
    /// <exception cref="ArgumentException">The address is not an absolute https address.</exception>
    private static Uri CheckBase(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Chainabuse is asked over https only.", nameof(uri));
        }

        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }

    /// <summary>Reads one report.</summary>
    /// <param name="item">The report.</param>
    /// <returns>What Chainsaw uses of it.</returns>
    private static Reported Report(JsonElement item)
    {
        var id = item.GetProperty("id").GetString() ?? throw new FormatException("A report has no id.");
        var category = item.TryGetProperty("scamCategory", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
        var created = DateTimeOffset.Parse(
            item.GetProperty("createdAt").GetString() ?? throw new FormatException("A report has no time."),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal);
        var addresses = item.TryGetProperty("addresses", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
                .Select(entry => entry.TryGetProperty("address", out var text) ? text.GetString() : null)
                .OfType<string>()
                .ToList()
            : [];
        return new Reported(id, category, created, Flag(item, "trusted"), Flag(item, "checked"), addresses);
    }

    /// <summary>Reads a true or false that may be missing.</summary>
    /// <param name="item">The report.</param>
    /// <param name="name">The property.</param>
    /// <returns><see langword="true"/> only when the property is there and true.</returns>
    private static bool Flag(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>
    /// Says whether a report names the address, so that a report the API gave for another reason never labels it.
    /// Ethereum's hex is compared without regard to case, other chains' exactly.
    /// </summary>
    /// <param name="report">The report.</param>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> when the report names it.</returns>
    private static bool Names(Reported report, ChainAddress address)
    {
        var comparison = address.Chain == Chain.Ethereum ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return report.Addresses.Any(named => string.Equals(named.Trim(), address.Value, comparison));
    }

    /// <summary>Makes the finding of all the reports of one category.</summary>
    /// <param name="address">The address.</param>
    /// <param name="category">The API's category.</param>
    /// <param name="reports">The reports, oldest first.</param>
    /// <returns>The finding.</returns>
    private static ProviderFinding Finding(ChainAddress address, string category, IReadOnlyList<Reported> reports)
    {
        var name = Categories.TryGetValue(category, out var swedish) ? swedish : category;
        var first = reports[0].Created.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var last = reports[^1].Created.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var when = first == last ? first : $"{first} – {last}";
        var count = reports.Count == 1 ? "1 rapport" : $"{reports.Count} rapporter";
        var ids = string.Join(", ", reports.Take(NamedReports).Select(report => report.Id));
        var more = reports.Count > NamedReports ? $" och {reports.Count - NamedReports} till" : string.Empty;
        var reason = string.Format(
            CultureInfo.InvariantCulture,
            "{0} på Chainabuse om adressen som {1} ({2}); {3} granskade av moderator, {4} från betrodda rapportörer. "
                + "Rapporterna skrivs in av allmänheten. Id: {5}{6}.",
            count,
            name,
            when,
            reports.Count(report => report.Checked),
            reports.Count(report => report.Trusted),
            ids,
            more);
        return new ProviderFinding(
            $"{ChainCodes[address.Chain]}:{address.Value}/{category}", LabelKind.Reported, name, reason);
    }

    /// <summary>What Chainsaw uses of one report.</summary>
    /// <param name="Id">The report's id.</param>
    /// <param name="Category">What it reports, or <see langword="null"/>.</param>
    /// <param name="Created">When it was made.</param>
    /// <param name="Trusted">Whether a trusted contributor made it.</param>
    /// <param name="Checked">Whether a moderator checked it.</param>
    /// <param name="Addresses">The addresses it names.</param>
    private sealed record Reported(
        string Id, string? Category, DateTimeOffset Created, bool Trusted, bool Checked,
        IReadOnlyList<string> Addresses);
}
