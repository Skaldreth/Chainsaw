using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Requests.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Requests.Tests;

public sealed class RequestsModuleTests
{
    [Fact]
    public void AddRequestsModule_returns_the_same_container()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddRequestsModule());
    }

    [Fact]
    public void AddRequestsModule_refuses_a_missing_container()
    {
        Assert.Throws<ArgumentNullException>(() => RequestsModule.AddRequestsModule(null!));
    }

    [Fact]
    public void AddRequestsModule_registers_the_service_and_the_table()
    {
        using var harness = new Harness();

        Assert.IsType<RequestService>(harness.Service);
        Assert.Empty(harness.Service.ListExports(harness.Case));
    }
}

public sealed class TemplateTests
{
    private static readonly RequestContext Context =
        new(Harness.CaseNumber, new("Testa Testsson", "Utredare", "Enheten"), new(2026, 11, 7, 13, 20, 0, TimeSpan.Zero));

    [Fact]
    public void A_request_for_information_holds_what_the_exchange_needs()
    {
        var document = RequestTemplates.Compose(Samples.Information(trail: Samples.Trail()), Context);

        Assert.Equal("Begäran om uppgifter · Börs A", document.Title);
        Assert.Contains("# Begäran om uppgifter · Börs A\n", document.Markdown, StringComparison.Ordinal);
        Assert.Contains(
            "Ärende 2026-0117 · upprättad 2026-11-07 14:20 (2026-11-07 13:20 UTC) av Testa Testsson, Utredare, Enheten",
            document.Markdown,
            StringComparison.Ordinal);
        Assert.Contains("## Insättningar · 2", document.Markdown, StringComparison.Ordinal);
        Assert.Contains(Samples.Deposit.Value, document.Markdown, StringComparison.Ordinal);
        Assert.Contains("`" + new string('9', 64) + "`", document.Markdown, StringComparison.Ordinal);
        Assert.Contains(new string('b', 64), document.Markdown, StringComparison.Ordinal);
        Assert.Contains("921140", document.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Times_are_written_local_with_their_offset_and_in_UTC()
    {
        var document = RequestTemplates.Compose(Samples.Information(), Context);
        var table = document.Blocks.Last(block => block.Kind == RequestBlockKind.Table);

        Assert.Equal("2026-10-28 21:14:05 +01:00", table.Rows[0][0]);
        Assert.Equal("2026-10-28 20:14:05", table.Rows[0][1]);
    }

    [Fact]
    public void Deposits_stand_in_time_order_and_unconfirmed_last()
    {
        var draft = Samples.Information(deposits:
        [
            Samples.Bitcoin("c3", 1_000, 30) with { Time = null, BlockHeight = null, BlockHash = null },
            Samples.Bitcoin("c2", 1_000, 30),
            Samples.Bitcoin("c1", 1_000, 28),
        ]);

        var table = RequestTemplates.Compose(draft, Context).Blocks.Last(block => block.Kind == RequestBlockKind.Table);

        Assert.Equal(["c1", "c2", "c3"], table.Rows.Select(row => row[4]));
        Assert.Equal("obekräftad", table.Rows[2][0]);
        Assert.Equal("obekräftad", table.Rows[2][5]);
    }

    [Fact]
    public void A_hash_the_case_lacks_is_said_honestly()
    {
        var draft = Samples.Information(deposits: [Samples.Bitcoin("c1", 1_000, 28) with { BlockHash = null }]);

        var table = RequestTemplates.Compose(draft, Context).Blocks.Last(block => block.Kind == RequestBlockKind.Table);

        Assert.Equal("saknas i ärendet", table.Rows[0][6]);
    }

    [Theory]
    [InlineData(60_000_000, "0,60 BTC")]
    [InlineData(21_500_000, "0,215 BTC")]
    [InlineData(1, "0,00000001 BTC")]
    [InlineData(300_000_000, "3,00 BTC")]
    public void Amounts_are_exact_with_the_language_s_separator(long sats, string expected)
    {
        Assert.Equal(
            expected, RequestTemplates.Amount(new AssetAmount(Asset.Bitcoin, sats), RequestTexts.Swedish));
    }

    [Fact]
    public void English_writes_its_own_words_and_a_point()
    {
        var document = RequestTemplates.Compose(Samples.Information(RequestLanguage.English), Context);

        Assert.Equal("Request for information · Börs A", document.Title);
        Assert.Contains("0.60 BTC", document.Markdown, StringComparison.Ordinal);
        Assert.Contains("## What we ask for", document.Markdown, StringComparison.Ordinal);
        Assert.Equal("2026-0117 Request Börs A 2026-11-07.md", document.FileName);
        Assert.DoesNotContain("Begäran", document.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void The_trail_is_one_sentence_with_hops_and_method_and_only_when_asked_for()
    {
        var with = RequestTemplates.Compose(Samples.Information(trail: Samples.Trail()), Context);
        var without = RequestTemplates.Compose(Samples.Information(), Context);

        Assert.Contains(
            "bc1q…a8f3 → bc1q…4tz9 → bc1q…x0wl → Börs A: 3 hopp, FIFO v1. Det spårade beloppet är en bedömning",
            with.Markdown,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Spåret dit", without.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_but_the_draft_reaches_the_file()
    {
        // The template has no way to the case's title or notes; only the case number and the operator are added.
        var document = RequestTemplates.Compose(Samples.Information(), Context);

        Assert.DoesNotContain("Offer", document.Markdown, StringComparison.Ordinal);
        Assert.Equal(
            ["Begäran om uppgifter · Börs A", "Det vi ber om", "Insättningsadress", "Insättningar · 2"],
            document.Blocks.Where(block => block.Kind is RequestBlockKind.Title or RequestBlockKind.Heading)
                .Select(block => block.Text));
    }

    [Fact]
    public void Without_a_profile_the_request_names_no_operator()
    {
        var document = RequestTemplates.Compose(Samples.Information(), Context with { Operator = null });

        Assert.Contains("(2026-11-07 13:20 UTC)\n", document.Markdown.Replace("*", string.Empty, StringComparison.Ordinal),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_request_for_information_needs_a_deposit()
    {
        var draft = Samples.Information(deposits: []);

        Assert.Throws<ArgumentException>(() => RequestTemplates.Compose(draft, Context));
    }

    [Fact]
    public void A_freeze_is_addressed_to_the_issuer_with_the_token_and_what_is_left()
    {
        var left = AssetAmount.Parse(Samples.Usdt, "8400");
        var draft = new RequestDraft(
            RequestKind.Freeze,
            RequestLanguage.Swedish,
            new RequestTarget("Tether", RequestTargetKind.TokenIssuer, Chain.Ethereum, [Samples.Holder]),
            [],
            null,
            Samples.Zone) { Holding = new RequestHolding("Tether", left) };

        var document = RequestTemplates.Compose(draft, Context);

        Assert.Equal("Begäran om frysning · Tether (USDT)", document.Title);
        Assert.Contains("8400,00 USDT", document.Markdown, StringComparison.Ordinal);
        Assert.Contains("USDT (0x2222222222222222222222222222222222222222)", document.Markdown, StringComparison.Ordinal);
        Assert.Contains("inte ett saldo från utgivaren", document.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Insättningar", document.Markdown, StringComparison.Ordinal);
        Assert.Equal("2026-0117 Begäran om frysning Tether 2026-11-07.md", document.FileName);
    }

    [Fact]
    public void A_freeze_needs_its_holding()
    {
        var draft = Samples.Information(kind: RequestKind.Freeze);

        Assert.Throws<ArgumentException>(() => RequestTemplates.Compose(draft, Context));
    }

    [Fact]
    public void The_file_name_drops_what_Windows_refuses()
    {
        var document = RequestTemplates.Compose(Samples.Information(name: "Börs: A/B *ny*?"), Context);

        Assert.Equal("2026-0117 Begäran Börs A B ny 2026-11-07.md", document.FileName);
    }

    [Fact]
    public void A_name_from_a_label_cannot_add_markup()
    {
        var document = RequestTemplates.Compose(Samples.Information(name: "# *Börs* [x](y) | z"), Context);

        Assert.Contains(
            "# Begäran om uppgifter · \\# \\*Börs\\* \\[x\\](y) \\| z\n", document.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_row_must_match_its_columns()
    {
        Assert.Throws<ArgumentException>(() => RequestBlock.Table(["a", "b"], [["only"]]));
    }

    [Fact]
    public void Markdown_tables_have_a_rule_and_code_for_mono_cells()
    {
        var text = RequestMarkdown.Write([RequestBlock.Table(["Adress", "Kedja"], [["abc", "Bitcoin"]], 0)]);

        Assert.Equal("| Adress | Kedja |\n| --- | --- |\n| `abc` | Bitcoin |\n", text);
    }
}

public sealed class DraftTests
{
    [Fact]
    public void A_target_needs_a_name_and_an_address()
    {
        Assert.Throws<ArgumentException>(() => new RequestTarget(" ", RequestTargetKind.Exchange, Chain.Bitcoin,
            [Samples.Deposit]));
        Assert.Throws<ArgumentException>(() => new RequestTarget("Börs", RequestTargetKind.Exchange, Chain.Bitcoin, []));
    }

    [Fact]
    public void A_target_s_key_is_its_first_address()
    {
        Assert.Equal(RequestTarget.KeyOf(Samples.Deposit), Samples.Exchange().Key);
    }

    [Fact]
    public void A_trail_has_a_start_an_end_and_a_method()
    {
        Assert.Throws<ArgumentException>(() => new RequestTrail(["only"], "FIFO v1"));
        Assert.Throws<ArgumentException>(() => new RequestTrail(["a", "b"], " "));
        Assert.Equal(1, new RequestTrail(["a", "b"], "FIFO v1").Hops);
    }
}

public sealed class ExportTests
{
    [Fact]
    public void An_export_writes_the_file_seals_it_and_journals_its_hash()
    {
        using var harness = new Harness();
        var path = Path.Combine(harness.Exports, "begäran.md");

        var export = harness.Service.Export(harness.Case, Samples.Information(), path);

        var bytes = File.ReadAllBytes(path);
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Assert.Equal(sha, export.Sha256);
        Assert.Equal("begäran.md", export.FileName);
        Assert.Equal(Samples.Exchange().Key, export.TargetKey);
        Assert.Equal([new string('9', 64), new string('a', 64)], export.Transactions);
        var entry = Assert.Single(harness.Journal(RequestJournalKinds.Exported));
        Assert.Contains(sha, entry.Summary, StringComparison.Ordinal);
        Assert.Equal(entry.Sequence, export.JournalSequence);
        using var details = JsonDocument.Parse(entry.Details!);
        Assert.Equal(sha, details.RootElement.GetProperty("sha256").GetString());
        Assert.True(harness.Verify().IsIntact);
    }

    [Fact]
    public void The_file_is_UTF8_without_a_mark_and_says_what_the_preview_says()
    {
        using var harness = new Harness();
        var draft = Samples.Information();
        var preview = harness.Service.Compose(harness.Case, draft);
        var path = Path.Combine(harness.Exports, preview.FileName);

        harness.Service.Export(harness.Case, draft, path);

        var bytes = File.ReadAllBytes(path);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal(preview.Markdown, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void An_export_never_writes_over_a_file()
    {
        using var harness = new Harness();
        var path = Path.Combine(harness.Exports, "begäran.md");
        File.WriteAllText(path, "någon annans fil");

        var first = harness.Service.Export(harness.Case, Samples.Information(), path);
        var second = harness.Service.Export(harness.Case, Samples.Information(), path);

        Assert.Equal("någon annans fil", File.ReadAllText(path));
        Assert.Equal("begäran (2).md", first.FileName);
        Assert.Equal("begäran (3).md", second.FileName);
        Assert.Equal(2, harness.Service.ListExports(harness.Case).Count);
    }

    [Fact]
    public void Exports_are_listed_after_the_case_is_opened_again()
    {
        using var harness = new Harness();
        var export = harness.Service.Export(
            harness.Case, Samples.Information(RequestLanguage.English), Path.Combine(harness.Exports, "a.md"));

        harness.Reopen();

        var listed = Assert.Single(harness.Service.ListExports(harness.Case));
        Assert.Equal(export with { Transactions = listed.Transactions }, listed);
        Assert.Equal(export.Transactions, listed.Transactions);
        Assert.Equal(RequestLanguage.English, listed.Language);
        Assert.Equal(export.Utc.UtcTicks, listed.Utc.UtcTicks);
    }

    [Fact]
    public void A_failed_seal_removes_the_file_again()
    {
        using var harness = new Harness();
        harness.Profiles.Profile = null;
        var path = Path.Combine(harness.Exports, "begäran.md");

        Assert.Throws<CaseException>(() => harness.Service.Export(harness.Case, Samples.Information(), path));

        Assert.False(File.Exists(path));
        Assert.Empty(harness.Service.ListExports(harness.Case));
    }

    [Fact]
    public void A_locked_vault_gives_a_preview_without_the_operator()
    {
        using var harness = new Harness();
        harness.Profiles.IsLocked = true;

        var document = harness.Service.Compose(harness.Case, Samples.Information());

        Assert.DoesNotContain("Testa Testsson", document.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void An_export_takes_the_time_it_is_made()
    {
        using var harness = new Harness();
        harness.Clock.UtcNow = new DateTimeOffset(2026, 12, 24, 8, 0, 0, TimeSpan.Zero);

        harness.Service.Export(harness.Case, Samples.Information(), Path.Combine(harness.Exports, "a.md"));

        Assert.Contains(
            "2026-12-24 08:00 UTC", File.ReadAllText(Path.Combine(harness.Exports, "a.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_path_without_a_file_is_refused()
    {
        using var harness = new Harness();

        Assert.Throws<ArgumentException>(() => harness.Service.Export(harness.Case, Samples.Information(), " "));
    }

    [Fact]
    public void No_contact_is_known_without_a_source()
    {
        using var harness = new Harness();

        Assert.Empty(harness.Service.ContactsFor("Börs A"));
        Assert.Empty(ExchangeContacts.Catalogue);
    }

    [Fact]
    public void Contacts_are_found_by_name_ignoring_case()
    {
        var contact = new ExchangeContact("Börs A", "Portal", new Uri("https://example.invalid/le"), new DateOnly(2026, 11, 8));
        var contacts = new ExchangeContacts([contact]);

        Assert.Same(contact, Assert.Single(contacts.For(" börs a ")));
        Assert.Empty(contacts.For("Börs B"));
        Assert.Empty(contacts.For(" "));
    }
}
