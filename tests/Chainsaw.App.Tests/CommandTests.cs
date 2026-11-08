using Chainsaw.App.Commands;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel.Commands;

namespace Chainsaw.App.Tests;

public sealed class CommandTests
{
    [Fact]
    public async Task Help_lists_every_command()
    {
        var reply = await new GateRig(VaultState.Unlocked).Commands.RunAsync("/help");

        foreach (var name in new[] { "/new", "/open", "/close", "/verify", "/lock", "/help" })
        {
            Assert.Contains(name, reply.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task An_unknown_command_is_a_short_error()
    {
        var reply = await new GateRig(VaultState.Unlocked).Commands.RunAsync("/hjälp");

        Assert.True(reply.IsError);
        Assert.Contains("/help", reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_command_that_throws_is_answered_not_crashed()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var registry = new CommandRegistry([new Throwing()], new StartPointInput(rig.Workflow, rig.Session, rig.NewCase));

        var reply = await registry.RunAsync("/boom");

        Assert.True(reply.IsError);
        Assert.Empty(registry.Complete("/boom x"));
    }

    [Fact]
    public void Two_commands_with_the_same_name_are_refused()
    {
        Assert.Throws<ArgumentException>(() => new CommandRegistry([new Throwing(), new Throwing()], null!));
    }

    [Fact]
    public async Task Tab_completes_a_command_name_and_then_a_case_number_worked_on_last_first()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Cases.AddFile("2026-0100", 60);
        rig.Cases.AddFile("2026-0117", 1);
        await rig.Workflow.RefreshCasesAsync();
        var shell = rig.Shell;

        shell.CommandText = "/op";
        shell.Complete();
        Assert.Equal("/open", shell.CommandText);

        shell.CommandText = "/open ";
        Assert.Equal("/open <ärendenummer> · Tab: 2026-0117", shell.ShownAbove);
        shell.Complete();
        Assert.Equal("/open 2026-0117", shell.CommandText);
        shell.Complete();
        Assert.Equal("/open 2026-0100", shell.CommandText);
        shell.Complete();
        Assert.Equal("/open 2026-0117", shell.CommandText);
    }

    [Fact]
    public async Task Open_without_a_number_suggests_the_case_worked_on_last()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Cases.AddFile("2026-0117", 1);
        await rig.Workflow.RefreshCasesAsync();

        var reply = await rig.Commands.RunAsync("/open");

        Assert.True(reply.IsError);
        Assert.Contains("2026-0117", reply.Text, StringComparison.Ordinal);
        Assert.Null(rig.Session.Current);
    }

    [Fact]
    public async Task Open_and_close_through_the_bar_and_the_title_bar_follows()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Cases.AddFile("A1", 1);
        var shell = rig.Shell;

        shell.CommandText = "/open A1";
        await shell.SubmitAsync();
        Assert.Equal("A1", shell.CaseTitle);
        Assert.Empty(shell.CommandText);
        Assert.False(shell.IsAnswerError);

        shell.CommandText = "/close";
        await shell.SubmitAsync();
        Assert.Equal(ShellViewModel.NoCaseText, shell.CaseTitle);
    }

    [Fact]
    public async Task A_failed_command_keeps_the_text_to_correct()
    {
        var shell = new GateRig(VaultState.Unlocked).Shell;
        shell.CommandText = "/open X9";

        await shell.SubmitAsync();

        Assert.Equal("/open X9", shell.CommandText);
        Assert.True(shell.IsAnswerError);
    }

    [Fact]
    public async Task New_opens_the_panel_with_the_number()
    {
        var rig = new GateRig(VaultState.Unlocked);

        await rig.Commands.RunAsync("/new 2026-0117");

        Assert.True(rig.NewCase.IsOpen);
        Assert.Equal("2026-0117", rig.NewCase.CaseNumber);
    }

    [Fact]
    public async Task Lock_locks_Chainsaw()
    {
        var rig = new GateRig(VaultState.Unlocked);

        await rig.Commands.RunAsync("/LOCK");

        Assert.Equal(GateMode.Locked, rig.Gate.Mode);
        Assert.Equal(1, rig.Vault.LockCalls);
    }

    [Fact]
    public async Task Verify_says_that_nothing_has_changed()
    {
        var rig = new GateRig(VaultState.Unlocked);
        Assert.True((await rig.Commands.RunAsync("/verify")).IsError);
        await rig.Workflow.CreateAsync(new NewCaseRequest("A1", string.Empty, []), null);

        var reply = await rig.Commands.RunAsync("/verify");

        Assert.False(reply.IsError);
        Assert.Equal("Inget har ändrats: 1 kedjor och 3 poster stämmer.", reply.Text);
    }

    [Fact]
    public async Task Verify_says_where_a_chain_breaks()
    {
        var rig = new GateRig(VaultState.Unlocked);
        await rig.Workflow.CreateAsync(new NewCaseRequest("A1", string.Empty, []), null);
        rig.Verifier.Result = new CaseVerification(
            DateTimeOffset.UnixEpoch,
            [CaseProblem.AnchorsReplaced],
            [
                new ChainVerification("cases.journal", 12, 6, "ab", new ChainBreak(7, ChainBreakReason.RecordChanged)),
                new ChainVerification("chains.raw_responses", 3, 3, "cd", null),
            ]);

        var reply = await rig.Commands.RunAsync("/verify");

        Assert.True(reply.IsError);
        var lines = reply.Text.Split(Environment.NewLine);
        Assert.Equal(2, lines.Length);
        Assert.Equal("Kedjan cases.journal bryts vid post 7: posten eller en post före den har ändrats.", lines[1]);
    }

    [Fact]
    public async Task An_address_without_a_slash_becomes_a_start_point_in_the_open_case()
    {
        var rig = new GateRig(VaultState.Unlocked);
        await rig.Workflow.CreateAsync(new NewCaseRequest("A1", string.Empty, []), null);

        var reply = await rig.Commands.RunAsync(Samples.Address);

        Assert.False(reply.IsError);
        Assert.Single(rig.Tracing.Added);
    }

    [Fact]
    public async Task An_address_with_no_case_open_fills_in_a_new_case()
    {
        var rig = new GateRig(VaultState.Unlocked);

        await rig.Commands.RunAsync(Samples.Txid);

        Assert.True(rig.NewCase.IsOpen);
        Assert.Equal(Samples.Txid, rig.NewCase.Rows.Single().StartPoint.Value);
        Assert.Empty(rig.Tracing.Added);
    }

    [Fact]
    public async Task A_tron_address_is_honestly_not_built_and_other_text_is_not_searched()
    {
        var rig = new GateRig(VaultState.Unlocked);

        var tron = await rig.Commands.RunAsync(Samples.Tron);
        var text = await rig.Commands.RunAsync("Börs A");

        Assert.Equal("TRON är inte byggt än, så det kan inte bli en startpunkt.", tron.Text);
        Assert.Contains("inte byggd än", text.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_mistyped_number_does_not_close_the_open_case()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Cases.AddFile("A1", 1);
        await rig.Workflow.OpenAsync("A1");
        var open = (TestCase)rig.Session.Current!;

        var reply = await rig.Commands.RunAsync("/open A9");

        Assert.True(reply.IsError);
        Assert.Same(open, rig.Session.Current);
        Assert.Equal(0, open.Disposed);
    }

    [Fact]
    public async Task A_new_case_with_the_open_cases_number_does_not_close_it()
    {
        var rig = new GateRig(VaultState.Unlocked);
        await rig.Workflow.CreateAsync(new NewCaseRequest("A1", string.Empty, []), null);
        var open = (TestCase)rig.Session.Current!;

        var creation = await rig.Workflow.CreateAsync(new NewCaseRequest("a1", string.Empty, []), null);

        Assert.False(creation.IsCreated);
        Assert.Equal(CaseTexts.For(CaseError.AlreadyExists), creation.Reply.Text);
        Assert.Equal(0, open.Disposed);
    }

    [Fact]
    public async Task A_tron_value_written_with_its_chain_is_never_taken_for_bitcoin()
    {
        var rig = new GateRig(VaultState.Unlocked);
        await rig.Workflow.CreateAsync(new NewCaseRequest("A1", string.Empty, []), null);

        var reply = await rig.Commands.RunAsync("tron:" + Samples.Txid);

        Assert.True(reply.IsError);
        Assert.Contains("TRON", reply.Text, StringComparison.Ordinal);
        Assert.Empty(rig.Tracing.Added);
    }

    /// <summary>A command that fails in an unexpected way.</summary>
    private sealed class Throwing : IShellCommand
    {
        public string Name => "boom";

        public string Usage => "/boom";

        public string Summary => "Kastar";

        public IReadOnlyList<string> Suggest(string argument) => throw new InvalidOperationException();

        public Task<CommandReply> RunAsync(string argument, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();
    }
}
