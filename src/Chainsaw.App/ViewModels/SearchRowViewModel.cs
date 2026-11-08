using Chainsaw.App.Graph;
using Chainsaw.App.Search;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// A row in the command bar's search (sketch KR1a): the node's form and colour from the graph, the name with what was
/// written in bold, a second line, what stands to the right, and the key's hint while it is chosen.
/// </summary>
public sealed partial class SearchRowViewModel : ObservableObject
{
    /// <summary>How many characters of an address or a hash a row shows before it is cut in the middle.</summary>
    public const int MonoRoom = 46;

    /// <summary>How many characters of a txid a row shows before it is cut in the middle.</summary>
    public const int TransactionRoom = 22;

    /// <summary>How many characters of a name a row shows before it is cut in the middle.</summary>
    public const int NameRoom = 60;

    /// <summary>The hint for a row shown in the graph.</summary>
    public const string ShowInGraphHint = "Visa i grafen";

    /// <summary>The hint for a Guide row.</summary>
    public const string OpenGuideHint = "Öppna i Guiden";

    /// <summary>The hint for a row the graph does not show.</summary>
    public const string NotInGraphHint = "Inte i grafen";

    /// <summary>The hint of a row that lies in the trash, whose Enter opens the trash (sketch SO1a).</summary>
    public const string InTrashHint = "Öppna soptunnan";

    /// <summary>Creates a row for a hit.</summary>
    /// <param name="hit">The hit.</param>
    /// <param name="isBest">Whether it is the best hit, drawn large at the top.</param>
    public SearchRowViewModel(SearchHit hit, bool isBest)
    {
        ArgumentNullException.ThrowIfNull(hit);
        Hit = hit;
        IsBest = isBest;
        Text = SearchText.Of(hit.Entry.Text, hit.Match, RoomOf(hit.Entry));
    }

    /// <summary>Gets the hit.</summary>
    public SearchHit Hit { get; }

    /// <summary>Gets the entry.</summary>
    public SearchEntry Entry => Hit.Entry;

    /// <summary>Gets whether it is the best hit.</summary>
    public bool IsBest { get; }

    /// <summary>Gets the name, split around what was written.</summary>
    public SearchText Text { get; }

    /// <summary>Gets the second line.</summary>
    public string Sub => Entry.Sub;

    /// <summary>Gets what stands to the right, or an empty text.</summary>
    public string Meta => Entry.Meta;

    /// <summary>Gets whether something stands to the right.</summary>
    public bool HasMeta => Entry.Meta.Length > 0;

    /// <summary>Gets the line under the best hit that says why.</summary>
    public string Lead => Entry.Lead;

    /// <summary>Gets the kind's name, small beside the best hit.</summary>
    public string KindText => Entry.Kind.Singular();

    /// <summary>Gets whether the name is a hash or an address, in the mono font.</summary>
    public bool IsMono => Entry.IsMono;

    /// <summary>Gets whether a label on it came only from other cases (the dashed ring).</summary>
    public bool IsFromOtherCase => Entry.IsFromOtherCase;

    /// <summary>Gets the icon's resource key.</summary>
    public string IconKey => Entry.Look.Icon;

    /// <summary>Gets the colour's resource key.</summary>
    public string InkKey => GraphInkKeys.Of(Entry.Look.Ink);

    /// <summary>Gets whether the frame is round, as for an actor we know nothing about.</summary>
    public bool IsRound => Entry.Look.Shape == NodeShape.Round;

    /// <summary>Gets whether the frame is dashed, as uncertain things are.</summary>
    public bool IsDashed => Entry.Look.IsDashed;

    /// <summary>Gets the graph's node the row is shown at, or <see langword="null"/>.</summary>
    public string? NodeKey => Entry.NodeKey;

    /// <summary>Gets the Guide section, for a Guide row; otherwise <see langword="null"/>.</summary>
    public string? GuideSection => Entry.GuideSection;

    /// <summary>Gets what Enter does with the row, as the hint beside it says.</summary>
    public string Hint => Entry switch
    {
        { GuideSection: not null } => OpenGuideHint,
        { NodeKey: not null } => ShowInGraphHint,
        { IsInTrash: true } => InTrashHint,
        _ => NotInGraphHint,
    };

    /// <summary>Gets or sets whether the row is chosen, which shows it in the graph or the Guide.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Gives how many characters of the entry's name a row shows.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The room.</returns>
    private static int RoomOf(SearchEntry entry) => entry switch
    {
        { Kind: SearchKind.Transaction } => TransactionRoom,
        { IsMono: true } => MonoRoom,
        _ => NameRoom,
    };
}

/// <summary>A group of rows under its heading, with <em>4 av 21 · Tab visar alla</em> when more exist.</summary>
/// <param name="Name">The heading, for example <c>Adresser</c>.</param>
/// <param name="Kind">The group's kind.</param>
/// <param name="Rows">The rows shown.</param>
/// <param name="MoreText">For example <c>4 av 21 · Tab visar alla</c>, or an empty text.</param>
public sealed record SearchGroupViewModel(
    string Name, SearchKind Kind, IReadOnlyList<SearchRowViewModel> Rows, string MoreText)
{
    /// <summary>Gets whether more hits exist than are shown.</summary>
    public bool HasMore => MoreText.Length > 0;
}
