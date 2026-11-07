using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// Taking back a link to another chain (sketch AN1b): a new sealed record with an optional reason, the link kept in the
/// history, and a link taken back made anew instead of taken back again.
/// </summary>
public sealed class LinkHistoryTests : IDisposable
{
    private static readonly ChainTransactionRef EthereumTx = new(Chain.Ethereum, "0x" + new string('a', 64));

    private readonly CaseHarness _harness = new();
    private readonly Flows _flows = new();

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task A_link_is_taken_back_without_a_reason_and_stays_in_the_history(string? reason)
    {
        await _flows.ExpandInto(_harness);
        var link = _harness.Service.LinkCrossChain(
            _harness.Case, new Outpoint(_flows.Peel3.Txid, 1), EthereumTx, "Troligt.");

        Assert.True(_harness.Service.UnlinkCrossChain(_harness.Case, link, reason));

        var entry = Assert.Single(_harness.Service.GetCrossChainHistory(_harness.Case));
        Assert.Equal(link, entry.Link);
        Assert.True(entry.IsTakenBack);
        Assert.Null(entry.Unlink!.Reason);
        var journal = _harness.Case.ReadJournal().Last();
        Assert.Equal(TracingJournalKinds.CrossChainUnlinked, journal.Kind);
        Assert.Equal(journal.Sequence, entry.Unlink.JournalSequence);
        Assert.Empty((await _harness.Load()).CrossChainLinks);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task The_history_keeps_the_reason_and_a_link_made_anew_after_it()
    {
        await _flows.ExpandInto(_harness);
        var coin = new Outpoint(_flows.Peel3.Txid, 1);
        var first = _harness.Service.LinkCrossChain(_harness.Case, coin, EthereumTx, "Först.");
        _harness.Service.UnlinkCrossChain(_harness.Case, first, "Fel tx, beloppet stämmer inte.");
        var before = _harness.Case.ReadJournal().Count;

        Assert.False(_harness.Service.UnlinkCrossChain(_harness.Case, first, null));
        Assert.Equal(before, _harness.Case.ReadJournal().Count);
        var anew = _harness.Service.LinkCrossChain(_harness.Case, coin, EthereumTx, "Rätt nu.");

        _harness.Reopen();

        var history = _harness.Service.GetCrossChainHistory(_harness.Case);
        Assert.Equal([first.Sequence, anew.Sequence], history.Select(entry => entry.Link.Sequence));
        Assert.Equal("Fel tx, beloppet stämmer inte.", history[0].Unlink!.Reason);
        Assert.False(history[1].IsTakenBack);
        Assert.Equal(anew, Assert.Single((await _harness.Load()).CrossChainLinks));
    }

    [Fact]
    public async Task A_reason_that_is_too_long_is_refused()
    {
        await _flows.ExpandInto(_harness);
        var link = _harness.Service.LinkCrossChain(
            _harness.Case, new Outpoint(_flows.Peel3.Txid, 1), EthereumTx, "Troligt.");

        Assert.Throws<ArgumentException>(
            () => _harness.Service.UnlinkCrossChain(_harness.Case, link, new string('x', 1_001)));
        Assert.False(Assert.Single(_harness.Service.GetCrossChainHistory(_harness.Case)).IsTakenBack);
    }
}
