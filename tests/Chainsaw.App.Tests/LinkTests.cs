using Chainsaw.App.Links;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

public sealed class LinkTests
{
    [Theory]
    [InlineData("chainsaw:bitcoin/{0}")]
    [InlineData("CHAINSAW:BTC/{0}")]
    [InlineData("chainsaw://bitcoin/{0}/")]
    [InlineData("  chainsaw:bitcoin/{0}\n")]
    public void A_link_names_a_bitcoin_start_point(string form)
    {
        Assert.True(ChainsawLink.TryParse(string.Format(form, Samples.Address), out var startPoint));
        Assert.Equal(Chain.Bitcoin, startPoint.Chain);
        Assert.Equal(Samples.Address, startPoint.Value);
    }

    [Fact]
    public void A_link_to_a_tron_tx_names_tron_although_it_looks_like_bitcoin()
    {
        Assert.True(ChainsawLink.TryParse("chainsaw:tron/" + Samples.Txid, out var startPoint));
        Assert.Equal(Chain.Tron, startPoint.Chain);
        Assert.False(startPoint.IsSupported);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bitcoin/x")]
    [InlineData("chainsaw:")]
    [InlineData("chainsaw:bitcoin")]
    [InlineData("chainsaw:bitcoin/")]
    [InlineData("chainsaw:monero/abc")]
    [InlineData("chainsaw:bitcoin/hej")]
    [InlineData("chainsaw:bitcoin/../../etc")]
    [InlineData("chainsaw:bitcoin/abc?open=1")]
    [InlineData("chainsaw:bitcoin/%2Fcmd")]
    [InlineData("chainsaw:/open A1")]
    public void Anything_else_is_refused(string text)
    {
        Assert.False(ChainsawLink.TryParse(text, out _));
    }

    [Fact]
    public void A_bitcoin_value_under_another_chain_is_refused()
    {
        Assert.False(ChainsawLink.TryParse("chainsaw:tron/" + Samples.Address, out _));
        Assert.False(ChainsawLink.TryParse($"chainsaw:bitcoin/{Samples.Address}/{Samples.Txid}", out _));
    }

    [Fact]
    public void A_long_link_is_not_read()
    {
        Assert.False(ChainsawLink.TryParse("chainsaw:bitcoin/" + new string('1', 5000), out _));
    }

    [Fact]
    public void A_link_while_locked_waits_shows_nothing_and_writes_nothing()
    {
        var rig = new GateRig(VaultState.Locked);

        Assert.True(rig.Links.Receive("chainsaw:bitcoin/" + Samples.Address));

        Assert.True(rig.Links.HasWaiting);
        Assert.False(rig.NewCase.IsOpen);
        Assert.Empty(rig.Shell.CommandText);
        Assert.Empty(rig.Cases.Created);
        Assert.Empty(rig.Tracing.Added);
    }

    [Fact]
    public async Task After_the_unlock_a_waiting_link_fills_in_a_new_case()
    {
        var rig = new GateRig(VaultState.Locked);
        rig.Links.Receive("chainsaw:bitcoin/" + Samples.Address);

        await rig.LockScreen.UnlockAsync(new TestEntry("correct horse battery"));

        Assert.False(rig.Links.HasWaiting);
        Assert.True(rig.NewCase.IsOpen);
        Assert.Equal(Samples.Address, rig.NewCase.StartPointsText);
        Assert.Empty(rig.Cases.Created);
    }

    [Fact]
    public async Task With_a_case_open_a_link_waits_in_the_bar_for_Enter()
    {
        var rig = new GateRig(VaultState.Unlocked);
        await rig.Workflow.CreateAsync(new NewCaseRequest("A1", string.Empty, []), null);

        rig.Links.Receive("chainsaw:bitcoin/" + Samples.Address);

        Assert.Equal(Samples.Address, rig.Shell.CommandText);
        Assert.Contains("Enter", rig.Shell.CommandAnswer, StringComparison.Ordinal);
        Assert.Empty(rig.Tracing.Added);

        await rig.Shell.SubmitAsync();
        Assert.Single(rig.Tracing.Added);
    }

    [Fact]
    public void A_link_during_the_first_run_waits_too()
    {
        var rig = new GateRig(VaultState.NotCreated);

        rig.Links.Receive("chainsaw:bitcoin/" + Samples.Address);

        Assert.True(rig.Links.HasWaiting);
        Assert.False(rig.NewCase.IsOpen);
    }

    [Fact]
    public void A_lock_after_a_link_was_offered_empties_the_bar()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Session.Open(new TestCase("A1"));
        rig.Links.Receive("chainsaw:bitcoin/" + Samples.Address);

        rig.Gate.Lock();

        Assert.Empty(rig.Shell.CommandText);
        Assert.False(rig.Links.HasWaiting);
    }

    [Fact]
    public void Text_that_is_not_a_link_is_ignored()
    {
        var rig = new GateRig(VaultState.Unlocked);

        Assert.False(rig.Links.Receive("/lock"));

        Assert.Equal(GateMode.Unlocked, rig.Gate.Mode);
    }
}
