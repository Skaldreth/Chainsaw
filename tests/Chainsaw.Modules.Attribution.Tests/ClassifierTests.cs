using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using static Chainsaw.Modules.Attribution.Tests.Synthetic;

namespace Chainsaw.Modules.Attribution.Tests;

public sealed class ClassifierTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData(LabelKind.Exchange, TrailEndReason.Exchange)]
    [InlineData(LabelKind.NoKycExchange, TrailEndReason.Exchange)]
    [InlineData(LabelKind.NestedService, TrailEndReason.Exchange)]
    [InlineData(LabelKind.OtcP2P, TrailEndReason.Exchange)]
    [InlineData(LabelKind.CryptoAtm, TrailEndReason.Exchange)]
    [InlineData(LabelKind.CryptoCard, TrailEndReason.Exchange)]
    [InlineData(LabelKind.Gambling, TrailEndReason.Service)]
    [InlineData(LabelKind.Merchant, TrailEndReason.Service)]
    [InlineData(LabelKind.GiftCard, TrailEndReason.Service)]
    [InlineData(LabelKind.Darknet, TrailEndReason.Service)]
    [InlineData(LabelKind.EscrowMarket, TrailEndReason.Service)]
    [InlineData(LabelKind.Mixer, TrailEndReason.Mixer)]
    [InlineData(LabelKind.CoinJoin, TrailEndReason.CoinJoin)]
    [InlineData(LabelKind.SwapBridge, TrailEndReason.OtherChain)]
    public void A_label_ends_the_trail_where_the_money_reaches_a_service(LabelKind kind, TrailEndReason reason)
    {
        var address = Wpkh("end-" + kind);
        _harness.Own(LabelSubject.ForAddress(address), kind, "Tjänsten");

        var hint = Assert.Single(_harness.Classifier.ClassifyAddress(_harness.Case, address));

        Assert.Equal(reason, hint.Reason);
        Assert.Contains("egen v1", hint.Source, StringComparison.Ordinal);
        Assert.StartsWith("Bedömning, inte ett faktum", hint.Explanation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LabelKind.Victim)]
    [InlineData(LabelKind.Unknown)]
    public void A_label_on_an_owner_does_not_end_the_trail(LabelKind kind)
    {
        var address = Wpkh("owner-" + kind);
        _harness.Own(LabelSubject.ForAddress(address), kind);

        Assert.Empty(_harness.Classifier.ClassifyAddress(_harness.Case, address));
    }

    [Fact]
    public void A_sanction_from_a_list_does_not_end_the_trail()
    {
        var address = Wpkh("owner-sanctioned");
        _harness.Sanctions.Import(new SdnFile().Xbt("9001", "SYNTETISK", address.Value).Stream());
        Assert.Single(_harness.Labels.CheckSanctions(_harness.Case, [address]).Added);

        Assert.Empty(_harness.Classifier.ClassifyAddress(_harness.Case, address));
    }

    [Fact]
    public void Every_kind_either_ends_the_trail_for_real_or_not_at_all()
    {
        foreach (var kind in Enum.GetValues<LabelKind>())
        {
            if (LabelTrailEnds.EndFor(kind) is { } reason)
            {
                Assert.True(reason.EndsTrail());
            }
        }
    }

    [Fact]
    public void A_label_taken_back_no_longer_ends_the_trail()
    {
        var address = Wpkh("retracted-end");
        var label = _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange);
        _harness.Labels.Retract(_harness.Case, label.Sequence, "Fel");

        Assert.Empty(_harness.Classifier.ClassifyAddress(_harness.Case, address));
    }

    [Fact]
    public void A_label_on_a_cluster_ends_the_trail_at_its_other_addresses()
    {
        var first = Wpkh("end-cluster-a");
        var second = Wpkh("end-cluster-b");
        _harness.Clustering.Analyse(
            _harness.Case,
            [Tx("end-cluster").In(first, 1_000_000).In(second, 2_000_000).Out(Wpkh("x"), 2_990_000).Build()]);
        _harness.Own(LabelSubject.ForCluster(first), LabelKind.Mixer);

        var hint = Assert.Single(_harness.Classifier.ClassifyAddress(_harness.Case, second));

        Assert.Equal(TrailEndReason.Mixer, hint.Reason);
    }

    [Fact]
    public void An_end_from_another_case_says_so()
    {
        var address = Wpkh("end-other-case");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange);
        _harness.Library.Absorb(_harness.Case);
        using var other = _harness.Create("2026-0202");
        _harness.Library.ShareInto(other, [address]);

        var hint = Assert.Single(_harness.Classifier.ClassifyAddress(other, address));

        Assert.Contains("från ett annat ärende", hint.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_labels_that_disagree_are_both_given_the_best_supported_first()
    {
        var address = Wpkh("disagree");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Mixer);
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, "Börsen AB");
        _harness.Profiles.Profile = new("Kollega Kollegasson", "Analytiker", "Enheten");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, "Börsen AB", "Andra svaret");

        var hints = _harness.Classifier.ClassifyAddress(_harness.Case, address);

        Assert.Equal([TrailEndReason.Exchange, TrailEndReason.Mixer], hints.Select(hint => hint.Reason));
    }

    [Fact]
    public void Transactions_are_left_to_the_other_classifiers()
    {
        var transaction = Tx("any").In(Wpkh("in"), 1_000).Out(Wpkh("out"), 900).Build();

        Assert.Empty(_harness.Classifier.ClassifyTransaction(_harness.Case, transaction));
    }
}
