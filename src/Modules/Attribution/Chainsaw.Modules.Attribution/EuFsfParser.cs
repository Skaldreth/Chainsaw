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
    /// <returns>The date the file was generated, the addresses and how many could never match (always none).</returns>
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
            reader.Read();
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sanctionEntity")
                {
                    ReadEntity(reader, addresses);
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
                0);
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
    private static void ReadEntity(XmlReader reader, List<SanctionedAddress> addresses)
    {
        var reference = reader.GetAttribute("euReferenceNumber") ?? reader.GetAttribute("logicalId") ?? string.Empty;
        string? strongName = null;
        string? anyName = null;
        var programmes = new List<string>();
        var found = new List<(string Text, string ListedAs)>();
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
                    var type = SanctionXml.Attribute(part, "identificationTypeDescription")
                        ?? SanctionXml.Attribute(part, "identificationTypeCode") ?? "identifiering";
                    found.AddRange(new[] { "number", "latinNumber" }
                        .Select(name => SanctionXml.Attribute(part, name))
                        .OfType<string>()
                        .Select(number => (number, $"identifiering: {type}")));
                    found.AddRange(Remarks(part));
                    break;
                case "remark":
                    found.Add((part.Value, InRemark));
                    break;
                default:
                    found.AddRange(Remarks(part));
                    break;
            }
        }

        var name = strongName ?? anyName ?? string.Empty;
        IReadOnlyList<string> programs = [.. programmes.Distinct(StringComparer.Ordinal)];
        foreach (var (address, currency, listedAs) in found
            .SelectMany(text => Addresses(text.Text).Select(each => (each.Address, each.Currency, text.ListedAs)))
            .DistinctBy(each => (each.Address, each.Currency)))
        {
            addresses.Add(new SanctionedAddress(currency, address, reference, name, programs) { ListedAs = listedAs });
        }
    }

    /// <summary>Gives the remarks inside a part.</summary>
    /// <param name="part">The part.</param>
    /// <returns>Each remark's text, marked as a remark.</returns>
    private static IEnumerable<(string Text, string ListedAs)> Remarks(XElement part) =>
        part.Descendants().Where(element => element.Name.LocalName == "remark")
            .Select(remark => (remark.Value, InRemark));

    /// <summary>
    /// Finds the Bitcoin and Ethereum addresses in a text by their form: each word that is a Bitcoin address with a
    /// valid checksum, or <c>0x</c> and 40 hex digits (kept as the list writes it).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>Each address with the currency code it is looked up under.</returns>
    internal static IEnumerable<(string Address, string Currency)> Addresses(string text)
    {
        foreach (var word in Words().Split(text))
        {
            if (word.Length == 0)
            {
                continue;
            }

            if (BitcoinAddress.TryParse(word, out var bitcoin))
            {
                yield return (bitcoin.Value, SanctionCurrency.Bitcoin);
            }
            else if (EthereumAddress.TryParse(word.ToLowerInvariant(), out _))
            {
                yield return (word, SanctionCurrency.Ether);
            }
        }
    }

    /// <summary>
    /// Splits a text into words at white space and punctuation that is never part of an address (no Bitcoin or
    /// Ethereum address has a full stop, comma or bracket in it).
    /// </summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"[\s.,;:()\[\]{}<>""'/|]+", RegexOptions.CultureInvariant)]
    private static partial Regex Words();
}
