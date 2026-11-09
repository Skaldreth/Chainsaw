using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Tracing.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// One row of a throw's tree (sketch SO1a), in the warning by the bin and in the trash's view: the glyph that hangs it
/// on the row above, the node's form and colour, its name and its role.
/// </summary>
public sealed partial class TrashRowViewModel : ObservableObject
{
    /// <summary>The glyph of a row with more rows after it at the same depth.</summary>
    public const string MiddleGlyph = "├";

    /// <summary>The glyph of the last row at its depth.</summary>
    public const string LastGlyph = "└";

    /// <summary>How far one depth is indented, in pixels.</summary>
    public const double IndentStep = 18;

    /// <summary>How far one depth is indented in the trash's view (sketch SO2a), where real branch lines are drawn.</summary>
    public const double TreeStep = 24;

    /// <summary>Where the trash view's tree starts: the root's shape stands this far in.</summary>
    public const double TreeStart = 10;

    /// <summary>How far into a depth's step its branch line runs: under the middle of the parent's shape.</summary>
    public const double BranchOffset = 11;

    /// <summary>Creates a row.</summary>
    /// <param name="member">The member.</param>
    /// <param name="depth">How deep it hangs: 0 for what was thrown.</param>
    /// <param name="isLast">Whether it is the last at its depth under its parent.</param>
    /// <param name="watches">The watches on it, by id; empty when none.</param>
    public TrashRowViewModel(TrashMember member, int depth, bool isLast, IReadOnlyList<string> watches)
    {
        ArgumentNullException.ThrowIfNull(member);
        Member = member;
        Depth = depth;
        Glyph = GlyphOf(depth, isLast);
        Look = LookOf(member.Sort);
        Watches = watches;
    }

    /// <summary>Gets the member.</summary>
    public TrashMember Member { get; }

    /// <summary>Gets the node's key.</summary>
    public string Key => Member.Key;

    /// <summary>Gets the node's name.</summary>
    public string Name => Member.Name;

    /// <summary>Gets how deep it hangs.</summary>
    public int Depth { get; }

    /// <summary>Gets whether it is what was thrown.</summary>
    public bool IsRoot => Depth == 0;

    /// <summary>Gets the glyph that hangs it on the row above.</summary>
    public string Glyph { get; }

    /// <summary>Gets how far it is indented: one step per depth below the first.</summary>
    public double Indent => Math.Max(0, Depth - 1) * IndentStep;

    /// <summary>Gets how far the row's shape stands in the trash's view: one tree step per depth.</summary>
    public double TreePad => TreeStart + (Depth * TreeStep);

    /// <summary>Gets where the branch into the row turns, in the trash's view; only drawn below the root.</summary>
    public double ElbowLeft => LineAt(Depth);

    /// <summary>
    /// Gets the branch lines that run through the row from top to bottom in the trash's view: one for each depth where
    /// a later row still hangs on the same parent, so that the tree reads as one piece.
    /// </summary>
    public IReadOnlyList<double> Rails { get; init; } = [];

    /// <summary>Gives where the branch line into a depth runs: under the middle of the parent's shape.</summary>
    /// <param name="depth">The depth, 1 or more.</param>
    /// <returns>Its distance from the row's left edge.</returns>
    public static double LineAt(int depth) => TreeStart + (Math.Max(0, depth - 1) * TreeStep) + BranchOffset;

    /// <summary>Gets its sort as the graph drew it.</summary>
    public SortLook Look { get; }

    /// <summary>Gets whether its frame is round (an actor we know nothing about).</summary>
    public bool IsRound => Look.Shape == NodeShape.Round;

    /// <summary>Gets the watches on it.</summary>
    public IReadOnlyList<string> Watches { get; }

    /// <summary>Gets whether it is watched.</summary>
    public bool IsWatched => Watches.Count > 0;

    /// <summary>
    /// Gets or sets whether its watches end when it is thrown (CLAUDE.md, <em>Bevakat slängs inte i tysthet</em>): on
    /// from the start, since a watch on something hidden would go on unseen, and the user may keep it.
    /// </summary>
    [ObservableProperty]
    public partial bool EndsWatches { get; set; } = true;

    /// <summary>Gets the throw the row lies in, once thrown; 0 in a warning.</summary>
    public long ThrowSequence { get; init; }

    /// <summary>Gets how many hang under it in its throw, so that putting it back says it takes them too.</summary>
    public int Below { get; init; }

    /// <summary>Gets the putting back's button: <c>Lägg tillbaka</c>, or with how many come along.</summary>
    public string RestoreText => Below == 0
        ? "Lägg tillbaka"
        : string.Create(CultureInfo.InvariantCulture, $"Lägg tillbaka med {Below} under");

    /// <summary>Gets the button's tooltip.</summary>
    public string RestoreToolTip => Below == 0
        ? "Lägger tillbaka den här i grafen, där den stod"
        : string.Create(CultureInfo.InvariantCulture, $"Lägger tillbaka {Name} och de {Below} under den");

    /// <summary>Gets what the row says it does in the warning: <c>slängs</c> or <c>följer med</c>.</summary>
    public string WarningRole => IsRoot ? "slängs" : "följer med";

    /// <summary>Gets what the row says it did in the trash's view: <c>slängd</c> or <c>följde med</c>.</summary>
    public string TrashRole => IsRoot ? "slängd" : "följde med";

    /// <summary>Gives the glyph of a row: none for what was thrown, else the tree's corner or tee.</summary>
    /// <param name="depth">How deep it hangs.</param>
    /// <param name="isLast">Whether it is the last at its depth.</param>
    /// <returns>The glyph.</returns>
    private static string GlyphOf(int depth, bool isLast)
    {
        if (depth == 0)
        {
            return string.Empty;
        }

        return isLast ? LastGlyph : MiddleGlyph;
    }

    /// <summary>How a thrown comment is drawn in the trash: the note's icon in a square frame (sketch KN1a).</summary>
    public static SortLook CommentLook { get; } =
        new(NodeSort.Unknown, "Kommentar", "Icon.Note", GraphInk.Unknown, NodeShape.Square, false);

    /// <summary>How a thrown own area is drawn in the trash: the area's icon in a square frame (sketch KF1b).</summary>
    public static SortLook AreaLook { get; } =
        new(NodeSort.Unknown, "Egen yta", "Icon.Area", GraphInk.Cluster, NodeShape.Square, false);

    /// <summary>
    /// Gives the look of a sort as the trash stored it: a comment's and an own area's their own, a node's its sort's;
    /// a sort this build does not know is drawn as unknown.
    /// </summary>
    /// <param name="sort">The stored sort.</param>
    /// <returns>The look.</returns>
    public static SortLook LookOf(string sort) => sort switch
    {
        TrashKeys.CommentSort => CommentLook,
        TrashKeys.AreaSort => AreaLook,
        _ when Enum.TryParse<NodeSort>(sort, out var known) && VisualLanguage.Sorts.Any(look => look.Sort == known)
            => VisualLanguage.Of(known),
        _ => VisualLanguage.Of(NodeSort.Unknown),
    };

    /// <summary>Makes the rows of a tree, each with its glyph.</summary>
    /// <param name="members">The tree, in depth-first order.</param>
    /// <param name="depthOf">Gives a member's depth.</param>
    /// <param name="watchesOf">Gives the watches on a member.</param>
    /// <param name="throwSequence">The throw the members lie in; 0 for a warning.</param>
    /// <returns>The rows.</returns>
    public static IReadOnlyList<TrashRowViewModel> Tree(
        IReadOnlyList<TrashMember> members,
        Func<TrashMember, int> depthOf,
        Func<TrashMember, IReadOnlyList<string>> watchesOf,
        long throwSequence = 0)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(depthOf);
        ArgumentNullException.ThrowIfNull(watchesOf);
        var depths = members.Select(depthOf).ToList();
        return
        [
            .. members.Select((member, index) =>
                new TrashRowViewModel(member, depths[index], IsLast(depths, index), watchesOf(member))
                {
                    ThrowSequence = throwSequence,
                    Below = CountBelow(depths, index),
                    Rails = RailsOf(depths, index),
                }),
        ];
    }

    /// <summary>
    /// Gives the branch lines through a row: for each depth from 1 to the row's own, the line runs on when a later row
    /// hangs at that depth before one hangs shallower (the row's own line then goes on past its elbow).
    /// </summary>
    /// <param name="depths">Every row's depth.</param>
    /// <param name="index">The row.</param>
    /// <returns>The lines' distances from the row's left edge.</returns>
    private static List<double> RailsOf(List<int> depths, int index)
    {
        var rails = new List<double>();
        for (var depth = 1; depth <= depths[index]; depth++)
        {
            if (GoesOn(depths, index, depth))
            {
                rails.Add(LineAt(depth));
            }
        }

        return rails;
    }

    /// <summary>Says whether a later row hangs at a depth before any row hangs shallower than it.</summary>
    /// <param name="depths">Every row's depth.</param>
    /// <param name="index">The row to look after.</param>
    /// <param name="depth">The depth.</param>
    /// <returns><see langword="true"/> when the line at the depth goes on below the row.</returns>
    private static bool GoesOn(List<int> depths, int index, int depth)
    {
        for (var later = index + 1; later < depths.Count; later++)
        {
            if (depths[later] <= depth)
            {
                return depths[later] == depth;
            }
        }

        return false;
    }

    /// <summary>Counts the rows that hang under a row: those after it that lie deeper, up to the next that does not.</summary>
    /// <param name="depths">Every row's depth.</param>
    /// <param name="index">The row.</param>
    /// <returns>How many.</returns>
    private static int CountBelow(List<int> depths, int index) =>
        depths.Skip(index + 1).TakeWhile(depth => depth > depths[index]).Count();

    /// <summary>
    /// Says whether a row is the last at its depth under its parent: no later row at the same depth comes before a
    /// shallower one.
    /// </summary>
    /// <param name="depths">Every row's depth.</param>
    /// <param name="index">The row.</param>
    /// <returns><see langword="true"/> when it is the last.</returns>
    private static bool IsLast(List<int> depths, int index)
    {
        for (var later = index + 1; later < depths.Count; later++)
        {
            if (depths[later] <= depths[index])
            {
                return depths[later] < depths[index];
            }
        }

        return true;
    }
}

/// <summary>
/// The warning by the bin (sketch SO1a) when a throw takes more with it, or something under a watch: the tree, with
/// what is thrown on top and what follows under it, and for each watch whether it ends. The graph meanwhile frames
/// what follows dashed and tones the rest down. No WPF.
/// </summary>
public sealed class TrashWarningViewModel : ObservableObject
{
    /// <summary>The warning's title.</summary>
    public const string Head = "Det här följer med";

    /// <summary>What the warning says at its foot: nothing leaves the case.</summary>
    public const string Foot = "Inget tas bort ur ärendet. Det slängda göms i grafen och kan läggas tillbaka.";

    /// <summary>Creates the warning.</summary>
    /// <param name="rows">The tree's rows.</param>
    public TrashWarningViewModel(IReadOnlyList<TrashRowViewModel> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            throw new ArgumentException("A warning shows at least what is thrown.", nameof(rows));
        }

        Rows = rows;
        foreach (var row in rows)
        {
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TrashRowViewModel.EndsWatches))
                {
                    OnPropertyChanged(nameof(ConfirmText));
                }
            };
        }
    }

    /// <summary>Gets the tree's rows.</summary>
    public IReadOnlyList<TrashRowViewModel> Rows { get; }

    /// <summary>Gets the keys of everything that would be thrown.</summary>
    public IReadOnlySet<string> Keys => Rows.Select(row => row.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>Gets the rows under a watch.</summary>
    public IReadOnlyList<TrashRowViewModel> Watched => [.. Rows.Where(row => row.IsWatched)];

    /// <summary>Gets whether any row is watched, so that the warning asks about the watches.</summary>
    public bool HasWatches => Rows.Any(row => row.IsWatched);

    /// <summary>Gets the watches the user chose to end, each once.</summary>
    public IReadOnlyList<string> WatchesToEnd =>
        [.. Rows.Where(row => row.EndsWatches).SelectMany(row => row.Watches).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Gets the line under the title: how many hang on what is thrown and are reached only through it, and that a
    /// watch is asked about.
    /// </summary>
    public string Text
    {
        get
        {
            var roots = Rows.Where(row => row.IsRoot).ToList();
            var following = Rows.Count - roots.Count;
            var parts = new List<string>();
            if (following > 0)
            {
                var who = following == 1 ? "aktör hänger" : "aktörer hänger";
                var on = roots.Count == 1 ? roots[0].Name : "det du slänger";
                parts.Add(string.Create(
                    CultureInfo.InvariantCulture, $"{following} {who} på {on} och nås bara genom den."));
            }

            var watched = Rows.Count(row => row.IsWatched);
            if (watched > 0)
            {
                parts.Add(watched == 1
                    ? "En av dem är bevakad: välj om bevakningen ska avslutas."
                    : string.Create(CultureInfo.InvariantCulture,
                        $"{watched} av dem är bevakade: välj vilka bevakningar som ska avslutas."));
            }

            return string.Join(" ", parts);
        }
    }

    /// <summary>
    /// Gets the button's text: <c>Släng alla 5 · Enter</c>, <c>Släng · Enter</c> for one, and with the watches that end
    /// named, <c>Släng och avsluta 2 bevakningar · Enter</c>, so that ending them is never a surprise.
    /// </summary>
    public string ConfirmText
    {
        get
        {
            var ending = WatchesToEnd.Count;
            if (ending > 0)
            {
                var word = ending == 1 ? "bevakning" : "bevakningar";
                return string.Create(CultureInfo.InvariantCulture, $"Släng och avsluta {ending} {word} · Enter");
            }

            return Rows.Count == 1
                ? "Släng · Enter"
                : string.Create(CultureInfo.InvariantCulture, $"Släng alla {Rows.Count} · Enter");
        }
    }

    /// <summary>Gets the members to throw, as the trash stores them.</summary>
    public IReadOnlyList<TrashMember> Members => [.. Rows.Select(row => row.Member)];
}
