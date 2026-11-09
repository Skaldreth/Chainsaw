using System.Text;
using System.Text.Json;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Attribution.Tests.Synthetic;

namespace Chainsaw.Modules.Attribution.Tests;

/// <summary>
/// Ransomwhere as an open source: read from a saved file in its JSON form (synthetic records, never the real data),
/// kept with its hash and licence, and each hit labelled as a reported address with the source as its origin.
/// </summary>
public sealed class OpenSourceTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void The_data_set_is_imported_with_its_licence_date_and_counts()
    {
        var bytes = Ransomwhere(
            Record(Wpkh("rw-1").Value, "Syntetisk familj", "2026-10-01T10:00:00.000Z", 2),
            Record(Pkh("rw-2").Value, "Annan", "2026-10-03T10:00:00.000Z", 1),
            Record("inte en adress", "Trasig", "2026-09-01T10:00:00.000Z", 0),
            Record(Wpkh("rw-3").Value, "Ethereum", "2026-09-01T10:00:00.000Z", 0, "ethereum"));

        var info = _harness.OpenSources.Import(new MemoryStream(bytes));

        Assert.Equal("Ransomwhere", info.Name);
        Assert.Contains("CC BY 4.0", info.License, StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2026, 10, 3), info.DataDate);
        Assert.Equal(2, info.AddressesByChain[nameof(Chain.Bitcoin)]);
        Assert.Equal(2, info.Skipped);
        Assert.Contains(info.Sha256, info.Version, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hit_is_labelled_reported_with_the_source_as_its_origin_once()
    {
        var address = Wpkh("rw-hit");
        var info = _harness.OpenSources.Import(new MemoryStream(Ransomwhere(
            Record(address.Value, "Syntetisk familj", "2026-10-01T10:00:00.000Z", 3))));

        var check = _harness.Labels.CheckOpenSources(
            _harness.Case, [address.ToChainAddress(), Wpkh("clean").ToChainAddress()]);
        var again = _harness.Labels.CheckOpenSources(_harness.Case, [address.ToChainAddress()]);

        var label = Assert.Single(check.Added);
        Assert.Equal(LabelKind.Reported, label.Kind);
        Assert.Equal("Ransomware: Syntetisk familj", label.Name);
        Assert.Equal(LabelSource.OpenSource, label.Origin.Source);
        Assert.Equal("Ransomwhere", label.Origin.SourceName);
        Assert.Equal(info.Version, label.Origin.SourceVersion);
        Assert.Contains("3 betalningar", label.Origin.Reason, StringComparison.Ordinal);
        Assert.Contains("CC BY 4.0", label.Origin.Reason, StringComparison.Ordinal);
        Assert.Contains("bedömning", label.Origin.Reason, StringComparison.Ordinal);
        Assert.Empty(again.Added);
        Assert.Equal(1, again.AlreadyLabelled);
        Assert.Equal(AttributionJournalKinds.OpenSourcesMatched, _harness.Case.ReadJournal().Last().Kind);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_changed_file_is_passed_over_and_named_and_the_labels_still_come_from_the_sanction_lists()
    {
        var address = Wpkh("rw-tamper");
        var info = _harness.OpenSources.Import(new MemoryStream(Ransomwhere(
            Record(address.Value, "A", "2026-10-01T10:00:00.000Z", 1))));
        _harness.Sanctions.Import(new SdnFile().Xbt("9001", "SYNTETISK", address.Value).Stream());
        var stored = Directory.GetFiles(Path.Combine(_harness.Directory, "Öppna källor"), "*.data").Single();
        File.WriteAllText(stored, "[]");

        var open = _harness.Labels.CheckOpenSources(_harness.Case, [address.ToChainAddress()]);
        var sanctions = _harness.Labels.CheckSanctions(_harness.Case, [address.ToChainAddress()]);

        Assert.Empty(open.Added);
        Assert.Equal(info.Sha256, Assert.Single(open.Unusable).Sha256);
        Assert.Single(sanctions.Added);
        Assert.True(_harness.OpenSources.Remove(info.Sha256));
        Assert.Empty(_harness.OpenSources.List());
    }

    [Fact]
    public void A_second_check_with_nothing_new_opens_no_write()
    {
        var address = Wpkh("rw-once");
        _harness.OpenSources.Import(new MemoryStream(Ransomwhere(
            Record(address.Value, "A", "2026-10-01T10:00:00.000Z", 1))));
        var (counted, counter) = WriteCountingCase.Wrap(_harness.Case);
        _harness.Labels.CheckOpenSources(counted, [address.ToChainAddress()]);
        var writes = counter.Writes;

        var again = _harness.Labels.CheckOpenSources(counted, [address.ToChainAddress()]);

        Assert.Equal(1, writes);
        Assert.Equal(writes, counter.Writes);
        Assert.Equal(1, again.AlreadyLabelled);
    }

    [Fact]
    public void A_file_with_a_byte_order_mark_is_recognised()
    {
        var bytes = Ransomwhere(Record(Wpkh("rw-bom").Value, "A", "2026-10-01T10:00:00.000Z", 1));

        var info = _harness.OpenSources.Import(new MemoryStream([0xEF, 0xBB, 0xBF, .. bytes]));

        Assert.Equal("Ransomwhere", info.Name);
    }

    [Fact]
    public void A_data_set_dated_after_today_is_refused()
    {
        var bytes = Ransomwhere(Record(Wpkh("rw-future").Value, "A", "2027-01-01T10:00:00.000Z", 1));

        var exception = Assert.Throws<OpenSourceListException>(
            () => _harness.OpenSources.Import(new MemoryStream(bytes)));

        Assert.Equal(OpenSourceListError.FutureDate, exception.Error);
    }

    [Fact]
    public void A_broken_description_is_repaired_from_the_file()
    {
        var info = _harness.OpenSources.Import(new MemoryStream(Ransomwhere(
            Record(Wpkh("rw-repair").Value, "A", "2026-10-01T10:00:00.000Z", 1))));
        var description = Directory.GetFiles(Path.Combine(_harness.Directory, "Öppna källor"), "*.json").Single();
        File.WriteAllText(description, "{trasig");
        using var restarted = new Harness(_harness.Directory);

        var listed = Assert.Single(restarted.OpenSources.List());

        Assert.Equal(info.Sha256, listed.Sha256);
        Assert.Equal(info.DataDate, listed.DataDate);
    }

    [Fact]
    public void A_file_that_is_not_a_known_source_is_refused()
    {
        var exception = Assert.Throws<OpenSourceListException>(
            () => _harness.OpenSources.Import(new MemoryStream("""[{"name": "något annat"}]"""u8.ToArray())));

        Assert.Equal(OpenSourceListError.NotKnown, exception.Error);
    }

    [Fact]
    public void A_data_set_without_dates_is_refused()
    {
        var bytes = Ransomwhere(Record(Wpkh("rw-nodate").Value, "A", null, 1));

        var exception = Assert.Throws<OpenSourceListException>(
            () => _harness.OpenSources.Import(new MemoryStream(bytes)));

        Assert.Equal(OpenSourceListError.NoDate, exception.Error);
    }

    [Fact]
    public void Without_a_source_nothing_is_written()
    {
        var journal = _harness.Case.ReadJournal().Count;

        var check = _harness.Labels.CheckOpenSources(_harness.Case, [Wpkh("none").ToChainAddress()]);

        Assert.Empty(check.Lists);
        Assert.Empty(check.Added);
        Assert.Equal(journal, _harness.Case.ReadJournal().Count);
    }

    private static Dictionary<string, object?> Record(
        string address, string family, string? updated, int payments, string chain = "bitcoin") => new()
    {
        ["address"] = address,
        ["balance"] = 0,
        ["blockchain"] = chain,
        ["createdAt"] = updated,
        ["updatedAt"] = updated,
        ["family"] = family,
        ["transactions"] = Enumerable.Range(0, payments)
            .Select(index => new { hash = Txid($"{address}-{index}").Value, time = 1_700_000_000 + index })
            .ToArray(),
    };

    private static byte[] Ransomwhere(params Dictionary<string, object?>[] records) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(records));
}
