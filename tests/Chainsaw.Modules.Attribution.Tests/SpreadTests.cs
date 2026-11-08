using Chainsaw.Modules.Attribution.Contracts;
using static Chainsaw.Modules.Attribution.Tests.Synthetic;

namespace Chainsaw.Modules.Attribution.Tests;

/// <summary>
/// <em>Sprid inte</em> on a label already set (sketch ET1a): a sealed record of its own with a line in the journal, the
/// latest one counting, and the library following when the case is absorbed again.
/// </summary>
public sealed class SpreadTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void An_own_label_is_marked_do_not_spread_sealed_and_journalled()
    {
        var label = _harness.Own(LabelSubject.ForAddress(Wpkh("exchange")), LabelKind.Exchange, "Börsen AB");

        Assert.True(_harness.Labels.SetDoNotSpread(_harness.Case, label.Sequence, doNotSpread: true));

        var entry = Assert.Single(_harness.Labels.GetEntries(_harness.Case));
        Assert.True(entry.DoNotSpread);
        Assert.Equal(label.Origin, entry.Origin);
        var journal = _harness.Case.ReadJournal().Last();
        Assert.Equal(AttributionJournalKinds.LabelSpreadChanged, journal.Kind);
        Assert.StartsWith("Etiketten sprids inte", journal.Summary, StringComparison.Ordinal);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void The_latest_marking_counts_and_survives_a_reopening()
    {
        var label = _harness.Own(LabelSubject.ForAddress(Wpkh("exchange")), LabelKind.Exchange);
        _harness.Labels.SetDoNotSpread(_harness.Case, label.Sequence, doNotSpread: true);
        Assert.True(_harness.Labels.SetDoNotSpread(_harness.Case, label.Sequence, doNotSpread: false));

        _harness.Reopen();

        Assert.False(Assert.Single(_harness.Labels.GetEntries(_harness.Case)).DoNotSpread);
        Assert.Equal(
            AttributionJournalKinds.LabelSpreadChanged, _harness.Case.ReadJournal().Last().Kind);
    }

    [Fact]
    public void Marking_a_label_as_it_already_is_writes_nothing()
    {
        var label = _harness.Own(LabelSubject.ForAddress(Wpkh("exchange")), LabelKind.Exchange, doNotSpread: true);
        var before = _harness.Case.ReadJournal().Count;

        Assert.False(_harness.Labels.SetDoNotSpread(_harness.Case, label.Sequence, doNotSpread: true));

        Assert.Equal(before, _harness.Case.ReadJournal().Count);
    }

    [Fact]
    public void A_label_taken_back_a_victim_or_an_unknown_sequence_is_not_marked()
    {
        var gone = _harness.Own(LabelSubject.ForAddress(Wpkh("gone")), LabelKind.Exchange);
        _harness.Labels.Retract(_harness.Case, gone.Sequence, null);
        var victim = _harness.Own(LabelSubject.ForAddress(Wpkh("victim")), LabelKind.Victim);

        Assert.False(_harness.Labels.SetDoNotSpread(_harness.Case, gone.Sequence, doNotSpread: true));
        Assert.False(_harness.Labels.SetDoNotSpread(_harness.Case, victim.Sequence, doNotSpread: true));
        Assert.False(_harness.Labels.SetDoNotSpread(_harness.Case, 9_999, doNotSpread: true));
    }

    [Fact]
    public void A_label_marked_after_it_was_absorbed_leaves_the_library_the_next_time()
    {
        var address = Wpkh("exchange");
        var label = _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, "Börsen AB");
        _harness.Library.Absorb(_harness.Case);
        Assert.Single(_harness.Library.List());

        _harness.Labels.SetDoNotSpread(_harness.Case, label.Sequence, doNotSpread: true);
        _harness.Library.Absorb(_harness.Case);

        Assert.Empty(_harness.Library.List());
    }

    [Fact]
    public void The_whole_history_holds_every_label_with_its_taking_back()
    {
        var kept = _harness.Own(LabelSubject.ForAddress(Wpkh("kept")), LabelKind.Exchange);
        var gone = _harness.Own(LabelSubject.ForAddress(Wpkh("gone")), LabelKind.Gambling);
        _harness.Labels.Retract(_harness.Case, gone.Sequence, "Fel kluster");

        var history = _harness.Labels.GetHistory(_harness.Case);

        Assert.Equal([kept.Sequence, gone.Sequence], history.Select(entry => entry.Label.Sequence));
        Assert.False(history[0].IsRetracted);
        Assert.True(history[0].CanChangeSpread);
        Assert.Equal("Fel kluster", history[1].Retraction?.Reason);
        Assert.False(history[1].CanChangeSpread);
    }
}
