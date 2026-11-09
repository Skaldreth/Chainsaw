using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution;

/// <summary>
/// Reads OFAC's SDN list in its XML form (<c>sdn.xml</c>): the publish date and every digital currency address
/// (<c>idType</c> <em>Digital Currency Address - XBT</em> and the like) with its entry. Nothing else is read.
/// </summary>
/// <remarks>
/// The reader refuses DTDs and external entities, so that a crafted file cannot read other files or expand without
/// bound. Element names are matched by their local name, whatever namespace the list uses.
/// </remarks>
internal static class OfacSdnParser
{
    /// <summary>The list's name as an origin states it.</summary>
    public const string ListName = "OFAC SDN";

    /// <summary>The start of the id type of a digital currency address.</summary>
    private const string CurrencyPrefix = "Digital Currency Address - ";

    /// <summary>The currency code of Bitcoin.</summary>
    public const string BitcoinCurrency = SanctionCurrency.Bitcoin;

    /// <summary>The form of the publish date on the list.</summary>
    private const string DateFormat = "MM/dd/yyyy";

    /// <summary>Reads the list.</summary>
    /// <param name="content">The file's bytes.</param>
    /// <returns>The publish date, the addresses and how many could never match.</returns>
    /// <exception cref="SanctionListException">The file is not an SDN list, or it has no publish date.</exception>
    public static ParsedSanctionList Parse(byte[] content)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = SanctionLists.MaximumLength,
        };
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var reader = XmlReader.Create(stream, settings);
            if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "sdnList")
            {
                throw new SanctionListException(SanctionListError.NotSdnList);
            }

            DateOnly? published = null;
            var addresses = new List<SanctionedAddress>();
            var skipped = 0;
            reader.Read();
            while (!reader.EOF)
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    reader.Read();
                    continue;
                }

                switch (reader.LocalName)
                {
                    case "publshInformation" or "publishInformation":
                        published = PublishDate((XElement)XNode.ReadFrom(reader));
                        break;
                    case "sdnEntry":
                        skipped += ReadEntry((XElement)XNode.ReadFrom(reader), addresses);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return new ParsedSanctionList(
                published ?? throw new SanctionListException(SanctionListError.NoPublishDate), addresses, skipped);
        }
        catch (XmlException exception)
        {
            throw new SanctionListException(SanctionListError.NotSdnList, exception);
        }
    }

    /// <summary>Gives the key an address is looked up by on the list, or none when it is not valid.</summary>
    /// <param name="address">The address as the list writes it.</param>
    /// <returns>The parsed address, or <see langword="null"/>.</returns>
    public static BitcoinAddress? BitcoinKey(SanctionedAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        Chain? chain = address.Currency switch
        {
            SanctionCurrency.Bitcoin => Chain.Bitcoin,
            SanctionCurrency.Litecoin => Chain.Litecoin,
            _ => null,
        };
        return chain is { } on && BitcoinAddress.TryParse(on, address.Address, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Gives the key an account is looked up by on the list, or none when the entry is not for an account chain.
    /// An Ethereum address (<c>0x</c> and 40 hex digits, under <see cref="SanctionCurrency.OnEthereum"/>) is kept in
    /// lower case, as the chain compares it; the letters' case is not checked, since an address the list wrote with a
    /// broken EIP-55 checksum still names those 20 bytes, and a sanction missed is worse than one shown that the user
    /// can look at. A TRON address (Base58Check with its checksum, under <see cref="SanctionCurrency.OnTron"/>) is
    /// kept as written: its checksum is part of it.
    /// </summary>
    /// <param name="address">The address as the list writes it.</param>
    /// <returns>The parsed address, or <see langword="null"/>.</returns>
    public static EthereumAddress? EthereumKey(SanctionedAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (SanctionCurrency.OnEthereum.Contains(address.Currency)
            && EthereumAddress.TryParse(address.Address.ToLowerInvariant(), out var ethereum))
        {
            return ethereum;
        }

        if (address.Currency == SanctionCurrency.Bsc
            && EthereumAddress.TryParse(Chain.Bsc, address.Address.ToLowerInvariant(), out var bsc))
        {
            return bsc;
        }

        return SanctionCurrency.OnTron.Contains(address.Currency)
            && address.Address.StartsWith('T') && EthereumAddress.TryParseTron(address.Address, out var tron, out _)
            ? tron
            : null;
    }


    /// <summary>Reads the publish date.</summary>
    /// <param name="information">The <c>publshInformation</c> element (the list spells it so).</param>
    /// <returns>The date, or <see langword="null"/> when it is missing or not a date.</returns>
    private static DateOnly? PublishDate(XElement information)
    {
        var text = Child(information, "Publish_Date");
        return DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>Reads an entry's digital currency addresses.</summary>
    /// <param name="entry">The <c>sdnEntry</c> element.</param>
    /// <param name="addresses">Receives the addresses.</param>
    /// <returns>How many of the entry's addresses can never match.</returns>
    private static int ReadEntry(XElement entry, List<SanctionedAddress> addresses)
    {
        var uid = Child(entry, "uid") ?? string.Empty;
        var name = string.Join(' ', new[] { Child(entry, "firstName"), Child(entry, "lastName") }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        var programs = Elements(entry, "programList").SelectMany(list => Elements(list, "program"))
            .Select(program => program.Value.Trim())
            .Where(program => program.Length > 0)
            .ToList();
        var skipped = 0;
        foreach (var id in Elements(entry, "idList").SelectMany(list => Elements(list, "id")))
        {
            var type = Child(id, "idType");
            var number = Child(id, "idNumber");
            if (type is null || !type.StartsWith(CurrencyPrefix, StringComparison.Ordinal) || number is null)
            {
                continue;
            }

            var address = new SanctionedAddress(type[CurrencyPrefix.Length..].Trim(), number, uid, name, programs);
            if (NeverMatches(address))
            {
                skipped++;
                continue;
            }

            addresses.Add(address);
        }

        return skipped;
    }

    /// <summary>
    /// Says whether an address of the list is not valid on the chain its code names, so that it can never match: a
    /// Bitcoin address that does not parse, or an ether address that is not <c>0x</c> and 40 hex digits. A token's
    /// address that is not an Ethereum address may be one on another chain (USDT on TRON), and is kept.
    /// </summary>
    /// <param name="address">The address as the list writes it.</param>
    /// <returns><see langword="true"/> when it can never match.</returns>
    private static bool NeverMatches(SanctionedAddress address) => address.Currency switch
    {
        BitcoinCurrency => BitcoinKey(address) is null,
        SanctionCurrency.Ether => EthereumKey(address) is null,
        _ => false,
    };

    /// <summary>Gives a child element's trimmed text.</summary>
    /// <param name="parent">The parent.</param>
    /// <param name="name">The child's local name.</param>
    /// <returns>The text, or <see langword="null"/> when there is no such child or it is empty.</returns>
    private static string? Child(XElement parent, string name)
    {
        var text = Elements(parent, name).FirstOrDefault()?.Value.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>Gives the child elements with a local name, whatever their namespace.</summary>
    /// <param name="parent">The parent.</param>
    /// <param name="name">The local name.</param>
    /// <returns>The elements.</returns>
    private static IEnumerable<XElement> Elements(XElement parent, string name) =>
        parent.Elements().Where(element => element.Name.LocalName == name);
}

/// <summary>What the SDN list holds.</summary>
/// <param name="PublishDate">The date the list says it was published.</param>
/// <param name="Addresses">The digital currency addresses that can match.</param>
/// <param name="Skipped">Addresses that are not valid for their chain.</param>
internal sealed record ParsedSanctionList(
    DateOnly PublishDate, IReadOnlyList<SanctionedAddress> Addresses, int Skipped);
