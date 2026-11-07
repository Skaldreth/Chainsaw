using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>
/// The sanction lists the user has imported. A list is read from a saved file (OFAC's SDN list as XML), never fetched
/// by Chainsaw, and is kept unchanged in the application data folder with its SHA-256 and dates, so that every hit can
/// be followed back to the exact file it came from.
/// </summary>
/// <remarks>The methods read and write files; call them off the UI thread. They are thread-safe.</remarks>
public interface ISanctionLists
{
    /// <summary>Gets the list hits are taken from: the one with the latest publish date, or none.</summary>
    SanctionListInfo? Current { get; }

    /// <summary>
    /// Imports the SDN list from a file the user saved. The file's bytes are kept as they are; importing the same
    /// file again changes nothing.
    /// </summary>
    /// <param name="content">The file's content.</param>
    /// <returns>What the list holds.</returns>
    /// <exception cref="SanctionListException">The file is not an SDN list Chainsaw can read.</exception>
    SanctionListInfo Import(Stream content);

    /// <summary>Lists the imported lists, the newest publish date first.</summary>
    /// <returns>The lists.</returns>
    IReadOnlyList<SanctionListInfo> List();

    /// <summary>Finds an address on the current list.</summary>
    /// <param name="address">The address.</param>
    /// <returns>
    /// The list and the entries that name the address, or <see langword="null"/> when no list is imported.
    /// </returns>
    /// <exception cref="SanctionListException">
    /// <see cref="SanctionListError.Tampered"/> when the stored file no longer has its hash.
    /// </exception>
    SanctionMatch? Find(BitcoinAddress address);
}

/// <summary>An imported sanction list.</summary>
/// <param name="Name">The list's name, <c>OFAC SDN</c>.</param>
/// <param name="Sha256">The SHA-256 of the file's bytes, in lower-case hex.</param>
/// <param name="PublishDate">The date the list says it was published.</param>
/// <param name="ImportedUtc">When it was imported, in UTC.</param>
/// <param name="AddressesByCurrency">
/// The number of digital currency addresses per currency code as the list writes it, for example <c>XBT</c>.
/// </param>
/// <param name="Skipped">
/// Addresses that are not valid for their chain and so can never match, for example an XBT address with a broken
/// checksum.
/// </param>
public sealed record SanctionListInfo(
    string Name,
    string Sha256,
    DateOnly PublishDate,
    DateTimeOffset ImportedUtc,
    IReadOnlyDictionary<string, int> AddressesByCurrency,
    int Skipped)
{
    /// <summary>Gets the list's version as an origin states it: the publish date and the hash.</summary>
    public string Version => $"publicerad {PublishDate:yyyy-MM-dd}, sha256 {Sha256}";
}

/// <summary>One digital currency address on a sanction list.</summary>
/// <param name="Currency">The currency code, for example <c>XBT</c> for Bitcoin.</param>
/// <param name="Address">The address as the list writes it.</param>
/// <param name="EntryUid">The list entry's uid.</param>
/// <param name="EntryName">The entry's name: the person or the entity.</param>
/// <param name="Programs">The sanction programmes the entry is listed under.</param>
public sealed record SanctionedAddress(
    string Currency, string Address, string EntryUid, string EntryName, IReadOnlyList<string> Programs);

/// <summary>An address looked up on a list.</summary>
/// <param name="List">The list.</param>
/// <param name="Hits">The entries that name the address; empty when it is not on the list.</param>
public sealed record SanctionMatch(SanctionListInfo List, IReadOnlyList<SanctionedAddress> Hits);

/// <summary>Why a sanction list could not be imported or read.</summary>
public enum SanctionListError
{
    /// <summary>The file is not XML in the SDN list's form.</summary>
    NotSdnList,

    /// <summary>The list does not say when it was published, so its origin cannot be stated.</summary>
    NoPublishDate,

    /// <summary>The file is larger than Chainsaw reads.</summary>
    TooLarge,

    /// <summary>A stored list no longer has the hash it was imported with.</summary>
    Tampered,
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
        SanctionListError.NotSdnList => "The file is not an SDN list in XML.",
        SanctionListError.NoPublishDate => "The list does not state its publish date.",
        SanctionListError.TooLarge => "The file is too large to be a sanction list.",
        SanctionListError.Tampered => "A stored sanction list no longer has its hash.",
        _ => "The sanction list could not be read.",
    };
}
