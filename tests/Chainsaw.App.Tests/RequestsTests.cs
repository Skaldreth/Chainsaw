using Chainsaw.App.Graph;
using Chainsaw.App.Navigation;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Requests.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>The requests as the Requests module would answer, in memory; records what is exported.</summary>
internal sealed class TestRequests : IRequestService
{
    /// <summary>The time every composed request says it was made.</summary>
    public static readonly DateTimeOffset Made = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    public List<RequestDraft> Composed { get; } = [];

    public List<(RequestDraft Draft, string Path)> Exported { get; } = [];

    public List<RequestExport> Exports { get; } = [];

    public List<ExchangeContact> Contacts { get; } = [];

    public Exception? Failure { get; set; }

    public RequestDocument Compose(ICase @case, RequestDraft draft)
    {
        Composed.Add(draft);
        return new RequestDocument(
            draft.Target.Name, [new RequestBlock(RequestBlockKind.Title, draft.Target.Name)], "# " + draft.Target.Name,
            $"{@case.Info.CaseNumber} Begäran {draft.Target.Name}.md")
        {
            MadeUtc = Made,
        };
    }

    public RequestExport Export(ICase @case, RequestDraft draft, string path)
    {
        if (Failure is { } failure)
        {
            throw failure;
        }

        Exported.Add((draft, path));
        var export = new RequestExport(
            Exports.Count + 1, draft.Kind, draft.Language, draft.Target.Key, draft.Target.Name,
            System.IO.Path.GetFileName(path), path, new string('c', 64),
            [.. draft.Deposits.Select(deposit => deposit.Transaction)],
            new DateTimeOffset(2026, 11, 7, 13, 20, 0, TimeSpan.Zero), 9);
        Exports.Add(export);
        return export;
    }

    public IReadOnlyList<RequestExport> ListExports(ICase @case) => [.. Exports];

    public IReadOnlyList<ExchangeContact> ContactsFor(string name) =>
        [.. Contacts.Where(contact => contact.Recipient == name)];
}

/// <summary>Answers the save dialog with a path, or cancels.</summary>
internal sealed class TestSavePicker : ISaveFilePicker
{
    public string? Path { get; set; } = "/tmp/begäran.md";

    public string? Offered { get; private set; }

    public string? PickRequestFile(string suggestedName)
    {
        Offered = suggestedName;
        return Path;
    }
}

public sealed class RequestExitsTests
{
    private static GraphScene ExchangeScene(LabelKind kind = LabelKind.Exchange) => Scene(
        new Dictionary<string, IReadOnlyList<Label>> { [EKey] = [Label(E, kind, "Börs A")] });

    [Fact]
    public void A_cash_out_traced_money_reached_is_an_exit()
    {
        var exit = Assert.Single(RequestExits.Find(ExchangeScene()));

        Assert.Equal(EKey, exit.NodeKey);
        Assert.Equal(RequestKind.Information, exit.Kind);
        Assert.Equal("Börs A", exit.Target.Name);
        Assert.Equal(RequestTargetKind.Exchange, exit.Target.Kind);
        Assert.Equal([E.ToChainAddress()], exit.Target.Addresses);
    }

    [Fact]
    public void An_actor_without_a_cash_out_label_is_no_exit()
    {
        Assert.Empty(RequestExits.Find(Scene()));
        Assert.Empty(RequestExits.Find(ExchangeScene(LabelKind.Mixer)));
    }

    [Fact]
    public void A_cash_out_no_traced_money_reached_is_no_exit()
    {
        var trace = Trace() with { Edges = [Traced(WV, 1.0m), Traced(VK, 0.6m)] };
        var scene = Scene(new Dictionary<string, IReadOnlyList<Label>> { [EKey] = [Label(E, LabelKind.Exchange, "Börs A")] },
            trace: trace);

        Assert.Empty(RequestExits.Find(scene));
    }

    [Theory]
    [InlineData(LabelKind.CryptoAtm, RequestTargetKind.Atm)]
    [InlineData(LabelKind.NoKycExchange, RequestTargetKind.NoKycExchange)]
    [InlineData(LabelKind.OtcP2P, RequestTargetKind.OtcP2P)]
    public void Every_blue_sort_is_a_cash_out(LabelKind label, RequestTargetKind expected)
    {
        Assert.Equal(expected, Assert.Single(RequestExits.Find(ExchangeScene(label))).Target.Kind);
    }

    [Fact]
    public void A_deposit_is_a_fact_with_its_transaction_time_height_and_hash()
    {
        var deposit = Assert.Single(Assert.Single(RequestExits.Find(ExchangeScene())).Deposits);

        Assert.Equal(Tx[2].Value, deposit.Transaction);
        Assert.Equal(E.ToChainAddress(), deposit.To);
        Assert.Equal(Btc(0.5m), deposit.Amount);
        Assert.Equal(102, deposit.BlockHeight);
        Assert.Equal(Block(102).Hash.Value, deposit.BlockHash);
        Assert.Equal(Block(102).Time, deposit.Time);
        Assert.True(deposit.IsTraced);
    }

    [Fact]
    public void The_trail_is_shortened_addresses_from_the_start_and_the_exit_s_name()
    {
        var trail = Assert.Single(RequestExits.Find(ExchangeScene())).Trail;

        Assert.NotNull(trail);
        Assert.Equal(
            [GraphScene.Shorten(V.Value), GraphScene.Shorten(K1.Value), "Börs A"], trail.Steps);
        Assert.Equal(2, trail.Hops);
        Assert.Equal("FIFO v1", trail.Method);
    }

    [Fact]
    public void A_victim_s_name_never_reaches_the_trail()
    {
        var labels = new Dictionary<string, IReadOnlyList<Label>>
        {
            [EKey] = [Label(E, LabelKind.Exchange, "Börs A")],
            [VKey] = [Label(V, LabelKind.Victim, "Anna Andersson")],
            [KKey] = [Label(K1, LabelKind.Unknown, "Misstänkt Svensson")],
        };

        var trail = Assert.Single(RequestExits.Find(Scene(labels))).Trail!;

        Assert.DoesNotContain(trail.Steps, step => step.Contains("Andersson", StringComparison.Ordinal));
        Assert.DoesNotContain(trail.Steps, step => step.Contains("Svensson", StringComparison.Ordinal));
    }

    [Fact]
    public void The_summary_says_the_chain_the_deposits_and_their_sum()
    {
        var exit = Assert.Single(RequestExits.Find(ExchangeScene()));

        Assert.Equal("Bitcoin · 1 insättning · " + AmountText.WithUnit(Btc(0.5m)), exit.Summary);
    }

    [Fact]
    public void When_writes_the_local_time_or_that_it_is_unconfirmed()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("T", TimeSpan.FromHours(1), "T", "T");
        var deposit = new RequestDeposit(
            "tx", E.ToChainAddress(), Btc(1m), new DateTimeOffset(2026, 10, 28, 20, 14, 0, TimeSpan.Zero), 1, "h", true);

        Assert.Equal("2026-10-28 21:14", RequestExits.When(deposit, zone));
        Assert.Equal("obekräftad", RequestExits.When(deposit with { Time = null }, zone));
    }
}

public sealed class RequestsViewModelTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("T", TimeSpan.FromHours(1), "T", "T");

    private readonly TestRequests _requests = new();

    private readonly TestSavePicker _picker = new();

    private readonly CaseSession _session = new();

    private readonly Toaster _toaster = new(new InlineUi());

    private RequestsViewModel Model(bool open = true)
    {
        if (open)
        {
            _session.Open(new TestCase("2026-0117"));
        }

        var model = new RequestsViewModel(_requests, _session, _picker, new InlineUi(), _toaster, Zone);
        model.Update(Scene(new Dictionary<string, IReadOnlyList<Label>> { [EKey] = [Label(E, LabelKind.Exchange, "Börs A")] }));
        return model;
    }

    [Fact]
    public void The_view_lists_the_exits_and_chooses_the_first()
    {
        var model = Model();

        var row = Assert.Single(model.Exits);
        Assert.True(model.HasExits);
        Assert.Same(row, model.Selected);
        Assert.True(row.IsSelected);
        Assert.Equal("Uttag som nåtts i ärendet · 1", model.CountText);
        Assert.NotNull(model.Document);
        Assert.Equal("Börs A", model.Document.Title);
    }

    [Fact]
    public void Traced_deposits_and_the_trail_are_taken_along_at_first()
    {
        var model = Model();

        var chip = Assert.Single(model.Chips);
        Assert.True(chip.IsOn);
        Assert.True(model.HasTrail);
        Assert.True(model.IsTrailOn);
        var draft = _requests.Composed[^1];
        Assert.Single(draft.Deposits);
        Assert.NotNull(draft.Trail);
        Assert.Same(Zone, draft.TimeZone);
    }

    [Fact]
    public void Leaving_out_every_deposit_says_why_and_cannot_be_exported()
    {
        var model = Model();

        model.ToggleDeposit(model.Chips[0]);

        Assert.Null(model.Document);
        Assert.Equal("Välj minst en insättning att ta med.", model.Problem);
        Assert.False(model.ExportCommand.CanExecute(null));
    }

    [Fact]
    public void The_trail_can_be_left_out()
    {
        var model = Model();

        model.ToggleTrail();

        Assert.False(model.IsTrailOn);
        Assert.Null(_requests.Composed[^1].Trail);
        model.ToggleTrail();
        Assert.NotNull(_requests.Composed[^1].Trail);
    }

    [Fact]
    public void The_choices_stay_when_the_graph_is_drawn_again()
    {
        var model = Model();
        model.ToggleTrail();
        model.ToggleDeposit(model.Chips[0]);

        model.Update(Scene(new Dictionary<string, IReadOnlyList<Label>> { [EKey] = [Label(E, LabelKind.Exchange, "Börs A")] }));

        Assert.False(model.IsTrailOn);
        Assert.False(model.Chips[0].IsOn);
    }

    [Fact]
    public void The_language_switches_between_Swedish_and_English()
    {
        var model = Model();
        Assert.Equal("Svenska", model.LanguageText);

        model.ToggleLanguage();

        Assert.Equal("English", model.LanguageText);
        Assert.Equal(RequestLanguage.English, _requests.Composed[^1].Language);
    }

    [Fact]
    public async Task Export_offers_the_file_name_writes_and_lists_the_export()
    {
        var model = Model();

        await model.ExportAsync();

        Assert.Equal("2026-0117 Begäran Börs A.md", _picker.Offered);
        var (draft, path) = Assert.Single(_requests.Exported);
        Assert.Equal("/tmp/begäran.md", path);
        Assert.Equal("Börs A", draft.Target.Name);
        var row = Assert.Single(model.Exports);
        Assert.Equal("begäran.md", row.FileName);
        Assert.Equal("cccccccc…cccc", row.ShortSha);
        Assert.Equal("2026-11-07 14:20", row.When);
        Assert.True(model.Exits[0].IsExported);
        Assert.Equal("Exporterad 2026-11-07 14:20", model.Exits[0].ExportedText);
        Assert.Contains(_toaster.Toasts, toast => toast.Title == RequestsViewModel.ExportedTitle);
    }

    [Fact]
    public async Task A_cancelled_dialog_exports_nothing()
    {
        var model = Model();
        _picker.Path = null;

        await model.ExportAsync();

        Assert.Empty(_requests.Exported);
        Assert.Empty(model.Exports);
    }

    [Fact]
    public async Task A_failed_export_is_said_in_a_toast()
    {
        var model = Model();
        _requests.Failure = new IOException("Disken är full.");

        await model.ExportAsync();

        Assert.Contains(_toaster.Toasts, toast => toast.Title == RequestsViewModel.ExportFailedTitle
            && toast.Text == "Filen gick inte att skriva.");
        Assert.False(model.IsExporting);
    }

    [Fact]
    public void Without_a_verified_contact_the_view_says_so_honestly()
    {
        var model = Model();

        Assert.Equal(
            "Ingen verifierad kontaktväg för Börs A finns i Chainsaw. Använd mottagarens egen väg för myndigheter.",
            model.ContactText);
    }

    [Fact]
    public void A_verified_contact_is_shown_with_its_source_and_date()
    {
        _requests.Contacts.Add(new ExchangeContact(
            "Börs A", "Portalen för myndigheter", new Uri("https://example.invalid/le"), new DateOnly(2026, 11, 8)));

        var model = Model();

        Assert.Equal(
            "Kontaktväg: Portalen för myndigheter (enligt example.invalid, kontrollerad 2026-11-08)", model.ContactText);
    }

    [Fact]
    public void Open_chooses_a_node_s_request()
    {
        var model = Model();

        Assert.True(model.Open(EKey, RequestKind.Information));
        Assert.False(model.Open(EKey, RequestKind.Freeze));
        Assert.False(model.Open(VKey, RequestKind.Information));
    }

    [Fact]
    public void Without_a_graph_the_view_is_empty_and_honest()
    {
        var model = Model();

        model.Update(null);

        Assert.False(model.HasExits);
        Assert.Null(model.Selected);
        Assert.Null(model.Document);
        Assert.Empty(model.Chips);
    }

    [Fact]
    public async Task Exports_already_in_the_case_are_listed_newest_first()
    {
        _requests.Exports.Add(new RequestExport(
            1, RequestKind.Information, RequestLanguage.Swedish, RequestTarget.KeyOf(E.ToChainAddress()), "Börs A",
            "a.md", "/a.md", new string('a', 64), [], new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero), 3));
        _requests.Exports.Add(new RequestExport(
            2, RequestKind.Information, RequestLanguage.Swedish, "Bitcoin:other", "Börs B",
            "b.md", "/b.md", new string('b', 64), [], new DateTimeOffset(2026, 11, 7, 8, 0, 0, TimeSpan.Zero), 4));

        var model = Model();
        await model.ExportsRead;

        Assert.Equal(["b.md", "a.md"], model.Exports.Select(row => row.FileName));
        Assert.Equal("Exporterad 2026-11-06 09:00", model.Exits[0].ExportedText);
    }
}

public sealed class RequestsShellTests
{
    [Fact]
    public void Begäran_is_built_and_needs_a_case()
    {
        var entry = ViewTable.For(AppView.Requests);

        Assert.True(entry.IsBuilt);
        Assert.True(entry.NeedsCase);
    }

    [Fact]
    public void Nytt_Begäran_needs_an_open_case()
    {
        var row = SubmenuTable.New.Single(r => r.Action == SubmenuAction.Request);

        Assert.Equal(SubmenuNeed.OpenCase, row.Need);
    }
}

public sealed class RequestFreezeTests
{
    [Fact]
    public void An_address_with_USDT_left_gets_a_freeze_to_Tether()
    {
        var freeze = Assert.Single(RequestExits.Find(EthSamples.Scene()), exit => exit.Kind == RequestKind.Freeze);

        Assert.Equal(EthSamples.Key(EthSamples.S), freeze.NodeKey);
        Assert.Equal("Tether", freeze.Target.Name);
        Assert.Equal(RequestTargetKind.TokenIssuer, freeze.Target.Kind);
        Assert.Equal([EthSamples.S.ToChainAddress()], freeze.Target.Addresses);
        Assert.NotNull(freeze.Holding);
        Assert.Equal(EthSamples.Usdt(100), freeze.Holding.Left);
        Assert.All(freeze.Deposits, deposit => Assert.Equal("USDT", deposit.Amount.Asset.Symbol));
        Assert.StartsWith("Tether · ", freeze.Name, StringComparison.Ordinal);
        Assert.Contains("kvar", freeze.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_read_history_no_freeze_is_offered()
    {
        Assert.DoesNotContain(
            RequestExits.Find(EthSamples.Scene(read: false)), exit => exit.Kind == RequestKind.Freeze);
    }
}

/// <summary>The fixes after the review of BG1b: what a request may hold, and that its marks stay.</summary>
public sealed class RequestReviewTests
{
    private const string XKey = "address:x";

    private static readonly BitcoinAddress E2 = Address(8);

    private static readonly BitcoinAddress X = Address(7);

    [Fact]
    public void A_request_concerns_only_the_addresses_traced_money_reached()
    {
        var exit = Assert.Single(RequestExits.Find(ClusterScene()));

        Assert.Equal([E.ToChainAddress()], exit.Target.Addresses);
        Assert.DoesNotContain(exit.Deposits, deposit => deposit.To == E2.ToChainAddress());
        Assert.Equal([E.ToChainAddress(), E2.ToChainAddress()], exit.NodeAddresses);
    }

    [Fact]
    public void An_untraced_deposit_to_the_same_address_can_be_taken_along()
    {
        var exit = Assert.Single(RequestExits.Find(ClusterScene()));

        var other = Assert.Single(exit.Deposits, deposit => deposit.Transaction == TxidOf(5).Value);
        Assert.False(other.IsTraced);
        Assert.Equal(E.ToChainAddress(), other.To);
        Assert.Equal(2, exit.Deposits.Count);
    }

    [Fact]
    public void The_request_names_only_the_addresses_of_the_chosen_deposits()
    {
        var requests = new TestRequests();
        var model = Model(requests);

        Assert.Equal([E.ToChainAddress()], requests.Composed[^1].Target.Addresses);
        Assert.Single(requests.Composed[^1].Deposits);

        model.ToggleDeposit(model.Chips.Single(chip => !chip.IsOn));

        Assert.Equal([E.ToChainAddress()], requests.Composed[^1].Target.Addresses);
        Assert.Equal(2, requests.Composed[^1].Deposits.Count);
    }

    [Fact]
    public void A_cash_out_is_named_only_by_its_cash_out_label()
    {
        var labels = new Dictionary<string, IReadOnlyList<Label>>
        {
            [EKey] = [Label(E, LabelKind.Exchange, "Börs A"), Label(E, LabelKind.Victim, "Anna Andersson")],
        };

        var exit = Assert.Single(RequestExits.Find(Scene(labels)));

        Assert.Equal("Börs A", exit.Target.Name);
        Assert.All(exit.Trail!.Steps, step => Assert.DoesNotContain("Andersson", step, StringComparison.Ordinal));
    }

    [Fact]
    public void A_cash_out_label_without_a_name_is_written_as_the_address()
    {
        var labels = new Dictionary<string, IReadOnlyList<Label>> { [EKey] = [Label(E, LabelKind.Exchange)] };

        var exit = Assert.Single(RequestExits.Find(Scene(labels)));

        Assert.Equal(GraphScene.Shorten(E.Value), exit.Target.Name);
        Assert.Equal(GraphScene.Shorten(E.Value), exit.Trail!.Steps[^1]);
    }

    [Fact]
    public void A_freeze_never_carries_a_victim_s_name()
    {
        var scene = FreezeScene(victim: true);

        var freeze = Assert.Single(RequestExits.Find(scene), exit => exit.Kind == RequestKind.Freeze);

        Assert.Equal("Tether", freeze.Target.Name);
        Assert.NotNull(freeze.Trail);
        Assert.Equal(GraphScene.Shorten(EthSamples.S.Value), freeze.Trail.Steps[^1]);
        Assert.DoesNotContain("Andersson", freeze.Name, StringComparison.Ordinal);
        Assert.All(freeze.Trail.Steps, step => Assert.DoesNotContain("Andersson", step, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_token_left_gets_a_freeze_of_its_own()
    {
        var scene = FreezeScene(victim: false, usdc: 50m);

        var freezes = RequestExits.Find(scene).Where(exit => exit.Kind == RequestKind.Freeze).ToList();

        Assert.Equal(["Circle", "Tether"], freezes.Select(freeze => freeze.Target.Name).Order());
        Assert.Equal(2, freezes.Select(freeze => freeze.Key).Distinct().Count());
    }

    [Fact]
    public void A_transaction_node_is_neither_a_step_nor_a_hop()
    {
        var graph = GraphSamples.Graph();
        var nodes = graph.Nodes
            .Select(node => node.Key == KKey ? node with { Kind = GraphNodeKind.Transaction } : node)
            .ToList();
        var labels = new Dictionary<string, IReadOnlyList<Label>> { [EKey] = [Label(E, LabelKind.Exchange, "Börs A")] };

        var trail = Assert.Single(RequestExits.Find(Scene(labels, graph with { Nodes = nodes }))).Trail;

        Assert.NotNull(trail);
        Assert.Equal([GraphScene.Shorten(V.Value), "Börs A"], trail.Steps);
        Assert.Equal(1, trail.Hops);
    }

    [Fact]
    public void The_exported_mark_stays_when_the_export_named_another_of_the_actor_s_addresses()
    {
        var exit = Assert.Single(RequestExits.Find(ClusterScene()));
        var export = new RequestExport(
            1, RequestKind.Information, RequestLanguage.Swedish, RequestTarget.KeyOf(E2.ToChainAddress()), "Börs A",
            "a.md", "/a.md", new string('a', 64), [], DateTimeOffset.UnixEpoch, 1);

        Assert.True(exit.IsExportedBy(export));
        Assert.False(exit.IsExportedBy(export with { Kind = RequestKind.Freeze }));
        Assert.Equal("Information:" + RequestTarget.KeyOf(E.ToChainAddress()), exit.Key);
    }

    [Fact]
    public void The_same_scene_is_searched_once()
    {
        var scene = ClusterScene();

        Assert.Same(RequestExits.Find(scene), RequestExits.Find(scene));
    }

    [Fact]
    public async Task The_export_says_the_time_the_preview_showed()
    {
        var requests = new TestRequests();
        var model = Model(requests);

        await model.ExportAsync();

        Assert.Equal(TestRequests.Made, Assert.Single(requests.Exported).Draft.MadeUtc);
    }

    /// <summary>The view over the cluster scene, with a case open.</summary>
    private static RequestsViewModel Model(TestRequests requests)
    {
        var session = new CaseSession();
        session.Open(new TestCase("2026-0117"));
        var model = new RequestsViewModel(requests, session, new TestSavePicker(), new InlineUi());
        model.Update(ClusterScene());
        return model;
    }

    /// <summary>
    /// The exchange E as a cluster of E and E2: traced money reaches E (tx 2), another actor X pays E2 (tx 4) and E
    /// itself (tx 5); neither of X's deposits is traced.
    /// </summary>
    private static GraphScene ClusterScene()
    {
        var graph = GraphSamples.Graph();
        var tx4 = Transaction(4, [(X, 0.31m)], [(E2, 0.3m)]);
        var tx5 = Transaction(5, [(X, 0.21m)], [(E, 0.2m)]);
        var transactions = new Dictionary<Txid, GraphTransaction>(graph.Utxo.Transactions)
        {
            [tx4.Txid] = new GraphTransaction(tx4, LookupStatus.FromCase, null),
            [tx5.Txid] = new GraphTransaction(tx5, LookupStatus.FromCase, null),
        };
        GraphNode[] nodes =
        [
            .. graph.Nodes.Where(node => node.Key != EKey),
            Node(EKey, GraphNodeKind.Cluster, [E, E2], hop: 2, size: 2),
            Node(XKey, GraphNodeKind.Address, [X], hop: 1),
        ];
        GraphEdge[] edges = [.. graph.Edges, Line("x-e2", 4, 0.3m), Line("x-e", 5, 0.2m)];
        var changed = graph with
        {
            Nodes = nodes, Edges = edges, Utxo = graph.Utxo with { Transactions = transactions },
        };
        return Scene(
            new Dictionary<string, IReadOnlyList<Label>> { [EKey] = [Label(E, LabelKind.Exchange, "Börs A")] },
            changed);
    }

    /// <summary>A line from X into E with one coin of a transaction numbered beyond the samples' four.</summary>
    private static GraphEdge Line(string key, int tx, decimal amount)
    {
        var txid = TxidOf(tx);
        var block = Block(100 + tx);
        return new GraphEdge(
            key, XKey, EKey, GraphEdgeKind.Transfer, Btc(amount),
            [AssetTransfer.OfCoin(txid, new Outpoint(txid, 0), Satoshi.FromBitcoin(amount), block)],
            block.Time, EdgeDirection.Forward);
    }

    /// <summary>
    /// The Ethereum samples with V as the start, so that a trail reaches S, optionally a victim's label on S and USDC
    /// sent to S as well.
    /// </summary>
    private static GraphScene FreezeScene(bool victim, decimal? usdc = null)
    {
        var graph = EthSamples.Graph();
        var start = EthSamples.Key(EthSamples.V);
        var nodes = graph.Nodes.Select(node => node with { IsStartPoint = node.Key == start }).ToList();
        var edges = graph.Edges.ToList();
        if (usdc is { } amount)
        {
            var coin = new AssetAmount(KnownTokens.EthereumUsdc, new System.Numerics.BigInteger(amount * 1_000_000m));
            var move = EthSamples.Move(6, EthereumTransferKind.Token, EthSamples.V, EthSamples.S, coin, 9);
            edges.Add(new GraphEdge(
                "v-s-usdc", start, EthSamples.Key(EthSamples.S), GraphEdgeKind.Transfer, coin,
                [AssetTransfer.OfEthereum(move, EthSamples.BlockOf(move.Block!))], move.Block!.Time,
                EdgeDirection.Forward));
        }

        var labels = new Dictionary<string, IReadOnlyList<Label>>();
        if (victim)
        {
            labels[EthSamples.Key(EthSamples.S)] = [Victim(EthSamples.S.ToChainAddress(), "Anna Andersson")];
        }

        var usdt = EthSamples.Usdt(1_000);
        var trace = new AccountTraceResult(
            new TracingMethodId(TracingMethodKind.Fifo, 1),
            [AccountSeed.Of(EthSamples.Transfers[0])],
            [new AssetTrace(usdt, [EthSamples.Traced(EthSamples.EdgeKey(EthSamples.V, EthSamples.S, "USDT"), usdt)], [])]);
        var changed = graph with
        {
            Nodes = nodes, Edges = edges, StartPoints = [TraceStartPoint.ForEthereumAddress(EthSamples.V)],
        };
        return GraphScene.Build(new GraphSnapshot(changed, labels, TracingMethodKind.Fifo, [], null, null)
        {
            AccountSeeds = [AccountSeed.Of(EthSamples.Transfers[0])],
            AccountTrace = trace,
        });
    }

    /// <summary>A victim's label on an account, from one origin.</summary>
    private static Label Victim(ChainAddress address, string name)
    {
        var origin = new LabelOrigin(
            Guid.NewGuid().ToString("N"), LabelSource.Own, LabelSource.Own, "egen", "1",
            new OperatorProfile("Utredare", "Analytiker", "Enheten"), DateTimeOffset.UnixEpoch, null, null);
        var subject = LabelSubject.ForAccount(address);
        return new Label(
            subject, LabelKind.Victim, name, [new LabelEntry(1, subject, LabelKind.Victim, name, origin, false, 1)]);
    }
}
