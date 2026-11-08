using Chainsaw.App.Graph;
using Chainsaw.App.Navigation;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Requests.Contracts;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>The reports as the Requests module would answer, in memory; records what is composed and exported.</summary>
internal sealed class TestReports : IReportService
{
    public static readonly DateTimeOffset Made = new(2026, 11, 8, 13, 40, 0, TimeSpan.Zero);

    public List<ReportDraft> Composed { get; } = [];

    public List<(ReportDraft Draft, string Path)> Exported { get; } = [];

    public List<ReportExport> Exports { get; } = [];

    public int Checks { get; private set; }

    public Exception? Failure { get; set; }

    public ReportLedger Check(ICase @case)
    {
        Checks++;
        return new ReportLedger(7, new string('e', 64), Made, true);
    }

    public RequestDocument Compose(ICase @case, ReportDraft draft)
    {
        lock (Composed)
        {
            Composed.Add(draft);
        }

        var text = "# Rapport\n\n## Spåret\n\n| Steg |\n\n*not*";
        return new RequestDocument("Rapport", [new RequestBlock(RequestBlockKind.Title, "Rapport")], text,
            $"{@case.Info.CaseNumber} underrättelse.md")
        {
            MadeUtc = Made,
        };
    }

    public ReportExport Export(ICase @case, ReportDraft draft, string path)
    {
        if (Failure is { } failure)
        {
            throw failure;
        }

        Exported.Add((draft, path));
        var export = new ReportExport(
            Exports.Count + 1, Path.GetFileName(path), path, new string('c', 64), draft.Transactions,
            [.. draft.Parts], Made, 9);
        Exports.Add(export);
        return export;
    }

    public IReadOnlyList<ReportExport> ListExports(ICase @case) => [.. Exports];
}

public sealed class ReportSelectionTests
{
    private const string VictimName = "Anna Offersson";

    private static readonly Dictionary<string, Cluster> NoClusters = [];

    private static GraphScene LabelledScene() => Scene(new Dictionary<string, IReadOnlyList<Label>>
    {
        [EKey] = [Label(E, LabelKind.Exchange, "Börs A")],
        [VKey] = [Label(V, LabelKind.Victim, VictimName)],
    });

    [Fact]
    public void Nothing_marked_gives_nothing()
    {
        var material = ReportSelection.Build(LabelledScene(), [], NoClusters);

        Assert.Same(ReportMaterial.Empty, material);
    }

    [Fact]
    public void The_trail_runs_along_traced_money_between_the_marked_actors()
    {
        var material = ReportSelection.Build(LabelledScene(), [VKey, KKey, EKey], NoClusters);

        var trail = Assert.Single(material.Trails);
        Assert.Equal("Börs A", trail.To);
        Assert.Equal(ReportTrailEnd.CashOut, trail.End);
        Assert.Equal(Btc(0.5m), trail.Traced);
        Assert.Equal([Tx[1].Value, Tx[2].Value], trail.Steps.Select(step => step.Transaction));
        var last = trail.Steps[^1];
        Assert.Equal(102, last.BlockHeight);
        Assert.Equal(Block(102).Hash.Value, last.BlockHash);
        Assert.Equal(Block(102).Time, last.Time);
        Assert.Equal(Btc(0.5m), last.Amount);
        Assert.StartsWith("Kluster ", last.From, StringComparison.Ordinal);
        Assert.Equal(1, material.EndCount);
        Assert.Equal(2, material.TransactionCount);
    }

    [Fact]
    public void A_victims_name_never_reaches_the_report()
    {
        var material = ReportSelection.Build(LabelledScene(), [VKey, KKey, EKey], NoClusters);

        var texts = material.Actors.Select(actor => actor.Name)
            .Concat(material.Trails.SelectMany(trail => trail.Steps.SelectMany(step => new[] { step.From, step.To })))
            .Concat(material.Trails.Select(trail => trail.From))
            .Concat(material.Conclusions.SelectMany(c => new[] { c.Statement, c.Why }))
            .Concat(material.Labels.SelectMany(label => new[] { label.Text, label.Source }));
        Assert.DoesNotContain(texts, text => text.Contains(VictimName, StringComparison.Ordinal));
        Assert.DoesNotContain(material.Labels, label => label.Text.StartsWith("Offer", StringComparison.Ordinal));
        Assert.Equal(GraphScene.Shorten(V.Value), material.Trails[0].From);
    }

    [Fact]
    public void A_labels_reason_note_and_setter_never_come_along()
    {
        var material = ReportSelection.Build(LabelledScene(), [EKey], NoClusters);

        var label = Assert.Single(material.Labels);
        Assert.Equal("Börs · Börs A", label.Text);
        Assert.Equal("egen etikett 2026-11-07", label.Source);
        var conclusion = Assert.Single(material.Conclusions);
        Assert.Equal($"{GraphScene.Shorten(E.Value)} är börs (Börs A).", conclusion.Statement);
        Assert.DoesNotContain("Sett", conclusion.Why, StringComparison.Ordinal);
        Assert.DoesNotContain("Utredare", conclusion.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trail_ends_open_at_the_last_actor_marked()
    {
        var material = ReportSelection.Build(LabelledScene(), [VKey, KKey], NoClusters);

        var trail = Assert.Single(material.Trails);
        Assert.Equal(ReportTrailEnd.Open, trail.End);
        Assert.Equal(0, material.EndCount);
    }

    [Fact]
    public void An_actor_not_marked_breaks_the_trail()
    {
        var material = ReportSelection.Build(LabelledScene(), [VKey, EKey], NoClusters);

        Assert.Empty(material.Trails);
        Assert.Equal(2, material.Actors.Count);
    }

    [Fact]
    public void The_traced_amounts_are_said_to_rest_on_the_method()
    {
        var material = ReportSelection.Build(LabelledScene(), [VKey, KKey, EKey], NoClusters);

        Assert.Contains(material.Conclusions, c => c.Statement.Contains("FIFO", StringComparison.Ordinal));
        Assert.Equal(LabelledScene().MethodText, material.Method);
    }

    [Fact]
    public void A_cluster_says_its_heuristics_versions_and_transactions()
    {
        var evidence = new ClusterEvidence(
            1, new HeuristicId("common-input", 2), Tx[3], [K1, K2], EvidenceStrength.Merge, "samma tx", null, null, 1,
            DateTimeOffset.UnixEpoch);
        var link = new ClusterLink(evidence, LinkState.Merged);
        var cluster = new Cluster(K1, [new ClusterMember(K1, null, null), new ClusterMember(K2, link, K1)], [link], [],
            [], []);

        var material = ReportSelection.Build(LabelledScene(), [KKey], new Dictionary<string, Cluster>
        {
            [KKey] = cluster,
        });

        var conclusion = Assert.Single(material.Conclusions);
        Assert.Contains("2 adresser har enligt klustringen samma ägare", conclusion.Statement, StringComparison.Ordinal);
        Assert.Contains("common-input v2", conclusion.Why, StringComparison.Ordinal);
        Assert.Contains(GraphScene.Shorten(Tx[3].Value), conclusion.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reports_name_is_never_written()
    {
        var scene = Scene(new Dictionary<string, IReadOnlyList<Label>>
        {
            [EKey] = [Label(E, LabelKind.Reported, "Kalle Bedragare", LabelSource.Provider)],
        });

        var material = ReportSelection.Build(scene, [EKey], NoClusters);

        Assert.DoesNotContain(material.Conclusions, c => c.Statement.Contains("Kalle", StringComparison.Ordinal));
        Assert.DoesNotContain(material.Labels, l => l.Text.Contains("Kalle", StringComparison.Ordinal));
        Assert.DoesNotContain(material.Actors, a => a.Name.Contains("Kalle", StringComparison.Ordinal));
    }

    [Fact]
    public void Clusters_wanted_are_the_marked_cluster_nodes()
    {
        var wanted = ReportSelection.ClustersWanted(LabelledScene(), [VKey, KKey]);

        Assert.Equal([(KKey, K1)], wanted);
    }

    [Fact]
    public void A_sanctioned_end_is_said_so()
    {
        var scene = Scene(new Dictionary<string, IReadOnlyList<Label>>
        {
            [EKey] = [Label(E, LabelKind.Sanctioned, "Växlare Z", LabelSource.SanctionList)],
        });

        var trail = Assert.Single(ReportSelection.Build(scene, [KKey, EKey], NoClusters).Trails);

        Assert.Equal(ReportTrailEnd.Sanctioned, trail.End);
    }

    [Fact]
    public void The_picture_lights_the_marked_and_frames_them()
    {
        var places = new Dictionary<string, GraphPoint>
        {
            [VKey] = new(0, 0), [KKey] = new(300, 0), [EKey] = new(600, 0), [UKey] = new(300, 200),
        };

        var picture = ReportPicture.Of(LabelledScene(), [VKey, KKey], places);

        Assert.Equal(4, picture.Nodes.Count);
        Assert.Equal(2, picture.Nodes.Count(node => node.IsMarked));
        Assert.Single(picture.Lines, line => line.IsMarked);
        Assert.True(picture.Bounds.X < 0);
        Assert.True(picture.Bounds.Right < 600 + 120);
    }
}

public sealed class ReportViewModelTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("T", TimeSpan.FromHours(1), "T", "T");

    private readonly TestReports _reports = new();

    private readonly TestSavePicker _picker = new() { Path = "/tmp/rapport.md" };

    private readonly CaseSession _session = new();

    private readonly Toaster _toaster = new(new InlineUi());

    private static GraphScene LabelledScene() => Scene(new Dictionary<string, IReadOnlyList<Label>>
    {
        [EKey] = [Label(E, LabelKind.Exchange, "Börs A")],
    });

    private async Task<ReportViewModel> ModelAsync(IReadOnlyList<string>? picked = null)
    {
        _session.Open(new TestCase("2026-0117"));
        var model = new ReportViewModel(_reports, _session, _picker, new InlineUi(), null, _toaster, Zone);
        await model.Built;
        model.Update(LabelledScene(), picked ?? [VKey, KKey, EKey], new Dictionary<string, GraphPoint>());
        await model.Built;
        return model;
    }

    [Fact]
    public void The_view_stands_last_among_the_work_with_ctrl_8()
    {
        var entry = ViewTable.For(AppView.Report);

        Assert.Equal("Rapport", entry.Name);
        Assert.Equal("Ctrl+8", entry.Gesture);
        Assert.True(entry.NeedsCase);
        Assert.Equal(AppView.Report, ViewTable.In(RailGroup.Work)[^1].View);
    }

    [Fact]
    public async Task The_report_is_built_from_the_marking()
    {
        var model = await ModelAsync();

        Assert.True(model.HasActors);
        Assert.Equal(3, model.Actors.Count);
        Assert.NotNull(model.Document);
        Assert.Equal("3 aktörer · 2 tx · 1 där pengarna lämnar eller spåret slutar", model.StatsText);
        var draft = _reports.Composed[^1];
        Assert.Single(draft.Trails);
        Assert.Equal(7, draft.Ledger?.Line);
        Assert.Equal(1, _reports.Checks);
    }

    [Fact]
    public async Task Nothing_marked_shows_the_hint()
    {
        var model = await ModelAsync([]);

        Assert.False(model.HasActors);
        Assert.Null(model.Document);
        Assert.False(model.ExportCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_parts_start_as_the_sketch_and_a_switch_composes_again()
    {
        var model = await ModelAsync();
        Assert.Equal("7 av 8", model.OnCountText);
        Assert.DoesNotContain(ReportPart.Labels, _reports.Composed[^1].Parts);

        model.TogglePart(model.Parts.Single(row => row.Part == ReportPart.Trail));
        await model.Built;

        Assert.Equal("6 av 8", model.OnCountText);
        Assert.DoesNotContain(ReportPart.Trail, _reports.Composed[^1].Parts);
    }

    [Fact]
    public async Task The_sek_part_says_honestly_when_no_rate_is_known()
    {
        var model = await ModelAsync();

        Assert.Equal(ReportViewModel.NoRateText, model.Parts.Single(row => row.Part == ReportPart.Sek).Sub);
    }

    [Fact]
    public async Task The_preview_switches_to_the_files_markdown()
    {
        var model = await ModelAsync();

        model.ShowRaw();

        Assert.True(model.IsRaw);
        Assert.False(model.IsPreview);
        Assert.Equal(RawLineKind.Heading, model.RawLines[0].Kind);
        Assert.Contains(model.RawLines, line => line.Kind == RawLineKind.Table);
        Assert.Contains(model.RawLines, line => line.Kind == RawLineKind.Faint);
        model.ShowPreview();
        Assert.True(model.IsPreview);
    }

    [Fact]
    public async Task Export_writes_through_the_module_and_lists_the_file()
    {
        var model = await ModelAsync();

        await model.ExportAsync();

        var (draft, path) = Assert.Single(_reports.Exported);
        Assert.Equal("/tmp/rapport.md", path);
        Assert.Equal(TestReports.Made, draft.MadeUtc);
        Assert.Equal("2026-0117 underrättelse.md", _picker.Offered);
        var row = Assert.Single(model.Exports);
        Assert.Equal("rapport.md", row.FileName);
        Assert.Contains(new string('c', 64), row.ToolTip, StringComparison.Ordinal);
        var toast = Assert.Single(_toaster.Toasts);
        Assert.Equal(ReportViewModel.ExportedTitle, toast.Title);
    }

    [Fact]
    public async Task A_cancelled_save_exports_nothing()
    {
        var model = await ModelAsync();
        _picker.Path = null;

        await model.ExportAsync();

        Assert.Empty(_reports.Exported);
    }

    [Fact]
    public async Task A_failed_export_is_said_in_a_toast()
    {
        var model = await ModelAsync();
        _reports.Failure = new IOException("Disken är full.");

        await model.ExportAsync();

        var toast = Assert.Single(_toaster.Toasts);
        Assert.Equal(ReportViewModel.ExportFailedTitle, toast.Title);
        Assert.False(model.IsExporting);
    }

    [Fact]
    public async Task Change_in_the_graph_asks_for_the_graph()
    {
        var model = await ModelAsync();
        var asked = false;
        model.GraphWanted += (_, _) => asked = true;

        model.GoToGraph();

        Assert.True(asked);
    }

    [Fact]
    public async Task Closing_the_case_empties_the_report()
    {
        var model = await ModelAsync();

        _session.Forget();
        await model.Built;

        Assert.False(model.HasActors);
        Assert.Null(model.Document);
        Assert.Empty(model.Exports);
    }
}
