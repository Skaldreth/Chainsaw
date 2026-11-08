using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Attribution.Contracts;
using static Chainsaw.Modules.Attribution.Tests.Synthetic;

namespace Chainsaw.Modules.Attribution.Tests;

public sealed class SanctionTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void An_imported_list_states_its_hash_dates_and_counts()
    {
        var file = new SdnFile()
            .Xbt("9001", "SYNTETISK BÖRS", Wpkh("sdn-1").Value)
            .Entry(
                "9002",
                "SYNTETISK MIXER",
                "CYBER3",
                ("Digital Currency Address - XBT", Pkh("sdn-2").Value),
                ("Digital Currency Address - ETH", "0x" + new string('a', 40)),
                ("Digital Currency Address - XBT", "1Inteengiltigadress"),
                ("Passport", "X1234"));

        var info = _harness.Sanctions.Import(file.Stream());

        Assert.Equal("OFAC SDN", info.Name);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(file.Bytes())), info.Sha256);
        Assert.Equal(new DateOnly(2026, 11, 5), info.PublishDate);
        Assert.Equal(_harness.Clock.UtcNow, info.ImportedUtc);
        Assert.Equal(2, info.AddressesByCurrency["XBT"]);
        Assert.Equal(1, info.AddressesByCurrency["ETH"]);
        Assert.Equal(1, info.Skipped);
        Assert.Contains(info.Sha256, info.Version, StringComparison.Ordinal);
        Assert.Contains("2026-11-05", info.Version, StringComparison.Ordinal);
    }

    [Fact]
    public void The_list_is_kept_byte_for_byte_in_the_data_folder()
    {
        var file = new SdnFile().Xbt("9001", "SYNTETISK", Wpkh("kept").Value);

        var info = _harness.Sanctions.Import(file.Stream());

        var stored = Directory.GetFiles(Path.Combine(_harness.Directory, "Sanktionslistor"), "*.xml");
        Assert.Equal(file.Bytes(), File.ReadAllBytes(Assert.Single(stored)));
        Assert.Contains(info.Sha256, Path.GetFileName(stored[0]), StringComparison.Ordinal);
    }

    [Fact]
    public void Importing_the_same_file_again_changes_nothing()
    {
        var file = new SdnFile().Xbt("9001", "SYNTETISK", Wpkh("again").Value);
        var first = _harness.Sanctions.Import(file.Stream());
        _harness.Clock.UtcNow = _harness.Clock.UtcNow.AddDays(1);

        var second = _harness.Sanctions.Import(file.Stream());

        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(first.ImportedUtc, second.ImportedUtc);
        Assert.Single(_harness.Sanctions.List());
    }

    [Fact]
    public void A_hit_cannot_be_marked_Sprid_inte()
    {
        var address = Wpkh("sanctioned");
        _harness.Sanctions.Import(new SdnFile().Xbt("9001", "SYNTETISK BÖRS", address.Value).Stream());
        var hit = Assert.Single(_harness.Labels.CheckSanctions(_harness.Case, [address]).Added);

        Assert.False(_harness.Labels.SetDoNotSpread(_harness.Case, hit.Sequence, doNotSpread: true));
    }

    [Fact]
    public void A_hit_is_labelled_sanctioned_with_the_list_as_its_origin()
    {
        var address = Wpkh("sanctioned");
        var info = _harness.Sanctions.Import(new SdnFile().Xbt("9001", "SYNTETISK BÖRS", address.Value).Stream());

        var check = _harness.Labels.CheckSanctions(_harness.Case, [address, Wpkh("clean")]);

        Assert.Equal(info.Sha256, check.List?.Sha256);
        var label = Assert.Single(check.Added);
        Assert.Equal(LabelKind.Sanctioned, label.Kind);
        Assert.Equal("SYNTETISK BÖRS", label.Name);
        Assert.Equal(LabelSource.SanctionList, label.Origin.Source);
        Assert.Equal("OFAC SDN", label.Origin.SourceName);
        Assert.Equal(info.Version, label.Origin.SourceVersion);
        Assert.Contains("uid 9001", label.Origin.Reason, StringComparison.Ordinal);
        Assert.Contains("CYBER2", label.Origin.Reason, StringComparison.Ordinal);
        var journal = _harness.Case.ReadJournal().Last();
        Assert.Equal(AttributionJournalKinds.SanctionsMatched, journal.Kind);
        Assert.Contains(info.Sha256, journal.Details, StringComparison.Ordinal);
        var explained = Assert.Single(_harness.Labels.GetLabels(_harness.Case, address)).Explain();
        Assert.StartsWith("Bedömning", explained, StringComparison.Ordinal);
        Assert.Contains(info.Sha256, explained, StringComparison.Ordinal);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_hit_is_labelled_once_also_when_it_was_taken_back()
    {
        var address = Wpkh("once");
        _harness.Sanctions.Import(new SdnFile().Xbt("9001", "SYNTETISK", address.Value).Stream());
        var label = Assert.Single(_harness.Labels.CheckSanctions(_harness.Case, [address]).Added);

        var again = _harness.Labels.CheckSanctions(_harness.Case, [address]);
        Assert.Empty(again.Added);
        Assert.Equal(1, again.AlreadyLabelled);

        _harness.Labels.Retract(_harness.Case, label.Sequence, "Utredd: annan ägare");
        var journal = _harness.Case.ReadJournal().Count;
        Assert.Empty(_harness.Labels.CheckSanctions(_harness.Case, [address]).Added);
        Assert.Equal(journal, _harness.Case.ReadJournal().Count);
    }

    [Fact]
    public void A_newer_list_is_current_and_labels_again_with_its_own_hash()
    {
        var address = Wpkh("relisted");
        _harness.Sanctions.Import(new SdnFile("10/01/2026").Xbt("9001", "SYNTETISK", address.Value).Stream());
        _harness.Labels.CheckSanctions(_harness.Case, [address]);
        var newer = _harness.Sanctions.Import(
            new SdnFile("11/01/2026").Xbt("9001", "SYNTETISK", address.Value).Stream());

        var check = _harness.Labels.CheckSanctions(_harness.Case, [address]);

        Assert.Equal(newer.Sha256, _harness.Sanctions.Current?.Sha256);
        Assert.Equal(newer.Version, Assert.Single(check.Added).Origin.SourceVersion);
        var merged = Assert.Single(_harness.Labels.GetLabels(_harness.Case, address));
        Assert.Equal(2, merged.Origins.Count);
    }

    [Fact]
    public void An_older_list_imported_later_does_not_become_current()
    {
        var newer = _harness.Sanctions.Import(new SdnFile("11/01/2026").Xbt("1", "A", Wpkh("n").Value).Stream());
        _harness.Clock.UtcNow = _harness.Clock.UtcNow.AddDays(1);
        _harness.Sanctions.Import(new SdnFile("01/01/2026").Xbt("1", "A", Wpkh("o").Value).Stream());

        Assert.Equal(newer.Sha256, _harness.Sanctions.Current?.Sha256);
    }

    [Fact]
    public void Without_a_list_nothing_is_checked()
    {
        var check = _harness.Labels.CheckSanctions(_harness.Case, [Wpkh("unchecked")]);

        Assert.Null(check.List);
        Assert.Empty(check.Added);
        Assert.Null(_harness.Sanctions.Find(Wpkh("unchecked")));
    }

    [Fact]
    public void A_changed_list_file_is_refused()
    {
        var address = Wpkh("tampered");
        _harness.Sanctions.Import(new SdnFile().Xbt("9001", "SYNTETISK", address.Value).Stream());
        var stored = Directory.GetFiles(Path.Combine(_harness.Directory, "Sanktionslistor"), "*.xml").Single();
        File.WriteAllText(stored, new SdnFile().Xbt("9001", "SYNTETISK", Wpkh("other").Value).Xml());

        var exception = Assert.Throws<SanctionListException>(() => _harness.Sanctions.Find(address));

        Assert.Equal(SanctionListError.Tampered, exception.Error);
    }

    [Fact]
    public void A_file_that_is_not_an_sdn_list_is_refused()
    {
        var exception = Assert.Throws<SanctionListException>(() =>
            _harness.Sanctions.Import(new MemoryStream(Encoding.UTF8.GetBytes("<html><body/></html>"))));

        Assert.Equal(SanctionListError.NotSdnList, exception.Error);
        Assert.Empty(_harness.Sanctions.List());
    }

    [Fact]
    public void A_list_without_a_publish_date_is_refused()
    {
        var exception = Assert.Throws<SanctionListException>(() =>
            _harness.Sanctions.Import(new SdnFile("inte ett datum").Stream()));

        Assert.Equal(SanctionListError.NoPublishDate, exception.Error);
    }

    [Fact]
    public void A_list_with_a_dtd_is_refused()
    {
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE sdnList [<!ENTITY x SYSTEM "file:///etc/passwd">]>
            <sdnList><publshInformation><Publish_Date>11/05/2026</Publish_Date></publshInformation>&x;</sdnList>
            """;

        var exception = Assert.Throws<SanctionListException>(() =>
            _harness.Sanctions.Import(new MemoryStream(Encoding.UTF8.GetBytes(xml))));

        Assert.Equal(SanctionListError.NotSdnList, exception.Error);
    }
}
