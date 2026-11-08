using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>The empty graph that guides (sketch GR6c), without WPF.</summary>
public sealed class GraphStartTests
{
    /// <summary>The BIP 173 test vector: a Bitcoin address of the right form that belongs to no one.</summary>
    private const string Address = "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4";

    /// <summary>A tx hash of Ethereum's form, made up.</summary>
    private static readonly string EthereumTx = "0x" + new string('a', 64);

    /// <summary>The cases, the session and the graph, with no case open.</summary>
    private sealed class Rig
    {
        public Rig()
        {
            Workflow = new CaseWorkflow(Store, Tracing, new TestVault(VaultState.Unlocked), Session);
            Graph = new GraphViewModel(
                Session, Workflow, Source, TestWays.For(Exit), new InlineUi(), new TestGraphEditor(), Clock);
            Graph.GuideWanted += (_, request) => Guides.Add(request);
        }

        public TestCaseStore Store { get; } = new();

        public CaseSession Session { get; } = new();

        public TestTracing Tracing { get; } = new();

        public TestGraphSource Source { get; } = new();

        public TestExitConnection Exit { get; } = new();

        public TestClock Clock { get; } = new();

        public CaseWorkflow Workflow { get; }

        public GraphViewModel Graph { get; }

        public GraphStartViewModel Start => Graph.Start;

        public List<GuideRequest> Guides { get; } = [];

        /// <summary>Opens a case without a start point.</summary>
        public void OpenEmptyCase()
        {
            var empty = GraphSamples.Graph() with { StartPoints = [], Nodes = [], Edges = [] };
            Source.Scene = () => GraphSamples.Scene(graph: empty);
            Session.Open(new TestCase("2026-0117"));
        }
    }

    [Fact]
    public void Without_a_case_the_hub_is_shown()
    {
        var rig = new Rig();

        Assert.Equal(GraphStartMode.NoCase, rig.Start.Mode);
        Assert.True(rig.Start.IsShown);
    }

    [Fact]
    public void A_case_without_a_start_point_shows_the_empty_start_point()
    {
        var rig = new Rig();

        rig.OpenEmptyCase();

        Assert.Equal(GraphStartMode.NoStartPoint, rig.Start.Mode);
    }

    [Fact]
    public void A_graph_hides_the_guide()
    {
        var rig = new Rig();

        rig.Session.Open(new TestCase("2026-0001"));

        Assert.Equal(GraphStartMode.None, rig.Start.Mode);
        Assert.False(rig.Start.IsShown);
    }

    [Fact]
    public void The_first_time_there_are_no_recent_cases()
    {
        var rig = new Rig();

        Assert.True(rig.Start.IsFirstTime);
        Assert.Empty(rig.Start.Recent);
    }

    [Fact]
    public async Task The_four_latest_cases_stand_around_nytt_arende_the_newest_first()
    {
        var rig = new Rig();
        rig.Store.AddFile("2026-0071", minutesAgo: 60 * 24 * 30);
        rig.Store.AddFile("2026-0114", minutesAgo: 60 * 20);
        rig.Store.AddFile("2026-0098", minutesAgo: 60 * 24 * 10);
        rig.Store.AddFile("2026-0109", minutesAgo: 60 * 24 * 4);
        rig.Store.AddFile("2026-0050", minutesAgo: 60 * 24 * 60);

        await rig.Workflow.RefreshCasesAsync();

        Assert.False(rig.Start.IsFirstTime);
        Assert.Equal(
            ["2026-0114", "2026-0109", "2026-0098", "2026-0071"],
            rig.Start.Recent.Select(item => item.CaseNumber));
    }

    [Theory]
    [InlineData(30, "ändrad i dag 11:30")]
    [InlineData(60 * 20, "ändrad i går 16:00")]
    [InlineData(60 * 24 * 4, "ändrad 3 nov")]
    [InlineData(60 * 24 * 400, "ändrad 3 okt 2025")]
    public void When_a_case_was_saved_is_told_in_words(int minutesAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

        var text = GraphStartViewModel.ChangedText(now.AddMinutes(-minutesAgo), now, TimeZoneInfo.Utc);

        Assert.Equal(expected, text);
    }

    [Fact]
    public void The_form_the_text_matches_is_lit()
    {
        var rig = new Rig();
        rig.OpenEmptyCase();

        rig.Start.StartPointText = Address;

        Assert.True(rig.Start.CanAdd);
        Assert.Equal(["Bitcoin-adress"], rig.Start.FormsShown.Where(form => form.IsLit).Select(form => form.Name));

        rig.Start.StartPointText = EthereumTx;

        Assert.Equal(["Ethereum-tx"], rig.Start.FormsShown.Where(form => form.IsLit).Select(form => form.Name));
    }

    [Fact]
    public void Nothing_is_lit_for_text_that_is_not_a_start_point()
    {
        var rig = new Rig();
        rig.OpenEmptyCase();

        rig.Start.StartPointText = "inte en adress";

        Assert.False(rig.Start.CanAdd);
        Assert.DoesNotContain(rig.Start.FormsShown, form => form.IsLit);
    }

    [Fact]
    public async Task Enter_adds_the_start_point_to_the_open_case()
    {
        var rig = new Rig();
        rig.OpenEmptyCase();
        rig.Start.StartPointText = Address;

        await rig.Start.AddCommand.ExecuteAsync(null);

        var added = Assert.Single(rig.Tracing.Added);
        Assert.Equal(Address, added.StartPoint.Address?.ToString());
        Assert.Equal(string.Empty, rig.Start.ErrorText);
    }

    [Fact]
    public async Task Text_that_is_not_recognised_is_not_added_and_says_why()
    {
        var rig = new Rig();
        rig.OpenEmptyCase();
        rig.Start.StartPointText = "hej";

        await rig.Start.AddCommand.ExecuteAsync(null);

        Assert.Empty(rig.Tracing.Added);
        Assert.Equal(GraphStartViewModel.NotRecognisedText, rig.Start.ErrorText);
    }

    [Fact]
    public void A_paste_goes_into_the_field_only_in_a_case_without_a_start_point()
    {
        var rig = new Rig();

        Assert.False(rig.Start.Paste(Address));

        rig.OpenEmptyCase();

        Assert.True(rig.Start.Paste($"  {Address}\r\n"));
        Assert.Equal(Address, rig.Start.StartPointText);
    }

    [Fact]
    public void Offline_says_that_nothing_can_be_fetched_and_online_names_the_exit()
    {
        var rig = new Rig();

        rig.Start.ShowExit(isOnline: false, name: null);

        Assert.Equal(GraphStartViewModel.OfflineSendText, rig.Start.SendText);
        Assert.True(rig.Start.IsFootOffline);

        rig.Start.ShowExit(isOnline: true, name: "Stockholm");

        Assert.Contains("genom Stockholm", rig.Start.SendText, StringComparison.Ordinal);
        Assert.False(rig.Start.IsFootOffline);
    }

    [Fact]
    public void So_tracks_one_in_chainsaw_opens_its_section_of_the_guide()
    {
        var rig = new Rig();

        rig.Start.GuideCommand.Execute(null);

        Assert.Equal(GuideTopic.HowToTrace, Assert.Single(rig.Guides).Topic);
    }

    [Fact]
    public async Task A_recent_case_is_opened()
    {
        var rig = new Rig();
        rig.Store.AddFile("2026-0114", minutesAgo: 10);
        await rig.Workflow.RefreshCasesAsync();

        await rig.Start.OpenCaseCommand.ExecuteAsync(rig.Start.Recent[0]);

        Assert.Equal("2026-0114", rig.Session.Current?.Info.CaseNumber);
    }

    [Fact]
    public void The_field_is_emptied_when_another_case_is_shown()
    {
        var rig = new Rig();
        rig.OpenEmptyCase();
        rig.Start.StartPointText = Address;

        rig.Session.Forget();

        Assert.Equal(string.Empty, rig.Start.StartPointText);
    }
}
