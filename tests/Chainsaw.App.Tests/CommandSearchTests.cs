using System.Diagnostics;
using Chainsaw.App.Commands;
using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.App.Search;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>
/// The search in the command bar (sketch KR1a): what it finds in the graph and the Guide, how the hits are ranked into
/// <em>Bästa träff</em> and groups, and how the view model chooses, shows and takes a row.
/// </summary>
public sealed class CommandSearchTests
{
    /// <summary>The Guide built into the program.</summary>
    private static readonly GuideLibrary Library = GuideLibrary.Load();

    /// <summary>The exchange's label on E.</summary>
    private static readonly Label ExchangeA = GraphSamples.Label(GraphSamples.E, LabelKind.Exchange, "Börs A");

    /// <summary>The made-up graph with the exchange's label on E.</summary>
    private static GraphScene Scene() => GraphSamples.Scene(
        new Dictionary<string, IReadOnlyList<Label>> { [GraphSamples.EKey] = [ExchangeA] });

    /// <summary>The index over the made-up graph and the Guide.</summary>
    private static SearchIndex Index() => SearchIndex.Build(Scene(), Library);

    /// <summary>The whole text, its start, a word's start and the inside are told apart, ignoring case.</summary>
    [Fact]
    public void MatchQualityIsWholeStartWordOrInside()
    {
        Assert.Equal(MatchQuality.Whole, SearchMatch.Find("Kraken", "kraken").Quality);
        Assert.Equal(MatchQuality.Start, SearchMatch.Find("Kraken", "KRA").Quality);
        var word = SearchMatch.Find("Kraken, hot wallet", "hot");
        Assert.Equal((MatchQuality.WordStart, 8, 3), (word.Quality, word.Index, word.Length));
        Assert.Equal(MatchQuality.Inside, SearchMatch.Find("bc1qxyz", "xy").Quality);
        Assert.False(SearchMatch.Find("Kraken", "bitstamp").IsMatch);
    }

    /// <summary>A long hash is cut in the middle around the match, which stays whole.</summary>
    [Fact]
    public void LongTextIsCutAroundTheMatch()
    {
        var txid = new string('a', 30) + "beef" + new string('c', 30);
        var text = SearchText.Of(txid, SearchMatch.Find(txid, "beef"), 22);

        Assert.Equal("beef", text.Match);
        Assert.Contains(SearchText.Ellipsis, text.Before, StringComparison.Ordinal);
        Assert.Contains(SearchText.Ellipsis, text.After, StringComparison.Ordinal);
        Assert.True(text.Shown.Length <= 24, text.Shown);
        Assert.Equal("kort", SearchText.Of("kort", SearchMatch.None, 22).Shown);
    }

    /// <summary>A label's name beats a Guide title that starts the same way; the Guide keeps its group.</summary>
    [Fact]
    public void LabelIsTheBestHitBeforeTheGuide()
    {
        var outcome = Index().Search("börs");

        var best = Assert.IsType<SearchHit>(outcome.Best);
        Assert.Equal(SearchKind.Label, best.Entry.Kind);
        Assert.Equal("Börs A", best.Entry.Text);
        Assert.Equal(GraphSamples.EKey, best.Entry.NodeKey);
        Assert.Contains("1 adress", best.Entry.Lead, StringComparison.Ordinal);
        Assert.Contains(outcome.Groups, group => group.Kind == SearchKind.Guide
            && group.Hits.Any(hit => hit.Entry.GuideSection == "borser"));
        Assert.DoesNotContain(outcome.Groups.SelectMany(group => group.Hits), hit => ReferenceEquals(hit, best));
    }

    /// <summary>
    /// The groups come in the sketch's order: Etiketter, Adresser, Tx, Kluster and Guide; a cluster named by the
    /// written start is the best hit, as in the sketch's <c>bc1q</c>.
    /// </summary>
    [Fact]
    public void GroupsFollowTheSketchsOrder()
    {
        var outcome = Index().Search("bc1q");
        var kinds = outcome.Groups.Select(group => group.Kind).ToList();

        Assert.Equal(kinds.OrderBy(kind => SearchKinds.InOrder.ToList().IndexOf(kind)), kinds);
        Assert.Contains(SearchKind.Address, kinds);
        Assert.Equal(SearchKind.Cluster, outcome.Best!.Entry.Kind);
        Assert.Equal(GraphSamples.KKey, outcome.Best.Entry.NodeKey);
        var byAddress = Index().Search(GraphSamples.K1.Value[..12]);
        Assert.Equal(SearchKind.Address, byAddress.Best!.Entry.Kind);
        Assert.Equal([SearchKind.Cluster], byAddress.Groups.Select(group => group.Kind));
    }

    /// <summary>A group shows four hits and says how many there are, and Tab folds it out.</summary>
    [Fact]
    public void GroupShowsFourAndUnfoldsWithTab()
    {
        var index = Index();
        var addresses = index.Search("bc1q").Groups.Single(group => group.Kind == SearchKind.Address);

        Assert.Equal(SearchIndex.GroupRows, addresses.Hits.Count);
        Assert.True(addresses.HasMore);
        var unfolded = index.Search("bc1q", unfolded: SearchKind.Address).Groups
            .Single(group => group.Kind == SearchKind.Address);
        Assert.Equal(unfolded.Total, unfolded.Hits.Count);
        Assert.False(unfolded.HasMore);
    }

    /// <summary>A whole address is the best hit, shown at its actor.</summary>
    [Fact]
    public void WholeAddressIsTheBestHit()
    {
        var best = Index().Search(GraphSamples.U.Value).Best!;

        Assert.Equal(SearchKind.Address, best.Entry.Kind);
        Assert.Equal(MatchQuality.Whole, best.Match.Quality);
        Assert.Equal(GraphSamples.UKey, best.Entry.NodeKey);
        Assert.True(best.Entry.IsMono);
    }

    /// <summary>A cluster is found by any of its addresses, below the address itself.</summary>
    [Fact]
    public void ClusterIsFoundByItsAddresses()
    {
        var outcome = Index().Search(GraphSamples.K2.Value);

        Assert.Equal(SearchKind.Address, outcome.Best!.Entry.Kind);
        var cluster = Assert.Single(outcome.Groups.Single(group => group.Kind == SearchKind.Cluster).Hits);
        Assert.Equal(GraphSamples.KKey, cluster.Entry.NodeKey);
        Assert.Contains("2 adresser", cluster.Entry.Sub, StringComparison.Ordinal);
        Assert.False(cluster.Match.IsMatch);
    }

    /// <summary>A tx on a line is shown at the line's receiver, with its sender, receiver, block and amount.</summary>
    [Fact]
    public void TransactionIsShownAtItsReceiver()
    {
        var best = Index().Search(GraphSamples.Tx[2].Value).Best!;

        Assert.Equal(SearchKind.Transaction, best.Entry.Kind);
        Assert.Equal(GraphSamples.EKey, best.Entry.NodeKey);
        Assert.Contains("Börs A", best.Entry.Sub, StringComparison.Ordinal);
        Assert.Contains("block 102", best.Entry.Sub, StringComparison.Ordinal);
        Assert.Equal("0,5000 BTC", best.Entry.Meta);
    }

    /// <summary>A tx the case has fetched that no line shows says so, and has nowhere in the graph to go.</summary>
    [Fact]
    public void FetchedTransactionOutsideTheGraphSaysSo()
    {
        var extra = GraphSamples.Transaction(5, [(GraphSamples.Address(9), 1m)], [(GraphSamples.Address(8), 0.9m)]);
        var graph = GraphSamples.Graph();
        graph = graph with
        {
            Transactions = new Dictionary<Txid, GraphTransaction>(graph.Transactions)
            {
                [extra.Txid] = new(extra, LookupStatus.FromCase, null),
            },
        };
        var index = SearchIndex.Build(GraphSamples.Scene(graph: graph), Library);

        var best = index.Search(extra.Txid.Value).Best!;

        Assert.StartsWith("Hämtad, inte i grafen", best.Entry.Sub, StringComparison.Ordinal);
        Assert.Null(best.Entry.NodeKey);
    }

    /// <summary><c>/guide</c> searches only the Guide; a concept finds its section.</summary>
    [Fact]
    public void GuideOnlyFindsTheSection()
    {
        var outcome = Index().Search("utxo", guideOnly: true);

        Assert.Equal("utxo-modellen", outcome.Best!.Entry.GuideSection);
        Assert.All(outcome.Groups, group => Assert.Equal(SearchKind.Guide, group.Kind));
        Assert.Equal("vaxeladress", Index().Search("vaxeladress", guideOnly: true).Best!.Entry.GuideSection);
    }

    /// <summary>One or two characters find words that start with them, never the inside of a hash.</summary>
    [Fact]
    public void ShortTextFindsOnlyStarts()
    {
        var outcome = Index().Search("qx");

        Assert.Null(outcome.Best);
        Assert.Empty(outcome.Groups);
    }

    /// <summary>A search overtaken by a newer one stops.</summary>
    [Fact]
    public void OvertakenSearchStops()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => Index().Search("bc1q", cancellationToken: cancelled.Token));
    }

    /// <summary>A large case is searched in one fast pass.</summary>
    [Fact]
    public void LargeCaseIsSearchedFast()
    {
        var look = SearchLook.Of(VisualLanguage.Of(NodeSort.Unknown));
        var entries = Enumerable.Range(0, 200_000)
            .Select(i => new SearchEntry(
                SearchKind.Address, $"bc1q{i:x40}", "Okänd", string.Empty, string.Empty, look, $"address:{i}", null)
            {
                IsMono = true,
            })
            .ToList();
        var index = new SearchIndex(entries);
        index.Search("warm");

        var watch = Stopwatch.StartNew();
        var outcome = index.Search($"{12_345:x40}");
        watch.Stop();

        Assert.Equal("address:12345", outcome.Best!.Entry.NodeKey);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), watch.Elapsed.ToString());
    }

    /// <summary>The view model searches off the UI thread, chooses the best hit and shows it in the graph.</summary>
    [Fact]
    public async Task ViewModelChoosesTheBestHitAndShowsIt()
    {
        var search = new CommandSearchViewModel(Library);
        search.UseScene(Scene());
        var shown = new List<SearchRowViewModel?>();
        search.ShowWanted += (_, row) => shown.Add(row);

        await search.SearchAsync("börs");

        Assert.NotNull(search.Best);
        Assert.Same(search.Best, search.Selected);
        Assert.True(search.Best!.IsSelected);
        Assert.Equal(GraphSamples.EKey, Assert.Single(shown)!.NodeKey);
        Assert.Equal(CommandSearchViewModel.ShowInGraphText, search.EnterText);
        Assert.Empty(search.EmptyText);
    }

    /// <summary>The arrows go through the rows, and each row is shown as it is chosen.</summary>
    [Fact]
    public async Task ArrowsMoveTheChoice()
    {
        var search = new CommandSearchViewModel(Library);
        search.UseScene(Scene());
        await search.SearchAsync("bc1q");
        var shown = new List<SearchRowViewModel?>();
        search.ShowWanted += (_, row) => shown.Add(row);

        search.Move(1);

        Assert.Same(search.Rows[1], search.Selected);
        Assert.False(search.Rows[0].IsSelected);
        Assert.Same(search.Rows[1], Assert.Single(shown));
        search.Move(-5);
        Assert.Same(search.Rows[0], search.Selected);
    }

    /// <summary>Tab folds out the chosen group and keeps the chosen row.</summary>
    [Fact]
    public async Task TabUnfoldsTheChosenGroup()
    {
        var search = new CommandSearchViewModel(Library);
        search.UseScene(Scene());
        await search.SearchAsync("bc1q");
        search.Move(1);
        var chosen = search.Selected!.Entry.Text;

        Assert.True(search.Unfold());
        await search.Searching;

        var addresses = search.Groups.Single(group => group.Kind == SearchKind.Address);
        Assert.False(addresses.HasMore);
        Assert.Equal(chosen, search.Selected!.Entry.Text);
    }

    /// <summary>
    /// An address the case does not have can be added as a start point, and the row says that nothing is fetched until
    /// it is expanded, and through which exit; offline, it says when.
    /// </summary>
    [Fact]
    public async Task UnknownAddressCanBeAdded()
    {
        var search = new CommandSearchViewModel(Library);
        search.UseScene(Scene());
        search.FollowExit(true, "Stockholm", OfflineWords.NoExitUntil);

        await search.SearchAsync(GraphSamples.Address(42).Value);

        Assert.True(search.IsUnknown);
        Assert.True(search.IsUnknownSelected);
        Assert.Equal("₿ Bitcoin", search.ChainText);
        Assert.Equal(CommandSearchViewModel.AddText, search.EnterText);
        Assert.Contains("genom Stockholm", search.UnknownText, StringComparison.Ordinal);
        Assert.Contains("Inget hämtas förrän", search.UnknownText, StringComparison.Ordinal);
        search.FollowExit(false, "Stockholm", OfflineWords.NoExitUntil);
        Assert.Contains(OfflineWords.NoExitUntil, search.UnknownText, StringComparison.Ordinal);
    }

    /// <summary>An address the case has is never offered as a new start point.</summary>
    [Fact]
    public async Task KnownAddressIsNotOfferedAgain()
    {
        var search = new CommandSearchViewModel(Library);
        search.UseScene(Scene());

        await search.SearchAsync(GraphSamples.E.Value);

        Assert.False(search.IsUnknown);
        Assert.Equal("₿ Bitcoin", search.ChainText);
        Assert.Equal(GraphSamples.EKey, search.Selected!.NodeKey);
    }

    /// <summary>A word the case does not have says so, and the Guide answers.</summary>
    [Fact]
    public async Task ConceptIsAnsweredByTheGuide()
    {
        var search = new CommandSearchViewModel(Library);
        search.UseScene(Scene());

        await search.SearchAsync("utxo");

        Assert.Equal("Inget i ärendet heter utxo. Guiden svarar på begrepp.", search.EmptyText);
        Assert.Equal("utxo-modellen", search.Selected!.GuideSection);
        Assert.Equal(CommandSearchViewModel.OpenGuideText, search.EnterText);
    }

    /// <summary><c>/guide</c> lists only the Guide; another command lists nothing.</summary>
    [Fact]
    public async Task CommandsListNothingButGuide()
    {
        var search = new CommandSearchViewModel(Library);
        search.UseScene(Scene());

        await search.SearchAsync("/guide utxo");
        Assert.All(search.Rows, row => Assert.Equal(SearchKind.Guide, row.Entry.Kind));
        Assert.Empty(search.ChainText);

        await search.SearchAsync("/open 2026");
        Assert.False(search.IsShown);
        Assert.Empty(search.Rows);
    }

    /// <summary>Without a case only the Guide is searched, and that is said.</summary>
    [Fact]
    public async Task WithoutCaseOnlyTheGuideIsSearched()
    {
        var search = new CommandSearchViewModel(Library);

        await search.SearchAsync("börs");

        Assert.Equal(CommandSearchViewModel.NoCaseText, search.EmptyText);
        Assert.All(search.Rows, row => Assert.Equal(SearchKind.Guide, row.Entry.Kind));
        Assert.False(search.IsUnknown);
    }

    /// <summary>A new graph is searched again, so that the list never shows what the graph no longer has.</summary>
    [Fact]
    public async Task NewGraphIsSearchedAgain()
    {
        var search = new CommandSearchViewModel(Library);
        search.UseScene(GraphSamples.Scene());
        await search.SearchAsync("börs");
        Assert.Equal(SearchKind.Guide, search.Best!.Entry.Kind);

        search.UseScene(Scene());
        await search.Searching;

        Assert.Equal(SearchKind.Label, search.Best!.Entry.Kind);
    }

    /// <summary>A click chooses a row, and a click on the chosen row takes it.</summary>
    [Fact]
    public async Task SecondClickTakesTheRow()
    {
        var search = new CommandSearchViewModel(Library);
        search.UseScene(Scene());
        await search.SearchAsync("bc1q");
        SearchRowViewModel? taken = null;
        search.Accepted += (_, row) => taken = row;

        search.Pick(search.Rows[2]);
        Assert.Null(taken);
        search.Pick(search.Rows[2]);

        Assert.Same(search.Rows[2], taken);
    }

    /// <summary>Enter in the bar takes the chosen row instead of running the text as a command.</summary>
    [Fact]
    public async Task EnterTakesTheChosenRow()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var search = new CommandSearchViewModel(Library);
        search.UseScene(Scene());
        var shell = new ShellViewModel(
            rig.Session, rig.Commands, rig.NewCase, rig.Workflow, rig.Way, rig.Exits, rig.Sanctions, rig.ExitPanel,
            rig.Settings, rig.Ui, search: search);
        SearchRowViewModel? taken = null;
        search.Accepted += (_, row) => taken = row;
        shell.OpenCommandBar();
        shell.CommandText = "börs";
        await search.Searching;

        await shell.SubmitAsync();

        Assert.Equal("Börs A", taken!.Entry.Text);
        Assert.Empty(shell.CommandAnswer);
        shell.FinishSearch();
        Assert.False(shell.IsCommandBarOpen);
        Assert.Empty(shell.CommandText);
    }

    /// <summary>The row's hint says what Enter does.</summary>
    [Fact]
    public void RowHintSaysWhatEnterDoes()
    {
        var outcome = Index().Search("börs");
        var label = new SearchRowViewModel(outcome.Best!, isBest: true);
        var guide = new SearchRowViewModel(
            outcome.Groups.Single(group => group.Kind == SearchKind.Guide).Hits[0], isBest: false);

        Assert.Equal(SearchRowViewModel.ShowInGraphHint, label.Hint);
        Assert.Equal(SearchRowViewModel.OpenGuideHint, guide.Hint);
        Assert.Equal("Börs", label.Text.Match);
        Assert.Equal(" A", label.Text.After);
        Assert.Equal("Etikett", label.KindText);
    }

    /// <summary><c>/guide</c> opens the Guide's panel at the section, and says so when the Guide has nothing.</summary>
    [Fact]
    public async Task GuideCommandOpensThePanel()
    {
        var panel = new GuidePanelViewModel(Library);
        var command = new GuideCommand(Library, panel);

        var reply = await command.RunAsync("utxo");

        Assert.False(reply.IsError);
        Assert.Equal("utxo-modellen", panel.Page!.Section.Id);
        Assert.True((await command.RunAsync("zzzzqqq")).IsError);
        Assert.Contains("vaxeladress", command.Suggest("vax"));
    }
}
