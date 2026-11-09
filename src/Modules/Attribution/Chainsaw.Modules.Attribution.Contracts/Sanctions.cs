using Chainsaw.SharedKernel;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>
/// The sanction lists the user has imported. A list is read from a saved file (OFAC's SDN list as XML, or the EU's
/// consolidated list of financial sanctions as XML), never fetched by Chainsaw, and is kept unchanged in the
/// application data folder with its SHA-256 and dates, so that every hit can be followed back to the exact file it came
/// from. Each kind of list has its own current list, the one with the latest date, and an address is looked up on all
/// of them.
/// </summary>
/// <remarks>The methods read and write files; call them off the UI thread. They are thread-safe.</remarks>
public interface ISanctionLists
{
    /// <summary>
    /// Gets the newest current list (see <see cref="CurrentLists"/>), the one with the latest publish date of any kind,
    /// or none; the list <see cref="Find(BitcoinAddress)"/> looks on.
    /// </summary>
    SanctionListInfo? Current { get; }

    /// <summary>
    /// Gets the list hits are taken from for each kind of list (OFAC SDN, EU FSF): the one of that kind with the latest
    /// publish date, the newest first. By default only <see cref="Current"/>.
    /// </summary>
    IReadOnlyList<SanctionListInfo> CurrentLists => Current is { } current ? [current] : [];

    /// <summary>
    /// Imports a sanction list from a file the user saved: OFAC's SDN list (<c>sdn.xml</c>) or the EU's consolidated
    /// list (the FSF file in XML), told apart by the file's content. The file's bytes are kept as they are; importing
    /// the same file again changes nothing.
    /// </summary>
    /// <param name="content">The file's content.</param>
    /// <returns>What the list holds.</returns>
    /// <exception cref="SanctionListException">
    /// The file is not a list Chainsaw can read, or it says it was published after today.
    /// </exception>
    SanctionListInfo Import(Stream content);

    /// <summary>Lists the imported lists, the newest publish date first.</summary>
    /// <returns>The lists.</returns>
    IReadOnlyList<SanctionListInfo> List();

    /// <summary>Finds an address on the newest current list (<see cref="Current"/>).</summary>
    /// <param name="address">The address.</param>
    /// <returns>
    /// The list and the entries that name the address, or <see langword="null"/> when no list is imported.
    /// </returns>
    /// <exception cref="SanctionListException">
    /// <see cref="SanctionListError.Tampered"/> when the stored file no longer has its hash.
    /// </exception>
    SanctionMatch? Find(BitcoinAddress address);

    /// <summary>
    /// Finds an account on the current list. On Ethereum: entries for ether and for the tokens on Ethereum the list
    /// names (<see cref="SanctionCurrency.OnEthereum"/>), compared without regard to the letters' case. On TRON: entries
    /// for TRX and for USDT and USDC written with a TRON address (<see cref="SanctionCurrency.OnTron"/>), compared
    /// exactly, since Base58Check is case sensitive.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>
    /// The list and the entries that name the address, or <see langword="null"/> when no list is imported.
    /// </returns>
    /// <exception cref="SanctionListException">
    /// <see cref="SanctionListError.Tampered"/> when the stored file no longer has its hash.
    /// </exception>
    SanctionMatch? Find(EthereumAddress address);

    /// <summary>
    /// Finds an address on every current list that can be read (<see cref="CurrentLists"/>); a list whose file has
    /// lost its hash is passed over, as <see cref="FindMany"/> says.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>The lists that name the address; empty when none does.</returns>
    IReadOnlyList<SanctionMatch> FindAll(BitcoinAddress address) =>
        Find(address) is { Hits.Count: > 0 } match ? [match] : [];

    /// <summary>
    /// Finds an Ethereum address on every current list that can be read, as <see cref="Find(EthereumAddress)"/> does
    /// on one.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>The lists that name the address; empty when none does.</returns>
    IReadOnlyList<SanctionMatch> FindAll(EthereumAddress address) =>
        Find(address) is { Hits.Count: > 0 } match ? [match] : [];

    /// <summary>
    /// Finds many addresses on every current list at once, each on its own chain (Bitcoin and Ethereum), reading which
    /// lists are current only once. A list whose file has lost its hash is passed over and named in
    /// <see cref="SanctionLookup.Unusable"/>, so that one changed list never keeps the others from being looked on.
    /// </summary>
    /// <param name="addresses">The addresses, each with its chain.</param>
    /// <returns>The lists looked on, those that could not be, and the hits of each address that has any.</returns>
    SanctionLookup FindMany(IReadOnlyCollection<ChainAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var matches = new Dictionary<ChainAddress, IReadOnlyList<SanctionMatch>>();
        foreach (var address in addresses.Distinct())
        {
            IReadOnlyList<SanctionMatch> found = address.Chain switch
            {
                Chain.Bitcoin when BitcoinAddress.TryParse(address.Value, out var bitcoin) => FindAll(bitcoin),
                Chain.Ethereum when EthereumAddress.TryParse(address.Value, out var account) => FindAll(account),
                Chain.Litecoin when BitcoinAddress.TryParse(Chain.Litecoin, address.Value, out var litecoin) =>
                    FindAll(litecoin),
                Chain.Bsc when EthereumAddress.TryParse(Chain.Bsc, address.Value, out var bsc) => FindAll(bsc),
                _ => [],
            };
            if (found.Count > 0)
            {
                matches[address] = found;
            }
        }

        return new SanctionLookup(CurrentLists, [], matches);
    }

    /// <summary>
    /// Removes an imported list from the data folder, for example one with a wrong date that would stay current
    /// (the review of etapp 8, M5). Labels already sealed in cases keep the list's hash and date; the file is gone
    /// from this computer.
    /// </summary>
    /// <param name="sha256">The list's <see cref="SanctionListInfo.Sha256"/>.</param>
    /// <returns><see langword="false"/> when no such list is stored.</returns>
    bool Remove(string sha256) => false;
}

/// <summary>The currency codes a sanction list writes, as Chainsaw reads them.</summary>
public static class SanctionCurrency
{
    /// <summary>The code OFAC's SDN list gives Bitcoin addresses.</summary>
    public const string Bitcoin = "XBT";

    /// <summary>
    /// The code OFAC's SDN list gives ether addresses; an Ethereum address on the EU's list is read with it.
    /// </summary>
    public const string Ether = "ETH";

    /// <summary>
    /// The codes whose addresses are Ethereum addresses: ether, and USDT and USDC, which the list writes with the
    /// address that holds them on Ethereum (an address under USDT that is not an Ethereum address, such as one on
    /// TRON, is not one of them). Other chains that share Ethereum's form of address (BSC, Arbitrum, Ethereum Classic)
    /// are other chains, and are not counted.
    /// </summary>
    public static IReadOnlySet<string> OnEthereum { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Ether, "USDT", "USDC",
    };

    /// <summary>The code OFAC's SDN list gives TRX addresses.</summary>
    public const string Tronix = "TRX";

    /// <summary>
    /// The codes whose addresses can be TRON addresses: TRX, and USDT and USDC, which the list writes with the address
    /// that holds them, on TRON a Base58Check address beginning with <c>T</c>. Only an address that is one counts;
    /// the same code with an Ethereum address is Ethereum's (<see cref="OnEthereum"/>).
    /// </summary>
    public static IReadOnlySet<string> OnTron { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Tronix, "USDT", "USDC",
    };

    /// <summary>
    /// The code OFAC's SDN list gives BSC addresses (<c>0x</c> and 40 hex digits). The list writes USDT and USDC with
    /// an address but not the chain; a token entry with Ethereum's form of address is read as Ethereum's
    /// (<see cref="OnEthereum"/>), so only the entries the list itself names BSC are BSC's. Its <c>BNB</c> entries are
    /// addresses on BNB's former Beacon Chain (<c>bnb1…</c>), which is not BSC, and are not counted.
    /// </summary>
    public const string Bsc = "BSC";

    /// <summary>The code OFAC's SDN list gives Litecoin addresses (<c>L…</c>, <c>M…</c>, <c>ltc1…</c>).</summary>
    public const string Litecoin = "LTC";

    /// <summary>Says whether a code is one of an account chain's (Ethereum's, TRON's or BSC's).</summary>
    /// <param name="currency">The code.</param>
    /// <returns><see langword="true"/> when an account can be listed under it.</returns>
    public static bool OnAccountChain(string currency) =>
        OnEthereum.Contains(currency) || OnTron.Contains(currency) || currency == Bsc;

    /// <summary>Says whether a code is one of a chain's own: an address on the chain can be listed under it.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="currency">The code.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool On(Chain chain, string currency) => chain switch
    {
        Chain.Bitcoin => currency == Bitcoin,
        Chain.Litecoin => currency == Litecoin,
        Chain.Ethereum => OnEthereum.Contains(currency),
        Chain.Tron => OnTron.Contains(currency),
        Chain.Bsc => currency == Bsc,
        _ => false,
    };
}

/// <summary>An imported sanction list.</summary>
/// <param name="Name">The list's name, <c>OFAC SDN</c> or <c>EU FSF</c>.</param>
/// <param name="Sha256">The SHA-256 of the file's bytes, in lower-case hex.</param>
/// <param name="PublishDate">The date the list says it was published.</param>
/// <param name="ImportedUtc">When it was imported, in UTC.</param>
/// <param name="AddressesByCurrency">
/// The number of digital currency addresses per currency code as the list writes it, for example <c>XBT</c>.
/// </param>
/// <param name="Skipped">
/// Addresses that are not valid for their chain and so can never match, for example an XBT address with a broken
/// checksum, or on the EU's list one labelled for another chain or on a test network.
/// </param>
public sealed record SanctionListInfo(
    string Name,
    string Sha256,
    DateOnly PublishDate,
    DateTimeOffset ImportedUtc,
    IReadOnlyDictionary<string, int> AddressesByCurrency,
    int Skipped)
{
    /// <summary>
    /// Gets how many of the addresses are uncertain (<see cref="SanctionedAddress.IsUncertain"/>): counted in
    /// <see cref="AddressesByCurrency"/>, but labelled only as possible sanctions.
    /// </summary>
    public int Uncertain { get; init; }

    /// <summary>Gets the list's version as an origin states it: the publish date and the hash.</summary>
    public string Version => $"publicerad {PublishDate:yyyy-MM-dd}, sha256 {Sha256}";
}

/// <summary>One digital currency address on a sanction list.</summary>
/// <param name="Currency">
/// The currency code, for example <c>XBT</c> for Bitcoin; on the EU's list, which has no codes, the code of the chain
/// whose form the address has.
/// </param>
/// <param name="Address">The address as the list writes it.</param>
/// <param name="EntryUid">The list entry's uid; on the EU's list its EU reference number.</param>
/// <param name="EntryName">The entry's name: the person or the entity.</param>
/// <param name="Programs">The sanction programmes the entry is listed under.</param>
public sealed record SanctionedAddress(
    string Currency, string Address, string EntryUid, string EntryName, IReadOnlyList<string> Programs)
{
    /// <summary>
    /// Gets where on the entry the address stood when the list has no type of its own for it (the EU's list: an
    /// identification's type, or a remark); <see langword="null"/> on OFAC's list, where it is a
    /// <em>Digital Currency Address</em>.
    /// </summary>
    public string? ListedAs { get; init; }

    /// <summary>
    /// Gets whether the list does not say for certain which chain the address is on (the EU's list: no label for a
    /// currency before it, or one that may be a company's name). Such a hit is a possible sanction to look at, never a
    /// sanction (<see cref="LabelKind.PossiblySanctioned"/>).
    /// </summary>
    public bool IsUncertain { get; init; }
}

/// <summary>Many addresses looked up on the current lists.</summary>
/// <param name="Lists">The lists looked on, the newest first.</param>
/// <param name="Unusable">The current lists that could not be looked on, since their file has lost its hash.</param>
/// <param name="Matches">The lists that name each address, for each address named by any.</param>
public sealed record SanctionLookup(
    IReadOnlyList<SanctionListInfo> Lists,
    IReadOnlyList<SanctionListInfo> Unusable,
    IReadOnlyDictionary<ChainAddress, IReadOnlyList<SanctionMatch>> Matches)
{
    /// <summary>Gets the current lists whose file is gone from the computer since the folder was read.</summary>
    public IReadOnlyList<SanctionListInfo> Missing { get; init; } = [];
}

/// <summary>An address looked up on a list.</summary>
/// <param name="List">The list.</param>
/// <param name="Hits">The entries that name the address; empty when it is not on the list.</param>
public sealed record SanctionMatch(SanctionListInfo List, IReadOnlyList<SanctionedAddress> Hits);

/// <summary>Why a sanction list could not be imported or read.</summary>
public enum SanctionListError
{
    /// <summary>The file is not XML in the form of a list Chainsaw reads (OFAC's SDN list or the EU's list).</summary>
    NotSdnList,

    /// <summary>The list does not say when it was published, so its origin cannot be stated.</summary>
    NoPublishDate,

    /// <summary>The file is larger than Chainsaw reads.</summary>
    TooLarge,

    /// <summary>A stored list no longer has the hash it was imported with.</summary>
    Tampered,

    /// <summary>A stored list's file is gone since the folder was read; it is asked for again later.</summary>
    Missing,

    /// <summary>
    /// The list says it was published after today; as the newest list it would stand in for every list imported
    /// later, so it is refused.
    /// </summary>
    FutureDate,
}

/// <summary>A sanction list could not be imported or read. The message never contains the list's content.</summary>
public sealed class SanctionListException : Exception
{
    /// <summary>Creates the exception for a reason.</summary>
    /// <param name="error">Why.</param>
    public SanctionListException(SanctionListError error)
        : base(MessageFor(error))
    {
        Error = error;
    }

    /// <summary>Creates the exception for a reason with the underlying failure.</summary>
    /// <param name="error">Why.</param>
    /// <param name="innerException">The failure that caused it.</param>
    public SanctionListException(SanctionListError error, Exception innerException)
        : base(MessageFor(error), innerException)
    {
        Error = error;
    }

    /// <summary>Gets why.</summary>
    public SanctionListError Error { get; }

    /// <summary>Gives the fixed message for a reason.</summary>
    /// <param name="error">The reason.</param>
    /// <returns>An English message for logs; the user interface shows its own Swedish text.</returns>
    private static string MessageFor(SanctionListError error) => error switch
    {
        SanctionListError.NotSdnList => "The file is not a sanction list Chainsaw can read.",
        SanctionListError.NoPublishDate => "The list does not state its publish date.",
        SanctionListError.TooLarge => "The file is too large to be a sanction list.",
        SanctionListError.Tampered => "A stored sanction list no longer has its hash.",
        SanctionListError.Missing => "A stored sanction list's file is gone.",
        SanctionListError.FutureDate => "The list says it was published after today.",
        _ => "The sanction list could not be read.",
    };
}
