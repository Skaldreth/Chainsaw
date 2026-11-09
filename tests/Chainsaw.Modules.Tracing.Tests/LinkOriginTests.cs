using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// A link to another chain that an approved suggestion makes (<see cref="ICrossChainLinks"/>): sealed in the caller's
/// transaction with its origin (the analysis with its version and the decision), read back with it, held to the same
/// rules as a link by hand, and taken back by its origin.
/// </summary>
public sealed class LinkOriginTests : IDisposable
{
    private static readonly ChainTransactionRef TronTx = new(Chain.Tron, new string('b', 64));

    private static readonly CrossChainOrigin Origin = new("cross-chain v1", 41);

    private readonly CaseHarness _harness = new();
    private readonly Flows _flows = new();

    public void Dispose() => _harness.Dispose();

    private ICrossChainLinks Links => _harness.Links;

    [Fact]
    public async Task An_approved_link_is_sealed_with_its_origin_and_an_optional_reason()
    {
        await _flows.ExpandInto(_harness);
        var coin = new Outpoint(_flows.Peel3.Txid, 1);

        var link = _harness.Case.Write(transaction =>
            Links.Link(transaction, new CrossChainLinkDraft(coin, null, TronTx, null, Origin)));

        _harness.Reopen();
        var stored = Assert.Single(_harness.Service.GetCrossChainHistory(_harness.Case)).Link;
        Assert.Equal(link.Sequence, stored.Sequence);
        Assert.Equal(Origin, stored.Origin);
        Assert.Equal(string.Empty, stored.Reason);
        Assert.Equal(coin, stored.From);
        var journal = _harness.Case.ReadJournal().Last();
        Assert.Equal(TracingJournalKinds.CrossChainLinked, journal.Kind);
        Assert.Contains("godkänt förslag, cross-chain v1", journal.Summary, StringComparison.Ordinal);
        Assert.Single((await _harness.Load()).CrossChainLinks);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task An_approved_link_is_not_taken_back_alone_by_hand()
    {
        await _flows.ExpandInto(_harness);
        var link = _harness.Case.Write(transaction => Links.Link(
            transaction, new CrossChainLinkDraft(new Outpoint(_flows.Peel3.Txid, 1), null, TronTx, null, Origin)));

        Assert.Throws<ArgumentException>(() => _harness.Service.UnlinkCrossChain(_harness.Case, link, null));

        Assert.False(Assert.Single(_harness.Service.GetCrossChainHistory(_harness.Case)).IsTakenBack);
        var trace = _harness.Service.Trace(await _harness.Load(), new TraceRequest(
            TracingMethodKind.Fifo, [new TraceSeed(new Outpoint(_flows.Peel3.Txid, 1))]));
        var end = Assert.Single(trace.Ends, item => item.Reason == TrailEndReason.OtherChain);
        Assert.Contains("godkänt förslag ur cross-chain v1", end.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_link_by_hand_has_no_origin()
    {
        await _flows.ExpandInto(_harness);

        _harness.Service.LinkCrossChain(_harness.Case, new Outpoint(_flows.Peel3.Txid, 1), TronTx, "För hand.");

        Assert.Null(Assert.Single(_harness.Service.GetCrossChainHistory(_harness.Case)).Link.Origin);
    }

    [Fact]
    public async Task A_coin_that_already_continues_is_not_linked_again()
    {
        await _flows.ExpandInto(_harness);
        var coin = new Outpoint(_flows.Peel3.Txid, 1);
        _harness.Service.LinkCrossChain(_harness.Case, coin, TronTx, "För hand.");

        Assert.Throws<ArgumentException>(() => _harness.Case.Write(transaction =>
            Links.Link(transaction, new CrossChainLinkDraft(coin, null, TronTx, null, Origin))));
        Assert.Throws<ArgumentException>(() => _harness.Case.Write(transaction =>
            Links.Link(transaction, new CrossChainLinkDraft(null, null, TronTx, null, Origin))));
        Assert.Single(_harness.Service.GetCrossChainHistory(_harness.Case));
    }

    [Fact]
    public async Task Only_the_links_the_origins_decision_made_are_taken_back()
    {
        await _flows.ExpandInto(_harness);
        _harness.Case.Write(transaction => Links.Link(
            transaction, new CrossChainLinkDraft(new Outpoint(_flows.Peel3.Txid, 1), null, TronTx, null, Origin)));
        _harness.Service.LinkCrossChain(_harness.Case, new Outpoint(_flows.Peel3.Txid, 0), TronTx, "För hand.");

        var other = _harness.Case.Write(transaction => Links.UnlinkMadeBy(transaction, "cross-chain v1", 42, null));
        var taken = _harness.Case.Write(transaction =>
            Links.UnlinkMadeBy(transaction, "cross-chain v1", 41, "Fel kandidat."));

        Assert.Equal(0, other);
        Assert.Equal(1, taken);
        var history = _harness.Service.GetCrossChainHistory(_harness.Case);
        Assert.True(history.Single(entry => entry.Link.Origin is not null).IsTakenBack);
        Assert.False(history.Single(entry => entry.Link.Origin is null).IsTakenBack);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Theory]
    [InlineData("cross-chain v1#12", "cross-chain v1", 12L)]
    [InlineData("a#b#3", "a#b", 3L)]
    public void An_origin_reads_back_as_it_was_written(string text, string analysis, long decision)
    {
        var origin = CrossChainOrigin.Parse(text)!;

        Assert.Equal(new CrossChainOrigin(analysis, decision), origin);
        Assert.Equal(text, origin.ToString());
        Assert.Null(CrossChainOrigin.Parse(null));
        Assert.Throws<FormatException>(() => CrossChainOrigin.Parse("utan nummer"));
    }
}
