using Chainsaw.Modules.Attribution.Contracts;
using static Chainsaw.Modules.Attribution.Tests.Synthetic;

namespace Chainsaw.Modules.Attribution.Tests;

/// <summary>Regression tests for the findings of the independent review of stage 8.</summary>
public sealed class ReviewTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void I1_a_label_on_the_victim_address_never_spreads()
    {
        var victim = Wpkh("victim-account");
        _harness.Own(LabelSubject.ForAddress(victim), LabelKind.Victim, "Offret");
        _harness.Own(LabelSubject.ForAddress(victim), LabelKind.Exchange, "Kalles konto på Börsen");

        _harness.Library.Absorb(_harness.Case);
        using var other = _harness.Create("2026-0202");

        Assert.Empty(_harness.Library.List());
        Assert.Empty(_harness.Library.ShareInto(other, [victim]));
    }

    [Fact]
    public void I1_a_label_on_the_victim_cluster_never_spreads()
    {
        var victim = Wpkh("victim-a");
        var sameWallet = Wpkh("victim-b");
        _harness.Clustering.Analyse(
            _harness.Case,
            [Tx("victim-wallet").In(victim, 1_000_000).In(sameWallet, 2_000_000).Out(Wpkh("p"), 2_990_000).Build()]);
        _harness.Own(LabelSubject.ForAddress(victim), LabelKind.Victim);
        _harness.Own(LabelSubject.ForAddress(sameWallet), LabelKind.Exchange);
        _harness.Own(LabelSubject.ForCluster(sameWallet), LabelKind.Exchange);

        _harness.Library.Absorb(_harness.Case);

        Assert.Empty(_harness.Library.List());
    }

    [Fact]
    public void I2_a_label_taken_back_where_it_was_set_is_withdrawn_from_every_copy()
    {
        var address = Wpkh("withdrawn");
        var label = _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, "Börsen AB");
        _harness.Library.Absorb(_harness.Case);
        using var second = _harness.Create("2026-0202");
        _harness.Library.ShareInto(second, [address]);
        _harness.Library.Absorb(second);

        _harness.Labels.Retract(_harness.Case, label.Sequence, "Fel börs");
        _harness.Library.Absorb(_harness.Case);
        using var third = _harness.Create("2026-0303");

        Assert.Empty(_harness.Library.List());
        Assert.Empty(_harness.Library.ShareInto(third, [address]));
        _harness.Library.ShareInto(second, [address]);
        Assert.Empty(_harness.Labels.GetLabels(second, address));
        var journal = second.ReadJournal().Last();
        Assert.Equal(AttributionJournalKinds.LabelRetracted, journal.Kind);
        Assert.DoesNotContain(Harness.CaseNumber, journal.Summary + journal.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("Fel börs", journal.Summary + journal.Details, StringComparison.Ordinal);
        Assert.True(_harness.Verify(second).IsIntact);
    }

    [Fact]
    public void I2_a_label_made_do_not_spread_after_it_spread_is_withdrawn()
    {
        var address = Wpkh("later-secret");
        var label = _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange);
        _harness.Library.Absorb(_harness.Case);
        using var second = _harness.Create("2026-0202");
        _harness.Library.ShareInto(second, [address]);
        _harness.Library.Absorb(second);

        _harness.Labels.Retract(_harness.Case, label.Sequence, "Ska inte spridas");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, doNotSpread: true);
        _harness.Library.Absorb(_harness.Case);

        Assert.Empty(_harness.Library.List());
    }

    [Fact]
    public void I3_the_same_operator_setting_the_same_label_twice_is_one_origin()
    {
        var address = Wpkh("double-click");
        var first = _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, "Börsen AB");
        var journal = _harness.Case.ReadJournal().Count;

        var second = _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, "Börsen AB");

        Assert.Equal(first, second);
        Assert.Equal(journal, _harness.Case.ReadJournal().Count);
        Assert.False(Assert.Single(_harness.Labels.GetLabels(_harness.Case, address)).IsConfirmed);
    }

    [Fact]
    public void I4_two_versions_of_the_same_sanction_list_confirm_nothing()
    {
        var address = Wpkh("two-versions");
        _harness.Sanctions.Import(new SdnFile("11/01/2026").Xbt("9001", "SYNTETISK", address.Value).Stream());
        _harness.Labels.CheckSanctions(_harness.Case, [address]);
        _harness.Sanctions.Import(new SdnFile("11/05/2026").Xbt("9001", "SYNTETISK", address.Value).Stream());
        _harness.Labels.CheckSanctions(_harness.Case, [address]);

        var label = Assert.Single(_harness.Labels.GetLabels(_harness.Case, address));

        Assert.Equal(2, label.Origins.Count);
        Assert.Equal(1, label.IndependentOrigins);
        Assert.False(label.IsConfirmed);
        Assert.DoesNotContain("Bekräftad", label.Explain(), StringComparison.Ordinal);
    }

    [Fact]
    public void I5_a_sanction_label_from_another_case_does_not_stop_the_case_own_check()
    {
        var address = Wpkh("own-check");
        _harness.Sanctions.Import(new SdnFile().Xbt("9001", "SYNTETISK", address.Value).Stream());
        _harness.Labels.CheckSanctions(_harness.Case, [address]);
        _harness.Library.Absorb(_harness.Case);
        using var other = _harness.Create("2026-0202");
        _harness.Library.ShareInto(other, [address]);

        var check = _harness.Labels.CheckSanctions(other, [address]);

        var own = Assert.Single(check.Added);
        Assert.Equal(LabelSource.SanctionList, own.Origin.Source);
        Assert.Contains("uid 9001", own.Origin.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void I6_every_hit_names_the_list_it_was_found_on()
    {
        var address = Wpkh("pinned");
        var list = _harness.Sanctions.Import(new SdnFile().Xbt("9001", "SYNTETISK", address.Value).Stream());

        var label = Assert.Single(_harness.Labels.CheckSanctions(_harness.Case, [address]).Added);

        Assert.Equal(list.Version, label.Origin.SourceVersion);
        Assert.Contains(list.Sha256, label.Origin.OriginId, StringComparison.Ordinal);
        Assert.Contains(list.Sha256, _harness.Case.ReadJournal().Last().Details, StringComparison.Ordinal);
    }

    [Fact]
    public void N1_a_victim_label_set_later_does_not_take_back_copies_elsewhere_and_can_be_undone()
    {
        var address = Wpkh("victim-later");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, "Börsen");
        _harness.Library.Absorb(_harness.Case);
        using var second = _harness.Create("2026-0202");
        _harness.Library.ShareInto(second, [address]);
        var victim = _harness.Own(LabelSubject.ForAddress(address), LabelKind.Victim);
        _harness.Library.Absorb(_harness.Case);

        _harness.Library.ShareInto(second, [address]);
        Assert.Single(_harness.Labels.GetLabels(second, address));
        Assert.Empty(_harness.Library.List());

        _harness.Labels.Retract(_harness.Case, victim.Sequence, "Fel adress");
        _harness.Library.Absorb(_harness.Case);
        Assert.Single(_harness.Library.List());
    }

    [Fact]
    public void N2_the_case_own_sanction_hit_is_shown_beside_a_copy()
    {
        var address = Wpkh("own-shown");
        _harness.Sanctions.Import(new SdnFile().Xbt("9001", "SYNTETISK", address.Value).Stream());
        _harness.Labels.CheckSanctions(_harness.Case, [address]);
        _harness.Library.Absorb(_harness.Case);
        using var other = _harness.Create("2026-0202");
        _harness.Library.ShareInto(other, [address]);
        _harness.Labels.CheckSanctions(other, [address]);

        var label = Assert.Single(_harness.Labels.GetLabels(other, address));

        Assert.False(label.IsOnlyFromOtherCases);
        Assert.Contains("uid 9001", label.Explain(), StringComparison.Ordinal);
    }

    [Fact]
    public void N3_setting_the_same_label_again_as_do_not_spread_is_sealed_but_confirms_nothing()
    {
        var address = Wpkh("changed-mind");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, "B");

        var second = _harness.Own(
            LabelSubject.ForAddress(address), LabelKind.Exchange, "B", "Börsens svar", doNotSpread: true);

        Assert.True(second.DoNotSpread);
        Assert.Equal(2, _harness.Labels.GetEntries(_harness.Case).Count);
        Assert.False(Assert.Single(_harness.Labels.GetLabels(_harness.Case, address)).IsConfirmed);
    }

    [Fact]
    public void N3_a_newer_version_of_an_analysis_is_sealed_but_confirms_nothing()
    {
        var address = Wpkh("analysis-v2");
        var draft = new LabelDraft(LabelSubject.ForAddress(address), LabelKind.Exchange, null, "Sweep")
        {
            Source = LabelSource.Behaviour,
            SourceName = "exchange-sweep",
            SourceVersion = "v1",
        };
        _harness.Labels.Add(_harness.Case, draft);

        var newer = _harness.Labels.Add(_harness.Case, draft with { SourceVersion = "v2" });

        Assert.Equal("v2", newer.Origin.SourceVersion);
        Assert.False(Assert.Single(_harness.Labels.GetLabels(_harness.Case, address)).IsConfirmed);
    }
}
