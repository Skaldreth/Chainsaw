using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Attribution.Tests.Synthetic;

namespace Chainsaw.Modules.Attribution.Tests;

/// <summary>
/// Labels on transactions: sealed with a line in the journal, taken back the same way, and a label that says where
/// the money goes ends the trail at the transaction, resting on the label.
/// </summary>
public sealed class TransactionLabelTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void A_swap_label_on_a_transaction_ends_the_trail_there()
    {
        var transaction = Tx("swap").In(Wpkh("from"), 100_000).Out(Wpkh("vault"), 99_000).Build();
        _harness.TransactionLabels.Add(_harness.Case, new TransactionLabelDraft(
            Chain.Bitcoin, transaction.Txid.Value.ToUpperInvariant(), LabelKind.SwapBridge, "Syntetisk swap",
            "Minnet i tx:en", null));

        var hint = Assert.Single(_harness.Classifier.ClassifyTransaction(_harness.Case, transaction));

        Assert.Equal(TrailEndReason.OtherChain, hint.Reason);
        Assert.Contains("etikett på tx", hint.Source, StringComparison.Ordinal);
        Assert.Contains("syntetisk swap", hint.Explanation, StringComparison.Ordinal);
        Assert.Contains("Minnet i tx:en", hint.Explanation, StringComparison.Ordinal);
        Assert.Equal(AttributionJournalKinds.TransactionLabelAdded, _harness.Case.ReadJournal().Last().Kind);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_label_taken_back_no_longer_ends_the_trail_but_stays_in_the_history()
    {
        var transaction = Tx("taken-back").In(Wpkh("a"), 50_000).Out(Wpkh("b"), 49_000).Build();
        var label = _harness.TransactionLabels.Add(_harness.Case, new TransactionLabelDraft(
            Chain.Bitcoin, transaction.Txid.Value, LabelKind.Mixer, null, null, null));

        Assert.True(_harness.TransactionLabels.Retract(_harness.Case, label.Sequence, "Fel tx"));
        Assert.False(_harness.TransactionLabels.Retract(_harness.Case, label.Sequence, null));

        Assert.Empty(_harness.Classifier.ClassifyTransaction(_harness.Case, transaction));
        var stored = Assert.Single(_harness.TransactionLabels.List(_harness.Case));
        Assert.False(stored.IsActive);
        Assert.Equal("Fel tx", stored.Retraction!.Reason);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_label_that_does_not_end_the_trail_gives_no_hint()
    {
        var transaction = Tx("unknown").In(Wpkh("c"), 10_000).Out(Wpkh("d"), 9_000).Build();
        _harness.TransactionLabels.Add(_harness.Case, new TransactionLabelDraft(
            Chain.Bitcoin, transaction.Txid.Value, LabelKind.Unknown, null, null, null));

        Assert.Empty(_harness.Classifier.ClassifyTransaction(_harness.Case, transaction));
    }

    [Theory]
    [InlineData(Chain.Bitcoin, "abc")]
    [InlineData(Chain.Ethereum, "abc")]
    [InlineData(Chain.Tron, "abc")]
    public void An_id_that_is_not_valid_on_its_chain_is_refused(Chain chain, string id)
    {
        Assert.Throws<ArgumentException>(() => _harness.TransactionLabels.Add(
            _harness.Case, new TransactionLabelDraft(chain, id, LabelKind.SwapBridge, null, null, null)));
    }

    [Fact]
    public void An_ethereum_hash_is_kept_in_lower_case()
    {
        var hash = "0x" + new string('A', 64);

        var label = _harness.TransactionLabels.Add(_harness.Case, new TransactionLabelDraft(
            Chain.Ethereum, hash, LabelKind.SwapBridge, null, null, null));

        Assert.Equal(hash.ToLowerInvariant(), label.Id);
        Assert.Single(_harness.TransactionLabels.For(_harness.Case, Chain.Ethereum, hash));
    }

    [Fact]
    public void A_victim_is_never_what_a_transaction_is()
    {
        Assert.Throws<ArgumentException>(() => _harness.TransactionLabels.Add(_harness.Case,
            new TransactionLabelDraft(Chain.Bitcoin, Txid("v").Value, LabelKind.Victim, null, null, null)));
    }
}
