using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using static Chainsaw.Modules.Attribution.Tests.Synthetic;

namespace Chainsaw.Modules.Attribution.Tests;

public sealed class LabelTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void An_own_label_has_its_origin_who_when_why_and_version()
    {
        var address = Wpkh("exchange-deposit");

        var label = _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, "Börsen AB", note: "Svar 12/3");

        Assert.Equal(LabelSource.Own, label.Origin.Source);
        Assert.Equal(LabelSource.Own, label.Origin.OriginalSource);
        Assert.Equal("egen", label.Origin.SourceName);
        Assert.Equal("v1", label.Origin.SourceVersion);
        Assert.Equal("Testa Testsson", label.Origin.SetBy.Name);
        Assert.Equal(_harness.Clock.UtcNow, label.Origin.SetUtc);
        Assert.Equal("Insättningsadress enligt börsens svar", label.Origin.Reason);
        Assert.Equal("Svar 12/3", label.Origin.Note);
        Assert.False(label.Origin.IsFromOtherCase);
        Assert.Equal(LabelGroup.Withdrawal, label.Kind.Group());
    }

    [Fact]
    public void A_label_is_sealed_with_a_journal_line_and_survives_reopening()
    {
        var address = Wpkh("mixer");
        var label = _harness.Own(LabelSubject.ForAddress(address), LabelKind.Mixer, "Blandaren");

        _harness.Reopen();

        var stored = Assert.Single(_harness.Labels.GetEntries(_harness.Case));
        Assert.Equal(label, stored);
        var journal = _harness.Case.ReadJournal().Single(entry => entry.Sequence == stored.JournalSequence);
        Assert.Equal(AttributionJournalKinds.LabelAdded, journal.Kind);
        Assert.Contains("mixer", journal.Summary, StringComparison.Ordinal);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_label_is_a_judgement_and_explains_what_it_rests_on()
    {
        var address = Wpkh("explained");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, "Börsen AB");

        var label = Assert.Single(_harness.Labels.GetLabels(_harness.Case, address));
        var text = label.Explain();

        Assert.StartsWith("Bedömning, inte ett faktum", text, StringComparison.Ordinal);
        Assert.Contains("Egen etikett av Testa Testsson", text, StringComparison.Ordinal);
        Assert.Contains("Insättningsadress enligt börsens svar", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_retracted_label_is_gone_but_stays_sealed_with_a_journal_line()
    {
        var address = Wpkh("wrong");
        var label = _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange);

        Assert.True(_harness.Labels.Retract(_harness.Case, label.Sequence, "Fel adress, förväxlad med en annan"));

        Assert.Empty(_harness.Labels.GetEntries(_harness.Case));
        Assert.Empty(_harness.Labels.GetLabels(_harness.Case, address));
        var journal = _harness.Case.ReadJournal().Last();
        Assert.Equal(AttributionJournalKinds.LabelRetracted, journal.Kind);
        Assert.Contains("Fel adress", journal.Details, StringComparison.Ordinal);
        Assert.False(_harness.Labels.Retract(_harness.Case, label.Sequence, "Igen"));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void Retracting_a_label_that_does_not_exist_changes_nothing()
    {
        var before = _harness.Case.ReadJournal().Count;

        Assert.False(_harness.Labels.Retract(_harness.Case, 42, "Finns inte"));

        Assert.Equal(before, _harness.Case.ReadJournal().Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_label_needs_a_reason(string reason)
    {
        Assert.Throws<ArgumentException>(() =>
            _harness.Own(LabelSubject.ForAddress(Wpkh("no-reason")), LabelKind.Exchange, reason: reason));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void A_retraction_needs_no_reason(string? reason)
    {
        var label = _harness.Own(LabelSubject.ForAddress(Wpkh("keep")), LabelKind.Exchange);

        Assert.True(_harness.Labels.Retract(_harness.Case, label.Sequence, reason));

        var entry = Assert.Single(_harness.Labels.GetHistory(_harness.Case, [label.Subject.Target]));
        Assert.True(entry.IsRetracted);
        Assert.Null(entry.Retraction!.Reason);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_retraction_reason_cannot_be_too_long()
    {
        var label = _harness.Own(LabelSubject.ForAddress(Wpkh("long")), LabelKind.Exchange);

        Assert.Throws<ArgumentException>(
            () => _harness.Labels.Retract(_harness.Case, label.Sequence, new string('x', 1_001)));
        Assert.False(Assert.Single(_harness.Labels.GetHistory(_harness.Case, [label.Subject.Target])).IsRetracted);
    }

    [Fact]
    public void The_history_keeps_a_label_taken_back_with_who_when_and_why_and_survives_reopening()
    {
        var address = Wpkh("history");
        var label = _harness.Own(LabelSubject.ForAddress(address), LabelKind.Mixer, "Blandaren");
        var other = _harness.Own(LabelSubject.ForAddress(Wpkh("elsewhere")), LabelKind.Exchange);
        _harness.Clock.UtcNow += TimeSpan.FromMinutes(5);
        Assert.True(_harness.Labels.Retract(_harness.Case, label.Sequence, "Gällde klustret bredvid"));

        _harness.Reopen();

        var entry = Assert.Single(_harness.Labels.GetHistory(_harness.Case, [address]));
        Assert.Equal(label, entry.Label);
        var retraction = Assert.IsType<LabelRetraction>(entry.Retraction);
        Assert.Equal("Testa Testsson", retraction.By!.Name);
        Assert.Equal(_harness.Clock.UtcNow, retraction.Utc);
        Assert.Equal("Gällde klustret bredvid", retraction.Reason);
        Assert.True(retraction.Sequence > other.Sequence);
        Assert.False(entry.CanRetract);
        Assert.True(entry.IsUsersOwn);
        var journal = _harness.Case.ReadJournal().Single(found => found.Sequence == retraction.JournalSequence);
        Assert.Equal(AttributionJournalKinds.LabelRetracted, journal.Kind);
        Assert.Empty(_harness.Labels.GetLabels(_harness.Case, address));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_label_taken_back_is_set_anew_instead_of_taken_back_again()
    {
        var address = Wpkh("again");
        var label = _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange);
        _harness.Labels.Retract(_harness.Case, label.Sequence, null);
        var before = _harness.Case.ReadJournal().Count;

        Assert.False(_harness.Labels.Retract(_harness.Case, label.Sequence, "Igen"));
        Assert.Equal(before, _harness.Case.ReadJournal().Count);

        var anew = _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange);
        var history = _harness.Labels.GetHistory(_harness.Case, [address]);
        Assert.Equal([label.Sequence, anew.Sequence], history.Select(entry => entry.Label.Sequence));
        Assert.Equal([false, true], history.Select(entry => entry.CanRetract));
        Assert.NotEqual(label.Origin.OriginId, anew.Origin.OriginId);
    }

    [Fact]
    public void Only_the_users_own_labels_can_be_taken_back_from_the_history()
    {
        var address = Wpkh("open-source");
        var open = _harness.Labels.Add(_harness.Case, new LabelDraft(
            LabelSubject.ForAddress(address), LabelKind.Mixer, "X", "Står på listan")
        {
            Source = LabelSource.OpenSource,
            SourceName = "Lista",
            SourceVersion = "läst 2026-11-07",
        });
        var behaviour = _harness.Labels.Add(_harness.Case, new LabelDraft(
            LabelSubject.ForAddress(address), LabelKind.Exchange, null, "Töms till samma plånbok")
        {
            Source = LabelSource.Behaviour,
            SourceName = "sweep",
            SourceVersion = "v1",
        });

        var history = _harness.Labels.GetHistory(_harness.Case, [address]);

        Assert.True(history.Single(entry => entry.Label.Sequence == open.Sequence).CanRetract);
        Assert.False(history.Single(entry => entry.Label.Sequence == behaviour.Sequence).CanRetract);
        Assert.Empty(_harness.Labels.GetHistory(_harness.Case, [Wpkh("nothing")]));
        Assert.Empty(_harness.Labels.GetHistory(_harness.Case, Array.Empty<BitcoinAddress>()));
    }

    [Theory]
    [InlineData(LabelSource.SanctionList)]
    [InlineData(LabelSource.OtherCase)]
    public void A_draft_cannot_claim_a_sanction_list_or_another_case(LabelSource source)
    {
        var draft = new LabelDraft(LabelSubject.ForAddress(Wpkh("claim")), LabelKind.Sanctioned, null, "Påstår")
        {
            Source = source,
            SourceName = "OFAC SDN",
            SourceVersion = "falsk",
        };

        Assert.Throws<ArgumentException>(() => _harness.Labels.Add(_harness.Case, draft));
    }

    [Fact]
    public void A_behaviour_label_names_its_analysis_and_version()
    {
        var draft = new LabelDraft(
            LabelSubject.ForAddress(Wpkh("sweep")), LabelKind.Exchange, null, "Insättningar sveps till samma plånbok")
        {
            Source = LabelSource.Behaviour,
            SourceName = "exchange-sweep",
            SourceVersion = "v1",
        };

        var label = _harness.Labels.Add(_harness.Case, draft);

        Assert.Equal(LabelSource.Behaviour, label.Origin.Source);
        Assert.Equal("exchange-sweep", label.Origin.SourceName);
        Assert.Equal("v1", label.Origin.SourceVersion);
        Assert.Throws<ArgumentException>(() => _harness.Labels.Add(_harness.Case, draft with { SourceName = null }));
    }

    [Fact]
    public void The_same_label_from_two_origins_is_merged_and_confirmed()
    {
        var address = Wpkh("twice");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, "Börsen AB");
        _harness.Profiles.Profile = new("Kollega Kollegasson", "Analytiker", "Enheten");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, " börsen ab ", "Samma börs enligt kvitto");

        var label = Assert.Single(_harness.Labels.GetLabels(_harness.Case, address));

        Assert.Equal(2, label.Origins.Count);
        Assert.True(label.IsConfirmed);
        Assert.Contains("Bekräftad av 2 oberoende ursprung", label.Explain(), StringComparison.Ordinal);
    }

    [Fact]
    public void Different_labels_on_the_same_address_are_kept_apart()
    {
        var address = Wpkh("disputed");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, "Börsen AB");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Mixer, "Blandaren");

        var labels = _harness.Labels.GetLabels(_harness.Case, address);

        Assert.Equal(2, labels.Count);
        Assert.All(labels, label => Assert.False(label.IsConfirmed));
    }

    [Fact]
    public void A_label_on_a_cluster_applies_to_every_address_in_it_and_says_it_rests_on_the_clustering()
    {
        var first = Wpkh("cluster-a");
        var second = Wpkh("cluster-b");
        var outsider = Wpkh("outsider");
        _harness.Clustering.Analyse(
            _harness.Case,
            [Tx("together").In(first, 1_000_000).In(second, 2_000_000).Out(Wpkh("paid"), 2_990_000).Build()]);
        _harness.Own(LabelSubject.ForCluster(first), LabelKind.Exchange, "Börsen AB");

        var label = Assert.Single(_harness.Labels.GetLabels(_harness.Case, second));

        Assert.Equal(LabelSubjectKind.Cluster, label.Subject.Kind);
        Assert.Contains("vilar också på klustringens slutsatser", label.Explain(), StringComparison.Ordinal);
        Assert.Empty(_harness.Labels.GetLabels(_harness.Case, outsider));
    }

    [Fact]
    public void A_label_on_an_address_alone_does_not_spread_to_its_cluster()
    {
        var first = Wpkh("alone-a");
        var second = Wpkh("alone-b");
        _harness.Clustering.Analyse(
            _harness.Case,
            [Tx("alone").In(first, 1_000_000).In(second, 2_000_000).Out(Wpkh("alone-paid"), 2_990_000).Build()]);
        _harness.Own(LabelSubject.ForAddress(first), LabelKind.Exchange);

        Assert.Empty(_harness.Labels.GetLabels(_harness.Case, second));
    }

    [Fact]
    public void A_victim_label_never_spreads_whatever_it_is_marked()
    {
        var label = _harness.Own(LabelSubject.ForAddress(Wpkh("victim")), LabelKind.Victim, "Offret");

        Assert.False(LabelRules.MaySpread(label));
        Assert.Equal(LabelGroup.Victim, label.Kind.Group());
    }

    [Fact]
    public void Every_kind_has_a_group_and_a_swedish_name()
    {
        foreach (var kind in Enum.GetValues<LabelKind>())
        {
            Assert.False(string.IsNullOrWhiteSpace(kind.Describe()));
            Assert.True(Enum.IsDefined(kind.Group()));
        }
    }
}
