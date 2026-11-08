using Chainsaw.App.Graph;
using Chainsaw.App.Navigation;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// An analysis that counts only the direct lines, as a stand-in for Tracing's: what goes straight to a service is
/// reached, the rest is not. Records what it was asked.
/// </summary>
internal sealed class DirectExposure : IExposureAnalysis
{
    public List<ExposureRequest> Asked { get; } = [];

    public ExposureResult Analyse(ExposureRequest request)
    {
        Asked.Add(request);
        var outwards = request.Direction == ExposureDirection.Out;
        var lines = request.Graph.Edges
            .Where(edge => edge.Kind == GraphEdgeKind.Transfer && edge.Amount.Asset == request.Asset)
            .Where(edge => (outwards ? edge.From : edge.To) == request.Subject)
            .ToList();
        var total = lines.Aggregate(AssetAmount.Zero(request.Asset), (sum, edge) => sum + edge.Amount);
        var reached = new List<ExposureReach>();
        var rest = AssetAmount.Zero(request.Asset);
        foreach (var edge in lines)
        {
            var far = outwards ? edge.To : edge.From;
            if (request.Services.Contains(far))
            {
                var tx = edge.Transfers.Select(t => t.Transaction).ToList();
                reached.Add(new ExposureReach(far, edge.Amount, 1, [request.Subject, far], [edge.Key], tx));
            }
            else
            {
                rest += edge.Amount;
            }
        }

        var flows = lines.Select(edge => new ExposureFlow(
            edge.Key, request.Subject, outwards ? edge.To : edge.From, edge.Amount)).ToList();
        var hops = lines.Select(edge => outwards ? edge.To : edge.From).Distinct()
            .ToDictionary(key => key, _ => 1);
        hops[request.Subject] = 0;
        return new ExposureResult(
            request.Subject, request.Direction, request.Depth, new TracingMethodId(request.Method, 1), total,
            [.. reached.OrderByDescending(reach => reach.Amount.Units)],
            rest.Units.Sign > 0
                ? new Dictionary<ExposureRest, AssetAmount> { [ExposureRest.NotReached] = rest }
                : new Dictionary<ExposureRest, AssetAmount>(),
            flows,
            hops);
    }
}

/// <summary>An analysis that refuses, as Tracing's does when money would be created or lost.</summary>
internal sealed class FailingExposure : IExposureAnalysis
{
    public ExposureResult Analyse(ExposureRequest request) => throw new InvalidOperationException("Fel i analysen.");
}

/// <summary>The view Exponering (sketch EP1b): the map of the graph, the rings' layout and the view model.</summary>
public sealed class ExposureTests
{
    /// <summary>K is an exchange's cluster? No: E is the exchange, U a mixer, W the victim.</summary>
    private static GraphScene Labelled() => Scene(new Dictionary<string, IReadOnlyList<Label>>
    {
        [EKey] = [Label(E, LabelKind.Exchange, "Börs A")],
        [UKey] = [Label(U, LabelKind.Sanctioned, "Växlare Z")],
        [WKey] = [Label(W, LabelKind.Victim, "Offer 1")],
    });

    [Fact]
    public void The_view_has_its_place_in_the_pill_and_is_built()
    {
        var entry = ViewTable.For(AppView.Exposure);

        Assert.True(entry.IsBuilt);
        Assert.True(entry.NeedsCase);
        Assert.Equal("Ctrl+4", entry.Gesture);
    }

    [Fact]
    public void Every_sort_with_a_meaning_is_a_service_and_the_greys_are_not()
    {
        var services = ExposureMap.Services(Labelled());

        Assert.Equal([EKey, UKey, WKey], services.Order(StringComparer.Ordinal));
        Assert.Equal(ExposureGroup.CashOut, ExposureMap.GroupOf(VisualLanguage.Of(NodeSort.Exchange)));
        Assert.Equal(ExposureGroup.Risk, ExposureMap.GroupOf(VisualLanguage.Of(NodeSort.Sanctioned)));
        Assert.Equal(ExposureGroup.Victim, ExposureMap.GroupOf(VisualLanguage.Of(NodeSort.Victim)));
        Assert.Equal(ExposureGroup.OtherChain, ExposureMap.GroupOf(VisualLanguage.Of(NodeSort.SwapBridge)));
        Assert.Equal(ExposureGroup.Unknown, ExposureMap.GroupOf(VisualLanguage.Of(NodeSort.Cluster)));
    }

    [Fact]
    public void The_subjects_are_the_actors_that_moved_money_the_start_point_first()
    {
        var subjects = ExposureMap.Subjects(Scene());

        Assert.Equal(VKey, subjects[0].Key);
        Assert.Contains(subjects, subject => subject.Key == KKey);
        Assert.All(subjects, subject => Assert.Equal([Asset.Bitcoin], subject.Assets));
    }

    [Fact]
    public void The_request_uses_the_graphs_method_and_never_counts_the_subject_as_a_service()
    {
        var request = ExposureMap.Request(Labelled(), EKey, Asset.Bitcoin, ExposureDirection.Out, 2);

        Assert.Equal(TracingMethodKind.Fifo, request.Method);
        Assert.DoesNotContain(EKey, request.Services);
        Assert.Equal(2, request.Depth);
    }

    [Fact]
    public async Task Paid_out_stands_to_the_right_and_got_to_the_left_on_the_rings()
    {
        var model = await Opened(Labelled(), VKey);

        var picture = model.Picture;
        var paid = Assert.Single(picture.Services, place => place.Side == ExposureDirection.Out);
        var got = Assert.Single(picture.Services, place => place.Side == ExposureDirection.In);

        Assert.Equal(UKey, paid.Key);
        Assert.True(paid.At.X > ExposureLayout.Centre.X);
        Assert.Equal(WKey, got.Key);
        Assert.True(got.At.X < ExposureLayout.Centre.X);
        Assert.Equal(ExposureLayout.Rings[1], Distance(paid.At), 6);
        Assert.Contains(picture.Via, place => place.Key == KKey);
    }

    [Fact]
    public async Task A_larger_share_is_a_larger_node_and_a_thicker_line()
    {
        var model = await Opened(Labelled(), KKey);

        var picture = model.Picture;
        var exchange = Assert.Single(
            picture.Services, place => place.Key == EKey && place.Side == ExposureDirection.Out);

        Assert.True(exchange.Size > ExposureLayout.ViaSize);
        var line = Assert.Single(picture.Lines, line => line.EdgeKey == KE);
        Assert.Equal(Asset.Bitcoin, model.Subject!.Assets[0]);
        Assert.True(line.Width > 1.5);
        Assert.Equal(GraphInk.CashOut, line.Ink);
    }

    [Fact]
    public async Task A_click_on_a_service_or_its_name_finds_it()
    {
        var model = await Opened(Labelled(), VKey);
        var paid = model.Picture.Services.Single(place => place.Side == ExposureDirection.Out);

        Assert.Equal(paid, model.Picture.At(paid.At));
        Assert.Equal(paid, model.Picture.At(new GraphPoint(paid.At.X + (paid.Size / 2) + 60, paid.At.Y)));
        Assert.Null(model.Picture.At(ExposureLayout.Centre));
    }

    [Fact]
    public async Task The_sides_show_the_shares_per_colour_with_the_rest_as_unknown()
    {
        var model = await Opened(Labelled(), VKey);

        var paid = model.Sides[0];
        Assert.Equal("BETALAT TILL", paid.Title);
        Assert.Equal("0,9900", paid.Total);
        Assert.Equal([ExposureGroup.Risk, ExposureGroup.Unknown], paid.Rows.Select(row => row.Group));
        Assert.Equal("39,4 %", paid.Rows[0].Percent);
        Assert.Equal("60,6 %", paid.Rows[1].Percent);
        Assert.Contains("Har inte nått en tjänst", paid.Rows[1].ToolTip, StringComparison.Ordinal);
        Assert.Equal("FÅTT FRÅN", model.Sides[1].Title);
        Assert.Equal(ExposureGroup.Victim, Assert.Single(model.Sides[1].Rows).Group);
    }

    [Fact]
    public async Task The_largest_service_paid_to_is_chosen_with_its_way_and_what_it_rests_on()
    {
        var model = await Opened(Labelled(), VKey);

        var detail = model.Detail!;
        Assert.Equal("Växlare Z", detail.Name);
        Assert.Equal("av det som gått ut", detail.Direction);
        Assert.Equal(2, detail.Crumbs.Count);
        Assert.True(detail.Crumbs[0].HasNext);
        Assert.StartsWith("tx ", detail.Transactions, StringComparison.Ordinal);
        Assert.Contains(detail.Basis, basis => basis.Text == "FIFO v1");
        Assert.Contains(detail.Basis, basis => basis.Text == ExposureResult.Analysis);
        Assert.Contains(detail.Basis, basis => basis.Text == "Växlare Z" && basis.Icon == "Icon.Kind.Ban");
        Assert.Equal(ExposureLayout.IdOf(ExposureDirection.Out, UKey), model.SelectedId);
        Assert.Contains(VU, model.Lit.Lines);
    }

    [Fact]
    public async Task Choosing_a_service_on_the_left_reads_it_as_got()
    {
        var model = await Opened(Labelled(), VKey);

        model.Pick(ExposureLayout.IdOf(ExposureDirection.In, WKey));

        Assert.Equal("Offer 1", model.Detail!.Name);
        Assert.Equal("av det som kommit in", model.Detail.Direction);
        Assert.Equal(["Offer 1", Labelled().Node(VKey)!.Name], model.Detail.Crumbs.Select(c => c.Text));
    }

    [Fact]
    public async Task An_actor_on_the_way_is_not_chosen()
    {
        var model = await Opened(Labelled(), VKey);
        var before = model.SelectedId;

        model.Pick(ExposureLayout.IdOf(ExposureDirection.Out, KKey));

        Assert.Equal(before, model.SelectedId);
    }

    [Fact]
    public async Task The_depth_is_asked_of_the_analysis_and_kept()
    {
        var (model, analysis) = await Shown(Labelled());
        model.Open(VKey);
        await model.Computed;

        model.SetDepth("1");
        await model.Computed;

        Assert.Equal(1, model.Depth);
        Assert.All(analysis.Asked.TakeLast(2), request => Assert.Equal(1, request.Depth));
        Assert.Equal([false, true], analysis.Asked.TakeLast(2).Select(r => r.Direction == ExposureDirection.In));
        Assert.True(model.Depths[0].IsSelected);
        model.SetDepth("9");
        Assert.Equal(1, model.Depth);
    }

    [Fact]
    public async Task The_subject_stays_when_the_graph_is_read_anew()
    {
        var model = await Opened(Labelled(), KKey);

        model.Update(Labelled());
        await model.Computed;

        Assert.Equal(KKey, model.Subject!.Key);
        Assert.True(model.Subjects.Single(row => row.Subject.Key == KKey).IsSelected);
    }

    [Fact]
    public async Task Without_a_graph_the_view_says_so_honestly()
    {
        var model = new ExposureViewModel(new DirectExposure(), new InlineUi()) { IsShown = true };

        model.Update(null);
        await model.Computed;

        Assert.False(model.HasSubject);
        Assert.False(model.HasDetail);
        Assert.False(model.Picture.HasSubject);
        Assert.False(model.ShowPathCommand.CanExecute(null));
    }

    [Fact]
    public async Task Show_the_way_hands_the_graph_the_actors_and_lines_with_a_note()
    {
        var model = await Opened(Labelled(), VKey);
        ExposurePath? wanted = null;
        model.PathWanted += (_, path) => wanted = path;

        model.ShowPathCommand.Execute(null);

        Assert.NotNull(wanted);
        Assert.Equal([VKey, UKey], wanted.Nodes);
        Assert.Equal([VU], wanted.Edges);
        Assert.Contains("→ Växlare Z", wanted.Note, StringComparison.Ordinal);
        Assert.Contains("FIFO v1", wanted.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Why_opens_the_guide_on_exposure_with_the_explanation()
    {
        var model = await Opened(Labelled(), VKey);
        Guide.GuideRequest? asked = null;
        model.GuideWanted += (_, request) => asked = request;

        model.AskWhyCommand.Execute(null);

        Assert.NotNull(asked);
        Assert.Equal(GuideTopic.Exposure, asked.Topic);
        Assert.Equal("exponering", GuideTopic.Exposure.FileName());
        Assert.Contains(asked.Context!.Lines, line => line.Topic == GuideTopic.Methods);
    }

    [Fact]
    public async Task The_graph_lights_the_way_until_something_else_is_chosen()
    {
        var session = new CaseSession();
        var workflow = new CaseWorkflow(
            new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), session);
        var graph = new GraphViewModel(
            session, workflow, new TestGraphSource(), TestWays.For(new TestExitConnection()), new InlineUi(),
            new TestGraphEditor(), new TestClock());
        session.Open(new TestCase("2026-0077"));
        await graph.ReloadAsync(fit: false);
        var path = new ExposurePath([VKey, KKey], [VK], "V → K");

        graph.ShowPath(path);

        Assert.True(graph.IsFocused);
        Assert.Contains(VK, graph.Near.Edges);
        graph.Choose(null, null);
        Assert.Null(graph.PathFocus);
    }

    [Fact]
    public async Task The_kronor_stand_after_the_amount_when_the_lines_have_a_value()
    {
        var value = new EdgeValue(23_400m, "SEK", []);
        var valued = GraphScene.Build(Labelled().Snapshot with
        {
            Values = new Dictionary<string, EdgeValue> { [VU] = value },
        });

        var model = await Opened(valued, VKey);
        var without = await Opened(Labelled(), VKey);

        Assert.EndsWith(" · " + value.Text, model.Detail!.Amount, StringComparison.Ordinal);
        Assert.DoesNotContain(" kr", without.Detail!.Amount, StringComparison.Ordinal);
        var risk = model.Sides[0].Rows.Single(row => row.Group == ExposureGroup.Risk);
        Assert.EndsWith(value.Text, risk.ToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void An_approved_way_out_of_a_mixer_stands_dashed_on_the_next_ring_as_a_suggestion_and_changes_no_number()
    {
        var scene = Labelled();
        var line = scene.Snapshot.Graph.Edges.Single(edge => edge.Key == VU);
        var paid = new ExposureResult(
            VKey, ExposureDirection.Out, 1, new TracingMethodId(TracingMethodKind.Fifo, 1), line.Amount,
            [new ExposureReach(UKey, line.Amount, 1, [VKey, UKey], [VU], [])],
            new Dictionary<ExposureRest, AssetAmount>(),
            [new ExposureFlow(VU, VKey, UKey, line.Amount)],
            new Dictionary<string, int> { [VKey] = 0, [UKey] = 1 });
        var got = paid with { Direction = ExposureDirection.In, Reached = [], Flows = [] };
        var subject = scene.Node(VKey)!;
        var names = ExposureNames.Of(scene);

        var plain = ExposureLayout.Build(subject, paid, got, names);
        var picture = ExposureLayout.Build(
            subject, paid, got, names, [new ApprovedWayOut(UKey, KKey), new ApprovedWayOut(UKey, VKey)]);

        var way = Assert.Single(picture.Candidates);
        Assert.Equal((KKey, true, ExposureLayout.CandidateWord), (way.Key, way.IsCandidate, way.Line));
        var radius = way.At.DistanceTo(ExposureLayout.Centre);
        Assert.Equal(ExposureLayout.Rings[2], radius, 6);
        var dotted = Assert.Single(picture.Lines, ring => ring.IsDotted);
        Assert.Equal(way.At, dotted.To);
        Assert.Equal(plain.Services, picture.Services);
        Assert.Empty(plain.Candidates);
        Assert.DoesNotContain(picture.Services.Concat(picture.Via), place => place.IsCandidate);
    }

    [Fact]
    public void A_way_out_of_a_mixer_on_the_last_ring_stands_beyond_it_inside_the_picture()
    {
        var scene = Labelled();
        var line = scene.Snapshot.Graph.Edges.Single(edge => edge.Key == VU);
        var paid = new ExposureResult(
            VKey, ExposureDirection.Out, 3, new TracingMethodId(TracingMethodKind.Fifo, 1), line.Amount,
            [new ExposureReach(UKey, line.Amount, 3, [VKey, UKey], [VU], [])],
            new Dictionary<ExposureRest, AssetAmount>(),
            [new ExposureFlow(VU, VKey, UKey, line.Amount)],
            new Dictionary<string, int> { [VKey] = 0, [UKey] = 3 });
        var got = paid with { Direction = ExposureDirection.In, Reached = [], Flows = [] };

        var picture = ExposureLayout.Build(
            scene.Node(VKey)!, paid, got, ExposureNames.Of(scene), [new ApprovedWayOut(UKey, KKey)]);

        var way = Assert.Single(picture.Candidates);
        Assert.Equal(ExposureLayout.Beyond, way.At.DistanceTo(ExposureLayout.Centre), 6);
        Assert.InRange(way.At.X + (way.Size / 2), 0, ExposureLayout.Width);
        Assert.InRange(way.At.Y + (way.Size / 2), 0, ExposureLayout.Height);
    }

    [Fact]
    public void The_money_is_valued_in_proportion_to_its_part_of_the_line_and_not_at_all_without_a_value()
    {
        var scene = Labelled();
        var line = scene.Snapshot.Graph.Edges.Single(edge => edge.Key == VU);
        var half = new AssetAmount(line.Amount.Asset, line.Amount.Units / 2);
        var result = new ExposureResult(
            VKey, ExposureDirection.Out, 1, new TracingMethodId(TracingMethodKind.Fifo, 1), line.Amount,
            [new ExposureReach(UKey, half, 1, [VKey, UKey], [VU], [])],
            new Dictionary<ExposureRest, AssetAmount>(),
            [new ExposureFlow(VU, VKey, UKey, half)],
            new Dictionary<string, int> { [VKey] = 0, [UKey] = 1 });
        var valued = scene.Snapshot with
        {
            Values = new Dictionary<string, EdgeValue> { [VU] = new(1_000m, "SEK", []) },
        };

        Assert.Equal(new EdgeValue(500m, "SEK", []).Text, ExposureValues.OfServices(valued, result, [UKey]));
        Assert.Equal(string.Empty, ExposureValues.OfServices(scene.Snapshot, result, [UKey]));
        Assert.Equal("0,1 BTC", ExposureValues.After("0,1 BTC", string.Empty));
    }

    [Fact]
    public async Task The_node_menu_opens_the_view_with_the_node_in_the_centre_and_the_graph_has_a_way_back()
    {
        var graph = await Graph();
        var wanted = new List<string>();
        var back = new List<AppView>();
        graph.ExposureWanted += (_, key) => wanted.Add(key);
        graph.ReturnWanted += (_, view) => back.Add(view);

        graph.OpenMenu(VKey);
        var row = Assert.Single(graph.MenuRows, row => row.Action == NodeMenuAction.Exposure);
        graph.RunMenu(row);
        graph.ShowPath(new ExposurePath([VKey, KKey], [VK], "V → K"));

        Assert.Equal([VKey], wanted);
        Assert.Equal("Exponering", graph.ReturnText);
        Assert.False(graph.ReturnsOnEscape);
        graph.Choose(null, null);
        Assert.True(graph.ReturnsOnEscape);
        graph.Return();
        Assert.Equal([AppView.Exposure], back);
        Assert.False(graph.HasReturn);
        graph.ShowPath(new ExposurePath([VKey, KKey], [VK], "V → K"));
        graph.ForgetReturn();
        Assert.Null(graph.PathFocus);
        Assert.False(graph.HasReturn);
    }

    [Fact]
    public async Task Nothing_is_read_while_the_view_is_hidden_and_a_stale_view_is_read_when_shown()
    {
        var analysis = new DirectExposure();
        var model = new ExposureViewModel(analysis, new InlineUi());

        model.Update(Labelled());
        model.Update(Labelled());

        Assert.Empty(analysis.Asked);
        model.IsShown = true;
        await model.Computed;
        Assert.Equal(2, analysis.Asked.Count);
        Assert.True(model.Picture.HasSubject);
    }

    [Fact]
    public void A_service_in_the_trash_is_still_a_service()
    {
        var scene = Labelled();
        var trashed = scene with { Nodes = [.. scene.Nodes.Where(node => node.Key != UKey)] };

        Assert.Contains(UKey, ExposureMap.Services(trashed));
        Assert.Equal(NodeSort.Sanctioned, ExposureMap.Looks(trashed)[UKey].Sort);
    }

    [Fact]
    public async Task An_analysis_that_fails_is_said_honestly()
    {
        var model = new ExposureViewModel(new FailingExposure(), new InlineUi()) { IsShown = true };

        model.Update(Labelled());
        await model.Computed;

        Assert.True(model.HasProblem);
        Assert.Equal(ExposureViewModel.FailedText, model.Problem);
        Assert.False(model.Picture.HasSubject);
    }

    [Fact]
    public void The_unknown_rest_is_said_with_its_reason()
    {
        Assert.Equal("Nådde ingen tjänst inom 3 hopp", ExposureViewModel.RestText(ExposureRest.NotReached, 3, "klustret"));
        Assert.Equal("Kom före historiken i grafen", ExposureViewModel.RestText(ExposureRest.BeforeHistory, 2, "klustret"));
        Assert.Equal("Aktören är inte expanderad", ExposureViewModel.RestText(ExposureRest.NotExpanded, 2, "adressen"));
        Assert.Equal("Gick till avgifter", ExposureViewModel.RestText(ExposureRest.Fees, 2, "adressen"));
        Assert.Equal("Gick tillbaka till adressen", ExposureViewModel.RestText(ExposureRest.Returned, 2, "adressen"));
    }

    private static async Task<GraphViewModel> Graph()
    {
        var session = new CaseSession();
        var workflow = new CaseWorkflow(
            new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), session);
        var graph = new GraphViewModel(
            session, workflow, new TestGraphSource(), TestWays.For(new TestExitConnection()), new InlineUi(),
            new TestGraphEditor(), new TestClock());
        session.Open(new TestCase("2026-0077"));
        await graph.ReloadAsync(fit: false);
        return graph;
    }

    private static async Task<(ExposureViewModel Model, DirectExposure Analysis)> Shown(GraphScene scene)
    {
        var analysis = new DirectExposure();
        var model = new ExposureViewModel(analysis, new InlineUi()) { IsShown = true };
        model.Update(scene);
        await model.Computed;
        return (model, analysis);
    }

    private static async Task<ExposureViewModel> Opened(GraphScene scene, string subject)
    {
        var (model, _) = await Shown(scene);
        model.Open(subject);
        await model.Computed;
        return model;
    }

    private static double Distance(GraphPoint point) => Math.Sqrt(
        Math.Pow(point.X - ExposureLayout.Centre.X, 2) + Math.Pow(point.Y - ExposureLayout.Centre.Y, 2));
}
