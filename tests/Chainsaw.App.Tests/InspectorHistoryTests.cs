using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>The inspector's tabs and history (sketch AN1b), and taking back and undoing from it.</summary>
public sealed class InspectorHistoryTests
{
    /// <summary>A moment in the sample's history.</summary>
    private static readonly DateTimeOffset Morning = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A made-up Ethereum transaction (never a real one).</summary>
    private static readonly ChainTransactionRef EthereumTx = new(Chain.Ethereum, "0x" + new string('a', 64));

    /// <summary>The tabs show for an actor, not for a line, and a form among the facts shows the facts.</summary>
    [Fact]
    public void TheTabsFollowTheChosenNode()
    {
        var rig = new Rig();

        rig.Graph.Choose(null, GraphSamples.VK);
        Assert.False(rig.History.HasNode);
        rig.History.ShowHistory();
        Assert.False(rig.History.IsShown);

        rig.Graph.Choose(GraphSamples.KKey, null);
        Assert.True(rig.History.HasNode);
        rig.History.ShowHistory();
        Assert.True(rig.History.IsShown);
        Assert.False(rig.History.IsFactsShown);

        rig.Forms.Open(InspectorForm.Label);
        Assert.False(rig.History.IsShown);

        rig.History.ShowHistory();
        rig.Graph.Choose(null, GraphSamples.VK);
        Assert.False(rig.History.IsShown);
    }

    /// <summary>The history is read for the chosen node, and an empty or unreadable history is said.</summary>
    [Fact]
    public void TheHistoryIsReadAndSaysWhenEmptyOrUnreadable()
    {
        var rig = new Rig();

        rig.Graph.Choose(GraphSamples.UKey, null);
        Assert.Equal(1, rig.Editor.HistoryReads);
        Assert.Equal(InspectorHistoryViewModel.EmptyText, rig.History.Notice);
        Assert.Equal("0", rig.History.CountText);

        rig.Editor.HistoryFailure = new InvalidOperationException("stängt");
        rig.Graph.Choose(GraphSamples.EKey, null);
        Assert.Equal(InspectorHistoryViewModel.ReadFailedText, rig.History.Notice);
    }

    /// <summary>
    /// Labels in time order: the user's own with Ta tillbaka, a sanction hit without, and a label taken back struck
    /// through with its taking back as a row of its own.
    /// </summary>
    [Fact]
    public void LabelsAndTheirTakingBackAreRowsInTimeOrder()
    {
        var own = Entry(1, GraphSamples.K1, LabelKind.Exchange, "Börs A", LabelSource.Own, Morning);
        var gone = Entry(2, GraphSamples.K1, LabelKind.Mixer, "Mixer X", LabelSource.Own, Morning.AddHours(-2));
        var hit = Entry(3, GraphSamples.K2, LabelKind.Sanctioned, "Entitet", LabelSource.SanctionList, Morning);
        var retraction = new LabelRetraction(4, Who("Max"), Morning.AddHours(-1), "Fel adress", 9);
        var data = Data(labels: [new(own, null), new(gone, retraction), new(hit, null)]);

        var rows = NodeHistory.Build(GraphSamples.Scene(), Node(GraphSamples.KKey), data);

        Assert.Equal(
            ["Etikett · Mixer X", "Tillbakatagen: Mixer X", "Etikett · Börs A", "Etikett · Entitet"],
            rows.Select(row => row.Text));
        Assert.True(rows[0].IsWithdrawn);
        Assert.False(rows[0].CanUndo);
        Assert.Equal("Max · Fel adress", rows[1].Sub);
        Assert.Equal(HistoryKind.Withdrawal, rows[1].Kind);
        Assert.True(rows[2].CanUndo);
        Assert.Equal(NodeHistory.RetractText, rows[2].UndoText);
        Assert.True(rows[2].ReachesLibrary);
        Assert.Equal(1, rows[2].LabelSequence);
        Assert.False(rows[3].CanUndo);
        Assert.Contains("OFAC", rows[3].Sub, StringComparison.Ordinal);
    }

    /// <summary>
    /// A victim's label and one marked Sprid inte never reached the library, so the form does not say it.
    /// </summary>
    [Fact]
    public void ALabelThatNeverSpreadDoesNotTellTheLibrary()
    {
        var victim = Entry(1, GraphSamples.U, LabelKind.Victim, null, LabelSource.Own, Morning);
        var kept = Entry(2, GraphSamples.U, LabelKind.Exchange, null, LabelSource.Own, Morning) with
        {
            DoNotSpread = true,
        };

        var rows = NodeHistory.Build(
            GraphSamples.Scene(), Node(GraphSamples.UKey), Data(labels: [new(victim, null), new(kept, null)]));

        Assert.All(rows, row => Assert.False(row.ReachesLibrary));
    }

    /// <summary>
    /// The splits of one breaking out (one journal entry) are one row with Ångra and all their sequences, whatever
    /// their heuristics; an undone breaking out is struck through, keeps its count, and its undoing is a row.
    /// </summary>
    [Fact]
    public void ABreakingOutIsOneRowAndItsUndoingAnother()
    {
        var first = Split(1, GraphSamples.Address(20), GraphSamples.K1, Morning, 5);
        var second = Split(2, GraphSamples.Address(20), GraphSamples.K2, Morning, 5) with
        {
            Heuristic = HeuristicNames.CommonInputOwnership,
        };
        var later = Split(3, GraphSamples.Address(21), GraphSamples.K1, Morning.AddHours(1), 7);
        var undo = new ClusterSplitUndo(4, 3, null, 8, Morning.AddHours(2));
        var data = Data(
            splits: [new(first, null), new(second, null), new(later, undo)],
            operators: new Dictionary<long, string> { [5] = "Max", [7] = "Max", [8] = "Kim" });

        var rows = NodeHistory.Build(GraphSamples.Scene(), Node(GraphSamples.KKey), data);

        Assert.Equal(
            ["Uppbruten · 1 ut", "Uppbruten · 1 ut", "Ångrad: uppbrytningen"], rows.Select(row => row.Text));
        Assert.Equal([1L, 2L], rows[0].SplitSequences);
        Assert.Equal(NodeHistory.UndoText, rows[0].UndoText);
        Assert.Equal("Max · Växel, CIO", rows[0].Sub);
        Assert.True(rows[1].IsWithdrawn);
        Assert.False(rows[1].CanUndo);
        Assert.Equal("Kim", rows[2].Sub);
    }

    /// <summary>A breaking out reads the same from the address that went out, before and after an undo.</summary>
    [Fact]
    public void ABreakingOutReadsTheSameFromItsOtherSide()
    {
        var first = Split(1, GraphSamples.U, GraphSamples.K1, Morning, 5);
        var second = Split(2, GraphSamples.U, GraphSamples.K2, Morning, 5);
        var undo = new ClusterSplitUndo(3, 1, "Fel", 6, Morning.AddHours(1));

        var before = NodeHistory.Build(
            GraphSamples.Scene(), Node(GraphSamples.UKey), Data(splits: [new(first, null), new(second, null)]));
        var after = NodeHistory.Build(
            GraphSamples.Scene(), Node(GraphSamples.UKey),
            Data(splits: [new(first, undo), new(second, undo with { Sequence = 4, SplitSequence = 2 })]));

        Assert.Equal("Uppbruten · 1 ut", Assert.Single(before).Text);
        Assert.Equal("Uppbruten · 1 ut", after[0].Text);
        Assert.True(after[0].IsWithdrawn);
        Assert.Equal("Ångrad: uppbrytningen", Assert.Single(after, row => row.Kind == HistoryKind.Withdrawal).Text);
    }

    /// <summary>Splits stored under different journal entries are separate breakings out.</summary>
    [Fact]
    public void SplitsUnderDifferentJournalEntriesAreSeparate()
    {
        var first = Split(1, GraphSamples.Address(20), GraphSamples.K1, Morning, 5);
        var second = Split(2, GraphSamples.Address(21), GraphSamples.K1, Morning, 6);
        var third = Split(3, GraphSamples.Address(22), GraphSamples.K1, Morning, 6);

        var groups = NodeHistory.BreakingsOut([new(third, null), new(first, null), new(second, null)]);

        Assert.Equal([[1L], [2L, 3L]], groups.Select(group => group.Select(entry => entry.Split.Sequence).ToList()));
    }

    /// <summary>
    /// A link from money that came to the node is a dashed row with Ta tillbaka; a link taken back is struck through.
    /// The writer of a journal entry the case could not name is said to be unknown.
    /// </summary>
    [Fact]
    public void LinksFromTheNodesCoinsAreRows()
    {
        var coin = new Outpoint(GraphSamples.Tx[1], 0);
        var link = new CrossChainLink(1, coin, EthereumTx, "Swap", 5, Morning);
        var gone = new CrossChainLink(2, coin, EthereumTx, "Först", 6, Morning.AddHours(-1));
        var elsewhere = new CrossChainLink(3, new Outpoint(GraphSamples.Tx[2], 0), EthereumTx, "Annan", 7, Morning);
        var data = Data(
            links: [new(link, null), new(gone, new CrossChainUnlink(4, "Fel tx", 8, Morning.AddMinutes(-30))),
                new(elsewhere, null)],
            operators: new Dictionary<long, string> { [5] = "Max", [8] = "Max" });

        var rows = NodeHistory.Build(GraphSamples.Scene(), Node(GraphSamples.KKey), data);

        Assert.Equal(
            ["Länk till annan kedja · ETH", "Tillbakatagen: länken till ETH", "Länk till annan kedja · ETH"],
            rows.Select(row => row.Text));
        Assert.True(rows[0].IsWithdrawn);
        Assert.StartsWith(NodeHistory.UnknownOperator, rows[0].Sub, StringComparison.Ordinal);
        Assert.Equal("Max · Fel tx", rows[1].Sub);
        Assert.True(rows[2].CanUndo);
        Assert.True(rows[2].IsDashed);
        Assert.Equal(link, rows[2].Link);
    }

    /// <summary>
    /// The facts and the automatic conclusions have no button: when money first came, with its block, and each
    /// heuristic that clustered the addresses.
    /// </summary>
    [Fact]
    public void FactsAndConclusionsHaveNoButton()
    {
        var evidence = new ClusterEvidence(
            1, new HeuristicId(HeuristicNames.CommonInputOwnership, 1), GraphSamples.Tx[2],
            [GraphSamples.K1, GraphSamples.K2], EvidenceStrength.Merge, "samma ägare", null, null, 1, Morning);
        var cluster = InspectorFormsTests.SampleCluster() with
        {
            Links = [new ClusterLink(evidence, LinkState.Merged)],
        };
        var node = Node(GraphSamples.KKey) is var view
            ? view with { Node = view.Node with { FirstReceived = GraphSamples.Block(101).Time } }
            : null;

        var rows = NodeHistory.Build(GraphSamples.Scene(), node!, Data(cluster: cluster));

        Assert.Equal(["Först in", "Klustrad"], rows.Select(row => row.Text));
        Assert.Equal("Block 101 · på kedjan", rows[0].Sub);
        Assert.True(rows[0].IsFact);
        Assert.Equal("CIO v1 · 2 adresser", rows[1].Sub);
        Assert.True(rows[1].IsDashed);
        Assert.All(rows, row => Assert.False(row.CanUndo));
    }

    /// <summary>
    /// Ta tillbaka folds out an optional reason; Enter takes the label back without one, the graph is read again and
    /// the form closes. Esc closes a form without storing.
    /// </summary>
    [Fact]
    public async Task ALabelIsTakenBackWithoutAReason()
    {
        var rig = new Rig();
        rig.Editor.History = Data(
            labels: [new(Entry(7, GraphSamples.U, LabelKind.Exchange, "Börs", LabelSource.Own, Morning), null)]);
        rig.Graph.Choose(GraphSamples.UKey, null);
        var row = Assert.Single(rig.History.Rows);

        rig.History.Toggle(row);
        Assert.True(row.IsOpen);
        Assert.True(rig.History.IsLabelOpen);
        Assert.True(rig.History.ReachesLibrary);
        Assert.True(rig.History.CanSubmit);
        Assert.Equal(NodeHistory.RetractText, rig.History.SubmitText);
        rig.History.Cancel();
        Assert.False(rig.History.IsOpen);
        Assert.Empty(rig.Editor.Undone);

        rig.History.Toggle(rig.History.Rows[0]);
        var reads = rig.Editor.HistoryReads;
        await rig.History.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(("label", "7", (string?)null), Assert.Single(rig.Editor.Undone));
        Assert.False(rig.History.IsOpen);
        Assert.True(rig.Editor.HistoryReads > reads);
    }

    /// <summary>A reason is passed on trimmed; one that is too long cannot be stored.</summary>
    [Fact]
    public async Task AReasonIsPassedOnAndATooLongOneIsRefused()
    {
        var rig = new Rig();
        var coin = new Outpoint(GraphSamples.Tx[1], 0);
        rig.Editor.History = Data(links: [new(new CrossChainLink(3, coin, EthereumTx, "Swap", 5, Morning), null)]);
        rig.Graph.Choose(GraphSamples.KKey, null);
        rig.History.Toggle(rig.History.Rows[0]);
        Assert.True(rig.History.IsLinkOpen);

        rig.History.Reason = new string('x', InspectorForms.MostReasonLength + 1);
        Assert.False(rig.History.CanSubmit);
        rig.History.Reason = "  Fel tx  ";
        await rig.History.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(("link", "3", (string?)"Fel tx"), Assert.Single(rig.Editor.Undone));
    }

    /// <summary>
    /// Undoing a breaking out cannot be stored until its confirmation has read which addresses merge again and by
    /// which heuristic; then all its splits are undone together.
    /// </summary>
    [Fact]
    public async Task UndoingABreakingOutIsConfirmedFirst()
    {
        var rig = new Rig();
        rig.Editor.History = Data(splits:
        [
            new(Split(1, GraphSamples.Address(20), GraphSamples.K1, Morning, 5), null),
            new(Split(2, GraphSamples.Address(20), GraphSamples.K2, Morning, 5), null),
        ]);
        var link = InspectorFormsTests.SampleCluster().Links[0];
        rig.Editor.Preview = new UndoSplitPreview(
            [Split(1, GraphSamples.K1, GraphSamples.Address(20), Morning, 5)],
            [new ClusterMember(GraphSamples.Address(20), link, GraphSamples.K1)],
            [new HeuristicId(HeuristicNames.CommonInputOwnership, 1)]);
        rig.Graph.Choose(GraphSamples.KKey, null);

        rig.History.Toggle(rig.History.Rows[0]);

        Assert.True(rig.History.IsSplitOpen);
        Assert.Equal(NodeHistory.UndoText, rig.History.SubmitText);
        var rejoined = Assert.Single(rig.History.Rejoined);
        Assert.Equal("CIO v1", rejoined.Heuristic);
        Assert.Equal("CIO v1", rig.History.RejoinHeuristics);
        Assert.Equal(string.Empty, rig.History.RejoinNotice);
        Assert.True(rig.History.CanSubmit);

        await rig.History.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(("split", "1,2", (string?)null), Assert.Single(rig.Editor.Undone));
    }

    /// <summary>
    /// A large cluster's confirmation lists the first addresses and counts the rest; reading the graph again keeps the
    /// open form and its reason and reads the confirmation again.
    /// </summary>
    [Fact]
    public void ALongConfirmationIsCountedAndARefreshKeepsTheForm()
    {
        var rig = new Rig();
        var split = Split(1, GraphSamples.Address(20), GraphSamples.K1, Morning, 5);
        rig.Editor.History = Data(splits: [new(split, null)]);
        var link = InspectorFormsTests.SampleCluster().Links[0];
        rig.Editor.Preview = new UndoSplitPreview(
            [split],
            [.. Enumerable.Range(30, 15)
                .Select(i => new ClusterMember(GraphSamples.Address(i), link, GraphSamples.K1))],
            [link.Evidence.Heuristic]);
        rig.Graph.Choose(GraphSamples.KKey, null);
        rig.History.Toggle(rig.History.Rows[0]);

        Assert.Equal(InspectorHistoryViewModel.MostRejoinRows, rig.History.Rejoined.Count);
        Assert.Equal("+3 till", rig.History.RejoinMore);

        rig.History.Reason = "Fel av mig";
        rig.Editor.Preview = new UndoSplitPreview([split], [], []);
        rig.Graph.Choose(GraphSamples.KKey, null);

        Assert.True(rig.History.IsSplitOpen);
        Assert.Equal("Fel av mig", rig.History.Reason);
        Assert.Empty(rig.History.Rejoined);
        Assert.Equal(string.Empty, rig.History.RejoinMore);
        Assert.Equal(InspectorHistoryViewModel.NothingRejoinsText, rig.History.RejoinNotice);
    }

    /// <summary>When nothing would merge again, or the splits are already undone, the confirmation says so.</summary>
    [Fact]
    public void TheConfirmationSaysWhenNothingMergesAgain()
    {
        var rig = new Rig();
        var split = Split(1, GraphSamples.K1, GraphSamples.Address(20), Morning, 5);
        rig.Editor.History = Data(splits: [new(split, null)]);
        rig.Editor.Preview = new UndoSplitPreview([split], [], []);
        rig.Graph.Choose(GraphSamples.KKey, null);

        rig.History.Toggle(rig.History.Rows[0]);
        Assert.Equal(InspectorHistoryViewModel.NothingRejoinsText, rig.History.RejoinNotice);
        Assert.True(rig.History.CanSubmit);

        rig.History.Cancel();
        rig.Editor.Preview = null;
        rig.History.Toggle(rig.History.Rows[0]);
        Assert.Equal(InspectorHistoryViewModel.AlreadyText, rig.History.RejoinNotice);
        Assert.False(rig.History.CanSubmit);
    }

    /// <summary>
    /// What the case refuses is said in the form; something already taken back is said; a library that could not
    /// follow is said in the graph's notice.
    /// </summary>
    [Fact]
    public async Task FailuresAreSaid()
    {
        var rig = new Rig();
        rig.Editor.History = Data(
            labels: [new(Entry(7, GraphSamples.U, LabelKind.Exchange, "Börs", LabelSource.Own, Morning), null)]);
        rig.Graph.Choose(GraphSamples.UKey, null);

        rig.History.Toggle(rig.History.Rows[0]);
        rig.Editor.Failure = new CaseException(CaseError.NoOperatorProfile);
        await rig.History.SubmitCommand.ExecuteAsync(null);
        Assert.Contains("operatörsprofilen saknas", rig.History.Error, StringComparison.Ordinal);
        Assert.True(rig.History.IsOpen);

        rig.Editor.Failure = null;
        rig.Editor.Outcome = new UndoOutcome(false);
        await rig.History.SubmitCommand.ExecuteAsync(null);
        Assert.Equal(InspectorHistoryViewModel.AlreadyText, rig.History.Error);

        rig.Editor.Outcome = new UndoOutcome(true, GraphEditor.LibraryLaterText);
        rig.History.Toggle(rig.History.Rows[0]);
        rig.History.Toggle(rig.History.Rows[0]);
        await rig.History.SubmitCommand.ExecuteAsync(null);
        Assert.Contains(GraphEditor.LibraryLaterText, rig.Graph.Notice, StringComparison.Ordinal);
    }

    /// <summary>Another node closes the open form; the same node read again keeps it while it can be undone.</summary>
    [Fact]
    public void TheOpenFormFollowsTheNode()
    {
        var rig = new Rig();
        rig.Editor.History = Data(
            labels: [new(Entry(7, GraphSamples.U, LabelKind.Exchange, "Börs", LabelSource.Own, Morning), null)]);
        rig.Graph.Choose(GraphSamples.UKey, null);
        rig.History.Toggle(rig.History.Rows[0]);
        rig.History.Reason = "Skäl";

        rig.Graph.Choose(GraphSamples.UKey, null);
        Assert.True(rig.History.IsOpen);
        Assert.Equal("Skäl", rig.History.Reason);
        Assert.Same(rig.History.OpenRow, rig.History.Rows[0]);

        rig.Graph.Choose(GraphSamples.EKey, null);
        Assert.False(rig.History.IsOpen);
        Assert.Equal(string.Empty, rig.History.Reason);
    }

    /// <summary>A row that cannot be undone does not open a form.</summary>
    [Fact]
    public void ARowWithoutAButtonDoesNotOpen()
    {
        var rig = new Rig();
        rig.Editor.History = Data(labels:
        [
            new(Entry(7, GraphSamples.U, LabelKind.Sanctioned, "E", LabelSource.SanctionList, Morning), null),
        ]);
        rig.Graph.Choose(GraphSamples.UKey, null);

        rig.History.Toggle(rig.History.Rows[0]);

        Assert.False(rig.History.IsOpen);
    }

    /// <summary>
    /// A row writes its time short in local time, with the full time in local time and UTC as its tooltip.
    /// </summary>
    [Fact]
    public void ARowWritesItsTime()
    {
        var local = Morning.ToLocalTime();

        Assert.StartsWith(local.Day + " nov ", NodeHistory.Short(Morning), StringComparison.Ordinal);
        Assert.Contains("UTC", InspectorContent.Time(Morning), StringComparison.Ordinal);
    }

    /// <summary>Gives a node of the sample scene.</summary>
    /// <param name="key">The node's key.</param>
    /// <returns>The node.</returns>
    private static NodeView Node(string key) => GraphSamples.Scene().Node(key)!;

    /// <summary>Gives an operator.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The profile.</returns>
    private static OperatorProfile Who(string name) => new(name, "Analytiker", "Enheten");

    /// <summary>Gives a sealed label.</summary>
    /// <param name="sequence">Its sequence.</param>
    /// <param name="address">What it is on.</param>
    /// <param name="kind">What it says.</param>
    /// <param name="name">Its name.</param>
    /// <param name="source">How the case got it.</param>
    /// <param name="when">When it was set.</param>
    /// <returns>The label.</returns>
    private static LabelEntry Entry(
        long sequence, BitcoinAddress address, LabelKind kind, string? name, LabelSource source, DateTimeOffset when)
    {
        var sourceName = source == LabelSource.SanctionList ? "OFAC SDN" : "egen";
        var origin = new LabelOrigin(
            "origin-" + sequence, source, source, sourceName, "v1", Who("Max"), when, "Sett", null);
        return new LabelEntry(sequence, LabelSubject.ForAddress(address), kind, name, origin, false, sequence);
    }

    /// <summary>Gives a split.</summary>
    /// <param name="sequence">Its sequence.</param>
    /// <param name="first">One address.</param>
    /// <param name="second">The other.</param>
    /// <param name="when">When.</param>
    /// <param name="journal">Its journal entry.</param>
    /// <returns>The split.</returns>
    private static ClusterSplit Split(
        long sequence, BitcoinAddress first, BitcoinAddress second, DateTimeOffset when, long journal) =>
        new(sequence, first, second, HeuristicNames.ChangeAddress, "Två ägare", journal, when);

    /// <summary>Gives what the history reads.</summary>
    /// <param name="labels">The labels.</param>
    /// <param name="splits">The splits.</param>
    /// <param name="links">The links.</param>
    /// <param name="cluster">The cluster.</param>
    /// <param name="operators">Who wrote the journal entries.</param>
    /// <returns>The data.</returns>
    private static NodeHistoryData Data(
        IReadOnlyList<LabelHistoryEntry>? labels = null,
        IReadOnlyList<SplitHistoryEntry>? splits = null,
        IReadOnlyList<CrossChainLinkHistoryEntry>? links = null,
        Cluster? cluster = null,
        IReadOnlyDictionary<long, string>? operators = null) =>
        new(labels ?? [], splits ?? [], links ?? [], cluster, operators ?? new Dictionary<long, string>());

    /// <summary>The graph with the forms and the history over fakes.</summary>
    private sealed class Rig
    {
        public Rig()
        {
            var workflow = new CaseWorkflow(
                new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), Session);
            Graph = new GraphViewModel(
                Session, workflow, Source, TestWays.For(new TestExitConnection()), new InlineUi(), Editor,
                new TestClock());
            Session.Open(new TestCase("2026-0001"));
        }

        public CaseSession Session { get; } = new();

        public TestGraphSource Source { get; } = new();

        public TestGraphEditor Editor { get; } = new();

        public GraphViewModel Graph { get; }

        public InspectorFormsViewModel Forms => Graph.Forms;

        public InspectorHistoryViewModel History => Graph.History;
    }
}
