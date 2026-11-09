using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Attribution;

/// <summary>
/// Reads the EU's consolidated list of persons, groups and entities subject to financial sanctions in its XML form
/// (the Financial Sanctions Files, <c>export</c> with <c>sanctionEntity</c> elements, as the Commission publishes it
/// on its FSF portal): the date the file was generated and every Bitcoin or Ethereum address the list names.
/// </summary>
/// <remarks>
/// <para>
/// Unlike OFAC's SDN list, the EU's list has no type of its own for a digital currency address (checked 2026-11-08:
/// the published structure has identifications with a type code and number, and remarks in free text), so an address
/// is recognised by its form wherever it stands in an identification's number or in a remark: a Bitcoin address must
/// pass its checksum, and an Ethereum address is <c>0x</c> and 40 hex digits. Where it stood is kept with the hit
/// (<see cref="SanctionedAddress.ListedAs"/>), so that the label says it. The list may name no addresses at all.
/// </para>
/// <para>The reader refuses DTDs and external entities, and reads an entity part by part (see
/// <see cref="SanctionXml"/>).</para>
/// </remarks>
internal static partial class EuFsfParser
{
    /// <summary>The list's name as an origin states it.</summary>
    public const string ListName = "EU FSF";

    /// <summary>The root element of the list.</summary>
    public const string RootName = "export";

    /// <summary>Where an address stood when it was in a remark.</summary>
    public const string InRemark = "en anmärkning";

    /// <summary>The parts of an entity read one item at a time: none, an entity's parts are its children.</summary>
    private static readonly IReadOnlySet<string> Containers = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Reads the list.</summary>
    /// <param name="content">The file's bytes.</param>
    /// <returns>The date the file was generated, the addresses and how many words could never match.</returns>
    /// <exception cref="SanctionListException">
    /// The file is not the EU's list, it does not say when it was generated, or an entity is too large.
    /// </exception>
    public static ParsedSanctionList Parse(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var reader = XmlReader.Create(stream, SanctionXml.Settings());
            if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != RootName)
            {
                throw new SanctionListException(SanctionListError.NotSdnList);
            }

            var generated = GenerationDate(reader.GetAttribute("generationDate"));
            var addresses = new List<SanctionedAddress>();
            var skipped = 0;
            reader.Read();
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sanctionEntity")
                {
                    skipped += ReadEntity(reader, addresses);
                }
                else if (reader.NodeType == XmlNodeType.Element)
                {
                    reader.Skip();
                }
                else
                {
                    reader.Read();
                }
            }

            return new ParsedSanctionList(
                ListName,
                generated ?? throw new SanctionListException(SanctionListError.NoPublishDate),
                addresses,
                skipped);
        }
        catch (XmlException exception)
        {
            throw new SanctionListException(SanctionListError.NotSdnList, exception);
        }
    }

    /// <summary>Reads the date the file was generated, in the time the file states.</summary>
    /// <param name="text">The root's <c>generationDate</c>, for example <c>2026-11-05T19:01:41.347+01:00</c>.</param>
    /// <returns>The date, or <see langword="null"/> when it is missing or not a time.</returns>
    private static DateOnly? GenerationDate(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? DateOnly.FromDateTime(time.DateTime)
            : null;

    /// <summary>Reads an entity's addresses: in its identifications' numbers and in its remarks.</summary>
    /// <param name="reader">The reader, on the <c>sanctionEntity</c> element; it ends after it.</param>
    /// <param name="addresses">Receives the addresses.</param>
    /// <returns>
    /// How many words had an address's form but could never match: on another chain by the words around them, on a
    /// test network, or on a chain Chainsaw does not read the list for (TRON).
    /// </returns>
    private static int ReadEntity(XmlReader reader, List<SanctionedAddress> addresses)
    {
        var reference = reader.GetAttribute("euReferenceNumber") ?? reader.GetAttribute("logicalId") ?? string.Empty;
        string? strongName = null;
        string? anyName = null;
        var programmes = new List<string>();
        var found = new List<Place>();
        foreach (var (_, part) in SanctionXml.Parts(reader, Containers))
        {
            switch (part.Name.LocalName)
            {
                case "nameAlias":
                    var whole = SanctionXml.Attribute(part, "wholeName");
                    anyName ??= whole;
                    strongName ??= SanctionXml.Attribute(part, "strong") == "true" ? whole : null;
                    found.AddRange(Remarks(part));
                    break;
                case "regulation" when SanctionXml.Attribute(part, "programme") is { } programme:
                    programmes.Add(programme);
                    break;
                case "identification":
                    found.AddRange(Identification(part));
                    found.AddRange(Remarks(part));
                    break;
                case "remark":
                    found.Add(new Place(part.Value, InRemark));
                    break;
                default:
                    found.AddRange(Remarks(part));
                    break;
            }
        }

        var name = strongName ?? anyName ?? string.Empty;
        IReadOnlyList<string> programs = [.. programmes.Distinct(StringComparer.Ordinal)];
        var skipped = 0;
        var seen = new HashSet<(string, string)>();
        foreach (var place in found)
        {
            foreach (var word in Words().Matches(place.Text).Cast<Match>())
            {
                switch (Read(place.Text, word))
                {
                    case { Address: { } address, Currency: { } currency } when seen.Add((address, currency)):
                        addresses.Add(new SanctionedAddress(currency, address, reference, name, programs)
                        {
                            ListedAs = $"{place.Where}: ”{Excerpt(place.Text, word)}”",
                        });
                        break;
                    case { Skipped: true }:
                        skipped++;
                        break;
                }
            }
        }

        return skipped;
    }

    /// <summary>Gives an identification's numbers, with its type, as places an address may stand.</summary>
    /// <param name="part">The <c>identification</c> element.</param>
    /// <returns>The number and its Latin form, each with the type in front so that the type is near it.</returns>
    private static IEnumerable<Place> Identification(XElement part)
    {
        var type = SanctionXml.Attribute(part, "identificationTypeDescription")
            ?? SanctionXml.Attribute(part, "identificationTypeCode") ?? "identifiering";
        return new[] { "number", "latinNumber" }
            .Select(name => SanctionXml.Attribute(part, name))
            .OfType<string>()
            .Select(number => new Place($"{type} {number}", $"identifiering: {type}"));
    }

    /// <summary>Gives the remarks inside a part.</summary>
    /// <param name="part">The part.</param>
    /// <returns>Each remark's text, marked as a remark.</returns>
    private static IEnumerable<Place> Remarks(XElement part) =>
        part.Descendants().Where(element => element.Name.LocalName == "remark")
            .Select(remark => new Place(remark.Value, InRemark));

    /// <summary>
    /// Reads a word as an address by its form: a Bitcoin address on the main network with a valid checksum, or
    /// <c>0x</c> and 40 hex digits for Ethereum; unless the words around it name another chain (BCH, Litecoin, BEP-20,
    /// Polygon …), since the same form is used there and a sanction must never move to the wrong chain (the review
    /// of round 5, I5).
    /// </summary>
    /// <param name="text">The text the word stands in.</param>
    /// <param name="word">The word.</param>
    /// <returns>The address and its currency code, that it was skipped, or neither when it is no address.</returns>
    private static Reading Read(string text, Match word)
    {
        var value = word.Value;
        var isBitcoin = BitcoinAddress.TryParse(value, out var bitcoin);
        var isEthereum = !isBitcoin && EthereumAddress.TryParse(value.ToLowerInvariant(), out _);
        if (!isBitcoin && !isEthereum)
        {
            return new Reading(null, null, Tron().IsMatch(value));
        }

        if (OtherChain().IsMatch(Around(text, word)) || (isBitcoin && bitcoin!.Network != BitcoinNetwork.Mainnet))
        {
            return new Reading(null, null, true);
        }

        return isBitcoin
            ? new Reading(bitcoin!.Value, SanctionCurrency.Bitcoin, false)
            : new Reading(value, SanctionCurrency.Ether, false);
    }

    /// <summary>Finds the Bitcoin and Ethereum addresses in a text, as <see cref="Read"/> reads each word.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Each address with the currency code it is looked up under.</returns>
    internal static IEnumerable<(string Address, string Currency)> Addresses(string text) =>
        Words().Matches(text).Cast<Match>()
            .Select(word => Read(text, word))
            .Where(reading => reading.Address is not null)
            .Select(reading => (reading.Address!, reading.Currency!));

    /// <summary>Gives the text near a word, where a chain would be named.</summary>
    /// <param name="text">The text.</param>
    /// <param name="word">The word.</param>
    /// <returns>Up to <see cref="ContextLength"/> characters on each side of the word.</returns>
    private static string Around(string text, Match word)
    {
        var from = Math.Max(0, word.Index - ContextLength);
        var to = Math.Min(text.Length, word.Index + word.Length + ContextLength);
        return text[from..word.Index] + " " + text[(word.Index + word.Length)..to];
    }

    /// <summary>
    /// Gives about 100 characters of the text around a word, on one line, so that <em>Varför?</em> shows what the
    /// list says (the review of round 5, I6).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="word">The word.</param>
    /// <returns>The excerpt, with … where it is cut.</returns>
    private static string Excerpt(string text, Match word)
    {
        var room = Math.Max(0, (ExcerptLength - word.Length) / 2);
        var from = Math.Max(0, word.Index - room);
        var to = Math.Min(text.Length, word.Index + word.Length + room);
        var excerpt = Spaces().Replace(text[from..to], " ").Trim();
        return (from > 0 ? "…" : string.Empty) + excerpt + (to < text.Length ? "…" : string.Empty);
    }

    /// <summary>How many characters on each side of a word are looked at for the name of another chain.</summary>
    private const int ContextLength = 60;

    /// <summary>About how long an excerpt is.</summary>
    private const int ExcerptLength = 100;

    /// <summary>The words an address may be: letters and digits, so that punctuation never is part of one.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"[A-Za-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex Words();

    /// <summary>
    /// Names of other chains that use a Bitcoin or Ethereum form of address: Bitcoin Cash and SV, Litecoin, Dogecoin,
    /// BNB Smart Chain (BEP-20), TRON, Polygon, Arbitrum, Optimism, Avalanche, Fantom, Base and Ethereum Classic.
    /// </summary>
    /// <returns>The pattern, as whole words in any case.</returns>
    [GeneratedRegex(
        @"\b(bitcoin[\s-]?cash|bch|bitcoin[\s-]?sv|bsv|litecoin|ltc|dogecoin|doge|bep[\s-]?20|bep2|bsc|bnb|"
        + @"binance[\s-]?smart[\s-]?chain|trc[\s-]?20|tron|trx|polygon|matic|arbitrum|optimism|avalanche|avax|"
        + @"fantom|ftm|ethereum[\s-]?classic|blockchair\.com/bitcoin-cash|blockchair\.com/litecoin)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OtherChain();

    /// <summary>A TRON address's form: <c>T</c> and 33 Base58 characters.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex("^T[1-9A-HJ-NP-Za-km-z]{33}$", RegexOptions.CultureInvariant)]
    private static partial Regex Tron();

    /// <summary>Runs of white space.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();

    /// <summary>A text on an entity where an address may stand.</summary>
    /// <param name="Text">The text.</param>
    /// <param name="Where">Where on the entry it stands, in Swedish.</param>
    private sealed record Place(string Text, string Where);

    /// <summary>What a word was read as.</summary>
    /// <param name="Address">The address as it is looked up, or <see langword="null"/>.</param>
    /// <param name="Currency">Its currency code, or <see langword="null"/>.</param>
    /// <param name="Skipped">Whether it had an address's form but can never match.</param>
    private sealed record Reading(string? Address, string? Currency, bool Skipped);
}
