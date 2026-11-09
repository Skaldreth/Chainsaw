using System.Globalization;
using System.Runtime.CompilerServices;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>The sources the view Etiketter filters on (sketch ET1a), in the order of its left column.</summary>
public enum LabelFilter
{
    /// <summary>A sanction list the user imported.</summary>
    Sanction,

    /// <summary>The user's own labels.</summary>
    Own,

    /// <summary>An open source: one built in (Ransomwhere) or one the user named in the form.</summary>
    OpenSource,

    /// <summary>A supplier of attribution, for example Chainabuse.</summary>
    Provider,

    /// <summary>An analysis of behaviour on the chain: a suggestion, never a fact.</summary>
    Behaviour,

    /// <summary>Another case, through the label library on this computer.</summary>
    OtherCase,
}

/// <summary>Whether the view Etiketter lists the case's labels or the library's.</summary>
public enum LabelScope
{
    /// <summary>The labels in the open case.</summary>
    Case,

    /// <summary>The label library: the labels from every case opened on this computer.</summary>
    Library,
}

/// <summary>Where a label's actor is in the graph shown (sketch ET1a).</summary>
public enum LabelPlace
{
    /// <summary>Not in the graph.</summary>
    None,

    /// <summary>Drawn on the surface.</summary>
    Graph,

    /// <summary>A card in a pile (sketch EX2b): drawn as the pile.</summary>
    Pile,

    /// <summary>In the trash (sketch SO1a): hidden, but still in the case.</summary>
    Trash,
}

/// <summary>What the view Etiketter writes and draws for each source, stated once.</summary>
public static class LabelFilters
{
    /// <summary>Gets every filter in the order of the left column.</summary>
    public static IReadOnlyList<LabelFilter> All { get; } =
    [
        LabelFilter.Sanction, LabelFilter.Own, LabelFilter.OpenSource, LabelFilter.Provider, LabelFilter.Behaviour,
        LabelFilter.OtherCase,
    ];

    /// <summary>Gives the filter a source belongs to.</summary>
    /// <param name="source">How the case got a label.</param>
    /// <returns>The filter.</returns>
    public static LabelFilter Of(LabelSource source) => source switch
    {
        LabelSource.SanctionList => LabelFilter.Sanction,
        LabelSource.OpenSource => LabelFilter.OpenSource,
        LabelSource.Provider => LabelFilter.Provider,
        LabelSource.Behaviour => LabelFilter.Behaviour,
        LabelSource.OtherCase => LabelFilter.OtherCase,
        _ => LabelFilter.Own,
    };

    /// <summary>Gives the source's icon, as in the sketch: a source is only an icon.</summary>
    /// <param name="filter">The source.</param>
    /// <returns>The icon's resource key.</returns>
    public static string Icon(LabelFilter filter) => filter switch
    {
        LabelFilter.Sanction => "Icon.Kind.Ban",
        LabelFilter.OpenSource => "Icon.Globe",
        LabelFilter.Provider => "Icon.Alert",
        LabelFilter.Behaviour => "Icon.Pulse",
        LabelFilter.OtherCase => "Icon.Library",
        _ => "Icon.Pen",
    };

    /// <summary>Gives the source's name in a row's tooltip and the chosen label's chip.</summary>
    /// <param name="filter">The source.</param>
    /// <param name="providers">The suppliers' names, for example <c>Chainabuse</c>.</param>
    /// <returns>The name.</returns>
    public static string Name(LabelFilter filter, string providers) => filter switch
    {
        LabelFilter.Sanction => "Sanktionslista",
        LabelFilter.OpenSource => "Öppen källa",
        LabelFilter.Provider => providers,
        LabelFilter.Behaviour => "Beteende",
        LabelFilter.OtherCase => "Annat ärende",
        _ => "Egen",
    };

    /// <summary>Gives the name in the left column, in the plural where the sketch has it.</summary>
    /// <param name="filter">The source.</param>
    /// <param name="providers">The suppliers' names.</param>
    /// <returns>The name.</returns>
    public static string ColumnName(LabelFilter filter, string providers) => filter switch
    {
        LabelFilter.Sanction => "Sanktionslistor",
        LabelFilter.Own => "Egna",
        LabelFilter.OpenSource => "Öppna källor",
        LabelFilter.OtherCase => "Andra ärenden",
        _ => Name(filter, providers),
    };
}

/// <summary>A source in the left column of the view Etiketter: a filter switched off and on with a click.</summary>
public sealed partial class LabelSourceViewModel : ObservableObject
{
    /// <summary>Creates the source.</summary>
    /// <param name="filter">The source.</param>
    /// <param name="name">Its name.</param>
    public LabelSourceViewModel(LabelFilter filter, string name)
    {
        Filter = filter;
        Name = name;
    }

    /// <summary>Gets the source.</summary>
    public LabelFilter Filter { get; }

    /// <summary>Gets its name.</summary>
    public string Name { get; }

    /// <summary>Gets its icon.</summary>
    public string Icon => LabelFilters.Icon(Filter);

    /// <summary>Gets whether it is the sanction list, whose icon is red (risk).</summary>
    public bool IsSanction => Filter == LabelFilter.Sanction;

    /// <summary>Gets or sets the line under the name: what the source is now.</summary>
    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;

    /// <summary>Gets or sets how many labels in the case come from it.</summary>
    [ObservableProperty]
    public partial int Count { get; set; }

    /// <summary>Gets or sets whether its labels are shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    public partial bool IsOn { get; set; } = true;

    /// <summary>Gets what a click does.</summary>
    public string ToolTip => (IsOn ? "Dölj" : "Visa") + " etiketterna från källan";
}

/// <summary>A heading in the list of the view Etiketter: the sanction hits in the graph, or the labels.</summary>
/// <param name="Title">The heading, in capitals as the sketch writes it.</param>
/// <param name="Count">How many rows are under it.</param>
/// <param name="Note">What the right end says, or empty.</param>
/// <param name="IsRisk">Whether it is the sanction hits, drawn in red.</param>
public sealed record LabelGroupHeader(string Title, int Count, string Note, bool IsRisk);

/// <summary>One origin of the chosen label: who, when, why and in which case (sketch ET1a).</summary>
/// <param name="Filter">The origin's source, for its icon.</param>
/// <param name="Who">Who set it, or the list, supplier or analysis it comes from.</param>
/// <param name="When">When, in local time and UTC.</param>
/// <param name="CaseText">The case it was set in, or empty.</param>
/// <param name="IsOtherCase">Whether it came from another case, which is drawn a little brighter.</param>
/// <param name="Why">Why, in plain words, or empty.</param>
/// <param name="Meta">The version, the note or the like, small; or empty.</param>
/// <param name="IsRetracted">Whether this origin's label is taken back.</param>
/// <param name="HasLine">Whether a line runs down to the next origin.</param>
public sealed record LabelOriginRow(
    LabelFilter Filter,
    string Who,
    string When,
    string CaseText,
    bool IsOtherCase,
    string Why,
    string Meta,
    bool IsRetracted,
    bool HasLine)
{
    /// <summary>Gets the origin's icon.</summary>
    public string Icon => LabelFilters.Icon(Filter);

    /// <summary>Gets whether it is a sanction list, whose icon is red.</summary>
    public bool IsSanction => Filter == LabelFilter.Sanction;

    /// <summary>Gets whether a case is named.</summary>
    public bool HasCase => CaseText.Length > 0;

    /// <summary>Gets whether a reason is written.</summary>
    public bool HasWhy => Why.Length > 0;

    /// <summary>Gets whether something small is written under the reason.</summary>
    public bool HasMeta => Meta.Length > 0;
}

/// <summary>A chip under the chosen label's name: its source, and that it is a judgement (sketch ET1a).</summary>
/// <param name="Text">What it says.</param>
/// <param name="Icon">Its icon.</param>
/// <param name="ToolTip">What hovering says, or empty.</param>
/// <param name="IsDashed">Whether its edge is dashed: a judgement or a suggestion, never a fact.</param>
/// <param name="IsSanction">Whether it is the sanction list's, drawn in red.</param>
public sealed record LabelChip(string Text, string Icon, string ToolTip, bool IsDashed, bool IsSanction);

/// <summary>
/// A label as the view Etiketter lists it (sketch ET1a): the same label from every origin as one row, with its sort as
/// a small node, its source as an icon, dots for independent origins and its marks. Everything it shows is worked out
/// when it is built; only whether it is chosen changes.
/// </summary>
public sealed partial class LabelRowViewModel : ObservableObject
{
    /// <summary>Gets the row's key, the same each time the case is read.</summary>
    public required string Key { get; init; }

    /// <summary>Gets what the label says, for example <c>Börs · Börs A</c>.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the name of its node in the graph, small beside the title; or empty.</summary>
    public string NodeName { get; init; } = string.Empty;

    /// <summary>Gets the chosen label's heading: its node's name, or what it says outside the graph.</summary>
    public string Heading => NodeName.Length > 0 ? NodeName : Title;

    /// <summary>Gets the address shortened, as the list writes it.</summary>
    public required string Address { get; init; }

    /// <summary>Gets the address in full, for copying.</summary>
    public required string FullAddress { get; init; }

    /// <summary>Gets how its sort is drawn: its node's look in the graph, otherwise the label's own sort.</summary>
    public required SortLook Look { get; init; }

    /// <summary>Gets whether the small node is round: an actor we know nothing about.</summary>
    public bool IsRound => Look.Shape == NodeShape.Round;

    /// <summary>Gets whether the small node's frame is dashed: uncertain, or a suggestion from an analysis.</summary>
    public bool IsDashed => Look.IsDashed || IsSuggestion;

    /// <summary>Gets the source it is listed under.</summary>
    public required LabelFilter Source { get; init; }

    /// <summary>Gets every source among its origins, for the filters.</summary>
    public required IReadOnlySet<LabelFilter> Sources { get; init; }

    /// <summary>Gets the source's icon.</summary>
    public string SourceIcon => LabelFilters.Icon(Source);

    /// <summary>Gets the source icon's tooltip.</summary>
    public required string SourceToolTip { get; init; }

    /// <summary>Gets whether it is a suggestion from an analysis, drawn dashed.</summary>
    public bool IsSuggestion => Source == LabelFilter.Behaviour;

    /// <summary>Gets whether it is a hit on a sanction list, whose source frame is red.</summary>
    public bool IsSanction => Source == LabelFilter.Sanction;

    /// <summary>Gets whether every one of its labels is taken back: struck through and faint.</summary>
    public bool IsRetracted { get; init; }

    /// <summary>Gets whether it stays in the case (<em>Sprid inte</em>).</summary>
    public bool IsNoSpread { get; init; }

    /// <summary>Gets whether an origin came from another case, through the library.</summary>
    public bool IsFromOtherCase => Sources.Contains(LabelFilter.OtherCase);

    /// <summary>Gets how many independent origins say the same, as dots.</summary>
    public required int Independent { get; init; }

    /// <summary>Gets the dots: one per independent origin, at most four.</summary>
    public IReadOnlyList<int> Dots => [.. Enumerable.Range(0, Math.Min(Independent, MaximumDots))];

    /// <summary>Gets whether more than one independent origin says the same: green dots.</summary>
    public bool IsConfirmed => Independent > 1;

    /// <summary>Gets the dots' tooltip.</summary>
    public string DotsToolTip => IsConfirmed
        ? $"Bekräftad: {Independent} oberoende ursprung"
        : "1 ursprung";

    /// <summary>Gets the key of its node in the graph, or <see langword="null"/> when it is not in the graph.</summary>
    public string? NodeKey { get; init; }

    /// <summary>Gets whether its node is in the graph, so that <em>Visa i grafen</em> is offered.</summary>
    public bool IsInGraph => NodeKey is not null;

    /// <summary>Gets where its actor is in the graph shown.</summary>
    public LabelPlace Place { get; init; }

    /// <summary>Gets whether a small mark says the actor lies in a pile or in the trash.</summary>
    public bool HasPlaceMark => Place is LabelPlace.Pile or LabelPlace.Trash;

    /// <summary>Gets the mark's icon: a stack for a pile, the trash for the trash.</summary>
    public string PlaceIcon => Place == LabelPlace.Trash ? "Icon.Trash" : "Icon.Stack";

    /// <summary>Gets what the mark says on hover.</summary>
    public string PlaceText => Place switch
    {
        LabelPlace.Pile => "I en hög i grafen",
        LabelPlace.Trash => "I soptunnan: dold i grafen men kvar i ärendet",
        _ => string.Empty,
    };

    /// <summary>Gets whether it is near a victim in the graph, so that it never reaches the library.</summary>
    public bool IsNearVictim { get; init; }

    /// <summary>Gets the case numbers that hold it, in the library; empty in the case.</summary>
    public IReadOnlyList<string> Cases { get; init; } = [];

    /// <summary>Gets whether case numbers are shown.</summary>
    public bool HasCases => Cases.Count > 0;

    /// <summary>Gets its origins, oldest first, as the chosen label shows them.</summary>
    public required IReadOnlyList<LabelOriginRow> Origins { get; init; }

    /// <summary>Gets the chips under the chosen label's name.</summary>
    public required IReadOnlyList<LabelChip> Chips { get; init; }

    /// <summary>Gets what its node is in the graph, one line under the label; or empty.</summary>
    public string NodeInfo { get; init; } = string.Empty;

    /// <summary>Gets who took it back, when and why; or empty while it counts.</summary>
    public string RetractedText { get; init; } = string.Empty;

    /// <summary>Gets the labels the user can take back, by sequence.</summary>
    public IReadOnlyList<long> Retractable { get; init; } = [];

    /// <summary>Gets the labels the user can mark <em>Sprid inte</em>, by sequence.</summary>
    public IReadOnlyList<long> Spreadable { get; init; } = [];

    /// <summary>Gets whether it is on a victim, whose label never spreads whatever it is marked.</summary>
    public bool IsVictim { get; init; }

    /// <summary>Gets whether <em>Ta tillbaka</em> is offered.</summary>
    public bool CanRetract => Retractable.Count > 0;

    /// <summary>Gets whether the switch <em>Sprid inte</em> is shown: an own label, or a victim's (on).</summary>
    public bool ShowsSpread =>
        Spreadable.Count > 0 || ((IsVictim || IsNearVictim) && !IsRetracted && Source == LabelFilter.Own);

    /// <summary>Gets why the switch cannot be turned, said under it; empty when it can.</summary>
    public string SpreadNote => (IsVictim, IsNearVictim) switch
    {
        (true, _) => "En etikett på ett offer sprids aldrig.",
        (_, true) => "Nära ett offer i grafen: når aldrig biblioteket.",
        _ => string.Empty,
    };

    /// <summary>Gets whether a note is said under the switch.</summary>
    public bool HasSpreadNote => SpreadNote.Length > 0;

    /// <summary>Gets whether the switch can be turned.</summary>
    public bool CanChangeSpread => Spreadable.Count > 0;

    /// <summary>Gets whether the actions at the foot are shown.</summary>
    public bool HasActions => CanRetract || ShowsSpread;

    /// <summary>Gets the heading over the origins.</summary>
    public string OriginTitle => (IsSanction, Independent) switch
    {
        (true, _) => "TRÄFFEN",
        (_, > 1) => $"URSPRUNG · {Independent} OBEROENDE",
        _ => "URSPRUNG",
    };

    /// <summary>Gets whether <em>Varför?</em> is offered: for an analysis, a supplier or an open source.</summary>
    public bool HasWhy => Sources.Contains(LabelFilter.Behaviour) || Sources.Contains(LabelFilter.Provider)
        || Sources.Contains(LabelFilter.OpenSource);

    /// <summary>Gets the text the filter field searches: the label, its node, its address and its cases.</summary>
    public required string SearchText { get; init; }

    /// <summary>Gets or sets whether the row is chosen.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>The most dots drawn, so that a row stays one height.</summary>
    private const int MaximumDots = 4;
}

/// <summary>
/// Builds the rows of the view Etiketter from what the case and the library hold (sketch ET1a): the same label from
/// every origin as one row, placed on its node in the graph where there is one. No WPF and no I/O, so that it is tested
/// in Chainsaw.App.Tests.
/// </summary>
public static class LabelRows
{
    /// <summary>The format of a local time with its UTC beside it.</summary>
    private const string TimeFormat = "yyyy-MM-dd HH:mm";

    /// <summary>
    /// Builds the case's rows: every label, also those taken back, merged by what it is on and what it says. The best
    /// supported first, those taken back last.
    /// </summary>
    /// <param name="history">Every label in the case with its taking back.</param>
    /// <param name="scene">The graph shown, or <see langword="null"/>.</param>
    /// <param name="words">How sources, cases and times are written.</param>
    /// <returns>The rows.</returns>
    public static IReadOnlyList<LabelRowViewModel> ForCase(
        IReadOnlyList<LabelHistoryEntry> history, GraphScene? scene, LabelWords words)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(words);
        var nodes = NodeFinder.For(scene);
        var victims = Victims(history, nodes);
        return
        [
            .. history
                .GroupBy(entry => (entry.Label.Subject, entry.Label.Kind, Name: NameKey(entry.Label.Name)))
                .Select(group => CaseRow([.. group], nodes, victims, words))
                .OrderBy(row => row.IsRetracted)
                .ThenByDescending(row => row.Independent)
                .ThenBy(row => row.IsFromOtherCase && row.Sources.Count == 1),
        ];
    }

    /// <summary>Builds the library's rows, each with the case numbers that hold it.</summary>
    /// <param name="library">The library's labels.</param>
    /// <param name="scene">The graph shown, or <see langword="null"/>.</param>
    /// <param name="words">How sources, cases and times are written.</param>
    /// <returns>The rows, in the library's order.</returns>
    public static IReadOnlyList<LabelRowViewModel> ForLibrary(
        IReadOnlyList<LibraryLabel> library, GraphScene? scene, LabelWords words)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(words);
        var nodes = NodeFinder.For(scene);
        return [.. library.Select(label => LibraryRow(label, nodes, words))];
    }

    /// <summary>Gives a name as it is compared: trimmed and in lower case, or empty.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The compared form.</returns>
    public static string NameKey(string? name) => name?.Trim().ToLowerInvariant() ?? string.Empty;

    /// <summary>Writes what a label says: its sort and its name.</summary>
    /// <param name="kind">The sort.</param>
    /// <param name="name">The name, or <see langword="null"/>.</param>
    /// <returns>For example <c>Börs · Börs A</c>.</returns>
    public static string Title(LabelKind kind, string? name) =>
        string.IsNullOrWhiteSpace(name) ? kind.Describe() : $"{kind.Describe()} · {name.Trim()}";

    /// <summary>
    /// Gives where the victims are: the addresses of the labels on a victim that count, and the graph's actors they
    /// are on, so that a label near a victim is said never to reach the library.
    /// </summary>
    /// <param name="history">Every label in the case.</param>
    /// <param name="nodes">Where the graph's nodes are found.</param>
    /// <returns>The victims' addresses and actors.</returns>
    private static VictimPlaces Victims(IReadOnlyList<LabelHistoryEntry> history, NodeFinder nodes)
    {
        var victims = history.Where(entry => !entry.IsRetracted && entry.Label.Kind == LabelKind.Victim).ToList();
        return new(
            victims.Select(entry => entry.Label.Subject.Target).ToHashSet(),
            victims.Select(entry => nodes.Find([entry.Label.Sequence], entry.Label.Subject).Key)
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>Builds one row of the case from the labels that say the same about the same subject.</summary>
    /// <param name="entries">The labels, oldest first; never empty.</param>
    /// <param name="nodes">Where the graph's nodes are found.</param>
    /// <param name="victims">Where the victims are.</param>
    /// <param name="words">How sources, cases and times are written.</param>
    /// <returns>The row.</returns>
    private static LabelRowViewModel CaseRow(
        IReadOnlyList<LabelHistoryEntry> entries, NodeFinder nodes, VictimPlaces victims, LabelWords words)
    {
        var first = entries[0].Label;
        var active = entries.Where(entry => !entry.IsRetracted).ToList();
        var counting = active.Count > 0 ? active : entries;
        var merged = new Label(first.Subject, first.Kind, first.Name, [.. counting.Select(entry => entry.Label)]);
        var source = MainSource(counting);
        var hit = nodes.Find(counting.Select(entry => entry.Label.Sequence), first.Subject);
        var node = hit.View;
        var isVictim = first.Kind == LabelKind.Victim;
        var isNearVictim = !isVictim && victims.Holds(first.Subject.Target, hit.Key);
        var ownActive = active.Where(entry => entry.IsUsersOwn).ToList();

        // One rule for the mark, the chip and the switch: a victim's label, and one near a victim, never spread.
        var isNoSpread = ownActive.Count > 0
            && (isVictim || isNearVictim || ownActive.All(entry => entry.Label.DoNotSpread));
        var spreadable = isNearVictim ? [] : active.Where(entry => entry.CanChangeSpread).ToList();
        var title = Title(first.Kind, first.Name);
        return new LabelRowViewModel
        {
            Key = Key(first.Subject, first.Kind, first.Name),
            Title = title,
            NodeName = node?.Name ?? string.Empty,
            Address = GraphScene.Shorten(first.Subject.Target.Value),
            FullAddress = first.Subject.Target.Value,
            Look = node?.Look ?? VisualLanguage.Of(VisualLanguage.SortOf(first.Kind)),
            Source = source,
            Sources = entries.Select(entry => LabelFilters.Of(entry.Label.Origin.Source)).ToHashSet(),
            SourceToolTip = words.SourceToolTip(source),
            IsRetracted = active.Count == 0,
            IsNoSpread = isNoSpread,
            Independent = merged.IndependentOrigins,
            NodeKey = hit.Key,
            Place = hit.Place,
            IsNearVictim = isNearVictim,
            Origins = Origins(entries, words),
            Chips = Chips(source, isNoSpread, words),
            NodeInfo = node is null ? string.Empty : NodeInfo(node),
            RetractedText = active.Count == 0 ? words.Retracted(entries[^1].Retraction!) : string.Empty,
            Retractable = [.. active.Where(entry => entry.CanRetract).Select(entry => entry.Label.Sequence)],
            Spreadable = [.. spreadable.Select(entry => entry.Label.Sequence)],
            IsVictim = isVictim,
            SearchText = string.Join(' ', title, node?.Name, first.Subject.Target.Value, words.CaseNumber),
        };
    }

    /// <summary>Builds one row of the library.</summary>
    /// <param name="label">The library's label.</param>
    /// <param name="nodes">Where the graph's nodes are found.</param>
    /// <param name="words">How sources, cases and times are written.</param>
    /// <returns>The row.</returns>
    private static LabelRowViewModel LibraryRow(LibraryLabel label, NodeFinder nodes, LabelWords words)
    {
        var source = LabelFilters.Of(label.Origin.OriginalSource);
        var hit = nodes.Find([], label.Subject);
        var node = hit.View;
        var title = Title(label.Kind, label.Name);
        var cases = words.LibraryCases(label.CaseNumbers);
        var origin = words.Origin(label.Origin, cases, isRetracted: false, hasLine: false);
        return new LabelRowViewModel
        {
            Key = "library|" + Key(label.Subject, label.Kind, label.Name),
            Title = title,
            NodeName = node?.Name ?? string.Empty,
            Address = GraphScene.Shorten(label.Subject.Target.Value),
            FullAddress = label.Subject.Target.Value,
            Look = node?.Look ?? VisualLanguage.Of(VisualLanguage.SortOf(label.Kind)),
            Source = source,
            Sources = new HashSet<LabelFilter> { source },
            SourceToolTip = words.SourceName(source),
            Independent = 1,
            NodeKey = hit.Key,
            Place = hit.Place,
            Cases = label.CaseNumbers,
            Origins = [origin],
            Chips = Chips(source, isNoSpread: false, words),
            NodeInfo = node is null ? string.Empty : NodeInfo(node),
            SearchText = string.Join(
                ' ', title, node?.Name, label.Subject.Target.Value, string.Join(' ', label.CaseNumbers)),
        };
    }

    /// <summary>
    /// Gives the source a row is listed under: the first origin that is not a copy from another case, since what was
    /// set here carries its reason; otherwise another case.
    /// </summary>
    /// <param name="entries">The labels that count.</param>
    /// <returns>The source.</returns>
    private static LabelFilter MainSource(IReadOnlyList<LabelHistoryEntry> entries) =>
        entries.Select(entry => entry.Label.Origin).FirstOrDefault(origin => !origin.IsFromOtherCase) is { } own
            ? LabelFilters.Of(own.Source)
            : LabelFilter.OtherCase;

    /// <summary>Writes every origin of a row, oldest first, with a line down to the next.</summary>
    /// <param name="entries">The labels.</param>
    /// <param name="words">How sources, cases and times are written.</param>
    /// <returns>The origins.</returns>
    private static IReadOnlyList<LabelOriginRow> Origins(IReadOnlyList<LabelHistoryEntry> entries, LabelWords words) =>
    [
        .. entries.Select((entry, index) => words.Origin(
            entry.Label.Origin,
            entry.Label.Origin.IsFromOtherCase ? LabelWords.OtherCase(entry.Label.Origin.CaseNumber) : words.ThisCase,
            entry.IsRetracted,
            hasLine: index < entries.Count - 1)),
    ];

    /// <summary>Gives the chips under the chosen label's name: its source, judgement or list, and Sprid inte.</summary>
    /// <param name="source">The source.</param>
    /// <param name="isNoSpread">Whether it stays in the case.</param>
    /// <param name="words">How sources are written.</param>
    /// <returns>The chips.</returns>
    private static IReadOnlyList<LabelChip> Chips(LabelFilter source, bool isNoSpread, LabelWords words)
    {
        var chips = new List<LabelChip>
        {
            new(words.SourceName(source), LabelFilters.Icon(source), string.Empty, false,
                source == LabelFilter.Sanction),
        };
        chips.Add(source switch
        {
            LabelFilter.Sanction => new("Står på listan", "Icon.Check",
                "Listan är en källa. Att ägaren är sanktionerad följer av listan.", false, false),
            LabelFilter.Behaviour => new(
                "Förslag", "Icon.Question", "En analys föreslår det: en bedömning", true, false),
            _ => new("Bedömning", "Icon.Question", "En etikett är en bedömning, aldrig ett faktum", true, false),
        });
        if (isNoSpread)
        {
            chips.Add(new("Sprids inte", "Icon.NoSpread", "Stannar i ärendet", false, false));
        }

        return chips;
    }

    /// <summary>Writes what a node is in the graph, one line under the label.</summary>
    /// <param name="node">The node.</param>
    /// <returns>For example <c>Kluster K1 · 14 adresser · 0,7500 BTC spårat hit</c>.</returns>
    private static string NodeInfo(NodeView node)
    {
        var parts = new List<string> { node.Name };
        if (node.IsStartPoint)
        {
            parts.Add("Startpunkt");
        }

        if (node.Count.Length > 0)
        {
            parts.Add(node.Count + " adresser");
        }

        if (!node.TracedIn.IsZero)
        {
            parts.Add(AmountText.WithUnit(node.TracedIn) + " spårat hit");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Gives a row's key: what the label is on and what it says.</summary>
    /// <param name="subject">What it is on.</param>
    /// <param name="kind">What it says.</param>
    /// <param name="name">The name.</param>
    /// <returns>The key.</returns>
    private static string Key(LabelSubject subject, LabelKind kind, string? name) =>
        string.Join('|', subject.Kind, subject.Target.Chain, subject.Target.Value, kind, NameKey(name));

    /// <summary>Writes a time in local time with its UTC beside it.</summary>
    /// <param name="utc">The time.</param>
    /// <param name="zone">The local zone.</param>
    /// <returns>For example <c>2026-11-06 15:02 (14:02 UTC)</c>.</returns>
    internal static string When(DateTimeOffset utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(utc, zone).ToString(TimeFormat, CultureInfo.InvariantCulture)
        + " (" + utc.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC)";

    /// <summary>Where the victims are in the case and in the graph.</summary>
    /// <param name="Addresses">The addresses of the labels on a victim that count.</param>
    /// <param name="Actors">The keys of the graph's actors those labels are on.</param>
    private sealed record VictimPlaces(IReadOnlySet<ChainAddress> Addresses, IReadOnlySet<string> Actors)
    {
        /// <summary>Says whether an address, or the actor it is on, is a victim's.</summary>
        /// <param name="address">The address.</param>
        /// <param name="actor">The key of its actor in the graph, or <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when it is near a victim.</returns>
        public bool Holds(ChainAddress address, string? actor) =>
            Addresses.Contains(address) || (actor is not null && Actors.Contains(actor));
    }

    /// <summary>Where a label's actor was found in the graph.</summary>
    /// <param name="View">The actor as drawn (a pile's card for a pile), or <see langword="null"/>.</param>
    /// <param name="Key">The key <em>Visa i grafen</em> chooses, or <see langword="null"/> when it cannot.</param>
    /// <param name="Place">Where it is.</param>
    private readonly record struct NodeHit(NodeView? View, string? Key, LabelPlace Place)
    {
        /// <summary>Gets the hit of a label that is not in the graph.</summary>
        public static NodeHit None => new(null, null, LabelPlace.None);
    }

    /// <summary>
    /// Finds a label's actor in the graph by the labels the graph read or by its address: drawn, a card in a pile,
    /// or in the trash. Built once per graph and looked up by address, so that a large graph is never searched row by
    /// row.
    /// </summary>
    private sealed class NodeFinder
    {
        /// <summary>The finders already built, one per graph, kept only as long as the graph.</summary>
        private static readonly ConditionalWeakTable<GraphScene, NodeFinder> Built = [];

        /// <summary>The finder of no graph.</summary>
        private static readonly NodeFinder Nothing = new(null);

        /// <summary>The actor each label the graph read is on, by the label's sequence.</summary>
        private readonly Dictionary<long, NodeHit> _bySequence = [];

        /// <summary>The actor each address is on.</summary>
        private readonly Dictionary<ChainAddress, NodeHit> _byAddress = [];

        /// <summary>Creates the finder.</summary>
        /// <param name="scene">The graph, or <see langword="null"/>.</param>
        private NodeFinder(GraphScene? scene)
        {
            if (scene is null)
            {
                return;
            }

            foreach (var node in scene.Nodes)
            {
                Add(node, new NodeHit(node, node.Key, LabelPlace.Graph));
            }

            foreach (var pile in scene.Piles.Values)
            {
                foreach (var card in pile.Cards)
                {
                    Add(card.Node, new NodeHit(card.Node, pile.Key, LabelPlace.Pile));
                }
            }

            foreach (var node in scene.Snapshot.Graph.Nodes.Where(scene.Trash.Hides))
            {
                foreach (var address in node.Addresses)
                {
                    _byAddress.TryAdd(address, new NodeHit(null, null, LabelPlace.Trash));
                }
            }
        }

        /// <summary>Gives the finder of a graph, built once.</summary>
        /// <param name="scene">The graph, or <see langword="null"/> for none.</param>
        /// <returns>The finder.</returns>
        public static NodeFinder For(GraphScene? scene) =>
            scene is null ? Nothing : Built.GetValue(scene, each => new NodeFinder(each));

        /// <summary>Finds the actor a label is on.</summary>
        /// <param name="sequences">The label's sealed records.</param>
        /// <param name="subject">What it is on.</param>
        /// <returns>Where it is; <see cref="NodeHit.None"/> when it is not in the graph.</returns>
        public NodeHit Find(IEnumerable<long> sequences, LabelSubject subject)
        {
            foreach (var sequence in sequences)
            {
                if (_bySequence.TryGetValue(sequence, out var found))
                {
                    return found;
                }
            }

            return _byAddress.GetValueOrDefault(subject.Target, NodeHit.None);
        }

        /// <summary>Takes in an actor: its labels and its addresses, the first one found kept.</summary>
        /// <param name="node">The actor.</param>
        /// <param name="hit">Where it is.</param>
        private void Add(NodeView node, NodeHit hit)
        {
            foreach (var entry in node.Labels.SelectMany(label => label.Entries))
            {
                _bySequence.TryAdd(entry.Sequence, hit);
            }

            foreach (var address in node.Node.Addresses)
            {
                _byAddress.TryAdd(address, hit);
            }
        }
    }
}

/// <summary>How the view Etiketter writes sources, cases, people and times.</summary>
/// <param name="CaseNumber">The open case's number.</param>
/// <param name="Providers">The suppliers' names, for example <c>Chainabuse</c>.</param>
/// <param name="Zone">The local zone.</param>
public sealed record LabelWords(string CaseNumber, string Providers, TimeZoneInfo Zone)
{
    /// <summary>What an origin from another case says about its case: its number stays there.</summary>
    public const string OtherCaseText = "Från ett annat ärende";

    /// <summary>Writes the case an origin from another case was set in, when its number came along.</summary>
    /// <param name="caseNumber">The number, or <see langword="null"/> for a copy made before it followed.</param>
    /// <returns>For example <c>Från ärende 2026-0098 · ett annat ärende</c>.</returns>
    public static string OtherCase(string? caseNumber) =>
        caseNumber is null ? OtherCaseText : $"Från ärende {caseNumber} · ett annat ärende";

    /// <summary>Gets what an origin set here says about its case.</summary>
    public string ThisCase => $"Ärende {CaseNumber} · här";

    /// <summary>Gives a source's name.</summary>
    /// <param name="filter">The source.</param>
    /// <returns>The name.</returns>
    public string SourceName(LabelFilter filter) => LabelFilters.Name(filter, Providers);

    /// <summary>Gives a source icon's tooltip: its name, and for an analysis that it is a suggestion.</summary>
    /// <param name="filter">The source.</param>
    /// <returns>The tooltip.</returns>
    public string SourceToolTip(LabelFilter filter) =>
        filter == LabelFilter.Behaviour ? SourceName(filter) + " · förslag, en bedömning" : SourceName(filter);

    /// <summary>Writes the cases a library label is in.</summary>
    /// <param name="cases">The case numbers.</param>
    /// <returns>For example <c>Ärende 2026-0117, 2026-0098</c>.</returns>
    public string LibraryCases(IReadOnlyList<string> cases) =>
        cases.Count == 0 ? string.Empty : (cases.Count == 1 ? "Ärende " : "Ärenden ") + string.Join(", ", cases);

    /// <summary>Writes who took a label back, when and why.</summary>
    /// <param name="retraction">The taking back.</param>
    /// <returns>For example <c>Tagen tillbaka 2026-11-07 09:40 (08:40 UTC) av M. Persson: fel kluster.</c>.</returns>
    public string Retracted(LabelRetraction retraction)
    {
        ArgumentNullException.ThrowIfNull(retraction);
        var text = "Tagen tillbaka " + LabelRows.When(retraction.Utc, Zone);
        if (retraction.By is { } by)
        {
            text += " av " + by.Name;
        }

        return retraction.Reason is { Length: > 0 } reason ? $"{text}: {reason}" : text + ".";
    }

    /// <summary>Writes one origin: who, when, the case, why and the source's version.</summary>
    /// <param name="origin">The origin.</param>
    /// <param name="caseText">What it says about the case.</param>
    /// <param name="isRetracted">Whether its label is taken back.</param>
    /// <param name="hasLine">Whether a line runs down to the next origin.</param>
    /// <returns>The row.</returns>
    public LabelOriginRow Origin(LabelOrigin origin, string caseText, bool isRetracted, bool hasLine)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var filter = LabelFilters.Of(origin.OriginalSource);
        var when = LabelRows.When(origin.SetUtc, Zone);
        var (who, whenText, meta) = filter switch
        {
            LabelFilter.Sanction => (origin.SourceName, "Kontrollerad " + when + " av " + origin.SetBy.Name,
                origin.SourceVersion),
            LabelFilter.Provider => (origin.SourceName, "Hämtad " + when + " av " + origin.SetBy.Name,
                origin.SourceVersion),
            LabelFilter.OpenSource => (origin.SourceName, "Läst " + when + " av " + origin.SetBy.Name,
                OpenSourceLicenses.Of(origin.SourceName) is { } license
                    ? $"{origin.SourceVersion} · {license}"
                    : origin.SourceVersion),
            LabelFilter.Behaviour => ($"Analysen {origin.SourceName} {origin.SourceVersion}", when, string.Empty),
            _ => (Person(origin.SetBy), when, NoteOf(origin)),
        };
        var source = origin.IsFromOtherCase ? LabelFilter.OtherCase : filter;
        return new LabelOriginRow(
            source, who, whenText, caseText, origin.IsFromOtherCase, origin.Reason ?? string.Empty, meta, isRetracted,
            hasLine);
    }

    /// <summary>Writes the investigator's own note, small under the reason; empty when there is none.</summary>
    /// <param name="origin">The origin.</param>
    /// <returns>The text.</returns>
    private static string NoteOf(LabelOrigin origin) =>
        origin.Note is { Length: > 0 } note ? "Anteckning: " + note : string.Empty;

    /// <summary>Writes who set a label: the name, title and unit that are known.</summary>
    /// <param name="profile">The operator.</param>
    /// <returns>For example <c>M. Persson · utredare · Enhet Syd</c>.</returns>
    private static string Person(OperatorProfile profile) =>
        string.Join(" · ", new[] { profile.Name, profile.Title, profile.Organization }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
}
