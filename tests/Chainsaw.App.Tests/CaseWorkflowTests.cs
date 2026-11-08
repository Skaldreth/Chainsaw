using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

public sealed class CaseWorkflowTests
{
    [Fact]
    public async Task Opening_a_case_saves_and_closes_the_one_open_before()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Cases.AddFile("A1", 10);
        rig.Cases.AddFile("A2", 5);
        await rig.Workflow.OpenAsync("A1");
        var first = (TestCase)rig.Session.Current!;

        var reply = await rig.Workflow.OpenAsync("A2");

        Assert.False(reply.IsError);
        Assert.Equal(1, first.Disposed);
        Assert.Equal("A2", rig.Session.Current!.Info.CaseNumber);
        Assert.Equal("Ärendet A2 är öppet.", reply.Text);
    }

    [Fact]
    public async Task A_case_that_is_already_open_is_not_opened_again()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Cases.AddFile("A1", 10);
        await rig.Workflow.OpenAsync("A1");
        var open = rig.Session.Current;

        var reply = await rig.Workflow.OpenAsync("a1");

        Assert.False(reply.IsError);
        Assert.Same(open, rig.Session.Current);
        Assert.Single(rig.Cases.Cases);
    }

    [Fact]
    public async Task A_missing_case_is_said_in_words()
    {
        var rig = new GateRig(VaultState.Unlocked);

        var reply = await rig.Workflow.OpenAsync("A9");

        Assert.True(reply.IsError);
        Assert.Equal(CaseTexts.For(CaseError.NotFound), reply.Text);
        Assert.Null(rig.Session.Current);
    }

    [Fact]
    public async Task A_failed_save_of_the_case_open_before_is_said_and_the_next_case_still_opens()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Cases.AddFile("A1", 10);
        rig.Cases.AddFile("A2", 5);
        await rig.Workflow.OpenAsync("A1");
        ((TestCase)rig.Session.Current!).CloseFailure = new IOException("disk");

        var reply = await rig.Workflow.OpenAsync("A2");

        Assert.Contains("A1 gick inte att spara", reply.Text, StringComparison.Ordinal);
        Assert.Equal("A2", rig.Session.Current!.Info.CaseNumber);
    }

    [Fact]
    public async Task Closing_saves_and_says_so()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Cases.AddFile("A1", 10);
        await rig.Workflow.OpenAsync("A1");
        var open = (TestCase)rig.Session.Current!;

        var reply = await rig.Workflow.CloseAsync();

        Assert.Equal("Ärendet A1 sparades och stängdes.", reply.Text);
        Assert.Equal(1, open.Disposed);
        Assert.Null(rig.Session.Current);
        Assert.True((await rig.Workflow.CloseAsync()).IsError);
    }

    [Fact]
    public async Task A_case_opened_while_Chainsaw_locked_is_closed_again()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Cases.AddFile("A1", 10);
        rig.Cases.DuringCreate = null;
        var store = rig.Cases;
        rig.Vault.Set(VaultState.Locked);

        var reply = await rig.Workflow.OpenAsync("A1");

        Assert.True(reply.IsError);
        Assert.Null(rig.Session.Current);
        Assert.True(SpinWait.SpinUntil(() => store.Cases[0].Disposed > 0, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task A_new_case_gets_its_title_its_bitcoin_start_points_and_a_note_for_the_rest()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var request = new NewCaseRequest(
            "2026-0117",
            "Investeringsbedrägeri via chatt",
            [
                ChainStartPoint.Recognise(Samples.Address),
                ChainStartPoint.Recognise(Samples.Txid),
                ChainStartPoint.Recognise(Samples.Tron),
            ]);

        var creation = await rig.Workflow.CreateAsync(request, sharingPassword: null);

        Assert.True(creation.IsCreated);
        Assert.False(creation.Reply.IsError);
        var created = (TestCase)rig.Session.Current!;
        Assert.Equal("Investeringsbedrägeri via chatt", created.Title);
        Assert.Equal(
            [Samples.Address, Samples.Txid],
            rig.Tracing.Added.Select(item => item.StartPoint.ToString()));
        var note = Assert.Single(created.Journal);
        Assert.Equal(AppJournalKinds.StartPointNoted, note.Kind);
        Assert.Contains(Samples.Tron, note.Summary, StringComparison.Ordinal);
        Assert.False(created.HasSharingPassword);
    }

    [Fact]
    public async Task A_start_point_that_is_a_transaction_the_case_lacks_is_fetched_and_one_it_holds_is_not()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Tracing.Graph = GraphSamples.Graph();
        var workflow = new CaseWorkflow(rig.Cases, rig.Tracing, rig.Vault, rig.Session, null, new TestAttribution());
        var refreshed = 0;
        workflow.StartPointAdded += (_, _) => refreshed++;
        var request = new NewCaseRequest(
            "2026-0117",
            string.Empty,
            [
                ChainStartPoint.Recognise(Samples.Address),
                ChainStartPoint.Recognise(Samples.EthereumTx),
                ChainStartPoint.Recognise(GraphSamples.Tx[0].Value),
            ]);

        var creation = await workflow.CreateAsync(request, sharingPassword: null);

        Assert.True(creation.IsCreated);
        var fetched = Assert.Single(rig.Tracing.Expanded);
        Assert.Equal(EthereumTxHash.Parse(Samples.EthereumTx), fetched.EthereumTransaction);
        Assert.Equal(1, refreshed);
    }

    [Fact]
    public async Task The_bitcoin_and_ethereum_start_points_are_checked_against_the_sanction_list()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var attribution = new TestAttribution();
        var workflow = new CaseWorkflow(rig.Cases, rig.Tracing, rig.Vault, rig.Session, null, attribution);
        var request = new NewCaseRequest(
            "2026-0118",
            string.Empty,
            [
                ChainStartPoint.Recognise(Samples.Address),
                ChainStartPoint.Recognise(Samples.Ethereum),
                ChainStartPoint.Recognise(Samples.EthereumTx),
                ChainStartPoint.Recognise(Samples.Tron),
            ]);

        var creation = await workflow.CreateAsync(request, sharingPassword: null);

        Assert.True(creation.IsCreated);
        Assert.Equal(
            [new ChainAddress(Chain.Bitcoin, Samples.Address), new ChainAddress(Chain.Ethereum, Samples.Ethereum)],
            attribution.SanctionChecked);
    }

    [Fact]
    public async Task A_failed_check_for_bitcoin_never_keeps_ethereum_from_being_checked()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var attribution = new TestAttribution { BitcoinSanctionsFail = true };
        var workflow = new CaseWorkflow(rig.Cases, rig.Tracing, rig.Vault, rig.Session, null, attribution);
        var request = new NewCaseRequest(
            "2026-0119",
            string.Empty,
            [ChainStartPoint.Recognise(Samples.Address), ChainStartPoint.Recognise(Samples.Ethereum)]);

        var creation = await workflow.CreateAsync(request, sharingPassword: null);

        Assert.True(creation.IsCreated);
        Assert.Equal([new ChainAddress(Chain.Ethereum, Samples.Ethereum)], attribution.SanctionChecked);
        Assert.Contains("för Bitcoin", creation.Reply.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("för Ethereum", creation.Reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_shared_case_gets_its_sharing_envelope()
    {
        var rig = new GateRig(VaultState.Unlocked);

        await rig.Workflow.CreateAsync(new NewCaseRequest("A1", string.Empty, []), "ett långt lösenord".ToCharArray());

        Assert.Equal("ett långt lösenord", ((TestCase)rig.Session.Current!).SharingPassword);
    }

    [Fact]
    public async Task A_start_point_that_fails_after_the_case_exists_keeps_the_case_open_and_says_so()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Tracing.Failure = new CaseException(CaseError.NoOperatorProfile);

        var creation = await rig.Workflow.CreateAsync(
            new NewCaseRequest("A1", string.Empty, [ChainStartPoint.Recognise(Samples.Address)]), null);

        Assert.True(creation.IsCreated);
        Assert.True(creation.Reply.IsError);
        Assert.Contains("lades inte till", creation.Reply.Text, StringComparison.Ordinal);
        Assert.NotNull(rig.Session.Current);
    }

    [Fact]
    public async Task A_case_number_that_exists_creates_nothing()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Cases.Failure = new CaseException(CaseError.AlreadyExists);

        var creation = await rig.Workflow.CreateAsync(new NewCaseRequest("A1", string.Empty, []), null);

        Assert.False(creation.IsCreated);
        Assert.Equal(CaseTexts.For(CaseError.AlreadyExists), creation.Reply.Text);
    }

    [Fact]
    public async Task A_start_point_is_added_to_the_open_case_once()
    {
        var rig = new GateRig(VaultState.Unlocked);
        await rig.Workflow.CreateAsync(new NewCaseRequest("A1", string.Empty, []), null);
        var startPoint = ChainStartPoint.Recognise(Samples.Address);

        var first = await rig.Workflow.AddStartPointAsync(startPoint);
        var again = await rig.Workflow.AddStartPointAsync(startPoint);

        Assert.Contains("lades till", first.Text, StringComparison.Ordinal);
        Assert.Contains("redan en startpunkt", again.Text, StringComparison.Ordinal);
        Assert.Single(rig.Tracing.Added);
    }

    [Fact]
    public async Task The_case_worked_on_last_is_suggested_first_and_the_open_one_not_at_all()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Cases.AddFile("2026-0100", 60);
        rig.Cases.AddFile("2026-0117", 1);
        rig.Cases.AddFile("2025-0001", 5);
        rig.Cases.AddFile("B7", 2);
        await rig.Workflow.OpenAsync("B7");
        await rig.Workflow.RefreshCasesAsync();

        Assert.Equal(["2026-0117", "2025-0001", "2026-0100"], rig.Workflow.SuggestCaseNumbers(string.Empty));
        Assert.Equal(["2026-0117", "2026-0100"], rig.Workflow.SuggestCaseNumbers("2026"));
    }

    [Fact]
    public async Task A_creation_cut_short_by_a_lock_says_that_the_case_exists_and_what_was_not_done()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Cases.DuringCreate = () => rig.Vault.Lock();
        rig.Tracing.Failure = new CaseException(CaseError.Closed);

        var creation = await rig.Workflow.CreateAsync(
            new NewCaseRequest("A1", string.Empty, [ChainStartPoint.Recognise(Samples.Address)]), null);

        Assert.False(creation.IsCreated);
        Assert.Contains("A1 skapades", creation.Reply.Text, StringComparison.Ordinal);
        Assert.Contains("lades inte till", creation.Reply.Text, StringComparison.Ordinal);
        Assert.Contains("/open A1", creation.Reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_case_forgotten_at_a_lock_is_closed_before_it_is_opened_again()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Cases.AddFile("A1", 1);
        await rig.Workflow.OpenAsync("A1");
        var first = (TestCase)rig.Session.Current!;
        rig.Session.Forget();

        await rig.Workflow.OpenAsync("A1");

        Assert.Equal(1, first.Disposed);
        Assert.NotSame(first, rig.Session.Current);
    }

    [Fact]
    public void Closing_at_exit_waits_for_the_save()
    {
        var session = new CaseSession();
        var open = new TestCase("A1");
        session.Open(open);

        Assert.True(session.CloseNow());

        Assert.Equal(1, open.Disposed);
        Assert.Null(session.Current);
    }
}
