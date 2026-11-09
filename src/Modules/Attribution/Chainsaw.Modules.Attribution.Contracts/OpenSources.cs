using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>
/// The open sources of labels the user has imported: a data set saved as a file (the first is Ransomwhere's list of
/// ransomware payment addresses), never fetched by Chainsaw, kept unchanged in the application data folder with its
/// SHA-256, its licence and the date of its data, so that every label can be followed back to the exact file. A
/// source is built in only when its terms allow it (the user's decision 2026-11-08: licence and quality are checked
/// first); what an open source says is a judgement with its source, never a fact (principle 2).
/// </summary>
/// <remarks>The methods read and write files; call them off the UI thread. They are thread-safe.</remarks>
public interface IOpenSourceLists
{
    /// <summary>Gets the list labels are taken from for each source: its latest data, the newest first.</summary>
    IReadOnlyList<OpenSourceListInfo> CurrentLists { get; }

    /// <summary>
    /// Imports an open source from a file the user saved, told apart by its content. The file's bytes are kept as
    /// they are; importing the same file again changes nothing.
    /// </summary>
    /// <param name="content">The file's content.</param>
    /// <returns>What the source holds.</returns>
    /// <exception cref="OpenSourceListException">
    /// The file is not a source Chainsaw reads, or it cannot be read.
    /// </exception>
    OpenSourceListInfo Import(Stream content);

    /// <summary>Lists the imported sources, the newest data first.</summary>
    /// <returns>The sources.</returns>
    IReadOnlyList<OpenSourceListInfo> List();

    /// <summary>
    /// Finds many addresses in every current source at once, reading which sources are current only once. A source
    /// whose file has lost its hash is passed over and named in <see cref="OpenSourceLookup.Unusable"/>.
    /// </summary>
    /// <param name="addresses">The addresses with their chain.</param>
    /// <returns>The sources looked in, those that could not be, and what they say about each address.</returns>
    OpenSourceLookup Find(IReadOnlyCollection<ChainAddress> addresses);

    /// <summary>Removes an imported source from the data folder; labels sealed in cases keep its hash.</summary>
    /// <param name="sha256">The source's <see cref="OpenSourceListInfo.Sha256"/>.</param>
    /// <returns><see langword="false"/> when no such source is stored.</returns>
    bool Remove(string sha256);
}

/// <summary>An imported open source.</summary>
/// <param name="Name">The source's name, for example <c>Ransomwhere</c>.</param>
/// <param name="License">The licence the data is published under, with the attribution it asks for.</param>
/// <param name="Sha256">The SHA-256 of the file's bytes, in lower-case hex.</param>
/// <param name="DataDate">The date of the newest record in the file, as the data states it.</param>
/// <param name="ImportedUtc">When it was imported, in UTC.</param>
/// <param name="AddressesByChain">The number of addresses per chain, by the chain's name.</param>
/// <param name="Skipped">Records that name no address Chainsaw can read on a chain it knows.</param>
public sealed record OpenSourceListInfo(
    string Name,
    string License,
    string Sha256,
    DateOnly DataDate,
    DateTimeOffset ImportedUtc,
    IReadOnlyDictionary<string, int> AddressesByChain,
    int Skipped)
{
    /// <summary>Gets the source's version as an origin states it: the date of its data and the hash.</summary>
    public string Version => $"data till {DataDate:yyyy-MM-dd}, sha256 {Sha256}";
}

/// <summary>What an open source says about an address.</summary>
/// <param name="Address">The address with its chain.</param>
/// <param name="Kind">What the label says, for a ransomware payment address <see cref="LabelKind.Reported"/>.</param>
/// <param name="Name">The label's name, for example <c>Ransomware: Netwalker</c>.</param>
/// <param name="Detail">What the record says, in Swedish, for the label's reason.</param>
public sealed record OpenSourceEntry(ChainAddress Address, LabelKind Kind, string Name, string Detail);

/// <summary>An address found on a source.</summary>
/// <param name="List">The source.</param>
/// <param name="Hits">What it says about the address.</param>
public sealed record OpenSourceMatch(OpenSourceListInfo List, IReadOnlyList<OpenSourceEntry> Hits);

/// <summary>What a check against the open sources found.</summary>
/// <param name="Lists">The sources that were used; empty when none is imported.</param>
/// <param name="Added">The new labels.</param>
/// <param name="AlreadyLabelled">Hits that were already labelled from the same file.</param>
public sealed record OpenSourceCheck(
    IReadOnlyList<OpenSourceListInfo> Lists, IReadOnlyList<LabelEntry> Added, int AlreadyLabelled)
{
    /// <summary>Gets the current sources that could not be looked in, since their file has lost its hash.</summary>
    public IReadOnlyList<OpenSourceListInfo> Unusable { get; init; } = [];
}

/// <summary>Many addresses looked up in the current open sources.</summary>
/// <param name="Lists">The sources looked in.</param>
/// <param name="Unusable">The current sources that could not be looked in, since their file has lost its hash.</param>
/// <param name="Matches">What the sources say about each address they name.</param>
public sealed record OpenSourceLookup(
    IReadOnlyList<OpenSourceListInfo> Lists,
    IReadOnlyList<OpenSourceListInfo> Unusable,
    IReadOnlyDictionary<ChainAddress, IReadOnlyList<OpenSourceMatch>> Matches);

/// <summary>Why an open source could not be imported or read.</summary>
public enum OpenSourceListError
{
    /// <summary>The file is not a source Chainsaw reads.</summary>
    NotKnown,

    /// <summary>The file does not say how new its data is, so its version cannot be stated.</summary>
    NoDate,

    /// <summary>The file is larger than Chainsaw reads.</summary>
    TooLarge,

    /// <summary>A stored source no longer has the hash it was imported with.</summary>
    Tampered,

    /// <summary>
    /// The source's newest record is dated after today; as the newest file of its source it would stand in for every
    /// file imported later, so it is refused.
    /// </summary>
    FutureDate,
}

/// <summary>An open source could not be imported or read. The message never contains the file's content.</summary>
public sealed class OpenSourceListException : Exception
{
    /// <summary>Creates the exception for a reason.</summary>
    /// <param name="error">Why.</param>
    public OpenSourceListException(OpenSourceListError error)
        : base(MessageFor(error))
    {
        Error = error;
    }

    /// <summary>Creates the exception for a reason with the underlying failure.</summary>
    /// <param name="error">Why.</param>
    /// <param name="innerException">The failure that caused it.</param>
    public OpenSourceListException(OpenSourceListError error, Exception innerException)
        : base(MessageFor(error), innerException)
    {
        Error = error;
    }

    /// <summary>Gets why.</summary>
    public OpenSourceListError Error { get; }

    /// <summary>Gives the fixed message for a reason.</summary>
    /// <param name="error">The reason.</param>
    /// <returns>An English message for logs; the user interface shows its own Swedish text.</returns>
    private static string MessageFor(OpenSourceListError error) => error switch
    {
        OpenSourceListError.NotKnown => "The file is not an open source Chainsaw reads.",
        OpenSourceListError.NoDate => "The source does not state how new its data is.",
        OpenSourceListError.TooLarge => "The file is too large to be an open source.",
        OpenSourceListError.Tampered => "A stored open source no longer has its hash.",
        OpenSourceListError.FutureDate => "The source's data is dated after today.",
        _ => "The open source could not be read.",
    };
}
