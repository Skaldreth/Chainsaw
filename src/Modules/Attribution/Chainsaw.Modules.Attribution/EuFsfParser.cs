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
                    case { Address: { } address, Currency: { } currency } reading when seen.Add((address, currency)):
                        addresses.Add(new SanctionedAddress(currency, address, reference, name, programs)
                        {
                            ListedAs = $"{place.Where}: ”{Excerpt(place.Text, word)}”",
                            IsUncertain = reading.IsUncertain,
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
    /// Reads a word as an address by its form and by the nearest label of a currency or chain before it in the same
    /// part of the text (split at <c>;</c>, commas and line breaks), as the EU writes several chains in one remark:
    /// <c>XBT 1A…; ETH 0x…; USDT (TRC-20) T…</c> (the review of round 5, I-a).
    /// <list type="bullet">
    /// <item>A label for Bitcoin before a Bitcoin address on the main network, or for Ethereum before <c>0x</c> and 40
    /// hex digits, is a hit.</item>
    /// <item>A SegWit address (<c>bc1…</c>) is Bitcoin's alone and a hit without a label.</item>
    /// <item>A label for another chain (BCH, Litecoin, TRC-20, BEP-20 …), a test network or a TRON address is skipped
    /// and counted.</item>
    /// <item>Anything else (no label, a name that may be a company's such as Polygon, a label that does not fit the
    /// form) is uncertain: a possible sanction, never a sanction.</item>
    /// </list>
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
            return new Reading(null, null, Tron().IsMatch(value), false);
        }

        var label = LabelBefore(text, word);
        if (label == ChainLabel.Other || (isBitcoin && bitcoin!.Network != BitcoinNetwork.Mainnet))
        {
            return new Reading(null, null, true, false);
        }

        if (isBitcoin)
        {
            var certain = label == ChainLabel.Bitcoin || (label == ChainLabel.None && IsSegWit(bitcoin!));
            return new Reading(bitcoin!.Value, SanctionCurrency.Bitcoin, false, !certain);
        }

        return new Reading(value, SanctionCurrency.Ether, false, label != ChainLabel.Ethereum);
    }

    /// <summary>
    /// Says whether a Bitcoin address is a SegWit or Taproot one (<c>bc1…</c>), a form no other chain uses; a Base58
    /// address (<c>1…</c>, <c>3…</c>) has the same form on Bitcoin Cash and, for <c>3…</c>, on Litecoin.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> for a SegWit or Taproot address.</returns>
    private static bool IsSegWit(BitcoinAddress address) =>
        address.Type is not (BitcoinScriptType.PayToPublicKeyHash or BitcoinScriptType.PayToScriptHash);

    /// <summary>Finds the Bitcoin and Ethereum addresses in a text, as <see cref="Read"/> reads each word.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Each address with the currency code it is looked up under, and whether it is uncertain.</returns>
    internal static IEnumerable<(string Address, string Currency, bool IsUncertain)> Addresses(string text) =>
        Words().Matches(text).Cast<Match>()
            .Select(word => Read(text, word))
            .Where(reading => reading.Address is not null)
            .Select(reading => (reading.Address!, reading.Currency!, reading.IsUncertain));

    /// <summary>
    /// Gives the label of a currency or chain nearest before a word, in the same part of the text: from the last
    /// <c>;</c>, comma or line break before it.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="word">The word.</param>
    /// <returns>The chain the nearest label names, or none.</returns>
    private static ChainLabel LabelBefore(string text, Match word)
    {
        var start = text.LastIndexOfAny([';', ',', '\n', '\r'], Math.Max(0, word.Index - 1)) + 1;
        var labels = Labels().Matches(text[start..word.Index]);
        if (labels.Count == 0)
        {
            return ChainLabel.None;
        }

        var last = labels[^1].Groups;
        if (last["bitcoin"].Success)
        {
            return ChainLabel.Bitcoin;
        }

        if (last["ethereum"].Success)
        {
            return ChainLabel.Ethereum;
        }

        return last["other"].Success ? ChainLabel.Other : ChainLabel.Uncertain;
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

    /// <summary>About how long an excerpt is.</summary>
    private const int ExcerptLength = 100;

    /// <summary>The words an address may be: letters and digits, so that punctuation never is part of one.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"[A-Za-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex Words();

    /// <summary>
    /// The labels of currencies and chains, as whole words in any case: Bitcoin (<c>XBT</c>, <c>BTC</c>), Ethereum
    /// (<c>ETH</c>, <c>ERC-20</c>), other chains that use the same forms of address (Bitcoin Cash and SV, Litecoin,
    /// Dogecoin, TRON, BNB Smart Chain, Ethereum Classic), and names of chains that are also used as names of
    /// companies (Polygon, Arbitrum …), which only make an address uncertain.
    /// </summary>
    /// <returns>The pattern, with the groups <c>bitcoin</c>, <c>ethereum</c>, <c>other</c> and <c>weak</c>.</returns>
    [GeneratedRegex(
        @"\b(?:(?<other>bitcoin[\s-]?cash|bch|bitcoin[\s-]?sv|bsv|litecoin|ltc|dogecoin|doge|trx|tron|trc[\s-]?20|"
        + @"bep[\s-]?20|bep2|bsc|bnb|binance[\s-]?smart[\s-]?chain|ethereum[\s-]?classic)"
        + @"|(?<bitcoin>xbt|btc|bitcoin)|(?<ethereum>eth|ethereum|erc[\s-]?20|ether)"
        + @"|(?<weak>polygon|matic|arbitrum|optimism|avalanche|avax|fantom|ftm|etc|base))\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Labels();

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
    /// <param name="IsUncertain">Whether the text does not say for certain which chain it is on.</param>
    private sealed record Reading(string? Address, string? Currency, bool Skipped, bool IsUncertain);

    /// <summary>What the nearest label before an address names.</summary>
    private enum ChainLabel
    {
        /// <summary>No label.</summary>
        None,

        /// <summary>Bitcoin.</summary>
        Bitcoin,

        /// <summary>Ethereum.</summary>
        Ethereum,

        /// <summary>Another chain that uses the same forms of address.</summary>
        Other,

        /// <summary>A name that may be a chain's or a company's.</summary>
        Uncertain,
    }
}
