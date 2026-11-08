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

/// <summary>Records what the inspector's forms ask the modules to store, and answers with a set cluster.</summary>
internal sealed class TestGraphEditor : IGraphEditor
{
    public List<LabelDraft> Labels { get; } = [];

    public List<SplitRequest> Splits { get; } = [];

    public List<(Outpoint Coin, ChainTransactionRef Target, string Reason)> Links { get; } = [];

    /// <summary>Gets or sets the cluster that is read; by default the sample's two addresses.</summary>
    public Cluster? Cluster { get; set; }

    /// <summary>Gets or sets what storing throws, or <see langword="null"/>.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Gets or sets after how many splits storing fails, when it does.</summary>
    public int FailAfter { get; set; }

    /// <summary>Gets or sets whether a split address stays in the cluster, as when another link holds it.</summary>
    public bool SplitsHold { get; set; } = true;

    /// <summary>Gets how many times the cluster was read.</summary>
    public int Reads { get; private set; }

    /// <summary>Reads the set cluster; an address split out leaves it while the splits hold.</summary>
    /// <param name="case">The case.</param>
    /// <param name="address">The address.</param>
    /// <param name="cancellationToken">Not used.</param>
    /// <returns>The cluster.</returns>
    public Task<Cluster> ReadClusterAsync(ICase @case, BitcoinAddress address, CancellationToken cancellationToken)
    {
        Reads++;
        var cluster = Cluster ?? InspectorFormsTests.SampleCluster();
        var gone = SplitsHold ? Splits.Select(split => split.First).ToHashSet() : [];
        return Task.FromResult(cluster with
        {
            Members = [.. cluster.Members.Where(member => !gone.Contains(member.Address))],
        });
    }

    public Task AddLabelAsync(ICase @case, LabelDraft draft)
    {
        if (Failure is { } failure)
        {
            throw failure;
        }

        Labels.Add(draft);
        return Task.CompletedTask;
    }

    public Task<SplitOutcome> SplitAsync(ICase @case, IReadOnlyList<SplitRequest> splits)
    {
        foreach (var split in splits)
        {
            if (Failure is { } failure && Splits.Count >= FailAfter)
            {
                return Task.FromResult(new SplitOutcome(Splits.Count, failure));
            }

            Splits.Add(split);
        }

        return Task.FromResult(new SplitOutcome(splits.Count, null));
    }

    public Task LinkCrossChainAsync(ICase @case, Outpoint coin, ChainTransactionRef target, string reason)
    {
        if (Failure is { } failure)
        {
            throw failure;
        }

        Links.Add((coin, target, reason));
        return Task.CompletedTask;
    }

    public List<(AccountSeed Movement, ChainTransactionRef Target, string Reason)> MovementLinks { get; } = [];

    public Task LinkCrossChainAsync(ICase @case, AccountSeed movement, ChainTransactionRef target, string reason)
    {
        if (Failure is { } failure)
        {
            throw failure;
        }

        MovementLinks.Add((movement, target, reason));
        return Task.CompletedTask;
    }

    /// <summary>Gets or sets what the history reads; empty by default.</summary>
    public NodeHistoryData History { get; set; } = new([], [], [], null, new Dictionary<long, string>());

    /// <summary>Gets or sets what reading the history throws, or <see langword="null"/>.</summary>
    public Exception? HistoryFailure { get; set; }

    /// <summary>Gets or sets what the confirmation of an undoing reads.</summary>
    public UndoSplitPreview? Preview { get; set; }

    /// <summary>Gets or sets what a taking back answers.</summary>
    public UndoOutcome Outcome { get; set; } = new(true);

    /// <summary>Gets what was taken back or undone, in order: the kind, what and the reason.</summary>
    public List<(string Kind, string What, string? Reason)> Undone { get; } = [];

    /// <summary>Gets how many times the history was read.</summary>
    public int HistoryReads { get; private set; }

    public Task<NodeHistoryData> ReadHistoryAsync(ICase @case, GraphNode node, CancellationToken cancellationToken)
    {
        HistoryReads++;
        return HistoryFailure is { } failure ? Task.FromException<NodeHistoryData>(failure) : Task.FromResult(History);
    }

    public Task<UndoOutcome> RetractLabelAsync(ICase @case, long sequence, string? reason) =>
        Undo("label", sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), reason);

    public Task<UndoOutcome> SetDoNotSpreadAsync(ICase @case, long sequence, bool doNotSpread) =>
        Undo(
            "spread",
            sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            doNotSpread ? "on" : "off");

    public Task<UndoSplitPreview?> PreviewUndoSplitsAsync(
        ICase @case, IReadOnlyList<long> splits, BitcoinAddress? from, CancellationToken cancellationToken) =>
        Task.FromResult(Preview);

    public Task<UndoOutcome> UndoSplitsAsync(ICase @case, IReadOnlyList<long> splits, string? reason) =>
        Undo("split", string.Join(",", splits), reason);

    public Task<UndoOutcome> UnlinkAsync(ICase @case, CrossChainLink link, string? reason) =>
        Undo("link", link.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), reason);

    public Task<UndoOutcome> UndoMergesAsync(ICase @case, IReadOnlyList<long> merges, string? reason) =>
        Undo("merge", string.Join(",", merges), reason);

    /// <summary>Gets or sets what undoing merges would merge again; by default nothing, while they hold.</summary>
    public UndoMergePreview? MergePreview { get; set; } = new([MergeSample], [], []);

    public Task<UndoMergePreview?> PreviewUndoMergesAsync(
        ICase @case, IReadOnlyList<long> merges, CancellationToken cancellationToken) =>
        Task.FromResult(MergePreview);

    /// <summary>A merge of the user's that holds, for a preview.</summary>
    private static ClusterMerge MergeSample => new(
        3, [GraphSamples.U, GraphSamples.E], GraphSamples.Tx[0], UserMerges.Id, "Grund", null, null, 5,
        DateTimeOffset.UnixEpoch);

    /// <summary>Records a taking back, or throws the set failure.</summary>
    /// <param name="kind">What sort.</param>
    /// <param name="what">Which.</param>
    /// <param name="reason">The reason.</param>
    /// <returns>The set outcome.</returns>
    private Task<UndoOutcome> Undo(string kind, string what, string? reason)
    {
        if (Failure is { } failure)
        {
            return Task.FromException<UndoOutcome>(failure);
        }

        Undone.Add((kind, what, reason));
        return Task.FromResult(Outcome);
    }
}

/// <summary>The inspector's forms (sketch IN1a) and their rules.</summary>
public sealed class InspectorFormsTests
{
    /// <summary>A made-up txid for the other chains (never a real one).</summary>
    private const string Hash = "2f86702824c1c099724caf4941d4072014b3ce107f80e222f828767efc2f9162";

    /// <summary>Gives the sample cluster K: K1 is the start, K2 joined by common-input-ownership in tx 2.</summary>
    /// <returns>The cluster.</returns>
    internal static Cluster SampleCluster()
    {
        var link = Merged(1, HeuristicNames.CommonInputOwnership, GraphSamples.K1, GraphSamples.K2);
        return new Cluster(
            GraphSamples.K1,
            [new ClusterMember(GraphSamples.K1, null, null), new ClusterMember(GraphSamples.K2, link, GraphSamples.K1)],
            [link],
            [],
            [],
            []);
    }

    /// <summary>Gives a merged link between addresses.</summary>
    /// <param name="sequence">The evidence's number.</param>
    /// <param name="heuristic">The heuristic's name.</param>
    /// <param name="addresses">The addresses.</param>
    /// <returns>The link.</returns>
    private static ClusterLink Merged(long sequence, string heuristic, params BitcoinAddress[] addresses) => new(
        new ClusterEvidence(
            sequence,
            new HeuristicId(heuristic, 1),
            GraphSamples.Tx[2],
            addresses,
            EvidenceStrength.Merge,
            "samma ägare",
            null,
            null,
            sequence,
            DateTimeOffset.UnixEpoch),
        LinkState.Merged);

    /// <summary>
    /// The split form's list of a cluster's addresses is virtualized in a bounded box, so that an exchange's cluster
    /// with thousands of addresses never builds a row for each (principle 6).
    /// </summary>
    [Fact]
    public void MemberListIsVirtualized()
    {
        var path = Path.Combine(GraphSceneTests.Root(), "src", "Chainsaw.App", "Views", "InspectorFormsView.xaml");
        var document = System.Xml.Linq.XDocument.Load(path);
        System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var list = document.Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Name") == "MemberList");

        Assert.NotNull(list.Attribute("MaxHeight"));
        Assert.Equal("True", (string?)list.Attribute("VirtualizingPanel.IsVirtualizing"));
        Assert.Contains(list.Descendants(), element => element.Name.LocalName == "VirtualizingStackPanel");
        Assert.Contains(
            list.Descendants(),
            element => element.Name.LocalName == "ScrollViewer"
                && (string?)element.Attribute("CanContentScroll") == "True");
    }

    /// <summary>
    /// The three buttons follow the chosen node: a label on an actor, a split on a cluster, a link on coins.
    /// </summary>
    [Fact]
    public void ButtonsFollowTheChosenNode()
    {
        var rig = new Rig();

        Assert.False(rig.Forms.HasNode);
        rig.Graph.Choose(GraphSamples.UKey, null);
        Assert.True(rig.Forms.HasNode);
        Assert.True(rig.Forms.CanLabel);
        Assert.False(rig.Forms.CanSplit);
        Assert.Equal(InspectorFormsViewModel.OnlyClusterText, rig.Forms.SplitToolTip);
        Assert.True(rig.Forms.CanCrossChain);
        rig.Graph.Choose(GraphSamples.KKey, null);
        Assert.True(rig.Forms.CanSplit);
        Assert.Equal("Bryt upp (U)", rig.Forms.SplitToolTip);
        rig.Graph.Choose(null, GraphSamples.VK);
        Assert.False(rig.Forms.HasNode);
        rig.Forms.Open(InspectorForm.Label);
        Assert.Equal(InspectorForm.None, rig.Forms.Form);
    }

    /// <summary>A split cannot be opened on an address, and choosing another node or Esc closes a form.</summary>
    [Fact]
    public void FormsCloseWithTheNodeAndEsc()
    {
        var rig = new Rig();
        rig.Graph.Choose(GraphSamples.UKey, null);

        rig.Forms.Open(InspectorForm.Split);
        Assert.Equal(InspectorForm.None, rig.Forms.Form);
        rig.Forms.Open(InspectorForm.Label);
        rig.Forms.Reason = "Skäl";
        Assert.True(rig.Forms.IsLabelOpen);
        rig.Forms.Open(InspectorForm.Label);
        Assert.False(rig.Forms.IsOpen);
        rig.Forms.Open(InspectorForm.Label);
        Assert.Equal(string.Empty, rig.Forms.Reason);
        rig.Graph.Choose(GraphSamples.EKey, null);
        Assert.False(rig.Forms.IsOpen);
        rig.Forms.Open(InspectorForm.Label);
        rig.Forms.Cancel();
        Assert.False(rig.Forms.IsOpen);
    }

    /// <summary>The same node read again keeps its open form and what was typed.</summary>
    [Fact]
    public async Task ReadingTheGraphAgainKeepsTheForm()
    {
        var rig = new Rig();
        rig.Graph.Choose(GraphSamples.UKey, null);
        rig.Forms.Open(InspectorForm.Label);
        rig.Forms.Reason = "Skäl";

        await rig.Graph.RefreshAsync();

        Assert.True(rig.Forms.IsLabelOpen);
        Assert.Equal("Skäl", rig.Forms.Reason);
    }

    /// <summary>A label needs a reason; it is stored through Attribution and the graph is read again.</summary>
    [Fact]
    public async Task LabelIsStoredAndTheGraphReadAgain()
    {
        var rig = new Rig();
        rig.Graph.Choose(GraphSamples.UKey, null);
        rig.Forms.Open(InspectorForm.Label);
        rig.Forms.LabelName = "  Säljare på P2P ";
        rig.Forms.ChooseKind(rig.Forms.Kinds.Single(kind => kind.Kind == LabelKind.OtcP2P));
        rig.Forms.DoNotSpread = true;
        Assert.True(rig.Forms.CanSubmit);
        Assert.Equal("Sätt etikett", rig.Forms.SubmitText);
        var loads = rig.Source.Loads.Count;

        rig.Forms.Reason = "Annons med samma adress";
        Assert.True(rig.Forms.CanSubmit);
        await rig.Forms.SubmitCommand.ExecuteAsync(null);

        var draft = Assert.Single(rig.Editor.Labels);
        Assert.Equal(LabelSubject.ForAddress(GraphSamples.U), draft.Subject);
        Assert.Equal(LabelKind.OtcP2P, draft.Kind);
        Assert.Equal("Säljare på P2P", draft.Name);
        Assert.Equal("Annons med samma adress", draft.Reason);
        Assert.True(draft.DoNotSpread);
        Assert.Equal(LabelSource.Own, draft.Source);
        Assert.False(rig.Forms.IsOpen);
        Assert.Equal(loads + 1, rig.Source.Loads.Count);
    }

    /// <summary>A label on a cluster is on the cluster; the victim is a label like any other (sketch GR5).</summary>
    [Fact]
    public async Task LabelOnAClusterIsOnTheCluster()
    {
        var rig = new Rig();
        rig.Graph.Choose(GraphSamples.KKey, null);
        rig.Forms.Open(InspectorForm.Label);
        Assert.True(rig.Forms.IsSpreadOptional);
        rig.Forms.ChooseKind(rig.Forms.Kinds[0]);
        Assert.False(rig.Forms.IsSpreadOptional);
        rig.Forms.Reason = "Offrets plånbok";

        await rig.Forms.SubmitCommand.ExecuteAsync(null);

        var draft = Assert.Single(rig.Editor.Labels);
        Assert.Equal(LabelSubject.ForCluster(GraphSamples.K1), draft.Subject);
        Assert.Equal(LabelKind.Victim, draft.Kind);
        Assert.Null(draft.Name);
    }

    /// <summary>
    /// The order and an exchange's answer are said in the reason; an open source must be named, and its version is
    /// the day it was read.
    /// </summary>
    [Fact]
    public void SourcesAreSaidInTheLabel()
    {
        var subject = LabelSubject.ForAddress(GraphSamples.U);
        var day = new DateOnly(2026, 11, 7);

        var order = InspectorForms.Draft(
            subject, LabelKind.Exchange, "", LabelBasis.Order, null, " Sida 2 ", false, day);
        var answer = InspectorForms.Draft(
            subject, LabelKind.Exchange, null, LabelBasis.ExchangeAnswer, null, "Svar", false, day);
        var open = InspectorForms.Draft(
            subject, LabelKind.Mixer, "Mixer X", LabelBasis.OpenSource, " Listan ", "Rad 4", false, day);

        Assert.Equal(InspectorForms.OrderPrefix + "Sida 2", order.Reason);
        Assert.Null(order.Name);
        Assert.Equal(LabelSource.Own, order.Source);
        Assert.Equal(InspectorForms.ExchangeAnswerPrefix + "Svar", answer.Reason);
        Assert.Equal(LabelSource.OpenSource, open.Source);
        Assert.Equal("Listan", open.SourceName);
        Assert.Equal("läst 2026-11-07", open.SourceVersion);
        Assert.Equal("Rad 4", open.Reason);
    }

    /// <summary>The form asks for the open source's name before it can be stored.</summary>
    [Fact]
    public void OpenSourceNeedsAName()
    {
        var rig = new Rig();
        rig.Graph.Choose(GraphSamples.UKey, null);
        rig.Forms.Open(InspectorForm.Label);
        rig.Forms.Reason = "Rad 4";
        rig.Forms.ChooseBasis(rig.Forms.Bases.Single(basis => basis.Basis == LabelBasis.OpenSource));

        Assert.True(rig.Forms.IsOpenSource);
        Assert.False(rig.Forms.CanSubmit);
        rig.Forms.SourceName = "Listan";
        Assert.True(rig.Forms.CanSubmit);
        Assert.Equal(["Egen bedömning", "Öppen källa", "Beställningen", "Svar från börs"],
            rig.Forms.Bases.Select(basis => basis.Name));
    }

    /// <summary>What the case refuses is said in the form, which stays open with what was typed.</summary>
    [Fact]
    public async Task RefusalIsSaidAndTheFormStays()
    {
        var rig = new Rig();
        rig.Editor.Failure = new CaseException(CaseError.NoOperatorProfile);
        rig.Graph.Choose(GraphSamples.UKey, null);
        rig.Forms.Open(InspectorForm.Label);
        rig.Forms.Reason = "Skäl";

        await rig.Forms.SubmitCommand.ExecuteAsync(null);

        Assert.True(rig.Forms.IsLabelOpen);
        Assert.Contains("operatörsprofilen", rig.Forms.Error, StringComparison.Ordinal);
        Assert.False(rig.Forms.IsSaving);
    }

    /// <summary>
    /// Breaking out reads the cluster's addresses with their heuristics, needs a pick that leaves someone and a
    /// reason, and stores one split per link through Clustering.
    /// </summary>
    [Fact]
    public async Task SplitBreaksThePickedAddressesOut()
    {
        var rig = new Rig();
        rig.Graph.Choose(GraphSamples.KKey, null);
        rig.Forms.Open(InspectorForm.Split);

        Assert.Equal(2, rig.Forms.Members.Count);
        Assert.Equal(string.Empty, rig.Forms.Members[0].Heuristic);
        Assert.Equal("CIO v1", rig.Forms.Members[1].Heuristic);
        Assert.Equal("0 av 2 ut", rig.Forms.PickedText);
        rig.Forms.ToggleMember(rig.Forms.Members[1]);
        Assert.Equal("Bryt ut 1", rig.Forms.SubmitText);
        Assert.True(rig.Forms.CanSubmit);
        rig.Forms.Reason = "Betaltjänst";
        Assert.True(rig.Forms.CanSubmit);
        rig.Forms.ToggleMember(rig.Forms.Members[0]);
        Assert.False(rig.Forms.CanSubmit);
        rig.Forms.ToggleMember(rig.Forms.Members[0]);

        await rig.Forms.SubmitCommand.ExecuteAsync(null);

        var split = Assert.Single(rig.Editor.Splits);
        Assert.Equal(
            new SplitRequest(GraphSamples.K2, GraphSamples.K1, HeuristicNames.CommonInputOwnership, "Betaltjänst"),
            split);
        Assert.False(rig.Forms.IsOpen);
    }

    /// <summary>A breaking out the case refuses stores nothing: the failure is said, and the graph is not read again.</summary>
    [Fact]
    public async Task ARefusedBreakingOutIsSaid()
    {
        var rig = new Rig();
        var a = GraphSamples.Address(20);
        rig.Editor.Cluster = new Cluster(
            GraphSamples.K1,
            [new ClusterMember(GraphSamples.K1, null, null), new ClusterMember(a, null, GraphSamples.K1)],
            [Merged(1, HeuristicNames.CommonInputOwnership, GraphSamples.K1, a),
                Merged(2, HeuristicNames.ChangeAddress, a, GraphSamples.K1)],
            [],
            [],
            []);
        rig.Editor.Failure = new CaseException(CaseError.Closed);
        rig.Graph.Choose(GraphSamples.KKey, null);
        rig.Forms.Open(InspectorForm.Split);
        rig.Forms.ToggleMember(rig.Forms.Members[1]);
        rig.Forms.Reason = "Skäl";
        var loads = rig.Source.Loads.Count;

        await rig.Forms.SubmitCommand.ExecuteAsync(null);

        Assert.StartsWith(InspectorFormsViewModel.SaveFailedText, rig.Forms.Error, StringComparison.Ordinal);
        Assert.True(rig.Forms.IsSplitOpen);
        Assert.Equal(loads, rig.Source.Loads.Count);
        Assert.Empty(rig.Editor.Splits);
    }

    /// <summary>A failure is said in fixed Swedish words, never with a raw message that may name a path.</summary>
    [Fact]
    public void AFailureNeverShowsTheRawMessage()
    {
        const string Raw = @"C:\Ärenden\hemligt.case";

        Assert.DoesNotContain(Raw, InspectorFormsViewModel.Failure(new IOException(Raw)), StringComparison.Ordinal);
        Assert.DoesNotContain(
            Raw, InspectorFormsViewModel.Failure(new ArgumentException(Raw)), StringComparison.Ordinal);
    }

    /// <summary>
    /// The cluster is read again before the splits, so a link that came after the rows were read is split too; and
    /// an address that another link still holds is said, never hidden.
    /// </summary>
    [Fact]
    public async Task SplitReadsTheClusterAgainAndChecksIt()
    {
        var rig = new Rig();
        rig.Graph.Choose(GraphSamples.KKey, null);
        rig.Forms.Open(InspectorForm.Split);
        rig.Forms.ToggleMember(rig.Forms.Members[1]);
        rig.Forms.Reason = "Skäl";
        var later = Merged(2, HeuristicNames.ChangeAddress, GraphSamples.K2, GraphSamples.K1);
        var sample = SampleCluster();
        rig.Editor.Cluster = sample with { Links = [.. sample.Links, later] };
        rig.Editor.SplitsHold = false;

        await rig.Forms.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(2, rig.Editor.Splits.Count);
        Assert.Contains(rig.Editor.Splits, split => split.Heuristic == HeuristicNames.ChangeAddress);
        Assert.StartsWith(
            "1 av de valda adresserna är fortfarande i klustret.", rig.Forms.Error, StringComparison.Ordinal);
        Assert.True(rig.Forms.IsSplitOpen);
    }

    /// <summary>
    /// When the graph read again after a split no longer has the chosen node, the form closes, and what the check found
    /// is said in the graph's notice instead of vanishing with it.
    /// </summary>
    [Fact]
    public async Task SplitWarningOutlivesItsForm()
    {
        var rig = new Rig();
        rig.Graph.Choose(GraphSamples.KKey, null);
        rig.Forms.Open(InspectorForm.Split);
        rig.Forms.ToggleMember(rig.Forms.Members[1]);
        rig.Forms.Reason = "Skäl";
        rig.Editor.SplitsHold = false;
        var graph = GraphSamples.Graph();
        rig.Source.Scene = () => GraphSamples.Scene(graph: graph with
        {
            Nodes = [.. graph.Nodes.Where(node => node.Key != GraphSamples.KKey)],
            Edges = [.. graph.Edges.Where(edge => edge.From != GraphSamples.KKey && edge.To != GraphSamples.KKey)],
        });

        await rig.Forms.SubmitCommand.ExecuteAsync(null);

        Assert.False(rig.Forms.IsOpen);
        Assert.Contains("fortfarande i klustret", rig.Graph.Notice, StringComparison.Ordinal);
    }

    /// <summary>A plain refusal the user has read in the form does not come back in the graph's notice.</summary>
    [Fact]
    public async Task ReadRefusalStaysWithItsForm()
    {
        var rig = new Rig();
        rig.Editor.Failure = new CaseException(CaseError.Closed);
        rig.Graph.Choose(GraphSamples.UKey, null);
        rig.Forms.Open(InspectorForm.Label);
        rig.Forms.Reason = "Skäl";
        await rig.Forms.SubmitCommand.ExecuteAsync(null);

        Assert.Contains("ärendet är stängt", rig.Forms.Error, StringComparison.Ordinal);
        rig.Graph.Choose(GraphSamples.EKey, null);

        Assert.DoesNotContain("ärendet är stängt", rig.Graph.Notice, StringComparison.Ordinal);
    }

    /// <summary>A refreshed graph reads the open split form's cluster again, keeping the picks still in it.</summary>
    [Fact]
    public async Task RefreshReadsTheMembersAgain()
    {
        var rig = new Rig();
        rig.Graph.Choose(GraphSamples.KKey, null);
        rig.Forms.Open(InspectorForm.Split);
        rig.Forms.ToggleMember(rig.Forms.Members[1]);
        var reads = rig.Editor.Reads;

        await rig.Graph.RefreshAsync();

        Assert.Equal(reads + 1, rig.Editor.Reads);
        Assert.Equal(2, rig.Forms.Members.Count);
        Assert.True(rig.Forms.Members[1].IsPicked);
        Assert.Equal("1 av 2 ut", rig.Forms.PickedText);
    }

    /// <summary>
    /// Breaking out splits every link that crosses between those who leave and those who stay, and keeps the links
    /// among those who leave, so that they leave together.
    /// </summary>
    [Fact]
    public void BreakOutSplitsOnlyTheCrossingLinks()
    {
        var a = GraphSamples.Address(20);
        var b = GraphSamples.Address(21);
        var c = GraphSamples.Address(22);
        var d = GraphSamples.Address(23);
        var cio = Merged(1, HeuristicNames.CommonInputOwnership, a, b, c);
        var change = Merged(2, HeuristicNames.ChangeAddress, c, d);
        var stopped = Merged(3, HeuristicNames.CommonInputOwnership, a, d) with { State = LinkState.Suggested };
        var cluster = new Cluster(a, [], [cio, change, cio], [stopped], [], []);

        var splits = InspectorForms.BreakOut(cluster, [c, d], " Skäl ");

        Assert.Equal(
            [new SplitRequest(c, a, HeuristicNames.CommonInputOwnership, "Skäl")],
            splits);
        Assert.Empty(InspectorForms.BreakOut(cluster, [], "Skäl"));
    }

    /// <summary>
    /// Two picked addresses that share one link with an address that stays each get a split, so that the history
    /// counts both and a later link from the second to the cluster is refused as well.
    /// </summary>
    [Fact]
    public void BreakOutSplitsEveryPickedAddressInALink()
    {
        var a = GraphSamples.Address(20);
        var b = GraphSamples.Address(21);
        var c = GraphSamples.Address(22);
        var cio = Merged(1, HeuristicNames.CommonInputOwnership, a, b, c);
        var cluster = new Cluster(a, [], [cio], [], [], []);

        var splits = InspectorForms.BreakOut(cluster, [a, b], "Skäl");

        Assert.Equal(
            [
                new SplitRequest(a, c, HeuristicNames.CommonInputOwnership, "Skäl"),
                new SplitRequest(b, c, HeuristicNames.CommonInputOwnership, "Skäl"),
            ],
            splits);
    }

    /// <summary>
    /// A merge of the user's own breaks out like a heuristic's: its split names the user's merges, and a user's link
    /// with the same sequence as a heuristic's (they are numbered in different chains) is not taken for it.
    /// </summary>
    [Fact]
    public void BreakOutSplitsAUsersMergeByItsOwnName()
    {
        var a = GraphSamples.Address(20);
        var b = GraphSamples.Address(21);
        var c = GraphSamples.Address(22);
        var cio = Merged(1, HeuristicNames.CommonInputOwnership, a, b);
        var user = Merged(1, UserMerges.Name, b, c);
        var cluster = new Cluster(a, [], [cio, user], [], [], []);

        var splits = InspectorForms.BreakOut(cluster, [c], string.Empty);

        Assert.Equal([new SplitRequest(c, b, UserMerges.Name, string.Empty)], splits);
        Assert.Equal("Egen v1", InspectorForms.ShortName(UserMerges.Id));
    }

    /// <summary>
    /// A link to another chain takes the only coin in, the chain recognised from the pasted transaction, and a reason;
    /// it is stored through Tracing.
    /// </summary>
    [Fact]
    public async Task CrossChainLinkIsStored()
    {
        var rig = new Rig();
        rig.Graph.Choose(GraphSamples.UKey, null);
        rig.Forms.Open(InspectorForm.CrossChain);

        Assert.False(rig.Forms.ShowsCoins);
        Assert.Equal("Ethereum-tx", rig.Forms.TxLabel);
        rig.Forms.TxText = Hash;
        Assert.False(rig.Forms.IsTxRecognised);
        Assert.Equal(Chain.Ethereum, rig.Forms.TargetChain);
        rig.Forms.ChooseChain(rig.Forms.Chains.Single(chain => chain.Name == "TRON"));
        Assert.True(rig.Forms.IsTxRecognised);
        Assert.Equal("TRON-tx", rig.Forms.TxLabel);
        rig.Forms.TxText = "0x" + Hash.ToUpperInvariant();
        Assert.Equal(Chain.Ethereum, rig.Forms.TargetChain);
        rig.Forms.TxText = "inte en tx";
        Assert.False(rig.Forms.IsTxRecognised);
        rig.Forms.TxText = "0x" + Hash;
        Assert.True(rig.Forms.CanSubmit);
        rig.Forms.Reason = "Samma belopp minus avgift";
        Assert.Equal("Knyt", rig.Forms.SubmitText);

        await rig.Forms.SubmitCommand.ExecuteAsync(null);

        var link = Assert.Single(rig.Editor.Links);
        Assert.Equal(new Outpoint(GraphSamples.Tx[1], 1), link.Coin);
        Assert.Equal(new ChainTransactionRef(Chain.Ethereum, "0x" + Hash), link.Target);
        Assert.Equal("Samma belopp minus avgift", link.Reason);
        Assert.False(rig.Forms.IsOpen);
    }

    /// <summary>A chain that is not built is shown toned and cannot be chosen.</summary>
    [Fact]
    public void ChainsNotBuiltCannotBeChosen()
    {
        var rig = new Rig();
        rig.Graph.Choose(GraphSamples.UKey, null);
        rig.Forms.Open(InspectorForm.CrossChain);
        var litecoin = rig.Forms.Chains.Single(chain => chain.Name == "Litecoin");

        rig.Forms.ChooseChain(litecoin);

        Assert.False(litecoin.IsBuilt);
        Assert.Contains(InspectorFormsViewModel.NotBuiltText, litecoin.ToolTip, StringComparison.Ordinal);
        Assert.Equal(Chain.Ethereum, rig.Forms.TargetChain);
        rig.Forms.ChooseChain(rig.Forms.Chains.Single(chain => chain.Name == "TRON"));
        Assert.Equal(Chain.Tron, rig.Forms.TargetChain);
    }

    /// <summary>
    /// With several coins in, the user chooses which went on, the largest first; a coin already linked is not offered.
    /// </summary>
    [Fact]
    public void SeveralCoinsAreChosenFrom()
    {
        var graph = GraphSamples.Graph();
        var second = GraphSamples.Edge("x-k", GraphSamples.UKey, GraphSamples.KKey, 3, 1, 0.7m);
        var link = new CrossChainLink(
            1,
            new Outpoint(GraphSamples.Tx[3], 0),
            new ChainTransactionRef(Chain.Tron, Hash),
            "Skäl",
            1,
            DateTimeOffset.UnixEpoch);
        var scene = GraphSamples.Scene(graph: graph with { Edges = [.. graph.Edges, second], CrossChainLinks = [link] });
        var cluster = scene.Node(GraphSamples.KKey)!;

        var coins = InspectorForms.IncomingCoins(scene, cluster);

        Assert.Equal(
            [new Outpoint(GraphSamples.Tx[3], 1), new Outpoint(GraphSamples.Tx[1], 0)],
            coins.Select(coin => coin.Coin));
        Assert.Empty(InspectorForms.IncomingCoins(scene, scene.Node(GraphSamples.WKey)!));
    }

    /// <summary>The sorts are offered with the victim and the unknown first, then in the legend's order.</summary>
    [Fact]
    public void SortsFollowTheLegend()
    {
        var kinds = InspectorForms.Kinds.Select(option => option.Kind).ToList();

        Assert.Equal(LabelKind.Victim, kinds[0]);
        Assert.Equal(LabelKind.Unknown, kinds[1]);
        Assert.Equal(LabelKind.Exchange, kinds[2]);
        Assert.Equal(Enum.GetValues<LabelKind>().Length - 2, kinds.Distinct().Count());
        Assert.DoesNotContain(LabelKind.Sanctioned, kinds);
        Assert.DoesNotContain(LabelKind.Reported, kinds);
        Assert.Equal("CIO v1", InspectorForms.ShortName(new HeuristicId(HeuristicNames.CommonInputOwnership, 1)));
        Assert.Equal("Växel v2", InspectorForms.ShortName(new HeuristicId(HeuristicNames.ChangeAddress, 2)));
    }

    /// <summary>The graph with its forms, a case open and the sample graph read.</summary>
    private sealed class Rig
    {
        public Rig()
        {
            Workflow = new CaseWorkflow(
                new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), Session);
            Graph = new GraphViewModel(
                Session, Workflow, Source, TestWays.For(new TestExitConnection()), new InlineUi(), Editor,
                new TestClock());
            Session.Open(new TestCase("2026-0001"));
        }

        public CaseSession Session { get; } = new();

        public CaseWorkflow Workflow { get; }

        public TestGraphSource Source { get; } = new();

        public TestGraphEditor Editor { get; } = new();

        public GraphViewModel Graph { get; }

        public InspectorFormsViewModel Forms => Graph.Forms;
    }
}
