using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Attribution.Tests.Synthetic;

namespace Chainsaw.Modules.Attribution.Tests;

/// <summary>
/// The EU's consolidated list beside OFAC's: read from its own XML form, each kind with its current list, an address
/// recognised by its form where it stands, and every current list looked on; and the lists' care in the data folder
/// (removed, repaired, a ceiling per entry).
/// </summary>
public sealed class EuSanctionTests : IDisposable
{
    private static readonly EthereumAddress Account = EthereumAddress.Parse("0x" + new string('d', 40));

    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void The_eus_list_is_read_with_its_generation_date_and_the_addresses_by_their_form()
    {
        var file = new EuFile()
            .Identified("EU.1.1", "SYNTETISK GRUPP", "RUS", Wpkh("eu-1").Value)
            .Remarked(
                "EU.1.2", "SYNTETISK MÄKLARE", "CYB", $"Plånböcker: {Pkh("eu-2").Value}, 0x{new string('D', 40)}.")
            .Plain("EU.1.3", "SYNTETISK PERSON", "IRN");

        var info = _harness.Sanctions.Import(file.Stream());

        Assert.Equal("EU FSF", info.Name);
        Assert.Equal(new DateOnly(2026, 11, 6), info.PublishDate);
        Assert.Equal(2, info.AddressesByCurrency[SanctionCurrency.Bitcoin]);
        Assert.Equal(1, info.AddressesByCurrency[SanctionCurrency.Ether]);
    }

    [Fact]
    public void A_hit_on_the_eus_list_says_where_on_the_entry_the_address_stood()
    {
        var address = Wpkh("eu-hit");
        var info = _harness.Sanctions.Import(
            new EuFile().Identified("EU.2.1", "SYNTETISK GRUPP", "RUS", address.Value, "Kryptoplånbok").Stream());

        var label = Assert.Single(_harness.Labels.CheckSanctions(_harness.Case, [address]).Added);

        Assert.Equal(LabelKind.Sanctioned, label.Kind);
        Assert.Equal("SYNTETISK GRUPP", label.Name);
        Assert.Equal("EU FSF", label.Origin.SourceName);
        Assert.Equal(info.Version, label.Origin.SourceVersion);
        Assert.Contains("EU-referens EU.2.1", label.Origin.Reason, StringComparison.Ordinal);
        Assert.Contains("identifiering: Kryptoplånbok", label.Origin.Reason, StringComparison.Ordinal);
        Assert.Contains("RUS", label.Origin.Reason, StringComparison.Ordinal);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void An_ethereum_address_in_a_remark_is_found_whatever_the_case()
    {
        _harness.Sanctions.Import(
            new EuFile().Remarked("EU.3.1", "SYNTETISK", "CYB", "Adress 0x" + new string('D', 40)).Stream());

        var label = Assert.Single(_harness.Labels.CheckSanctions(_harness.Case, [Account]).Added);

        Assert.Contains("en anmärkning", label.Origin.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_kinds_of_list_are_current_and_both_are_looked_on()
    {
        var address = Wpkh("on-both");
        var ofac = _harness.Sanctions.Import(
            new SdnFile("11/01/2026").Xbt("9001", "OFAC-NAMN", address.Value).Stream());
        var eu = _harness.Sanctions.Import(new EuFile().Identified("EU.4.1", "EU-NAMN", "RUS", address.Value).Stream());

        var check = _harness.Labels.CheckSanctions(_harness.Case, [address]);

        Assert.Equal([eu.Sha256, ofac.Sha256], _harness.Sanctions.CurrentLists.Select(list => list.Sha256));
        Assert.Equal(2, check.Added.Count);
        Assert.Equal(["EU FSF", "OFAC SDN"], check.Added.Select(label => label.Origin.SourceName).Order());
        var journal = _harness.Case.ReadJournal().Last();
        Assert.Contains("OFAC SDN", journal.Summary, StringComparison.Ordinal);
        Assert.Contains("EU FSF", journal.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_newer_list_of_one_kind_never_shadows_the_other_kind()
    {
        var address = Wpkh("shadow");
        _harness.Sanctions.Import(new SdnFile("10/01/2026").Xbt("9001", "OFAC-NAMN", address.Value).Stream());
        _harness.Sanctions.Import(new EuFile().Plain("EU.5.1", "ANNAN", "RUS").Stream());

        var check = _harness.Labels.CheckSanctions(_harness.Case, [address]);

        Assert.Equal("OFAC SDN", Assert.Single(check.Added).Origin.SourceName);
    }

    [Fact]
    public void Addresses_on_both_chains_are_checked_in_one_call()
    {
        var bitcoin = Wpkh("both-chains");
        _harness.Sanctions.Import(new EuFile()
            .Identified("EU.6.1", "A", "RUS", bitcoin.Value)
            .Remarked("EU.6.2", "B", "RUS", "0x" + new string('d', 40))
            .Stream());

        var check = _harness.Labels.CheckSanctions(
            _harness.Case, [bitcoin.ToChainAddress(), Account.ToChainAddress(), Wpkh("clean").ToChainAddress()]);

        Assert.Equal(2, check.Added.Count);
    }

    [Fact]
    public void A_list_without_a_generation_date_is_refused()
    {
        var file = new EuFile().Plain("EU.7.1", "A", "RUS");

        var exception = Assert.Throws<SanctionListException>(() => _harness.Sanctions.Import(file.Stream(false)));

        Assert.Equal(SanctionListError.NoPublishDate, exception.Error);
    }

    [Fact]
    public void An_entity_with_too_many_parts_is_refused_rather_than_read_whole()
    {
        var file = new EuFile().Many("EU.8.1", 20_001);

        var exception = Assert.Throws<SanctionListException>(() => _harness.Sanctions.Import(file.Stream()));

        Assert.Equal(SanctionListError.TooLarge, exception.Error);
    }

    [Fact]
    public void An_sdn_entry_with_too_many_ids_is_refused_rather_than_read_whole()
    {
        var ids = Enumerable.Range(0, 20_001).Select(index => ("Passport", "P" + index)).ToArray();
        var file = new SdnFile().Entry("9001", "MÅNGA", "CYBER2", ids);

        var exception = Assert.Throws<SanctionListException>(() => _harness.Sanctions.Import(file.Stream()));

        Assert.Equal(SanctionListError.TooLarge, exception.Error);
    }

    [Fact]
    public void A_removed_list_is_no_longer_current_and_its_files_are_gone()
    {
        var future = _harness.Sanctions.Import(new SdnFile("11/07/2026").Xbt("1", "A", Wpkh("x").Value).Stream());
        var older = _harness.Sanctions.Import(new SdnFile("10/01/2026").Xbt("1", "A", Wpkh("y").Value).Stream());

        Assert.True(_harness.Sanctions.Remove(future.Sha256));

        Assert.Equal(older.Sha256, _harness.Sanctions.Current?.Sha256);
        Assert.DoesNotContain(
            Directory.GetFiles(Path.Combine(_harness.Directory, "Sanktionslistor")),
            path => path.Contains(future.Sha256, StringComparison.Ordinal));
        Assert.False(_harness.Sanctions.Remove(future.Sha256));
        Assert.False(_harness.Sanctions.Remove("../" + older.Sha256));
    }

    [Fact]
    public void A_list_whose_description_is_broken_is_repaired_from_its_file_and_not_dropped()
    {
        var info = _harness.Sanctions.Import(new SdnFile().Xbt("1", "A", Wpkh("repair").Value).Stream());
        var description = Directory.GetFiles(Path.Combine(_harness.Directory, "Sanktionslistor"), "*.json").Single();
        File.WriteAllText(description, "{trasig");

        using var restarted = new Harness(_harness.Directory);

        var listed = Assert.Single(restarted.Sanctions.List());
        Assert.Equal(info.Sha256, listed.Sha256);
        Assert.Equal(info.PublishDate, listed.PublishDate);
        Assert.Equal(info.AddressesByCurrency, listed.AddressesByCurrency);
    }

    [Fact]
    public void A_list_added_outside_chainsaw_is_noticed_without_a_restart()
    {
        Assert.Empty(_harness.Sanctions.List());
        using var other = new Harness(_harness.Directory);
        other.Sanctions.Import(new SdnFile().Xbt("1", "A", Wpkh("outside").Value).Stream());

        Assert.Single(_harness.Sanctions.List());
    }
}
