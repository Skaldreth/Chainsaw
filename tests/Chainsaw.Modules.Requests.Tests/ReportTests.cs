using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chainsaw.Modules.Requests.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Requests.Tests;

/// <summary>Synthetic reports; no real address, transaction or exchange.</summary>
internal static class ReportSamples
{
    public static readonly string TxA = new('a', 64);

    public static readonly string TxB = new('c', 64);

    public static readonly string Hash = new('b', 64);

    public static ReportStep Step(string from, string to, string tx, long sats, int day, long? height = 921_140) =>
        new(from, to, tx, new AssetAmount(Asset.Bitcoin, sats), new DateTimeOffset(2026, 10, day, 20, 14, 5,
            TimeSpan.Zero), height, height is null ? null : Hash);

    public static ReportTrail Trail(ReportTrailEnd end = ReportTrailEnd.CashOut, string to = "Börs A") =>
        new("bc1q…a8f3", to, end,
            [Step("bc1q…a8f3", "bc1q…x0wl", TxA, 55_000_000, 14), Step("bc1q…x0wl", to, TxB, 25_000_000, 16)],
            new AssetAmount(Asset.Bitcoin, 25_000_000))
        {
            Value = "≈ 263 100 kr",
        };

    public static IReadOnlySet<ReportPart> All { get; } = Enum.GetValues<ReportPart>().ToHashSet();

    public static ReportDraft Draft(IReadOnlySet<ReportPart>? parts = null, IReadOnlyList<ReportTrail>? trails = null) =>
        new(
            trails ?? [Trail()],
            [new ReportConclusion("Klustret bc1q…x0wl är en plånbok med 14 adresser.", "common-input v1 · tx aaaa…aaaa")],
            [new ReportLabel("Börs · Börs A", "egen etikett, 2026-11-06")],
            "FIFO v1",
            parts ?? All,
            Samples.Zone);
}

public sealed class ReportTemplateTests
{
    private static readonly RequestContext Context =
        new(Harness.CaseNumber, new("Testa Testsson", "Utredare", "Enheten"), new(2026, 11, 8, 13, 40, 0, TimeSpan.Zero));

    private static string Compose(ReportDraft draft) => ReportTemplate.Compose(draft, Context).Markdown;

    [Fact]
    public void A_report_has_the_case_number_and_who_made_it_never_the_title()
    {
        var document = ReportTemplate.Compose(ReportSamples.Draft(), Context);

        Assert.Equal("Underrättelse · ärende 2026-0117", document.Title);
        Assert.Contains(
            "Upprättad 2026-11-08 14:40 (2026-11-08 13:40 UTC) av Testa Testsson, Utredare, Enheten",
            document.Markdown,
            StringComparison.Ordinal);
        Assert.Equal("2026-0117 underrättelse 2026-11-08.md", document.FileName);
    }

    [Fact]
    public void Each_step_has_time_utc_amount_txid_height_and_hash()
    {
        var markdown = Compose(ReportSamples.Draft());

        Assert.Contains("## Spåret bc1q…a8f3 → Börs A · Uttag funnet", markdown, StringComparison.Ordinal);
        Assert.Contains("| Steg | Tid (lokal) | UTC | Belopp | Tx | Block | Blockhash |", markdown, StringComparison.Ordinal);
        Assert.Contains("2026-10-16 21:14:05 +01:00", markdown, StringComparison.Ordinal);
        Assert.Contains("2026-10-16 20:14:05", markdown, StringComparison.Ordinal);
        Assert.Contains("0,25 BTC", markdown, StringComparison.Ordinal);
        Assert.Contains("`" + ReportSamples.TxB + "`", markdown, StringComparison.Ordinal);
        Assert.Contains("921140", markdown, StringComparison.Ordinal);
        Assert.Contains("`" + ReportSamples.Hash + "`", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_the_chain_part_the_height_and_hash_are_left_out()
    {
        var parts = ReportSamples.All.Where(part => part != ReportPart.Chain).ToHashSet();

        var markdown = Compose(ReportSamples.Draft(parts));

        Assert.Contains("| Steg | Tid (lokal) | UTC | Belopp | Tx |\n", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(ReportSamples.Hash, markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void The_traced_amount_is_said_to_be_a_judgement_and_its_value_needs_the_sek_part()
    {
        var withSek = Compose(ReportSamples.Draft());
        var withoutSek = Compose(ReportSamples.Draft(ReportSamples.All.Where(p => p != ReportPart.Sek).ToHashSet()));

        Assert.Contains(
            "Spårat: 0,25 BTC · ≈ 263 100 kr till kursen vid tx-tiden · FIFO v1, en bedömning",
            withSek,
            StringComparison.Ordinal);
        Assert.Contains("Spårat: 0,25 BTC · FIFO v1, en bedömning", withoutSek, StringComparison.Ordinal);
        Assert.DoesNotContain("263 100", withoutSek, StringComparison.Ordinal);
    }

    [Fact]
    public void A_conclusion_is_followed_by_why()
    {
        var markdown = Compose(ReportSamples.Draft());

        Assert.Contains(
            "- Klustret bc1q…x0wl är en plånbok med 14 adresser.\n\n  > Varför: common\\-input v1 · tx aaaa…aaaa",
            markdown.Replace("common-input", "common\\-input", StringComparison.Ordinal),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_cash_outs_say_when_and_how_much_and_a_sanctioned_end_says_so()
    {
        var markdown = Compose(ReportSamples.Draft(trails:
            [ReportSamples.Trail(), ReportSamples.Trail(ReportTrailEnd.Sanctioned, "Växlare Z")]));

        Assert.Contains(
            "- Börs A: insättning 2026-10-16 21:14:05 +01:00, 0,25 BTC spårat · underlag för en begäran",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains("- Växlare Z: insättning", markdown, StringComparison.Ordinal);
        Assert.Contains("spårat · sanktionerad", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_risk_end_is_no_cash_out()
    {
        var markdown = Compose(ReportSamples.Draft(trails: [ReportSamples.Trail(ReportTrailEnd.Risk, "Mixer X")]));

        Assert.Contains("· Spåret tar slut", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("## Uttag", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Parts_switched_off_are_left_out()
    {
        var markdown = Compose(ReportSamples.Draft(new HashSet<ReportPart> { ReportPart.Summary }));

        Assert.Contains("## Sammanfattning", markdown, StringComparison.Ordinal);
        Assert.Contains("Spårade pengar har följts med FIFO v1 i 1 spår: 0,25 BTC till Börs A.", markdown,
            StringComparison.Ordinal);
        Assert.DoesNotContain("## Spåret", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("## Slutsatser", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("## Etiketter", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("## Kontroll", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("## Uttag", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_trails_the_summary_says_so_honestly()
    {
        var markdown = Compose(ReportSamples.Draft(trails: []));

        Assert.Contains("Inga spår mellan de markerade aktörerna.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ledger_writes_the_top_hash_or_that_it_is_checked_at_export()
    {
        var unchecked_ = Compose(ReportSamples.Draft());
        var ledger = new ReportLedger(42, new string('e', 64), new(2026, 11, 8, 13, 36, 0, TimeSpan.Zero), true);
        var broken = ledger with { IsIntact = false };

        var checkedText = Compose(ReportSamples.Draft() with { Ledger = ledger });

        Assert.Contains("Hashkedjan kontrolleras när rapporten exporteras.", unchecked_, StringComparison.Ordinal);
        Assert.Contains(
            "Hashkedjans topphash vid journalrad 42, kontrollerad 2026-11-08 14:36 (2026-11-08 13:36 UTC): "
            + "inga ändringar.",
            checkedText,
            StringComparison.Ordinal);
        Assert.Contains("`" + new string('e', 64) + "`", checkedText, StringComparison.Ordinal);
        Assert.Contains(
            "kontrollen fann ändringar", Compose(ReportSamples.Draft() with { Ledger = broken }),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_from_a_label_cannot_become_markup()
    {
        var trail = ReportSamples.Trail(to: "Börs *A* | # x");

        var markdown = Compose(ReportSamples.Draft(trails: [trail]));

        Assert.Contains("Börs \\*A\\* \\| \\# x", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void The_closing_line_says_what_is_fact_and_what_is_judgement()
    {
        Assert.Contains("Kedjan är fakta;", Compose(ReportSamples.Draft()), StringComparison.Ordinal);
    }
}

public sealed class ReportServiceTests
{
    [Fact]
    public void The_module_registers_the_report_service_and_its_table()
    {
        using var harness = new Harness();

        Assert.IsType<ReportService>(harness.Reports);
        Assert.Empty(harness.Reports.ListExports(harness.Case));
    }

    [Fact]
    public void Check_gives_the_journals_top_hash()
    {
        using var harness = new Harness();

        var ledger = harness.Reports.Check(harness.Case);

        var journal = harness.Verify().Chains.Single(chain => chain.Chain == ReportService.JournalChain);
        Assert.True(ledger.IsIntact);
        Assert.Equal(journal.HeadSequence, ledger.Line);
        Assert.Equal(journal.HeadHash, ledger.Hash);
        Assert.Equal(harness.Clock.UtcNow, ledger.CheckedUtc);
    }

    [Fact]
    public void Export_writes_the_file_seals_it_and_journals_its_hash()
    {
        using var harness = new Harness();
        var path = Path.Combine(harness.Exports, "rapport.md");

        var export = harness.Reports.Export(harness.Case, ReportSamples.Draft(), path);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), export.Sha256);
        Assert.Equal("rapport.md", export.FileName);
        Assert.Equal([ReportSamples.TxA, ReportSamples.TxB], export.Transactions);
        Assert.Equal(Enum.GetValues<ReportPart>(), export.Parts);
        var entry = Assert.Single(harness.Journal(RequestJournalKinds.ReportExported));
        Assert.Equal(export.JournalSequence, entry.Sequence);
        Assert.Contains(export.Sha256, entry.Summary, StringComparison.Ordinal);
        using var details = JsonDocument.Parse(entry.Details!);
        Assert.Equal(export.Sha256, details.RootElement.GetProperty("sha256").GetString());
        Assert.True(harness.Verify().IsIntact);
        var listed = Assert.Single(harness.Reports.ListExports(harness.Case));
        Assert.Equal((export.Sequence, export.Sha256, export.Path), (listed.Sequence, listed.Sha256, listed.Path));
        Assert.Equal(export.Transactions, listed.Transactions);
        Assert.Equal(export.Parts, listed.Parts);
    }

    [Fact]
    public void Export_writes_the_top_hash_as_it_stood_just_before()
    {
        using var harness = new Harness();
        var before = harness.Reports.Check(harness.Case);

        harness.Reports.Export(harness.Case, ReportSamples.Draft(), Path.Combine(harness.Exports, "r.md"));

        var text = File.ReadAllText(Path.Combine(harness.Exports, "r.md"), Encoding.UTF8);
        Assert.Contains("`" + before.Hash + "`", text, StringComparison.Ordinal);
        Assert.Contains("journalrad " + before.Line.ToString(System.Globalization.CultureInfo.InvariantCulture), text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Export_never_writes_over_a_file()
    {
        using var harness = new Harness();
        var path = Path.Combine(harness.Exports, "rapport.md");
        File.WriteAllText(path, "någon annans");

        var export = harness.Reports.Export(harness.Case, ReportSamples.Draft(), path);

        Assert.Equal("någon annans", File.ReadAllText(path));
        Assert.Equal("rapport (2).md", export.FileName);
    }

    [Fact]
    public void Export_keeps_the_preview_time()
    {
        using var harness = new Harness();
        var made = new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

        harness.Reports.Export(
            harness.Case, ReportSamples.Draft() with { MadeUtc = made }, Path.Combine(harness.Exports, "r.md"));

        Assert.Contains("Upprättad 2026-11-08 10:00 (2026-11-08 09:00 UTC)",
            File.ReadAllText(Path.Combine(harness.Exports, "r.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void Export_survives_a_reopening()
    {
        using var harness = new Harness();
        harness.Reports.Export(harness.Case, ReportSamples.Draft(), Path.Combine(harness.Exports, "r.md"));

        harness.Reopen();

        var export = Assert.Single(harness.Reports.ListExports(harness.Case));
        Assert.Equal("r.md", export.FileName);
        Assert.True(harness.Verify().IsIntact);
    }

    [Fact]
    public void A_failed_seal_removes_the_file()
    {
        using var harness = new Harness();
        harness.Profiles.Profile = null;
        var path = Path.Combine(harness.Exports, "r.md");

        Assert.ThrowsAny<Exception>(() => harness.Reports.Export(harness.Case, ReportSamples.Draft(), path));

        Assert.False(File.Exists(path));
        Assert.Empty(harness.Reports.ListExports(harness.Case));
    }

    [Fact]
    public void A_report_and_a_request_are_sealed_apart()
    {
        using var harness = new Harness();

        harness.Reports.Export(harness.Case, ReportSamples.Draft(), Path.Combine(harness.Exports, "r.md"));

        Assert.Empty(harness.Service.ListExports(harness.Case));
        Assert.Empty(harness.Journal(RequestJournalKinds.Exported));
    }
}
