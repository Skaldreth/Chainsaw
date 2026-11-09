using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Tracing.Tests.Synthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// The clustering's input at a transaction the source no longer knows (sketch HB1a): an expansion leaves a gone
/// transaction out once the answers settle that it is gone, so that what rests on it is held back by itself, never
/// taken back; one endpoint's word alone leaves it in, and a transaction that comes back is analysed again. On every
/// UTXO chain with block changes: Bitcoin and Litecoin.
/// </summary>
public sealed class GoneClusteringTests : IDisposable
{
    private readonly CaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData(nameof(Chain.Bitcoin))]
    [InlineData(nameof(Chain.Litecoin))]
    public async Task A_settled_gone_transaction_is_left_out_and_its_conclusion_is_held_back_but_still_counts(
        string chainName)
    {
        var (source, a, b, gone) = Setup(chainName);
        await _harness.Expand(gone);
        Assert.Contains(b, _harness.Clustering.GetCluster(_harness.Case, a).Addresses);
        var stored = Count();

        MarkGone(source, gone, corroborated: true);
        var graph = (await _harness.Expand(a)).Graph;

        var held = Assert.Single(_harness.Clustering.GetHeldBack(_harness.Case));
        Assert.Equal((gone.Txid, gone.Txid), (held.Txid, Assert.Single(held.Missing)));
        Assert.True(held.IsWaiting);
        Assert.Contains(b, _harness.Clustering.GetCluster(_harness.Case, a).Addresses);
        Assert.Equal(stored, Count());
        Assert.True(graph.Gone.ContainsKey(gone.Txid));
        Assert.True(graph.Transactions.ContainsKey(gone.Txid));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task One_endpoints_word_alone_holds_nothing_back()
    {
        var (source, a, _, gone) = Setup(nameof(Chain.Bitcoin));
        await _harness.Expand(gone);

        MarkGone(source, gone, corroborated: false);
        await _harness.Expand(a);

        Assert.Empty(_harness.Clustering.GetHeldBack(_harness.Case));
    }

    [Fact]
    public async Task A_gone_transaction_that_comes_back_is_analysed_again_and_held_back_no_more()
    {
        var (source, a, b, gone) = Setup(nameof(Chain.Bitcoin));
        await _harness.Expand(gone);
        MarkGone(source, gone, corroborated: true);
        await _harness.Expand(a);
        Assert.Single(_harness.Clustering.GetHeldBack(_harness.Case));
        var stored = Count();

        source.Changes.Clear();
        await _harness.Expand(a);

        Assert.Empty(_harness.Clustering.GetHeldBack(_harness.Case));
        Assert.Contains(b, _harness.Clustering.GetCluster(_harness.Case, a).Addresses);
        Assert.Equal(stored, Count());
    }

    [Fact]
    public async Task Letting_a_gone_transactions_conclusion_stand_is_respected_by_the_next_expansion()
    {
        var (source, a, b, gone) = Setup(nameof(Chain.Bitcoin));
        await _harness.Expand(gone);
        MarkGone(source, gone, corroborated: true);
        await _harness.Expand(a);

        Assert.NotNull(_harness.Clustering.DecideHeldBack(_harness.Case, gone.Txid, ConclusionChoice.Keep, null));
        await _harness.Expand(b);

        Assert.False(Assert.Single(_harness.Clustering.GetHeldBack(_harness.Case)).IsWaiting);
        Assert.Contains(b, _harness.Clustering.GetCluster(_harness.Case, a).Addresses);
    }

    [Fact]
    public async Task Letting_a_gone_transactions_conclusion_go_splits_the_wallet_and_seals_nothing_new()
    {
        var (source, a, b, gone) = Setup(nameof(Chain.Bitcoin));
        await _harness.Expand(gone);
        MarkGone(source, gone, corroborated: true);
        await _harness.Expand(a);
        var stored = Count();

        Assert.NotNull(_harness.Clustering.DecideHeldBack(_harness.Case, gone.Txid, ConclusionChoice.Release, null));
        await _harness.Expand(b);

        Assert.Empty(_harness.Clustering.GetHeldBack(_harness.Case));
        Assert.DoesNotContain(b, _harness.Clustering.GetCluster(_harness.Case, a).Addresses);
        Assert.Equal(stored, Count());
        Assert.True(_harness.Verify().IsIntact);
    }

    /// <summary>
    /// Gives the chain's fake source, two addresses on it, and a transaction there that spends their coins together,
    /// so that common inputs merge them; the source knows the transaction.
    /// </summary>
    private (FakeSource Source, BitcoinAddress A, BitcoinAddress B, BitcoinTransaction Gone) Setup(string chainName)
    {
        var litecoin = chainName == nameof(Chain.Litecoin);
        var source = litecoin ? _harness.Litecoin : _harness.Source;
        var (a, b, shop) = litecoin
            ? (LitecoinPkh("ltc-wallet-a"), LitecoinPkh("ltc-wallet-b"), LitecoinPkh("ltc-shop"))
            : (Wpkh("wallet-a"), Wpkh("wallet-b"), Wpkh("shop"));
        var gone = Tx(litecoin ? Chain.Litecoin : Chain.Bitcoin, "gone-consolidation", 900_001)
            .In(a, 300_000).In(b, 200_000).Out(shop, 499_000).Build();
        source.Add(gone);
        return (source, a, b, gone);
    }

    /// <summary>Says, as Chains would from the sealed answers, that the transaction left the chain.</summary>
    private static void MarkGone(FakeSource source, BitcoinTransaction gone, bool corroborated)
    {
        var before = new BlockSighting(900_001, gone.Block!.Hash.Value, DateTimeOffset.UnixEpoch, 10)
        {
            Endpoint = "esplora.test",
        };
        var now = new BlockSighting(null, null, DateTimeOffset.UnixEpoch.AddHours(1), 60) { Endpoint = "esplora.test" };
        source.Changes.Add(new BlockChange(gone.Txid, BlockChangeKind.Gone, before, now)
        {
            Chain = gone.Txid.Chain, IsCorroborated = corroborated, GoneHosts = ["esplora.test"],
        });
    }

    /// <summary>Counts the sealed conclusions in the case.</summary>
    private long Count() => _harness.Case.Read(reader =>
    {
        using var command = reader.CreateCommand("SELECT COUNT(*) FROM clustering_evidence");
        return (long)command.ExecuteScalar()!;
    });
}
