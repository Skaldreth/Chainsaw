using System.Text;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;
using static Chainsaw.Modules.Attribution.Tests.Synthetic;

namespace Chainsaw.Modules.Attribution.Tests;

public sealed class LibraryTests : IDisposable
{
    private const string SecretNote = "Tipsaren heter Hemlig";

    private const string SecretReason = "Enligt förhör med vittnet";

    private const string OtherCaseNumber = "2026-0202";

    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void The_library_takes_a_case_labels_and_gives_them_to_another_case()
    {
        var address = Wpkh("shared-exchange");
        var original = _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, "Börsen AB");

        Assert.Equal(new LibraryAbsorption(1, 0), _harness.Library.Absorb(_harness.Case));
        using var other = _harness.Create(OtherCaseNumber);
        var shared = Assert.Single(_harness.Library.ShareInto(other, [address, Wpkh("not-labelled")]));

        Assert.Equal(LabelKind.Exchange, shared.Kind);
        Assert.Equal("Börsen AB", shared.Name);
        Assert.Equal(LabelSource.OtherCase, shared.Origin.Source);
        Assert.Equal(LabelSource.Own, shared.Origin.OriginalSource);
        Assert.Equal(original.Origin.OriginId, shared.Origin.OriginId);
        Assert.Equal("Testa Testsson", shared.Origin.SetBy.Name);
        Assert.Equal(original.Origin.SetUtc, shared.Origin.SetUtc);
        var label = Assert.Single(_harness.Labels.GetLabels(other, address));
        Assert.True(label.IsOnlyFromOtherCases);
        Assert.Contains("Från ett annat ärende", label.Explain(), StringComparison.Ordinal);
        Assert.Equal(AttributionJournalKinds.LabelsFromLibrary, other.ReadJournal().Last().Kind);
        Assert.True(_harness.Verify(other).IsIntact);
    }

    [Fact]
    public void The_case_number_the_note_and_the_reason_never_reach_another_case()
    {
        var address = Wpkh("secret");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, "Börsen AB", SecretReason, SecretNote);
        _harness.Library.Absorb(_harness.Case);
        using var other = _harness.Create(OtherCaseNumber);
        _harness.Profiles.Profile = new("Kollega Kollegasson", "Analytiker", "Enheten");

        var shared = Assert.Single(_harness.Library.ShareInto(other, [address]));

        Assert.Null(shared.Origin.Reason);
        Assert.Null(shared.Origin.Note);
        var everything = new StringBuilder();
        foreach (var entry in other.ReadJournal())
        {
            everything.Append(entry.Summary).Append(entry.Details).Append('\n');
        }

        foreach (var label in _harness.Labels.GetLabels(other))
        {
            everything.Append(label.Explain()).Append(label.Entries[0].Origin).Append('\n');
        }

        var text = everything.ToString();
        Assert.DoesNotContain(Harness.CaseNumber, text, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretNote, text, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretReason, text, StringComparison.Ordinal);
        Assert.DoesNotContain(_harness.Case.Info.Id.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Kollega Kollegasson", other.ReadJournal().Last().Operator.Name);
    }

    [Fact]
    public void The_library_keeps_the_case_number_and_the_note_for_the_operator_and_is_encrypted()
    {
        var address = Wpkh("kept-secret");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, "Börsen AB", SecretReason, SecretNote);

        _harness.Library.Absorb(_harness.Case);

        var stored = Assert.Single(_harness.Library.List());
        Assert.Equal([Harness.CaseNumber], stored.CaseNumbers);
        Assert.Equal(SecretNote, stored.Origin.Note);
        Assert.Equal(SecretReason, stored.Origin.Reason);
        var file = Directory.GetFiles(Path.Combine(_harness.Directory, "Etiketter")).Single();
        var bytes = Encoding.UTF8.GetString(File.ReadAllBytes(file));
        Assert.DoesNotContain(Harness.CaseNumber, bytes, StringComparison.Ordinal);
        Assert.DoesNotContain("Hemlig", bytes, StringComparison.Ordinal);
        Assert.DoesNotContain(address.Value, bytes, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_marked_do_not_spread_never_enters_the_library()
    {
        var address = Wpkh("do-not-spread");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, doNotSpread: true);

        Assert.Equal(new LibraryAbsorption(0, 0), _harness.Library.Absorb(_harness.Case));
        using var other = _harness.Create(OtherCaseNumber);

        Assert.Empty(_harness.Library.List());
        Assert.Empty(_harness.Library.ShareInto(other, [address]));
    }

    [Fact]
    public void A_victim_label_never_enters_the_library()
    {
        var address = Wpkh("victim-spread");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Victim, "Offret");

        _harness.Library.Absorb(_harness.Case);

        Assert.Empty(_harness.Library.List());
    }

    [Fact]
    public void A_label_taken_back_leaves_the_library_when_its_case_is_absorbed_again()
    {
        var address = Wpkh("taken-back");
        var label = _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange);
        _harness.Library.Absorb(_harness.Case);
        _harness.Labels.Retract(_harness.Case, label.Sequence, "Fel");

        Assert.Equal(new LibraryAbsorption(0, 1), _harness.Library.Absorb(_harness.Case));

        Assert.Empty(_harness.Library.List());
    }

    [Fact]
    public void A_label_the_case_took_back_is_not_given_again()
    {
        var address = Wpkh("refused");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange);
        _harness.Library.Absorb(_harness.Case);
        using var other = _harness.Create(OtherCaseNumber);
        var shared = Assert.Single(_harness.Library.ShareInto(other, [address]));
        _harness.Labels.Retract(other, shared.Sequence, "Stämmer inte i det här ärendet");

        Assert.Empty(_harness.Library.ShareInto(other, [address]));
        Assert.Empty(_harness.Labels.GetLabels(other, address));
    }

    [Fact]
    public void A_case_is_not_given_its_own_labels()
    {
        var address = Wpkh("own-back");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange);
        _harness.Library.Absorb(_harness.Case);

        Assert.Empty(_harness.Library.ShareInto(_harness.Case, [address]));
    }

    [Fact]
    public void A_label_from_another_case_and_an_own_label_confirm_each_other()
    {
        var address = Wpkh("confirmed");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, "Börsen AB");
        _harness.Library.Absorb(_harness.Case);
        using var other = _harness.Create(OtherCaseNumber);
        _harness.Profiles.Profile = new("Kollega Kollegasson", "Analytiker", "Enheten");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, "Börsen AB", "Eget svar", @case: other);

        _harness.Library.ShareInto(other, [address]);

        var label = Assert.Single(_harness.Labels.GetLabels(other, address));
        Assert.True(label.IsConfirmed);
        Assert.False(label.IsOnlyFromOtherCases);
    }

    [Fact]
    public void The_same_label_held_by_two_cases_is_one_origin()
    {
        var address = Wpkh("one-origin");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange);
        _harness.Library.Absorb(_harness.Case);
        using var second = _harness.Create(OtherCaseNumber);
        _harness.Library.ShareInto(second, [address]);
        _harness.Library.Absorb(second);
        using var third = _harness.Create("2026-0303");

        var shared = Assert.Single(_harness.Library.ShareInto(third, [address]));

        var stored = Assert.Single(_harness.Library.List());
        Assert.Equal([Harness.CaseNumber, OtherCaseNumber], stored.CaseNumbers);
        Assert.False(Assert.Single(_harness.Labels.GetLabels(third, address)).IsConfirmed);
        Assert.Equal(LabelSource.Own, shared.Origin.OriginalSource);
    }

    [Fact]
    public void A_colleague_label_spreads_on_without_its_reason()
    {
        var address = Wpkh("colleague");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Mixer, "Blandaren", SecretReason, SecretNote);
        _harness.Library.Absorb(_harness.Case);
        using var colleagueFile = _harness.Create(OtherCaseNumber);
        _harness.Library.ShareInto(colleagueFile, [address]);

        // On a new computer the library is empty; the colleague's file brings the label along.
        Directory.Delete(Path.Combine(_harness.Directory, "Etiketter"), recursive: true);
        _harness.Library.Absorb(colleagueFile);

        var stored = Assert.Single(_harness.Library.List());
        Assert.Null(stored.Origin.Reason);
        Assert.Null(stored.Origin.Note);
        Assert.Equal(LabelSource.Own, stored.Origin.Source);
        Assert.Equal([OtherCaseNumber], stored.CaseNumbers);
    }

    [Fact]
    public void A_label_on_a_cluster_spreads_as_a_label_on_a_cluster()
    {
        var address = Wpkh("cluster-spread");
        _harness.Own(LabelSubject.ForCluster(address), LabelKind.Exchange);
        _harness.Library.Absorb(_harness.Case);
        using var other = _harness.Create(OtherCaseNumber);

        var shared = Assert.Single(_harness.Library.ShareInto(other, [address]));

        Assert.Equal(LabelSubjectKind.Cluster, shared.Subject.Kind);
    }

    [Fact]
    public void A_locked_vault_keeps_the_library_closed()
    {
        _harness.Own(LabelSubject.ForAddress(Wpkh("locked")), LabelKind.Exchange);
        _harness.Keys.Locked = true;

        var exception = Assert.Throws<VaultException>(() => _harness.Library.Absorb(_harness.Case));

        Assert.Equal(VaultError.Locked, exception.Error);
    }

    [Fact]
    public void A_library_from_another_vault_is_refused_and_left_as_it_is()
    {
        _harness.Own(LabelSubject.ForAddress(Wpkh("other-vault")), LabelKind.Exchange);
        _harness.Library.Absorb(_harness.Case);
        var file = Directory.GetFiles(Path.Combine(_harness.Directory, "Etiketter")).Single();
        var before = File.ReadAllBytes(file);
        _harness.Keys.Secret = "another vault";

        Assert.Throws<InvalidDataException>(() => _harness.Library.Absorb(_harness.Case));

        Assert.Equal(before, File.ReadAllBytes(file));
    }

    [Fact]
    public void Sharing_nothing_writes_nothing()
    {
        using var other = _harness.Create(OtherCaseNumber);
        var before = other.ReadJournal().Count;

        Assert.Empty(_harness.Library.ShareInto(other, [Wpkh("nothing")]));

        Assert.Equal(before, other.ReadJournal().Count);
        Assert.Empty(other.Read(reader => LabelStore.Read(reader).All));
    }
}
