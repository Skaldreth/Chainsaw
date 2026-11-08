using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>The view Etiketter (sketch ET1a): its rows, filters, library and the changes it seals.</summary>
public sealed class LabelsTests
{
    private const string CaseNumber = "2026-0117";

    private static readonly DateTimeOffset Set = new(2026, 11, 6, 14, 2, 0, TimeSpan.Zero);

    private static readonly OperatorProfile Me = new("M. Persson", "utredare", "Enhet Syd");

    private static readonly OperatorProfile Colleague = new("A. Lind", "analytiker", "Enhet Väst");

    private readonly TestAttribution _attribution = new();

    private readonly TestLibrary _library = new();

    private readonly TestSanctionLists _sanctions = new();

    private readonly TestGraphEditor _editor = new();

    private readonly CaseSession _session = new();

    private readonly Toaster _toaster = new(new InlineUi());

    [Fact]
    public async Task The_same_label_from_two_origins_is_one_confirmed_row()
    {
        _attribution.History =
        [
            Entry(1, E, LabelKind.Exchange, "Börs A", LabelSource.Own, Me),
            Entry(2, E, LabelKind.Exchange, "börs a ", LabelSource.OtherCase, Colleague),
        ];

        var model = await ModelAsync(Scene());

        var row = Assert.Single(model.Items.OfType<LabelRowViewModel>());
        Assert.Equal("Börs · Börs A", row.Title);
        Assert.Equal(LabelFilter.Own, row.Source);
        Assert.True(row.IsConfirmed);
        Assert.True(row.IsFromOtherCase);
        Assert.Equal(EKey, row.NodeKey);
        Assert.Equal("URSPRUNG · 2 OBEROENDE", row.OriginTitle);
        Assert.Equal(
            ["Ärende 2026-0117 · här", LabelWords.OtherCaseText], row.Origins.Select(origin => origin.CaseText));
        Assert.Equal("M. Persson · utredare · Enhet Syd", row.Origins[0].Who);
        Assert.Equal("2026-11-06 14:02 (14:02 UTC)", row.Origins[0].When);
        Assert.True(row.Origins[0].HasLine);
        Assert.False(row.Origins[1].HasLine);
        Assert.Same(row, model.Selected);
    }

    [Fact]
    public async Task Sanction_hits_in_the_graph_stand_first_in_red_and_the_rest_under()
    {
        _sanctions.Imported.Add([1]);
        _attribution.History =
        [
            Entry(1, U, LabelKind.Mixer, null, LabelSource.Own, Me),
            Entry(2, E, LabelKind.Sanctioned, "Växlare Z", LabelSource.SanctionList, Me, sourceName: "OFAC SDN"),
            Entry(3, Address(40), LabelKind.Sanctioned, null, LabelSource.SanctionList, Me, sourceName: "OFAC SDN"),
        ];

        var model = await ModelAsync(Scene());

        var headers = model.Items.OfType<LabelGroupHeader>().ToList();
        Assert.Equal(["SANKTIONSTRÄFFAR I GRAFEN", "ETIKETTER I ÄRENDET"], headers.Select(header => header.Title));
        Assert.True(headers[0].IsRisk);
        Assert.Equal("OFAC SDN · publicerad 2026-11-01", headers[0].Note);
        var hit = Assert.IsType<LabelRowViewModel>(model.Items[1]);
        Assert.True(hit.IsSanction);
        Assert.Equal("TRÄFFEN", hit.OriginTitle);
        Assert.Contains(hit.Chips, chip => chip.Text == "Står på listan");
        Assert.Equal(2, headers[1].Count);
        Assert.Equal("OFAC SDN · 2026-11-01", model.Sources.Single(source => source.IsSanction).Detail);
    }

    [Fact]
    public async Task A_label_taken_back_is_struck_through_last_with_who_and_why()
    {
        var retraction = new LabelRetraction(9, Me, Set.AddHours(1), "fel kluster", 9);
        _attribution.History =
        [
            Entry(1, E, LabelKind.Gambling, "Spelsajt", LabelSource.Own, Me, retraction: retraction),
            Entry(2, U, LabelKind.Exchange, null, LabelSource.Own, Me),
        ];

        var model = await ModelAsync(Scene());

        var rows = model.Items.OfType<LabelRowViewModel>().ToList();
        Assert.Equal(["Börs", "Spel · Spelsajt"], rows.Select(row => row.Title));
        Assert.True(rows[1].IsRetracted);
        Assert.False(rows[1].CanRetract);
        Assert.Equal("Tagen tillbaka 2026-11-06 15:02 (15:02 UTC) av M. Persson: fel kluster", rows[1].RetractedText);
        Assert.Equal(1, model.CaseCount);
    }

    [Fact]
    public async Task The_sources_filter_the_list_and_count_their_labels()
    {
        _attribution.History =
        [
            Entry(1, E, LabelKind.Exchange, "Börs A", LabelSource.Own, Me),
            Entry(2, E, LabelKind.Exchange, "Börs A", LabelSource.OtherCase, Colleague),
            Entry(3, U, LabelKind.Gambling, null, LabelSource.Behaviour, Me, sourceName: "gambling-pattern"),
        ];
        var model = await ModelAsync(Scene());
        var behaviour = model.Sources.Single(source => source.Filter == LabelFilter.Behaviour);
        var other = model.Sources.Single(source => source.Filter == LabelFilter.OtherCase);
        Assert.Equal(1, behaviour.Count);
        Assert.Equal(1, other.Count);
        Assert.True(model.Items.OfType<LabelRowViewModel>().Single(row => row.IsSuggestion).IsDashed);

        model.ToggleSourceCommand.Execute(behaviour);
        Assert.Equal(["Börs · Börs A"], model.Items.OfType<LabelRowViewModel>().Select(row => row.Title));

        model.ToggleSourceCommand.Execute(other);
        Assert.Empty(model.Items);
        Assert.Equal(LabelsViewModel.FilteredText, model.EmptyMessage);
        Assert.Null(model.Selected);
        Assert.False(other.IsOn);
        Assert.Equal("Visa etiketterna från källan", other.ToolTip);
    }

    [Fact]
    public async Task The_filter_field_finds_a_label_by_its_address_or_name()
    {
        _attribution.History =
        [
            Entry(1, E, LabelKind.Exchange, "Börs A", LabelSource.Own, Me),
            Entry(2, U, LabelKind.Mixer, null, LabelSource.Own, Me),
        ];
        var model = await ModelAsync(Scene());

        model.Query = U.Value[^6..];
        Assert.Equal(["Mixer"], model.Items.OfType<LabelRowViewModel>().Select(row => row.Title));

        model.Query = "börs a";
        Assert.Equal(["Börs · Börs A"], model.Items.OfType<LabelRowViewModel>().Select(row => row.Title));
    }

    [Fact]
    public async Task Without_labels_the_list_says_so_honestly()
    {
        var model = await ModelAsync(Scene());

        Assert.False(model.HasItems);
        Assert.False(model.HasSelected);
        Assert.Equal(LabelsViewModel.EmptyText, model.EmptyMessage);
    }

    [Fact]
    public async Task The_library_lists_its_labels_with_their_case_numbers_and_counts_what_went_out()
    {
        _attribution.History = [Entry(1, E, LabelKind.Exchange, "Börs A", LabelSource.OtherCase, Colleague)];
        _library.Listed =
        [
            Library(E, LabelKind.Exchange, "Börs A", [CaseNumber, "2026-0098"]),
            Library(Address(41), LabelKind.OtcP2P, "Kontantväxlare", ["2025-0412"]),
        ];
        var model = await ModelAsync(Scene());
        Assert.Equal("2", model.LibraryCount);
        Assert.Equal("1", model.WentOut);
        Assert.Equal(1, model.CameIn);

        model.ChooseScopeCommand.Execute(LabelScope.Library);

        var rows = model.Items.OfType<LabelRowViewModel>().ToList();
        Assert.Equal([CaseNumber, "2026-0098"], rows[0].Cases);
        Assert.True(rows[0].IsInGraph);
        Assert.False(rows[1].IsInGraph);
        Assert.False(model.HasActions);
        Assert.Equal("Ärenden 2026-0117, 2026-0098", rows[0].Origins[0].CaseText);

        model.Query = "0412";
        Assert.Equal(
            ["OTC eller P2P · Kontantväxlare"], model.Items.OfType<LabelRowViewModel>().Select(row => row.Title));
    }

    [Fact]
    public async Task A_library_that_cannot_be_read_is_said_honestly()
    {
        _library.ListFailure = new VaultException(VaultError.Locked);
        var model = await ModelAsync(Scene());

        model.ChooseScopeCommand.Execute(LabelScope.Library);

        Assert.Equal("–", model.LibraryCount);
        Assert.Equal("–", model.WentOut);
        Assert.Equal(LabelsViewModel.LibraryClosedText, model.EmptyMessage);
    }

    [Fact]
    public async Task Sprid_inte_marks_every_own_label_of_the_row_and_reads_the_graph_again()
    {
        _attribution.History =
        [
            Entry(1, E, LabelKind.Exchange, "Börs A", LabelSource.Own, Me),
            Entry(2, E, LabelKind.Exchange, "Börs A", LabelSource.Own, Colleague),
            Entry(3, E, LabelKind.Exchange, "Börs A", LabelSource.OtherCase, Colleague),
        ];
        var model = await ModelAsync(Scene());
        var changed = 0;
        model.Changed += (_, _) => changed++;
        Assert.True(model.Selected!.ShowsSpread);
        Assert.False(model.Selected.IsNoSpread);

        await model.ToggleSpreadCommand.ExecuteAsync(null);

        Assert.Equal([("spread", "1", "on"), ("spread", "2", "on")], _editor.Undone);
        Assert.Equal(1, changed);
        var toast = Assert.Single(_toaster.Toasts);
        Assert.Equal(LabelsViewModel.NoSpreadTitle, toast.Title);
    }

    [Fact]
    public async Task A_victim_s_label_shows_Sprid_inte_on_and_cannot_be_turned()
    {
        _attribution.History = [Entry(1, W, LabelKind.Victim, "Offret", LabelSource.Own, Me)];

        var model = await ModelAsync(Scene());

        var row = model.Selected!;
        Assert.True(row.ShowsSpread);
        Assert.True(row.IsNoSpread);
        Assert.False(model.ToggleSpreadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Ta_tillbaka_folds_out_the_reason_and_seals_through_the_editor()
    {
        _attribution.History =
        [
            Entry(1, E, LabelKind.Exchange, "Börs A", LabelSource.Own, Colleague),
            Entry(2, E, LabelKind.Exchange, "Börs A", LabelSource.SanctionList, Me, sourceName: "OFAC SDN"),
        ];
        var model = await ModelAsync(Scene());
        Assert.True(model.ShowsRetractButton);

        model.BeginRetractCommand.Execute(null);
        Assert.True(model.IsRetracting);
        Assert.False(model.ShowsRetractButton);
        model.RetractReason = "  fel börs ";
        await model.ConfirmRetractCommand.ExecuteAsync(null);

        Assert.Equal([("label", "1", "fel börs")], _editor.Undone);
        Assert.False(model.IsRetracting);
        Assert.Equal(LabelsViewModel.RetractedTitle, Assert.Single(_toaster.Toasts).Title);
    }

    [Fact]
    public async Task When_the_library_cannot_follow_the_user_is_warned()
    {
        _attribution.History = [Entry(1, E, LabelKind.Exchange, null, LabelSource.Own, Me)];
        _editor.Outcome = new UndoOutcome(true, GraphEditor.LibraryLaterText);
        var model = await ModelAsync(Scene());

        model.BeginRetractCommand.Execute(null);
        await model.ConfirmRetractCommand.ExecuteAsync(null);

        var toast = Assert.Single(_toaster.Toasts);
        Assert.Equal(ToastKind.Warning, toast.Kind);
        Assert.Equal(GraphEditor.LibraryLaterText, toast.Text);
    }

    [Fact]
    public async Task A_failure_is_told_and_nothing_else_happens()
    {
        _attribution.History = [Entry(1, E, LabelKind.Exchange, null, LabelSource.Own, Me)];
        _editor.Failure = new InvalidOperationException("Ärendet är stängt.");
        var model = await ModelAsync(Scene());

        await model.ToggleSpreadCommand.ExecuteAsync(null);

        Assert.Equal(ToastKind.Error, Assert.Single(_toaster.Toasts).Kind);
        Assert.False(model.IsSaving);
    }

    [Fact]
    public async Task Visa_i_grafen_asks_for_the_node_and_is_not_offered_outside_the_graph()
    {
        _attribution.History =
        [
            Entry(1, E, LabelKind.Exchange, "Börs A", LabelSource.Own, Me),
            Entry(2, Address(42), LabelKind.Mixer, null, LabelSource.Own, Me),
        ];
        var model = await ModelAsync(Scene());
        var wanted = new List<string>();
        model.ShowInGraphWanted += (_, key) => wanted.Add(key);

        model.ShowInGraphCommand.Execute(null);
        model.Move(1);

        Assert.Equal([EKey], wanted);
        Assert.Equal("Mixer", model.Selected?.Title);
        Assert.False(model.CanShowInGraph);
        Assert.False(model.ShowInGraphCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_closed_case_empties_the_view()
    {
        _attribution.History = [Entry(1, E, LabelKind.Exchange, null, LabelSource.Own, Me)];
        var model = await ModelAsync(Scene());

        await _session.CloseAsync();

        Assert.Empty(model.Items);
        Assert.Null(model.Selected);
    }

    [Fact]
    public async Task The_filter_field_filters_the_rows_already_built()
    {
        _attribution.History =
        [
            Entry(1, E, LabelKind.Exchange, "Börs A", LabelSource.Own, Me),
            Entry(2, U, LabelKind.Mixer, null, LabelSource.Own, Me),
        ];
        var model = await ModelAsync(Scene());
        var mixer = model.Items.OfType<LabelRowViewModel>().Single(row => row.Title == "Mixer");

        model.Query = "mixer";
        model.Query = string.Empty;

        Assert.Same(mixer, model.Items.OfType<LabelRowViewModel>().Single(row => row.Title == "Mixer"));
    }

    [Fact]
    public async Task A_sanction_hit_in_the_trash_still_stands_first_with_its_mark()
    {
        _sanctions.Imported.Add([1]);
        _attribution.History =
            [Entry(1, U, LabelKind.Sanctioned, "Växlare Z", LabelSource.SanctionList, Me, sourceName: "OFAC SDN")];
        var thrown = new TrashThrow(
            1, [new TrashMember(UKey, null, "U", "Unknown")], Me, DateTimeOffset.UnixEpoch, 1);

        var model = await ModelAsync(Scene().WithTrash(new TrashFilter([thrown])));

        Assert.Equal("SANKTIONSTRÄFFAR I GRAFEN", Assert.IsType<LabelGroupHeader>(model.Items[0]).Title);
        var hit = Assert.IsType<LabelRowViewModel>(model.Items[1]);
        Assert.Equal(LabelPlace.Trash, hit.Place);
        Assert.True(hit.HasPlaceMark);
        Assert.False(hit.IsInGraph);
    }

    [Fact]
    public async Task A_label_near_a_victim_says_it_never_reaches_the_library_and_cannot_be_turned()
    {
        _attribution.History =
        [
            Entry(1, W, LabelKind.Victim, "Offret", LabelSource.Own, Me),
            Entry(2, W, LabelKind.Exchange, "Börs A", LabelSource.Own, Me),
        ];

        var model = await ModelAsync(Scene());

        var near = model.Items.OfType<LabelRowViewModel>().Single(row => row.Title == "Börs · Börs A");
        Assert.True(near.IsNearVictim);
        Assert.True(near.IsNoSpread);
        Assert.True(near.ShowsSpread);
        Assert.False(near.CanChangeSpread);
        Assert.Contains(near.Chips, chip => chip.Text == "Sprids inte");
        Assert.Equal("Nära ett offer i grafen: når aldrig biblioteket.", near.SpreadNote);
    }

    [Fact]
    public async Task Taking_back_a_row_with_a_copy_says_the_copy_stays()
    {
        _attribution.History =
        [
            Entry(1, E, LabelKind.Exchange, "Börs A", LabelSource.Own, Me),
            Entry(2, E, LabelKind.Exchange, "Börs A", LabelSource.OtherCase, Colleague),
        ];
        var model = await ModelAsync(Scene());

        model.BeginRetractCommand.Execute(null);
        await model.ConfirmRetractCommand.ExecuteAsync(null);

        Assert.EndsWith(LabelsViewModel.CopyStaysText, Assert.Single(_toaster.Toasts).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_change_still_reads_the_case_again()
    {
        _attribution.History = [Entry(1, E, LabelKind.Exchange, null, LabelSource.Own, Me)];
        _editor.Failure = new InvalidOperationException("Ärendet är stängt.");
        var model = await ModelAsync(Scene());
        var changed = 0;
        model.Changed += (_, _) => changed++;

        await model.ToggleSpreadCommand.ExecuteAsync(null);

        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task A_case_that_cannot_be_read_is_said_honestly()
    {
        _attribution.HistoryFailure = new InvalidDataException("trasig");

        var model = await ModelAsync(Scene());

        Assert.False(model.HasItems);
        Assert.StartsWith("Etiketterna kunde inte läsas", model.EmptyMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_copy_names_the_case_it_was_set_in_and_carries_its_reason()
    {
        var copy = Entry(1, E, LabelKind.Exchange, "Börs A", LabelSource.OtherCase, Colleague);
        var origin = copy.Label.Origin with { CaseNumber = "2026-0098", Reason = "Enligt börsens svar" };
        _attribution.History = [copy with { Label = copy.Label with { Origin = origin } }];

        var model = await ModelAsync(Scene());

        var shown = Assert.Single(model.Selected!.Origins);
        Assert.Equal("Från ärende 2026-0098 · ett annat ärende", shown.CaseText);
        Assert.Equal("Enligt börsens svar", shown.Why);
        Assert.Equal("A. Lind · analytiker · Enhet Väst", shown.Who);
    }

    [Fact]
    public void A_title_is_the_sort_and_the_name()
    {
        Assert.Equal("Mixer", LabelRows.Title(LabelKind.Mixer, " "));
        Assert.Equal("Börs · Börs A", LabelRows.Title(LabelKind.Exchange, " Börs A "));
    }

    private async Task<LabelsViewModel> ModelAsync(GraphScene scene)
    {
        _session.Open(new TestCase(CaseNumber));
        var model = new LabelsViewModel(
            _attribution, _library, _sanctions, new TestSuppliers(), _editor, _session, new InlineUi(), _toaster,
            TimeZoneInfo.Utc);
        model.Update(scene);
        await model.Reading;
        return model;
    }

    private static LabelHistoryEntry Entry(
        long sequence,
        BitcoinAddress address,
        LabelKind kind,
        string? name,
        LabelSource source,
        OperatorProfile who,
        string sourceName = "egen",
        LabelRetraction? retraction = null)
    {
        var original = source == LabelSource.OtherCase ? LabelSource.Own : source;
        var origin = new LabelOrigin(
            "origin-" + who.Name, source, original, sourceName, "1", who, Set,
            source == LabelSource.OtherCase ? null : "Sett", null);
        var subject = LabelSubject.ForAddress(address);
        var entry = new LabelEntry(sequence, subject, kind, name, origin, false, sequence);
        return new LabelHistoryEntry(entry, retraction);
    }

    private static LibraryLabel Library(
        BitcoinAddress address, LabelKind kind, string name, IReadOnlyList<string> cases) =>
        new(
            LabelSubject.ForAddress(address), kind, name,
            new LabelOrigin("lib-" + name, LabelSource.Own, LabelSource.Own, "egen", "1", Colleague, Set, "Sett", null),
            cases);
}
