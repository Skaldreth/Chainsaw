using Chainsaw.App.Guide;

namespace Chainsaw.App.Search;

/// <summary>
/// What the command bar's search looks among (sketch KR1a): the open case's labels, addresses, transactions and
/// clusters, read from the graph, and the Guide's sections. It is built once per graph, off the UI thread, and a search
/// is one pass over it without touching the case, so that it stays fast in a large case (principle 6).
/// </summary>
public sealed class SearchIndex
{
    /// <summary>How many hits a group shows before <em>Tab visar alla</em>.</summary>
    public const int GroupRows = 4;

    /// <summary>How many hits a group shows when it is folded out with Tab, so that the list stays light.</summary>
    public const int AllRows = 100;

    /// <summary>The shortest text that is looked for inside words, and not only at their start.</summary>
    public const int InsideLength = 3;

    /// <summary>How often a search looks whether it has been overtaken by a newer one.</summary>
    private const int CancelCheck = 512;

    /// <summary>The score of one step of match quality; a better match always wins over the kind.</summary>
    private const int QualityStep = 100;

    /// <summary>The score of one step of the kind's weight.</summary>
    private const int KindStep = 10;

    /// <summary>The score for being in the graph, where the hit can be shown.</summary>
    private const int ShownBonus = 5;

    /// <summary>Everything that can be found.</summary>
    private readonly IReadOnlyList<SearchEntry> _entries;

    /// <summary>Creates an index over entries.</summary>
    /// <param name="entries">The entries.</param>
    public SearchIndex(IReadOnlyList<SearchEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries;
    }

    /// <summary>Gets an index with nothing in it.</summary>
    public static SearchIndex Empty { get; } = new([]);

    /// <summary>Gets how many entries the index holds.</summary>
    public int Count => _entries.Count;

    /// <summary>Builds the index over a graph and the Guide.</summary>
    /// <param name="scene">The open case's graph, or <see langword="null"/> when no case is open.</param>
    /// <param name="library">The Guide's sections.</param>
    /// <returns>The index.</returns>
    public static SearchIndex Build(Graph.GraphScene? scene, GuideLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        var entries = scene is null ? new List<SearchEntry>() : SceneEntries.Read(scene);
        entries.AddRange(GuideEntries.Read(library));
        return new SearchIndex(entries);
    }

    /// <summary>Searches for what is written.</summary>
    /// <param name="term">What is written, trimmed; an empty text finds nothing.</param>
    /// <param name="guideOnly">Whether only the Guide is searched, as with <c>/guide</c>.</param>
    /// <param name="unfolded">The group folded out with Tab, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Stops a search that a newer one has overtaken.</param>
    /// <returns>The best hit and the groups.</returns>
    /// <exception cref="OperationCanceledException">The search was overtaken.</exception>
    public SearchOutcome Search(
        string term, bool guideOnly = false, SearchKind? unfolded = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(term);
        if (term.Length == 0)
        {
            return SearchOutcome.Nothing(term);
        }

        var found = SearchKinds.InOrder.ToDictionary(kind => kind, _ => new List<SearchHit>());
        for (var i = 0; i < _entries.Count; i++)
        {
            if (i % CancelCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var entry = _entries[i];
            if ((!guideOnly || entry.Kind == SearchKind.Guide) && Hit(entry, term) is { } hit)
            {
                found[entry.Kind].Add(hit);
            }
        }

        foreach (var hits in found.Values)
        {
            hits.Sort(Better);
        }

        var best = found.Values.Where(hits => hits.Count > 0).Select(hits => hits[0]).OrderBy(hit => hit, Order)
            .FirstOrDefault();
        var groups = SearchKinds.InOrder
            .Select(kind => Group(kind, found[kind], best, unfolded == kind))
            .Where(group => group.Total > 0)
            .ToList();
        return new SearchOutcome(term, best, groups);
    }

    /// <summary>Gives the order of two hits: the better first.</summary>
    private static Comparer<SearchHit> Order { get; } = Comparer<SearchHit>.Create(Better);

    /// <summary>
    /// Compares two hits: the higher score first, then the shorter text (closer to what is written), then the text.
    /// </summary>
    /// <param name="a">One hit.</param>
    /// <param name="b">The other.</param>
    /// <returns>Below zero when <paramref name="a"/> comes first.</returns>
    private static int Better(SearchHit a, SearchHit b)
    {
        var byScore = b.Score.CompareTo(a.Score);
        if (byScore != 0)
        {
            return byScore;
        }

        var byLength = a.Entry.Text.Length.CompareTo(b.Entry.Text.Length);
        return byLength != 0 ? byLength : string.CompareOrdinal(a.Entry.Text, b.Entry.Text);
    }

    /// <summary>Makes a group of hits, without the best hit, which stands above them.</summary>
    /// <param name="kind">The group's kind.</param>
    /// <param name="hits">Its hits, best first.</param>
    /// <param name="best">The best hit, or <see langword="null"/>.</param>
    /// <param name="unfolded">Whether the group is folded out with Tab.</param>
    /// <returns>The group.</returns>
    private static SearchGroup Group(SearchKind kind, List<SearchHit> hits, SearchHit? best, bool unfolded)
    {
        var rest = hits.Where(hit => !ReferenceEquals(hit, best)).ToList();
        var shown = rest.Take(unfolded ? AllRows : GroupRows).ToList();
        return new SearchGroup(kind, shown, rest.Count, unfolded);
    }

    /// <summary>Matches an entry: its text, or else one of its other words, which counts one step lower.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="term">What is written.</param>
    /// <returns>The hit, or <see langword="null"/>.</returns>
    private static SearchHit? Hit(SearchEntry entry, string term)
    {
        var match = SearchMatch.Find(entry.Text, term);
        var quality = match.Quality;
        if (!match.IsMatch)
        {
            quality = AliasQuality(entry, term);
            match = SearchMatch.None;
        }

        if (quality == MatchQuality.None || (quality == MatchQuality.Inside && term.Length < InsideLength))
        {
            return null;
        }

        var score = ((int)quality * QualityStep) + (entry.Kind.Weight() * KindStep)
            + (entry.NodeKey is null ? 0 : ShownBonus);
        return new SearchHit(entry, match, score);
    }

    /// <summary>Gives how well the best of an entry's other words matches, one step below its text.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="term">What is written.</param>
    /// <returns>The quality, never above <see cref="MatchQuality.Start"/>.</returns>
    private static MatchQuality AliasQuality(SearchEntry entry, string term)
    {
        var best = MatchQuality.None;
        foreach (var alias in entry.Aliases)
        {
            var quality = SearchMatch.Find(alias, term).Quality;
            if (quality > best)
            {
                best = quality;
            }
        }

        return best <= MatchQuality.Inside ? best : best - 1;
    }
}

/// <summary>A hit: the entry, where what was written stands in its text, and how well it matched.</summary>
/// <param name="Entry">The entry.</param>
/// <param name="Match">Where it matched in the text; none when it matched one of its other words.</param>
/// <param name="Score">How well it matched, with its kind and whether it is in the graph; higher is better.</param>
public sealed record SearchHit(SearchEntry Entry, SearchMatch Match, int Score);

/// <summary>A group of hits under its heading (sketch KR1a).</summary>
/// <param name="Kind">The group's kind.</param>
/// <param name="Hits">The hits shown, best first.</param>
/// <param name="Total">How many there are in all, the best hit not counted.</param>
/// <param name="IsUnfolded">Whether the group is folded out with Tab.</param>
public sealed record SearchGroup(SearchKind Kind, IReadOnlyList<SearchHit> Hits, int Total, bool IsUnfolded)
{
    /// <summary>Gets whether more hits exist than are shown.</summary>
    public bool HasMore => Total > Hits.Count;
}

/// <summary>What a search found.</summary>
/// <param name="Term">What was written.</param>
/// <param name="Best">The best hit, or <see langword="null"/> when nothing matched.</param>
/// <param name="Groups">The groups with hits, in the groups' order.</param>
public sealed record SearchOutcome(string Term, SearchHit? Best, IReadOnlyList<SearchGroup> Groups)
{
    /// <summary>Gives what a search for nothing found.</summary>
    /// <param name="term">What was written.</param>
    /// <returns>No hits.</returns>
    public static SearchOutcome Nothing(string term) => new(term, null, []);

    /// <summary>Gets whether anything in the case (not the Guide) matched.</summary>
    public bool HasCaseHits =>
        Best is { Entry.Kind: not SearchKind.Guide } || Groups.Any(group => group.Kind != SearchKind.Guide);
}
