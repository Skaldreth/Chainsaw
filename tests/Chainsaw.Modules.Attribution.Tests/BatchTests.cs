using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using static Chainsaw.Modules.Attribution.Tests.Synthetic;

namespace Chainsaw.Modules.Attribution.Tests;

/// <summary>
/// Stage 15: the labels and the ends of the trail for many addresses at once give what one address at a time gives,
/// reading the case once.
/// </summary>
public sealed class BatchTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Many_addresses_at_once_get_the_same_labels_as_one_at_a_time()
    {
        var first = Wpkh("batch-cluster-a");
        var second = Wpkh("batch-cluster-b");
        var alone = Wpkh("batch-alone");
        var outsider = Wpkh("batch-outsider");
        _harness.Clustering.Analyse(
            _harness.Case,
            [Tx("batch-together").In(first, 1_000_000).In(second, 2_000_000).Out(Wpkh("paid"), 2_990_000).Build()]);
        _harness.Own(LabelSubject.ForCluster(first), LabelKind.Exchange, "Börsen AB");
        _harness.Own(LabelSubject.ForAddress(alone), LabelKind.Mixer);
        _harness.Own(LabelSubject.ForAddress(second), LabelKind.Merchant, "Butiken");
        Chainsaw.Modules.Chains.Contracts.BitcoinAddress[] addresses = [first, second, alone, outsider, first];

        var batch = _harness.Labels.GetLabels(_harness.Case, addresses);

        foreach (var address in new[] { first, second, alone })
        {
            Assert.Equal(Keys(_harness.Labels.GetLabels(_harness.Case, address)), Keys(batch[address]));
        }

        Assert.False(batch.ContainsKey(outsider));
        Assert.Equal(2, batch[second].Count);
    }

    private static List<string> Keys(IEnumerable<Label> labels) =>
    [
        .. labels.Select(label =>
            $"{label.Subject}|{label.Kind}|{label.Name}|{string.Join(',', label.Entries.Select(e => e.Sequence))}"),
    ];

    [Fact]
    public void Without_labels_nothing_is_read_from_the_clusters()
    {
        Assert.Empty(_harness.Labels.GetLabels(_harness.Case, [Wpkh("nothing-a"), Wpkh("nothing-b")]));
    }

    [Fact]
    public void The_ends_of_many_addresses_at_once_are_the_ends_one_at_a_time()
    {
        var exchange = Wpkh("batch-end-exchange");
        var victim = Wpkh("batch-end-victim");
        _harness.Own(LabelSubject.ForAddress(exchange), LabelKind.Exchange);
        _harness.Own(LabelSubject.ForAddress(victim), LabelKind.Victim);
        ITrailEndClassifier classifier = _harness.Classifier;

        var ends = classifier.ClassifyAddresses(_harness.Case, [exchange, victim]);

        var hint = Assert.Single(ends[exchange]);
        Assert.Equal(TrailEndReason.Exchange, hint.Reason);
        Assert.Equal(classifier.ClassifyAddress(_harness.Case, exchange), ends[exchange]);
        Assert.False(ends.ContainsKey(victim)); // a victim's label does not end the trail
    }
}
